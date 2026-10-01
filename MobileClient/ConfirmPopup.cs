using System;
using Godot;

/// <summary>
/// "Are you sure?"
///
/// `UIConfirmPopup.cpp`. One popup used by everything in the game that
/// can do something it cannot undo - exiling a member, disbanding a
/// guild, giving up a hall, renouncing - and by the places that just
/// have something to say, which use the same window with a single OK.
///
/// Four things in that file decide how it behaves, and all four are
/// kept:
///
///  - it carries an id. The caller puts one in and reads it back when
///    the answer comes, which is how "exile" knows who it was about
///    without holding a reference across the popup.
///  - No is the default. The popup activates the No button when it
///    opens, and Return, Space and Escape all press it - so the worst
///    outcome of a stray keystroke is nothing happening. In OK mode
///    those same keys press OK, because there is no wrong answer to a
///    statement.
///  - it closes itself when the data behind it is invalidated, if the
///    caller said it should. See DataInvalidated below; that is the
///    fourth, and it was the one missing.
///  - a handler is used once. `_RaiseConfirm` and `_RaiseCancel` clear
///    both handlers and the id afterwards, so a second popup cannot
///    fire the first one's action. That matters here for the same
///    reason it mattered there: these actions are irreversible.
///
/// A fifth, which the reference gets from its window system and this
/// client has to build: the window is AlwaysOnTop
/// (`Meridian59.layout:2859`, `:2873`) and is not modal
/// (`UIConfirmPopup.cpp`), so it is moved to front when shown and nothing
/// else is ever drawn over it afterwards. Here "on top" is "last child".
/// <see cref="Panels.ToFront"/> puts it back after any panel that raises
/// itself through it, and <see cref="Reraise"/> - hung off the parent's
/// ChildOrderChanged - catches the rest: a panel's own MoveChild, and
/// every AddChild of a node built after the popup. A question that is
/// armed is a question that is visible.
///
/// And one decision the reference does not make for us: what an arriving
/// question does to the one on screen. See <see cref="Choice"/>.
///
/// Until now the mobile client sent exile, disband and abandon-hall
/// straight off the button press with nothing in between. On a phone
/// that is worse than on a desktop, not better.
/// </summary>
public partial class ConfirmPopup : Control
{
    [Export] public int FontSize = 17;

    ColorRect _shade;
    /// <summary>The card. Named _panel still because IsOpen is read off it.</summary>
    Panel _panel;
    Panel _bar;
    Label _heading, _text;
    Button _yes, _no, _ok;

    /// <summary>
    /// The question is the biggest thing on the card. FontSize is the
    /// caller's number for the ordinary text in this client; a question
    /// a player is about to answer irreversibly is not ordinary text,
    /// so it is set from it rather than to it.
    /// </summary>
    int QuestionSize => FontSize + 7;

    Action<uint> _confirmed;
    Action _cancelled;
    uint _id;

    /// <summary>Statements waiting behind the one on screen. See <see cref="Tell"/>.</summary>
    struct Waiting { public string Text; public Action<uint> Acknowledged; public bool CloseOnInvalidate; }
    readonly System.Collections.Generic.Queue<Waiting> _waiting = new System.Collections.Generic.Queue<Waiting>();
    const int MaxWaiting = 8;

    /// <summary>
    /// A question was turned away because another one was on screen.
    /// The argument is the text that was refused. GameView puts it in the
    /// chat, so a refusal is something the player is told rather than a
    /// button that silently did nothing.
    /// </summary>
    public event Action<string> Refused;

    /// <summary>
    /// Whether an invalidation of the server's data takes this popup
    /// down with it. Set per call, exactly as the reference sets it per
    /// call: `ShowChoice` and `ShowOK` both take a closeOnInvalidate
    /// argument and store it (`UIConfirmPopup.cpp:52-68`, `:83-99`).
    ///
    /// The distinction the reference draws with it is worth keeping. A
    /// question about something IN the world - exile this member,
    /// disband this guild, change these stats - passes true, because its
    /// Yes acts on an id that only means anything while the lists it came
    /// from are still valid (`UIGuild.cpp:865`, `:878`, `:884`,
    /// `UIStatChangeWizard.cpp:429`). A popup about the connection or the
    /// account passes false, because there is no game data behind it to
    /// go stale (`OgreClient.cpp:653`, `:895`, `:908`).
    /// </summary>
    bool _closeOnInvalidate = true;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The shade is not decoration: it is what stops a tap meant for
        // the popup landing on the panel underneath it.
        _shade = new ColorRect { Color = M59Skin.Scrim, Visible = false, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_shade);

