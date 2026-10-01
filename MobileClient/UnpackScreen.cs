using System;
using Godot;

/// <summary>
/// The screen the game shows while it is copying its data out of the
/// APK, and the screen it shows when that cannot be done.
///
/// Both halves earn their place. The progress half used to be one line
/// of text in the corner - `_status.Text = "Unpacking game data..."` -
/// over a black frame, for several minutes, on first run; a phone
/// player has no reason to believe that is not a hang. The refusal half
/// did not exist at all: a device with no room for ~470MB of game data
/// wrote what it could, swallowed every error into logcat and went on
/// to the login screen, so the fault surfaced much later as a world
/// with no walls in it.
///
/// It is the client's own UI, in-page, as everything here is - Ashton's
/// standing rule, and in any case Godot's own alert() on Android is a
/// toast-shaped thing you cannot read a path out of.
/// </summary>
public partial class UnpackScreen : Control
{
    [Export] public int FontSize = 16;

    /// <summary>Raised when the player asks to try again.</summary>
    public event Action Retry;

    ColorRect _bg;
    Label _title;
    Label _body;
    Button _again;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f) };
        AddChild(_bg);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", FontSize + 8);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.88f, 0.6f));
        AddChild(_title);

        // Wrapped, because every message this screen carries names a
        // path or a number of megabytes and a clipped path is no use to
        // anybody trying to report it.
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _body.AddThemeFontSizeOverride("font_size", FontSize);
        _body.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
        AddChild(_body);

        _again = new Button { Text = "Try again", Visible = false };
        _again.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _again.Pressed += () => Retry?.Invoke();
        AddChild(_again);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_body == null) return;
        Vector2 v = GetViewportRect().Size;

        // Sized, not anchored: an anchored child of a Control with no
        // rect of its own comes out zero by zero and never draws. Same
        // trap ResourcePrompt documents.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        float pad = Panels.Side(v, 0.06f);
        float w = v.X - pad * 2f;

        _title.Position = new Vector2(pad, pad);
        _title.Size = new Vector2(w, (FontSize + 8) * 1.6f);

        _body.Position = new Vector2(pad, pad + (FontSize + 8) * 2.4f);
        _body.Size = new Vector2(w, v.Y * 0.6f);

        float h = FontSize * 2.6f;
        _again.Position = new Vector2(pad, v.Y - pad - h);
        _again.Size = new Vector2(Math.Min(w, 260f), h);
    }

    /// <summary>
    /// The copy is running. Called from the worker thread's deferred
    /// callback, so it must survive being called before _Ready has run
    /// - which it does, because the labels are the only thing it needs
    /// and a null one means the node is not up yet and the next tick's
    /// message will land.
    /// </summary>
    public void Working(string line)
    {
        Visible = true;
        if (_title != null) _title.Text = "Installing game data";
        if (_body != null)
            _body.Text = line + "\n\nThis happens once, and it takes a few minutes.\n" +
                         "Leave the game in front while it runs.";
        if (_again != null) _again.Visible = false;
    }

    /// <summary>
    /// It cannot be done, and here is why, in words with a path or a
    /// size in them. The button is offered because every refusal this
    /// screen carries is one the player can act on and then retry -
    /// freeing space, most of all - and a copy that stopped half way
    /// resumes rather than starting over.
    /// </summary>
    public void Problem(string detail)
    {
        Visible = true;
        if (_title != null) _title.Text = "Game data not installed";
        if (_body != null) _body.Text = detail;
        if (_again != null) _again.Visible = true;
        Layout();
    }

    /// <summary>Done; get out of the way.</summary>
    public void Done() { Visible = false; }
}
