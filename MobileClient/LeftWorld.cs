using System;
using Godot;

/// <summary>
/// What you see when the server has ended the session on purpose.
///
/// Quit (149) is the server saying "you are done here" - an ordinary
/// logout, a kick, a shutdown. The library's handler resets the data and
/// stops (`Meridian59/Client/BaseClient.cs:648-651`); the reference does
/// two more things on top of it, writing the played action button set to
/// its config and dropping back to the scene it selects characters from
/// (`Meridian59.Ogre.Client/OgreClient.cpp:952-962`, the same place
/// `HandleCharactersMessage` at `:946-949` puts its
/// `UIMode::AvatarSelection`).
///
/// This client did neither, and the failure was a confusing one rather
/// than an obvious one. The HUD stayed drawn over a room that had just
/// been emptied - every button live, nothing behind them - for as long as
/// the server took to close the socket. Then the close arrived as a read
/// error and the player was told they had lost their connection, which
/// was not true: they had been logged out, and the fix is not to retry a
/// connection but to log in again. A kicked player was told the wrong
/// thing about why.
///
/// So this is deliberately NOT `LostConnection`. Same shape, same way
/// back in - a full-screen notice with one button that reconnects, which
/// is how this client already returns to the login and character screens
/// - but it says what actually happened. That distinction is the whole
/// reason it is a separate panel.
///
/// House rule: a panel, not an OS dialog. Nothing in this client puts a
/// system dialog on screen.
/// </summary>
public partial class LeftWorld : Control
{
    [Export] public int FontSize = 18;

    /// <summary>Back to the login and character screens.</summary>
    public event Action Back;

    ColorRect _backdrop;
    Label _title, _detail;
    Button _again;

    public bool IsOpen => _backdrop != null && _backdrop.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Opaque enough to hide the dead last frame behind it, and a
        // ColorRect rather than a transparent root, because the frame
        // underneath is a photograph of a room that no longer exists and
        // leaving it readable is what made the old behaviour confusing.
        // MouseFilter.Stop on it is the half that matters: it swallows
        // taps meant for the HUD buttons still drawn beneath.
        _backdrop = new ColorRect
        {
            Color = new Color(0.02f, 0.02f, 0.03f, 0.92f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_backdrop);

        _title = Line("You have left the world.", FontSize + 6, new Color(1, 0.92f, 0.6f));
        _detail = Line("", FontSize, new Color(0.8f, 0.82f, 0.88f));

        _again = new Button { Text = "Log in again", Visible = false, Name = "loginAgain" };
        _again.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _again.Pressed += () => { Close(); Back?.Invoke(); };
        AddChild(_again);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Line(string text, int size, Color colour)
    {
        var l = new Label
        {
            Text = text,
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        AddChild(l);
        return l;
    }

    void Layout()
    {
        if (_backdrop == null) return;
        Vector2 v = GetViewportRect().Size;
        float side = Panels.Side(v, 0.08f, 20f);
        float rowH = FontSize * 2.6f;

        // Positioned here rather than anchored, for the reason every
        // panel in this client gives: these live under a CanvasLayer
        // whose Control parent has no rect of its own, and an anchored
        // child of one of those comes out zero by zero and never draws.
        //
        // And in the SCREEN's coordinates rather than the layer's. The
        // interface layer is scaled and shifted to keep controls off the
        // curve of the glass (SafeArea.Apply), so a rectangle the size of
        // the viewport laid out in the layer's own coordinates comes out
        // inset by that much - about forty-eight pixels short down the
        // right at 1920x1080, with thinner strips top and bottom. Through
        // those strips you see the last frame of the room you have just
        // been thrown out of, lit and undimmed, which is precisely the
        // "photograph of a room that no longer exists" this backdrop
        // exists to cover. So the layer's transform is undone, the same
        // way ScreenEffects.Sync and PlayerOverlays.Sync undo it and for
        // the same stated reason: the effect belongs to the whole screen.
        //
        // The title, the detail line and the button are left alone: those
        // are interface, and they belong inside the safe rectangle.
        Vector2 scale = Vector2.One, offset = Vector2.Zero;
        if (GetParent() is CanvasLayer layer) { scale = layer.Scale; offset = layer.Offset; }
        if (scale.X <= 0f) scale.X = 1f;
        if (scale.Y <= 0f) scale.Y = 1f;
        _backdrop.Position = new Vector2(-offset.X / scale.X, -offset.Y / scale.Y);
        _backdrop.Size = new Vector2(v.X / scale.X, v.Y / scale.Y);

        float mid = v.Y * 0.4f;
        _title.Position = new Vector2(side, mid);
        _title.Size = new Vector2(v.X - side * 2f, rowH);

        _detail.Position = new Vector2(side, mid + rowH);
        _detail.Size = new Vector2(v.X - side * 2f, rowH * 2f);

        _again.Position = new Vector2(side, mid + rowH * 3.4f);
        _again.Size = new Vector2(v.X - side * 2f, rowH);
    }

    /// <summary>
    /// Puts it up. <paramref name="detail"/> is the second line, for
    /// whatever the server said about it if it said anything.
    /// </summary>
    public void Open(string detail = "")
    {
        if (IsOpen) return;
        // Above whatever else is open - see Panels.ToFront. A quit can
        // land while a panel or a confirmation is on screen, and this
        // has to be the thing in front of it.
        Panels.ToFront(this);
        GetParent()?.MoveChild(this, -1);
        _detail.Text = string.IsNullOrWhiteSpace(detail)
            ? "Log in again to carry on playing."
            : detail;
        _backdrop.Visible = _title.Visible = _detail.Visible = _again.Visible = true;
        Layout();
    }

    /// <summary>Takes it down. Named so as not to hide Control.Hide.</summary>
    public void Close()
    {
        if (_backdrop == null) return;
        _backdrop.Visible = _title.Visible = _detail.Visible = _again.Visible = false;
    }
}
