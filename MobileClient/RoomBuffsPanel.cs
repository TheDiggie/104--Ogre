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

        var sb = new System.Text.StringBuilder();
        foreach (ObjectBase b in buffs) sb.Append(b?.ID).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        Vector2 v = GetViewportRect().Size;

        int used = 0;
        foreach (ObjectBase b in buffs)
        {
            if (b?.Resource == null) continue;
            uint id = b.ID;

            Button slot = Take(used);
            slot.Icon = Icon(b);
            slot.TooltipText = b.Name;
            slot.Size = new Vector2(IconSize + 8f, IconSize + 8f);
            // Filled right to left from the right margin, so the row
            // grows towards the middle instead of off the screen.
            slot.Position = new Vector2(
                v.X - Margin - (used + 1) * (IconSize + 12f),
                Margin + TopReserve);
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
        _icons[key] = tex;
        return tex;
    }
}
