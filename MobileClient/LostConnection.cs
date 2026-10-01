using System;
using Godot;

/// <summary>
/// What you see when the connection goes away.
///
/// Until this existed, nothing did. The socket dropped, a line of .NET
/// exception text went into the chat log - "IOException: Unable to
/// write data to the transport connection: Broken pipe" - and the last
/// frame of the world stayed on screen with every button still looking
/// live. Nothing worked and nothing said so.
///
/// That is a desktop oversight carried into a place it does not belong.
/// A desktop client loses its connection when something is wrong; a
/// phone loses it when you walk into a lift, and it will happen to
/// every player of this on the first day. It needs a plain sentence and
/// a way back in.
///
/// The exception text still goes to the chat log, because when
/// something genuinely is wrong that is the only clue anyone has.
/// </summary>
public partial class LostConnection : Control
{
    [Export] public int FontSize = 18;

    /// <summary>Try the connection again.</summary>
    public event Action Retry;

    ColorRect _back;
    Panel _card, _bar;
    Label _title, _said, _detail;
    Button _retry;

    public bool IsOpen => _back != null && _back.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Heavier than the panels' scrim on purpose: what is behind this
        // is the last frame of a world you are no longer in, and leaving
        // it readable is what made the old behaviour confusing.
        _back = new ColorRect
        {
            Color = new Color(M59Skin.Scrim.R, M59Skin.Scrim.G, M59Skin.Scrim.B, 0.88f),
            Visible = false,
        };
        AddChild(_back);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Lost connection");
        _title.Visible = false;
        AddChild(_title);

        // What happened and what happens next, in that order. A phone
        // loses its connection in a lift, not because something is
        // broken, and the screen should not sound like a crash.
        _said = Text("The server stopped answering.\n\nReconnect puts you back in the world with the character you were playing.",
                     M59Skin.BodySize, M59Skin.Text);
        // The technical reason, kept and kept SMALL: it is the only clue
        // anyone has when something genuinely is wrong, and it is not
        // what the player is here to read.
        _detail = Text("", M59Skin.SmallSize, M59Skin.TextDim);

        _retry = new Button { Text = "Reconnect", Visible = false, Name = "reconnect" };
        M59Skin.Dress(_retry, M59Skin.Kind.Primary);
        _retry.Pressed += () => { Hide2(); Retry?.Invoke(); };
        AddChild(_retry);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Text(string text, int size, Color colour)
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
        if (_back == null) return;
        Vector2 v = GetViewportRect().Size;

        // Sized here rather than anchored: this lives under a
        // CanvasLayer whose Control parent has no rect, and an anchored
        // child of one of those comes out zero by zero and never draws.
        //
        // And measured in the SCREEN's coordinates rather than this
        // layer's. The interface layer is scaled and shifted to keep
        // controls off the curve of the glass (SafeArea.Apply), so a
        // rectangle the size of the viewport laid out in the layer's own
        // coordinates lands inset by exactly that much: at 1920x1080 it
        // stops about forty-eight pixels short on the right and leaves
        // thinner strips top and bottom, and what shows through them is
        // a live, undimmed band of the world you have just been
        // disconnected from. The backdrop is not part of the interface -
        // it belongs to the whole screen - so the layer's transform is
        // undone here, which is what ScreenEffects.Sync and
        // PlayerOverlays.Sync already do for the same reason.
        //
        // The text and the button are deliberately NOT treated this way:
        // they ARE interface, and they should stay inside the safe
        // rectangle with everything else.
        Vector2 scale = Vector2.One, offset = Vector2.Zero;
        if (GetParent() is CanvasLayer layer) { scale = layer.Scale; offset = layer.Offset; }
        if (scale.X <= 0f) scale.X = 1f;
        if (scale.Y <= 0f) scale.Y = 1f;
        _back.Position = new Vector2(-offset.X / scale.X, -offset.Y / scale.Y);
        _back.Size = new Vector2(v.X / scale.X, v.Y / scale.Y);

        // A centred card, like every panel, rather than three lines
        // floating in the middle of a black screen.
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

        M59Skin.FootRow(M59Skin.Foot(card), _retry);
    }

    /// <summary>How tall a wrapped label comes out at this width.</summary>
    static float Measure(Label l, float wrap)
        => l == null ? 0f
         : l.GetThemeFont("font").GetMultilineStringSize(
               l.Text ?? "", HorizontalAlignment.Left, wrap,
               l.GetThemeFontSize("font_size")).Y;

    /// <summary>Puts it up, with the technical reason underneath.</summary>
    public void Show(string detail)
    {
        if (IsOpen) return;
        Panels.ToFront(this);
        _detail.Text = detail ?? "";
        _back.Visible = _title.Visible = _detail.Visible = _retry.Visible = true;
        _card.Visible = _bar.Visible = _said.Visible = true;
        // Laid out on the way in, the way LeftWorld.Open does: the
        // backdrop's rectangle now depends on the interface layer's
        // transform, and that is settled after this panel was built.
        Layout();
    }

    /// <summary>Takes it down. Named so as not to hide Control.Hide.</summary>
    public void Hide2()
    {
        if (_back == null) return;
        _back.Visible = _title.Visible = _detail.Visible = _retry.Visible = false;
        _card.Visible = _bar.Visible = _said.Visible = false;
    }
}
