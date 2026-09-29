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

    NewsGroup _news;
    string _signature = "";
    int _picked = -1;
    bool _composing;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Board", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _headline = Heading("", FontSize, new Color(0.75f, 0.78f, 0.84f));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _body = new RichTextLabel { BbcodeEnabled = true, Visible = false };
        _body.AddThemeFontSizeOverride("normal_font_size", FontSize);
        AddChild(_body);

        _subject = new LineEdit { PlaceholderText = "subject", Visible = false };
        _subject.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_subject);

        _text = new TextEdit { Visible = false };
        _text.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_text);

        _new = Push("New", () => Compose(null));
        _reply = Push("Reply", Reply);
        _refresh = Push("Refresh", () => Refresh?.Invoke());
        _delete = Push("Delete", Kill);
        _close = Push("Close", Close);
        _send = Push("Post", Send);
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

    Button Push(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 780f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.8f;
        _headline.Position = new Vector2(side, y); y += FontSize * 1.8f;

        if (_composing)
        {
            _subject.Position = new Vector2(side, y);
            _subject.Size = new Vector2(w, rowH); y += rowH + 6f;

            float bodyH = top + height - rowH - 8f - y;
            _text.Position = new Vector2(side, y);
            _text.Size = new Vector2(w, bodyH);

            Row(new[] { _send, _cancel }, side, top + height - rowH, w, rowH);
            return;
        }

        float buttons = rowH * 2f + 8f;
        float rest = top + height - buttons - 8f - y;
        float listH = rest * 0.45f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, listH);
        _rows.CustomMinimumSize = new Vector2(w, 0);
        y += listH + 8f;

        _body.Position = new Vector2(side, y);
        _body.Size = new Vector2(w, rest - listH - 8f);

        float by = top + height - buttons;
        Row(new[] { _new, _reply, _refresh }, side, by, w, rowH);
        Row(new[] { _delete, _close }, side, by + rowH + 8f, w, rowH);
    }

    static void Row(Button[] row, float x, float y, float w, float h)
    {
        float each = (w - 8f * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(x + i * (each + 8f), y);
            row[i].Size = new Vector2(each, h);
        }
    }

    public void Close()
    {
        if (_news != null) _news.IsVisible = false;
        _composing = false;
        Show(false);
    }

    void Show(bool on)
    {
        bool list = on && !_composing;
        bool write = on && _composing;

        _panel.Visible = on; _title.Visible = on; _headline.Visible = on;
        _scroll.Visible = list; _body.Visible = list;
        foreach (Button b in new[] { _new, _reply, _refresh, _delete, _close }) b.Visible = list;
        _subject.Visible = write; _text.Visible = write;
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
        sb.Append('|').Append(news.Text?.Length);
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
    }

    Control ArticleRow(ArticleHead a, int index)
    {
        uint number = a.Number;
        var b = new Button
        {
            Text = $"  {a.Title}\n     {a.Poster}   {a.Time.ToShortDateString()} {a.Time.ToShortTimeString()}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Flat = index != _picked,
            Name = $"article{index}",
        };
        b.AddThemeFontSizeOverride("font_size", FontSize - 2);
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
        Post?.Invoke(Truncate(_subject.Text ?? ""), _text.Text ?? "");
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
