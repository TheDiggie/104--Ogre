using System.Collections.Generic;
using Godot;
using Meridian59.Data;

/// <summary>
/// The word across the middle of the screen when you cannot move.
///
/// This is the game's `UISplashNotifier.cpp`: a list of notifications,
/// the last one added is the one shown, and three things put themselves
/// on it - RESTING while `Data.IsResting`, PARALYZED while
/// `Data.Effects.Paralyze.IsActive`, SAVING while `Data.IsWaiting`. The
/// game's fourth, PRESS A KEY, belongs to the options window's key
/// learning and has no place on a phone.
///
/// It matters more here than it does there. All three states stop the
/// avatar dead: `BaseClient.SendReqMoveMessage` returns early on
/// resting, waiting or paralyze, and `TryMove` denies the step. On a
/// desktop a player who cannot walk still has a keyboard, a chat log
/// and a mouse to reason with. On a phone a thumb on a stick that moves
/// nothing, with nothing on screen to say why, is a broken game - the
/// same failure the lost-connection overlay exists for.
///
/// Placed as the game places it: centred, at four tenths of the way
/// down, large, and passing every touch straight through.
/// </summary>
public partial class SplashNotifier : Control
{
    [Export] public int FontSize = 44;

    // The game's own strings (`Constants.h:934-936`).
    public const string Resting   = "RESTING";
    public const string Paralyzed = "PARALYZED";
    public const string Saving    = "SAVING";

    readonly List<string> _notifications = new List<string>();
    Label _label;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _label.AddThemeConstantOverride("outline_size", 8);
        AddChild(_label);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_label == null) return;
        Vector2 v = GetViewportRect().Size;
        // {{0.5,-300},{0.4,-30},{0.5,300},{0.4,30}} in the game's layout:
        // centred on the width, centred on four tenths of the height.
        _label.Size = new Vector2(v.X, FontSize * 2f);
        _label.Position = new Vector2(0, v.Y * 0.4f - FontSize);
    }

    /// <summary>
    /// Reads the three states and keeps the list the way the game keeps
    /// it: a state that turns on is appended, a state that turns off is
    /// removed, and what shows is whatever was added last. Polled rather
    /// than subscribed - three bools a frame is cheaper than three
    /// PropertyChanged handlers, and there is no ordering subtlety left
    /// once the list does the ordering.
    /// </summary>
    public void Sync(DataController data)
    {
        if (data == null) { Set(Resting, false); Set(Paralyzed, false); Set(Saving, false); return; }

        Set(Resting, data.IsResting);
        Set(Paralyzed, data.Effects?.Paralyze?.IsActive ?? false);
        Set(Saving, data.IsWaiting);

        if (_label != null)
            _label.Text = _notifications.Count > 0
                ? _notifications[_notifications.Count - 1] : "";
    }

    void Set(string what, bool on)
    {
        bool had = _notifications.Contains(what);
        if (on && !had) _notifications.Add(what);
        else if (!on && had) _notifications.Remove(what);
    }
}
