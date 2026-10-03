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
/// It sits under the minimap by default, in the corner opposite your
/// own enchantments, where the eye already goes for things about the
/// room. That is its NATURAL place only: the row is a HUD piece
/// ("roombuffs", "Room enchantments"), so the arrange screen moves,
/// scales, fades and hides it like the portrait's own row - Ashton:
/// "the room enchantments need to be a editable movable ui element".
/// The game's window is draggable too (`UIRoomEnchantments.cpp`), so
/// this is the reference's behaviour reached through the one editor
/// every piece here shares rather than a drag on the row itself.
///
/// Composing and placing are two jobs: <see cref="Sync"/> rebuilds the
/// icons when the LIST moves (or the piece's scale, which changes the
/// pixels), <see cref="Relay"/> puts them where the layout store says
/// whenever the layout moves. Nothing in the view reads this row's
/// bottom - the log and the enchantments on you stack in the
/// top-LEFT corner - so moving it disturbs nothing else.
/// </summary>
public partial class RoomBuffsPanel : Control
{
    [Export] public int IconSize = 28;
    [Export] public float Margin = 12f;
    /// <summary>
    /// Leaves room for whatever owns the top-right corner. Part of the
    /// natural rect only: once the player has dragged the row, their
    /// offset is measured from this same fixed base, so the base cannot
    /// follow the minimap around or the saved offset would lie.
    /// </summary>
    [Export] public float TopReserve = 0f;

