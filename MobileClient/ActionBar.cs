using System;
using Godot;

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
        SetTarget(null);
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
        Vector2 v = GetViewportRect().Size;
        float pad = 12f;
        float h = FontSize * 2.6f;
        float w = (v.X - pad * 5) / 4f;
        float y = pad + FontSize * 2.4f;

        _name.Position = new Vector2(pad, pad);
        Button[] all = { _look, _get, _attack, _use };
        for (int i = 0; i < all.Length; i++)
        {
            all[i].Position = new Vector2(pad + i * (w + pad), y);
            all[i].Size = new Vector2(w, h);
        }
    }

    /// <summary>Shows the row for a named target, or hides it when null.</summary>
    public void SetTarget(string targetName)
    {
        bool on = !string.IsNullOrEmpty(targetName);
        Visible = on;
        if (on) _name.Text = targetName;
    }
}
