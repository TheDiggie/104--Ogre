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
    Label _title, _detail;
    Button _retry;

    public bool IsOpen => _back != null && _back.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _back = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.88f), Visible = false };
        AddChild(_back);

        _title = Text("Lost connection to the server.", FontSize + 6, new Color(1, 0.92f, 0.6f));
        _detail = Text("", FontSize, new Color(0.8f, 0.82f, 0.88f));

        _retry = new Button { Text = "Reconnect", Visible = false, Name = "reconnect" };
        _retry.AddThemeFontSizeOverride("font_size", FontSize + 2);
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
        if (_back == null) return;
        Vector2 v = GetViewportRect().Size;
        float side = Panels.Side(v, 0.08f, 20f);
        float rowH = FontSize * 2.6f;

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

        float mid = v.Y * 0.4f;
        _title.Position = new Vector2(side, mid);
        _title.Size = new Vector2(v.X - side * 2f, rowH);

        _detail.Position = new Vector2(side, mid + rowH);
        _detail.Size = new Vector2(v.X - side * 2f, rowH * 2f);

        _retry.Position = new Vector2(side, mid + rowH * 3.4f);
        _retry.Size = new Vector2(v.X - side * 2f, rowH);
    }

    /// <summary>Puts it up, with the technical reason underneath.</summary>
    public void Show(string detail)
    {
        if (IsOpen) return;
        Panels.ToFront(this);
        _detail.Text = detail ?? "";
        _back.Visible = _title.Visible = _detail.Visible = _retry.Visible = true;
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
    }
}
