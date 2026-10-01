using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
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
///
/// Two more of the reference's Game tab now survive as well, and both
/// were missing for the same reason - they are not renderer knobs, so
/// nothing about a phone excuses dropping them.
///
/// The password. The layout gives the tab three masked boxes and a
/// button (`Meridian59.layout:4279-4318`), UIOptions binds them
/// (`:155-161`) and `OnChangePasswordClicked` (`:2777-2815`) validates
/// them before calling SendReqChangePassword. The library call was
/// sitting in `BaseClient.cs:797` with nothing in this client reaching
/// it, which means a mobile-only player whose password leaked had no way
/// to rotate it short of finding a desktop.
///
/// The language. `Meridian59.layout:4207` plus `UIOptions.cpp:456-458`
/// offer three, and `OnLanguageChanged` (`:2667-2688`) puts the choice on
/// Config->Language AND on ResourceManager->StringResources->Language.
/// That second one is the part worth being clear about: the RSB holds
/// every language at once and `StringDictionary.Language`
/// (`StringDictionary.cs:66`) picks which bracket of the id space a
/// lookup reads from (`:101-117`). So this is not a setting about
/// button labels - it is what language the SERVER's own strings arrive
/// in: object names, room names, the things people say to you.
/// `BaseClient.Connect` already passes `Config.Language` into
/// `SelectStringDictionary` (`BaseClient.cs:119-121`); this client simply
/// never set it, so everyone got English whether they read it or not.
/// </summary>
public partial class OptionsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 46;

    /// <summary>A preference bit changed: tell the server.</summary>
    public event Action Preferences;

    /// <summary>
    /// The player wants the alias editor.
    ///
    /// In the reference the aliases are not a separate window at all -
    /// they are a page of this one, reached by a category button beside
    /// the others (`UIOptions.cpp:18`, :623, :1241). A page of a
    /// full-screen panel is a second full-screen panel on a phone, so it
    /// is its own thing here; but it is still reached from Settings,
    /// which is where a player who has used the desktop client will look
    /// for it.
    /// </summary>
    public event Action EditAliases;

    /// <summary>Sound and music, 0..1 after the game's 0..10 scale.</summary>
    public event Action<float> SoundVolume;
    public event Action<float> MusicVolume;
    public event Action<bool> LoopSounds;
    /// <summary>Ambient light factor, 0..0.8 like the game's slider.</summary>
    public event Action<float> Brightness;
    /// <summary>Look sensitivity and whether up is down.</summary>
    public event Action<float> LookSpeed;
    public event Action<bool> InvertLook;

    /// <summary>
    /// Something to say, in the client's own popup. The reference says
    /// all four of its password refusals through `ConfirmPopup::ShowOK`
    /// (`UIOptions.cpp:2785`, :2792, :2799, :2806) and this client has
    /// the same one window for the same job.
    /// </summary>
    public event Action<string> Complain;

    /// <summary>
    /// Old and new password, once all four of the reference's checks
    /// have passed. The send and the write-back of the new password onto
    /// the connection entry are the view's, because they are the two
    /// things `OnChangePasswordClicked` does after validating
    /// (`UIOptions.cpp:2810-2812`) and both need the client.
    /// </summary>
    public event Action<string, string> ChangePassword;

    /// <summary>
    /// A different language was picked. Applied by the view, where the
    /// resource manager and the data model are - see
    /// `UIOptions.cpp:2681-2691` for the three things that has to touch.
    /// </summary>
    public event Action<LanguageCode> LanguageChanged;

    /// <summary>
    /// The password this client believes the account has, so the "old
    /// password incorrect" check has something to check against.
    ///
    /// The reference compares against
    /// `Config->SelectedConnectionInfo->Password`
    /// (`UIOptions.cpp:2790`) - it is a local sanity check, not an
    /// authentication; the server is what actually decides. Kept as a
    /// reader rather than a copied string so this panel never holds a
    /// credential of its own.
    /// </summary>
    public Func<string> KnownPassword { get; set; }

    /// <summary>
    /// Whether there is a logged-in account behind this panel.
    ///
    /// The reference's `UIMode::Playing`, which is exactly what decides
    /// whether the Game tab shows its switches and password boxes or the
    /// two "you must be logged in" lines instead
    /// (`UIOptions.cpp:971`, :979-987). It matters more here than there,
    /// because this panel now opens from the login screen as well - see
    /// LoginPrompt.Options - and a password box on a screen with no
    /// account behind it is an invitation to type a real password into
    /// nothing.
    /// </summary>
    public bool Playing { get; set; }

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

    // The client-side settings, on the game's own 0..10 scale, starting
    // where the reference's configuration file starts them:
    // DEFAULTVAL_ENGINE_SOUNDVOLUME is 10 and
    // DEFAULTVAL_ENGINE_MUSICVOLUME is 4 (`OgreClientConfig.h:56-57`),
    // DEFAULTVAL_ENGINE_BRIGHTNESSFACTOR is 0 (:54) and
    // DEFAULTVAL_ENGINE_DISABLELOOPSOUNDS is false (:58). Seven and five
    // were nobody's numbers, and the game is a quieter game at seven
    // than the one the sounds were mixed for.
    float _sound = 10f, _music = 4f, _bright = 0f, _look = 1f;
    bool _loops = true, _invert;

    // The language, starting where the library starts it:
    // Config.DEFAULTVAL_LANGUAGE is LanguageCode.English
    // (`Config.cs:71`).
    LanguageCode _language = LanguageCode.English;

    /// <summary>
    /// The three the reference offers, in its order
    /// (`UIOptions.cpp:456-458`). Not the whole LanguageCode enum, which
    /// runs to a couple of hundred values (`LanguageCode.cs:22+`) -
    /// offering a language the RSB has no strings for would just mean
    /// every lookup silently falling back to English
    /// (`StringDictionary.cs:41`, :117).
    /// </summary>
    static readonly LanguageCode[] Languages =
    {
        LanguageCode.English, LanguageCode.German, LanguageCode.Portuguese,
    };

    // The three password boxes, held rather than found by name: they are
    // rebuilt with the rest of the list on every Open, and a name search
    // through a rebuilt tree is one more thing that can quietly return
    // nothing.
    LineEdit _oldPass, _newPass, _confirmPass;

    // Where they are kept between sessions. The reference writes all of
    // these into configuration.xml on the way out
    // (`OgreClientConfig.cpp` saves the <engine> block from the same
    // literals above), and nothing here kept them at all: every start
    // put the volume back and a player who wants the game silent had to
    // silence it again each time. Persisted the way this client persists
    // its other state - a Godot ConfigFile under user:// - for the
    // reasons AliasStore and HotbarStore both give at length: there is
    // no writable file beside the executable on a phone, and no
    // shutdown to hang a save on, so every change writes.
    const string StorePath = "user://settings.cfg";
    const string StoreSection = "client";

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>
    /// Whether this panel's own bottom-right button may show itself.
    ///
    /// The panel now exists before the login screen does, so that the
    /// login screen has something to open (see LoginPrompt.Options). Its
    /// button, though, belongs to the row that lives over the world: on
    /// the login screen it would be a second, differently-placed way into
    /// the same panel, drawn on top of a full-screen prompt. The view
    /// turns it on when the row turns on. False until then.
    ///
    /// Not folded into Panels.ShowOpeners because that runs from the
    /// frame loop, and the frame loop does nothing at all until there is
    /// a client (`GameView.Pump` returns on a null client) - which is
    /// precisely the stretch this has to cover.
    /// </summary>
    public bool OpenerAllowed
    {
        get => _openerAllowed;
        set
        {
            _openerAllowed = value;
            if (_open != null) _open.Visible = value && !IsOpen;
        }
    }
    bool _openerAllowed;

    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        Restore();

        _open = new Button { Text = "Settings", Name = "settingsButton" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        // Hidden until the view says the row of openers is in play -
        // see OpenerAllowed. It used to be born visible, which was
        // harmless while nothing built this panel before the world.
        _open.Visible = _openerAllowed;
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

    /// <summary>
    /// Reads the saved settings, if there are any. Called from _Ready so
    /// the first Build shows what is actually in force.
    /// </summary>
    void Restore()
    {
        try
        {
            var file = new ConfigFile();
            if (file.Load(StorePath) != Error.Ok) return;
            _sound  = (float)file.GetValue(StoreSection, "sound", _sound);
            _music  = (float)file.GetValue(StoreSection, "music", _music);
            _bright = (float)file.GetValue(StoreSection, "brightness", _bright);
            _look   = (float)file.GetValue(StoreSection, "look", _look);
            _loops  = (bool)file.GetValue(StoreSection, "loops", _loops);
            _invert = (bool)file.GetValue(StoreSection, "invert", _invert);
            // Stored by name rather than by number, the way
            // configuration.xml stores it (`Config.cs:589` parses the
            // <language value="English" /> attribute with Enum.TryParse).
            // The numbers are an on-the-wire detail of the RSB format and
            // not something to pin a saved file to.
            string lang = (string)file.GetValue(StoreSection, "language", _language.ToString());
            if (Enum.TryParse<LanguageCode>(lang, out LanguageCode parsed)) _language = parsed;
        }
        catch (Exception e) { GD.PrintErr($"[OptionsPanel] load: {e.Message}"); }
    }

    /// <summary>
    /// Writes them. On every change rather than on the way out - see
    /// AliasStore.Save for why a phone client has no way out to write
    /// on.
    /// </summary>
    void Keep()
    {
        try
        {
            var file = new ConfigFile();
            file.SetValue(StoreSection, "sound", _sound);
            file.SetValue(StoreSection, "music", _music);
            file.SetValue(StoreSection, "brightness", _bright);
            file.SetValue(StoreSection, "look", _look);
            file.SetValue(StoreSection, "loops", _loops);
            file.SetValue(StoreSection, "invert", _invert);
            file.SetValue(StoreSection, "language", _language.ToString());
            file.Save(StorePath);
        }
        catch (Exception e) { GD.PrintErr($"[OptionsPanel] save: {e.Message}"); }
    }

    /// <summary>
    /// Hands the current settings to whoever is listening.
    ///
    /// The events are wired by the view as it builds its widgets, and
    /// the things that act on them - the sound player, the touch
    /// controls - are built after this panel is. So the restored values
    /// would sit here doing nothing until the player happened to press a
    /// plus. The view calls this once everything exists, which is the
    /// same order the reference works in: Config->Load runs before the
    /// controllers that read it.
    /// </summary>
    public void Apply()
    {
        SoundVolume?.Invoke(_sound / 10f);
        MusicVolume?.Invoke(_music / 10f);
        LoopSounds?.Invoke(_loops);
        Brightness?.Invoke(_bright);
        LookSpeed?.Invoke(_look);
        InvertLook?.Invoke(_invert);
        // The language is deliberately NOT pushed from here. Apply runs
        // while the view is still building the client, and Config.Load
        // comes after it - and Load sets Language from configuration.xml
        // (`Config.cs:581-594`), so anything set before it is overwritten.
        // The view reads ChosenLanguage once the client is initialised
        // instead; see GameView.
    }

    /// <summary>
    /// The saved language, for the view to apply at a moment of its own
    /// choosing. See Apply for why it is not pushed with the rest.
    /// </summary>
    public LanguageCode ChosenLanguage => _language;

    public void Open() { Build(); Show(true); }
    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _close.Visible = on; _open.Visible = !on && _openerAllowed;
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
        _scroll.Size = new Vector2(w, top + height - rowH - 24f - y);
        _rows.CustomMinimumSize = new Vector2(w, 0);

        // Twelve pixels of air under the last row, so the panel's
        // own edge and the button's are not the same line.
        _close.Position = new Vector2(side, top + height - rowH - 12f);
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
        // Cleared with the rest, which it was not: Preference() appends
        // to this on every Build, so a second Open left FollowPreferences
        // walking rows whose CheckBox had been queued for freeing. It
        // only ever threw on the reopen, which is why nothing noticed.
        _prefRows.Clear();
        _oldPass = _newPass = _confirmPass = null;
        _signature = "";

        _rows.AddChild(Section("Sound"));
        _rows.AddChild(Slider("Sound volume", () => _sound, v =>
            { _sound = v; SoundVolume?.Invoke(v / 10f); Keep(); }));
        _rows.AddChild(Slider("Music volume", () => _music, v =>
            { _music = v; MusicVolume?.Invoke(v / 10f); Keep(); }));
        _rows.AddChild(Switch("Looping sounds", () => _loops, on =>
            { _loops = on; LoopSounds?.Invoke(on); Keep(); }));

        _rows.AddChild(Section("Picture"));
        // 0 to 0.8, which is the file's own cap, in tenths.
        _rows.AddChild(Slider("Brightness", () => _bright * 10f, v =>
            { _bright = Mathf.Min(v, 8f) / 10f; Brightness?.Invoke(_bright); Keep(); }, 8f));

        _rows.AddChild(Section("Controls"));
        _rows.AddChild(Slider("Look speed", () => _look * 10f, v =>
            { _look = Mathf.Max(v, 1f) / 10f; LookSpeed?.Invoke(_look); Keep(); }, 30f));
        _rows.AddChild(Switch("Invert look", () => _invert, on =>
            { _invert = on; InvertLook?.Invoke(on); Keep(); }));

        // The reference's own Language heading and its one control
        // (`Meridian59.layout:4194-4211`). A stepper rather than a
        // combobox because there are three of them and this file already
        // steps through everything else; the reference's own control is a
        // read-only combobox, which with three items is the same
        // question asked with more taps.
        _rows.AddChild(Section("Language"));
        _rows.AddChild(Choice("Selected Language", () => _language.ToString(),
            () => StepLanguage(-1), () => StepLanguage(1)));

        _rows.AddChild(Section("Character"));
        if (!Playing)
        {
            // `SettingsDisabledDescription`, verbatim
            // (`Meridian59.layout:4224`), shown on exactly the condition
            // the reference shows it on: mode != Playing
            // (`UIOptions.cpp:971`).
            _rows.AddChild(Note("You must be logged in to modify settings."));
        }
        else
        {
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

        _rows.AddChild(Section("Chat"));
        // The reference's alias tab is not mode-gated, because its editor
        // works straight off Config and Config exists before login. This
        // client's AliasEditor is handed the client's Config by the view
        // (`GameView`, Widget("aliases")), and there is no client until
        // login - so from the login screen this row would be a button that
        // does nothing, which is worse than a row that says why.
        if (Playing) _rows.AddChild(Opens("Aliases", "Edit", () => EditAliases?.Invoke()));
        else _rows.AddChild(Note("You must be logged in to edit your aliases."));

        Account();
    }

    /// <summary>
    /// English, German, Portuguese and round again.
    ///
    /// Nothing is pushed out if the choice did not change, which is the
    /// reference's own first guard (`UIOptions.cpp:2681-2683`) - the
    /// re-resolve it triggers walks every object in the room and every
    /// list in the data model (`DataController.cs:1161+`), and doing that
    /// because somebody tapped plus and then minus is waste.
    /// </summary>
    void StepLanguage(int by)
    {
        int at = Array.IndexOf(Languages, _language);
        if (at < 0) at = 0;
        LanguageCode next = Languages[Mathf.PosMod(at + by, Languages.Length)];
        if (next == _language) return;

        _language = next;
        Keep();
        LanguageChanged?.Invoke(_language);
    }

    /// <summary>
    /// Changing the account password: the Game tab's bottom half
    /// (`Meridian59.layout:4279-4318`).
    ///
    /// Three masked boxes and a button, and - when there is no account
    /// behind the panel - the reference's own line instead of them
    /// (`Meridian59.layout:4276`, shown on mode != Playing at
    /// `UIOptions.cpp:979`).
    /// </summary>
    void Account()
    {
        _rows.AddChild(Section("Account"));

        if (!Playing)
        {
            _rows.AddChild(Note("You must be logged in to change your password."));
            return;
        }

        _oldPass = Secret("Current Password", "oldPassword");
        _newPass = Secret("New Password", "newPassword");
        _confirmPass = Secret("Confirm Password", "confirmPassword");

        var go = new Button
        {
            Text = "Change Password",
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = "changePassword",
        };
        go.AddThemeFontSizeOverride("font_size", FontSize);
        go.Pressed += Rotate;
        _rows.AddChild(go);
    }

    /// <summary>
    /// A labelled masked box. `MaskText True` on all three of the
    /// reference's editboxes (`Meridian59.layout:4288`, :4300, :4312) -
    /// which matters most for the current one, since a shoulder over a
    /// phone is closer than a shoulder over a monitor.
    /// </summary>
    LineEdit Secret(string caption, string node)
    {
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(0, FontSize * 1.8f) };
        label.AddThemeFontSizeOverride("font_size", FontSize - 1);
        label.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.8f));
        _rows.AddChild(label);

        var box = new LineEdit
        {
            Secret = true,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = node,
        };
        box.AddThemeFontSizeOverride("font_size", FontSize);
        _rows.AddChild(box);
        return box;
    }

    /// <summary>
    /// The four checks, in the reference's order and with its own
    /// wording (`UIOptions.cpp:2782-2808`). The order is not
    /// interchangeable: filled-in first, because an empty box has no
    /// business being compared; then the old one, because telling
    /// somebody their two new passwords disagree when they have
    /// mistyped the old one sends them off fixing the wrong box.
    ///
    /// None of this is security - the server decides, and it will refuse
    /// a wrong old password itself. It is there so the four common
    /// mistakes are named on the spot instead of coming back as a
    /// refusal with no reason attached.
    /// </summary>
    void Rotate()
    {
        if (_oldPass == null || _newPass == null || _confirmPass == null) return;

        string old = _oldPass.Text ?? "";
        string now = _newPass.Text ?? "";
        string again = _confirmPass.Text ?? "";

        if (old.Length == 0 || now.Length == 0 || again.Length == 0)
        {
            Complain?.Invoke("Please fill out all password fields.");
            return;
        }

        // Skipped when nothing knows the current password - the
        // environment-variable route into this client never passes
        // through the login box, so there is a logged-in account whose
        // password this panel was never told. The reference always has it
        // because its login window is the only way in.
        string known = null;
        try { known = KnownPassword?.Invoke(); } catch { }
        if (!string.IsNullOrEmpty(known) && old != known)
        {
            Complain?.Invoke("Old password incorrect.");
            return;
        }

        if (now != again)
        {
            Complain?.Invoke("New passwords do not match.");
            return;
        }

        if (old == now)
        {
            Complain?.Invoke("New password is same as old password.");
            return;
        }

        ChangePassword?.Invoke(old, now);

        // Cleared once it has gone. The reference leaves the boxes full,
        // which on a desktop is a window you close; here the panel is the
        // screen, and three filled password boxes left sitting on it are
        // three password boxes somebody can hand the phone over with.
        _oldPass.Text = ""; _newPass.Text = ""; _confirmPass.Text = "";
    }

    /// <summary>
    /// A line of the reference's own explanatory text. Wrapped, because
    /// these are sentences and the panel is as wide as a phone.
    /// </summary>
    Control Note(string text)
    {
        var l = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, FontSize * 2f),
        };
        l.AddThemeFontSizeOverride("font_size", FontSize - 1);
        l.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
        return l;
    }

    /// <summary>
    /// A row that leads somewhere else, laid out like the sliders so the
    /// list still reads as one column: what it is on the left, the way in
    /// on the right.
    /// </summary>
    Control Opens(string name, string verb, Action pressed)
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

        var go = new Button
        {
            Text = verb,
            CustomMinimumSize = new Vector2(112, 0),
            Name = $"open{Slug(name)}",
        };
        go.AddThemeFontSizeOverride("font_size", FontSize);
        go.Pressed += () => pressed();
        line.AddChild(go);

        return line;
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

    /// <summary>
    /// A setting whose value is a word rather than a number, stepped the
    /// same way the numbers are so the column still reads as one column.
    /// Laid out exactly as Slider lays itself out - the value is just
    /// wider, because "Portuguese" is.
    /// </summary>
    Control Choice(string name, Func<string> read, Action down, Action up)
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
            CustomMinimumSize = new Vector2(130, 0),
        };
        value.AddThemeFontSizeOverride("font_size", FontSize);
        value.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        line.AddChild(value);
        _values[name] = value;
        _readers[name] = read;

        var less = new Button { Text = "-", CustomMinimumSize = new Vector2(52, 0), Name = $"less{Slug(name)}" };
        less.AddThemeFontSizeOverride("font_size", FontSize + 2);
        less.Pressed += () => { down(); Redraw(); };
        line.AddChild(less);

        var more = new Button { Text = "+", CustomMinimumSize = new Vector2(52, 0), Name = $"more{Slug(name)}" };
        more.AddThemeFontSizeOverride("font_size", FontSize + 2);
        more.Pressed += () => { up(); Redraw(); };
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
