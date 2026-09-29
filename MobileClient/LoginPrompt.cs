using System;
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
/// </summary>
public partial class LoginPrompt : Control
{
    [Export] public int FontSize = 18;

    const string Remembered = "user://account.txt";

    /// <summary>Account and password, once Connect is pressed.</summary>
    public event Action<string, string> Submitted;

    Label _title, _note;
    LineEdit _user, _pass;
    Button _go;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _title = new Label { Text = "Meridian 59" };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 14);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        AddChild(_title);

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

        float pad = Mathf.Max(20f, v.X * 0.08f);
        float w = Mathf.Min(v.X - pad * 2f, 520f);
        float x = (v.X - w) * 0.5f;
        float h = FontSize * 2.8f;
        float y = Mathf.Max(pad, v.Y * 0.26f);

        _title.Position = new Vector2(x, y - h * 2f);
        _title.Size = new Vector2(w, h * 1.6f);

        _user.Position = new Vector2(x, y);
        _user.Size = new Vector2(w, h);

        _pass.Position = new Vector2(x, y + h + 10f);
        _pass.Size = new Vector2(w, h);

        _go.Position = new Vector2(x, y + (h + 10f) * 2f + 6f);
        _go.Size = new Vector2(w, h);

        _note.Position = new Vector2(x, y + (h + 10f) * 3f + 16f);
        _note.Size = new Vector2(w, h * 2f);
    }
}
