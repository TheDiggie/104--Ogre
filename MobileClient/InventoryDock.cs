using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Your pack, on the glass: a small grid of what you are carrying,
/// drawn over the world beside the other HUD pieces, so an item can be
/// seen and used without opening the bag.
///
/// WHY. The player's words: "add an inventory window that I can use
/// to always see and interact with so I don't need to go through
/// menus to see my items. Make sure people can move, resize or close
/// it in the UI customizer." The full-screen bag (InventoryPanel)
/// stays as it is - it is the place for a hundred items and for
/// dragging them into order - and this is in addition to it, the same
/// way the Purse is in addition to the coin in the bag: the few things
/// you reach for in a fight, always in the same place.
///
/// DIVERGENCE from the reference, and a deliberate one. The game's
/// inventory is a window (`UIInventory.cpp`) that a desktop keeps open
/// beside the world; on a phone the bag covers the world, so a
/// desktop's "always open" has to become a HUD piece.
///
/// WHAT IT IS IN THE LAYOUT. A piece ("dock", "Inventory dock") like
/// the purse and the vitals: the arrange screen moves it, SCALES it -
/// which is the resize, the slot size follows Scale - fades it and
/// hides it, which is the close. And because a grid has a second
/// honest size, how many ACROSS is part of the piece too
/// (<see cref="M59Hud.Piece.Columns"/>): the same slots laid out wide
/// or tall, saved with the layout, backed out by Cancel. Default
/// visible, because the player asked for it to be there.
///
/// WHAT A TAP DOES. The bag's selection semantics, not a new set: a
/// tap picks the item and a strip of the bag's own actions appears
/// hung off the grid - Use / Look / Hotbar together and Drop at the
/// far end on its own, for the reason <see cref="M59Skin.FootLeft"/>
/// gives (it is the one control that destroys something). The same
/// slot again, or a tap on the world, puts it away. The picked item
/// becomes the target one double-tap window later, exactly as the
/// bag's does (<see cref="InventoryPanel.Targeted"/>), and the view
/// restores the world target afterwards - see the pair of lines in
/// GameView.Pump that save and restore <c>_targetBeforeBag</c>; the
/// dock's selection counts as the bag being open there.
///
/// WHAT IT NEVER DOES is walk you. The slots take taps and a drag
/// that starts on one is eaten by it, as a drag that starts on any
/// HUD button is (notes/harness.md, "A drag that starts on a button or
/// a panel is eaten by that control"); the ground between the slots is
/// MouseFilter.Ignore, so a finger that lands beside a slot is the
/// world's. Under a panel or the drawer it stays drawn under the
/// scrim, as the purse does, and the scrim takes the touches.
///
/// Rebuilt only when what it shows changes, by the same signature the
/// bag uses, because an icon is a composed texture and six of them a
/// frame would cost more than the room does.
/// </summary>
public partial class InventoryDock : Control
{
    public const string Id = "dock";

    /// <summary>
    /// A slot's side at scale 1. Bigger than a thumb (44) by a margin,
    /// because the icon inside keeps the game's 40-in-52 ratio and a
    /// 44-point slot leaves the art 34 points, which is a smudge. At
    /// MinScale it is 39, which is still a target - the player chose
    /// that size, and the editor shows them what they chose.
    /// </summary>
    [Export] public float SlotSize = 56f;
    [Export] public float Sep = 6f;
    [Export] public int FontSize = 14;
    /// <summary>Eight across by default: a thirty-item pack is four rows, which fits over the chat.</summary>
    [Export] public int Columns = 8;
    public const int MinCols = 2, MaxCols = 16;

    /// <summary>The gutter between this and the piece it hangs under.</summary>
    const float Gutter = 16f;

    /// <summary>The same four verbs as the bag, raised with the same objects.</summary>
    public event Action<InventoryObject> UseItem;
    public event Action<InventoryObject> DropItem;
    public event Action<InventoryObject> LookItem;
    public event Action<InventoryObject> BindItem;
    /// <summary>A tap has settled into a target - see InventoryPanel.Targeted.</summary>
    public event Action<InventoryObject> Targeted;

    /// <summary>What is picked, re-pointed at the live instance on every rebuild.</summary>
    public InventoryObject Selection { get; private set; }
    public bool HasSelection => Selection != null;

    Control _host;
    readonly List<InventorySlot> _slots = new List<InventorySlot>();
    Panel _strip;
    Label _name;
    Button _use, _look, _bind, _drop;

    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    IList<InventoryObject> _items;
    string _signature = "";
    string _stamp = "";
    bool _missed;
    ulong _retryAt;

