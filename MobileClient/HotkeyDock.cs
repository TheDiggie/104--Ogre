using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// The hotbar, laid flat on the glass: a grid of every seat, in Num
/// order, drawn and sized exactly as the inventory dock draws the pack.
///
/// WHY. The player's words: "add a new optional box players can unhide
/// in the ui customizer. A hotkey box that looks just like the
/// inventory box and they can change the rows and columns just like the
/// inventory. And let them edit it with the hotkey customizer." The
/// combat cluster shows four seats a page under the thumb, which is
/// right for a fight and wrong for a player with three pages of buffs
/// who wants them all in view; the reference shows all forty-eight at
/// once (`UIActionButtons.cpp:23-26`) and never has the problem. This
/// is that grid, as a HUD piece, for the player who asks for it.
///
/// OPTIONAL means hidden by default. A second copy of the hotbar on the
/// glass is a thing to opt into, so the piece registers with
/// <c>hidden: true</c> (<see cref="M59Hud.Piece.DefaultHidden"/>) and
/// the player unhides it in the arrange screen the way any hidden piece
/// comes back: the editor draws it faint, a tap picks it, Show shows it.
/// Across and Down are the dock's own verbs on the same card
/// (<see cref="M59Hud.Piece.Columns"/>, <see cref="M59Hud.Piece.Rows"/>),
/// saved as the same sixth and seventh fields.
///
/// WHAT IT SHOWS is the cluster's model, uncut: every config in
/// <c>Data.ActionButtons</c> by Num (<see cref="ActionButtons.ByNum"/>),
/// Attack among them as one more seat - here it is a binding like any
/// other, not the primary - and an Unset config as the empty square,
/// because the arc is positional (ActionButtons.Sync, "THE ARC IS
/// POSITIONAL") and a hole the HotKeys panel made is a hole here too.
/// A grid that re-packed the holes would show the seats in an order the
/// panel does not.
///
/// WHAT A TAP DOES is what the cluster does, through the cluster: the
/// press goes to <see cref="ActionButtons.Perform"/>, which is the one
/// path every seat fires through - Run (HotbarAct) around Activate(), or
/// the view's SendReqGo for the Door slot. Nothing here knows what a
/// binding sends. A held Attack repeats as the cluster's does (the
/// reference re-activates a held key every input tick,
/// `ControllerInput.cpp:995-1030`); the other kinds are tap-only in this
/// box, because the cluster's rule for them reads its own hold state
/// (the item's in-use flag at press time) and sharing that is a
/// refactor this box does not pay for.
///
/// WHAT IT NEVER DOES is write a seat. Edit, the one button in its
/// strip, opens the HotKeys panel - the same panel the drawer tile opens
/// - and that panel owns the setters. The box follows the list through
/// the same subscription the cluster uses (ListChanged, see
/// ActionButtons.Follow), so a seat set, cleared or swapped there is
/// redrawn here when the panel closes - and, as every polled piece, it
/// carries the resolution state of each seat's art in its signature and
/// retries a picture that was not readable yet (notes/godot-ui.md, "A
/// polled signature must hold everything the panel draws").
/// </summary>
public partial class HotkeyDock : Control
{
    public const string Id = "hotkeys";

    /// <summary>The inventory dock's slot, the same number for the same reason (InventoryDock.SlotSize).</summary>
    [Export] public float SlotSize = 56f;
    [Export] public float Sep = 6f;
    /// <summary>A seat with no picture shows its name; this is the size of that name at 1x.</summary>
    [Export] public int FontSize = 12;
    /// <summary>Eight across, two down: the inventory dock's defaults, as asked.</summary>
    [Export] public int Columns = 8;
    public const int MinCols = 2, MaxCols = 16;
    [Export] public int Rows = 2;
    public const int MinRows = 1, MaxRows = 8;

    /// <summary>The gutter between this and the piece it sits beside.</summary>
    const float Gutter = 16f;

    /// <summary>The player wants the HotKeys panel. The view opens it, as it does for the drawer tile.</summary>
    public event Action EditRequested;

    /// <summary>
    /// The press, supplied by the view as the cluster's own
    /// <see cref="ActionButtons.Perform"/>: (config, repeat) -> sent.
    /// Null in a bare harness, where a seat then draws and does nothing.
    /// </summary>
    public Func<ActionButtonConfig, bool, bool> Perform;

