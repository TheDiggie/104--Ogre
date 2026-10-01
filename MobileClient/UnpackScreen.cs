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
    Panel _card, _bar;
    Label _title;
    Label _body;
    Button _again;
    /// <summary>The bar: a track with a fill in it, and the count beside.</summary>
    ColorRect _track, _fill;
    Label _count;
    /// <summary>Where the copy is, 0..1; negative means "no number yet".</summary>
    float _done = -1f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _bg = new ColorRect { Color = M59Skin.Scrim };
        AddChild(_bg);

        // A card, like every other panel. This screen is up for minutes
        // on first run, and a sentence on a black field is what a crash
        // looks like.
        _card = M59Skin.Window();
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        AddChild(_bar);

        _title = M59Skin.Title("");
        AddChild(_title);

        // The progress is the point of the working half: a player who
        // can see the bar move knows the phone is not hung, which is
        // the whole complaint this screen was built for.
        _track = new ColorRect { Color = new Color(0.047f, 0.043f, 0.039f) };
        AddChild(_track);
        _fill = new ColorRect { Color = M59Skin.Gold };
        AddChild(_fill);
        _count = M59Skin.Body("", true);
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_count);

        // Wrapped, because every message this screen carries names a
        // path or a number of megabytes and a clipped path is no use to
        // anybody trying to report it.
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _body.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _body.AddThemeColorOverride("font_color", M59Skin.Text);
        AddChild(_body);

        _again = new Button { Text = "Try again", Visible = false };
        M59Skin.Dress(_again, M59Skin.Kind.Primary);
        _again.Pressed += () => Retry?.Invoke();
        AddChild(_again);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_body == null) return;
        if (!IsInsideTree()) return;
        Vector2 v = GetViewportRect().Size;

        // Sized, not anchored: an anchored child of a Control with no
        // rect of its own comes out zero by zero and never draws. Same
        // trap ResourcePrompt documents.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        bool bar = _track.Visible;
        float barH = bar ? 14f + M59Skin.Gap + M59Skin.BodySize * 1.5f : 0f;

        // Measured, so the card is the size of what it has to say: the
        // working half is two lines and a refusal naming a path and two
        // sizes is five.
        Rect2 probe = M59Skin.Frame(v);
        float w = Mathf.Min(probe.Size.X, Mathf.Clamp(v.X * 0.52f, 440f, 860f));
        float wrap = w - M59Skin.Pad * 2f;
        float textH = _body.GetThemeFont("font").GetMultilineStringSize(
            _body.Text ?? "", HorizontalAlignment.Left, wrap,
            _body.GetThemeFontSize("font_size")).Y;

        Rect2 full = M59Skin.Frame(v, textH + barH + M59Skin.Gap * 2f, _again.Visible);
        Rect2 card = new Rect2(Mathf.Round((v.X - w) * 0.5f), full.Position.Y,
                               Mathf.Round(w), full.Size.Y);
        Rect2 body = M59Skin.Body(card, _again.Visible);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        _body.Position = body.Position;
        _body.Size = new Vector2(body.Size.X, Mathf.Max(M59Skin.BodySize * 1.4f, body.Size.Y - barH));

        // The bar goes at the FOOT of the body, under the words, with
        // the count at its right end: one glance says both how far it
        // has got and that it is still going.
        if (bar)
        {
            float y = body.Position.Y + body.Size.Y - 14f;
            _track.Position = new Vector2(body.Position.X, y);
            _track.Size = new Vector2(body.Size.X, 14f);
            _fill.Position = _track.Position;
            // A copy that has not reported a count yet shows a sliver
            // rather than an empty trough, which reads as stalled.
            float at = _done < 0f ? 0.02f : Mathf.Clamp(_done, 0.02f, 1f);
            _fill.Size = new Vector2(Mathf.Round(body.Size.X * at), 14f);
            _count.Position = new Vector2(body.Position.X, y - M59Skin.BodySize * 1.5f);
            _count.Size = new Vector2(body.Size.X, M59Skin.BodySize * 1.5f);
        }

        if (_again.Visible) M59Skin.FootRow(M59Skin.Foot(card), _again);
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
            _body.Text = "This happens once, and it takes a few minutes.\n" +
                         "Leave the game in front while it runs.";
        if (_again != null) _again.Visible = false;
        // The line carries the numbers ("...: 418 of 1065",
        // M59Paths.UnpackIfNeeded), so the bar is read out of the words
        // rather than plumbed separately - nothing else knows them, and
        // a line with no pair in it just leaves the bar where it was.
        if (_count != null) _count.Text = line;
        if (_track != null) { _track.Visible = true; _fill.Visible = true; _count.Visible = true; }
        Fraction(line);
        Layout();
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
        // No bar on a refusal: nothing is moving, and a frozen bar is
        // the thing that makes a stopped copy look like a running one.
        if (_track != null) { _track.Visible = false; _fill.Visible = false; _count.Visible = false; }
        Layout();
    }

    /// <summary>
    /// Reads "<c>N of M</c>" out of the progress line and keeps it as a
    /// fraction for the bar. Display only: a line without a pair leaves
    /// the bar where it was rather than resetting it.
    /// </summary>
    void Fraction(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        System.Text.RegularExpressions.Match m =
            System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+of\s+(\d+)");
        if (!m.Success) return;
        if (!long.TryParse(m.Groups[1].Value, out long at)) return;
        if (!long.TryParse(m.Groups[2].Value, out long all) || all <= 0) return;
        _done = Mathf.Clamp((float)(at / (double)all), 0f, 1f);
        // Just the numbers beside the bar: the sentence they came in is
        // already the title of the card.
        if (_count != null) _count.Text = $"{at} of {all}";
    }

    /// <summary>Done; get out of the way.</summary>
    public void Done() { Visible = false; }
}