    InventoryObject _arming;
    ulong _armedAt;
    const ulong DoubleTapMs = 250;   // UI_INTERVALINVENTORYCLICK, as the bag

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // One node to hand M59Hud so Dress can fade and hide the whole
        // grid at once; the strip is a sibling and follows by hand,
        // because a fade meant for the grid should not fade the verbs.
        _host = new Control { Name = "dockHost", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_host);

        _strip = M59Skin.Window();
        _strip.Name = "dockStrip";
        _strip.Visible = false;
        // The plate takes the touch so a press between the buttons does
        // not fall through to the world and clear the very selection
        // the strip is for.
        _strip.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_strip);

        _name = M59Skin.Caption("");
        _name.Name = "dockName";
        _name.Visible = false;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.ClipText = true;
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _name.AddThemeColorOverride("font_color", M59Skin.Text);
        AddChild(_name);

        _use  = Make("Use",    "dockUse",  M59Skin.Kind.Primary,   () => { if (Selection != null) UseItem?.Invoke(Selection); });
        _look = Make("Look",   "dockLook", M59Skin.Kind.Secondary, () => { if (Selection != null) LookItem?.Invoke(Selection); });
        _bind = Make("Hotbar", "dockBind", M59Skin.Kind.Secondary, () => { if (Selection != null) BindItem?.Invoke(Selection); });
        _drop = Make("Drop",   "dockDrop", M59Skin.Kind.Danger,    () => { if (Selection != null) DropItem?.Invoke(Selection); });

        M59Hud.Piece p = M59Hud.Register(Id, "Inventory dock", _host);
        if (p != null) { p.MinColumns = MinCols; p.MaxColumns = MaxCols; p.DefaultColumns = Columns; }
        M59Hud.Changed += Layout;
        GetViewport().SizeChanged += Layout;
    }

    public override void _ExitTree() => M59Hud.Changed -= Layout;

    Button Make(string text, string node, M59Skin.Kind kind, Action pressed)
    {
        var b = new Button { Text = text, Name = node, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    // ---- the model ------------------------------------------------

    static M59Hud.Piece Piece => M59Hud.Get(Id);

    static float HudScale()
    {
        M59Hud.Piece p = Piece;
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    int Across()
    {
        M59Hud.Piece p = Piece;
        return p == null ? Columns : p.ColumnsNow;
    }

    /// <summary>
    /// Everything the layout store says about the piece, as one value,
    /// compared every frame - a layout LOADED after _Ready leaves the
    /// grid where it was and says nothing (AvatarPanel.HudStamp).
    /// </summary>
    static string HudStamp()
    {
        M59Hud.Piece p = Piece;
        if (p == null) return "";
        return $"{p.Offset.X},{p.Offset.Y},{p.Scale},{p.Alpha},{(p.Hidden ? 1 : 0)},{p.Columns},{(M59Hud.Editing ? 1 : 0)}";
    }

    /// <summary>
    /// Follows the pack. Cheap to call every frame: a signature, and a
    /// rebuild only when it moved. The signature is the bag's - id,
    /// count, in use, applyable, name, colour, effect and frame
    /// (InventoryPanel.Sync says why each is there) - plus the columns
    /// and the scale, because both change what a slot is.
    /// </summary>
    public void Sync(IList<InventoryObject> items)
    {
        _items = items;
        string stamp = HudStamp();
        bool relay = stamp != _stamp;
        _stamp = stamp;

        if (items == null) { if (relay) Layout(); return; }

        var sb = new System.Text.StringBuilder();
        foreach (InventoryObject o in items)
        {
            sb.Append(o?.ID).Append(':').Append(o?.Count)
              .Append(o != null && o.IsInUse ? "u" : "-").Append(':')
              .Append(o != null && o.Flags != null && o.Flags.IsApplyable ? "a" : "-").Append(':')
              .Append(o?.Name).Append(':').Append(o?.ColorTranslation).Append(':')
              .Append(o?.Effect).Append(':').Append(o?.ViewerFrameIndex).Append(';');
        }
        sb.Append('@').Append(Across()).Append('@').Append(IconPixels())
          .Append('@').Append(Selection?.ID);
        string signature = sb.ToString();

        // A sprite that exists but is not yet readable changes nothing
        // in the data, so no signature can see it: retry on a timer
        // while any compose failed (notes/godot-ui.md).
        bool retry = _missed && Time.GetTicksMsec() >= _retryAt;

        if (signature == _signature && !retry) { if (relay) Layout(); return; }
        _signature = signature;

        // The selection follows the live instance, or goes when the
        // item has left the pack - the rule in notes/godot-ui.md, "A
        // model object held across a rebuild goes stale". Not through
        // Pick: the item was not tapped, so the target is not re-armed.
        if (Selection != null)
        {
            InventoryObject still = null;
            foreach (InventoryObject o in items)
                if (o != null && o.ID == Selection.ID) { still = o; break; }
            Selection = still;
        }

        Rebuild(items);
        Layout();
    }

    /// <summary>The icon's pixels: the game's 40 in a 52 slot, at the slot's drawn size.</summary>
    int IconPixels() => Mathf.Max(24, Mathf.RoundToInt(SlotSize * HudScale() * 40f / 52f));

    void Rebuild(IList<InventoryObject> items)
    {
        foreach (InventorySlot s in _slots) { _host.RemoveChild(s); s.QueueFree(); }
        _slots.Clear();
        _missed = false;

        // Always at least one full row: an empty pack is still a dock,
        // in the same place, and the editor has a handle to place it
        // by. The last row is filled out with empty squares so the
        // grid reads as a grid rather than a ragged line.
        int cols = Across();
        int n = Math.Max(cols, items.Count);
        int slots = (n + cols - 1) / cols * cols;
        int px = IconPixels();
        for (int i = 0; i < slots; i++)
        {
            InventoryObject o = i < items.Count ? items[i] : null;
            InventorySlot s = Slot(o, px);
            s.Name = o != null ? $"dock{o.ID}" : $"dockEmpty{i}";
            _host.AddChild(s);
            _slots.Add(s);
        }
        if (_missed) _retryAt = Time.GetTicksMsec() + 500;
    }

    /// <summary>
    /// One square, the bag's own look (InventoryPanel.Slot) at a
    /// smaller size: a filled slot a shade above an empty one, the
    /// picked one lit with a gold edge, a worn one with a warm edge,
    /// and the count badged whenever the thing is a stack.
    /// </summary>
    InventorySlot Slot(InventoryObject o, int px)
    {
        var slot = new InventorySlot { Item = o, MouseFilter = MouseFilterEnum.Stop };

        var box = new StyleBoxFlat
        {
            BgColor = o != null ? new Color(M59Skin.RowAlt.R, M59Skin.RowAlt.G, M59Skin.RowAlt.B, 0.92f)
                                : new Color(0.055f, 0.051f, 0.043f, 0.7f),
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(6);
        if (o != null && Selection != null && o.ID == Selection.ID)
        {
            box.BgColor = M59Skin.RowPick;
            box.BorderColor = o.IsInUse ? new Color(1f, 0.8f, 0.35f) : M59Skin.Gold;
            box.SetBorderWidthAll(3);
        }
        else if (o != null && o.IsInUse)
        {
            box.BorderColor = new Color(1f, 0.8f, 0.35f);
            box.SetBorderWidthAll(2);
        }
        slot.AddThemeStyleboxOverride("panel", box);
        if (o == null) return slot;

        ImageTexture tex = InventoryPanel.ComposeIcon(o, px, _icons);
        if (tex == null) _missed = true;
        var icon = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = 4; icon.OffsetTop = 4; icon.OffsetRight = -4; icon.OffsetBottom = -4;
        slot.AddChild(icon);

        if (o.Count > 0)
        {
            var count = new Label
            {
                Text = o.Count.ToString(),
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            count.AddThemeFontSizeOverride("font_size", Mathf.Max(9, Mathf.RoundToInt(FontSize * HudScale())));
            count.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            count.AddThemeConstantOverride("outline_size", 4);
            count.SetAnchorsPreset(LayoutPreset.FullRect);
            count.OffsetRight = -3; count.OffsetBottom = -1;
            slot.AddChild(count);
        }

        slot.TooltipText = o.Name;
        slot.Preview = tex;
        // Tap picks, the same slot again puts it away. No double-tap-
        // to-use here: the strip is one tap away and a second tap on a
        // small slot over the world is too easy to make by accident.
        slot.Tapped += item =>
        {
            if (M59Hud.Editing) return;
            if (Selection != null && Selection.ID == item.ID) Pick(null);
            else Pick(item);
        };
        return slot;
    }

    /// <summary>Selects, as a tap does. The harness uses it by name too.</summary>
    public void Pick(InventoryObject item)
    {
        Selection = item;
        // Arm the target rather than set it - Tick does the setting,
        // one window later, as the bag's does (InventoryPanel.Pick).
        _arming = item;
        _armedAt = Time.GetTicksMsec();
        _signature = "";   // the grid draws the selection
        if (_items != null) Sync(_items); else Layout();
    }

    /// <summary>A tap on the world, a panel opening, the editor: the strip goes away.</summary>
    public void Deselect()
    {
        if (Selection == null && _arming == null) return;
        _arming = null;
        Pick(null);
    }

    public override void _Process(double delta)
    {
        if (_arming == null) return;
        if (Time.GetTicksMsec() - _armedAt < DoubleTapMs) return;
        InventoryObject landing = _arming;
        _arming = null;
        Targeted?.Invoke(landing);
    }

    // ---- layout -----------------------------------------------------

    /// <summary>
    /// Where the designer put it: the left edge, a gutter under the
    /// side keys (Auto run) and above the chat block, clear of the
    /// target card on the right and of the fixed pad, which sits
    /// lower (FixedControls.Layout). Found from the side piece's
    /// NATURAL rect, not where the player moved it, for the reason
    /// the pad gives: a default that followed another piece's drag
    /// would not be a default.
    /// </summary>
    Rect2 Natural(Vector2 v, Vector2 size)
    {
        M59Hud.Piece side = M59Hud.Get("sidekeys");
        float top = side != null && side.Natural.Size.Y > 2f
            ? side.Natural.Position.Y + side.Natural.Size.Y + Gutter
            : Mathf.Round(v.Y * 0.5f);
        return new Rect2(Gutter, Mathf.Round(top), size);
    }

    void Layout()
    {
        if (_host == null) return;
        Vector2 v = GetViewportRect().Size;
        Position = Vector2.Zero; Size = v;
        _host.Position = Vector2.Zero; _host.Size = v;

        bool show = M59Hud.Shows(Id);
        // Dress sets Visible false for a hidden piece outside the
        // editor and never sets it back - re-shown on every layout, as
        // the other pieces do (FixedControls.Layout).
        _host.Visible = true;
        M59Hud.Dress(Id);
        if (!show)
        {
            _host.Visible = false;
            Strip(false, default);
            return;
        }

        float sc = HudScale();
        float side = Mathf.Round(SlotSize * sc), sep = Mathf.Round(Sep * sc);
        int cols = Across();
        int rows = Math.Max(1, (_slots.Count + cols - 1) / cols);
        var size = new Vector2(cols * side + (cols - 1) * sep, rows * side + (rows - 1) * sep);

        Rect2 at = M59Hud.Place(Id, Natural(v, size), v);
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].Position = at.Position + new Vector2((i % cols) * (side + sep), (i / cols) * (side + sep));
            _slots[i].Size = new Vector2(side, side);
        }

        Strip(Selection != null && !M59Hud.Editing, at);
    }

    /// <summary>
    /// The verbs, hung under the grid - or over it when the grid is
    /// near the bottom, which over the chat it usually is not. As wide
    /// as the grid or as the four buttons need, whichever is more, with
    /// Drop pinned to the far end so the width of the strip is between
    /// it and Use. Not scaled with the piece: the strip is a footer,
    /// and a footer's buttons are the house size wherever they are.
    /// </summary>
    void Strip(bool on, Rect2 grid)
    {
        _strip.Visible = on; _name.Visible = on;
        _use.Visible = on; _look.Visible = on; _bind.Visible = on; _drop.Visible = on;
        if (!on) return;

        Vector2 v = GetViewportRect().Size;
        const float btnH = 44f, nameH = 26f, pad = 10f;
        float gap = M59Skin.Gap;
        float W(Button b) => Mathf.Max(96f, b.Text.Length * 11f + 36f);
        float need = pad + W(_use) + gap + W(_look) + gap + W(_bind) + gap * 3f + W(_drop) + pad;
        float w = Mathf.Max(grid.Size.X, need);
        float h = pad + nameH + 4f + btnH + pad;

        float x = Mathf.Clamp(grid.Position.X, 0f, Mathf.Max(0f, v.X - w));
        float y = grid.Position.Y + grid.Size.Y + 6f;
        if (y + h > v.Y) y = grid.Position.Y - 6f - h;
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, v.Y - h));

        _strip.Position = new Vector2(x, y);
        _strip.Size = new Vector2(w, h);

        _name.Text = string.IsNullOrWhiteSpace(Selection.Name) ? "(unnamed)" : Selection.Name;
        _name.Position = new Vector2(x + pad, y + pad);
        _name.Size = new Vector2(w - pad * 2f, nameH);

        // The library decides between use, unuse and apply; the label
        // says which (InventoryPanel.Relabel).
        _use.Text = Selection.Flags != null && Selection.Flags.IsApplyable ? "Apply"
                  : (Selection.IsInUse ? "Unuse" : "Use");

        float by = y + pad + nameH + 4f;
        float bx = x + pad;
        foreach (Button b in new[] { _use, _look, _bind })
        {
            b.Position = new Vector2(bx, by);
            b.Size = new Vector2(W(b), btnH);
            bx += W(b) + gap;
        }
        _drop.Size = new Vector2(W(_drop), btnH);
        _drop.Position = new Vector2(x + w - pad - W(_drop), by);
    }
}
