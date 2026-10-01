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

    // The card, its title bar and the round close in it - the shared
    // chrome every panel wears now. See M59Skin.
    Panel _card, _bar;
    Button _x;
    /// <summary>The letter's own surface, so the page reads as a page.</summary>
    Panel _page;
    /// <summary>Who it is from and when, over the letter.</summary>
    Label _letterHead;
    /// <summary>What an empty mailbox says.</summary>
    Label _empty;

    // The compose half.
    LineEdit _to, _subject;
    TextEdit _text;
    Button _send, _cancel;
    /// <summary>
    /// Labels for the compose boxes. A placeholder vanishes the moment
    /// anything is typed, so a form made of placeholders alone has no
    /// labels at all once it is filled in.
    /// </summary>
    Label _toLabel, _subjectLabel, _textLabel;

    // The in-page question asked when Reply would overwrite a draft that
    // has a body. Never a system dialog: these are children of the panel.
    ColorRect _dim;
    Panel _askCard;
    Label _askText;
    Button _askYes, _askNo;
    Action _replace;
    bool _asking;

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


    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Mail" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open, "Letters", 230);

        // The scrim dims the world instead of hiding it, and the card
        // over it is what the panel actually is now. See M59Skin.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window(); _card.Visible = false; AddChild(_card);
        _bar = M59Skin.TitleBar(); _bar.Visible = false; AddChild(_bar);

        _title = M59Skin.Title("Mail"); _title.Visible = false; AddChild(_title);
        _x = M59Skin.CloseX(Close); _x.Visible = false; AddChild(_x);

        _error = Heading("", M59Skin.SmallSize + 2, M59Skin.Danger);

        // The page the letter sits on, under the text: the list and the
        // letter were one unbroken column of grey, and a surface is what
        // tells you the reading has started.
        _page = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _page.AddThemeStyleboxOverride("panel", Sunken());
        AddChild(_page);

        _letterHead = new Label { Visible = false, ClipText = true };
        _letterHead.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _letterHead.AddThemeColorOverride("font_color", M59Skin.Gold);
        AddChild(_letterHead);

        _empty = M59Skin.Empty("No letters. Refresh asks the server for anything not collected.");
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
        // A letter is read, not scanned: the lines need air between them
        // or a paragraph is a grey brick.
        _body.AddThemeConstantOverride("line_separation", 7);
        AddChild(_body);

        _toLabel = FieldLabel("To");
        _subjectLabel = FieldLabel("Subject");
        _textLabel = FieldLabel("Letter");

        _to = Field("one name, or several separated by commas");
        _subject = Field("what it is about");
        // The game's own limits, and they are not arbitrary: SendSendMail
        // glues "Subject: " + the title onto the front of the body, and
        // the server's 4096 applies to the whole thing - which is why the
        // reference takes the title's 60 and another 11 off the body's
        // allowance (`UIMailCompose.cpp:20-26`). Without them a long
        // letter is silently truncated or refused with nothing said.
        _subject.MaxLength = 60;
        _text = new TextEdit { Visible = false, PlaceholderText = "..." };
        _text.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _text.AddThemeColorOverride("font_color", M59Skin.Text);
        _text.AddThemeColorOverride("font_placeholder_color", M59Skin.TextOff);
        _text.AddThemeStyleboxOverride("normal", Sunken());
        _text.AddThemeStyleboxOverride("focus", Sunken(true));
        AddChild(_text);

        _new = Push("New", Resume);
        _reply = Push("Reply", () => Reply(false));
        _replyAll = Push("Reply all", () => Reply(true));
        _refresh = Push("Refresh", () => Refresh?.Invoke());
        _delete = Push("Delete", Remove, M59Skin.Kind.Danger);
        _close = Push("Close", Close);

        _send = Push("Send", Ask, M59Skin.Kind.Primary);
        _cancel = Push("Cancel", () => { _composing = false; _asked = null; Show(true); });

        // Last children, so they draw over everything else in the panel.
        _dim = new ColorRect { Color = M59Skin.Scrim, Visible = false,
                               MouseFilter = MouseFilterEnum.Stop };
        AddChild(_dim);
        // The question gets a card of its own rather than floating text
        // on a black wash: it is a window in front of a window.
        _askCard = M59Skin.Window();
        _askCard.Visible = false;
        AddChild(_askCard);
        _askText = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                               HorizontalAlignment = HorizontalAlignment.Center,
                               VerticalAlignment = VerticalAlignment.Center };
        _askText.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _askText.AddThemeColorOverride("font_color", M59Skin.Text);
        AddChild(_askText);
        _askYes = Push("Replace draft", () => { Action go = _replace; Unask(); go?.Invoke(); }, M59Skin.Kind.Danger);
        _askYes.Name = "draftreplace";
        _askNo = Push("Keep draft", () => { Unask(); Resume(); }, M59Skin.Kind.Primary);
        _askNo.Name = "draftkeep";

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
    /// An inset surface: the letter's page, and the inside of every box
    /// on the compose form. M59Skin has raised chrome and rows but
    /// nothing that reads as "type in here", and a box has to look
    /// punched into the card rather than sitting on it.
    /// </summary>
    static StyleBoxFlat Sunken(bool lit = false)
    {
        var s = new StyleBoxFlat
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
        return s;
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

    LineEdit Field(string hint)
    {
        var e = new LineEdit { PlaceholderText = hint, Visible = false };
        e.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        e.AddThemeColorOverride("font_color", M59Skin.Text);
        e.AddThemeColorOverride("font_placeholder_color", M59Skin.TextOff);
        e.AddThemeStyleboxOverride("normal", Sunken());
        e.AddThemeStyleboxOverride("focus", Sunken(true));
        AddChild(e);
        return e;
    }

    Button Push(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    public void Open() { _composing = false; _asking = false; _pickedNum = -1; _asked = null; Show(true); Refresh?.Invoke(); _signature = ""; }
    // Close forgets the selection and any lookup in flight, so the next
    // open starts with nothing chosen (`UIMail.cpp:182`, `:266`: Respond
    // and Delete do nothing without a selection) and a late LookupNames
    // answer cannot send a letter the player walked away from.
    //
    // It does NOT touch the draft. The reference keeps the compose
    // window's text when it is hidden and only empties it after a send
    // (`UIMailCompose.cpp:91-94`); hiding it is `Window->hide()` /
    // `setVisible(false)` and nothing clears the boxes.
    public void Close() { _composing = false; _asking = false; _replace = null; _pickedNum = -1; _asked = null; Show(false); }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        bool list = on && !_composing;
        bool write = on && _composing;

        _panel.Visible = on; _title.Visible = on; _open.Visible = !on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _error.Visible = on && !string.IsNullOrEmpty(_error.Text);

        _scroll.Visible = list; _body.Visible = list;
        // The page and the letter's header only mean anything while a
        // letter is chosen; the empty line only while there are none.
        _page.Visible = list && _pickedNum >= 0;
        _letterHead.Visible = list && _pickedNum >= 0;
        _empty.Visible = list && _buttons.Count == 0;
        foreach (Button b in new[] { _new, _reply, _replyAll, _refresh, _delete, _close })
            b.Visible = list;

        _to.Visible = write; _subject.Visible = write; _text.Visible = write;
        _toLabel.Visible = write; _subjectLabel.Visible = write; _textLabel.Visible = write;
        _send.Visible = write; _cancel.Visible = write;

        bool ask = on && list && _asking;
        _dim.Visible = ask; _askCard.Visible = ask; _askText.Visible = ask;
        _askYes.Visible = ask; _askNo.Visible = ask;
        // A letter is in progress: say so, so New is not mistaken for a
        // blank page.
        _new.Text = HasDraft ? "Draft" : "New";

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    /// <summary>
    /// A line of prose is read by jumping back to the left edge, and
    /// over about this many pixels the eye loses the next line. The
    /// card is as wide as a sideways phone and the letter is not.
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

        // The question covers the whole screen and is centred on it, so
        // it is never half off the card it belongs to.
        _dim.Position = Vector2.Zero; _dim.Size = v;
        float aw = Mathf.Min(v.X - 64f, 620f), ah = 230f;
        var ask = new Rect2(Mathf.Round((v.X - aw) * 0.5f), Mathf.Round((v.Y - ah) * 0.5f), aw, ah);
        _askCard.Position = ask.Position; _askCard.Size = ask.Size;
        _askText.Position = new Vector2(ask.Position.X + M59Skin.Pad, ask.Position.Y + M59Skin.Pad);
        _askText.Size = new Vector2(aw - M59Skin.Pad * 2f, ah - M59Skin.Pad * 2f - 48f - M59Skin.Gap);
        M59Skin.FootRow(new Rect2(ask.Position.X + M59Skin.Pad,
                                  ask.Position.Y + ah - M59Skin.Pad - 48f,
                                  aw - M59Skin.Pad * 2f, 48f), _askNo, _askYes);

        float errH = _error.Visible ? M59Skin.SmallSize * 2f : 0f;
        _error.Position = new Vector2(body.Position.X, body.Position.Y);
        _error.Size = new Vector2(body.Size.X, errH);
        float top = body.Position.Y + errH;
        float rest = body.Size.Y - errH;

        if (_composing)
        {
            // The form is a form, not the whole card: a To box two
            // thousand pixels long for a name of six letters is what
            // the sideways screen does to anything stretched to fit.
            float fw = Mathf.Min(body.Size.X, 900f);
            float fx = body.Position.X + Mathf.Round((body.Size.X - fw) * 0.5f);
            float lh = M59Skin.SmallSize + 8f, fh = 46f, y = top;

            _toLabel.Position = new Vector2(fx, y); _toLabel.Size = new Vector2(fw, lh); y += lh;
            _to.Position = new Vector2(fx, y); _to.Size = new Vector2(fw, fh);
            y += fh + M59Skin.Gap;
            _subjectLabel.Position = new Vector2(fx, y); _subjectLabel.Size = new Vector2(fw, lh); y += lh;
            _subject.Position = new Vector2(fx, y); _subject.Size = new Vector2(fw, fh);
            y += fh + M59Skin.Gap;
            _textLabel.Position = new Vector2(fx, y); _textLabel.Size = new Vector2(fw, lh); y += lh;
            _text.Position = new Vector2(fx, y);
            _text.Size = new Vector2(fw, Mathf.Max(fh, top + rest - y));

            M59Skin.FootRow(foot, _cancel, _send);
            return;
        }

        // Two columns on a wide card, stacked on a narrow one. The list
        // and the letter are different things and the eye should not
        // have to be told so twice: side by side the letter keeps a
        // readable measure AND the list keeps its full height, which
        // stacking cannot give both of.
        bool wide = body.Size.X >= 980f;
        float listW = wide ? Mathf.Clamp(body.Size.X * 0.40f, 320f, 560f) : body.Size.X;
        float listH = wide ? rest : Mathf.Max(M59Skin.RowH * 2f, rest * 0.45f);

        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(listW, listH);
        // 14 was the old bar's width and the bar is 28 now, so the
        // rows ran under the grabber. One number, in the skin.
        _rows.CustomMinimumSize = new Vector2(
            M59Skin.RowsW(new Rect2(Vector2.Zero, new Vector2(listW, 0))), 0);
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        float rx = wide ? body.Position.X + listW + M59Skin.Pad : body.Position.X;
        float ry = wide ? top : top + listH + M59Skin.Pad;
        float rw = wide ? body.Size.X - listW - M59Skin.Pad : body.Size.X;
        float rh = wide ? rest : rest - listH - M59Skin.Pad;

        _page.Position = new Vector2(rx, ry);
        _page.Size = new Vector2(rw, Mathf.Max(0f, rh));

        // Inside the page: the sender's line, then the letter, held to
        // a measure and centred in whatever is left over.
        float inner = Mathf.Min(rw - M59Skin.Pad * 2f, Measure);
        float ix = rx + Mathf.Round((rw - inner) * 0.5f);
        _letterHead.Position = new Vector2(ix, ry + M59Skin.Pad);
        _letterHead.Size = new Vector2(inner, M59Skin.BodySize + 10f);
        float bodyTop = ry + M59Skin.Pad + M59Skin.BodySize + 10f + M59Skin.Gap;
        _body.Position = new Vector2(ix, bodyTop);
        _body.Size = new Vector2(inner, Mathf.Max(0f, ry + rh - M59Skin.Pad - bodyTop));

        // Right to left: Close under the thumb that dismisses it, New -
        // the one thing this panel is for besides reading - furthest in.
        M59Skin.FootRow(foot, _close, _delete, _refresh, _replyAll, _reply, _new);
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
            // Nothing in the box says so in words, rather than leaving
            // an empty rectangle that reads as a panel that failed.
            _empty.Visible = true;
            _page.Visible = false; _letterHead.Visible = false;
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

        _empty.Visible = false;
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
            CustomMinimumSize = new Vector2(0, M59Skin.RowH),
            // Dressed rather than Flat: the stripes are what keep a
            // mailbox of sixty letters readable, and Pick draws the
            // chosen one. The alternating shade is by position.
            Name = $"mail{index}",
        };
        M59Skin.Dress(b, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
        b.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        b.Pressed += () => Pick(index);
        return b;
    }

    void Pick(int index)
    {
        _pickedNum = index >= 0 && index < _sorted.Count ? (long)_sorted[index].Num : -1;
        for (int i = 0; i < _buttons.Count; i++) M59Skin.Pick(_buttons[i], i == index, i % 2 != 0);

        // Indexes are into the SORTED list, which is what the rows were
        // built from; the model's own order is not the shown order.
        if (index < 0 || index >= _sorted.Count)
        {
            _body.Text = ""; _letterHead.Text = "";
            _page.Visible = false; _letterHead.Visible = false;
            return;
        }
        Mail chosen = _sorted[index];
        // The letter's own heading, so the page says what is on it
        // rather than starting mid-sentence under a list of sixty rows.
        DateTime when = Meridian59.Common.MeridianDate.ToDateTime(chosen.Timestamp);
        _letterHead.Text = $"{chosen.Title}   -   {chosen.Sender}, {when.ToShortDateString()}";
        _page.Visible = IsOpen && !_composing;
        _letterHead.Visible = _page.Visible;
        string text = chosen.Message != null ? chosen.Message.FullString : "";
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

        Begin(to, (prefix ? "Re: " : "") + title);
    }

    /// <summary>Anything typed into the letter in progress.</summary>
    bool HasDraft => !string.IsNullOrEmpty(_to.Text) || !string.IsNullOrEmpty(_subject.Text)
                  || !string.IsNullOrEmpty(_text.Text);

    /// <summary>
    /// Show the compose form exactly as it was left. The reference's New
    /// only shows the window (`UIMail.cpp:166-171`) - the boxes are
    /// emptied after a send and at no other time - so a letter that was
    /// cancelled, or hidden with the panel, is still there.
    /// </summary>
    void Resume()
    {
        _error.Text = "";
        _asked = null;
        _composing = true;
        Show(true);
    }

    /// <summary>Start a reply: recipients and subject set, body empty.</summary>
    void Fill(string to, string subject)
    {
        _to.Text = to ?? "";
        _subject.Text = subject ?? "";
        _text.Text = "";
        Resume();
    }

    void Unask() { _asking = false; _replace = null; Show(true); }

    /// <summary>
    /// A reply overwrites the recipients and subject (`UIMail.cpp:194-199`),
    /// and reply-all empties the body (`:246`). Over an unsent body that
    /// would destroy the letter, so the player is asked first, in the
    /// panel.
    /// </summary>
    void Begin(string to, string subject)
    {
        if (string.IsNullOrEmpty(_text.Text)) { Fill(to, subject); return; }
        _replace = () => Fill(to, subject);
        _asking = true;
        _askText.Text = "You have an unsent letter. Replace it with this reply?";
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
