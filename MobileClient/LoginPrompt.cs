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

    Label _title, _note, _serverLabel;
    ColorRect _bg;
    LineEdit _user, _pass;
    Button _go, _options;
    OptionButton _servers;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f) };
        AddChild(_bg);

        _title = new Label { Text = "Meridian 59" };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 14);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        AddChild(_title);

        // Above the account, as it is in the layout: Server at y=75,
        // Username at y=110, Password at y=140
        // (`Meridian59.layout:3045`, :3058, :3068). The order is not
        // arbitrary - which server you are on decides which account name
        // means anything, so it is the first thing asked.
        _serverLabel = new Label { Text = "Server" };
        _serverLabel.AddThemeFontSizeOverride("font_size", FontSize - 2);
        _serverLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.8f));
        AddChild(_serverLabel);

        // An OptionButton rather than a row of steppers, because this is
        // the reference's own control: a read-only combobox
        // (`Meridian59.layout:3047` sets ReadOnly True, so it picks from
        // the list and never takes typing). Godot draws its list as a
        // popup inside the game window, which is what the house rule
        // wants - no OS dialog anywhere near it.
        _servers = new OptionButton { Name = "serverPick" };
        _servers.AddThemeFontSizeOverride("font_size", FontSize);
        _servers.ItemSelected += index => Pick((int)index);
        AddChild(_servers);

        _user = new LineEdit { PlaceholderText = "account" };
        _user.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _user.TextSubmitted += _ => Go();
        AddChild(_user);

        _pass = new LineEdit { PlaceholderText = "password", Secret = true };
        _pass.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _pass.TextSubmitted += _ => Go();
        AddChild(_pass);

        _go = new Button { Text = "Connect" };
        _go.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _go.Pressed += Go;
        AddChild(_go);

        // The layout's own shape: a small square button on the same line
        // as Connect, at the right-hand end (`Meridian59.layout:3074-3080`
        // puts it at x 100..125 against Connect's 0..100). Spelt out
        // rather than iconised because there are no CEGUI icon images
        // here to borrow.
        _options = new Button { Text = "Settings", Name = "loginOptions" };
        _options.AddThemeFontSizeOverride("font_size", FontSize - 2);
        _options.Pressed += () => Options?.Invoke();
        AddChild(_options);

        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _note.AddThemeFontSizeOverride("font_size", FontSize - 4);
        _note.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.66f));
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
        _note.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.66f));
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
        _note.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0.45f));
        _go.Disabled = false;
    }

    void Go()
    {
        string u = _user.Text.Trim(), p = _pass.Text;
        if (u.Length == 0) { _user.GrabFocus(); return; }
        if (p.Length == 0) { _pass.GrabFocus(); return; }

        Remember(u);
        _go.Disabled = true;
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


        float pad = Mathf.Max(20f, v.X * 0.08f);
        float w = Mathf.Min(v.X - pad * 2f, 520f);
        float x = (v.X - w) * 0.5f;
        float h = FontSize * 2.8f;
        float step = h + 10f;
        // Two rows taller than it was - a label and the picker - so the
        // column starts higher to keep the whole of it on a short screen
        // held sideways. Clamped rather than computed from the middle
        // because the title sits two rows ABOVE y and would otherwise
        // walk off the top.
        float y = Mathf.Max(h * 2f + pad, v.Y * 0.20f);

        _title.Position = new Vector2(x, y - h * 2f);
        _title.Size = new Vector2(w, h * 1.6f);

        _serverLabel.Position = new Vector2(x, y);
        _serverLabel.Size = new Vector2(w, FontSize * 1.6f);

        _servers.Position = new Vector2(x, y + FontSize * 1.7f);
        _servers.Size = new Vector2(w, h);

        float row = y + FontSize * 1.7f + step;

        _user.Position = new Vector2(x, row);
        _user.Size = new Vector2(w, h);

        _pass.Position = new Vector2(x, row + step);
        _pass.Size = new Vector2(w, h);

        // Connect and Settings share the line, as they do in the layout
        // (`Meridian59.layout:3070`, :3076): Connect takes the width it
        // always did less the small button and the gap.
        float side = Mathf.Max(96f, w * 0.25f);
        _go.Position = new Vector2(x, row + step * 2f + 6f);
        _go.Size = new Vector2(w - side - 8f, h);

        _options.Position = new Vector2(x + w - side, row + step * 2f + 6f);
        _options.Size = new Vector2(side, h);

        _note.Position = new Vector2(x, row + step * 3f + 16f);
        _note.Size = new Vector2(w, h * 2f);
    }
}
