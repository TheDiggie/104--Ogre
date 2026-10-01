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
/// Until now the mobile client sent exile, disband and abandon-hall
/// straight off the button press with nothing in between. On a phone
/// that is worse than on a desktop, not better.
/// </summary>
public partial class ConfirmPopup : Control
{
    [Export] public int FontSize = 17;

    ColorRect _shade, _panel;
    Label _text;
    Button _yes, _no, _ok;

    Action<uint> _confirmed;
    Action _cancelled;
    uint _id;

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
        _shade = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_shade);

        _panel = new ColorRect { Color = new Color(0.06f, 0.06f, 0.08f, 1f), Visible = false };
        AddChild(_panel);

        _text = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _text.AddThemeFontSizeOverride("font_size", FontSize);
        _text.AddThemeColorOverride("font_color", new Color(0.92f, 0.93f, 0.96f));
        AddChild(_text);

        _yes = Push("Yes", () => Answer(true));
        _no = Push("No", () => Answer(false));
        _ok = Push("OK", () => Answer(true));

        GetViewport().SizeChanged += Layout;
        Layout();
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

        _shade.Position = Vector2.Zero;
        _shade.Size = v;

        float w = Mathf.Min(v.X * 0.86f, 620f);
        // Tall enough for what is in it. A confirmation is one line and
        // 300 was plenty; the quest window's help is fifteen, and a box
        // that does not grow simply spills its text across the screen
        // behind it. The reference has a second, larger popup for
        // exactly this case - ShowOKLarge (`UINPCQuestList.cpp:357`) -
        // and measuring is the same thing without a second box.
        float wrap = w - 40f;
        float textH = _text != null
            ? _text.GetThemeFont("font").GetMultilineStringSize(
                  _text.Text ?? "", HorizontalAlignment.Left, wrap,
                  _text.GetThemeFontSize("font_size")).Y
            : 0f;
        float h = Mathf.Clamp(textH * 1.15f + FontSize * 2.6f + 76f, v.Y * 0.34f, v.Y * 0.86f);
        h = Mathf.Min(h, v.Y - 24f);
        float x = (v.X - w) * 0.5f, y = (v.Y - h) * 0.5f;
        float rowH = FontSize * 2.6f;

        _panel.Position = new Vector2(x, y);
        _panel.Size = new Vector2(w, h);

        _text.Position = new Vector2(x + 20f, y + 16f);
        _text.Size = new Vector2(w - 40f, h - rowH - 44f);

        float by = y + h - rowH - 16f;
        if (_ok.Visible)
        {
            _ok.Position = new Vector2(x + 20f, by);
            _ok.Size = new Vector2(w - 40f, rowH);
        }
        else
        {
            float each = (w - 48f) * 0.5f;
            // No first, on the left: it is the default, and the
            // destructive one should not be where a thumb rests.
            _no.Position = new Vector2(x + 20f, by);
            _no.Size = new Vector2(each, rowH);
            _yes.Position = new Vector2(x + 28f + each, by);
            _yes.Size = new Vector2(each, rowH);
        }
    }

    /// <summary>
    /// A yes/no question. <paramref name="id"/> comes back with the
    /// answer, which is how the caller knows what it asked about.
    /// </summary>
    public void Choice(string text, uint id, Action<uint> confirmed, Action cancelled = null,
                       bool closeOnInvalidate = true)
    {
        _text.Text = text;
        _id = id;
        _confirmed = confirmed;
        _cancelled = cancelled;
        // True by default because every question this client asks is a
        // question about the world - a guild member, a hall, a stat
        // change - and those are the ones the reference marks true.
        _closeOnInvalidate = closeOnInvalidate;

        _yes.Visible = true; _no.Visible = true; _ok.Visible = false;
        Show(true);
    }

    /// <summary>Something to say, with one way out.</summary>
    public void Tell(string text, Action<uint> acknowledged = null, bool closeOnInvalidate = true)
    {
        _text.Text = text;
        _id = 0;
        _confirmed = acknowledged;
        _cancelled = null;
        _closeOnInvalidate = closeOnInvalidate;

        _yes.Visible = false; _no.Visible = false; _ok.Visible = true;
        Show(true);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _shade.Visible = on; _panel.Visible = on; _text.Visible = on;
        if (!on) { _yes.Visible = false; _no.Visible = false; _ok.Visible = false; }
        if (on) GetParent()?.MoveChild(this, -1);
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
        if (!_closeOnInvalidate) return;

        _confirmed = null; _cancelled = null; _id = 0;
        Show(false);
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
