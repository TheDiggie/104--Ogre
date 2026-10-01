using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// Making a character.
///
/// `UIAvatarCreateWizard.cpp`. Until now the mobile client could pick a
/// character and not make one, so a new account had nothing to log in
/// as - which is a strange thing for a client to be missing.
///
/// The server sends the whole palette: the hair and skin colours
/// available, the skull, hair, eye, nose and mouth art for each gender,
/// the spells and skills on offer with their costs, and the points to
/// spend. `SendSystemMessageSendCharInfo` asks for it, the data layer
/// fills `CharCreationInfo` and calls `SetExampleModel()`, and the
/// wizard follows that.
///
/// The face is not drawn part by part here. `SetExampleModel` takes
/// seven indices - gender, skin colour, hair colour, hair, eyes, nose,
/// mouth - and rebuilds `ExampleModel` as an object with the right
/// suboverlays hung off it, which is then composed exactly like any
/// other object in the world. So the portrait is the same code path as
/// a player standing in a room, and picking a nose is choosing an
/// index.
///
/// The rules again live in the model. `SelectSpell` and `SelectSkill`
/// return a `CharSelectAbilityError` rather than a bool, and each value
/// is its own sentence: out of points, you took a level two before
/// enough level ones, or - the interesting one - Qor and Shal'ille
/// refuse each other, because the two schools will not be learned by
/// the same person. Those come back as a popup, which is what the file
/// does with them.
///
/// The game lays this out as five tabs with Back and Next. That is a
/// mouse's way through a form; here it is one scrolling column, because
/// a phone scrolls better than it tabs.
/// </summary>
public partial class CreateCharacter : Control
{
    [Export] public int FontSize = 15;
    [Export] public int RowHeight = 40;
    [Export] public int PortraitSize = 120;

    /// <summary>Everything is filled in: make it.</summary>
    public event Action<string, string> Create;
    /// <summary>Something the model refused.</summary>
    public event Action<string> Complain;
    /// <summary>Backed out.</summary>
    public event Action Cancelled;

    ColorRect _panel;
    Panel _card, _bar, _faceBox;
    Label _title, _points, _pointsCap;
    TextureRect _face;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    LineEdit _name;
    TextEdit _description;
    Button _make, _close;

    CharCreationInfo _info;
    readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();
    Gender _gender = Gender.Male;
    int _skin, _hair, _hairStyle, _eyes, _nose, _mouth;
    string _signature = "";
    string _faceKey = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Opaque: there is no world behind the creation screen, only
        // the view's connection log, which must not read through it.
        _panel = new ColorRect
        {
            Color = new Color(M59Skin.Scrim.R, M59Skin.Scrim.G, M59Skin.Scrim.B),
            Visible = false,
        };
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("New character");
        _title.Visible = false;
        AddChild(_title);

        // The budget, in the card's own header rather than in the
        // scrolling column: it is the one number every choice below
        // spends, and a budget you have to scroll back to is a budget
        // you spend blind.
        _pointsCap = M59Skin.Caption("Points to spend");
        _pointsCap.Visible = false;
        AddChild(_pointsCap);
        _points = new Label { Visible = false };
        _points.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _points.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        AddChild(_points);

