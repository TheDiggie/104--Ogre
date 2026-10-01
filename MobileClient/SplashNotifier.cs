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
    /// <summary>Two short rules flanking the word. Not a card: see Layout.</summary>
    ColorRect _left, _right;

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
        // The panels' gold rather than white: this belongs to the same
        // family as the cards, and over torchlit stone a warm word reads
        // where a white one glares. The heavy outline stays - it is what
        // keeps the word legible over any wall behind it.
        _label.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _label.AddThemeConstantOverride("outline_size", 8);
        AddChild(_label);

        // It is NOT a card: a state you are stuck in should not take a
        // window out of the world. Two short rules either side are
        // enough to say the word is the client speaking, and they pass
        // every touch through exactly as the label does.
        _left = Rule();
        _right = Rule();

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    ColorRect Rule()
    {
        var r = new ColorRect
        {
            Color = new Color(M59Skin.Gold.R, M59Skin.Gold.G, M59Skin.Gold.B, 0.45f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(r);
        return r;
    }

    void Layout()
    {
        if (_label == null) return;
        Vector2 v = GetViewportRect().Size;
        // {{0.5,-300},{0.4,-30},{0.5,300},{0.4,30}} in the game's layout:
        // centred on the width, centred on four tenths of the height.
        _label.Size = new Vector2(v.X, FontSize * 2f);
        _label.Position = new Vector2(0, v.Y * 0.4f - FontSize);

        // The rules sit on the word's centre line, clear of the longest
        // of the three words at this size.
        float gap = FontSize * 6f;
        float len = Mathf.Min(v.X * 0.14f, 220f);
        float y = Mathf.Round(v.Y * 0.4f) - 1f;
        _left.Position = new Vector2(v.X * 0.5f - gap - len, y);
        _left.Size = new Vector2(len, 2f);
        _right.Position = new Vector2(v.X * 0.5f + gap, y);
        _right.Size = new Vector2(len, 2f);
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

        string word = _notifications.Count > 0 ? _notifications[_notifications.Count - 1] : "";
        if (_label != null) _label.Text = Spaced(word);
        // The rules belong to the word, so they go with it.
        if (_left != null) _left.Visible = word.Length > 0;
        if (_right != null) _right.Visible = word.Length > 0;
    }

    /// <summary>
    /// The word, letter-spaced. Display only - the list still holds the
    /// game's own strings, which is what everything compares against.
    /// A single word in capitals across the middle of the screen reads
    /// as a label rather than as shouting once it is spaced out.
    /// </summary>
    static string Spaced(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        var b = new System.Text.StringBuilder(word.Length * 2);
        foreach (char c in word) { b.Append(c); b.Append(' '); }
        return b.ToString(0, b.Length - 1);
    }

    void Set(string what, bool on)
    {
        bool had = _notifications.Contains(what);
        if (on && !had) _notifications.Add(what);
        else if (!on && had) _notifications.Remove(what);
    }
}
