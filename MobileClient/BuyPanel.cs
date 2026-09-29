using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

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
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 40;
    [Export] public int RowHeight = 56;

    /// <summary>Buy these, from the merchant the server named.</summary>
    public event Action<List<TradeOfferObject>> Buy;

    ColorRect _panel;
    Label _title, _sum;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _ok, _close;

    readonly HashSet<uint> _ticked = new HashSet<uint>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    readonly List<TradeOfferObject> _stock = new List<TradeOfferObject>();
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "For sale", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);

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

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.66f, 640f);
        float top = v.Y - height - side;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _scroll.Position = new Vector2(side, top + FontSize * 2.2f);
        _scroll.Size = new Vector2(v.X - side * 2f, height - FontSize * 2.2f - rowH * 2f - 20f);

        _sum.Position = new Vector2(side, top + height - rowH * 2f - 6f);
        _sum.Size = new Vector2(v.X - side * 2f, rowH);

        float y = top + height - rowH;
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
    public void Dismiss()
    {
        if (_buy != null) { _buy.IsVisible = false; _buy.Clear(true); }
        Close();
    }

    public void Close()
    {
        Show(false);
        _ticked.Clear();
        _signature = "";
    }

    void Show(bool on)
    {
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

        if (buy == null || !buy.IsVisible || buy.Items == null || buy.Items.Count == 0)
        {
            if (IsOpen) Close();
            return;
        }

        if (!IsOpen) Show(true);

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
                if (o != null) { _stock.Add(o); _rows.AddChild(Row(o)); }

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
        tick.Toggled += on =>
        {
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

        if (o.Count > 1)
        {
            var many = new Label
            {
                Text = $"x{o.Count}",
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            many.AddThemeFontSizeOverride("font_size", FontSize);
            many.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
            line.AddChild(many);
        }

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
