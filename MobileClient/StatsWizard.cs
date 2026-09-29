using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Spending a stat change: six attributes and seven schools.
///
/// `UIStatChangeWizard.cpp`. The server offers this - a
/// ReqStatChange message fills `Data.StatChangeInfo` and sets
/// IsVisible - and until now the mobile client had nowhere to put it,
/// so the offer arrived and vanished. On a character that has earned
/// one, that is the whole of the reward gone.
///
/// Almost none of the rules are in the view, and that is the point of
/// reading the model first. `StatChangeInfo`'s setters refuse anything
/// they should:
///
///  - an attribute must stay between the minimum and 50, and may only
///    go up while there are points left: `value &lt; might ||
///    AttributesAvailable + might - value &gt;= 0`.
///  - points available is not a field. It is 220 minus the six
///    attributes added up, recomputed on every change.
///  - intellect has a floor that moves. Setting it below
///    IntellectNeeded silently sets it to IntellectNeeded instead,
///    because the schools you know cost intellect to keep -
///    `(TotalLearnPoints - 16) * 5 / 2`. So lowering schools is what
///    frees intellect, not the other way round.
///  - a school may not go above the level you arrived with
///    (`value &lt;= origLevel`). You can give levels up and take them
///    back, and that is all.
///
/// So the buttons here just ask, and a refusal shows up as nothing
/// happening - which is exactly what the game's drag on a progress bar
/// does. The bars are the game's too: an attribute reads against 50, a
/// school against the level you started with, and a school you never
/// studied reads "0" rather than "0 / 0".
///
/// OK is the one piece of real logic, and it is the file's: if
/// intellect is below what the schools need, it says so and sends
/// nothing; otherwise it asks "Are you sure you want to change your
/// stats?" and sends on yes. The mouse wheel and the drag-along-the-bar
/// are replaced by a minus and a plus, which is the same operation a
/// finger can hit.
/// </summary>
public partial class StatsWizard : Control
{
    [Export] public int FontSize = 15;
    [Export] public int RowHeight = 40;

    /// <summary>Everything checked out: send the change.</summary>
    public event Action Apply;
    /// <summary>Ask before sending, and say so when it cannot be sent.</summary>
    public event Action<string, Action> Confirm;
    public event Action<string> Complain;

    ColorRect _panel;
    Label _title, _points;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _ok, _close;

    StatChangeInfo _info;
    string _signature = "";
    readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Stat change", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _points = Heading("", FontSize + 1, new Color(1, 0.86f, 0.4f));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _ok = Push("OK", Ok);
        _close = Push("Close", Close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    Button Push(string text, Action pressed)
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
        float height = Mathf.Min(v.Y * 0.82f, 820f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.8f;
        _points.Position = new Vector2(side, y); y += FontSize * 2f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, top + height - rowH - 8f - y);
        _rows.CustomMinimumSize = new Vector2(w, 0);

        float by = top + height - rowH;
        float each = (w - 8f) * 0.5f;
        _ok.Position = new Vector2(side, by);
        _ok.Size = new Vector2(each, rowH);
        _close.Position = new Vector2(side + each + 8f, by);
        _close.Size = new Vector2(each, rowH);
    }

    public void Close()
    {
        if (_info != null) _info.IsVisible = false;
        Show(false);
    }

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on; _points.Visible = on;
        _scroll.Visible = on; _ok.Visible = on; _close.Visible = on;
        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Sync(StatChangeInfo info)
    {
        if (_rows == null) return;

        if (info == null || !info.IsVisible)
        {
            if (IsOpen) { _info = info; Show(false); _signature = ""; }
            return;
        }

        bool first = _info != info || _rows.GetChildCount() == 0;
        _info = info;
        if (!IsOpen) Show(true);

        if (first)
        {
            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            _values.Clear();

            _rows.AddChild(Section("Attributes"));
            _rows.AddChild(Attribute("Might", () => info.Might, v => info.Might = v));
            _rows.AddChild(Attribute("Intellect", () => info.Intellect, v => info.Intellect = v));
            _rows.AddChild(Attribute("Stamina", () => info.Stamina, v => info.Stamina = v));
            _rows.AddChild(Attribute("Agility", () => info.Agility, v => info.Agility = v));
            _rows.AddChild(Attribute("Mysticism", () => info.Mysticism, v => info.Mysticism = v));
            _rows.AddChild(Attribute("Aim", () => info.Aim, v => info.Aim = v));

            _rows.AddChild(Section("Schools"));
            _rows.AddChild(School("Shal'ille", () => info.LevelSha, () => info.OrigLevelSha, v => info.LevelSha = v));
            _rows.AddChild(School("Qor", () => info.LevelQor, () => info.OrigLevelQor, v => info.LevelQor = v));
            _rows.AddChild(School("Kraanan", () => info.LevelKraanan, () => info.OrigLevelKraanan, v => info.LevelKraanan = v));
            _rows.AddChild(School("Faren", () => info.LevelFaren, () => info.OrigLevelFaren, v => info.LevelFaren = v));
            _rows.AddChild(School("Riija", () => info.LevelRiija, () => info.OrigLevelRiija, v => info.LevelRiija = v));
            _rows.AddChild(School("Jala", () => info.LevelJala, () => info.OrigLevelJala, v => info.LevelJala = v));
            _rows.AddChild(School("Weaponcraft", () => info.LevelWC, () => info.OrigLevelWC, v => info.LevelWC = v));
        }

        string now = $"{info.Might},{info.Intellect},{info.Stamina},{info.Agility},{info.Mysticism},{info.Aim}|"
                   + $"{info.LevelSha},{info.LevelQor},{info.LevelKraanan},{info.LevelFaren},"
                   + $"{info.LevelRiija},{info.LevelJala},{info.LevelWC}";
        if (!first && now == _signature) return;
        _signature = now;

        // Points available is derived, not stored: 220 less the six
        // attributes. It moves on every change, which is why it is
        // redrawn here rather than set once.
        _points.Text = $"{info.AttributesAvailable} / {StatChangeInfo.ATTRIBUTE_MAXSUM} left"
                     + $"    intellect needed: {info.IntellectNeeded}";
        Redraw();
    }

