using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// What has been cast on the room you are standing in.
///
/// `UIRoomEnchantments.cpp`. The server sends an AddEnchantment with
/// BuffType.RoomBuff and the data layer files it in `Data.RoomBuffs`,
/// separately from `AvatarBuffs` - the same message type carries both
/// and the type byte is the only thing telling them apart. Nothing in
/// this client showed the room's, so a room under a shaal's or a
/// forest's blessing looked exactly like one that was not.
///
/// The game's version is a movable grid of icon buttons with the buff's
/// name as a tooltip, and a single click on one sends
/// `SendReqLookMessage(id)` - the same look an object gets, answered
/// with a description. Three things follow from that file:
///
///  - the icon is composed as a buff icon, not as an object: front
///    frame, hotspot 0, no Y offset, centred in a small square. That is
///    what <see cref="AvatarPanel"/> already does for your own
///    enchantments, and it is the same call here.
///  - removing one shuffles the rest down rather than leaving a hole.
///    The grid does that by swapping widgets; a row rebuilt from the
///    list in order gets there by itself.
///  - the name is a tooltip, which a finger cannot hover. Here the tap
///    that would show it asks the server for the description instead,
///    which is the other half of what the game's click does and the
///    half a phone can actually use.
///
/// It sits under the minimap rather than floating: there is no room on
/// a phone for a window you drag around, and the corner opposite your
/// own enchantments is where the eye already goes for things about the
/// room.
/// </summary>
public partial class RoomBuffsPanel : Control
{
    [Export] public int IconSize = 28;
    [Export] public float Margin = 12f;
    /// <summary>Leaves room for whatever owns the top-right corner.</summary>
    [Export] public float TopReserve = 0f;

    /// <summary>
    /// How many enchantments the panel will show at all.
    ///
    /// The game's grid is a fixed number of slots, built once:
    /// `UIRoomEnchantments.cpp:23` sizes it
    /// `UI_ROOMENCHANTMENTS_COLS * UI_ROOMENCHANTMENTS_ROWS`, and
    /// `Constants.h:874-875` makes that 14 by 1. There is no growth path -
    /// `BuffAdd` (`UIRoomEnchantments.cpp:123`) only touches a slot when
    /// `Grid->getChildCount() > Index`, so a fifteenth room enchantment is
    /// dropped on the floor by the reference client too. Fourteen is
    /// therefore the count to mirror, not a limit invented here.
    /// </summary>
    const int MaxSlots = 14 * 1;

    /// <summary>An enchantment was tapped: look at it.</summary>
    public event Action<uint> Look;

    readonly List<Button> _slots = new List<Button>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _signature = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Sync(ObjectBaseList<ObjectBase> buffs)
    {
        if (buffs == null) { Hide(0); return; }

        var sb = Sig.Start();
        // The resolution state of each entry belongs in the signature as
        // much as its id does. A room enchantment arrives as an
        // AddEnchantment long before its sprite does: the reference client
        // never waits for one, it hands the object to a composer and
        // repaints the slot later, from the composer's own
        // NewImageAvailable callback (`UIRoomEnchantments.cpp:42` subscribes
        // it, `:87-101` is the repaint). There is no equivalent callback
        // here - the icon is composed inline - so the panel's only chance
        // to pick up a late sprite is another rebuild.
        //
        // With an id-only signature there was no such chance. The loop
        // below skips an entry whose Resource has not resolved yet, the
        // signature it produced was identical once the resource landed,
        // Sync returned early for ever, and that enchantment stayed
        // invisible for as long as the room held it. Appending whether the
        // resource is there yet moves the signature exactly when a sprite
        // resolves, which is the moment a rebuild is needed.
        // <see cref="AvatarPanel.SyncBuffs"/> does the same for your own.
        foreach (ObjectBase b in buffs)
            sb.Opt(b?.ID).Append(b?.Resource != null ? "+" : "-").Append(';');
        if (!Sig.Changed(sb, ref _signature)) return;

        Vector2 v = GetViewportRect().Size;

        // The game's row is fourteen 16-pixel icons inside a window the
        // layout caps at 250x42 (`Meridian59.layout:1284`), so all fourteen
        // always fit across it and one row is all the grid ever needs.
        // A phone is narrower than a desktop window is wide and these icons
        // are IconSize, not sixteen, so fourteen of them do not fit across
        // a portrait screen. Wrapping onto further rows below is a
        // deliberate divergence: it keeps the reference's count of fourteen
        // visible - which a single clipped row would not - rather than
        // changing how many the panel holds.
        float step = IconSize + 12f;
        int perRow = Mathf.Clamp((int)((v.X - 2f * Margin) / step), 1, MaxSlots);

        int used = 0;
        foreach (ObjectBase b in buffs)
        {
            if (b?.Resource == null) continue;
            // Past the grid's capacity the reference simply does nothing
            // with the entry (`UIRoomEnchantments.cpp:123`); stopping here
            // is the same outcome without leaving stale slots behind.
            if (used >= MaxSlots) break;
            uint id = b.ID;

            Button slot = Take(used);
            slot.Icon = Icon(b);
            slot.TooltipText = b.Name;
            slot.Size = new Vector2(IconSize + 8f, IconSize + 8f);
            // Filled right to left from the right margin, so a row grows
            // towards the middle instead of off the screen, and downwards
            // once the row is full.
            int col = used % perRow;
            int row = used / perRow;
            slot.Position = new Vector2(
                v.X - Margin - (col + 1) * step,
                Margin + TopReserve + row * step);
            slot.Visible = slot.Icon != null;

            // Rebuilt rows are reused slots, so the old handler has to
            // go or a tap looks at whatever was in that position before.
            foreach (Godot.Collections.Dictionary c in slot.GetSignalConnectionList(BaseButton.SignalName.Pressed))
                slot.Disconnect(BaseButton.SignalName.Pressed, (Callable)c["callable"]);
            slot.Pressed += () => Look?.Invoke(id);

            used++;
        }
        Hide(used);
    }

    Button Take(int index)
    {
        while (_slots.Count <= index)
        {
            var b = new Button { Flat = true, Visible = false, IconAlignment = HorizontalAlignment.Center };
            AddChild(b);
            _slots.Add(b);
        }
        return _slots[index];
    }

    void Hide(int from)
    {
        for (int i = from; i < _slots.Count; i++) _slots[i].Visible = false;
    }

    ImageTexture Icon(ObjectBase o)
    {
        string key = $"{o.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[RoomBuffsPanel] {o.Name}: {e.Message}"); }
        // Only a picture is worth keeping. Caching the failure would undo
        // the signature fix above for the half-loaded case: the resource
        // exists, so the signature moves and a rebuild happens, but
        // `M59Compose.Icon` can still come back with nothing while the
        // bitmap behind it is unread - and a cached null would then be
        // answered for ever instead of being composed on the next rebuild.
        if (tex != null) _icons[key] = tex;
        return tex;
    }
}
