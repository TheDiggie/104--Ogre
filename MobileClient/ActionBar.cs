using System;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// What you can do to the thing you tapped.
///
/// Targeting is the library's own: tapping sets DataController.TargetID,
/// which marks the object and fills TargetObject, and the no-argument
/// Send* overloads act on it. Nothing here keeps a second idea of what is
/// selected.
///
/// The row hides itself when there is no target, because on a phone every
/// permanently visible control is screen the game does not get.
/// </summary>
public partial class ActionBar : Control
{
    [Export] public int FontSize = 16;

    /// <summary>
    /// Pixels at the bottom of the screen already spoken for - the chat
    /// log and its input line. The row sits above them. Widgets that each
    /// picked their own corner ended up on top of each other.
    /// </summary>
    public float BottomReserve
    {
        get => _reserve;
        set { _reserve = value; Layout(); }
    }
    float _reserve;

    // Named away from Godot's own members: a plain "Get" hides
    // GodotObject.Get, and "Show" would sit alongside CanvasItem.Show.
    public event Action LookAt, PickUp, AttackTarget, UseTarget;

    Label _name;
    Button _look, _get, _attack, _use;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _name = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _name.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _name.AddThemeConstantOverride("outline_size", 4);
        AddChild(_name);

        _look   = Make("Look",   () => LookAt?.Invoke());
        _get    = Make("Get",    () => PickUp?.Invoke());
        _attack = Make("Attack", () => AttackTarget?.Invoke());
        _use    = Make("Use",    () => UseTarget?.Invoke());

        GetViewport().SizeChanged += Layout;
        Layout();
        SetTarget((RoomObject)null);
    }

    Button Make(string text, Action pressed)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_name == null) return;
        Vector2 v = GetViewportRect().Size;
        float pad = 12f;
        float h = FontSize * 2.6f;
        float w = (v.X - pad * 5) / 4f;
        float y = v.Y - _reserve - pad - h;

        _name.Position = new Vector2(pad, y - FontSize * 1.8f);
        Button[] all = { _look, _get, _attack, _use };
        for (int i = 0; i < all.Length; i++)
        {
            all[i].Position = new Vector2(pad + i * (w + pad), y);
            all[i].Size = new Vector2(w, h);
        }
    }

    /// <summary>
    /// Shows the row for a target, or hides it when there is none.
    ///
    /// The name is drawn in the colour the server gives that object -
    /// `NameColors.GetColorFor`, the same function the loot list, the look
    /// window and the labels over people's heads use. The game's own
    /// target window does exactly this, and hides itself when the target
    /// has no name or is flagged invisible.
    /// </summary>
    public void SetTarget(RoomObject target)
    {
        bool on = target != null
            && !string.IsNullOrWhiteSpace(target.Name)
            && (target.Flags == null || target.Flags.Drawing != ObjectFlags.DrawingType.Invisible);

        Visible = on;
        if (!on) return;

        _name.Text = target.Name;

        uint argb = target.Flags != null ? NameColors.GetColorFor(target.Flags) : NameColors.NORMAL;
        _name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f));
    }
}
