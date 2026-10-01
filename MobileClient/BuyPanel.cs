using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// A check box that can be seen. Godot's default CheckBox draws its
/// box and tick in dark grey strokes meant for a light theme, and every
/// panel here is a near-black sheet: the Players panel's "mute" box and
/// the Buy panel's row selector were a few dark pixels on dark, and on
/// Buy the tick appeared only once the row's own "pressed" fill lit the
/// background behind it. These two pictures carry their own light
/// border and their own fill, so the box reads on any background and
/// the tick reads in every state - unchecked, checked, disabled.
/// </summary>
public static class TickStyle
{
    static readonly System.Collections.Generic.Dictionary<(int, bool, bool), ImageTexture> _cache =
        new System.Collections.Generic.Dictionary<(int, bool, bool), ImageTexture>();

    static ImageTexture Get(int n, bool ticked, bool dim)
    {
        if (!_cache.TryGetValue((n, ticked, dim), out ImageTexture t))
            _cache[(n, ticked, dim)] = t = Make(n, ticked, dim);
        return t;
    }

    public static void Apply(CheckBox box, int size = 32)
    {
        box.AddThemeIconOverride("unchecked", Get(size, false, false));
        box.AddThemeIconOverride("checked", Get(size, true, false));
        box.AddThemeIconOverride("unchecked_disabled", Get(size, false, true));
        box.AddThemeIconOverride("checked_disabled", Get(size, true, true));
        // The default icon colours tint by state and are not pure
        // white, which would dim the tick again.
        foreach (string c in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color",
                                     "icon_hover_pressed_color", "icon_focus_color" })
            box.AddThemeColorOverride(c, Colors.White);
    }

    static ImageTexture Make(int n, bool ticked, bool dim)
    {
        float a = dim ? 0.45f : 1f;
        var border = new Color(0.92f, 0.94f, 1f, a);
        var fill = ticked ? new Color(0.16f, 0.5f, 0.24f, a) : new Color(0.22f, 0.24f, 0.30f, a);
        var tick = new Color(1f, 1f, 1f, a);

        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        int m = Mathf.Max(2, n / 12);       // border width
        int pad = Mathf.Max(1, n / 16);
        for (int y = pad; y < n - pad; y++)
            for (int x = pad; x < n - pad; x++)
            {
                bool edge = x < pad + m || y < pad + m || x >= n - pad - m || y >= n - pad - m;
                img.SetPixel(x, y, edge ? border : fill);
            }

        if (ticked)
        {
            // A check mark: down-right to the foot, then up-right, three
            // pixels thick.
            Vector2 p0 = new Vector2(n * 0.24f, n * 0.52f);
            Vector2 p1 = new Vector2(n * 0.43f, n * 0.72f);
            Vector2 p2 = new Vector2(n * 0.78f, n * 0.30f);
            Line(img, p0, p1, tick, m + 1);
            Line(img, p1, p2, tick, m + 1);
        }
        return ImageTexture.CreateFromImage(img);
    }

    static void Line(Image img, Vector2 a, Vector2 b, Color c, int w)
    {
        int steps = (int)(a.DistanceTo(b) * 2f);
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = a.Lerp(b, i / (float)Math.Max(1, steps));
            for (int dy = -w / 2; dy <= w / 2; dy++)
                for (int dx = -w / 2; dx <= w / 2; dx++)
                {
                    int x = (int)p.X + dx, y = (int)p.Y + dy;
                    if (x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight())
                        img.SetPixel(x, y, c);
                }
        }
    }
}

/// <summary>
/// The shopkeeper's stock.
///
/// `UIBuy.cpp`: a multi-select list of what the merchant sells, each row
/// an icon, the name and the price, with a running sum underneath and an
/// OK button that buys everything ticked in one message. Three details
/// come from that file rather than from taste:
///
///  - the window is the server's decision. `BuyInfo.IsVisible` goes up
///    when the merchant sends their stock and down when OK is pressed;
///    the view does not open it. `AvatarAction.Buy` asks - it looks for
///    a buyable object near you and sends `SendReqBuyMessage`.
///  - the sum counts a stackable item as `Count * Price` and everything
///    else as `Price` once, which is `Buy::CalculateSum` exactly.
///  - OK sends one `ReqBuyItems` carrying the merchant's id and an
///    ObjectID per ticked row, each with that row's count - not one
///    message per item.
///
/// Multi-select is a checkbox per row here rather than ctrl-click,
/// because a finger has no ctrl.
/// </summary>
public partial class BuyPanel : Control
{
    /// <summary>
    /// Air under the last button row. Without it the button's bottom
    /// edge and the panel's own are the same line, and the only way out
    /// of the window reads as cut off while every row above it has a
    /// gap.
    /// </summary>
    const float Foot = 12f;