    /// <summary>The piece's id in the layout store and hud.cfg.</summary>
    public const string PieceId = "roombuffs";

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
    /// <summary>How many slots the last rebuild filled; what Relay lays out.</summary>
    int _used;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // One cluster: the icons move and scale together. The node given
        // is this one, which M59Hud.Dress fades as a whole - every slot
        // is a child, so the fade is inherited.
        M59Hud.Register(PieceId, "Room enchantments", this);
        M59Hud.Changed += Relay;
        GetViewport().SizeChanged += Relay;
        Relay();
    }

    public override void _ExitTree() => M59Hud.Changed -= Relay;

    /// <summary>
    /// Everything the layout store says about this piece, as one value,
    /// compared on the per-frame entry point (Sync): a layout LOADED
    /// after _Ready, or an editor that changed a piece without raising
    /// Changed, leaves a piece drawn where it used to be and says
    /// nothing about it. The same rule every piece follows.
    /// </summary>
    M59Hud.Stamp _stamp;

    /// <summary>The player's size for this piece, inside the model's band.</summary>
    static float HudScale()
    {
        M59Hud.Piece p = M59Hud.Get(PieceId);
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>One slot's pitch at this scale: the icon plus its gap.</summary>
    static float Step(float sc, int icon) => (icon + 12f) * sc;

    /// <summary>
    /// How many fit across. The game's row is fourteen 16-pixel icons
    /// inside a window the layout caps at 250x42 (`Meridian59.layout:1284`),
    /// so all fourteen always fit and one row is all its grid needs.
    /// These icons are IconSize, not sixteen, so fourteen of them may
    /// not fit across a narrow screen; wrapping onto further rows is a
    /// deliberate divergence that keeps the reference's count of
    /// fourteen VISIBLE, which a clipped row would not.
    /// </summary>
    int PerRow(float sc, Vector2 v)
        => Mathf.Clamp((int)((v.X - 2f * Margin) / Step(sc, IconSize)), 1, MaxSlots);

    /// <summary>
    /// The rect the designer wants, before the player's offset: the
    /// filled slots as they wrap, hung from the top-right corner under
    /// whatever TopReserve clears. With nothing cast on the room there
    /// is no row; in the editor it is given one slot's worth so there is
    /// still a handle to pick it up by - a piece with a zero rect cannot
    /// be picked (HudEditor.Drawn), and a row that only exists while the
    /// room is enchanted is a row the player could never place.
    /// </summary>
    Rect2 Natural(float sc, int count, Vector2 v)
    {
        float step = Step(sc, IconSize);
        float slot = (IconSize + 8f) * sc;
        int cols = PerRow(sc, v);
        int n = count > 0 ? count : (M59Hud.Editing ? 1 : 0);
        int rows = (n + cols - 1) / cols;
        int wide = Mathf.Min(n, cols);
        var size = n > 0
            ? new Vector2((wide - 1) * step + slot, (rows - 1) * step + slot)
            : Vector2.Zero;
        return new Rect2(v.X - Margin - size.X, Margin + TopReserve, size);
    }

    /// <summary>
    /// Puts the slots at the piece's current place and size without
    /// recomposing any art. The layout store's Changed and the viewport
    /// call it; Sync calls it after a rebuild.
    /// </summary>
    void Relay()
    {
        if (!IsInsideTree()) return;
        Vector2 v = GetViewportRect().Size;
        float sc = HudScale();
        float step = Step(sc, IconSize);
        float slot = (IconSize + 8f) * sc;
        int cols = PerRow(sc, v);

        // The player's own hide, shown faint in the editor so it can be
        // found (Dress). The host stays Visible - the view sets that for
        // the whole interface - and the slots carry the hide.
        bool show = M59Hud.Shows(PieceId);
        M59Hud.Dress(PieceId);

        Rect2 at = M59Hud.Place(PieceId, Natural(sc, _used, v), v);
        int wide = Mathf.Max(1, Mathf.Min(_used, cols));
        for (int i = 0; i < _slots.Count; i++)
        {
            Button b = _slots[i];
            if (i >= _used) { b.Visible = false; continue; }
            // Filled right to left from the rect's right edge, as the
            // row always was: it grows towards the middle of the screen
            // instead of off it, and downwards once a row is full.
            int col = i % cols, row = i / cols;
            b.Position = new Vector2(at.Position.X + (wide - 1 - col) * step,
                                     at.Position.Y + row * step);
            b.Size = new Vector2(slot, slot);
            b.Visible = show && b.Icon != null;
        }
    }

    public void Sync(ObjectBaseList<ObjectBase> buffs)
    {
        // The stamp first, so a layout change with an unchanged list
        // still moves the row.
        M59Hud.Stamp stamp = M59Hud.StampOf(PieceId);
        if (stamp != _stamp) { _stamp = stamp; Relay(); }

        if (buffs == null) { if (_used != 0) { _used = 0; Hide(0); Relay(); } return; }

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
        // The icons are composed at the player's size for this piece, so
        // a bigger row is bigger pictures and not the same pictures
        // further apart; the pixel size is in the signature so a change
        // of size rebuilds.
        int px = Mathf.Max(8, Mathf.RoundToInt(IconSize * HudScale()));
        sb.Append(px).Append('|');
        foreach (ObjectBase b in buffs)
            sb.Opt(b?.ID).Append(b?.Resource != null ? "+" : "-").Append(';');
        // The third half of the rule the comment above quotes: a sprite
        // that exists but is not yet readable changes nothing in the
        // data, so no signature can see it - retry on a timer while any
        // compose failed (SpellsPanel.cs, AvatarPanel.cs do the same).
        // Without it a slot whose first compose missed stayed invisible
        // for as long as the room held the enchantment.
        bool retry = _missed && Time.GetTicksMsec() >= _retryAt;
        if (!Sig.Changed(sb, ref _signature) && !retry) return;
        _missed = false;

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
            slot.Icon = Icon(b, px);
            slot.TooltipText = b.Name;

            // Rebuilt rows are reused slots, so the old handler has to
            // go or a tap looks at whatever was in that position before.
            foreach (Godot.Collections.Dictionary c in slot.GetSignalConnectionList(BaseButton.SignalName.Pressed))
                slot.Disconnect(BaseButton.SignalName.Pressed, (Callable)c["callable"]);
            slot.Pressed += () => Look?.Invoke(id);

            used++;
        }
        Hide(used);
        _used = used;
        if (_missed) _retryAt = Time.GetTicksMsec() + 500;
        // Placed after the rebuild: the natural rect is the filled
        // slots, so it is only known now.
        Relay();
    }

    Button Take(int index)
    {
        while (_slots.Count <= index)
        {
            var b = new Button
            {
                Flat = true, Visible = false,
                IconAlignment = HorizontalAlignment.Center,
                Name = $"roombuff{_slots.Count}",
            };
            AddChild(b);
            _slots.Add(b);
        }
        return _slots[index];
    }

    void Hide(int from)
    {
        for (int i = from; i < _slots.Count; i++) _slots[i].Visible = false;
    }

    ImageTexture Icon(ObjectBase o, int px)
    {
        string key = $"{o.Resource.Filename}:{px}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, px)); }
        catch (Exception e) { GD.PrintErr($"[RoomBuffsPanel] {o.Name}: {e.Message}"); }
        // Only a picture is worth keeping. Caching the failure would undo
        // the signature fix above for the half-loaded case: the resource
        // exists, so the signature moves and a rebuild happens, but
        // `M59Compose.Icon` can still come back with nothing while the
        // bitmap behind it is unread - and a cached null would then be
        // answered for ever instead of being composed on the next rebuild.
        if (tex != null) _icons[key] = tex;
        else _missed = true;
        return tex;
    }

    /// <summary>A compose came back empty this rebuild; Sync retries at _retryAt.</summary>
    bool _missed;
    ulong _retryAt;
}
