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

        float side = Mathf.Max(16f, v.X * 0.05f);
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

        _rows.AddChild(Attribute("Might", () => _info.Might, v => _info.Might = v));
        _rows.AddChild(Attribute("Intellect", () => _info.Intellect, v => _info.Intellect = v));
        _rows.AddChild(Attribute("Stamina", () => _info.Stamina, v => _info.Stamina = v));
        _rows.AddChild(Attribute("Agility", () => _info.Agility, v => _info.Agility = v));
        _rows.AddChild(Attribute("Mysticism", () => _info.Mysticism, v => _info.Mysticism = v));
        _rows.AddChild(Attribute("Aim", () => _info.Aim, v => _info.Aim = v));

        _rows.AddChild(Section("Spells"));
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

    Control Attribute(string name, Func<uint> get, Action<uint> set)
        => Row(name, () => get().ToString(),
               () => { set(get() - 1); _signature = ""; },
               () => { set(get() + 1); _signature = ""; });

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

    void Portrait()
    {
        ObjectBase model = _info?.ExampleModel;
        if (model?.Resource == null) return;

        // ObjectBase has no appearance hash - that lives on RoomObject -
        // so the key is what actually changed: the seven indices.
        string key = $"{model.Resource.Filename}:{_gender}:{_skin}:{_hair}:"
                   + $"{_hairStyle}:{_eyes}:{_nose}:{_mouth}";
        if (key == _faceKey) return;
        _faceKey = key;

        try { _face.Texture = M59Assets.FromTex(M59Compose.Icon(model, PortraitSize)); }
        catch (Exception e) { GD.PrintErr($"[CreateCharacter] face: {e.Message}"); }
    }

    void Finish()
    {
        if (_info == null) return;
        string name = _name.Text ?? "";
        if (name.Trim().Length == 0) { Complain?.Invoke("Your character needs a name."); return; }

        Create?.Invoke(name, _description.Text ?? "");
        Show(false);
    }

    static string Slug(string s) => s.Replace("'", "").Replace(" ", "");
}
