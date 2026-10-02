using System;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// A bulletin board: the articles on it, the one you are reading, and
/// the one you are writing.
///
/// `UINewsGroup.cpp` and `UINewsGroupCompose.cpp`, two windows in the
/// game and one panel here for the same reason mail is: a phone cannot
/// usefully show a list and a compose form at once.
///
/// The board belongs to an object in the world - a news globe - and the
/// server decides when you are looking at one. A LookNewsGroup message
/// fills `Data.NewsGroup` and raises IsVisible, and `BaseClient`
/// immediately asks for the article list on your behalf. So this
/// follows that flag and never opens itself.
///
/// The list is headers only. `ArticleHead` carries a number, a title,
/// an author and a time, and the body is a second round trip:
/// selecting a row sends `SendReqArticle(number)` and the answer lands
/// in `NewsGroup.Text`. So reading is not free, and the text box is
/// empty until a row is picked.
///
/// One thing the file does not do, which this does: `HandleArticles`
/// adds to the list rather than replacing it, so pressing Refresh in
/// the game appends the board to itself. The list is cleared here
/// before re-asking.
///
/// Posting sends `SendPostArticle(globe, title, text)` and then
/// re-requests, because a posted article does not come back on its own.
/// Reply prefixes "Re: " unless the title already has one - the same
/// rule as mail, including the German client's "Aw:" - and truncates
/// to the server's own subject limit rather than letting the server
/// refuse it.
///
/// Left out: the Mail author button, which in the game's own file is an
/// empty handler that does nothing at all.
/// </summary>
public partial class NewsPanel : Control
{
    // The air under the last button row that this file used to add by
    // hand is M59Skin's footer band now: Foot() insets the buttons
    // inside it, so the card's edge and a button's are never one line.

    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 52;

    /// <summary>Ask for the article list again.</summary>
    public event Action Refresh;
    /// <summary>Read this article.</summary>
    public event Action<uint> Read;
    /// <summary>Post: title and text, to the board being read.</summary>
    public event Action<string, string> Post;
    /// <summary>Remove this article from the board.</summary>
    public event Action<uint> Remove;

    ColorRect _panel;
    Label _title, _headline;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    RichTextLabel _body;
    LineEdit _subject;
    TextEdit _text;
    Button _new, _reply, _refresh, _delete, _close, _send, _cancel;

    // The shared chrome: a card over a scrim with a title bar and a
    // round close, rather than a full-screen rectangle. See M59Skin.
    Panel _card, _bar;
    Button _x;
    /// <summary>The article's surface, so the reading reads as reading.</summary>
    Panel _page;
    /// <summary>The article's own title, poster and date, over its text.</summary>
    Label _articleHead;
    /// <summary>What a board with nothing pinned to it says.</summary>
    Label _empty;
    /// <summary>Captions for the compose boxes.</summary>
    Label _subjectLabel, _textLabel;

    NewsGroup _news;
    string _signature = "";
    int _picked = -1;
    bool _composing;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window(); _card.Visible = false; AddChild(_card);
        _bar = M59Skin.TitleBar(); _bar.Visible = false; AddChild(_bar);

        _title = M59Skin.Title("Board"); _title.Visible = false; AddChild(_title);
        _x = M59Skin.CloseX(Close); _x.Visible = false; AddChild(_x);
        // The board's own headline, under the title bar: it belongs to
        // the board rather than to any one article.
        _headline = Heading("", M59Skin.BodySize, M59Skin.TextDim);

        _page = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _page.AddThemeStyleboxOverride("panel", Sunken());
        AddChild(_page);

        _articleHead = new Label { Visible = false, ClipText = true };
        _articleHead.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _articleHead.AddThemeColorOverride("font_color", M59Skin.Gold);
        AddChild(_articleHead);

        _empty = M59Skin.Empty("Nothing posted here yet.");
        _empty.Visible = false;
        AddChild(_empty);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        _rows.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _body = new RichTextLabel { BbcodeEnabled = true, Visible = false };
        _body.AddThemeFontSizeOverride("normal_font_size", M59Skin.BodySize);
        _body.AddThemeColorOverride("default_color", M59Skin.Text);
        // An article is prose: it needs the line spacing a notice board
        // would have given it on paper.
        _body.AddThemeConstantOverride("line_separation", 7);
        AddChild(_body);

        _subjectLabel = FieldLabel("Subject");
        _textLabel = FieldLabel("Posting");

        _subject = new LineEdit { PlaceholderText = "what it is about", Visible = false };
        _subject.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _subject.AddThemeColorOverride("font_color", M59Skin.Text);
        _subject.AddThemeColorOverride("font_placeholder_color", M59Skin.TextOff);
        _subject.AddThemeStyleboxOverride("normal", Sunken());
        _subject.AddThemeStyleboxOverride("focus", Sunken(true));
        AddChild(_subject);

        _text = new TextEdit { Visible = false };
        _text.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _text.AddThemeColorOverride("font_color", M59Skin.Text);
        _text.AddThemeStyleboxOverride("normal", Sunken());
        _text.AddThemeStyleboxOverride("focus", Sunken(true));
        AddChild(_text);

