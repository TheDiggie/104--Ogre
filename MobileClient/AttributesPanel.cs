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
    Label _title;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    readonly List<(Label name, ProgressBar bar, Label value)> _widgets =
        new List<(Label, ProgressBar, Label)>();
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Me" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Attributes", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);

        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _close = new Button { Text = "Close", Visible = false };
        _close.AddThemeFontSizeOverride("font_size", FontSize);
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
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _close.Visible = on; _open.Visible = !on;
        Layout();
    }

    /// <summary>Where the open button sits. Set by the view.</summary>
    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(64, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 64, v.Y - ButtonBottom - 40);

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.66f, 620f);
        float top = v.Y - height - side;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _scroll.Position = new Vector2(side, top + FontSize * 2.2f);
        _scroll.Size = new Vector2(v.X - side * 2f, height - FontSize * 2.2f - rowH - 16f);
        _rows.CustomMinimumSize = new Vector2(_scroll.Size.X, 0);

        _close.Position = new Vector2(side, top + height - rowH);
        _close.Size = new Vector2(v.X - side * 2f, rowH);
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
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (StatNumeric a in attributes) sb.Append(a?.ResourceName).Append(';');
        string now = sb.ToString();

        if (now != _signature)
        {
            _signature = now;
            _widgets.Clear();
            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            foreach (StatNumeric a in attributes)
                if (a != null) _rows.AddChild(Row(a));
            _title.Text = $"Attributes ({attributes.Count})";
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
        }
    }

    Control Row(StatNumeric a)
    {
        var box = new Control { CustomMinimumSize = new Vector2(0, RowHeight) };

        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(a.ResourceName) ? $"stat {a.Num}" : a.ResourceName,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", new Color(0.86f, 0.86f, 0.9f));
        name.Position = new Vector2(0, 0);
        box.AddChild(name);

        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.SetAnchorsPreset(LayoutPreset.BottomWide);
        bar.OffsetTop = -(RowHeight * 0.42f);
        bar.OffsetBottom = 0;
        box.AddChild(bar);

        var value = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        value.AddThemeFontSizeOverride("font_size", FontSize);
        value.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.6f));
        value.SetAnchorsPreset(LayoutPreset.TopWide);
        box.AddChild(value);

        _widgets.Add((name, bar, value));
        return box;
    }
}
