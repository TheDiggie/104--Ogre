using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Settings.
///
/// `UIOptions.cpp` is the largest file in the Ogre client at some two
/// and a half thousand lines, and almost none of it means anything
/// here: screen resolution, window borders, vsync, FSAA, anisotropic
/// filtering, mipmaps, 3D models, the sky, weather particles, mouse
/// aim speed and distance, camera collisions, key bindings. Those are
/// an Ogre desktop's knobs. A phone has a screen it was given and no
/// keyboard to bind.
///
/// What survives the translation is the part that is about the game
/// rather than the renderer, and it comes in two halves.
///
/// The client's own: sound volume, music volume, whether looping
/// sounds play at all, and a brightness factor. Brightness is not a
/// gamma slider - `OnBrightnessChanged` sets Config->BrightnessFactor
/// and calls AdjustAmbientLight, so it scales the room's own ambient
/// light rather than the final picture, and the file caps it at 0.8.
/// Kept as a factor for the same reason: a room the server has darkened
/// should still be darker than one it has not.
///
/// The server's: the preference flags. Those are not in UIOptions at
/// all - in the game they are chat commands, `/grouping`, `/autoloot`
/// and the rest, each setting a bit in ClientPreferences and sending
/// the whole word back with SendUserCommandSendPreferences. A phone has
/// a Say box but nobody is going to type those into it, so they are
/// offered here as switches. The safety one is the same bit the status
/// bar already flips, and both see the same value.
/// </summary>
public partial class OptionsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 46;

    /// <summary>A preference bit changed: tell the server.</summary>
    public event Action Preferences;

    /// <summary>Sound and music, 0..1 after the game's 0..10 scale.</summary>
    public event Action<float> SoundVolume;
    public event Action<float> MusicVolume;
    public event Action<bool> LoopSounds;
    /// <summary>Ambient light factor, 0..0.8 like the game's slider.</summary>
    public event Action<float> Brightness;
    /// <summary>Look sensitivity and whether up is down.</summary>
    public event Action<float> LookSpeed;
    public event Action<bool> InvertLook;

    Button _open;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    PreferencesFlags _prefs;
    readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();
    readonly Dictionary<string, Func<string>> _readers = new Dictionary<string, Func<string>>();
    readonly List<CheckBox> _switches = new List<CheckBox>();
    string _signature = "";

    // The client-side settings live here rather than in a config file:
    // this client has no options file yet, and inventing one to hold
    // six numbers would be the larger change.
    float _sound = 7f, _music = 5f, _bright = 0f, _look = 1f;
    bool _loops = true, _invert;

    public bool IsOpen => _panel != null && _panel.Visible;

    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Settings", Name = "settingsButton" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Settings", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
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

    public void Open() { Build(); Show(true); }
    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _close.Visible = on; _open.Visible = !on;
        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(96, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 96, v.Y - ButtonBottom - 40);

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 800f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 2f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, top + height - rowH - 12f - y);
        _rows.CustomMinimumSize = new Vector2(w, 0);

        _close.Position = new Vector2(side, top + height - rowH);
        _close.Size = new Vector2(w, rowH);
    }

    /// <summary>The flags this panel switches; read every time it opens.</summary>
    public void Follow(PreferencesFlags prefs)
    {
        _prefs = prefs;
        FollowPreferences();
    }

    void Build()
    {
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _values.Clear(); _readers.Clear(); _switches.Clear();
        _signature = "";

        _rows.AddChild(Section("Sound"));
        _rows.AddChild(Slider("Sound volume", () => _sound, v =>
            { _sound = v; SoundVolume?.Invoke(v / 10f); }));
        _rows.AddChild(Slider("Music volume", () => _music, v =>
            { _music = v; MusicVolume?.Invoke(v / 10f); }));
        _rows.AddChild(Switch("Looping sounds", () => _loops, on =>
            { _loops = on; LoopSounds?.Invoke(on); }));

        _rows.AddChild(Section("Picture"));
        // 0 to 0.8, which is the file's own cap, in tenths.
        _rows.AddChild(Slider("Brightness", () => _bright * 10f, v =>
            { _bright = Mathf.Min(v, 8f) / 10f; Brightness?.Invoke(_bright); }, 8f));

        _rows.AddChild(Section("Controls"));
        _rows.AddChild(Slider("Look speed", () => _look * 10f, v =>
            { _look = Mathf.Max(v, 1f) / 10f; LookSpeed?.Invoke(_look); }, 30f));
        _rows.AddChild(Switch("Invert look", () => _invert, on =>
            { _invert = on; InvertLook?.Invoke(on); }));

        _rows.AddChild(Section("Character"));
        _rows.AddChild(Preference("Safety off", () => _prefs != null && _prefs.IsSafetyOff,
                                  on => { if (_prefs != null) _prefs.IsSafetyOff = on; }));
        _rows.AddChild(Preference("Temporary safety on death", () => _prefs != null && _prefs.TempSafe,
                                  on => { if (_prefs != null) _prefs.TempSafe = on; }));
        _rows.AddChild(Preference("Grouping", () => _prefs != null && _prefs.Grouping,
                                  on => { if (_prefs != null) _prefs.Grouping = on; }));
        _rows.AddChild(Preference("Pick loot up automatically", () => _prefs != null && _prefs.AutoLoot,
                                  on => { if (_prefs != null) _prefs.AutoLoot = on; }));
        _rows.AddChild(Preference("Combine spell items", () => _prefs != null && _prefs.AutoCombine,
                                  on => { if (_prefs != null) _prefs.AutoCombine = on; }));
        _rows.AddChild(Preference("Use the reagent bag", () => _prefs != null && _prefs.ReagentBag,
                                  on => { if (_prefs != null) _prefs.ReagentBag = on; }));
        _rows.AddChild(Preference("Show spell power", () => _prefs != null && _prefs.SpellPower,
                                  on => { if (_prefs != null) _prefs.SpellPower = on; }));
    }

    /// <summary>
    /// The seven server-side preference switches and how to read each
    /// one back, so they can be greyed out until the server has spoken
    /// and re-read when it does.
    /// </summary>
    readonly List<(CheckBox Box, Func<bool> Get)> _prefRows =
        new List<(CheckBox, Func<bool>)>();

    /// <summary>
    /// Puts the preference switches where the model says they are, and
    /// shows them as unavailable until it has anything to say.
    /// </summary>
    public void FollowPreferences()
    {
        bool live = _prefs != null && _prefs.Enabled;
        foreach ((CheckBox box, Func<bool> get) in _prefRows)
        {
            box.Disabled = !live;
            box.SetPressedNoSignal(get());
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
        l.AddThemeFontSizeOverride("font_size", FontSize + 2);
        l.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        return l;
    }

    /// <summary>
    /// A number with a minus and a plus rather than a drag bar: a slider
    /// thin enough to fit a row is not a thing a thumb can place
    /// accurately, and these all have few enough steps to step through.
    /// </summary>
    Control Slider(string name, Func<float> get, Action<float> set, float max = 10f)
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
            Text = $"{get():0}",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(70, 0),
        };
        value.AddThemeFontSizeOverride("font_size", FontSize);
        value.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        line.AddChild(value);
        _values[name] = value;
        _readers[name] = () => $"{get():0}";

        var less = new Button { Text = "-", CustomMinimumSize = new Vector2(52, 0), Name = $"less{Slug(name)}" };
        less.AddThemeFontSizeOverride("font_size", FontSize + 2);
        less.Pressed += () => { set(Mathf.Max(0f, get() - 1f)); Redraw(); };
        line.AddChild(less);

        var more = new Button { Text = "+", CustomMinimumSize = new Vector2(52, 0), Name = $"more{Slug(name)}" };
        more.AddThemeFontSizeOverride("font_size", FontSize + 2);
        more.Pressed += () => { set(Mathf.Min(max, get() + 1f)); Redraw(); };
        line.AddChild(more);

        return line;
    }

    Control Switch(string name, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox
        {
            Text = "  " + name,
            ButtonPressed = get(),
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = $"opt{Slug(name)}",
        };
        box.AddThemeFontSizeOverride("font_size", FontSize);
        box.Toggled += on => set(on);
        _switches.Add(box);
        return box;
    }

    /// <summary>
    /// A server-side preference. The bit is set locally and the whole
    /// word goes up, which is what every one of the chat commands does.
    /// </summary>
    Control Preference(string name, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox
        {
            Text = "  " + name,
            ButtonPressed = get(),
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = $"pref{Slug(name)}",
        };
        box.AddThemeFontSizeOverride("font_size", FontSize);
        // Nothing is sent until the server has told us what the
        // preferences actually are. The word arrives as
        // UserCommandReceivePreferences and sets PreferencesFlags.Enabled
        // (DataController.cs:2785); until then every flag reads zero, so
        // opening Settings early showed all seven switches off and
        // flipping any one of them sent the whole word back with the
        // other six cleared - safety off, autoloot, reagent bag and
        // spell power quietly turned off on the server. The reference
        // keeps the boxes disabled for exactly this long
        // (`UIOptions.cpp:994-1002`, :2702).
        box.Toggled += on =>
        {
            if (_prefs == null || !_prefs.Enabled) { box.SetPressedNoSignal(get()); return; }
            set(on);
            Preferences?.Invoke();
        };
        _prefRows.Add((box, get));
        _switches.Add(box);
        return box;
    }

    void Redraw()
    {
        foreach (KeyValuePair<string, Func<string>> r in _readers)
            if (_values.TryGetValue(r.Key, out Label l)) l.Text = r.Value();
    }

    static string Slug(string s) => s.Replace(" ", "").Replace("'", "");
}