        // The face sits in a sunken frame beside the budget, so it
        // reads as a portrait rather than as art that came loose: it
        // used to float in the top-right corner outside everything.
        _faceBox = new Panel { Visible = false };
        _faceBox.AddThemeStyleboxOverride("panel", M59Skin.Sunken());
        AddChild(_faceBox);
        _face = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
        };
        AddChild(_face);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // See notes/godot-ui.md: without this the rows are only as wide
        // as their longest line and every value column lands wherever
        // that row's text ended.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _make = Push("Create", Finish, M59Skin.Kind.Primary);
        _close = Push("Cancel", () => { Show(false); Cancelled?.Invoke(); }, M59Skin.Kind.Secondary);

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

    /// <summary>The header strip: the portrait's side, so the budget beside it.</summary>
    float HeaderH => PortraitSize;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // As tall as the screen allows and no wider than a line of
        // prose wants to be: this form is mostly the wizard's own
        // sentences, and at 1620 points wide - which is what an
        // unbounded card is on a sideways phone - they are lines nobody
        // can follow back to the start. The old panel ran the full
        // width and put every value column a thousand points from the
        // name it belonged to.
        Rect2 card = M59Skin.Frame(v, 0f, true, CardW(v, M59Skin.Measure + M59Skin.Pad * 4f));
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        // Header: the budget on the left, the face on the right, both
        // inside the card and both fixed while the form scrolls.
        float faceW = PortraitSize;
        _faceBox.Position = new Vector2(body.Position.X + body.Size.X - faceW, body.Position.Y);
        _faceBox.Size = new Vector2(faceW, HeaderH);
        _face.Position = _faceBox.Position + new Vector2(6f, 6f);
        _face.Size = _faceBox.Size - new Vector2(12f, 12f);

        _pointsCap.Position = new Vector2(body.Position.X, body.Position.Y + 6f);
        _pointsCap.Size = new Vector2(body.Size.X - faceW - M59Skin.Gap, 20f);
        _points.Position = new Vector2(body.Position.X, body.Position.Y + 30f);
        _points.Size = new Vector2(body.Size.X - faceW - M59Skin.Gap, HeaderH - 36f);
        _points.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        float top = body.Position.Y + HeaderH + M59Skin.Gap;
        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(body.Size.X,
                                   Mathf.Max(M59Skin.RowH, body.Position.Y + body.Size.Y - top));
        // Less the scrollbar's own width: a column sized to the whole
        // viewport runs underneath the bar, and the right-hand end of
        // every field and every + button sat behind it.
        // The bar is wider than this file's own BarW now; the skin
        // owns that number. See M59Skin.RowsW.
        _rows.CustomMinimumSize = new Vector2(M59Skin.RowsW(body), 0);

        // Create last in the line, where the skin puts the one thing a
        // panel is for.
        M59Skin.FootRow(foot, _make, _close);
    }

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on; _points.Visible = on;
        _pointsCap.Visible = on; _card.Visible = on; _bar.Visible = on;
        _faceBox.Visible = on; _face.Visible = on; _scroll.Visible = on;
        _make.Visible = on; _close.Visible = on;
        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Open(CharCreationInfo info)
    {
        _info = info;
        Watch(info);
        if (info == null) return;

        _gender = Gender.Male;
        _skin = _hair = _hairStyle = _eyes = _nose = _mouth = 0;
        _signature = ""; _faceKey = "";
        Build();
        Reface();
        Show(true);
    }

    public void Close() => Show(false);

    // The refusal watch. The reference subscribes once when the wizard is
    // built and unsubscribes when it is destroyed
    // (`UIAvatarCreateWizard.cpp:98-99`, `:209-210`) and reads the flag in
    // OnCharCreationInfoPropertyChanged (:229). The object it watches is
    // the data layer's single long-lived CharCreationInfo
    // (`DataController.cs:950,345`), so the wizard owns the subscription
    // for exactly as long as it watches that object: one handler however
    // many times the wizard is opened, and gone with the node.
    CharCreationInfo _watched;

    void Watch(CharCreationInfo info)
    {
        if (ReferenceEquals(_watched, info)) return;
        if (_watched != null) _watched.PropertyChanged -= OnInfoChanged;
        _watched = info;
        if (_watched != null) _watched.PropertyChanged += OnInfoChanged;
    }

    void OnInfoChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != CharCreationInfo.PROPNAME_CHARINFONOTOKERROR) return;
        var info = sender as CharCreationInfo;
        if (info == null) return;
        CharInfoNotOkError why = info.CharInfoNotOkError;
        if (why == CharInfoNotOkError.NoError) return;
        // Cleared before it is shown, as the reference does once it has
        // read it (:411-490): the clear raises this event again and that
        // one returns on NoError above, so a second handler on the same
        // object - a leftover subscription elsewhere - reads NoError too
        // and stays quiet.
        info.CharInfoNotOkError = CharInfoNotOkError.NoError;
        Refused(why);
    }

    public override void _ExitTree() => Watch(null);

    void Build()
    {
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _values.Clear();
        _readers.Clear();
        _spellRows.Clear();
        _skillRows.Clear();
        _stripe.Clear();
        _taken.Clear();
        _alt = false;

        _rows.AddChild(Section("Who"));
        _rows.AddChild(M59Skin.Caption("Name"));
        _name = M59Skin.Field(new LineEdit
        {
            PlaceholderText = "name",
            CustomMinimumSize = new Vector2(0, M59Skin.RowH - 8f),
        });
        // The game's own cap (`UIAvatarCreateWizard.cpp:82`). Without
        // it an over-long name went to the server and came back as
        // NameTooLong - which, until now, was silence.
        _name.MaxLength = Meridian59.Common.Constants.BlakservStringLengths.MAX_CHAR_NAME_LEN;
        _name.Name = "charName";
        _rows.AddChild(_name);

        _rows.AddChild(M59Skin.Caption("Description"));
        _description = M59Skin.Field(new TextEdit
        {
            PlaceholderText = "description",
            CustomMinimumSize = new Vector2(0, RowHeight * 2.4f),
        });
        _description.Name = "charDescription";
        _rows.AddChild(_description);

        _rows.AddChild(Section("Face"));
        _rows.AddChild(Stepper("Gender", () => _gender == Gender.Male ? "male" : "female",
            () => Sex(Gender.Female), () => Sex(Gender.Male)));
        _rows.AddChild(Stepper("Skin", () => (_skin + 1).ToString(),
            () => Step(ref _skin, -1, Colors(true)), () => Step(ref _skin, 1, Colors(true))));
        _rows.AddChild(Stepper("Hair colour", () => (_hair + 1).ToString(),
            () => Step(ref _hair, -1, Colors(false)), () => Step(ref _hair, 1, Colors(false))));
        _rows.AddChild(Stepper("Hair", () => (_hairStyle + 1).ToString(),
            () => Step(ref _hairStyle, -1, Parts("hair")), () => Step(ref _hairStyle, 1, Parts("hair"))));
        _rows.AddChild(Stepper("Eyes", () => (_eyes + 1).ToString(),
            () => Step(ref _eyes, -1, Parts("eyes")), () => Step(ref _eyes, 1, Parts("eyes"))));
        _rows.AddChild(Stepper("Nose", () => (_nose + 1).ToString(),
            () => Step(ref _nose, -1, Parts("nose")), () => Step(ref _nose, 1, Parts("nose"))));
        _rows.AddChild(Stepper("Mouth", () => (_mouth + 1).ToString(),
            () => Step(ref _mouth, -1, Parts("mouth")), () => Step(ref _mouth, 1, Parts("mouth"))));

        _rows.AddChild(Section("Attributes"));
        // The three presets are the game's own, and they are a kindness
        // rather than a shortcut: a first character spent badly is a
        // character you stop playing.
        var presets = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        presets.AddThemeConstantOverride("separation", 8);
        presets.AddChild(Preset("Warrior", () => _info.SetAttributesToWarrior()));
        presets.AddChild(Preset("Mage", () => _info.SetAttributesToMage()));
        presets.AddChild(Preset("Hybrid", () => _info.SetAttributesToHybrid()));
        _rows.AddChild(presets);

        // The reference's hint, under the same heading it sits under
        // there (`Meridian59.layout:2760`). It is not decoration: the
        // school requirements are written in multiples of five, so a
        // character built in ones is a character a point short of things.
        _rows.AddChild(Prose(
            "Hint: It is a good idea to use multiples of 5 when you set your stats!"));

        // Each attribute now carries the wizard's own sentence about it.
        //
        // The layout gives all six a description apiece
        // (`Meridian59.layout:2657`, :2676, :2695, :2714, :2733, :2752)
        // and this client showed six steppers and nothing else - so the
        // one permanent decision in the whole client was being made
        // blind. Nothing on the screen said that Mysticism is what the
        // three mana schools are gated on, or that Intellect gates the
        // other two, so the player who put everything into Might met
        // those rules later as a refusal with no explanation.
        //
        // Brought across word for word rather than rewritten. These
        // sentences are the game telling you what its own numbers mean,
        // and they name specific schools and specific weapons; a summary
        // of them would be new content, and wrong content the first time
        // the game changed under it.
        //
        // Listed in this file's order rather than the layout's, which
        // reads down two columns - Might, Agility, Aim on the left and
        // Stamina, Mysticism, Intellect on the right - and is a
        // two-column layout's ordering, not a meaning.
        _rows.AddChild(Attribute("Might", () => _info.Might, v => _info.Might = v,
            "Might increases your weight limit and melee damage. Mighty warriors can "
            + "inflict great damage with maces, hammers or their bare hands."));
        _rows.AddChild(Attribute("Intellect", () => _info.Intellect, v => _info.Intellect = v,
            "An adventurer's intellect decides how much they can learn. Great intellect "
            + "is a necessity to master the schools of Jala, the Muse, and Riija the Trickster."));
        _rows.AddChild(Attribute("Stamina", () => _info.Stamina, v => _info.Stamina = v,
            "Your stamina affects your maximum health and allows you to master the arts "
            + "of Kraanan, the Fist, more easily."));
        _rows.AddChild(Attribute("Agility", () => _info.Agility, v => _info.Agility = v,
            "Agile fighters are less likely to get hit, master many weapon skills more "
            + "easily and inflict greater damage with the scimitar."));
        _rows.AddChild(Attribute("Mysticism", () => _info.Mysticism, v => _info.Mysticism = v,
            "Your mysticism determines your maximum mana and is needed for the magical "
            + "schools of Faren, the Fury, Shal'ille, the Compassionate and Qor, the Vile."));
        _rows.AddChild(Attribute("Aim", () => _info.Aim, v => _info.Aim = v,
            "True aim helps you to hit your targets and allows you to be highly "
            + "effective with long swords and bows."));

        _rows.AddChild(Section("Spells"));
        // `AvatarCreateWizard.SpellsSkillsDisclaimer`
        // (`Meridian59.layout:2817`), which is the only place in the
        // client either of these two rules is written down.
        //
        // They are both rules the model enforces by refusing - Reason()
        // below turns NotEnoughLevelOneError and the two school errors
        // into sentences - and a rule you only meet as a refusal is a
        // rule the player reverse-engineers. Worse here than on a
        // desktop: the desktop player had this paragraph under both
        // lists the whole time.
        //
        // Above the lists rather than below them, which is the one
        // departure. The reference can put it at the bottom because its
        // two lists and its disclaimer are all on screen at once; this is
        // a scrolling column, and text below a hundred abilities is text
        // read after the choosing.
        //
        // Verbatim, with one exception: the reference's "anta- gonists"
        // is a hyphen hand-placed to break that word across a
        // fixed-width CEGUI label, and reproducing it here would put a
        // hyphen in the middle of a line that Godot wraps for itself.
        _rows.AddChild(Prose(
            "To unlock a school's second rank of abilities, choose two or more abilities "
            + "of the first rank.  The schools of Shal'ille (good) and Qor (evil) are "
            + "natural antagonists and, thus, mutually exclusive."));
        if (_info.Spells != null)
        {
            int i = 0;
            foreach (AvatarCreatorSpellObject sp in _info.Spells)
                if (sp != null) _rows.AddChild(Ability(
                    $"spell{i++}", sp.SpellName, (int)sp.SpellCost, sp.ExtraID, true));
        }

        _rows.AddChild(Section("Skills"));
        if (_info.Skills != null)
        {
            int i = 0;
            foreach (AvatarCreatorSkillObject sk in _info.Skills)
                if (sk != null) _rows.AddChild(Ability(
                    $"skill{i++}", sk.SkillName, (int)sk.SkillCost, sk.ExtraID, false));
        }
    }

    /// <summary>
    /// A section of the form: the skin's gold heading with a rule under
    /// it, returned as one block so the rule cannot drift away from the
    /// name it underlines. Space above, none below, so a heading reads
    /// as belonging to what follows it.
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

    Button Preset(string text, Action pick)
    {
        var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = $"preset{text}" };
        M59Skin.Dress(b, M59Skin.Kind.Secondary);
        b.Pressed += () => { pick(); _signature = ""; };
        return b;
    }

    void Sex(Gender g)
    {
        if (_gender == g) return;
        _gender = g;
        // The file resets every face index when gender changes, because
        // the two sets are different lengths and index 7 of one is not
        // index 7 of the other.
        _hairStyle = _eyes = _nose = _mouth = 0;
        Reface();
    }

    void Step(ref int value, int by, int count)
    {
        if (count <= 0) { value = 0; Reface(); return; }
        value = Mathf.PosMod(value + by, count);
        Reface();
    }

    int Colors(bool skin)
    {
        byte[] a = skin ? _info?.SkinColors : _info?.HairColors;
        return a != null ? a.Length : 0;
    }

    int Parts(string which)
    {
        if (_info == null) return 0;
        bool male = _gender == Gender.Male;
        switch (which)
        {
            case "hair": return (male ? _info.MaleHairIDs : _info.FemaleHairIDs)?.Length ?? 0;
            case "eyes": return (male ? _info.MaleEyeIDs : _info.FemaleEyeIDs)?.Length ?? 0;
            case "nose": return (male ? _info.MaleNoseIDs : _info.FemaleNoseIDs)?.Length ?? 0;
            default: return (male ? _info.MaleMouthIDs : _info.FemaleMouthIDs)?.Length ?? 0;
        }
    }

    /// <summary>
    /// Rebuilds the example model from the seven indices, which is the
    /// one call the face costs.
    /// </summary>
    void Reface()
    {
        if (_info == null) return;
        _info.SetExampleModel(_gender, _skin, _hair, _hairStyle, _eyes, _nose, _mouth);
        _signature = "";
        _faceKey = "";
    }

    Control Stepper(string name, Func<string> read, Action down, Action up)
        => Row(name, read, down, up);

    /// <summary>
    /// One attribute: the stepper, and under it the wizard's own
    /// sentence about what the number does. The two are returned as one
    /// block so the text cannot drift away from the row it explains when
    /// the list is rebuilt.
    /// </summary>
    Control Attribute(string name, Func<uint> get, Action<uint> set, string about)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 0);
        block.AddChild(Row(name, () => get().ToString(),
            () => { set(get() - 1); _signature = ""; },
            () => { set(get() + 1); _signature = ""; }));
        block.AddChild(Prose(about));
        return block;
    }

    /// <summary>
    /// A paragraph of the wizard's own explanatory text. Wrapped and set
    /// smaller and dimmer than a row, so a screenful of these still reads
    /// as a form with notes on it rather than as a wall.
    /// </summary>
    Control Prose(string text)
    {
        var l = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        l.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        l.AddThemeColorOverride("font_color", M59Skin.TextDim);
        return l;
    }

    /// <summary>
    /// Which stripe the next row takes. A form of forty rows with
    /// paragraphs between them needs something to count down, and the
    /// skin's two row colours are it; reset per Build so the stripes
    /// start the same way every time.
    /// </summary>
    bool _alt;

    Control Row(string name, Func<string> read, Action down, Action up)
    {
        // On a striped panel, with the contents inset: the rows used to
        // be bare HBoxes on the background, so the whole form read as
        // loose text with buttons at the end of it.
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
            // A fixed column, so the numbers line down the form instead
            // of each landing where its own name ended.
            CustomMinimumSize = new Vector2(90, 0),
        };
        value.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        value.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        line.AddChild(value);
        _values[name] = value;
        _readers[name] = read;

        var less = new Button { Text = "-", Name = $"less{Slug(name)}" };
        M59Skin.Dress(less, M59Skin.Kind.Step);
        less.CustomMinimumSize = new Vector2(StepW, 0);
        less.Pressed += () => down();
        line.AddChild(less);

        var more = new Button { Text = "+", Name = $"more{Slug(name)}" };
        M59Skin.Dress(more, M59Skin.Kind.Step);
        more.CustomMinimumSize = new Vector2(StepW, 0);
        more.Pressed += () => up();
        line.AddChild(more);

        return holder;
    }

    /// <summary>A stepper wide enough for a thumb.</summary>
    const float StepW = 56f;

    /// <summary>What Godot's vertical scrollbar takes out of the width.</summary>
    const float BarW = 16f;

    readonly Dictionary<string, Func<string>> _readers = new Dictionary<string, Func<string>>();
    // Held rather than looked up by node name: a name search through a
    // rebuilt tree is one more thing that can quietly return nothing,
    // and it did.
    readonly Dictionary<uint, Button> _spellRows = new Dictionary<uint, Button>();
    readonly Dictionary<uint, Button> _skillRows = new Dictionary<uint, Button>();
    /// <summary>Which stripe each ability row is, so Pick can put it back.</summary>
    readonly Dictionary<Button, bool> _stripe = new Dictionary<Button, bool>();
    /// <summary>Each ability row's name label, which is what carries its colour.</summary>
    readonly Dictionary<Button, Label> _taken = new Dictionary<Button, Label>();

    /// <summary>
    /// One spell or skill. Tapping it takes it; tapping it again gives
    /// it back. The model answers with a reason when it will not.
    /// </summary>
    Control Ability(string node, string name, int cost, uint id, bool spell)
    {
        var b = new Button
        {
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, M59Skin.RowH - 8f),
            Name = node,
        };
        bool alt = _alt;
        _alt = !_alt;
        M59Skin.Dress(b, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
        _stripe[b] = alt;
        if (spell) _spellRows[id] = b; else _skillRows[id] = b;

        // The name and the cost as two columns inside the row, rather
        // than one string with spaces in it: a hundred abilities whose
        // price lands wherever the name ended is a column you cannot
        // read down. The labels take no presses, so the whole row is
        // still one target.
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", (int)M59Skin.Gap);
        line.OffsetLeft = M59Skin.Pad; line.OffsetRight = -M59Skin.Pad;
        b.AddChild(line);

        var label = new Label
        {
            Text = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        label.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(label);
        _taken[b] = label;

        var price = new Label
        {
            Text = cost.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(64, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        price.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        price.AddThemeColorOverride("font_color", M59Skin.Gold);
        line.AddChild(price);

        b.Pressed += () =>
        {
            if (_info == null) return;

            if (Has(id, spell))
            {
                if (spell) _info.DeselectSpell(id); else _info.DeselectSkill(id);
            }
            else
            {
                CharSelectAbilityError err = spell ? _info.SelectSpell(id) : _info.SelectSkill(id);
                if (err != CharSelectAbilityError.NoError) Complain?.Invoke(Reason(err));
            }
            _signature = "";
        };
        return b;
    }

    bool Has(uint id, bool spell)
    {
        if (spell)
        {
            if (_info.SelectedSpells == null) return false;
            foreach (AvatarCreatorSpellObject s in _info.SelectedSpells)
                if (s != null && s.ExtraID == id) return true;
            return false;
        }
        if (_info.SelectedSkills == null) return false;
        foreach (AvatarCreatorSkillObject s in _info.SelectedSkills)
            if (s != null && s.ExtraID == id) return true;
        return false;
    }

    /// <summary>
    /// The four refusals, each said the way the game says it. The two
    /// school ones are the reason this is an enum and not a bool: Qor
    /// and Shal'ille will not be learned by the same person.
    /// </summary>
    static string Reason(CharSelectAbilityError e)
    {
        switch (e)
        {
            case CharSelectAbilityError.NoPointsLeftError:
                return "You have no ability points left.";
            case CharSelectAbilityError.AlreadyHaveShalilleError:
                return "You already have a Shal'ille spell, so you cannot take a Qor one.";
            case CharSelectAbilityError.AlreadyHaveQorError:
                return "You already have a Qor spell, so you cannot take a Shal'ille one.";
            case CharSelectAbilityError.NotEnoughLevelOneError:
                return "You need more level one abilities before taking that one.";
            default:
                return "That cannot be taken.";
        }
    }

    public void Sync()
    {
        if (!IsOpen || _info == null) return;

        string now = $"{_info.Might},{_info.Intellect},{_info.Stamina},{_info.Agility},"
                   + $"{_info.Mysticism},{_info.Aim},{_info.AttributesAvailable},"
                   + $"{_info.SkillPointsAvailable},{_gender},{_skin},{_hair},"
                   + $"{_hairStyle},{_eyes},{_nose},{_mouth},"
                   + $"{_info.SelectedSpells?.Count},{_info.SelectedSkills?.Count}";
        if (now == _signature) return;
        _signature = now;

        // One number per line, each said in full: "70 attribute
        // points, 45 ability points left" read as one sentence with a
        // comma in it, and which number the "left" belonged to was
        // anyone's guess.
        _points.Text = $"{_info.AttributesAvailable} attribute points left\n"
                     + $"{_info.SkillPointsAvailable} ability points left";

        foreach (KeyValuePair<string, Func<string>> r in _readers)
            if (_values.TryGetValue(r.Key, out Label l)) l.Text = r.Value();

        Mark();
        Portrait();
    }

    /// <summary>What you have taken is marked, rather than moved to a second list.</summary>
    void Mark()
    {
        foreach (KeyValuePair<uint, Button> r in _spellRows) Tint(r.Value, Has(r.Key, true));
        foreach (KeyValuePair<uint, Button> r in _skillRows) Tint(r.Value, Has(r.Key, false));
    }

    /// <summary>
    /// Marks a taken ability the way this skin marks any chosen row -
    /// the lit fill and the gold edge down the left - instead of by
    /// turning the row's text green and flattening it. Which rows are
    /// marked is unchanged; only what the mark looks like is.
    ///
    /// The name is a child Label now, so the colour goes on the label:
    /// a font_color override on the Button would not reach it.
    /// </summary>
    void Tint(Button b, bool taken)
    {
        if (b == null) return;
        M59Skin.Pick(b, taken, _stripe.TryGetValue(b, out bool alt) && alt);
        if (_taken.TryGetValue(b, out Label name))
            name.AddThemeColorOverride("font_color", taken ? M59Skin.GoldBright : M59Skin.Text);
    }

    /// <summary>
    /// Turns the example model's resource ids into loaded art. Set by
    /// the view, which is what holds the resource manager.
    ///
    /// Nothing did this, so the face was never drawn: the model the
    /// library builds carries ids and no files, Portrait gave up on the
    /// null resource, and you picked a nose blind.
    /// </summary>
    public Action<ObjectBase> Resolve { get; set; }

    void Portrait()
    {
        ObjectBase model = _info?.ExampleModel;
        if (model == null) return;

        // The art has to be fetched before anything can be drawn: the
        // model the library builds carries resource ids and no files.
        try { Resolve?.Invoke(model); } catch { }

        // ObjectBase has no appearance hash - that lives on RoomObject -
        // so the key is what actually changed: the seven indices. Not
        // the resource's filename, which this model does not have: its
        // own overlay id is zero and every part hangs off a hotspot.
        string key = $"{_gender}:{_skin}:{_hair}:{_hairStyle}:{_eyes}:{_nose}:{_mouth}";
        if (key == _faceKey) return;
        _faceKey = key;

        // Composed from the HEAD hotspot, which is what the game does
        // (`UIAvatarCreateWizard.cpp:87`). It matters here more than
        // anywhere: this model has no body at all - its own overlay id
        // is zero and the five face parts hang off hotspots - so
        // composing it whole finds nothing to draw.
        try
        {
            _face.Texture = M59Assets.FromTex(
                M59Compose.Face(model, PortraitSize, (byte)KnownHotspot.HEAD));
        }
        catch (Exception e) { GD.PrintErr($"[CreateCharacter] face: {e.Message}"); }
    }

    void Finish()
    {
        if (_info == null) return;
        string name = _name.Text ?? "";
        if (name.Trim().Length == 0) { Complain?.Invoke("Your character needs a name."); return; }

        Create?.Invoke(name, _description.Text ?? "");
        // The window stays up until the server says yes. It used to
        // close here, hopefully, so "that name is taken" left an empty
        // screen, no message, and every choice you had made gone. The
        // server answers CharInfoOk - which enters the world and takes
        // this with it - or CharInfoNotOk, which Refused() explains.
    }

    /// <summary>
    /// The server's reason for refusing a new character, in its own
    /// words: EN_CHARINFONOTOKERROR_OKDIALOG (Language.cpp:74-90),
    /// which the reference shows in a popup and then clears
    /// (`UIAvatarCreateWizard.cpp:411-490`). Nothing here read the
    /// flag at all, so a refusal was silence.
    /// </summary>
    public void Refused(CharInfoNotOkError why)
    {
        if (why == CharInfoNotOkError.NoError) return;
        Complain?.Invoke(Excuse(why));
    }

    static string Excuse(CharInfoNotOkError why) => why switch
    {
        CharInfoNotOkError.NotFirstTime =>
            "That character slot is already in use. Try a different one.",
        CharInfoNotOkError.NameTooLong =>
            "Character names must be between 3 and 30 characters long, "
            + "or your name is already in use.",
        CharInfoNotOkError.NameBadCharacters => "Invalid character used in name.",
        CharInfoNotOkError.NameInUse => "Your character name is already taken by someone else.",
        CharInfoNotOkError.NoMobName => "You may not pick the name of a Meridian 59 monster.",
        CharInfoNotOkError.NoNPCName => "You may not pick the name of a Meridian 59 NPC.",
        CharInfoNotOkError.NoGuildName =>
            "You may not name your character after an existing Meridian 59 guild.",
        CharInfoNotOkError.NoBadWords =>
            "You may not use offensive language in your character name.",
        CharInfoNotOkError.NoConfusingName =>
            "Please pick another name - this one could cause confusion in game.",
        CharInfoNotOkError.NoRetiredName =>
            "Please pick another name: this one belongs to a former Meridian 59 "
            + "developer or staff member and is reserved for their future use.",
        CharInfoNotOkError.DescriptionTooLong =>
            "Player descriptions cannot be more than 1000 characters.",
        CharInfoNotOkError.InvalidGender =>
            "You must select either male or female when creating your character.",
        _ => "Character creation failed. Try again, and ask an admin if it keeps happening.",
    };

    static string Slug(string s) => s.Replace("'", "").Replace(" ", "");
}