        _panel = M59Skin.Window();
        _panel.Visible = false;
        AddChild(_panel);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        // No close in the title bar, deliberately: a question has two
        // answers and both of them are buttons. A third way out would be
        // a third answer with no handler behind it.
        _heading = M59Skin.Title("");
        _heading.Visible = false;
        AddChild(_heading);

        _text = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _text.AddThemeFontSizeOverride("font_size", QuestionSize);
        _text.AddThemeColorOverride("font_color", M59Skin.Text);
        AddChild(_text);

        // The two answers are not the same button. Yes is the one that
        // acts - gold, or red when what it does cannot be taken back -
        // and No is the plain one that backs out.
        _yes = Push("Yes", () => Answer(true), M59Skin.Kind.Primary);
        _no = Push("No", () => Answer(false), M59Skin.Kind.Secondary);
        _ok = Push("OK", () => Answer(true), M59Skin.Kind.Primary);

        GetViewport().SizeChanged += Layout;
        // AlwaysOnTop, for real. Anything that adds, removes or moves a
        // sibling raises ChildOrderChanged on the parent, so this sees
        // every way a node can end up above an armed popup, including the
        // ones that never go near Panels.ToFront. Deferred because the
        // signal fires from inside the move or the add, and changing the
        // child list from in there is exactly what the engine refuses
        // while a parent is busy setting up its children.
        Node parent = GetParent();
        if (parent != null) parent.ChildOrderChanged += OnSiblingsChanged;
        Layout();
    }

    public override void _ExitTree()
    {
        Node parent = GetParent();
        if (parent != null && GodotObject.IsInstanceValid(parent))
            parent.ChildOrderChanged -= OnSiblingsChanged;
    }

    bool _reraisePending;

    void OnSiblingsChanged()
    {
        if (!IsOpen || _reraisePending) return;
        Node parent = GetParent();
        if (parent == null || parent.GetChildCount() < 2 || parent.GetChild(parent.GetChildCount() - 1) == this) return;
        _reraisePending = true;
        CallDeferred(nameof(Reraise));
    }

    /// <summary>Back to the last child, if something has got in front of it.</summary>
    void Reraise()
    {
        _reraisePending = false;
        if (!IsOpen || !IsInsideTree()) return;
        Panels.KeepPopupOnTop(GetParent());
    }

    Button Push(string text, Action pressed, M59Skin.Kind kind)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// A question is a SMALL card, not a screen of black: the width is
    /// chosen first, the question measured against it, and the card
    /// comes out exactly as tall as what it has to say.
    /// <see cref="M59Skin.Frame"/> does the height that way; the width
    /// is capped here because Frame has no argument for it and its own
    /// cap is the one a LIST wants - most of a sideways screen, which
    /// for four words of question is the fault being fixed.
    /// </summary>
    static Rect2 Small(Vector2 v, float wantH, float wantW)
    {
        Rect2 r = M59Skin.Frame(v, wantH);
        float w = Mathf.Min(r.Size.X, wantW);
        return new Rect2(Mathf.Round((v.X - w) * 0.5f), r.Position.Y, Mathf.Round(w), r.Size.Y);
    }

    void Layout()
    {
        if (_panel == null) return;
        // Nothing to lay out against once this has left the tree, and
        // asking anyway is an error rather than a zero: GetViewportRect
        // is guarded by `!is_inside_tree()` inside the engine and prints
        // every time it is called from outside it.
        //
        // That is not a hypothetical. Teardown walks straight through
        // here: GameView._ExitTree disconnects, BaseClient.Disconnect
        // resets the DataController, the reset raises Invalidate, and
        // Invalidate is wired to DataInvalidated, which calls Show(false),
        // which calls this - by which point the popup is on its way out
        // of the tree and there is no viewport to measure. Every single
        // run ended with that error in the log.
        //
        // A hidden popup does not need a layout in any case, so leaving
        // without one costs nothing: Show(true) lays it out again on the
        // way in, and so does the viewport's SizeChanged.
        if (!IsInsideTree()) return;
        Vector2 v = GetViewportRect().Size;

        _shade.Position = Vector2.Zero;
        _shade.Size = v;

        // Width first, then the question measured against it, then the
        // card sized to the answer. A confirmation is one line and the
        // quest window's help is fifteen; the reference has a second,
        // larger popup for exactly that case - ShowOKLarge
        // (`UINPCQuestList.cpp:357`) - and measuring is the same thing
        // without a second box.
        float w = Mathf.Clamp(v.X * 0.40f, 380f, 720f);
        float wrap = w - M59Skin.Pad * 2f;
        float textH = _text != null
            ? _text.GetThemeFont("font").GetMultilineStringSize(
                  _text.Text ?? "", HorizontalAlignment.Left, wrap,
                  _text.GetThemeFontSize("font_size")).Y
            : 0f;

        Rect2 card = Small(v, textH + M59Skin.Gap * 2f, w);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _panel.Position = card.Position;
        _panel.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _heading.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _heading.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        _text.Position = body.Position;
        _text.Size = body.Size;

        // Right to left, the house order (M59Skin.FootRow): the button
        // that dismisses sits under the thumb at the right edge, which
        // here is also the safe default - and Yes, which is the one that
        // cannot be taken back, is NOT where a thumb rests.
        if (_ok.Visible) M59Skin.FootRow(foot, _ok);
        else M59Skin.FootRow(foot, _no, _yes);
    }

    /// <summary>
    /// A yes/no question. <paramref name="id"/> comes back with the
    /// answer, which is how the caller knows what it asked about.
    ///
    /// Returns false, and asks nothing, when something is already on
    /// screen. That is a decision, not a gap. The reference has no
    /// answer to copy: its ShowChoice overwrites the text and the id and
    /// then `Show*` adds the handler with `+=` (`UIConfirmPopup.cpp:52-82`),
    /// so a second question leaves BOTH actions bound to the one Yes -
    /// the very hazard `_RaiseConfirm` clearing the handlers afterwards
    /// exists to contain. Replacing the question is the same bug with a
    /// different face: the player is reading "exile Bob?", a push swaps
    /// the words under their thumb, and the Yes they were about to press
    /// answers something else. Queueing a QUESTION is no better, because
    /// it comes up later, after the player has moved on, asking about
    /// something they no longer have in front of them - the exact
    /// out-of-context popup this class exists to prevent.
    ///
    /// So a question never displaces and never waits: it is refused, the
    /// caller is told (and <see cref="Refused"/> tells the player), and
    /// whatever started it can be started again once the screen is clear.
    /// Every question this client asks starts from the player's own tap,
    /// and with the shade over everything the only way to start a second
    /// is a keyboard or a typed command (/suicide), which is a "try again"
    /// and costs nothing. Nothing that a server pushes is a question - see
    /// <see cref="Tell"/> for the ones that are.
    /// </summary>
    /// <param name="destructive">
    /// Whether Yes destroys something. Cosmetic only - it decides
    /// whether Yes is the gold button or the red one - but the two
    /// should not look alike, and nothing but the caller knows which
    /// this is: exiling a member and renting a room arrive here
    /// identically.
    /// </param>
    public bool Choice(string text, uint id, Action<uint> confirmed, Action cancelled = null,
                       bool closeOnInvalidate = true, bool destructive = false)
    {
        if (IsOpen)
        {
            GD.Print($"[ConfirmPopup] refused a question while one is open: {text}");
            Refused?.Invoke(text);
            return false;
        }

        _text.Text = text;
        _id = id;
        _confirmed = confirmed;
        _cancelled = cancelled;
        // True by default because every question this client asks is a
        // question about the world - a guild member, a hall, a stat
        // change - and those are the ones the reference marks true.
        _closeOnInvalidate = closeOnInvalidate;

        _heading.Text = "Are you sure?";
        M59Skin.Dress(_yes, destructive ? M59Skin.Kind.Danger : M59Skin.Kind.Primary);

        _yes.Visible = true; _no.Visible = true; _ok.Visible = false;
        Show(true);
        return true;
    }

    /// <summary>
    /// Something to say, with one way out.
    ///
    /// Unlike a question, a statement is queued behind whatever is on
    /// screen rather than refused or replaced, and shown when the player
    /// has answered it. These are the things a SERVER pushes at an
    /// arbitrary moment - a shield claim's refusal (`GameView.ShieldError`,
    /// polled off `GuildShieldInfo`), the password change's verdict
    /// (`PasswordAnswered`) - so two can land in one frame, or land on top
    /// of a question the player is reading. A statement carries no action
    /// the player could be tricked into, so replacing it would only lose
    /// the news, and refusing it would lose the news too; waiting costs
    /// one more tap. They keep arrival order, a repeat of one already
    /// waiting is dropped, and at most <see cref="MaxWaiting"/> wait (the
    /// oldest is kept, the newest dropped - a flood is not a reason to
    /// bury the first thing that was said).
    /// </summary>
    public void Tell(string text, Action<uint> acknowledged = null, bool closeOnInvalidate = true)
    {
        if (IsOpen)
        {
            foreach (Waiting w in _waiting)
                if (w.Text == text) return;
            if (_waiting.Count >= MaxWaiting) return;
            _waiting.Enqueue(new Waiting { Text = text, Acknowledged = acknowledged, CloseOnInvalidate = closeOnInvalidate });
            return;
        }
        ShowTell(text, acknowledged, closeOnInvalidate);
    }

    void ShowTell(string text, Action<uint> acknowledged, bool closeOnInvalidate)
    {
        _text.Text = text;
        _id = 0;
        _confirmed = acknowledged;
        _cancelled = null;
        _closeOnInvalidate = closeOnInvalidate;

        // A statement, not a question: it is told what it is in the
        // title bar so the single button does not read as a choice.
        _heading.Text = "Notice";
        _yes.Visible = false; _no.Visible = false; _ok.Visible = true;
        Show(true);
    }

    /// <summary>The next statement in line, if the screen is clear.</summary>
    void Next()
    {
        if (IsOpen || _waiting.Count == 0 || !IsInsideTree()) return;
        Waiting w = _waiting.Dequeue();
        ShowTell(w.Text, w.Acknowledged, w.CloseOnInvalidate);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront. The popup is
        // the one thing that goes to the very end of the parent's
        // children, and ToFront's own re-raise of an open popup is a
        // no-op for it because it is already last.
        _shade.Visible = on; _panel.Visible = on; _text.Visible = on;
        _bar.Visible = on; _heading.Visible = on;
        if (!on) { _yes.Visible = false; _no.Visible = false; _ok.Visible = false; }
        if (on) Panels.ToFront(this);
        Layout();
    }

    /// <summary>
    /// Both handlers and the id go before the handler runs, so a second
    /// popup cannot fire the first one's action - and so an action that
    /// opens another popup does not clear the one it just opened.
    /// </summary>
    void Answer(bool yes)
    {
        Action<uint> confirmed = _confirmed;
        Action cancelled = _cancelled;
        uint id = _id;

        _confirmed = null; _cancelled = null; _id = 0;
        Show(false);

        if (yes) confirmed?.Invoke(id);
        else cancelled?.Invoke();

        // After the handler, so one that asks a follow-up question gets
        // the screen first and the waiting statements come after it.
        Next();
    }

    /// <summary>
    /// The server has invalidated its data, and this popup may be
    /// asking about something that no longer exists.
    ///
    /// InvalidateData (228) means the server has thrown away the lists it
    /// sent us and will send them again: `HandleInvalidateData` calls
    /// `Invalidate()` (`Meridian59/Data/DataController.cs:2947-2951`),
    /// which clears the online players, the room contents, the guild
    /// roster and the rest (`:1006`). Object ids are handed out per
    /// session and reused, so an id captured before the sweep is not a
    /// promise about anything after it.
    ///
    /// That is why the reference hangs this off its own Invalidate
    /// override (`DataControllerOgre.cpp:61-71`) and why the handler it
    /// calls does three things rather than one
    /// (`UIConfirmPopup.cpp:199-214`): it hides the window, and it drops
    /// both handlers and the id. Hiding alone would not be enough - the
    /// popup would be off screen with a live Yes still bound to a stale
    /// id - and dropping the id alone would leave a question on screen
    /// whose buttons do nothing. Both, or neither.
    ///
    /// Concretely: "Are you sure you want to exile Bob?" is asked, a
    /// system save lands, the guild roster is emptied and re-sent, and
    /// the id that was Bob is now whatever the server next chose to call
    /// it. Yes would exile that instead. Nothing in this client closed
    /// that hole, because nothing in this client could hear an
    /// invalidation at all until `MobileData` existed to raise it.
    ///
    /// Silent on purpose. The player pressed nothing, so neither handler
    /// runs - not Yes, and not the cancel path either, which is the
    /// reference's choice too: a cancel handler is an action, and this is
    /// the absence of one.
    /// </summary>
    public void DataInvalidated()
    {
        // The waiting ones go by the same rule as the one on screen:
        // those that were about the world are stale, the rest are not.
        if (_waiting.Count > 0)
        {
            var keep = new System.Collections.Generic.Queue<Waiting>();
            foreach (Waiting w in _waiting) if (!w.CloseOnInvalidate) keep.Enqueue(w);
            _waiting.Clear();
            foreach (Waiting w in keep) _waiting.Enqueue(w);
        }

        if (IsOpen && _closeOnInvalidate)
        {
            _confirmed = null; _cancelled = null; _id = 0;
            Show(false);
        }
        Next();
    }

    /// <summary>
    /// Escape, and the back gesture that reaches the same place: the No
    /// button, or OK when there is nothing to refuse. That is the
    /// file's own key handling, where Return, Space and Escape all
    /// press the safe one.
    /// </summary>
    public override void _UnhandledInput(InputEvent ev)
    {
        if (!IsOpen) return;
        if (ev is InputEventKey k && k.Pressed && !k.Echo
            && (k.Keycode == Key.Escape || k.Keycode == Key.Enter || k.Keycode == Key.Space))
        {
            Answer(_ok.Visible);
            GetViewport().SetInputAsHandled();
        }
    }
}
