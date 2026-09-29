using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// Mail: the list, the message, and the one you are writing.
///
/// `UIMail.cpp` and `UIMailCompose.cpp`, which are two windows in the
/// game and one panel here, because a phone cannot usefully show a list
/// and a compose form at once.
///
/// The list is not over anything on `Data`. Mail lives in
/// `ResourceManager.Mails`, saved to disk as XML: `BaseClient` takes an
/// arriving Mail message, immediately asks the server to delete that
/// copy, renumbers it to one past the highest one held locally, and
/// files it. So the mailbox is the client's own, the server only ever
/// hands over what is new, and Refresh means "ask for anything I have
/// not collected" rather than "reload".
///
/// Sending is two round trips, and that is the file's design rather
/// than an accident. Send does not send: it splits the recipients on
/// commas, trims them, and asks the server for their object ids with
/// `SendReqLookupNames`. The answer comes back as a LookupNames message
/// with one id per name in the order asked, and an id of zero means
/// there is no such player. Only when every name resolved does
/// `SendSendMail` go out; otherwise the unknown names are named in an
/// error line and nothing is sent. The subject is not a field on the
/// wire - `SendSendMail` glues "Subject: " and the title onto the front
/// of the body, and `Mail.ParseTitle` pulls it back off at the other
/// end.
///
/// Reply prefixes "Re: " unless the title already starts with "Re:" or
/// "Aw:" - the German client's abbreviation, kept because the file
/// keeps it - and addresses the sender. Reply all addresses the sender
/// and everyone else on it, and always prefixes, which is the file's
/// own inconsistency rather than one introduced here.
///
/// Delete removes the mail from the local list only. The server's copy
/// went the moment it arrived.
/// </summary>
public partial class MailPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 52;

    /// <summary>Opened: ask the server for anything not collected.</summary>
    public event Action Refresh;
    /// <summary>Look these names up; the answer decides whether to send.</summary>
    public event Action<string[]> Lookup;

    Button _open;
    ColorRect _panel;
    Label _title, _error;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    RichTextLabel _body;
    Button _new, _reply, _replyAll, _refresh, _delete, _close;

    // The compose half.
    LineEdit _to, _subject;
    TextEdit _text;
    Button _send, _cancel;

    MailList _mails;
    readonly List<Button> _buttons = new List<Button>();
    int _picked = -1;
    string _signature = "";
    bool _composing;

    public bool IsOpen => _panel != null && _panel.Visible;

    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Mail" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Mail", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _error = Heading("", FontSize, new Color(0.9f, 0.3f, 0.3f));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _body = new RichTextLabel { BbcodeEnabled = true, Visible = false };
        _body.AddThemeFontSizeOverride("normal_font_size", FontSize);
        AddChild(_body);

        _to = Field("to (comma separated)");
        _subject = Field("subject");
        _text = new TextEdit { Visible = false, PlaceholderText = "..." };
        _text.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_text);

        _new = Push("New", () => Compose(null, null));
        _reply = Push("Reply", () => Reply(false));
        _replyAll = Push("Reply all", () => Reply(true));
        _refresh = Push("Refresh", () => Refresh?.Invoke());
        _delete = Push("Delete", Remove);
        _close = Push("Close", Close);

        _send = Push("Send", Ask);
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

    LineEdit Field(string hint)
    {
        var e = new LineEdit { PlaceholderText = hint, Visible = false };
        e.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(e);
        return e;
    }

    Button Push(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    public void Open() { _composing = false; Show(true); Refresh?.Invoke(); _signature = ""; }
    public void Close() { _composing = false; Show(false); }

    void Show(bool on)
    {
        bool list = on && !_composing;
        bool write = on && _composing;

        _panel.Visible = on; _title.Visible = on; _open.Visible = !on;
        _error.Visible = on && !string.IsNullOrEmpty(_error.Text);

        _scroll.Visible = list; _body.Visible = list;
        foreach (Button b in new[] { _new, _reply, _replyAll, _refresh, _delete, _close })
            b.Visible = list;

        _to.Visible = write; _subject.Visible = write; _text.Visible = write;
        _send.Visible = write; _cancel.Visible = write;

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(76, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 76, v.Y - ButtonBottom - 40);

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 780f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.8f;
        _error.Position = new Vector2(side, y);
        if (_error.Visible) y += FontSize * 1.6f;

        if (_composing)
        {
            _to.Position = new Vector2(side, y);
            _to.Size = new Vector2(w, rowH); y += rowH + 6f;
            _subject.Position = new Vector2(side, y);
            _subject.Size = new Vector2(w, rowH); y += rowH + 6f;

            float bodyH = top + height - rowH - 8f - y;
            _text.Position = new Vector2(side, y);
            _text.Size = new Vector2(w, bodyH);

            Row(new[] { _send, _cancel }, side, top + height - rowH, w, rowH);
            return;
        }

        // Two rows of buttons: a finger needs six of them somewhere.
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
        Row(new[] { _new, _reply, _replyAll }, side, by, w, rowH);
        Row(new[] { _refresh, _delete, _close }, side, by + rowH + 8f, w, rowH);
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

    public void Sync(MailList mails)
    {
        if (_rows == null || !IsOpen || _composing) return;
        _mails = mails;

        if (mails == null || mails.Count == 0)
        {
            if (_signature != "empty")
            {
                _signature = "empty";
                foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
                _buttons.Clear();
                _title.Text = "Mail (none)";
                _body.Text = "";
                _picked = -1;
            }
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (Mail m in mails) sb.Append(m?.Num).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _buttons.Clear();

        for (int i = 0; i < mails.Count; i++)
        {
            Button b = MailRow(mails[i], i);
            _buttons.Add(b);
            _rows.AddChild(b);
        }

        _title.Text = $"Mail ({mails.Count})";
        Pick(_picked >= 0 && _picked < mails.Count ? _picked : 0);
    }

    Button MailRow(Mail m, int index)
    {
        DateTime when = Meridian59.Common.MeridianDate.ToDateTime(m.Timestamp);
        var b = new Button
        {
            Text = $"  {m.Num}   {m.Sender}   {m.Title}\n     {when.ToShortDateString()} {when.ToShortTimeString()}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Flat = true,
            Name = $"mail{index}",
        };
        b.AddThemeFontSizeOverride("font_size", FontSize - 2);
        b.Pressed += () => Pick(index);
        return b;
    }

    void Pick(int index)
    {
        _picked = index;
        for (int i = 0; i < _buttons.Count; i++) _buttons[i].Flat = i != index;

        if (_mails == null || index < 0 || index >= _mails.Count) { _body.Text = ""; return; }
        string text = _mails[index].Message != null ? _mails[index].Message.FullString : "";
        _body.Text = text != null ? text.Replace("[", "[lb]") : "";
    }

    void Remove()
    {
        if (_mails == null || _picked < 0 || _picked >= _mails.Count) return;
        _mails.RemoveAt(_picked);
        _picked = -1;
        _signature = "";
    }

    void Reply(bool all)
    {
        if (_mails == null || _picked < 0 || _picked >= _mails.Count) return;
        Mail m = _mails[_picked];

        string title = m.Title ?? "";
        // Reply keeps an existing Re:/Aw:; reply-all always prefixes.
        bool prefix = all || !(title.StartsWith("Re:") || title.StartsWith("Aw:"));

        string to = m.Sender ?? "";
        if (all && m.Recipients != null)
            foreach (string r in m.Recipients) to += "," + r;

        Compose(to, (prefix ? "Re: " : "") + title);
    }

    void Compose(string to, string subject)
    {
        _to.Text = to ?? "";
        _subject.Text = subject ?? "";
        _text.Text = "";
        _error.Text = "";
        _composing = true;
        Show(true);
    }

    /// <summary>
    /// Send does not send. It asks the server who these names are, and
    /// <see cref="Answer"/> decides.
    /// </summary>
    void Ask()
    {
        string raw = _to.Text ?? "";
        if (raw.Trim().Length == 0) { _error.Text = "No recipients."; Show(true); return; }

        string[] names = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();

        _asked = names;
        _error.Text = "";
        Lookup?.Invoke(names);
    }

    string[] _asked;

    /// <summary>
    /// The server's answer to the lookup: one id per name asked, in
    /// order, zero for a name it does not know. Returns what to send, or
    /// null when a name did not resolve - having put the unknown ones in
    /// the error line, exactly as `ProcessResult` does.
    /// </summary>
    public bool Answer(ObjectID[] ids,
                       out ObjectID[] to, out string subject, out string text)
    {
        to = null; subject = null; text = null;
        if (_asked == null || ids == null || ids.Length != _asked.Length) return false;

        string missing = "";
        for (int i = 0; i < ids.Length; i++)
            if (ids[i].ID == 0) missing += (missing.Length > 0 ? "," : "") + _asked[i];

        if (missing.Length > 0)
        {
            _error.Text = "Unknown recipients: " + missing;
            Show(true);
            return false;
        }

        to = ids;
        subject = _subject.Text ?? "";
        text = _text.Text ?? "";

        _to.Text = ""; _subject.Text = ""; _text.Text = "";
        _error.Text = "";
        _composing = false;
        Show(false);
        return true;
    }
}
