using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// Your character: might, stamina and the rest.
///
/// `UIAttributes.cpp` is a list over `Data->AvatarAttributes`, one row
/// per attribute, each a name and a bar. The bar's fill is the same
/// formula the condition bars use and is worth copying exactly rather
/// than assuming it is current over maximum:
///
///     range = ValueRenderMax - ValueRenderMin
///     fill  = ValueCurrent   - ValueRenderMin
///     perc  = fill / max(range, 1)
///
/// - so a stat the server renders on a 0-100 scale reads the same
/// whatever its own maximum happens to be, and the division can never
/// be by zero. The number printed on the bar is `ValueCurrent`, not the
/// percentage.
///
/// Names come from the server as string resources, so they are whatever
/// this server calls them; nothing here has a list of attribute names
/// built in.
/// </summary>
public partial class AttributesPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 44;

    /// <summary>Raised when opened, to ask the server for a fresh set.</summary>
    public event Action Opened;

    Button _open;
    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title;
    Label _empty;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    /// <summary>
    /// The four things a row draws. The maximum joined them because a
    /// bar with a number at one end and nothing to measure it against
    /// says only "some of something".
    /// </summary>
    readonly List<(Label name, ProgressBar bar, Label value, Label max)> _widgets =
        new List<(Label, ProgressBar, Label, Label)>();
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Stats" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open, "Your character", 40);

        // The scrim eats the touch that would reach the world behind;
        // the card is opaque, which the old 0.95 panel was not.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Attributes");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        _empty = M59Skin.Empty("The server has not sent your attributes yet.");
        _empty.Visible = false;
        AddChild(_empty);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        // Or the list is only as wide as its longest attribute name and
        // every bar after it ends somewhere different - see
        // notes/godot-ui.md, "A ScrollContainer sizes its child to that
        // child's minimum".
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _close = new Button { Text = "Close", Visible = false };
        M59Skin.Dress(_close, M59Skin.Kind.Secondary);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public void Open()
    {
        Show(true);
        Opened?.Invoke();
        _signature = "";
    }

    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _close.Visible = on; _open.Visible = !on;
        if (!on) _empty.Visible = false;
        Layout();
    }

    /// <summary>Where the open button sits. Set by the view.</summary>

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;


        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Sized to the attributes there are. Six of them used to leave
        // three hundred pixels of black under the last bar with the
        // Close button alone at the bottom of it.
        float rowH = Mathf.Max(RowHeight, M59Skin.RowH);
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        // Narrow as well as short: a name, a bar and a number do not
        // want the whole screen between them. See M59Skin.ListW.
        Rect2 card = M59Skin.Frame(v, shown * (rowH + 6f), true, M59Skin.ListW);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        _scroll.Position = body.Position;
        _scroll.Size = body.Size;
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        M59Skin.RowsFit(_rows, body);
        _empty.Position = body.Position;
        _empty.Size = body.Size;

        // Close at the right of the footer. It is the way out, not the
        // point of the panel.
        M59Skin.FootRow(foot, _close);
    }

    /// <summary>
    /// Follows the client's own attribute list. Rebuilt only when the set
    /// changes; the values are written into the existing rows every time,
    /// because a stat moving is not a reason to throw the row away.
    /// </summary>
    public void Sync(StatNumericList attributes)
    {
        if (_rows == null || !IsOpen) return;

        if (attributes == null || attributes.Count == 0)
        {
            _title.Text = "Attributes (none yet)";
            // A sentence rather than an empty box - the old panel showed
            // six hundred pixels of nothing and said so only in the title.
            _empty.Visible = true;
            return;
        }
        _empty.Visible = false;

        var sb = Sig.Start();
        foreach (StatNumeric a in attributes) sb.Append(a?.ResourceName).Append(';');
        if (Sig.Changed(sb, ref _signature))
        {
            _widgets.Clear();
            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            int index = 0;
            foreach (StatNumeric a in attributes)
                if (a != null) _rows.AddChild(Row(a, index++ % 2 == 1));
            _title.Text = $"Attributes ({attributes.Count})";
            // The card is sized to the row count, so a set that just
            // changed length needs the frame measured again.
            Layout();
        }

        int i = 0;
        foreach (StatNumeric a in attributes)
        {
            if (a == null || i >= _widgets.Count) { i++; continue; }
            var w = _widgets[i++];

            // UIAttributes.cpp, AttributeChange: the fill is measured
            // between the render bounds, not against the maximum.
            int range = a.ValueRenderMax - a.ValueRenderMin;
            int fill = a.ValueCurrent - a.ValueRenderMin;
            w.bar.Value = Mathf.Clamp((double)fill / Math.Max(range, 1), 0.0, 1.0) * 100.0;
            w.value.Text = a.ValueCurrent.ToString();
            // The scale the fill is measured against, printed beside the
            // value so the bar is readable as a proportion of something
            // rather than as an unlabelled stripe. The number on the row
            // is still ValueCurrent, as the class comment says.
            w.max.Text = $"/ {a.ValueRenderMax}";
        }
    }

    /// <summary>
    /// One attribute, as a single aligned line: the name in a fixed
    /// column, the bar taking whatever is left, then the value and the
    /// scale it is measured against in fixed columns of their own.
    ///
    /// It used to be three children anchored over one another - the name
    /// at the top left, the value at the top RIGHT and the bar across
    /// the bottom - so a row read as two unrelated things stacked, and
    /// nothing lined up down the list. A row is one line now.
    /// </summary>
    Control Row(StatNumeric a, bool alt)
    {
        float rowH = Mathf.Max(RowHeight, M59Skin.RowH);

        // The stripe is painted by the row's own background rather than
        // by a Button, because nothing here is tappable: an attribute is
        // read, not pressed.
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, rowH) };
        box.AddThemeStyleboxOverride("panel", Stripe(alt));

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", (int)M59Skin.Gap);
        box.AddChild(line);

        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(a.ResourceName) ? $"stat {a.Num}" : a.ResourceName,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(220, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(name);

        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 16),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // Godot's default ProgressBar is a grey slab on a grey slab,
        // which is what made six bars read as six smudges. M59Skin has
        // no bar of its own, so the two styleboxes are built here from
        // its colours.
        bar.AddThemeStyleboxOverride("background", Trough());
        bar.AddThemeStyleboxOverride("fill", Fill());
        line.AddChild(bar);

        var value = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(64, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        value.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        value.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        line.AddChild(value);

        var max = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(72, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        max.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        max.AddThemeColorOverride("font_color", M59Skin.TextDim);
        line.AddChild(max);

        _widgets.Add((name, bar, value, max));
        return box;
    }

    /// <summary>The row's own tint, alternating down the list.</summary>
    static StyleBoxFlat Stripe(bool alt)
    {
        var s = new StyleBoxFlat
        {
            BgColor = alt ? M59Skin.RowAlt : M59Skin.Row,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            AntiAliasing = true,
        };
        return s;
    }

    static StyleBoxFlat Trough()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.047f, 0.043f, 0.039f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
        };
        return s;
    }

    static StyleBoxFlat Fill()
    {
        var s = new StyleBoxFlat
        {
            BgColor = M59Skin.Gold,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            AntiAliasing = true,
        };
        return s;
    }
}
