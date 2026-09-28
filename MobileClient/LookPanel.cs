using System;
using Godot;
using Meridian59.Data;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// What you see when you look at something.
///
/// `UIObjectDetails.cpp` is the window this follows: a picture of the
/// thing, its name in the colour the server gives it, the description the
/// server sent, and an inscription underneath when the thing carries one.
///
/// All four come from the client's own `Data.LookObject`, an `ObjectInfo`
/// the library fills from the server's reply - the object, the message,
/// the inscription, the look type and whether the window is up. The view
/// follows those; it does not decide when to appear. Sending the look is
/// the only half this client had.
///
/// The picture is composed with the **viewer's** frame, not the front one
/// - that is the one difference from the inventory's icons, and it is
/// what makes a creature in the look window face the way it faces in the
/// world.
/// </summary>
public partial class LookPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int PictureSize = 128;

    ColorRect _panel;
    TextureRect _picture;
    Label _name;
    RichTextLabel _description;
    Label _inscription;
    Button _close;

    uint _shown;
    string _lastText = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        AddChild(_panel);

        _picture = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_picture);

        _name = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _name.AddThemeConstantOverride("outline_size", 3);
        AddChild(_name);

        _description = new RichTextLabel { Visible = false, BbcodeEnabled = true, ScrollActive = true };
        _description.AddThemeFontSizeOverride("normal_font_size", FontSize);
        AddChild(_description);

        _inscription = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _inscription.AddThemeFontSizeOverride("font_size", FontSize - 1);
        _inscription.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.6f));
        AddChild(_inscription);

        _close = new Button { Text = "Close", Visible = false };
        _close.AddThemeFontSizeOverride("font_size", FontSize);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Mathf.Max(16f, v.X * 0.06f);
        float w = v.X - side;
        float h = Mathf.Min(v.Y * 0.55f, 520f);
        float top = v.Y * 0.18f;

        _panel.Position = new Vector2(side * 0.5f, top);
        _panel.Size = new Vector2(w, h);

        _picture.Position = new Vector2(side, top + 14f);
        _picture.Size = new Vector2(PictureSize, PictureSize);

        _name.Position = new Vector2(side + PictureSize + 16f, top + 16f);

        float textTop = top + PictureSize + 26f;
        _description.Position = new Vector2(side, textTop);
        _description.Size = new Vector2(w - side, h - (textTop - top) - FontSize * 5f);

        _inscription.Position = new Vector2(side, top + h - FontSize * 4.4f);
        _inscription.Size = new Vector2(w - side, FontSize * 2f);

        _close.Position = new Vector2(side, top + h - FontSize * 2.4f - 8f);
        _close.Size = new Vector2(w - side, FontSize * 2.4f);
    }

    public void Close()
    {
        Show(false);
        // The server's own flag, so the client and the view agree on
        // whether the window is up.
        if (_info != null) _info.IsVisible = false;
    }

    void Show(bool on)
    {
        _panel.Visible = on;
        _picture.Visible = on && _picture.Texture != null;
        _name.Visible = on;
        _description.Visible = on;
        _inscription.Visible = on && !string.IsNullOrWhiteSpace(_inscription.Text);
        _close.Visible = on;
    }

    ObjectInfo _info;

    /// <summary>
    /// Follows the client's look object. Everything here - whether the
    /// window is up, what is in it - is the server's, by way of the
    /// library.
    /// </summary>
    public void Sync(DataController data)
    {
        _info = data?.LookObject;
        if (_info == null) { if (IsOpen) Show(false); return; }

        if (!_info.IsVisible) { if (IsOpen) Show(false); return; }

        ObjectBase o = _info.ObjectBase;
        string text = _info.Message?.FullString ?? "";
        string ins = _info.Inscription?.FullString ?? "";

        uint id = o?.ID ?? 0;
        if (id != _shown || text != _lastText)
        {
            _shown = id;
            _lastText = text;

            _name.Text = o?.Name ?? "";
            if (o?.Flags != null)
            {
                uint argb = NameColors.GetColorFor(o.Flags);
                _name.AddThemeColorOverride("font_color", new Color(
                    ((argb >> 16) & 0xFF) / 255f,
                    ((argb >> 8) & 0xFF) / 255f,
                    (argb & 0xFF) / 255f));
            }

            _description.Text = text;
            _inscription.Text = ins;

            try
            {
                // The viewer's frame, which is what this window uses and
                // the inventory does not.
                _picture.Texture = M59Assets.FromTex(Viewer(o, PictureSize));
            }
            catch (Exception e) { GD.PrintErr($"[Look] {o?.Name}: {e.Message}"); }
        }

        if (!IsOpen) Show(true);
    }

    /// <summary>
    /// Composes with the viewer's frame. `M59Compose.Icon` uses the front
    /// frame, as the inventory wants; this window wants the other.
    /// </summary>
    static Tex Viewer(ObjectBase o, int size)
    {
        if (o is RoomObject ro)
        {
            Tex t = M59Compose.Build(ro, out _, out _);
            if (t != null) return t;
        }
        return M59Compose.Icon(o, size);
    }
}
