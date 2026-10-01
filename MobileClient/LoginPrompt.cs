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

    Label _title, _note, _serverLabel, _userLabel, _passLabel;
    ColorRect _bg;
    Panel _card, _bar;
    LineEdit _user, _pass;
    Button _go, _options;
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

        // The progress line and every refusal. Body-sized rather than
        // the small print it was: when something goes wrong this is the
        // only thing the player is given.
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _note.AddThemeFontSizeOverride("font_size", M59Skin.BodySize - 2);
        _note.AddThemeColorOverride("font_color", NoteCalm);
        AddChild(_note);

        Recall();

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>Where we are connecting, so it is never a mystery.</summary>
    public void Server(string host, int port) { if (_note != null) _note.Text = $"{host}:{port}"; }

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
        _note.Text = $"{rows[at].Host}:{rows[at].Port}";
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

    void Recall()
    {
        try
        {
            if (!FileAccess.FileExists(Remembered)) { _user.GrabFocus(); return; }
            using FileAccess f = FileAccess.Open(Remembered, FileAccess.ModeFlags.Read);
            string saved = f?.GetAsText()?.Trim();
            if (!string.IsNullOrEmpty(saved)) { _user.Text = saved; _pass.GrabFocus(); }
            else _user.GrabFocus();
        }
        catch { _user.GrabFocus(); }
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
        float wantH = 3f * (CapH + boxH) + M59Skin.Gap * 2f + M59Skin.Gap * 1.5f + boxH;
        // A prompt, not a list: 560 points, the width Frame caps at for
        // a card whose longest line is a server name.
        Rect2 card = M59Skin.Frame(v, wantH, true, CardW(v, 560f));
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

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
        y += CapH + boxH + M59Skin.Gap * 1.5f;

        // Inside the card, under the fields it talks about. Stranded
        // outside it - which is where a line at the bottom of the
        // screen would be - it would be the one message a player in
        // trouble never finds.
        _note.Position = new Vector2(x, y);
        _note.Size = new Vector2(w, Mathf.Max(boxH, body.Position.Y + body.Size.Y - y));

        // Connect last in the line, as the skin lays a footer out: the
        // primary action is the one nearest the thumb.
        M59Skin.FootRow(foot, _go, _options);
    }
}
