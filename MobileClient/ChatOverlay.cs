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
    public float BlockHeight =>
        (FontSize * 2.4f + 12f * 2f + (FontSize + 6) * Lines + 12f) * HudScale();


    /// <summary>
    /// Everything the layout store says about this piece, as one value.
    ///
    /// Compared every frame on the cheap entry point below, because a
    /// piece that lays itself out only on an event cannot see a layout
    /// LOADED after it was built, or an editor that changed a piece
    /// without raising Changed - and the failure is silent: the piece
    /// draws itself exactly where it used to be.
    /// </summary>
    static string HudStamp(string id)
    {
        M59Hud.Piece p = M59Hud.Get(id);
        if (p == null) return "";
        return $"{p.Offset.X},{p.Offset.Y},{p.Scale},{p.Alpha},{(p.Hidden ? 1 : 0)},{(M59Hud.Editing ? 1 : 0)}";
    }

    string _stamp = "";

    /// <summary>The player's size for this cluster, inside the model's band.</summary>
    static float HudScale()
    {
        M59Hud.Piece p = M59Hud.Get("chat");
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>
    /// The smallest a control a thumb presses may become. Say and Log are
    /// pressed with the other thumb on the stick, so the scale's floor of
    /// 0.7 is not allowed to take them under the 44 points a thumb needs:
    /// the entry row is 38 points high at 0.7 and is clamped here.
    /// </summary>
    const float TapFloor = 44f;

    /// <summary>The size the strip and the row were last dressed at.</summary>
    float _dressedAt;

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
    // The card the full log is drawn on - see M59Skin. The transient
    // overlay above is NOT a card and must not become one: it lives
    // over the world. Only this screen is a window.
    Panel _fullCard, _fullBar, _fullPage;
    Button _fullX;
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

        // The strip behind the HUD log. The reference's chat is a framed
        // window with its own background; here the lines sit on the
        // world, and the server styles some of them red - which on the
        // inn's red carpet was one colour on itself. An outline alone was
        // not enough.
        //
        // Opaque, and in the skin's own sunken colour, which is the whole
        // point: at 0.45 the backing was a different colour behind every
        // step the player took, so the contrast of a chat colour was a
        // function of the floor. The world's bright red carpet dragged
        // the dark runs to nothing and its black ceiling dragged the pale
        // ones. Now the two places chat appears - this strip and the full
        // log's sunken page (<see cref="M59Skin.Sunken"/>, the same
        // 0.055/0.051/0.045) - are the SAME surface, so one contrast
        // measurement covers both and neither moves with the view.
        _logBack = new ColorRect { Color = Back, MouseFilter = MouseFilterEnum.Ignore };
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
        // A thin dark edge, not the old four. Four pixels of outline on a
        // sixteen pixel glyph is most of the glyph: the counters of e, a
        // and o filled in and every letter read as a blob, which is the
        // other half of what "hard to read" meant. Two is enough to hold
        // an edge now that the strip beneath is opaque.
        _log.AddThemeConstantOverride("outline_size", 2);
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

        _open = new Button { Text = "Chat" };
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open, "Speak", 10, Panels.Where.Owner);

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
        Panels.Opener(_history, "The chat log", 20, Panels.Where.Owner);

        // The scrim dims the world and eats the touch that would reach
        // it; the CARD is the opaque part. (The old full-bleed rectangle
        // was made opaque because at 0.95 the minimap ring, both button
        // rows and the vitals read through the text - a log you have to
        // squint past is not a log. The card keeps that promise: nothing
        // is drawn through it.)
        _fullBack = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        AddChild(_fullBack);

        _fullCard = M59Skin.Window();
        _fullCard.Visible = false;
        AddChild(_fullCard);

        _fullBar = M59Skin.TitleBar();
        _fullBar.Visible = false;
        AddChild(_fullBar);

        _fullTitle = M59Skin.Title("Chat log");
        _fullTitle.Visible = false;
        AddChild(_fullTitle);

        // The same call as the Close in the footer, not a second way out.
        _fullX = M59Skin.CloseX(HideHistory);
        _fullX.Visible = false;
        AddChild(_fullX);

        // A sunken page to read from. Everything else on the card is
        // raised, and a reading surface that is raised reads as another
        // button. It sits UNDER the scroll, which is added after it.
        _fullPage = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _fullPage.AddThemeStyleboxOverride("panel", M59Skin.Sunken());
        AddChild(_fullPage);

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
        // Body size, not the HUD's: the overlay's sixteen is for a few
        // lines over the world, this is the thing a player reads longest.
        // The separation is what keeps a wrapped message from fusing
        // with the next one.
        _full.AddThemeFontSizeOverride("normal_font_size", M59Skin.BodySize);
        _full.AddThemeFontSizeOverride("bold_font_size", M59Skin.BodySize);
        _full.AddThemeFontSizeOverride("italics_font_size", M59Skin.BodySize);
        _full.AddThemeFontSizeOverride("bold_italics_font_size", M59Skin.BodySize);
        _full.AddThemeFontSizeOverride("mono_font_size", M59Skin.BodySize);
        _full.AddThemeConstantOverride("line_separation", 6);
        _full.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;

        // Horizontal scrolling off: the label is sized to the scroll's
        // width less its bar, so it never needs it, and a bar's width
        // of overflow had made the page wobble sideways.
        _fullScroll = new TouchScroll
        {
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
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
        M59Skin.Dress(_fullPlain, M59Skin.Kind.Secondary);
        _fullPlain.Pressed += TogglePlain;
        AddChild(_fullPlain);

        _fullCopy = new Button { Text = "Copy", Visible = false, Name = "chatCopy" };
        M59Skin.Dress(_fullCopy, M59Skin.Kind.Secondary);
        _fullCopy.Pressed += CopyLog;
        AddChild(_fullCopy);

        // Confirmation, in the client's own UI. A copy that says nothing
        // is indistinguishable from a copy that failed, and the house
        // rule - and the reference, which has no OS dialogs anywhere in
        // its chat path either - rules out asking the platform to say it.
        _fullNote = new Label { Text = "", Visible = false };
        _fullNote.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        _fullNote.AddThemeColorOverride("font_color", new Color(0.56f, 0.88f, 0.56f));
        _fullNote.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_fullNote);

        _fullClose = new Button { Text = "Close", Visible = false };
        M59Skin.Dress(_fullClose, M59Skin.Kind.Secondary);
        _fullClose.Pressed += HideHistory;
        AddChild(_fullClose);

        // The strip and the say/log row are one movable cluster; the
        // full-screen history is not, so no node is handed over and the
        // transparency is applied by HudDress. See M59Hud.
        M59Hud.Register("chat", "Chat");
        M59Hud.Changed += Layout;

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
        // Everything the strip and the row are made of multiplies: the
        // padding, the entry's height, the button widths, the line pitch
        // and the font in all of them. The entry row carries the touch
        // floor, because Say, Log and the recall arrow are pressed.
        float sc = HudScale();
        Redress(sc);
        float pad = 12f * sc;
        float entryH = Mathf.Max(TapFloor, FontSize * 2.4f * sc);
        float btnW = Mathf.Max(TapFloor, FontSize * 5f * sc);
        float blockW = BlockWidth;
        float logH = (FontSize + 6) * Lines * sc;

        // THE NATURAL RECT is the whole block, strip and row together:
        // the log sits on the entry's line and the two read as one thing,
        // so they move and scale as one. The full-screen history is NOT
        // part of it - that is a panel, and panels do not move.
        float blockH = logH + pad * 2f + entryH;
        var natural = new Rect2(pad, v.Y - entryH - pad - (logH + pad), blockW, blockH);
        Rect2 at = M59Hud.Place("chat", natural, v);
        float x = at.Position.X;
        float rowY = at.Position.Y + at.Size.Y - entryH;

        // The recall arrow stops short of whatever owns the corner -
        // the minimap's own button sits there, and the two were drawn
        // on top of one another.
        float recallW = entryH;
        float right = pad + RightReserve;
        _entry.Position = new Vector2(x, rowY);
        _entry.Size = new Vector2(Mathf.Min(v.X - pad - right, blockW), entryH);

        // Inside the entry's own right edge rather than beyond it. Put
        // outside, it sat over a menu button, and the button's text
        // showed through the arrow.
        _back.Position = new Vector2(_entry.Position.X + _entry.Size.X - recallW - 3f * sc, rowY);
        _back.Size = new Vector2(recallW, entryH);

        _open.Position = new Vector2(x, rowY);
        _open.Size = new Vector2(btnW, entryH);

        _history.Position = new Vector2(x + btnW + 8f * sc, rowY);
        _history.Size = new Vector2(btnW, entryH);

        // Sized here rather than left on anchors. This overlay lives in
        // a CanvasLayer, and its Control parent has no rect of its own,
        // so an anchored child came out zero by zero: the log opened
        // and absolutely nothing appeared. Everything else in this file
        // is positioned explicitly, which is why only this was invisible.
        _fullBack.Position = Vector2.Zero;
        _fullBack.Size = v;

        // A card as wide as a line of prose wants to be, not as wide as
        // a sideways phone: a message wrapped across 1600 points cannot
        // be followed back to its start. Measure is the text column;
        // the rest is the page's margins, the scrollbar and the card's
        // padding.
        Rect2 card = M59Skin.Frame(v, 0f, true, M59Skin.Measure + 24f + 16f + M59Skin.Pad * 2f);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _fullCard.Position = card.Position;
        _fullCard.Size = card.Size;
        _fullBar.Position = card.Position;
        _fullBar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _fullTitle.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _fullTitle.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _fullX.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _fullX.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                      card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        // The page fills the body; the scroll sits inside its margins
        // (the sunken stylebox's own 12 and 8) so text never touches
        // the edge.
        _fullPage.Position = body.Position;
        _fullPage.Size = body.Size;
        _fullScroll.Position = body.Position + new Vector2(12f, 8f);
        _fullScroll.Size = body.Size - new Vector2(24f, 16f);
        // The label wraps against a width; without one it has no height
        // either and the scroll stays empty. Less the scrollbar, which
        // the container takes out of the same width.
        VScrollBar fullBar = _fullScroll.GetVScrollBar();
        float barW = fullBar != null ? fullBar.GetCombinedMinimumSize().X : 0f;
        _full.CustomMinimumSize = new Vector2(Mathf.Max(1f, _fullScroll.Size.X - barW - 4f), 0);

        // Plain, Copy, Close left to right with Close at the right-hand
        // end, which is where a thumb has been finding it. FootRow lays
        // out from the right, so it is given them in reverse.
        float left = M59Skin.FootRow(foot, _fullClose, _fullCopy, _fullPlain);

        // The copy notice takes the empty left end of the footer, so it
        // never sits under a button and never covers the log.
        _fullNote.Position = foot.Position;
        _fullNote.Size = new Vector2(Mathf.Max(0f, left - foot.Position.X), foot.Size.Y);

        _log.Position = new Vector2(x, rowY - pad - logH);
        _log.Size = new Vector2(blockW, logH);
        _logBack.Position = _log.Position - new Vector2(6f * sc, 4f * sc);
        _logBack.Size = _log.Size + new Vector2(12f * sc, 8f * sc);
        _logBack.Visible = _log.Visible && !ShowingHistory;
        HudDress();
        // The scrollbar itself is not wanted on the HUD - the full log
        // has its own screen - so it is given no width.
        VScrollBar bar = _log.GetVScrollBar();
        if (bar != null) { bar.CustomMinimumSize = Vector2.Zero; bar.Modulate = new Color(1, 1, 1, 0); }
    }

    /// <summary>
    /// Re-applies the font sizes when the player's scale moved. A Godot
    /// control keeps whatever size it was given, so a strip that grew its
    /// box and not its lines would be the obvious failure.
    /// </summary>
    void Redress(float sc)
    {
        if (Mathf.IsEqualApprox(_dressedAt, sc)) return;
        _dressedAt = sc;
        int pt = Mathf.Max(8, Mathf.RoundToInt(FontSize * sc));
        // Every family the markup can switch to, not only the normal
        // one: a line the server styled bold would otherwise keep its
        // shipped size while the strip around it scaled.
        _log.AddThemeFontSizeOverride("normal_font_size", pt);
        _log.AddThemeFontSizeOverride("bold_font_size", pt);
        _log.AddThemeFontSizeOverride("italics_font_size", pt);
        _log.AddThemeFontSizeOverride("bold_italics_font_size", pt);
        _log.AddThemeFontSizeOverride("mono_font_size", pt);
        _entry.AddThemeFontSizeOverride("font_size", Mathf.Max(8, Mathf.RoundToInt((FontSize + 2) * sc)));
        _back.AddThemeFontSizeOverride("font_size", Mathf.Max(8, Mathf.RoundToInt((FontSize + 2) * sc)));
        _history.AddThemeFontSizeOverride("font_size", pt);
        _open.AddThemeFontSizeOverride("font_size", pt);
    }

    /// <summary>
    /// The player's transparency and their hide, over the strip and the
    /// row and nothing else.
    ///
    /// The node is not handed to <see cref="M59Hud.Register"/> and this is
    /// done by hand, because this control also carries the full-screen
    /// history: fading or hiding the root would fade a panel, and a panel
    /// is not part of the piece. The hide is ANDed under the existing
    /// rules rather than replacing them - which of the entry and the Say
    /// row is up is still the chat's own business.
    /// </summary>
    void HudDress()
    {
        M59Hud.Piece p = M59Hud.Get("chat");
        float a = p == null || M59Hud.Editing ? 1f
                : Mathf.Clamp(p.Alpha, M59Hud.MinAlpha, M59Hud.MaxAlpha);
        var tint = new Color(1f, 1f, 1f, a);
        _logBack.Modulate = tint; _log.Modulate = tint;
        _entry.Modulate = tint; _back.Modulate = tint;
        _open.Modulate = tint; _history.Modulate = tint;

        bool show = M59Hud.Shows("chat");
        _log.Visible = show;
        _logBack.Visible = show && !ShowingHistory;
        // Which half of the row is up is the chat's own state, kept in
        // _typing so this can put it back: hiding the piece and showing
        // it again must not leave both halves away, which would be a chat
        // the player cannot open.
        _entry.Visible = show && _typing;
        _back.Visible = show && _typing;
        _history.Visible = show && !_typing;
        // Say is NOT touched here. It is a registered opener, so
        // Panels.ShowOpeners sets its Visible every frame - including the
        // player's hide, which that file ANDs in for this piece - and a
        // write here would be undone next frame.
    }

    /// <summary>
    /// Whether the entry box is the half of the row that is up. The same
    /// fact <see cref="Capturing"/> reads off the box, kept separately so
    /// the player's hide can be undone without losing it.
    /// </summary>
    bool _typing;

    public override void _ExitTree() => M59Hud.Changed -= Layout;

    /// <summary>True while the full log is covering the screen.</summary>
    public bool ShowingHistory => _fullBack != null && _fullBack.Visible;

    void ShowHistory()
    {
        _fullBack.Visible = true; _fullScroll.Visible = true; _fullClose.Visible = true;
        _fullTitle.Visible = true;
        _fullCard.Visible = true; _fullBar.Visible = true; _fullPage.Visible = true; _fullX.Visible = true;
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
        _fullCard.Visible = false; _fullBar.Visible = false; _fullPage.Visible = false; _fullX.Visible = false;
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
        _typing = true;
        _entry.Visible = true;
        _back.Visible = true;
        _open.Visible = false;
        // The log button sits where the entry does; while you are
        // typing it was drawn across the middle of the sentence.
        _history.Visible = false;
        _entry.GrabFocus();
        Keyboard(true);
        HudDress();
    }

    public void Close()
    {
        _typing = false;
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
        HudDress();
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

        // The player's layout, before the early-out: the strip is rebuilt
        // only when a message arrives, and a drag is not a message.
        string stamp = HudStamp("chat");
        if (stamp != _stamp) { _stamp = stamp; Layout(); }

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

    /// <summary>
    /// The surface chat is read on, in both places it appears: this is
    /// the HUD strip's colour and it is also `M59Skin.Sunken`'s fill,
    /// which is the full log's page. Every contrast figure below is
    /// against this one value.
    /// </summary>
    static readonly Color Back = new Color(0.055f, 0.051f, 0.045f);
    const string BackHex = "0e0d0b";

    /// <summary>
    /// What a run too dark to sit on <see cref="Back"/> is given to sit
    /// on instead: parchment, close to the skin's own body text colour so
    /// it reads as ink on a page rather than as a highlighter.
    /// </summary>
    const string ChipHex = "ede7dc";

    /// <summary>
    /// Below this against the backing a run is unreadable and needs the
    /// chip. Set at 3:1 - the large-text floor - deliberately low: it
    /// takes exactly the fifteen colours that genuinely cannot be seen
    /// (1.0:1 to 2.8:1) and leaves Fire at 3.9:1 and Magenta at 4.1:1
    /// alone, which are the ladder's own floor and are WORSE on parchment
    /// (3.8:1, 3.5:1) than on the dark. A chip is a visible thing; it is
    /// given only where the alternative is nothing at all.
    /// </summary>
    const double Floor = 3.0;

    /// <summary>One coloured run.</summary>
    /// <remarks>
    /// The colours are the reference's, exactly (`Constants.h`, via
    /// `Util.h:880-954`) and are not ours to change. Fifteen of them -
    /// Black, Blue, Green, Purple, Red, Drab, ImperialBlue, Steel,
    /// Violet, QuestRed and Gray1-5 - are under 3:1 against the surface
    /// chat is read on, some of them at 1.0:1, so something of this
    /// client's own has to carry them.
    ///
    /// What that used to be was a 2px #e6e6e6 OUTLINE, and it worked in
    /// the sense that the letters were legible. But an outline surrounds
    /// the glyph, so a pale one on a dark page is a halo: on a phone,
    /// where a 16pt glyph stem is about two pixels wide, the ring is as
    /// wide as the letter it rings and the eye reads the pale fringe
    /// rather than the dark shape inside it. The owner's words were
    /// "weird white around them". That is the halo.
    ///
    /// What replaces it is a solid parchment BACKGROUND behind the run -
    /// one rectangle, not a ring - with the outline explicitly off so the
    /// label's black edge does not blob the glyph on it. Nothing touches
    /// the letterform: the edges are the font's own, the fringing has
    /// nowhere to come from, and the dark colours go from 1.0-2.8:1 to
    /// 5.2-17.1:1. A pale outline cannot be made to not surround the
    /// letter; this does not surround it in the first place.
    ///
    /// A drop shadow was the other candidate and cannot work here: Godot
    /// gives a RichTextLabel one shadow for the whole label, not one per
    /// run, and a shadow dark enough to read as depth does nothing
    /// whatever for text that is itself black.
    /// </remarks>
    static string Dressed(string hex, string part)
    {
        string run = $"[color=#{hex}]{part}[/color]";
        return Contrast(hex, BackHex) < Floor
            ? $"[bgcolor=#{ChipHex}][outline_size=0]{run}[/outline_size][/bgcolor]"
            : run;
    }

    /// <summary>
    /// WCAG 2.1 relative luminance, which is linear-light and so counts
    /// 0000ff (0.07) as dark where a naive average would not.
    /// </summary>
    static double Luminance(string hex)
    {
        static double Lin(int v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(Convert.ToInt32(hex.Substring(0, 2), 16))
             + 0.7152 * Lin(Convert.ToInt32(hex.Substring(2, 2), 16))
             + 0.0722 * Lin(Convert.ToInt32(hex.Substring(4, 2), 16));
    }

    /// <summary>
    /// WCAG contrast ratio between two hex colours, 1:1 to 21:1. The
    /// measure the audit used, and the one every figure in this file is
    /// quoted in.
    /// </summary>
    static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
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