    /// <summary>See InventoryDock.Covered - the same gate, for the same scroll box.</summary>
    public bool Covered { get; set; }

    Control _host;
    TouchScroll _box;
    Control _grid;
    readonly List<Button> _slots = new List<Button>();
    /// <summary>The button number showing in each slot, resolved at the press (ActionButtons.Fire says why not the object).</summary>
    readonly List<int> _nums = new List<int>();
    Panel _strip;
    Label _name;
    Button _edit;

    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    DataController _data;
    Meridian59.Data.Lists.ActionButtonList _list;
    string _signature = "";
    M59Hud.Stamp _stamp;
    bool _missed;
    ulong _retryAt;

    // The hold. Only Attack repeats here - see the class comment.
    int _heldSlot = -1;
    ulong _heldSince;
    bool _repeating;
    /// <summary>The cluster's delay before a press becomes a hold (ActionButtons.RepeatDelayMs).</summary>
    [Export] public ulong RepeatDelayMs = 250;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // One node to hand M59Hud, as the dock does; the strip is INSIDE
        // it here, because Edit is part of the piece - it has no
        // selection to belong to, so it fades and hides with the grid.
        _host = new Control { Name = "hotdockHost", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_host);

        _box = new TouchScroll { Name = "hotdockBox", MouseFilter = MouseFilterEnum.Ignore };
        _box.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _host.AddChild(_box);
        M59Skin.SlimScroller(_box);
        _box.FitContent = false;
        _grid = new Control { Name = "hotdockGrid", MouseFilter = MouseFilterEnum.Ignore };
        _box.AddChild(_grid);

        _strip = M59Skin.Window();
        _strip.Name = "hotdockStrip";
        _strip.MouseFilter = MouseFilterEnum.Stop;
        _host.AddChild(_strip);

        _name = M59Skin.Caption("Hotkeys");
        _name.Name = "hotdockName";
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.ClipText = true;
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _name.AddThemeColorOverride("font_color", M59Skin.Text);
        _host.AddChild(_name);

        _edit = new Button { Text = "Edit", Name = "hotdockEdit" };
        M59Skin.Dress(_edit, M59Skin.Kind.Secondary);
        _edit.Pressed += () => { if (!M59Hud.Editing) EditRequested?.Invoke(); };
        _host.AddChild(_edit);