    [Export] public int FontSize = 16;
    [Export] public int IconSize = 40;
    [Export] public int RowHeight = 56;

    /// <summary>Buy these, from the merchant the server named.</summary>
    public event Action<List<TradeOfferObject>> Buy;
    /// <summary>How many of this stackable line to buy.</summary>
    public event Action<TradeOfferObject> AmountWanted;

    /// <summary>
    /// Describe this one. The game looks at a row on a right click
    /// (`UIBuy.cpp:223`) - reading what something is before paying
    /// twelve hundred for it is not a luxury. A hold stands in for the
    /// right button.
    /// </summary>
    public event Action<uint> Look;

    /// <summary>How long a press is held before it describes instead of ticking.</summary>
    [Export] public ulong LongPressMs = 600;

    ulong _downAt;
    bool _reverting;

    ColorRect _panel;
    Label _title, _sum;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _ok, _close;

    readonly HashSet<uint> _ticked = new HashSet<uint>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    readonly List<TradeOfferObject> _stock = new List<TradeOfferObject>();
    /// <summary>
    /// How many of each line the merchant actually has, by id - the
    /// ceiling for "how many", which the line's own count stops being
    /// the moment you choose fewer.
    ///
    /// Choosing an amount writes it into the line, which is what the
    /// reference does (`UIBuy.cpp:255`). The reference can get away with
    /// it because its amount box has no maximum at all - you type over
    /// it. Here the amount prompt is clamped to the number it is opened
    /// with, so re-opening it on the line's current count made every
    /// choice one-way: three of a hundred, and three was the ceiling
    /// from then on. Kept beside the model rather than in it, the way
    /// TradePanel keeps its own amounts.
    /// </summary>
    readonly Dictionary<uint, uint> _stockMost = new Dictionary<uint, uint>();
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Opaque. At 0.95 the chat overlay (added to the same layer, and
        // anchored to the bottom-left) shows through at 5%, which is
        // plenty for white text on this colour: on 1920x1080 the running
        // total sits at y~860, exactly where the chat lines draw, and
        // the one number a player must read before spending had chat
        // running through it.
        //
        // Why the whole panel rather than the alternatives. MOVING the
        // total only relocates the collision: the chat block is anchored
        // to the screen, not to this panel, and in portrait it spans the
        // full width, so nowhere in the lower half of the panel is safe.
        // GIVING THE TOTAL ITS OWN BACKING fixes one label and leaves the
        // prices, names and button captions beside it ghosted by the same
        // bleed. The game has neither problem because its shop is an
        // opaque CEGUI frame (`UIBuy.cpp:12` takes it as a FrameWindow),
        // so nothing draws behind any of it; an opaque panel is the same
        // thing, and the 5% it gives up showed nothing of the world
        // anyway. ChatOverlay's own full-screen log is opaque for the
        // same reason (ChatOverlay.cs, `_fullBack`).
        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "For sale", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _sum = new Label { Text = "", Visible = false };
        _sum.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _sum.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.4f));
        AddChild(_sum);

        _ok = Action("Buy", () =>
        {
            var want = new List<TradeOfferObject>();
            foreach (TradeOfferObject o in _stock)
                if (o != null && _ticked.Contains(o.ID)) want.Add(o);
            if (want.Count > 0) Buy?.Invoke(want);
            Dismiss();
        });
        _close = Action("Close", Dismiss);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Action(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.66f, 640f);
        float top = v.Y - height - side;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _scroll.Position = new Vector2(side, top + FontSize * 2.2f);
        _scroll.Size = new Vector2(v.X - side * 2f, height - FontSize * 2.2f - rowH * 2f - 20f);

        _sum.Position = new Vector2(side, top + height - rowH * 2f - 6f - Foot);
        _sum.Size = new Vector2(v.X - side * 2f, rowH);

        float y = top + height - rowH - Foot;
        Button[] row = { _ok, _close };
        float w = (v.X - side * 2f - 8f) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(side + i * (w + 8f), y);
            row[i].Size = new Vector2(w, rowH);
        }
    }

    /// <summary>
    /// Closes the shop the way the game closes it - by telling the model,
    /// which is the only thing the window's visibility follows.
    ///
    /// Hiding the Controls alone did not work: Sync runs every frame,
    /// saw IsVisible still set, and reopened the window on the next one,
    /// so both Buy and Close looked like they did nothing. The game sets
    /// the flag false after buying (`UIBuy.cpp:294`) and clears the stock
    /// as well when the window is closed outright (`:308`), since what a
    /// shopkeeper had is not worth remembering once you have walked away.
    /// </summary>
    /// <summary>
    /// Rebuilds the rows and the total now. Called when something
    /// outside changed a line - choosing how many of a stackable to
    /// buy - because the signature is what normally drives a rebuild
    /// and a count change is exactly what it watches.
    /// </summary>
    public void Refresh() => _signature = "";

    public void Dismiss()
    {
        if (_buy != null) { _buy.IsVisible = false; _buy.Clear(true); }
        Close();
    }

    public void Close()
    {
        Show(false);
        _ticked.Clear();
        _stockMost.Clear();
        _signature = "";
    }

    /// <summary>
    /// The most of this line there is to buy - the ceiling for the
    /// amount prompt, which is not the line's current count once you
    /// have chosen fewer than all of them.
    /// </summary>
    public uint Most(TradeOfferObject line)
    {
        if (line == null) return 1u;
        return _stockMost.TryGetValue(line.ID, out uint most) && most > line.Count
            ? most : line.Count;
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _sum.Visible = on; _ok.Visible = on; _close.Visible = on;
    }

    /// <summary>
    /// Follows the merchant's stock. `IsVisible` is the server's switch,
    /// not the view's.
    /// </summary>
    BuyInfo _buy;

    public void Sync(BuyInfo buy)
    {
        if (_rows == null) return;
        _buy = buy;

        // Visibility is the model's to decide, not the item count's.
        // The reference opens the window on IsVisible alone
        // (`UIBuy.cpp:60-66`), so a shop with nothing in stock is an
        // open, empty shop rather than a tap that appeared to do
        // nothing.
        if (buy == null || !buy.IsVisible)
        {
            if (IsOpen) Close();
            return;
        }

        if (!IsOpen) Show(true);

        if (buy.Items == null || buy.Items.Count == 0)
        {
            if (_signature != "")
            {
                _signature = "";
                foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            }
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (TradeOfferObject o in buy.Items)
            sb.Append(o?.ID).Append(':').Append(o?.Count).Append(':').Append(o?.Price).Append(';');
        string now = sb.ToString();

        if (now != _signature)
        {
            _signature = now;
            _stock.Clear();

            // What you had picked is kept across a rebuild, minus
            // anything the shop no longer has. The rebuild fires on any
            // change to a count or a price - the server's doing, not
            // yours - and clearing the whole selection meant a stock
            // update silently unticked everything you had chosen. The
            // game has no such problem: its selection lives on the list
            // widget and an unrelated item changing does not touch it.
            var stillThere = new HashSet<uint>();
            foreach (TradeOfferObject o in buy.Items)
                if (o != null && _ticked.Contains(o.ID)) stillThere.Add(o.ID);
            _ticked.Clear();
            foreach (uint id in stillThere) _ticked.Add(id);

            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

            foreach (TradeOfferObject o in buy.Items)
                if (o != null)
                {
                    // The high-water mark, because our own writes only
                    // ever lower a line's count and the server's can
                    // raise it. Never lowered here, so choosing fewer
                    // does not shrink the ceiling - which is the bug -
                    // and a shop that genuinely restocks upwards still
                    // lets you ask for the lot.
                    if (!_stockMost.TryGetValue(o.ID, out uint had) || o.Count > had)
                        _stockMost[o.ID] = o.Count;

                    _stock.Add(o); _rows.AddChild(Row(o));
                }

            string who = buy.TradePartner != null && !string.IsNullOrWhiteSpace(buy.TradePartner.Name)
                ? buy.TradePartner.Name : "For sale";
            _title.Text = $"{who} ({_stock.Count})";
        }

        Total();
    }

    /// <summary>
    /// What the ticked rows come to. Buy::CalculateSum counts a stackable
    /// item as count times price and everything else as its price once.
    /// </summary>
    void Total()
    {
        long sum = 0;
        foreach (TradeOfferObject o in _stock)
        {
            if (o == null || !_ticked.Contains(o.ID)) continue;
            sum += o.IsStackable ? (long)o.Count * o.Price : o.Price;
        }
        _sum.Text = _ticked.Count == 0 ? "nothing picked" : $"{sum} for {_ticked.Count}";
        _ok.Disabled = _ticked.Count == 0;
    }

    Control Row(TradeOfferObject o)
    {
        TradeOfferObject captured = o;

        var tick = new CheckBox
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            // A row rebuilt while it was picked comes back picked.
            ButtonPressed = _ticked.Contains(o.ID),
            // Named so a scripted run can tick one: the row's text lives
            // in child labels, so there is nothing to find it by.
            Name = $"buy{o.ID}",
        };
        tick.AddThemeFontSizeOverride("font_size", FontSize);
        TickStyle.Apply(tick);
        tick.ButtonDown += () => _downAt = Time.GetTicksMsec();
        tick.Toggled += on =>
        {
            // Held describes rather than ticks, and puts the tick back.
            // Setting the property raises this again, hence the guard.
            if (_reverting) return;
            ulong down = _downAt;
            _downAt = 0;
            if (down != 0 && Time.GetTicksMsec() - down >= LongPressMs)
            {
                _reverting = true;
                tick.ButtonPressed = !on;
                _reverting = false;
                Look?.Invoke(captured.ID);
                return;
            }
            if (on) _ticked.Add(captured.ID); else _ticked.Remove(captured.ID);
            Total();
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = RowHeight; line.OffsetTop = 6;
        line.OffsetRight = -8; line.OffsetBottom = -6;
        tick.AddChild(line);

        line.AddChild(new TextureRect
        {
            Texture = Icon(o),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        uint argb = o.Flags != null ? NameColors.GetColorFor(o.Flags) : NameColors.NORMAL;
        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
        line.AddChild(name);

        if (o.IsStackable)
        {
            // How many, and a way to change it. The game gives a
            // stackable line its own edit box and writes what you type
            // back into the item (`UIBuy.cpp:240-258`), so you can buy
            // five of a hundred; we had a label, and the only amount you
            // could buy was the whole pile.
            //
            // A box to type in is the wrong control on a phone. The
            // amount prompt already exists for dropping part of a stack
            // and knows how to ask this question, so the row asks for
            // it instead.
            var many = new Button
            {
                Text = $"x{o.Count}",
                Name = $"many{o.ID}",
                TooltipText = "How many",
            };
            many.AddThemeFontSizeOverride("font_size", FontSize);
            many.Pressed += () => AmountWanted?.Invoke(o);
            line.AddChild(many);
        }
        // There is no second branch. IsStackable IS Count > 0
        // (`ObjectID.cs:202-205`), so an `else if (o.Count > 1)` behind
        // that test can never run: what stood here was a label for a
        // count that the branch above had already claimed. Deleted
        // rather than left looking like a case that is handled.

        var price = new Label
        {
            Text = o.Price.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(80, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        price.AddThemeFontSizeOverride("font_size", FontSize);
        price.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.4f));
        line.AddChild(price);

        return tick;
    }

    ImageTexture Icon(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        // Keyed on everything the composed picture depends on. The
        // file alone was not enough: two items sharing a BGF but dyed
        // differently compose to different pictures, and an item that
        // changes appearance kept the first icon it was ever drawn
        // with - where the game's composer re-pushes a texture every
        // time the object changes.
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}:{IconSize}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[BuyPanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
