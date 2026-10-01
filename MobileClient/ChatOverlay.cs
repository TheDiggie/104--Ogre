using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;

/// <summary>
/// Chat log and input line for the live view.
///
/// Meridian is a talking game - most of what happens arrives as text -
/// so the view is not usable without this even though it renders fine.
///
/// The log mirrors the client's own ChatMessages list rather than keeping
/// its own copy: the library already caps that list and already resolves
/// the server's string resources and inline variables into FullString.
///
/// The input line is hidden until asked for, because on a phone an always
/// present LineEdit means an always present software keyboard over half
/// the screen. While it is open <see cref="Capturing"/> is true and the
/// view stops reading movement keys, so typing "was" does not walk you
/// into a wall.
/// </summary>
public partial class ChatOverlay : Control
{
    /// <summary>How many lines of history to show.</summary>
    [Export] public int Lines = 8;
    [Export] public int FontSize = 16;

    /// <summary>
    /// How much of the bottom of the screen this occupies, so other
    /// widgets can stay clear of it rather than each guessing.
    /// </summary>
    public float BlockHeight => FontSize * 2.4f + 12f * 2f + (FontSize + 6) * Lines + 12f;

    /// <summary>True while the text field has focus and owns the keyboard.</summary>
    public bool Capturing => _entry != null && _entry.Visible;

    /// <summary>Raised with the text the player submitted, already trimmed.</summary>
    public event Action<string> Submitted;

    RichTextLabel _log;
    LineEdit _entry;
    Button _open, _history, _back;
    int _seen;
    bool _dirty = true;

    /// <summary>
    /// Asks for the previous or next thing typed. True walks back
    /// through the history, false forward; the caller returns the line
    /// or null, and null going forward means the end of the history and
    /// an empty box, which is what ArrowDown does in the game.
    /// </summary>
    public event Func<bool, string> History;
    /// <summary>The box was closed: start the next recall from the top.</summary>
    public event Action HistoryReset;
    Meridian59.Data.Lists.BaseList<ServerString> _watching;

    // The full log, behind a button. The corner shows the last few lines
    // because that is what you want while walking; the whole thing is what
    // you want when you missed something, and the library keeps 200.
    ColorRect _fullBack;
    Label _fullTitle;
    ColorRect _logBack;
    ScrollContainer _fullScroll;
    RichTextLabel _full;
    Button _fullClose, _fullPlain, _fullCopy;
    Label _fullNote;
    readonly List<string> _lines = new List<string>();

    /// <summary>
    /// The same log with the styling stripped off - one message per
    /// entry, exactly as the server sent it, no BBCode.
    ///
    /// The reference keeps the log twice over for the same reason. Its
    /// chat window holds two controls: `Chat.Text`, a StaticText carrying
    /// CEGUI markup, and `Chat.TextPlain`, a read-only MultiLineEditbox
    /// (`Meridian59.layout:769-788`, `UIChat.cpp:9-13`). A
    /// `PlainMode` flag says which of the two is visible
    /// (`UIChat.cpp:93-116`) and `Util::GetChatString` is handed that
    /// flag: true and it returns `ChatMessage->FullString` plus a
    /// newline and nothing else, false and it walks the style list
    /// building font and colour tags (`Util.h:871-876`). So plain mode is
    /// not a rendering trick, it is the same text without the markup -
    /// which is what this list is.
    /// </summary>
    readonly List<string> _plain = new List<string>();

    /// <summary>
    /// Whether the full log is showing the plain text rather than the
    /// styled text. The reference's `ControllerUI::Chat::PlainMode`
    /// (`ControllerUI.h:320`), flipped by a right-click anywhere on the
    /// chat text (`UIChat.cpp:307-317`).
    /// </summary>
    bool _plainMode;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;      // taps fall through to the view