        _new = Push("New", () => Compose(null));
        _reply = Push("Reply", Reply);
        _refresh = Push("Refresh", () => Refresh?.Invoke());
        _delete = Push("Delete", Kill, M59Skin.Kind.Danger);
        _close = Push("Close", Close);
        _send = Push("Post", Send, M59Skin.Kind.Primary);
        _cancel = Push("Cancel", () => { _composing = false; Show(true); });

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    /// <summary>
    /// An inset surface: the article's page and the inside of a box on
    /// the compose form. M59Skin has raised chrome and rows but nothing
    /// that reads as "type in here".
    /// </summary>
    static StyleBoxFlat Sunken(bool lit = false)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.051f, 0.043f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = lit ? M59Skin.Gold : M59Skin.Rule,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 8,
            AntiAliasing = true,
        };
    }

    /// <summary>A caption over a box, in the small print.</summary>
    Label FieldLabel(string text)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        l.AddThemeColorOverride("font_color", M59Skin.GoldDim);
        AddChild(l);
        return l;
    }

    Button Push(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// How wide a line of prose may run before the eye loses the next
    /// one. The card is as wide as a sideways phone; the article is not.
    /// </summary>
    const float Measure = 760f;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        Rect2 card = M59Skin.Frame(v);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position; _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        float headH = string.IsNullOrEmpty(_headline.Text) ? 0f : M59Skin.BodySize + 12f;
        _headline.Position = new Vector2(body.Position.X, body.Position.Y);
        _headline.Size = new Vector2(body.Size.X, headH);
        float top = body.Position.Y + headH;
        float rest = body.Size.Y - headH;

        if (_composing)
        {
            // Held to a form's width: a subject box the width of a
            // sideways phone is a trough, not a field.
            float fw = Mathf.Min(body.Size.X, 900f);
            float fx = body.Position.X + Mathf.Round((body.Size.X - fw) * 0.5f);
            float lh = M59Skin.SmallSize + 8f, fh = 46f, y = top;

            _subjectLabel.Position = new Vector2(fx, y); _subjectLabel.Size = new Vector2(fw, lh); y += lh;
            _subject.Position = new Vector2(fx, y); _subject.Size = new Vector2(fw, fh);
            y += fh + M59Skin.Gap;
            _textLabel.Position = new Vector2(fx, y); _textLabel.Size = new Vector2(fw, lh); y += lh;
            _text.Position = new Vector2(fx, y);
            _text.Size = new Vector2(fw, Mathf.Max(fh, top + rest - y));

            M59Skin.FootRow(foot, _cancel, _send);
            return;
        }

        // The headers on one side, the article on the other, when there
        // is room for both; stacked when there is not. Reading an
        // article should not mean losing sight of the board.
        bool wide = body.Size.X >= 980f;
        float listW = wide ? Mathf.Clamp(body.Size.X * 0.40f, 320f, 560f) : body.Size.X;
        float listH = wide ? rest : Mathf.Max(M59Skin.RowH * 2f, rest * 0.42f);

        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(listW, listH);
        // 14 was the old bar's width and the bar is 28 now, so the
        // rows ran under the grabber. One number, in the skin.
        M59Skin.RowsFit(_rows, new Rect2(Vector2.Zero, new Vector2(listW, 0)));
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        float rx = wide ? body.Position.X + listW + M59Skin.Pad : body.Position.X;
        float ry = wide ? top : top + listH + M59Skin.Pad;
        float rw = wide ? body.Size.X - listW - M59Skin.Pad : body.Size.X;
        float rh = wide ? rest : rest - listH - M59Skin.Pad;

        _page.Position = new Vector2(rx, ry);
        _page.Size = new Vector2(rw, Mathf.Max(0f, rh));

        float inner = Mathf.Min(rw - M59Skin.Pad * 2f, Measure);
        float ix = rx + Mathf.Round((rw - inner) * 0.5f);
        _articleHead.Position = new Vector2(ix, ry + M59Skin.Pad);
        _articleHead.Size = new Vector2(inner, M59Skin.BodySize + 10f);
        float bodyTop = ry + M59Skin.Pad + M59Skin.BodySize + 10f + M59Skin.Gap;
        _body.Position = new Vector2(ix, bodyTop);
        _body.Size = new Vector2(inner, Mathf.Max(0f, ry + rh - M59Skin.Pad - bodyTop));

        // Right to left: Close under the dismissing thumb, New - the
        // reason to be at a board you cannot read your way out of -
        // furthest from it.
        M59Skin.FootRow(foot, _close, _delete, _refresh, _reply, _new);
    }

    public void Close()
    {
        if (_news != null) _news.IsVisible = false;
        _composing = false;
        Show(false);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        bool list = on && !_composing;
        bool write = on && _composing;

        _panel.Visible = on; _title.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _headline.Visible = list && !string.IsNullOrEmpty(_headline.Text);
        _scroll.Visible = list; _body.Visible = list;
        // The page is only meaningful once an article has been asked
        // for; until then the right half is honestly empty.
        _page.Visible = list && _picked >= 0;
        _articleHead.Visible = _page.Visible;
        _empty.Visible = list && _rows.GetChildCount() == 0;
        foreach (Button b in new[] { _new, _reply, _refresh, _delete, _close }) b.Visible = list;
        _subject.Visible = write; _text.Visible = write;
        _subjectLabel.Visible = write; _textLabel.Visible = write;
        _send.Visible = write; _cancel.Visible = write;

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Sync(NewsGroup news)
    {
        if (_rows == null) return;

        if (news == null || !news.IsVisible)
        {
            if (IsOpen) { _news = news; _composing = false; Show(false); _signature = ""; }
            return;
        }

        _news = news;
        if (!IsOpen) Show(true);
        if (_composing) return;

        var sb = new System.Text.StringBuilder();
        sb.Append(news.NewsGlobeID).Append('|').Append(news.Headline).Append('|');
        if (news.Articles != null)
            foreach (ArticleHead a in news.Articles) sb.Append(a?.Number).Append(';');
        // The text itself, not its length: tapping a row rebuilds with the
        // OLD body before the new one arrives, and two articles whose
        // bodies are the same length (or both empty) left the signature
        // unchanged, so the new body was never assigned.
        sb.Append('|').Append(news.Text);
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _title.Text = news.NewsGlobeObject != null && !string.IsNullOrWhiteSpace(news.NewsGlobeObject.Name)
            ? news.NewsGlobeObject.Name : "Board";
        _headline.Text = news.Headline ?? "";

        // The body arrives on its own message, after a row was asked
        // for, so it is set here rather than when the row was tapped.
        string text = news.Text ?? "";
        _body.Text = text.Replace("[", "[lb]");

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        int i = 0;
        if (news.Articles != null)
            foreach (ArticleHead a in news.Articles)
                if (a != null) _rows.AddChild(ArticleRow(a, i++));

        if (_picked >= i) _picked = -1;

        // The article's own heading over its text, and the honest empty
        // line when the board has nothing on it. Both are display only:
        // what is picked and what was asked for are unchanged.
        ArticleHead chosen = Picked();
        _articleHead.Text = chosen != null
            ? $"{chosen.Title}   -   {chosen.Poster}, {chosen.Time.ToShortDateString()}" : "";
        _page.Visible = IsOpen && !_composing && chosen != null;
        _articleHead.Visible = _page.Visible;
        _empty.Visible = IsOpen && !_composing && i == 0;
        _headline.Visible = IsOpen && !_composing && !string.IsNullOrEmpty(_headline.Text);
        // The headline's height is part of the body's arithmetic, so a
        // board that has just named itself has to be laid out again.
        Layout();
    }

    Control ArticleRow(ArticleHead a, int index)
    {
        uint number = a.Number;
        var b = new Button
        {
            Text = $"  {a.Title}\n     {a.Poster}   {a.Time.ToShortDateString()} {a.Time.ToShortTimeString()}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, M59Skin.RowH),
            Name = $"article{index}",
        };
        // Dressed as a list row, striped by position, and the chosen
        // one marked - which is what Flat was doing, in a way that
        // showed only as the absence of a border.
        M59Skin.Dress(b, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
        M59Skin.Pick(b, index == _picked, index % 2 != 0);
        b.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        b.Pressed += () =>
        {
            _picked = index;
            _signature = "";
            Read?.Invoke(number);
        };
        return b;
    }

    ArticleHead Picked()
    {
        if (_news?.Articles == null || _picked < 0 || _picked >= _news.Articles.Count) return null;
        return _news.Articles[_picked];
    }

    void Reply()
    {
        ArticleHead a = Picked();
        if (a == null) return;

        string title = a.Title ?? "";
        bool prefix = !(title.StartsWith("Re:") || title.StartsWith("Aw:"));
        Compose(Truncate((prefix ? "Re: " : "") + title));
    }

    /// <summary>
    /// The server's own subject limit, applied here rather than left
    /// for the server to refuse.
    /// </summary>
    static string Truncate(string s)
    {
        int max = Meridian59.Common.Constants.BlakservStringLengths.NEWS_POSTING_MAX_SUBJECT_LENGTH;
        return s != null && s.Length > max ? s.Substring(0, max) : s;
    }

    void Compose(string subject)
    {
        _subject.Text = subject ?? "";
        _text.Text = "";
        _composing = true;
        Show(true);
    }

    void Send()
    {
        // The body has its own cap, NEWS_POSTING_MAX_LENGTH - 1, as the
        // reference sets on the box (`UINewsGroupCompose.cpp:17-18`).
        // Godot's multi-line box has no MaxLength, so it is cut here.
        string body = _text.Text ?? "";
        int cap = Meridian59.Common.Constants.BlakservStringLengths.NEWS_POSTING_MAX_LENGTH - 1;
        if (body.Length > cap) body = body.Substring(0, cap);
        Post?.Invoke(Truncate(_subject.Text ?? ""), body);
        _subject.Text = ""; _text.Text = "";
        _composing = false;
        _signature = "";
        Show(true);
    }

    void Kill()
    {
        ArticleHead a = Picked();
        if (a == null) return;
        Remove?.Invoke(a.Number);
        _picked = -1;
        _signature = "";
    }
}
