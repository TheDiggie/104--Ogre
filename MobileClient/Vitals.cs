using System;
using Godot;
using Meridian59.Common.Constants;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// Health, mana and vigor, which is most of what you need to see while
/// playing and none of which was on screen.
///
/// Reads the client's own AvatarCondition stats rather than keeping a
/// copy: the library maintains those from the server, including the
/// maxima, and a second copy would only go stale.
///
/// Draws nothing at all until the server has sent something, so it stays
/// out of the way while the view is still connecting.
/// </summary>
public partial class Vitals : Control
{
    [Export] public float BarWidth = 180f;
    [Export] public float BarHeight = 14f;
    [Export] public int FontSize = 12;

    DataController _data;
    Label _text;

    static readonly Color Back   = new Color(0.05f, 0.05f, 0.07f, 0.75f);
    static readonly Color Health = new Color(0.78f, 0.22f, 0.22f);
    static readonly Color Mana   = new Color(0.30f, 0.45f, 0.90f);
    static readonly Color Vigor  = new Color(0.85f, 0.72f, 0.25f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _text = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _text.AddThemeFontSizeOverride("font_size", FontSize);
        _text.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _text.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _text.AddThemeConstantOverride("outline_size", 3);
        AddChild(_text);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>Where the bars sit: above whatever owns the bottom.</summary>
    public float BottomReserve { get; set; }

    void Layout()
    {
        if (_text == null) return;
        Vector2 v = GetViewportRect().Size;
        _text.Position = new Vector2(12f, v.Y - BottomReserve - 12f - BarHeight * 3f - 18f - FontSize * 1.4f);
    }

    int _lastHp = -1, _lastMp = -1, _lastVp = -1;

    /// <summary>
    /// Points at the client's data and redraws only when a number has
    /// actually moved - this is called every frame, and three rectangles
    /// that have not changed are three rectangles not worth redrawing.
    /// </summary>
    public void Follow(DataController data)
    {
        _data = data;
        if (data == null) return;
        int hp = data.HitPoints, mp = data.ManaPoints, vp = data.VigorPoints;
        if (hp == _lastHp && mp == _lastMp && vp == _lastVp) return;
        _lastHp = hp; _lastMp = mp; _lastVp = vp;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_data == null) return;

        int hp = Max(StatNums.HITPOINTS, out int hpMax);
        int mp = Max(StatNums.MANA, out int mpMax);
        int vp = Max(StatNums.VIGOR, out int vpMax);

        // Nothing from the server yet - do not draw three empty boxes.
        if (hpMax <= 0 && mpMax <= 0 && vpMax <= 0) { _text.Text = ""; return; }

        Vector2 v = GetViewportRect().Size;
        float x = 12f;
        float y = v.Y - BottomReserve - 12f - BarHeight * 3f - 18f;

        Bar(x, y,                    hp, hpMax, Health);
        Bar(x, y + BarHeight + 4f,   mp, mpMax, Mana);
        Bar(x, y + (BarHeight + 4f) * 2f, vp, vpMax, Vigor);

        _text.Text = $"{hp}/{hpMax}   {mp}/{mpMax}   {vp}/{vpMax}";
    }

    void Bar(float x, float y, int cur, int max, Color fill)
    {
        DrawRect(new Rect2(x, y, BarWidth, BarHeight), Back);
        if (max <= 0) return;
        float f = Mathf.Clamp(cur / (float)max, 0f, 1f);
        DrawRect(new Rect2(x + 1f, y + 1f, (BarWidth - 2f) * f, BarHeight - 2f), fill);
    }

    int Max(byte which, out int maximum)
    {
        maximum = 0;
        StatNumeric s = _data?.AvatarCondition?.GetItemByNum(which);
        if (s == null) return 0;
        maximum = s.ValueMaximum;
        return s.ValueCurrent;
    }
}
