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
    // The chosen letter, by its Num and not by its place in the list: the
    // list is newest first, so a letter arriving at index 0 would shift an
    // index onto a different letter, and Reply / Delete would act on it.
    // The reference binds the row to its item (`UIMail.cpp:86-128`).
    // -1 is no selection, which is where the reference starts.
    long _pickedNum = -1;
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
        Panels.Opener(_open);

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
        // The game's own limits, and they are not arbitrary: SendSendMail
        // glues "Subject: " + the title onto the front of the body, and
        // the server's 4096 applies to the whole thing - which is why the
        // reference takes the title's 60 and another 11 off the body's
        // allowance (`UIMailCompose.cpp:20-26`). Without them a long
        // letter is silently truncated or refused with nothing said.
        _subject.MaxLength = 60;
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
        _cancel = Push("Cancel", () => { _composing = false; _asked = null; Show(true); });

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

    public void Open() { _composing = false; _pickedNum = -1; _asked = null; Show(true); Refresh?.Invoke(); _signature = ""; }
    // Close forgets the selection and any lookup in flight, so the next
    // open starts with nothing chosen (`UIMail.cpp:182`, `:266`: Respond
    // and Delete do nothing without a selection) and a late LookupNames
    // answer cannot send a letter the player walked away from.
    public void Close() { _composing = false; _pickedNum = -1; _asked = null; Show(false); }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
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

        float side = Panels.Side(v, 0.06f);
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
        // The last row wants air under it: without it the Close
        // button's bottom edge and the panel's own are the same line
        // and the only way out looks cut off.
        float buttons = rowH * 2f + 8f + 12f;
        float rest = top + height - buttons - 8f - y;
        // The list is the window. It had 45% of the space and the body
        // had the rest, so thirty-eight letters were shown through a
        // five-row slot with the last one sliced in half, while a
        // one-line message sat above two hundred pixels of nothing.
        float listH = rest * 0.62f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, listH);
        _rows.CustomMinimumSize = new Vector2(w, 0);
        // A real gap between the list and the letter. At 8 pixels the
        // last row's second line and the first line of the body sat on
        // the same baseline with no divider, and the letter read as
        // part of the row above it.
        const float split = 20f;
        y += listH + split;

        _body.Position = new Vector2(side, y);
        _body.Size = new Vector2(w, rest - listH - split);

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
                _pickedNum = -1;
            }
            return;
        }

        // Newest first. The reference shows them in whatever order the
        // resource manager read them off the disk and only turns the
        // user's own column sorting OFF (`UIMail.cpp:19`), so a
        // mailbox there is in no order at all - thirty letters listed
        // 19, 6, 18, 3, 24, and their dates equally shuffled. That is
        // tolerable beside four sortable columns and useless on a
        // phone, where the list is the whole window. A deliberate
        // departure, and the only one this panel makes.
        _sorted.Clear();
        foreach (Mail m in mails) if (m != null) _sorted.Add(m);
        // Newest first, and the letter number breaks a tie: two
        // letters delivered in the same minute carry the same
        // timestamp, and sorting on the date alone left those pairs in
        // whatever order the list happened to hold them - 40, 39, then
        // 37, 38, then 36, 35.
        _sorted.Sort((a, b2) =>
        {
            int byDate = b2.Timestamp.CompareTo(a.Timestamp);
            return byDate != 0 ? byDate : b2.Num.CompareTo(a.Num);
        });

        var sb = new System.Text.StringBuilder();
        foreach (Mail m in _sorted) sb.Append(m.Num).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _buttons.Clear();

        for (int i = 0; i < _sorted.Count; i++)
        {
            Button b = MailRow(_sorted[i], i);
            _buttons.Add(b);
            _rows.AddChild(b);
        }

        _title.Text = $"Mail ({mails.Count})";
        // Nothing is selected until the player taps a row, as in the
        // reference (`UIMail.cpp:182`, `:266`). Delete is permanent on
        // both sides - the server's copy went when the letter arrived
        // (`Meridian59/Client/BaseClient.cs:609-619`) - so a letter the
        // player never chose must never be the one it acts on. A
        // selection that is still in the list stays on its letter.
        int keep = -1;
        for (int i = 0; i < _sorted.Count; i++)
            if (_sorted[i].Num == _pickedNum) { keep = i; break; }
        Pick(keep);
    }

    /// <summary>The mailbox in the order it is shown, newest first.</summary>
    readonly List<Mail> _sorted = new List<Mail>();

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
        _pickedNum = index >= 0 && index < _sorted.Count ? (long)_sorted[index].Num : -1;
        for (int i = 0; i < _buttons.Count; i++) _buttons[i].Flat = i != index;

        // Indexes are into the SORTED list, which is what the rows were
        // built from; the model's own order is not the shown order.
        if (index < 0 || index >= _sorted.Count) { _body.Text = ""; return; }
        string text = _sorted[index].Message != null ? _sorted[index].Message.FullString : "";
        _body.Text = text != null ? text.Replace("[", "[lb]") : "";
    }

    /// <summary>The selected letter, found by Num, or null.</summary>
    Mail Picked()
    {
        if (_pickedNum < 0) return null;
        foreach (Mail m in _sorted) if (m.Num == _pickedNum) return m;
        return null;
    }

    void Remove()
    {
        Mail m = Picked();
        if (_mails == null || m == null) return;
        _mails.Remove(m);
        _pickedNum = -1;
        _signature = "";
    }

    void Reply(bool all)
    {
        Mail m = Picked();
        if (m == null) return;

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
        _asked = null;
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
        // Cancel and Close clear _asked; an answer with nobody waiting
        // for it is dropped, not sent and not allowed to reopen the panel.
        if (!_composing || !IsOpen) return false;
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
        // Godot's multi-line box has no MaxLength, so the body is cut
        // here instead, to the same allowance the reference gives it:
        // MAIL_MESSAGE_MAX_LENGTH less the title's 60 and the eleven
        // characters SendSendMail glues on in front
        // (`UIMailCompose.cpp:20-26`).
        text = _text.Text ?? "";
        int room = Meridian59.Common.Constants.BlakservStringLengths.MAIL_MESSAGE_MAX_LENGTH - 60 - 10 - 1;
        if (text.Length > room) text = text.Substring(0, room);

        _to.Text = ""; _subject.Text = ""; _text.Text = "";
        _error.Text = "";
        _composing = false;
        _asked = null;
        // Only the compose form closes; the mailbox stays up, as it does
        // in the reference (`UIMailCompose.cpp:91-97`). Closing the whole
        // panel made sending two letters in a row needlessly slow.
        Show(true);
        return true;
    }
}