    void Redraw()
    {
        if (_info == null) return;
        Set("Might", _info.Might, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);
        Set("Intellect", _info.Intellect, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);
        Set("Stamina", _info.Stamina, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);
        Set("Agility", _info.Agility, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);
        Set("Mysticism", _info.Mysticism, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);
        Set("Aim", _info.Aim, (int)StatChangeInfo.ATTRIBUTE_MAXVALUE);

        SetSchool("Shal'ille", _info.LevelSha, _info.OrigLevelSha);
        SetSchool("Qor", _info.LevelQor, _info.OrigLevelQor);
        SetSchool("Kraanan", _info.LevelKraanan, _info.OrigLevelKraanan);
        SetSchool("Faren", _info.LevelFaren, _info.OrigLevelFaren);
        SetSchool("Riija", _info.LevelRiija, _info.OrigLevelRiija);
        SetSchool("Jala", _info.LevelJala, _info.OrigLevelJala);
        SetSchool("Weaponcraft", _info.LevelWC, _info.OrigLevelWC);
    }

    void Set(string name, int value, int max)
    {
        if (_values.TryGetValue(name, out Label l)) l.Text = $"{value} / {max}";
    }

    /// <summary>A school you never studied reads "0", not "0 / 0".</summary>
    void SetSchool(string name, int value, int orig)
    {
        if (_values.TryGetValue(name, out Label l))
            l.Text = orig > 0 ? $"{value} / {orig}" : "0";
    }

    Control Section(string text)
    {
        var l = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, RowHeight * 0.9f),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize + 2);
        l.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        return l;
    }

    Control Attribute(string name, Func<byte> get, Action<byte> set)
        => Row(name, () => $"{get()} / {StatChangeInfo.ATTRIBUTE_MAXVALUE}",
               () => set((byte)(get() - 1)), () => set((byte)(get() + 1)));

    Control School(string name, Func<byte> get, Func<byte> orig, Action<byte> set)
        => Row(name, () => orig() > 0 ? $"{get()} / {orig()}" : "0",
               () => { if (get() > 0) set((byte)(get() - 1)); },
               () => set((byte)(get() + 1)));

    Control Row(string name, Func<string> read, Action down, Action up)
    {
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        line.AddThemeConstantOverride("separation", 8);

        var label = new Label
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", new Color(0.86f, 0.88f, 0.92f));
        line.AddChild(label);

        var value = new Label
        {
            Text = read(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(90, 0),
        };
        value.AddThemeFontSizeOverride("font_size", FontSize);
        value.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        line.AddChild(value);
        _values[name] = value;

        // The model refuses anything it should, so these just ask. A
        // refusal looks like nothing happening, which is what dragging
        // a progress bar past its limit did in the game.
        var less = new Button { Text = "-", Name = $"less{Slug(name)}" };
        less.AddThemeFontSizeOverride("font_size", FontSize + 2);
        less.CustomMinimumSize = new Vector2(52, 0);
        less.Pressed += () => { down(); _signature = ""; };
        line.AddChild(less);

        var more = new Button { Text = "+", Name = $"more{Slug(name)}" };
        more.AddThemeFontSizeOverride("font_size", FontSize + 2);
        more.CustomMinimumSize = new Vector2(52, 0);
        more.Pressed += () => { up(); _signature = ""; };
        line.AddChild(more);

        return line;
    }

    static string Slug(string s) => s.Replace("'", "").Replace(" ", "");

    /// <summary>
    /// `OnButtonOKClicked`: below the intellect the schools need, say so
    /// and send nothing. Otherwise ask, and send on yes.
    /// </summary>
    void Ok()
    {
        if (_info == null) return;

        if (_info.Intellect < _info.IntellectNeeded)
        {
            Complain?.Invoke("Invalid intellect for number of schools!");
            return;
        }

        Confirm?.Invoke("Are you sure you want to change your stats?", () =>
        {
            Apply?.Invoke();
            Close();
        });
    }
}
