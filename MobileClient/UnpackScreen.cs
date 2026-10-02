using System;
using Godot;

/// <summary>
/// The screen the game shows while it is copying its data out of the
/// APK or fetching it from the website, the screen it shows when that
/// cannot be done, and - on a build that fetches - the question it asks
/// before the first download.
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
///
/// The question half came with ResourceSync. Half a gigabyte over a
/// phone's data plan is not something to start on a tap that was meant
/// to open the game, so the first run says the size and asks; it is
/// the same card with two buttons, not ConfirmPopup, because the
/// player is not in the game yet and the one screen they are looking
/// at should be the one that asks them. Not now leaves the Download
/// button on it rather than a blank, so there is always a way on.
/// </summary>
public partial class UnpackScreen : Control
{
    [Export] public int FontSize = 16;

    /// <summary>Raised when the player asks to try again.</summary>
    public event Action Retry;
    /// <summary>Raised when the player agrees to the first download.</summary>
    public event Action Download;
    /// <summary>Raised when the player puts the first download off.</summary>
    public event Action Later;

    ColorRect _bg;
    Panel _card, _bar;
    Label _title;
    Label _body;
    Button _again, _download, _later;
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

        // Named, so the harness presses them by node rather than by
        // caption: "Download" is a caption that could end up on more
        // than one button in this client, and a name does not move.
        _again = new Button { Text = "Try again", Visible = false, Name = "tryAgain" };
        M59Skin.Dress(_again, M59Skin.Kind.Primary);
        _again.Pressed += () => Retry?.Invoke();
        AddChild(_again);

        _download = new Button { Text = "Download", Visible = false, Name = "downloadNow" };
        M59Skin.Dress(_download, M59Skin.Kind.Primary);
        _download.Pressed += () => Download?.Invoke();
        AddChild(_download);

        _later = new Button { Text = "Not now", Visible = false, Name = "downloadLater" };
        M59Skin.Dress(_later, M59Skin.Kind.Secondary);
        _later.Pressed += () => Later?.Invoke();
        AddChild(_later);

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

        bool foot = _again.Visible || _download.Visible || _later.Visible;
        Rect2 full = M59Skin.Frame(v, textH + barH + M59Skin.Gap * 2f, foot);
        Rect2 card = new Rect2(Mathf.Round((v.X - w) * 0.5f), full.Position.Y,
                               Mathf.Round(w), full.Size.Y);
        Rect2 body = M59Skin.Body(card, foot);

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

        // Right to left, the house order (M59Skin.FootRow): the button
        // that backs out sits under the thumb at the right edge, as No
        // does on ConfirmPopup, and the one that starts half a gigabyte
        // of download is the one a thumb has to reach for.
        if (foot) M59Skin.FootRow(M59Skin.Foot(card), _later, _download, _again);
    }

    /// <summary>
    /// The copy is running. Called from the worker thread's deferred
    /// callback, so it must survive being called before _Ready has run
    /// - which it does, because the labels are the only thing it needs
    /// and a null one means the node is not up yet and the next tick's
    /// message will land.
    /// </summary>
    public void Working(string line)
        => Working(line, "Installing game data",
                   "This happens once, and it takes a few minutes.\n" +
                   "Leave the game in front while it runs.");

    /// <summary>
    /// The same screen with its own words: the download says "use
    /// Wi-Fi" where the unpack says "a few minutes", and the title is
    /// what the player reads first.
    /// </summary>
    public void Working(string line, string title, string body)
    {
        Visible = true;
        if (_title != null) _title.Text = title;
        if (_body != null) _body.Text = body;
        Buttons(false, false, false);
        // The line carries the numbers ("...: 418 of 1065",
        // M59Paths.UnpackIfNeeded), so the bar is read out of the words
        // rather than plumbed separately - nothing else knows them, and
        // a line with no pair in it just leaves the bar where it was.
        if (_count != null) _count.Text = line;
        Bar(true);
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
        Buttons(true, false, false);
        // No bar on a refusal: nothing is moving, and a frozen bar is
        // the thing that makes a stopped copy look like a running one.
        Bar(false);
        Layout();
    }

    /// <summary>
    /// The first-run question: how much, and whether to fetch it now.
    /// Download and Not now; nothing else, and no bar.
    /// </summary>
    public void Offer(string detail)
    {
        Visible = true;
        if (_title != null) _title.Text = "Download game data";
        if (_body != null) _body.Text = detail;
        Buttons(false, true, true);
        Bar(false);
        Layout();
    }

    /// <summary>
    /// Not now was pressed. The player is parked, not stranded: the
    /// size is still on the card and so is the Download button, which
    /// is the only way on from here - there is no game without the data.
    /// </summary>
    public void Parked(string detail)
    {
        Visible = true;
        if (_title != null) _title.Text = "Game data not downloaded";
        if (_body != null) _body.Text = detail;
        Buttons(false, true, false);
        Bar(false);
        Layout();
    }

    void Buttons(bool again, bool download, bool later)
    {
        if (_again != null) _again.Visible = again;
        if (_download != null) _download.Visible = download;
        if (_later != null) _later.Visible = later;
    }

    void Bar(bool on)
    {
        if (_track != null) { _track.Visible = on; _fill.Visible = on; _count.Visible = on; }
    }

    /// <summary>
    /// Reads "<c>N of M</c>" out of the progress line and keeps it as a
    /// fraction for the bar. Display only: a line without a pair leaves
    /// the bar where it was rather than resetting it.
    ///
    /// The LAST pair in the line, and the numbers may carry a decimal
    /// point or a thousands comma. The unpack says "418 of 1065"; the
    /// download says "Downloading 312 / 4,700 - 84.2 of 467 MB", where
    /// the pair that should drive the bar is the bytes - a bar that
    /// moved by file count would sit still for the whole of a 9 MB
    /// .bgf and then jump. The old pattern, integers only, read
    /// "84.2 of 467" as "2 of 467" and the bar went backwards.
    /// </summary>
    void Fraction(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        System.Text.RegularExpressions.MatchCollection ms =
            System.Text.RegularExpressions.Regex.Matches(line, @"([\d,]+(?:\.\d+)?)\s+of\s+([\d,]+(?:\.\d+)?)");
        if (ms.Count == 0) return;
        System.Text.RegularExpressions.Match m = ms[ms.Count - 1];
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, inv, out double at)) return;
        if (!double.TryParse(m.Groups[2].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, inv, out double all) || all <= 0) return;
        _done = Mathf.Clamp((float)(at / all), 0f, 1f);
        // Beside the bar: for the unpack, just the numbers, since the
        // sentence they came in is already the title of the card; for
        // a line with no such prefix, the line itself.
        if (_count != null)
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            _count.Text = colon >= 0 ? line.Substring(colon + 2) : line;
        }
    }

    /// <summary>Done; get out of the way.</summary>
    public void Done() { Visible = false; }
}
