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
/// Three things in that file decide how it behaves, and all three are
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
        float h = Mathf.Min(v.Y * 0.34f, 300f);
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
    public void Choice(string text, uint id, Action<uint> confirmed, Action cancelled = null)
    {
        _text.Text = text;
        _id = id;
        _confirmed = confirmed;
        _cancelled = cancelled;

        _yes.Visible = true; _no.Visible = true; _ok.Visible = false;
        Show(true);
    }

    /// <summary>Something to say, with one way out.</summary>
    public void Tell(string text, Action<uint> acknowledged = null)
    {
        _text.Text = text;
        _id = 0;
        _confirmed = acknowledged;
        _cancelled = null;

        _yes.Visible = false; _no.Visible = false; _ok.Visible = true;
        Show(true);
    }

    void Show(bool on)
    {
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
