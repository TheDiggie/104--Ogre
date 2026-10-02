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
    Panel _card, _bar;
    Label _title, _points, _pointsCap;
    ProgressBar _budget;
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

        // The scrim, not an opaque sheet: unlike the login and creation
        // screens this one opens over the world, and the room behind it
        // is where the character being changed is standing.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Stat change");
        _title.Visible = false;
        AddChild(_title);

        // The budget in the card's header, out of the scrolling list:
        // every stepper below spends it, and a number you have to
        // scroll back to is a number you spend blind.
        _pointsCap = M59Skin.Caption("Attribute points");
        _pointsCap.Visible = false;
        AddChild(_pointsCap);
        _points = new Label { Visible = false };
        _points.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _points.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        AddChild(_points);
        // The pool as a bar as well as a number. The game spends these
        // points on progress bars you drag (`UIStatChangeWizard.cpp`),
        // and "25 / 220 left" alone never said which way it was going.
        _budget = M59Skin.Bar();
        _budget.Visible = false;
        _budget.MinValue = 0;
        _budget.MaxValue = StatChangeInfo.ATTRIBUTE_MAXSUM;
        AddChild(_budget);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // See notes/godot-ui.md: without this the rows are only as wide
        // as their longest line and every value column lands wherever
        // that row's text ended.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _ok = Push("OK", Ok, M59Skin.Kind.Primary);
        _close = Push("Close", Close, M59Skin.Kind.Secondary);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Push(string text, Action pressed, M59Skin.Kind kind)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }


    /// <summary>
    /// How wide the card should ask to be.
    ///
    /// Frame caps the width, and for a prompt-shaped card the cap is a
    /// fixed number of points - which is right on a sideways phone and
    /// wrong on an upright one, where the viewport is as wide as the
    /// landscape one (the project stretches canvas items and expands
    /// the aspect, so a portrait window grows the HEIGHT and keeps
    /// X at 1920) and a 560-point card is a third of the glass with
    /// nothing either side of it. Held tall, the card takes the screen.
    ///
    /// M59Skin could grow this; Frame's wantW is the place for it.
    /// </summary>
    static float CardW(Vector2 v, float wide)
        => v.Y > v.X ? Mathf.Max(wide, v.X * 0.9f) : wide;

    /// <summary>The header strip: a caption, the number, and the bar under it.</summary>
    const float HeaderH = 78f;
    /// <summary>What Godot's vertical scrollbar takes out of the width.</summary>
    const float BarW = 16f;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Bounded on both axes and centred. The old panel was pinned to
        // the bottom of the screen and ran nearly its full width, so a
        // row's name and its number were a thousand points apart.
        Rect2 card = M59Skin.Frame(v, 0f, true, CardW(v, M59Skin.Measure + M59Skin.Pad * 4f));
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        _pointsCap.Position = new Vector2(body.Position.X, body.Position.Y);
        _pointsCap.Size = new Vector2(body.Size.X, 20f);
        _points.Position = new Vector2(body.Position.X, body.Position.Y + 22f);
        _points.Size = new Vector2(body.Size.X, 28f);
        _budget.Position = new Vector2(body.Position.X, body.Position.Y + 56f);
        _budget.Size = new Vector2(body.Size.X, 10f);

        float top = body.Position.Y + HeaderH;
        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(body.Size.X,
                                   Mathf.Max(M59Skin.RowH, body.Position.Y + body.Size.Y - top));
        // Less the scrollbar, or the right-hand end of every + button
        // sits behind it.
        // The bar is wider than this file's own BarW now; the skin
        // owns that number. See M59Skin.RowsW.
        M59Skin.RowsFit(_rows, body);

        // OK last in the line, where the skin puts the one thing a
        // panel is for.
        M59Skin.FootRow(foot, _ok, _close);
    }

    public void Close()
    {
        if (_info != null) _info.IsVisible = false;
        Show(false);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _points.Visible = on;
        _pointsCap.Visible = on; _budget.Visible = on;
        _card.Visible = on; _bar.Visible = on;
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
            _alt = false;

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
        // Said in words, with the two numbers kept apart. "25 / 220
        // left   intellect needed: 1" read as one number over another
        // and left you guessing which of the two was the budget.
        _points.Text = $"{info.AttributesAvailable} left to spend of "
                     + $"{StatChangeInfo.ATTRIBUTE_MAXSUM}   -   intellect needed: {info.IntellectNeeded}";
        // The bar fills with what is SPENT, so an empty bar is an
        // unspent pool and a full one is a character with nothing left.
        _budget.Value = StatChangeInfo.ATTRIBUTE_MAXSUM - info.AttributesAvailable;
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

    /// <summary>
    /// A section of the form: the skin's gold heading with a rule under
    /// it, as one block so the rule cannot drift from the name it
    /// underlines.
    /// </summary>
    Control Section(string text)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 4);
        block.AddChild(new Control { CustomMinimumSize = new Vector2(0, M59Skin.Gap) });
        Label l = M59Skin.Heading(text);
        l.AddThemeFontSizeOverride("font_size", M59Skin.BodySize + 2);
        block.AddChild(l);
        ColorRect rule = M59Skin.Hairline();
        rule.CustomMinimumSize = new Vector2(0, 1);
        block.AddChild(rule);
        return block;
    }

    Control Attribute(string name, Func<byte> get, Action<byte> set)
        => Row(name, () => $"{get()} / {StatChangeInfo.ATTRIBUTE_MAXVALUE}",
               () => set((byte)(get() - 1)), () => set((byte)(get() + 1)));

    Control School(string name, Func<byte> get, Func<byte> orig, Action<byte> set)
        => Row(name, () => orig() > 0 ? $"{get()} / {orig()}" : "0",
               () => { if (get() > 0) set((byte)(get() - 1)); },
               () => set((byte)(get() + 1)));

    /// <summary>Which stripe the next row takes, so a long list keeps its place.</summary>
    bool _alt;
    /// <summary>A stepper wide enough for a thumb.</summary>
    const float StepW = 56f;

    Control Row(string name, Func<string> read, Action down, Action up)
    {
        // On a striped panel with its contents inset: the rows were
        // bare HBoxes on the background, so thirteen of them read as
        // loose text with buttons at the end.
        var holder = new PanelContainer();
        holder.AddThemeStyleboxOverride("panel", M59Skin.Stripe(_alt));
        _alt = !_alt;

        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", (int)M59Skin.Pad);
        pad.AddThemeConstantOverride("margin_right", 6);
        pad.AddThemeConstantOverride("margin_top", 4);
        pad.AddThemeConstantOverride("margin_bottom", 4);
        holder.AddChild(pad);

        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, M59Skin.RowH - 8f) };
        line.AddThemeConstantOverride("separation", (int)M59Skin.Gap);
        pad.AddChild(line);

        var label = new Label
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        label.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(label);

        var value = new Label
        {
            Text = read(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            // A fixed column, so the numbers line down the list.
            CustomMinimumSize = new Vector2(96, 0),
        };
        value.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        value.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        line.AddChild(value);
        _values[name] = value;

        // The model refuses anything it should, so these just ask. A
        // refusal looks like nothing happening, which is what dragging
        // a progress bar past its limit did in the game.
        var less = new Button { Text = "-", Name = $"less{Slug(name)}" };
        M59Skin.Dress(less, M59Skin.Kind.Step);
        less.CustomMinimumSize = new Vector2(StepW, 0);
        less.Pressed += () => { down(); _signature = ""; };
        line.AddChild(less);

        var more = new Button { Text = "+", Name = $"more{Slug(name)}" };
        M59Skin.Dress(more, M59Skin.Kind.Step);
        more.CustomMinimumSize = new Vector2(StepW, 0);
        more.Pressed += () => { up(); _signature = ""; };
        line.AddChild(more);

        return holder;
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
