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
    Panel _card, _bar;
    Label _title, _said, _detail;
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
            Color = new Color(M59Skin.Scrim.R, M59Skin.Scrim.G, M59Skin.Scrim.B, 0.92f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_backdrop);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("You have left the world");
        _title.Visible = false;
        AddChild(_title);

        // What happened, said plainly and without blame: this is an
        // ordinary logout as often as it is a kick, and the whole point
        // of this panel is that it is NOT the connection having failed.
        _said = Line("The server ended the session. Nothing was lost.", M59Skin.BodySize, M59Skin.Text);
        // Whatever the server said about it, if it said anything.
        _detail = Line("", M59Skin.SmallSize, M59Skin.TextDim);

        _again = new Button { Text = "Log in again", Visible = false, Name = "loginAgain" };
        M59Skin.Dress(_again, M59Skin.Kind.Primary);
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

        // A centred card, like every panel in this client, rather than
        // three lines floating in the middle of a black screen.
        Rect2 probe = M59Skin.Frame(v);
        float w = Mathf.Min(probe.Size.X, Mathf.Clamp(v.X * 0.46f, 420f, 780f));
        float wrap = w - M59Skin.Pad * 2f;
        float saidH = Measure(_said, wrap);
        float detailH = Measure(_detail, wrap);

        Rect2 full = M59Skin.Frame(v, saidH + detailH + M59Skin.Gap * 3f);
        Rect2 card = new Rect2(Mathf.Round((v.X - w) * 0.5f), full.Position.Y,
                               Mathf.Round(w), full.Size.Y);
        Rect2 body = M59Skin.Body(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        _said.Position = body.Position;
        _said.Size = new Vector2(body.Size.X, saidH);
        _detail.Position = new Vector2(body.Position.X, body.Position.Y + body.Size.Y - detailH);
        _detail.Size = new Vector2(body.Size.X, detailH);

        M59Skin.FootRow(M59Skin.Foot(card), _again);
    }

    /// <summary>How tall a wrapped label comes out at this width.</summary>
    static float Measure(Label l, float wrap)
        => l == null ? 0f
         : l.GetThemeFont("font").GetMultilineStringSize(
               l.Text ?? "", HorizontalAlignment.Left, wrap,
               l.GetThemeFontSize("font_size")).Y;

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
        _card.Visible = _bar.Visible = _said.Visible = true;
        Layout();
    }

    /// <summary>Takes it down. Named so as not to hide Control.Hide.</summary>
    public void Close()
    {
        if (_backdrop == null) return;
        _backdrop.Visible = _title.Visible = _detail.Visible = _again.Visible = false;
        _card.Visible = _bar.Visible = _said.Visible = false;
    }
}