        // Hidden until the player says otherwise - the one thing that
        // makes this piece "optional" rather than one more default.
        M59Hud.Piece p = M59Hud.Register(Id, "Hotkey box", _host, hidden: true);
        if (p != null)
        {
            p.MinColumns = MinCols; p.MaxColumns = MaxCols; p.DefaultColumns = Columns;
            p.MinRows = MinRows; p.MaxRows = MaxRows; p.DefaultRows = Rows;
        }
        M59Hud.Changed += Layout;
        GetViewport().SizeChanged += Layout;
    }

    public override void _ExitTree()
    {
        M59Hud.Changed -= Layout;
        if (_list != null) _list.ListChanged -= OnButtonsChanged;
    }

    // ---- the model ------------------------------------------------

    static M59Hud.Piece Piece => M59Hud.Get(Id);

    static float HudScale()
    {
        M59Hud.Piece p = Piece;
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    int Across() { M59Hud.Piece p = Piece; return p == null ? Columns : p.ColumnsNow; }
    int Down() { M59Hud.Piece p = Piece; return p == null ? Rows : p.RowsNow; }

    M59Hud.Stamp HudStamp()
    {
        M59Hud.Piece p = Piece;
        return p == null ? default : new M59Hud.Stamp(p, Covered ? 1 : 0);
    }

    /// <summary>The icon's pixels: the game's 40 in a 52 slot, at the slot's drawn size (InventoryDock.IconPixels).</summary>
    int IconPixels() => Mathf.Max(24, Mathf.RoundToInt(SlotSize * HudScale() * 40f / 52f));

    /// <summary>
    /// Watches the list the way the cluster does (ActionButtons.Follow):
    /// SetToSpell and its fellows set type and name as plain fields, so
    /// a poll sees nothing and the subscription is what catches a rebind.
    /// </summary>
    void Follow(DataController data)
    {
        if (data?.ActionButtons == null || ReferenceEquals(_list, data.ActionButtons)) return;
        if (_list != null) _list.ListChanged -= OnButtonsChanged;
        _list = data.ActionButtons;
        _list.ListChanged += OnButtonsChanged;
        _signature = "";
    }

    void OnButtonsChanged(object sender, System.ComponentModel.ListChangedEventArgs e) => _signature = "";

    readonly List<ActionButtonConfig> _seats = new List<ActionButtonConfig>();

    /// <summary>
    /// Follows the hotbar. Cheap every frame: a signature - Num, type,
    /// name and the art's resolution state per seat, as the cluster and
    /// the HotKeys panel sign - and a rebuild only when it moved.
    /// </summary>
    public void Sync(DataController data)
    {
        _data = data;
        Follow(data);
        M59Hud.Stamp stamp = HudStamp();
        bool relay = stamp != _stamp;
        _stamp = stamp;

        if (data?.ActionButtons == null)
        {
            if (_slots.Count > 0) { Rebuild(null); _signature = ""; }
            if (relay) Layout();
            return;
        }

        List<ActionButtonConfig> seats = _seats; seats.Clear();
        foreach (ActionButtonConfig b in data.ActionButtons) if (b != null) seats.Add(b);
        ActionButtons.Stable(seats);

        var sb = Sig.Start();
        foreach (ActionButtonConfig b in seats)
        {
            sb.Append(b.Num).Append(':').Append((int)b.ButtonType).Append(':').Append(b.Name);
            if (b.Data is ObjectBase o)
                sb.Append(':').Append(o.Resource?.Filename).Append(':').Append(o.ColorTranslation)
                  .Append(':').Append(o.Effect).Append(':').Append(o.ViewerFrameIndex);
            sb.Append(';');
        }
        sb.Append('@').Append(Across()).Append('@').Append(Down()).Append('@').Append(IconPixels());
        bool retry = _missed && Time.GetTicksMsec() >= _retryAt;

        if (!Sig.Changed(sb, ref _signature) && !retry) { if (relay) Layout(); return; }

        Rebuild(seats);
        Layout();
    }

    void Rebuild(List<ActionButtonConfig> seats)
    {
        EndHold();
        foreach (Button s in _slots) { _grid.RemoveChild(s); s.QueueFree(); }
        _slots.Clear();
        _nums.Clear();
        _missed = false;
        if (seats == null) return;

        // The whole box, padded with empty squares to its last row, as
        // the dock pads the pack (InventoryDock.Rebuild says why).
        int cols = Across();
        int n = Math.Max(cols * Down(), seats.Count);
        int count = (n + cols - 1) / cols * cols;
        int px = IconPixels();
        for (int i = 0; i < count; i++)
        {
            ActionButtonConfig cfg = i < seats.Count ? seats[i] : null;
            bool empty = cfg == null || cfg.ButtonType == ActionButtonType.Unset;
            Button s = Slot(empty ? null : cfg, px, i);
            s.Name = !empty ? $"hotdock{cfg.Num}" : $"hotdockEmpty{i}";
            _grid.AddChild(s);
            _slots.Add(s);
            _nums.Add(empty ? -1 : cfg.Num);
        }
        if (_missed) _retryAt = Time.GetTicksMsec() + 500;
    }

    /// <summary>
    /// One square, the inventory dock's look (InventoryDock.Slot): a
    /// filled seat a shade above an empty one, the same rule-coloured
    /// edge and corner, the picture inset the same four points. A
    /// Button rather than an InventorySlot because a seat is pressed,
    /// not picked, and a press can be held (ButtonDown/ButtonUp).
    /// </summary>
    Button Slot(ActionButtonConfig cfg, int px, int slot)
    {
        var b = new Button
        {
            ClipText = true,
            ExpandIcon = false,
            IconAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Stop,
        };
        StyleBoxFlat normal = Box(cfg != null);
        StyleBoxFlat down = Box(cfg != null);
        down.BorderColor = M59Skin.Gold;
        down.SetBorderWidthAll(3);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal);
        b.AddThemeStyleboxOverride("focus", normal);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("pressed", down);
        b.AddThemeFontSizeOverride("font_size", Mathf.Max(9, Mathf.RoundToInt(FontSize * HudScale())));
        b.AddThemeColorOverride("font_color", M59Skin.Text);
        b.AddThemeColorOverride("font_pressed_color", M59Skin.GoldBright);
        b.AddThemeColorOverride("font_hover_color", M59Skin.Text);
        b.AddThemeColorOverride("font_focus_color", M59Skin.Text);
        if (cfg == null) { b.Disabled = true; b.FocusMode = FocusModeEnum.None; return b; }

        // As the cluster captions (ActionButtons.Sync): the picture when
        // there is one, the name when there is not - every action - and
        // the key beside the shared picture for an alias.
        Texture2D icon = Icon(cfg, px);
        bool alias = cfg.ButtonType == ActionButtonType.Alias;
        b.Icon = icon;
        b.Text = icon != null && !alias ? "" : Short(cfg.Name, alias ? 6 : 8);
        if (b.Text.Length > 0 && icon != null) b.IconAlignment = HorizontalAlignment.Left;
        b.TooltipText = ActionButtons.IsGo(cfg) ? ActionButtons.GoName + "\nThrough the door" : cfg.Name;

        // The slot index, resolved to a Num at the press, for the
        // reason the cluster gives (ActionButtons.Sync, "The slot, not
        // the button number and not the config object").
        b.ButtonDown += () => OnDown(slot);
        b.ButtonUp += () => EndHold();
        b.Pressed += () => Fire(slot);
        return b;
    }

    static StyleBoxFlat Box(bool filled)
    {
        var box = new StyleBoxFlat
        {
            BgColor = filled ? new Color(M59Skin.RowAlt.R, M59Skin.RowAlt.G, M59Skin.RowAlt.B, 0.92f)
                             : new Color(0.055f, 0.051f, 0.043f, 0.7f),
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(2);
        return box;
    }

    // ---- the press ---------------------------------------------------

    ActionButtonConfig At(int slot)
    {
        if (slot < 0 || slot >= _nums.Count || _nums[slot] < 0) return null;
        return _data?.ActionButtons?.GetByNum(_nums[slot]);
    }

    void OnDown(int slot)
    {
        if (M59Hud.Editing) return;
        _heldSlot = slot;
        _heldSince = Time.GetTicksMsec();
        _repeating = false;
    }

    /// <summary>
    /// Ends a hold. The flag Pressed reads is cleared at the end of the
    /// frame, because Godot may raise Pressed before or after ButtonUp
    /// (ActionButtons.EndHold).
    /// </summary>
    void EndHold()
    {
        if (_heldSlot < 0) return;
        _heldSlot = -1;
        Callable.From(() => _repeating = false).CallDeferred();
    }

    /// <summary>A held Attack swings every frame past the delay; the library's interval is the throttle.</summary>
    public override void _Process(double delta)
    {
        if (_heldSlot < 0 || Covered) return;
        if (Time.GetTicksMsec() - _heldSince < RepeatDelayMs) return;
        ActionButtonConfig cfg = At(_heldSlot);
        if (!ActionButtons.IsAttack(cfg)) return;
        _repeating = true;
        Perform?.Invoke(cfg, true);
    }

    /// <summary>A tap, or the release of a press that did not repeat - the cluster's Fire.</summary>
    void Fire(int slot)
    {
        bool held = _heldSlot == slot;
        if (_repeating) { EndHold(); return; }
        if (held) EndHold();
        if (M59Hud.Editing || Covered) return;
        ActionButtonConfig cfg = At(slot);
        if (cfg == null) return;
        Perform?.Invoke(cfg, false);
    }

    // ---- pictures ----------------------------------------------------

    /// <summary>The seat's picture, as the HotKeys panel composes it; a miss is not cached (notes/godot-ui.md).</summary>
    Texture2D Icon(ActionButtonConfig cfg, int px)
    {
        if (cfg.ButtonType == ActionButtonType.Alias) return ActionButtons.AliasIcon();
        if (cfg.ButtonType == ActionButtonType.Action || cfg.ButtonType == ActionButtonType.Unset) return null;
        if (cfg.Data is not ObjectBase o) return null;
        if (o.Resource == null) { _missed = true; return null; }
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}:{px}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;
        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, px)); }
        catch (Exception e) { GD.PrintErr($"[HotkeyDock] icon: {e.Message}"); }
        if (tex != null) _icons[key] = tex;
        else _missed = true;
        return tex;
    }

    static string Short(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "?";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max);
    }

    // ---- layout ------------------------------------------------------

    /// <summary>The strip's height: the inventory dock's, so the two boxes read as a pair (InventoryDock.Strip).</summary>
    const float StripPad = 10f, StripName = 26f, StripBtn = 44f;
    const float StripH = StripPad + StripName + 4f + StripBtn + StripPad;

    /// <summary>
    /// Where the designer put it: beside the inventory dock, a gutter to
    /// its right at the same top, from the dock's NATURAL rect - not
    /// where the player moved it, for the reason the dock gives about
    /// the side keys. At 1920x1080 with the dock at its default that is
    /// (522,543), eight slots wide, clear of the target card (1336+) and
    /// above the chat block.
    /// </summary>
    Rect2 Natural(Vector2 v, Vector2 size)
    {
        M59Hud.Piece dock = M59Hud.Get(InventoryDock.Id);
        if (dock != null && dock.Natural.Size.X > 2f)
            return new Rect2(Mathf.Round(dock.Natural.Position.X + dock.Natural.Size.X + Gutter),
                             dock.Natural.Position.Y, size);
        return new Rect2(Gutter, Mathf.Round(v.Y * 0.5f), size);
    }

    void Layout()
    {
        if (_host == null) return;
        Vector2 v = GetViewportRect().Size;
        Position = Vector2.Zero; Size = v;
        _host.Position = Vector2.Zero; _host.Size = v;

        bool show = M59Hud.Shows(Id);
        _host.Visible = true;
        M59Hud.Dress(Id);
        if (!show) { _host.Visible = false; EndHold(); return; }

        float sc = HudScale();
        float side = Mathf.Round(SlotSize * sc), sep = Mathf.Round(Sep * sc);
        int cols = Across();
        int rows = Math.Max(1, (_slots.Count + cols - 1) / cols);
        int seen = Down();
        bool scrolls = rows > seen;
        float gridW = cols * side + (cols - 1) * sep;
        float gridH = rows * side + (rows - 1) * sep;
        float boxW = gridW + (scrolls ? M59Skin.SlimBarW : 0f);
        float boxH = seen * side + (seen - 1) * sep;

        // The piece is the box AND the strip, so the editor's handle
        // and its overlap test see the whole thing: Edit is always
        // there, where the dock's verbs come and go with a selection.
        const float gap = 6f;
        float W(Button b) => Mathf.Max(96f, b.Text.Length * 11f + 36f);
        float stripW = Mathf.Max(boxW, StripPad + 120f + M59Skin.Gap + W(_edit) + StripPad);
        var size = new Vector2(Mathf.Max(boxW, stripW), boxH + gap + StripH);

        Rect2 at = M59Hud.Place(Id, Natural(v, size), v);
        _box.Position = at.Position;
        _box.Size = new Vector2(boxW, boxH);
        _box.VerticalScrollMode = scrolls ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        // The same gate as the dock's, for the same reasons
        // (InventoryDock.Layout): Stop only while there is something to
        // scroll, input only while nothing is over it.
        _box.MouseFilter = scrolls ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _box.SetProcessInput(scrolls && !M59Hud.Editing && !Covered);
        if (!scrolls) _box.ScrollVertical = 0;
        _grid.CustomMinimumSize = new Vector2(gridW, gridH);
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].Position = new Vector2((i % cols) * (side + sep), (i / cols) * (side + sep));
            _slots[i].Size = new Vector2(side, side);
        }

        // The strip under the box, the dock's shape: a name line and a
        // row of buttons - one button here. Not scaled with the piece,
        // as the dock's is not: a footer's buttons are the house size.
        float x = at.Position.X, y = at.Position.Y + boxH + gap;
        _strip.Position = new Vector2(x, y);
        _strip.Size = new Vector2(size.X, StripH);
        _name.Position = new Vector2(x + StripPad, y + StripPad);
        _name.Size = new Vector2(size.X - StripPad * 2f, StripName);
        _edit.Size = new Vector2(W(_edit), StripBtn);
        _edit.Position = new Vector2(x + StripPad, y + StripPad + StripName + 4f);
        // Pressable only outside the editor, where the editor's glass
        // takes the touch anyway; Disabled says so to a scripted press.
        _edit.Disabled = M59Hud.Editing;
    }
}
