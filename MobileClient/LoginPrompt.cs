using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Asks who you are.
///
/// The client used to read `M59USER` and `M59PASS` out of the
/// environment and refuse to start without them, which is fine from a
/// terminal and useless in an exported build - nobody double-clicks a
/// game and then goes to set environment variables. So the environment
/// still wins when it is set (handy for the test harnesses), and this
/// asks when it is not.
///
/// The account name is remembered in `user://`; the password never is.
/// Storing it would mean writing a real credential to disk in the clear,
/// which is not worth saving anyone four seconds of typing.
///
/// Two things the reference's own login window has and this did not.
///
/// A server picker (`Meridian59.layout:3045`, filled from
/// `Config->Connections` at `UILogin.cpp:24-26`). See ServerList for why
/// a baked-in host is a worse thing on a phone than on a desktop: there
/// is nobody to edit a file beside the executable, so a server that
/// moves is a server you cannot reach until somebody ships a new build.
///
/// An Options button (`Meridian59.layout:3075`, wired at
/// `UILogin.cpp:16` and `:37`, and it does nothing more than
/// `ToggleVisibility(Options::Window)` at `:163-171`). This client gated
/// every way into Settings on being in the world, which meant the player
/// who most needs them - the one who cannot get past this screen, or who
/// wants the music down before they are standing in a room - had none.
/// </summary>
public partial class LoginPrompt : Control
{
    [Export] public int FontSize = 18;

    const string Remembered = "user://account.txt";

    /// <summary>Account and password, once Connect is pressed.</summary>
    public event Action<string, string> Submitted;

    /// <summary>
    /// Settings, from here. The reference's `Login.Options` button does
    /// exactly this and nothing else (`UILogin.cpp:163-171`).
    /// </summary>
    public event Action Options;

    /// <summary>
    /// A different server was chosen. Raised as it is chosen rather than
    /// on Connect, which is the reference's timing too: `OnServerChanged`
    /// sets SelectedConnectionIndex and refills the username and
    /// password boxes from the new entry there and then
    /// (`UILogin.cpp:88-102`).
    /// </summary>
    public event Action<int> ServerChanged;

    Label _title, _note, _serverLabel, _userLabel, _passLabel, _version, _update, _howto;

    /// <summary>
    /// How a player who has no account gets one, under the password
    /// box. Ashton: "on the login screen describe making an account".
    /// One string, one place, because this is the fact most likely to
    /// change about the server and least likely to be in the code.
    ///
    /// It used to end "Keep the password: it cannot be recovered", which
    /// is true and is the wrong note to leave a new player on. Ashton,
    /// 2026-10-03: "remove the note under login saying u cant recover
    /// your password, instead say 'if you need help reach out to us on
    /// discord'". The Discord button it points at is in the footer.
    /// </summary>
    public const string NewAccountHint =
        "New here? Type an account name and password of your own and press Connect - " +
        "the server creates the account the first time you log in. If you need help, reach out to us on Discord.";

    /// <summary>Where "reach out to us on Discord" goes, and the wiki beside it.</summary>
    /// <summary>A poster's width when there is room, and the least it will stand at.</summary>
    const float PosterW = 320f, PosterMin = 200f;

    public const string DiscordUrl = "https://discord.gg/vjEkbpxAJt";
    public const string WikiUrl = "https://wiki.meridian59.us/";
    ColorRect _bg;
    Panel _card, _bar;
    LineEdit _user, _pass;
    Button _go, _options;
    Poster _discord, _wiki;
    OptionButton _servers;

