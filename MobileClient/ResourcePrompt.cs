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
    Panel _card, _bar;
    Label _title;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _bg = new ColorRect { Color = M59Skin.Scrim };
        AddChild(_bg);

        // A card, like every other panel: this is the first thing a new
        // player ever sees, and a wall of text on black reads as a
        // crash rather than as a question.
        _card = M59Skin.Window();
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        AddChild(_bar);
        _title = M59Skin.Title("Where is the game data?");
        AddChild(_title);

        // Dim, because it is the EVIDENCE - the places already tried -
        // and the thing being asked for is the box under it.
        _message = M59Skin.Body("", true);
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_message);

        _entry = new LineEdit { PlaceholderText = @"C:\Meridian-104\resource" };
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _entry.AddThemeColorOverride("font_color", M59Skin.Text);
        _entry.AddThemeColorOverride("caret_color", M59Skin.Gold);
        _entry.AddThemeStyleboxOverride("normal", Sunk());
        _entry.AddThemeStyleboxOverride("focus", Sunk());
        _entry.TextSubmitted += _ => Try();
        AddChild(_entry);

        _use = new Button { Text = "Use this folder" };
        M59Skin.Dress(_use, M59Skin.Kind.Primary);
        _use.Pressed += Try;
        AddChild(_use);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>The typed path's box: the card's own dark, sunk into it.</summary>
    static StyleBoxFlat Sunk()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.047f, 0.043f, 0.039f),
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
        };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 8;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 1;
        return s;
    }

    void Layout()
    {
        if (_message == null) return;
        if (!IsInsideTree()) return;
        Vector2 v = GetViewportRect().Size;

        // Sized here rather than anchored: an anchored child of a
        // Control with no rect of its own comes out zero by zero and
        // never draws. See ChatOverlay for the window that spent its
        // whole life invisible for this reason.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        float box = FontSize * 2.8f;
        float lead = M59Skin.BodySize * 1.4f;

        // Measured, so the card is as tall as the list of places tried
        // and no taller - the list is three lines on one device and
        // seven on another.
        Rect2 probe = M59Skin.Frame(v);
        float w = Mathf.Min(probe.Size.X, Mathf.Clamp(v.X * 0.56f, 460f, 900f));
        float wrap = w - M59Skin.Pad * 2f;
        float textH = _message.GetThemeFont("font").GetMultilineStringSize(
            _message.Text ?? "", HorizontalAlignment.Left, wrap,
            _message.GetThemeFontSize("font_size")).Y;

        Rect2 full = M59Skin.Frame(v, textH + M59Skin.Gap * 2f + box);
        Rect2 card = new Rect2(Mathf.Round((v.X - w) * 0.5f), full.Position.Y,
                               Mathf.Round(w), full.Size.Y);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        // The evidence fills the body; the box sits on the bottom of it,
        // directly over the button that acts on what is typed in it.
        _message.Position = body.Position;
        _message.Size = new Vector2(body.Size.X, Mathf.Max(lead, body.Size.Y - box - M59Skin.Gap));
        _entry.Position = new Vector2(body.Position.X, body.Position.Y + body.Size.Y - box);
        _entry.Size = new Vector2(body.Size.X, box);

        M59Skin.FootRow(foot, _use);
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
