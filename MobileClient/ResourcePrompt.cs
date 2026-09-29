using System;
using Godot;

/// <summary>
/// Asks where the game's files are, when none of the usual places has
/// them.
///
/// Worth having because the usual places are guesses: installs move,
/// people keep them on another drive, and a client that just says "not
/// found" and stops is a client you cannot use. What is typed is checked
/// for actual room and bitmap files before it is accepted, and remembered
/// so the question is asked once.
/// </summary>
public partial class ResourcePrompt : Control
{
    [Export] public int FontSize = 16;

    /// <summary>Raised with a folder that has been checked and remembered.</summary>
    public event Action<string> Accepted;

    Label _message;
    LineEdit _entry;
    Button _use;
    ColorRect _bg;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f) };
        AddChild(_bg);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _message.AddThemeFontSizeOverride("font_size", FontSize);
        _message.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.7f));
        AddChild(_message);

        _entry = new LineEdit { PlaceholderText = @"C:\Meridian-104\resource" };
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _entry.TextSubmitted += _ => Try();
        AddChild(_entry);

        _use = new Button { Text = "Use this folder" };
        _use.AddThemeFontSizeOverride("font_size", FontSize);
        _use.Pressed += Try;
        AddChild(_use);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_message == null) return;
        Vector2 v = GetViewportRect().Size;

        // Sized here rather than anchored: an anchored child of a
        // Control with no rect of its own comes out zero by zero and
        // never draws. See ChatOverlay for the window that spent its
        // whole life invisible for this reason.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        float pad = Mathf.Max(16f, v.X * 0.06f);
        float w = v.X - pad * 2f;
        float h = FontSize * 2.6f;

        _message.Position = new Vector2(pad, pad);
        _message.Size = new Vector2(w, v.Y * 0.55f);

        _entry.Position = new Vector2(pad, pad + v.Y * 0.58f);
        _entry.Size = new Vector2(w, h);

        _use.Position = new Vector2(pad, pad + v.Y * 0.58f + h + 10f);
        _use.Size = new Vector2(w * 0.5f, h);
    }

    /// <summary>Shows the prompt with the list of places already tried.</summary>
    public void Ask(string whatWasTried)
    {
        if (_message != null) _message.Text = whatWasTried;
        Visible = true;
        Layout();
        _entry?.GrabFocus();
    }

    void Try()
    {
        string dir = _entry.Text;
        if (M59Paths.Remember(dir))
        {
            Visible = false;
            Accepted?.Invoke(dir.Trim().Trim('"'));
        }
        else
        {
            _message.Text = $"No .roo or .bgf files in:\n  {dir}\n\n" +
                            "That should be the 'resource' folder inside an installed client.";
        }
    }
}
