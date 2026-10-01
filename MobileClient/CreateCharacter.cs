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
    Label _title, _points;
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

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.98f), Visible = false };
        AddChild(_panel);

        _title = Heading("New character", FontSize + 5, new Color(1, 0.92f, 0.6f));
        _points = Heading("", FontSize, new Color(1, 0.86f, 0.4f));

        _face = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
        };
        AddChild(_face);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _make = Push("Create", Finish);
        _close = Push("Cancel", () => { Show(false); Cancelled?.Invoke(); });

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

        float side = Panels.Side(v, 0.05f);
        float rowH = FontSize * 2.6f;
        float height = v.Y - side;
        float top = side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 8f);
        _panel.Size = new Vector2(v.X - side, height);

        float y = top;
        _title.Position = new Vector2(side, y);
        _face.Position = new Vector2(v.X - side - PortraitSize, y);
        _face.Size = new Vector2(PortraitSize, PortraitSize);
        y += FontSize * 2f;
        _points.Position = new Vector2(side, y);
        y += Mathf.Max(FontSize * 2f, PortraitSize - FontSize * 2f);

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, top + height - rowH - 20f - y);
        _rows.CustomMinimumSize = new Vector2(w, 0);

        float by = top + height - rowH - 12f;
        float each = (w - 8f) * 0.5f;
        _make.Position = new Vector2(side, by);
        _make.Size = new Vector2(each, rowH);
        _close.Position = new Vector2(side + each + 8f, by);
        _close.Size = new Vector2(each, rowH);
    }

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on; _points.Visible = on;
        _face.Visible = on; _scroll.Visible = on;
        _make.Visible = on; _close.Visible = on;
        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Open(CharCreationInfo info)
    {
        _info = info;
        if (info == null) return;

        _gender = Gender.Male;
        _skin = _hair = _hairStyle = _eyes = _nose = _mouth = 0;
        _signature = ""; _faceKey = "";
        Build();
        Reface();
        Show(true);
    }

    public void Close() => Show(false);

    void Build()
    {
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _values.Clear();
        _readers.Clear();
        _spellRows.Clear();
        _skillRows.Clear();

        _rows.AddChild(Section("Who"));
        _name = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(0, RowHeight) };
        // The game's own cap (`UIAvatarCreateWizard.cpp:82`). Without
        // it an over-long name went to the server and came back as
        // NameTooLong - which, until now, was silence.
        _name.MaxLength = Meridian59.Common.Constants.BlakservStringLengths.MAX_CHAR_NAME_LEN;
        _name.AddThemeFontSizeOverride("font_size", FontSize);
        _name.Name = "charName";
        _rows.AddChild(_name);

        _description = new TextEdit
        {
            PlaceholderText = "description",
            CustomMinimumSize = new Vector2(0, RowHeight * 2.4f),
        };
        _description.AddThemeFontSizeOverride("font_size", FontSize);
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

    Control Section(string text)
    {
        var l = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, RowHeight),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize + 3);
        l.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        return l;
    }

    Button Preset(string text, Action pick)
    {
        var b = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = $"preset{text}" };
        b.AddThemeFontSizeOverride("font_size", FontSize);
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
        l.AddThemeFontSizeOverride("font_size", FontSize - 2);
        l.AddThemeColorOverride("font_color", new Color(0.68f, 0.71f, 0.78f));
        return l;
    }

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
        _readers[name] = read;

        var less = new Button { Text = "-", CustomMinimumSize = new Vector2(52, 0), Name = $"less{Slug(name)}" };
        less.AddThemeFontSizeOverride("font_size", FontSize + 2);
        less.Pressed += () => down();
        line.AddChild(less);

        var more = new Button { Text = "+", CustomMinimumSize = new Vector2(52, 0), Name = $"more{Slug(name)}" };
        more.AddThemeFontSizeOverride("font_size", FontSize + 2);
        more.Pressed += () => up();
        line.AddChild(more);

        return line;
    }

    readonly Dictionary<string, Func<string>> _readers = new Dictionary<string, Func<string>>();
    // Held rather than looked up by node name: a name search through a
    // rebuilt tree is one more thing that can quietly return nothing,
    // and it did.
    readonly Dictionary<uint, Button> _spellRows = new Dictionary<uint, Button>();
    readonly Dictionary<uint, Button> _skillRows = new Dictionary<uint, Button>();

    /// <summary>
    /// One spell or skill. Tapping it takes it; tapping it again gives
    /// it back. The model answers with a reason when it will not.
    /// </summary>
    Control Ability(string node, string name, int cost, uint id, bool spell)
    {
        var b = new Button
        {
            Text = $"  {(string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name)}   [{cost}]",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Flat = true,
            Name = node,
        };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        if (spell) _spellRows[id] = b; else _skillRows[id] = b;
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

        _points.Text = $"{_info.AttributesAvailable} attribute points, "
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

    static void Tint(Button b, bool taken)
    {
        if (b == null) return;
        b.Flat = !taken;
        b.AddThemeColorOverride("font_color",
            taken ? new Color(0.55f, 1f, 0.6f) : new Color(0.86f, 0.88f, 0.92f));
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