        // A dim strip behind the HUD log. The reference's chat is a
        // framed window with its own background; here the lines sit on
        // the world, and the server styles some of them red - which on
        // the inn's red carpet was one colour on itself. An outline
        // alone was not enough.
        _logBack = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_logBack);

        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            // Not FitContent, and clipped. The last eight MESSAGES are
            // not eight rows: wrap two of them and the label grows
            // downward past the block it was given, over the entry box
            // and the button row below it. Everything else on screen
            // reserves space from BlockHeight, which is computed from
            // the same eight rows, so the overflow lands on widgets
            // that had no way to know. The reference has no such
            // problem - its log is a framed window with a scrollbar
            // (`UIChat.cpp:20-21`) - and the honest equivalent here is
            // to keep the block and let the oldest line fall off the
            // top of it.
            FitContent = false,
            ClipContents = true,
            // Scrolling, but with the bar hidden and always following
            // the end: that is what keeps the NEWEST line against the
            // bottom of the block when the last eight messages wrap to
            // more than eight rows. Clipping alone cut the newest line
            // in half, which is the wrong half to lose.
            ScrollFollowing = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // No anchor preset: Layout() places this explicitly, and an anchor
        // would fight it. The root Control is the anchored one.
        _log.AddThemeFontSizeOverride("normal_font_size", FontSize);
        _log.AddThemeColorOverride("default_color", new Color(1, 1, 1));
        _log.AddThemeConstantOverride("outline_size", 4);
        _log.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        AddChild(_log);

        // The server's own limit, so an over-long line is refused here
        // rather than composed, sent and truncated at the other end.
        _entry = new LineEdit
        {
            PlaceholderText = "say something",
            Visible = false,
            CaretBlink = true,
            MaxLength = Meridian59.Common.Constants.BlakservStringLengths.MAX_CHAT_LEN,
        };
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 2);
        // An opaque box to type into. The default one is part
        // transparent, and the row of menu buttons sits at the same
        // height behind it - so what you were typing read as part of
        // the menu, with "Settings" and "Guild" showing through the
        // middle of the sentence.
        var typing = new StyleBoxFlat { BgColor = new Color(0.04f, 0.04f, 0.06f, 0.98f) };
        typing.SetContentMarginAll(10);
        typing.BorderColor = new Color(0.45f, 0.45f, 0.5f);
        typing.SetBorderWidthAll(1);
        _entry.AddThemeStyleboxOverride("normal", typing);
        _entry.AddThemeStyleboxOverride("focus", typing);
        _entry.TextSubmitted += OnSubmitted;
        AddChild(_entry);

        _open = new Button { Text = "Say" };
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        // The recall button sits at the end of the entry, where a
        // thumb already is when the box is open.
        _back = new Button { Text = "\u2191", Visible = false, Name = "chatRecall" };
        _back.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _back.Pressed += Recall;
        AddChild(_back);

        _history = new Button { Text = "Log" };
        _history.AddThemeFontSizeOverride("font_size", FontSize);
        _history.Pressed += ShowHistory;
        AddChild(_history);
        Panels.Opener(_history);

        // Opaque. At 0.95 the minimap ring, both button rows, the
        // vitals and the wall behind them all read through the text -
        // a log you have to squint past is not a log.
        _fullBack = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f), Visible = false };
        AddChild(_fullBack);

        _fullTitle = new Label { Text = "Chat log", Visible = false };
        _fullTitle.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _fullTitle.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.6f));
        AddChild(_fullTitle);

        _full = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            // The reference's plain control is a read-only
            // MultiLineEditbox, which is selectable by definition
            // (`Meridian59.layout:784-787`). This is the nearest thing a
            // RichTextLabel has: it costs nothing where there is no
            // pointer, and where there is one it gives back the
            // reference's drag-select exactly.
            SelectionEnabled = true,
        };
        _full.AddThemeFontSizeOverride("normal_font_size", FontSize);
        _full.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;

        _fullScroll = new ScrollContainer { Visible = false };
        _fullScroll.AddChild(_full);
        AddChild(_fullScroll);

        // The plain-text switch, and the way off the device with the
        // text.
        //
        // What the reference does: the chat window can be flipped
        // between its styled StaticText and a read-only
        // MultiLineEditbox holding the same messages unadorned, by
        // right-clicking the text (`UIChat.cpp:307-317` flips
        // `PlainMode` and forces a renew; `UIChat.cpp:93-116` swaps
        // which control is visible and carries the scroll position
        // across). Once the editbox is up the player can drag-select in
        // it and press Ctrl+C: `Chat.TextPlain` subscribes
        // `OnCopyPasteKeyDown` (`UIChat.cpp:53`), which on Ctrl+C calls
        // `ControllerUI::CopyToClipboard` (`ControllerUI.cpp:1065-1071`),
        // which takes the editbox's SELECTION - `getSelectionStartIndex`
        // and `getSelectionLength` - and hands it to
        // `Clipboard::SetText` (`ControllerUI.cpp:606-620`). The plain
        // control exists precisely so that what lands on the clipboard is
        // text and not a mouthful of `[colour='FF800000']` tags.
        //
        // Why this differs: the flip itself ports honestly and is ported
        // - a button here, a right-click there, both deliberate and both
        // reversible. The COPY does not. Its two halves are a mouse drag
        // to select and a chorded keystroke to lift, and a touch screen
        // has neither: a drag across text is how you scroll the log, and
        // there is no Ctrl to hold. Rather than fake a selection model
        // nobody can drive with a thumb, the button copies the whole
        // visible buffer in one press - which is what a player wanting
        // to paste a fight or a merchant's price list into a message
        // was reaching for anyway. Selection is still enabled on the
        // label for devices that do have a pointer, and a selection, if
        // there is one, wins over the whole buffer - so on a tablet with
        // a trackpad the behaviour collapses back onto the reference's.
        _fullPlain = new Button { Text = "Plain", Visible = false, Name = "chatPlain" };
        _fullPlain.AddThemeFontSizeOverride("font_size", FontSize);
        _fullPlain.Pressed += TogglePlain;
        AddChild(_fullPlain);

        _fullCopy = new Button { Text = "Copy", Visible = false, Name = "chatCopy" };
        _fullCopy.AddThemeFontSizeOverride("font_size", FontSize);
        _fullCopy.Pressed += CopyLog;
        AddChild(_fullCopy);

        // Confirmation, in the client's own UI. A copy that says nothing
        // is indistinguishable from a copy that failed, and the house
        // rule - and the reference, which has no OS dialogs anywhere in
        // its chat path either - rules out asking the platform to say it.
        _fullNote = new Label { Text = "", Visible = false };
        _fullNote.AddThemeFontSizeOverride("font_size", FontSize);
        _fullNote.AddThemeColorOverride("font_color", new Color(0.56f, 0.88f, 0.56f));
        AddChild(_fullNote);

        _fullClose = new Button { Text = "Close", Visible = false };
        _fullClose.AddThemeFontSizeOverride("font_size", FontSize);
        _fullClose.Pressed += HideHistory;
        AddChild(_fullClose);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>Pixels at the bottom right already spoken for.</summary>
    public float RightReserve { get; set; }

    /// <summary>
    /// How wide the chat block is. Held sideways there is width to
    /// spare and height there is none, so the log keeps to the left
    /// and leaves the other half of the screen for the hotbar - which
    /// otherwise has to stack above the chat and ends up lying across
    /// the middle of the screen, where the thumbs are.
    /// </summary>
    public float BlockWidth
    {
        get
        {
            Vector2 v = GetViewportRect().Size;
            float pad = 12f;
            float full = v.X - pad * 2f;
            return v.X > v.Y ? Mathf.Min(full, v.X * 0.52f) : full;
        }
    }

    void Layout()
    {
        Vector2 v = GetViewportRect().Size;
        float pad = 12f;
        float entryH = FontSize * 2.4f;
        float btnW = FontSize * 5f;
        float blockW = BlockWidth;

        // The recall arrow stops short of whatever owns the corner -
        // the minimap's own button sits there, and the two were drawn
        // on top of one another.
        float recallW = entryH;
        float right = pad + RightReserve;
        _entry.Position = new Vector2(pad, v.Y - entryH - pad);
        _entry.Size = new Vector2(Mathf.Min(v.X - pad - right, blockW), entryH);

        // Inside the entry's own right edge rather than beyond it. Put
        // outside, it sat over a menu button, and the button's text
        // showed through the arrow.
        _back.Position = new Vector2(_entry.Position.X + _entry.Size.X - recallW - 3f,
                                     v.Y - entryH - pad);
        _back.Size = new Vector2(recallW, entryH);

        _open.Position = new Vector2(pad, v.Y - entryH - pad);
        _open.Size = new Vector2(btnW, entryH);

        _history.Position = new Vector2(pad + btnW + 8f, v.Y - entryH - pad);
        _history.Size = new Vector2(btnW, entryH);

        // Sized here rather than left on anchors. This overlay lives in
        // a CanvasLayer, and its Control parent has no rect of its own,
        // so an anchored child came out zero by zero: the log opened
        // and absolutely nothing appeared. Everything else in this file
        // is positioned explicitly, which is why only this was invisible.
        float side = Panels.Side(v, 0.05f);
        _fullBack.Position = Vector2.Zero;
        _fullBack.Size = v;

        float titleH = FontSize + 12f;
        _fullTitle.Position = new Vector2(side, side * 0.5f);
        _fullScroll.Position = new Vector2(side, side * 0.5f + titleH);
        _fullScroll.Size = new Vector2(v.X - side * 2f,
                                       v.Y - side * 0.5f - titleH - entryH - side * 0.8f);
        // The label wraps against a width; without one it has no height
        // either and the scroll stays empty.
        _full.CustomMinimumSize = new Vector2(_fullScroll.Size.X, 0);

        // Three across the bottom where Close alone used to be: the
        // plain-text switch, the copy, and Close. Close keeps the right
        // hand end, which is where a thumb has been finding it.
        float rowW = v.X - side * 2f;
        float thirdW = Mathf.Min(rowW / 3f - 6f, FontSize * 7f);
        float rowY = v.Y - entryH - side * 0.5f;
        _fullPlain.Position = new Vector2(side, rowY);
        _fullPlain.Size = new Vector2(thirdW, entryH);
        _fullCopy.Position = new Vector2(side + thirdW + 8f, rowY);
        _fullCopy.Size = new Vector2(thirdW, entryH);
        _fullClose.Position = new Vector2(side + rowW - thirdW, rowY);
        _fullClose.Size = new Vector2(thirdW, entryH);

        // The copy notice above the row, right-aligned into the gap
        // between Copy and Close so it never sits under a button.
        _fullNote.Position = new Vector2(side, rowY - FontSize - 10f);
        _fullNote.Size = new Vector2(rowW, FontSize + 6f);

        float logH = (FontSize + 6) * Lines;
        _log.Position = new Vector2(pad, v.Y - entryH - pad * 2 - logH);
        _log.Size = new Vector2(blockW, logH);
        _logBack.Position = _log.Position - new Vector2(6f, 4f);
        _logBack.Size = _log.Size + new Vector2(12f, 8f);
        _logBack.Visible = _log.Visible && !ShowingHistory;
        // The scrollbar itself is not wanted on the HUD - the full log
        // has its own screen - so it is given no width.
        VScrollBar bar = _log.GetVScrollBar();
        if (bar != null) { bar.CustomMinimumSize = Vector2.Zero; bar.Modulate = new Color(1, 1, 1, 0); }
    }

    /// <summary>True while the full log is covering the screen.</summary>
    public bool ShowingHistory => _fullBack != null && _fullBack.Visible;

    void ShowHistory()
    {
        _fullBack.Visible = true; _fullScroll.Visible = true; _fullClose.Visible = true;
        _fullTitle.Visible = true;
        _fullPlain.Visible = true; _fullCopy.Visible = true;
        // Opens styled, as the reference opens styled: `PlainMode`
        // starts false (`ControllerUI.h:320`). Plain mode is something
        // you ask for to get the text out, not the everyday view - the
        // styling is how you tell a shout from a system line.
        _fullNote.Visible = false;
        Render();
        // Above the panels built after this one, or the Close button
        // sits under the hotbar and cannot be pressed.
        GetParent()?.MoveChild(this, -1);
        Layout();
        // Newest at the bottom, which is where you were looking. Deferred
        // because the scrollbar does not know its range until the label
        // has been laid out.
        Callable.From(() => _fullScroll.ScrollVertical = (int)_fullScroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    void HideHistory()
    {
        _fullBack.Visible = false; _fullScroll.Visible = false; _fullClose.Visible = false;
        _fullTitle.Visible = false;
        _fullPlain.Visible = false; _fullCopy.Visible = false; _fullNote.Visible = false;
    }

    /// <summary>
    /// Flips the full log between the styled text and the plain text.
    ///
    /// This is the reference's `PlainMode` flip
    /// (`UIChat.cpp:307-317`), reached by a button rather than by a
    /// right-click because a touch screen has no second mouse button and
    /// a long-press on the log is already how a platform offers its own
    /// text selection. The reference carries the scroll position from one
    /// control to the other when it flips (`UIChat.cpp:101-115`) so the
    /// player does not lose their place; there is one control here and
    /// the two buffers have the same number of lines, so the scroll
    /// offset is simply left alone and lands in the same place.
    /// </summary>
    void TogglePlain()
    {
        _plainMode = !_plainMode;
        _fullNote.Visible = false;
        Render();
    }

    /// <summary>
    /// Writes the log to the system clipboard.
    ///
    /// Always the PLAIN buffer, whichever mode the view is in. That is
    /// the point of the reference's plain control: `Chat.TextPlain` holds
    /// `FullString` and nothing else (`Util.h:873-874`), so Ctrl+C over
    /// it lifts text rather than markup. Copying what the styled view
    /// holds would paste this client's BBCode into whatever the player
    /// pastes it into, which is not what the reference puts on the
    /// clipboard.
    ///
    /// A selection wins if there is one, which is
    /// `CopyToClipboard`'s own rule - it copies
    /// `substr(getSelectionStartIndex(), getSelectionLength())`
    /// (`ControllerUI.cpp:606-614`) - and falls back to the whole buffer,
    /// which is the divergence: with no drag-select on a touch screen
    /// there is never a selection to copy, and a button that does nothing
    /// unless you first did something impossible is not an affordance.
    /// </summary>
    void CopyLog()
    {
        string selected = _full != null && _full.GetSelectedText() is string sel && sel.Length > 0
            ? sel
            : null;

        // Selection or not, the text that goes out is plain. A selection
        // made in styled mode is a selection of the RENDERED text -
        // Godot hands back what is on screen, not the BBCode behind it -
        // so it is already markup-free and can go as it is.
        string text = selected ?? string.Join("\n", _plain);

        if (string.IsNullOrEmpty(text))
        {
            Note("nothing to copy");
            return;
        }

        try
        {
            DisplayServer.ClipboardSet(text);
            Note(selected != null ? "selection copied" : $"{_plain.Count} lines copied");
        }
        catch (Exception e)
        {
            // Clipboard access is platform-dependent and not worth
            // taking the log screen down over - the reference's own
            // clipboard path is equally a platform call. Say so in the
            // client's own UI and carry on.
            GD.PrintErr($"[ChatOverlay] clipboard: {e.Message}");
            Note("could not copy");
        }
    }

    /// <summary>
    /// A line of feedback on the log screen, in the client's own UI
    /// rather than a platform dialog.
    /// </summary>
    void Note(string text)
    {
        if (_fullNote == null) return;
        _fullNote.Text = text;
        _fullNote.Visible = true;
    }

    /// <summary>
    /// Puts the current buffer into the label and labels the switch with
    /// what pressing it would do next, which is how the reference's flip
    /// reads: there is no state to guess at, because the text in front of
    /// you says which one you are looking at.
    /// </summary>
    void Render()
    {
        if (_full == null) return;
        _full.BbcodeEnabled = !_plainMode;
        _full.Text = _plainMode ? string.Join("\n", _plain) : string.Join("\n", _lines);
        _fullPlain.Text = _plainMode ? "Styled" : "Plain";
        _fullTitle.Text = _plainMode ? "Chat log - plain text" : "Chat log";
    }

    /// <summary>
    /// Opens the entry with something already in it and the caret at the
    /// end - "tell Alice " and then whatever you type. The game starts a
    /// tell by typing the whole command; a phone has no keyboard sitting
    /// there to type it into, so the parts that know a name can fill it
    /// in.
    /// </summary>
    public void Compose(string prefix)
    {
        _entry.Text = prefix ?? "";
        _entry.CaretColumn = _entry.Text.Length;
        Open();
    }

    /// <summary>
    /// The up-arrow beside the entry: the chat command history, which on
    /// a desktop is ArrowUp and ArrowDown in the box.
    ///
    /// It is worth more here than there. The history holds the last
    /// twenty things you typed, and retyping `tell Alexandrina ...` on
    /// a soft keyboard costs a great deal more than retyping it on a
    /// real one.
    ///
    /// One button rather than two, and it goes round. It walks back,
    /// wraps to an empty box at the end - which is where ArrowDown
    /// would have taken you - and then starts again at the newest.
    /// Without the last step the library's index sits past the end and
    /// every further press gives you the empty box again
    /// (DataController.cs:1533-1560), so an overshoot meant closing the
    /// box and reopening it.
    /// </summary>
    void Recall()
    {
        if (History == null) return;
        string line = History(true);
        if (line == null) HistoryReset?.Invoke();
        _entry.Text = line ?? "";
        _entry.CaretColumn = _entry.Text.Length;
    }

    public void Open()
    {
        // Above the row of menu buttons along the bottom edge, which is
        // built after this one and was drawing over the entry: the box
        // and the buttons shared a line, so what you were typing read as
        // part of the menu and a thumb aiming for the text landed on
        // Settings.
        Panels.ToFront(this);
        _entry.Visible = true;
        _back.Visible = true;
        _open.Visible = false;
        // The log button sits where the entry does; while you are
        // typing it was drawn across the middle of the sentence.
        _history.Visible = false;
        _entry.GrabFocus();
        Keyboard(true);
    }

    public void Close()
    {
        _entry.Visible = false;
        _back.Visible = false;
        _entry.Text = "";
        _open.Visible = true;
        _history.Visible = true;
        // Both Enter and Escape reset the walk in the game, so the next
        // recall starts from the newest line again.
        HistoryReset?.Invoke();
        _entry.ReleaseFocus();
        Keyboard(false);
    }

    /// <summary>
    /// Shows or hides the on-screen keyboard, and never lets that fail
    /// the caller.
    ///
    /// It is platform-dependent and not essential - the box works
    /// without it on anything with real keys - but it was the last
    /// statement in Open, so where it throws it takes everything after
    /// the call with it. That is how Compose lost its prefix: it opened
    /// the box, the keyboard call threw, and "tell Alice " was never
    /// written. Tapping a name in the who list gave you an empty box
    /// and no hint that it had meant to do more.
    /// </summary>
    void Keyboard(bool show)
    {
        try
        {
            if (show) DisplayServer.VirtualKeyboardShow(_entry.Text);
            else DisplayServer.VirtualKeyboardHide();
        }
        catch { }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (!Capturing && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter))
            {
                Open();
                GetViewport().SetInputAsHandled();
            }
            else if (Capturing && k.Keycode == Key.Escape)
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    void OnSubmitted(string text)
    {
        string t = text.Trim();
        Close();
        if (t.Length > 0) Submitted?.Invoke(t);
    }

    /// <summary>
    /// Redraws the log if the client has new messages. Cheap to call every
    /// frame: it compares counts and does nothing when nothing arrived.
    /// </summary>
    /// <summary>
    /// Subscribes to the chat list, once.
    ///
    /// Polling the count is not enough, and the reason is worth stating
    /// because the symptom is silent and total. `DataController` caps
    /// the log at 200: every chat handler does
    /// `if (Count > Maximum) Remove(this[0]); Add(msg);`. So the count
    /// climbs to 201 and then never changes again - one out, one in,
    /// for the rest of the session - and a view that redraws only when
    /// the count moves stops redrawing at the 202nd message and shows
    /// the same screen of chat for ever after. `UIChat.cpp` subscribes
    /// to ListChanged instead, which is what this does.
    /// </summary>
    public void Follow(Meridian59.Data.Lists.BaseList<ServerString> messages)
    {
        if (ReferenceEquals(_watching, messages)) return;
        if (_watching != null) _watching.ListChanged -= OnChatChanged;
        _watching = messages;
        if (_watching != null) _watching.ListChanged += OnChatChanged;
        _dirty = true;
    }

    void OnChatChanged(object sender, System.ComponentModel.ListChangedEventArgs e) => _dirty = true;

    public void Sync(IList<ServerString> messages)
    {
        if (_log == null || messages == null) return;
        if (!_dirty && messages.Count == _seen) return;
        _dirty = false;
        _seen = messages.Count;
        _current = messages;

        _lines.Clear();
        _plain.Clear();
        foreach (ServerString m in messages)
        {
            // An empty message is still a line: the reference appends
            // the newline whatever the text is (`Util.h:957`, plain mode
            // `Util.h:873-874`, queued by `UIChat.cpp:126-133`), and a
            // missing resource string arrives exactly like this. Only a
            // null message has nothing to draw.
            if (m == null) continue;
            string line = Markup(m) ?? "";
            _lines.Add(line);
            // The same message with no markup at all, which is what
            // `Util::GetChatString` returns in plain mode: `FullString`
            // and a newline (`Util.h:873-874`). Built here, beside the
            // styled line, so the two lists stay index-for-index
            // aligned - the flip between them must not move the
            // player's place in the log.
            _plain.Add(m.FullString ?? "");
        }
        // Our own notices are merged back in where they happened. They
        // have to be kept apart and re-added here, because this rebuilds
        // the whole list from the server's - so a line added by Local
        // was thrown away by the next rebuild and vanished from the log
        // it had just appeared in.
        //
        // Each one remembers the server message it followed, and is put
        // back straight after that message. A count is not enough: the
        // library stops growing its list at ChatMessagesMaximum + 1
        // (`DataController.cs:2735-2738` removes the oldest before every
        // add), so from then on the count never changes while every
        // message slides one place towards the front - a notice pinned
        // to a count stays put while the chat moves past it. A notice
        // whose message has been dropped has fallen off the front of the
        // list, and belongs there.
        if (_local.Count > 0)
        {
            var server = new List<string>(_lines);
            var serverPlain = new List<string>(_plain);
            _lines.Clear();
            _plain.Clear();
            // Where each surviving message now sits, so an anchor is one
            // lookup. Built once per rebuild.
            var at = new Dictionary<ServerString, int>(ReferenceEqualityComparer.Instance as IEqualityComparer<ServerString>);
            int k = 0;
            foreach (ServerString m in messages) { if (m != null) at[m] = ++k; }
            int li = 0;
            for (int i = 0; i <= server.Count; i++)
            {
                while (li < _local.Count && Slot(_local[li], at) <= i)
                {
                    // Both buffers take the notice at the same index, so
                    // the plain list stays a line-for-line twin of the
                    // styled one however many notices are folded in.
                    _lines.Add(_local[li].Line);
                    _plain.Add(_local[li].Plain);
                    li++;
                }
                if (i < server.Count) { _lines.Add(server[i]); _plain.Add(serverPlain[i]); }
            }
        }

        int from = Math.Max(0, _lines.Count - Lines);
        _log.Text = string.Join("\n", _lines.GetRange(from, _lines.Count - from));

        if (ShowingHistory)
        {
            // End-lock, which `UIChat` sets on both scrollbars: while
            // you are at the bottom the view follows new messages, and
            // it stops following the moment you scroll away - so
            // reading back through a fight is not yanked out from under
            // you, and sitting at the bottom does not silently fall
            // behind. The reference turns it back on within five pixels
            // of the bottom, and the same slack is used here.
            VScrollBar bar = _fullScroll.GetVScrollBar();
            bool atEnd = bar == null
                      || _fullScroll.ScrollVertical >= (int)(bar.MaxValue - bar.Page) - 5;

            // Through Render rather than straight at the label: while
            // plain mode is up, new messages have to arrive as plain
            // text too. The reference has the same obligation and meets
            // it the same way - its Tick picks the control by
            // `PlainMode` and appends `GetChatString(msg, PlainMode)`
            // to it (`UIChat.cpp:120-143`).
            Render();

            if (atEnd)
                Callable.From(() =>
                {
                    VScrollBar b = _fullScroll.GetVScrollBar();
                    if (b != null)
                        _fullScroll.ScrollVertical = (int)Math.Max(0f, b.MaxValue - b.Page);
                }).CallDeferred();
        }
    }

    /// <summary>
    /// One message as the game renders it: a run of styles over the text,
    /// each with its own colour and weight, rather than one tint for the
    /// whole line.
    ///
    /// The server does not send a coloured string - it sends the text plus
    /// a list of styles, each naming a start, a length, a colour and
    /// whether it is bold, italic or underlined. The Ogre client's
    /// Util::GetChatString walks exactly this list to build its markup,
    /// and a message with no styles at all is drawn plain.
    ///
    /// Public because chat is not the only place the server sends styled
    /// text: quest requirements arrive the same way, and
    /// `UINPCQuestList` draws them through the same `GetChatString`.
    /// </summary>
    public static string Markup(ServerString m)
    {
        // Null only for no message at all. An empty FullString is an
        // empty line, not a missing one (`Util.h:957` appends the
        // newline unconditionally).
        if (m == null) return null;
        string text = m.FullString;
        if (string.IsNullOrEmpty(text)) return "";

        // Nothing styled: the whole line in the colour its kind gets.
        if (m.Styles == null || m.Styles.Count == 0)
            return Dressed(Tint(m.ChatMessageType), Escape(text));

        var sb = new System.Text.StringBuilder();
        foreach (ChatStyle style in m.Styles)
        {
            if (style == null) continue;
            int start = Math.Clamp(style.StartIndex, 0, text.Length);
            int len = Math.Clamp(style.Length, 0, text.Length - start);
            if (len == 0) continue;

            string part = Escape(text.Substring(start, len));

            if (style.IsBold) part = $"[b]{part}[/b]";
            if (style.IsCursive) part = $"[i]{part}[/i]";
            // No underline and no strikeout: `Util::GetChatString`
            // reads IsBold, IsCursive and Color and nothing else
            // (`Util.h:880-954`), so `~U` and `~S` runs are drawn plain
            // there and must be here.

            sb.Append(Dressed(Tint(style.Color), part));
        }

        return sb.Length > 0 ? sb.ToString()
                             : Dressed(Tint(m.ChatMessageType), Escape(text));
    }

    /// <summary>One coloured run.</summary>
    /// <remarks>
    /// The colours are the reference's, exactly (`Constants.h`, via
    /// `Util.h:880-954`). What is mobile's own is the 4px black outline
    /// (`_Ready`), and on a colour that is itself near black that
    /// outline swallows the glyphs: Black, Blue, ImperialBlue, Gray1-5
    /// and the dark chat red were seen unreadable over the world. A
    /// dark run therefore gets a light outline instead; the fill is not
    /// touched.
    /// </remarks>
    static string Dressed(string hex, string part)
    {
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        // Linear-light luminance, so 0000ff (0.07) counts as dark and
        // 006400 (0.10) does too, while 8f26aa (0.10) and 004792 sit at
        // the line.
        static double Lin(int v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double lum = 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        string run = $"[color=#{hex}]{part}[/color]";
        return lum < 0.11 ? $"[outline_size=2][outline_color=#e6e6e6]{run}[/outline_color][/outline_size]" : run;
    }

    /// <summary>The server's own text can contain [, which BBCode eats.</summary>
    static string Escape(string s) => s.Replace("[", "[lb]");

    /// <summary>
    /// The chat colours, as the client defines them in Constants.h.
    ///
    /// The six at the top are the ones vanilla has, and they are not the
    /// obvious ones: chat red is 0x800000 and green 0x006400, both dark,
    /// purple is 0x8F26AA. The rest exist only in the flavour Server 104
    /// runs - the same flavour this library is built as - and a client
    /// that only knows the six draws two dozen server colours as white.
    /// </summary>
    static string Tint(ChatColor c) => c switch
    {
        ChatColor.Black => "000000",
        ChatColor.Blue => "0000ff",
        ChatColor.Green => "006400",
        ChatColor.Purple => "8f26aa",
        ChatColor.Red => "800000",
        ChatColor.White => "ffffff",

        ChatColor.Aquamarine => "7fffd4",
        ChatColor.Cyan => "2eeafa",
        ChatColor.Drab => "404000",
        ChatColor.Emerald => "00fa78",
        ChatColor.Fire => "e10000",
        ChatColor.Champagne => "e8d5c3",
        ChatColor.ImperialBlue => "000080",
        ChatColor.Jonquil => "ffb432",
        ChatColor.Lime => "00f000",
        ChatColor.Magenta => "cd00cd",
        ChatColor.Orange => "fa7800",
        ChatColor.Pink => "ff00a6",
        ChatColor.Steel => "004792",
        ChatColor.ToxicGreen => "78fa00",
        ChatColor.OffWhite => "f5f4ef",
        ChatColor.Violet => "800080",
        ChatColor.Golden => "f5cd5a",
        ChatColor.Yellow => "e6e619",
        ChatColor.Bronze => "e6be8a",
        ChatColor.Gray1 => "0a0a0a",
        ChatColor.Gray2 => "141414",
        ChatColor.Gray3 => "1e1e1e",
        ChatColor.Gray4 => "282828",
        ChatColor.Gray5 => "323232",
        ChatColor.Gray6 => "c8c8c8",
        ChatColor.Gray7 => "d2d2d2",
        ChatColor.Gray8 => "dcdcdc",
        ChatColor.Gray9 => "e6e6e6",
        ChatColor.Gray10 => "f0f0f0",
        ChatColor.QuestGreen => "00960f",
        ChatColor.QuestRed => "b41400",
        ChatColor.MercenaryColor => "ffd1b0",

        _ => "ffffff",
    };

    static string Tint(ChatMessageType t) => t switch
    {
        ChatMessageType.SystemMessage => "a0d8ff",
        ChatMessageType.ServerChatMessage => "d8d8c0",
        _ => "ffffff",          // ObjectChatMessage: someone talking
    };

    /// <summary>
    /// Adds a line of our own, for status the server did not send.
    ///
    /// Kept in its own list and merged by Sync rather than written
    /// straight into the label. Writing to the label appended without a
    /// separator - the text Sync builds has no trailing newline - so a
    /// notice ran onto the end of whatever was last said, and two
    /// unrelated sentences shared a line. It also grew the label
    /// unbounded, past the height the block is laid out for.
    /// </summary>
    public void Local(string text)
    {
        if (_log == null) return;
        _local.Add(new Notice
        {
            Line = $"[color=#8fe08f]{text.Replace("[", "[lb]")}[/color]",
            // Unescaped and untinted: this is the copy that goes to the
            // clipboard, and `[lb]` in a pasted line is this client's
            // markup leaking out.
            Plain = text,
            Anchor = (_current != null && _current.Count > 0) ? _current[_current.Count - 1] : null,
        });
        // The library caps its own chat list; this follows suit rather
        // than keeping every notice of a long session.
        if (_local.Count > 200) _local.RemoveRange(0, _local.Count - 200);
        _dirty = true;
    }

    /// <summary>A line of ours, and the server message it followed.</summary>
    struct Notice { public string Line; public string Plain; public ServerString Anchor; }

    /// <summary>How many server lines precede the notice now: after its
    /// anchor if that is still in the list, otherwise at the front.</summary>
    static int Slot(Notice n, Dictionary<ServerString, int> at)
        => n.Anchor != null && at.TryGetValue(n.Anchor, out int i) ? i : 0;

    /// <summary>The list last synced, for Local to anchor against.</summary>
    IList<ServerString> _current;

    readonly List<Notice> _local = new List<Notice>();
}