    /// <summary>
    /// The note's resting colour. It doubles as the error line, so both
    /// Pick and Go put it back to this before writing anything that is
    /// not a refusal - see those two. Dim rather than grey-blue now,
    /// because it sits on the card with the rest of the skin.
    /// </summary>
    static readonly Color NoteCalm = M59Skin.TextDim;
    /// <summary>A refusal, and the one thing the player gets when something goes wrong.</summary>
    static readonly Color NoteBad = new Color(1f, 0.58f, 0.50f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Opaque, not the skin's translucent Scrim: there is no world
        // behind this screen yet, only the connection log the view
        // writes down the left, and that must not read through the card.
        _bg = new ColorRect { Color = new Color(M59Skin.Scrim.R, M59Skin.Scrim.G, M59Skin.Scrim.B) };
        AddChild(_bg);

        _card = M59Skin.Window();
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        AddChild(_bar);

        // The game's name IS the title bar. Bigger than the skin's
        // TitleSize and nothing else like it in the client, because this
        // is the one screen whose job is to say which game this is -
        // still inside the 58-point bar, so the body is where Frame put it.
        _title = M59Skin.Title("Meridian 59");
        _title.AddThemeFontSizeOverride("font_size", 34);
        AddChild(_title);

        // Above the account, as it is in the layout: Server at y=75,
        // Username at y=110, Password at y=140
        // (`Meridian59.layout:3045`, :3058, :3068). The order is not
        // arbitrary - which server you are on decides which account name
        // means anything, so it is the first thing asked.
        _serverLabel = M59Skin.Caption("Server");
        AddChild(_serverLabel);

        // An OptionButton rather than a row of steppers, because this is
        // the reference's own control: a read-only combobox
        // (`Meridian59.layout:3047` sets ReadOnly True, so it picks from
        // the list and never takes typing). Godot draws its list as a
        // popup inside the game window, which is what the house rule
        // wants - no OS dialog anywhere near it.
        _servers = new OptionButton { Name = "serverPick" };
        // Dressed as a button and aligned left, so it reads as the
        // choice it is rather than as an engine widget: a bare
        // OptionButton on a black page was the one control on this
        // screen that looked like a form field from another program.
        M59Skin.Dress(_servers, M59Skin.Kind.Secondary);
        _servers.Alignment = HorizontalAlignment.Left;
        // The list it drops is drawn by a PopupMenu with its own theme,
        // which Dress cannot reach - left alone it opened as a grey
        // engine menu over the card.
        PopupMenu list = _servers.GetPopup();
        list.AddThemeStyleboxOverride("panel", M59Skin.Sunken());
        list.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        list.AddThemeColorOverride("font_color", M59Skin.Text);
        list.AddThemeColorOverride("font_hover_color", M59Skin.GoldBright);
        _servers.ItemSelected += index => Pick((int)index);
        AddChild(_servers);

        _userLabel = M59Skin.Caption("Account");
        AddChild(_userLabel);

        _user = M59Skin.Field(new LineEdit { PlaceholderText = "account" });
        _user.TextSubmitted += _ => Go();
        AddChild(_user);

        _passLabel = M59Skin.Caption("Password");
        AddChild(_passLabel);

        // Secret stays Secret: this box never shows what is typed in it.
        _pass = M59Skin.Field(new LineEdit { PlaceholderText = "password", Secret = true });
        _pass.TextSubmitted += _ => Go();
        AddChild(_pass);

        _howto = new Label
        {
            Text = NewAccountHint,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _howto.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        _howto.AddThemeColorOverride("font_color", M59Skin.TextDim);
        AddChild(_howto);

        // The one thing this screen is for.
        _go = new Button { Text = "Connect" };
        M59Skin.Dress(_go, M59Skin.Kind.Primary);
        _go.Pressed += Go;
        AddChild(_go);

        // The layout's own shape: a small button on the same line as
        // Connect (`Meridian59.layout:3074-3080` puts it at x 100..125
        // against Connect's 0..100). Spelt out rather than iconised
        // because there are no CEGUI icon images here to borrow. In the
        // footer beside Connect now, laid out from the right like every
        // other panel's actions.
        _options = new Button { Text = "Settings", Name = "loginOptions" };
        M59Skin.Dress(_options, M59Skin.Kind.Secondary);
        _options.Pressed += () => Options?.Invoke();
        AddChild(_options);

        // The two doors out of the client, at the LEFT end of the footer
        // where FootLeft puts the thing that must not be mistaken for the
        // row of actions on the right. Ashton, 2026-10-03: "add a discord
        // button on the left that links to ... on the right add a link to
        // https://wiki.meridian59.us/". Discord takes the far left, the
        // wiki sits beside it; both hand the address to the system
        // browser the way Update does (OS.ShellOpen), which is the one
        // place an outside window is the right answer - there is no
        // in-page way to open Discord.
        // POSTERS, not buttons. Ashton: "i dont want them as small
        // buttons, i want them like posters to the left and right of
        // the login window." Two tall cards flanking the login card,
        // each the whole of itself a target, each saying in large type
        // what it opens. They are the only two places a new player is
        // sent, so they get the room a footer button cannot give.
        _discord = new Poster("Discord",
                              "Join the community.\nHelp, news, events, and the people who run the server.",
                              "Tap to open Discord", DiscordUrl) { Name = "loginDiscord" };
        AddChild(_discord);
        _wiki = new Poster("Wiki",
                           "Every spell, item, monster and map.\nwiki.meridian59.us",
                           "Tap to open the wiki", WikiUrl) { Name = "loginWiki" };
        AddChild(_wiki);

        // The progress line and every refusal. Body-sized rather than
        // the small print it was: when something goes wrong this is the
        // only thing the player is given.
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _note.AddThemeFontSizeOverride("font_size", M59Skin.BodySize - 2);
        _note.AddThemeColorOverride("font_color", NoteCalm);
        AddChild(_note);

        // WHICH BUILD THIS IS, in the corner of the title bar.
        //
        // Nothing in the client said, and the cost of that showed up the
        // first time the update prompt did not appear: neither the
        // player nor anyone helping them could tell whether the manifest
        // was wrong, the path was wrong, or the phone was simply already
        // on the newest build and the silence was correct. Three very
        // different problems with one symptom, and no way to tell them
        // apart without unpacking the APK.
        //
        // The same string the updater compares against - the project's
        // own config/version, read through Updater.Running - so what is
        // on screen is by construction what the check used, rather than
        // a second copy that can disagree with it.
        //
        // On the title bar rather than beside the fields: it is wanted
        // perhaps twice in a client's life and should cost nothing to
        // ignore the rest of the time.
        _version = new Label
        {
            Text = Updater.Running.Length > 0 ? $"v{Updater.Running}" : "",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _version.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        _version.AddThemeColorOverride("font_color", M59Skin.TextDim);
        AddChild(_version);

        // What the update check concluded, on its own line under the
        // note. It cannot share _note: that line is the server address
        // and the login errors, and an update verdict that replaces
        // "wrong password" is worse than no verdict at all.
        _update = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _update.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        _update.AddThemeColorOverride("font_color", M59Skin.TextDim);
        AddChild(_update);

        Recall();

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>Where we are connecting, so it is never a mystery.</summary>
    /// <summary>
    /// Once wrote host:port into the note. It writes nothing now: the
    /// server has a name in the dropdown and the address under it was
    /// a number nobody at a login screen needs - Ashton, 2026-10-02,
    /// "remove the IP address from the login screen". The note is kept
    /// for what it is for: "connecting..." and the refusals.
    /// </summary>
    public void Server(string host, int port) { if (_note != null) _note.Text = ""; }

    /// <summary>
    /// The update check's verdict, shown on the card it runs behind.
    ///
    /// The connection log would be the obvious home and is the wrong
    /// one: GameView writes that label from its own _log inside
    /// RenderFrame, which does not run until there is a world, so a
    /// line put there at the login screen is never drawn at all. Found
    /// by shooting the screen and seeing nothing - a write that
    /// succeeds and then never appears looks exactly like a write that
    /// did not happen.
    /// </summary>
    /// <summary>
    /// Turns the card into the one thing a player on a superseded build
    /// can do: update.
    ///
    /// Connect becomes Update, in the primary colour, and pressing it
    /// hands the download link to the browser exactly as the old "Get
    /// it" did (OS.ShellOpen - the reasoning about Android's installer
    /// is in Updater's header). The account boxes are left alone but
    /// nothing reads them: there is no Connect to press. The note says
    /// which version is required so the player knows this is the site's
    /// decision and not a dead button.
    ///
    /// Not undoable within a run, deliberately. The check runs once per
    /// launch; the only way back to Connect is to install the build the
    /// site names and start again, which is the whole point of the
    /// gate. Ashton, 2026-10-02: "don't let people play with old
    /// versions."
    /// </summary>
    public void RequireUpdate(string version, string url)
    {
        if (_go == null || _required) return;
        _required = true;
        _go.Pressed -= Go;
        _go.Text = "Update";
        _go.Disabled = false;
        _go.Pressed += () => { if (url.Length > 0) OS.ShellOpen(url); };
        _user.Editable = false;
        _pass.Editable = false;
        _note.AddThemeColorOverride("font_color", NoteCalm);
        _note.Text = $"Version {version} is required to play. Tap Update to get it.";
        Layout();
    }
    bool _required;

    public void UpdateNote(string text)
    {
        if (_update == null) return;
        _update.Text = text ?? "";
        _update.Visible = _update.Text.Length > 0;
        Layout();
    }

    /// <summary>
    /// Fills the picker, the way `UILogin::Initialize` fills its
    /// combobox: one item per entry, named by the entry's Name
    /// (`UILogin.cpp:24-26`), then the selection set from config
    /// (`:42-43`).
    ///
    /// A single entry still shows the row rather than hiding it. Where
    /// you are connecting is worth stating even when there is only one
    /// answer, and the row disappearing the moment configuration.xml
    /// went missing would be the sort of difference nobody can
    /// diagnose from a phone.
    /// </summary>
    public void Choices(List<ServerList.Entry> rows, int selected)
    {
        if (_servers == null || rows == null) return;

        _servers.Clear();
        foreach (ServerList.Entry e in rows) _servers.AddItem(e.Name);

        if (rows.Count == 0) { Layout(); return; }

        int at = Mathf.Clamp(selected, 0, rows.Count - 1);
        // Selected without raising ItemSelected: this is the state
        // arriving, not the player choosing, and telling the view about a
        // change it just made is how a remembered pick gets rewritten
        // with itself.
        _servers.Select(at);
        _note.Text = "";   // no address on the card; see Server()
        Layout();
    }

    void Pick(int index)
    {
        // Back to the neutral colour: the note doubles as the error line
        // (see Trouble), and a red address under a server you have just
        // changed away from is a lie about the one you picked.
        _note.AddThemeColorOverride("font_color", NoteCalm);
        ServerChanged?.Invoke(index);
    }

    /// <summary>Which row is showing, for whoever is about to connect.</summary>
    public int Chosen => _servers != null ? _servers.Selected : 0;

    /// <summary>
    /// The account and password that belong to the server now showing.
    ///
    /// `OnServerChanged` refills both boxes from the newly selected
    /// ConnectionInfo (`UILogin.cpp:94-101`), because an account name is
    /// per-server: the name that works on the live server is very often
    /// not a name at all on a test one. Only fills what it was given -
    /// an entry with no stored username leaves whatever was typed alone,
    /// rather than wiping it because a file was blank.
    /// </summary>
    public void Account(string account, string password)
    {
        if (!string.IsNullOrEmpty(account)) _user.Text = account;
        if (!string.IsNullOrEmpty(password)) _pass.Text = password;
    }

    public void Trouble(string why)
    {
        if (_note == null) return;
        _note.Text = why;
        _note.AddThemeColorOverride("font_color", NoteBad);
        _go.Disabled = false;
    }

    void Go()
    {
        string u = _user.Text.Trim(), p = _pass.Text;

        // The guard on an empty box stays, and it now says so.
        //
        // The reference has no guard at all: `OnConnectClicked` copies
        // both boxes straight into the selected ConnectionInfo and calls
        // `Connect()` whatever they hold (`UILogin.cpp:146-158`), and the
        // server answers a blank account with a LoginFailed, which
        // becomes the popup "Your account credentials are not correct."
        // (`OgreClient.cpp:887-896`). So a desktop player always gets a
        // sentence back; it costs a socket, a handshake and a refusal to
        // get it.
        //
        // What was here took the half of that trade that helps nobody.
        // It moved focus to the empty box and returned - nothing sent,
        // nothing said. On a desktop a caret jumping between two fields
        // IS an answer; on a phone the soft keyboard covers the fields
        // it is jumping between and there is no caret to see, so
        // Connect read as a dead button, which is what it was reported
        // as.
        //
        // The guard is the kinder half and is kept: a server has nothing
        // to tell you about a box you left blank that you do not already
        // know, and a phone on a bad connection pays real seconds to be
        // told it. What was missing is the other half, so the note that
        // already carries "connecting..." and every refusal
        // (see Trouble) carries this too. In-page, as everything this
        // client says is - never an engine or OS dialog.
        if (u.Length == 0) { Trouble("Type your account name to connect."); _user.GrabFocus(); return; }
        if (p.Length == 0) { Trouble("Type your password to connect."); _pass.GrabFocus(); return; }

        Remember(u);
        _go.Disabled = true;
        // Back to the neutral colour before the progress line, for the
        // reason Pick does it: the note doubles as the error line, and
        // "connecting..." in refusal red is a lie about what is
        // happening.
        _note.AddThemeColorOverride("font_color", NoteCalm);
        _note.Text = "connecting...";
        Submitted?.Invoke(u, p);
    }

    /// <summary>
    /// Fills in the remembered account name. It no longer focuses
    /// anything.
    ///
    /// It used to put the caret in the password box (or the account box
    /// when nothing was remembered), which on a desktop is a courtesy -
    /// the next keystroke goes where it is wanted. On a phone focus IS
    /// the keyboard: the app opened straight onto a soft keyboard
    /// covering half the card, before the player had decided to log in
    /// at all. Ashton, 2026-10-02: "soon as I open the app it pulls up my
    /// keyboard ... make people click on password first." The two
    /// GrabFocus calls in Go() stay - those answer a press on Connect
    /// with an empty box, which is a moment the player asked for a
    /// caret.
    /// </summary>
    void Recall()
    {
        try
        {
            if (!FileAccess.FileExists(Remembered)) return;
            using FileAccess f = FileAccess.Open(Remembered, FileAccess.ModeFlags.Read);
            string saved = f?.GetAsText()?.Trim();
            if (!string.IsNullOrEmpty(saved)) _user.Text = saved;
        }
        catch { }
    }

    void Remember(string account)
    {
        try
        {
            using FileAccess f = FileAccess.Open(Remembered, FileAccess.ModeFlags.Write);
            f?.StoreString(account);
        }
        catch { /* not being able to remember it is not worth an error */ }
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

    /// <summary>Caption, then the box it names. Both are a fixed height here.</summary>
    const float CapH = 22f;

    /// <summary>
    /// How much keyboard was up at the last layout. Polled rather than
    /// signalled because Godot raises nothing when the on-screen
    /// keyboard opens or closes; it is two cheap calls a frame and only
    /// while this screen is up.
    /// </summary>
    float _keyboard;

    public override void _Process(double delta)
    {
        if (_title == null || !Visible) return;
        float k = M59Skin.KeyboardH(GetViewport());
        // A point of jitter is not worth a relayout.
        if (Mathf.Abs(k - _keyboard) < 1f) return;
        _keyboard = k;
        Layout();
    }

    void Layout()
    {
        if (_title == null) return;
        Vector2 v = GetViewportRect().Size;

        // Sized here rather than anchored: an anchored child of a
        // Control with no rect of its own comes out zero by zero and
        // never draws. See ChatOverlay for the window that spent its
        // whole life invisible for this reason.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        float boxH = Mathf.Max(M59Skin.RowH, FontSize * 2.6f);
        // Three captioned boxes, the gaps between them, and two lines of
        // note under the lot. Handed to Frame so the card is the size of
        // what is in it rather than the size of the screen - on a 1080
        // frame the old column floated in the top-left quadrant with
        // six hundred pixels of nothing under it.
        // The new-account hint, as many lines as it wraps to at this
        // width (measured, not guessed: at 560 points it is three lines,
        // and a guess of two had the note drawn through its last one).
        Rect2 probe = M59Skin.Body(M59Skin.Frame(v, 0f, true, CardW(v, 560f)));
        Font howFont = _howto.GetThemeFont("font");
        float howH = howFont.GetMultilineStringSize(NewAccountHint, HorizontalAlignment.Left,
                                                    probe.Size.X, M59Skin.SmallSize).Y + 4f;
        float wantH = 3f * (CapH + boxH) + M59Skin.Gap * 2f + howH + M59Skin.Gap * 1.5f + boxH;
        // A prompt, not a list: 560 points, the width Frame caps at for
        // a card whose longest line is a server name.
        Rect2 card = M59Skin.Frame(v, wantH, true, CardW(v, 560f));
        // Up out from under the on-screen keyboard. A centred card is
        // centred on the whole glass and the keyboard takes the bottom
        // of it, so the box you are typing into sat behind the keys you
        // were typing with - reported from the phone, and the first
        // screen in the client is the worst possible place for it.
        card = M59Skin.ClearOfKeyboard(card, v, _keyboard);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);
        if (_version != null)
        {
            _version.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
            _version.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);
        }

        float x = body.Position.X, w = body.Size.X, y = body.Position.Y;

        _serverLabel.Position = new Vector2(x, y);
        _serverLabel.Size = new Vector2(w, CapH);
        _servers.Position = new Vector2(x, y + CapH);
        _servers.Size = new Vector2(w, boxH);
        y += CapH + boxH + M59Skin.Gap;

        _userLabel.Position = new Vector2(x, y);
        _userLabel.Size = new Vector2(w, CapH);
        _user.Position = new Vector2(x, y + CapH);
        _user.Size = new Vector2(w, boxH);
        y += CapH + boxH + M59Skin.Gap;

        _passLabel.Position = new Vector2(x, y);
        _passLabel.Size = new Vector2(w, CapH);
        _pass.Position = new Vector2(x, y + CapH);
        _pass.Size = new Vector2(w, boxH);
        y += CapH + boxH + M59Skin.Gap * 0.5f;

        _howto.Position = new Vector2(x, y);
        _howto.Size = new Vector2(w, howH);
        y += howH + M59Skin.Gap;

        // Inside the card, under the fields it talks about. Stranded
        // outside it - which is where a line at the bottom of the
        // screen would be - it would be the one message a player in
        // trouble never finds.
        float noteH = Mathf.Max(boxH, body.Position.Y + body.Size.Y - y);
        if (_update != null && _update.Visible)
        {
            // The verdict takes the bottom of the block and the note
            // keeps the rest, so neither grows over the other when the
            // sentence is a long one.
            float updH = Mathf.Min(noteH * 0.5f, CapH * 2f);
            noteH -= updH;
            _update.Position = new Vector2(x, y + noteH);
            _update.Size = new Vector2(w, updH);
        }
        _note.Position = new Vector2(x, y);
        _note.Size = new Vector2(w, noteH);

        // Connect last in the line, as the skin lays a footer out: the
        // primary action is the one nearest the thumb. Discord and Wiki
        // from the other end. At 560 points the four take 514 of the
        // footer's 524 (121 + 132 + 121 + 110 and three gaps), so the
        // card need not grow - but it is checked, not trusted: should a
        // caption ever lengthen, the links drop out rather than overlap
        // Connect.
        M59Skin.FootRow(foot, _go, _options);

        // The posters stand either side of the card, as tall as it,
        // and as wide as the space allows up to PosterW. Under PosterMin
        // of room (a narrow phone with the keyboard up, say) they step
        // out rather than squeeze: a poster you cannot read is noise
        // beside the one box that matters.
        float side = card.Position.X - M59Skin.Pad * 2f;
        float pw = Mathf.Min(PosterW, side);
        bool fit = pw >= PosterMin;
        _discord.Visible = _wiki.Visible = fit;
        if (fit)
        {
            _discord.Place(new Rect2(card.Position.X - M59Skin.Pad - pw, card.Position.Y, pw, card.Size.Y));
            _wiki.Place(new Rect2(card.End.X + M59Skin.Pad, card.Position.Y, pw, card.Size.Y));
        }
    }
}

/// <summary>
/// A poster beside the login card: a tall card that is one big button,
/// with a word at the top, a line or two under it, and what a tap does
/// along the bottom. Styled as a window so it reads as part of the
/// same screen; the title in gold at twice the title size so it reads
/// from across a room.
/// </summary>
public sealed partial class Poster : Button
{
    readonly Label _word, _blurb, _foot;

    public Poster(string word, string blurb, string foot, string url)
    {
        FocusMode = FocusModeEnum.None;
        var s = M59Skin.Flat(M59Skin.Card, M59Skin.Radius);
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 2;
        s.BorderColor = M59Skin.EdgeLit;
        s.ShadowColor = new Color(0, 0, 0, 0.55f);
        s.ShadowSize = 18;
        s.AntiAliasing = true;
        AddThemeStyleboxOverride("normal", s);
        var lit = (StyleBoxFlat)s.Duplicate();
        lit.BorderColor = M59Skin.Gold;
        AddThemeStyleboxOverride("hover", lit);
        AddThemeStyleboxOverride("pressed", lit);
        AddThemeStyleboxOverride("focus", s);
        Pressed += () => OS.ShellOpen(url);

        _word = new Label { Text = word, HorizontalAlignment = HorizontalAlignment.Center,
                            MouseFilter = MouseFilterEnum.Ignore };
        _word.AddThemeFontSizeOverride("font_size", 52);
        _word.AddThemeColorOverride("font_color", M59Skin.Gold);
        AddChild(_word);
        _blurb = new Label { Text = blurb, HorizontalAlignment = HorizontalAlignment.Center,
                             AutowrapMode = TextServer.AutowrapMode.WordSmart,
                             MouseFilter = MouseFilterEnum.Ignore };
        _blurb.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _blurb.AddThemeColorOverride("font_color", M59Skin.Text);
        AddChild(_blurb);
        _foot = new Label { Text = foot, HorizontalAlignment = HorizontalAlignment.Center,
                            MouseFilter = MouseFilterEnum.Ignore };
        _foot.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        _foot.AddThemeColorOverride("font_color", M59Skin.TextDim);
        AddChild(_foot);
    }

    public void Place(Rect2 r)
    {
        Position = r.Position; Size = r.Size;
        float pad = M59Skin.Pad;
        float w = r.Size.X - pad * 2f;
        _word.Position = new Vector2(pad, pad * 1.5f);
        _word.Size = new Vector2(w, 70f);
        _blurb.Position = new Vector2(pad, pad * 1.5f + 80f);
        _blurb.Size = new Vector2(w, Mathf.Max(40f, r.Size.Y - pad * 1.5f - 80f - 40f - pad));
        _foot.Position = new Vector2(pad, r.Size.Y - pad - 28f);
        _foot.Size = new Vector2(w, 28f);
    }
}
