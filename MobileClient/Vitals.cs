using System;
using Godot;
using Meridian59.Common.Constants;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// The condition bars, drawn the way the game draws them.
///
/// The rules are the Ogre client's `UIAvatar::ConditionChange`, not a
/// guess at what a health bar should be:
///
///  - one bar per entry in the avatar's own condition list, in the
///    server's order. Not three hardcoded stats: the server decides what
///    you have, and vanilla sends a fourth - the chance of getting tougher
///    - which the game shows as a grey bar and this used to throw away.
///  - the fill is not current over maximum. It is
///    <c>(current - renderMin) / (max - renderMin)</c>, and <c>max</c> is
///    the render maximum for vigor and tougher-chance but the plain
///    maximum for everything else. A stat whose render floor is not zero
///    reads wrong otherwise.
///  - the colours are the client's own: hit points 0x800000, mana
///    0x000080, vigor 0x707000, tougher-chance 0x444444. They are darker
///    than a health bar usually is because the bar imagery lightens them.
///  - the text on the bar is "current / max".
///  - below a third it blinks, and keeps blinking while it stays there.
///    The game runs a one-shot highlight on every change and switches that
///    animation to looping under 33%.
/// </summary>
public partial class Vitals : Control
{
    [Export] public float BarWidth = 180f;
    [Export] public float BarHeight = 16f;
    [Export] public int FontSize = 12;

    DataController _data;

    static readonly Color Back  = new Color(0.05f, 0.05f, 0.07f, 0.75f);
    static readonly Color Edge  = new Color(0.55f, 0.55f, 0.60f, 0.55f);

    // UI_COLOURRECT_BAR_*, as the client defines them.
    static readonly Color Red    = new Color(0x80 / 255f, 0f, 0f);
    static readonly Color Blue   = new Color(0f, 0f, 0x80 / 255f);
    static readonly Color Yellow = new Color(0x70 / 255f, 0x70 / 255f, 0f);
    static readonly Color Grey   = new Color(0x44 / 255f, 0x44 / 255f, 0x44 / 255f);

    /// <summary>Below this share the bar blinks, as the game's does.</summary>
    public const float LowWater = 0.333f;

    Label[] _labels = Array.Empty<Label>();
    double _clock;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    /// <summary>
    /// Where the block sits, in the top-left corner. The game keeps its
    /// avatar panel there too, with the portrait on the left and the bars
    /// beside it - the head is at 13,14 and the condition bars start at
    /// x=95 of a 250-wide panel.
    /// </summary>
    public float Left { get; set; } = 94f;
    public float Top { get; set; } = 14f;

    public override void _Process(double delta)
    {
        // Only for the blink, and only while something is low.
        if (!_lowSomething) return;
        _clock += delta;
        QueueRedraw();
    }

    bool _lowSomething;
    string _signature = "";

    /// <summary>
    /// Points at the client's data and redraws when a number has actually
    /// moved. Called every frame; bars that have not changed are bars not
    /// worth redrawing.
    /// </summary>
    public void Follow(DataController data)
    {
        _data = data;
        if (data?.AvatarCondition == null) return;

        var sb = new System.Text.StringBuilder();
        foreach (StatNumeric s in data.AvatarCondition)
            sb.Append(s.Num).Append(':').Append(s.ValueCurrent).Append('/')
              .Append(s.ValueMaximum).Append('/').Append(s.ValueRenderMax).Append(';');

        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_data?.AvatarCondition == null) return;

        int count = _data.AvatarCondition.Count;
        if (count == 0) return;

        EnsureLabels(count);

        float gap = 4f;
        float x = Left;
        float y = Top;

        _lowSomething = false;
        int i = 0;

        foreach (StatNumeric s in _data.AvatarCondition)
        {
            // Vigor and tougher-chance are drawn against their render
            // maximum; everything else against its plain one.
            int max = (s.Num == StatNums.VIGOR || s.Num == StatNums.TOUGHERCHANCE)
                ? s.ValueRenderMax : s.ValueMaximum;

            int range = Math.Max(1, max - s.ValueRenderMin);
            float fill = Mathf.Clamp((s.ValueCurrent - s.ValueRenderMin) / (float)range, 0f, 1f);

            bool low = fill < LowWater && s.Num != StatNums.TOUGHERCHANCE;
            if (low) _lowSomething = true;

            Color c = Colour(s.Num);
            if (low)
            {
                // The blink: the game runs a highlight animation on the
                // bar. A brightness pulse is the same idea with the tools
                // to hand.
                float pulse = 0.5f + 0.5f * MathF.Sin((float)_clock * 6f);
                c = c.Lerp(new Color(1f, 1f, 1f), 0.35f * pulse);
            }

            float row = y + (BarHeight + gap) * i;
            DrawRect(new Rect2(x, row, BarWidth, BarHeight), Back);
            DrawRect(new Rect2(x + 1f, row + 1f, (BarWidth - 2f) * fill, BarHeight - 2f), c);
            DrawRect(new Rect2(x, row, BarWidth, BarHeight), Edge, false, 1f);

            // The name as well as the numbers. `UIAvatar.cpp` puts the
            // condition's own icon beside each bar - Resource.Frames[0]
            // - and that icon is the only label a bar gets there. Here
            // three stats are colour-coded and everything else is grey,
            // so a condition the server invents is a grey bar with two
            // numbers on it and no way to tell what it measures. The
            // library resolves ResourceName for exactly this, and the
            // character sheet already uses it.
            Label label = _labels[i];
            string name = s.ResourceName;
            label.Text = string.IsNullOrWhiteSpace(name)
                ? $"{s.ValueCurrent} / {max}"
                : $"{name}  {s.ValueCurrent} / {max}";
            label.Position = new Vector2(x + 6f, row - 1f);
            label.Visible = true;
            i++;
        }

        for (; i < _labels.Length; i++) _labels[i].Visible = false;
    }

    /// <summary>The client's own bar colours, by stat.</summary>
    static Color Colour(byte num)
    {
        switch (num)
        {
            case StatNums.HITPOINTS: return Red;
            case StatNums.MANA: return Blue;
            case StatNums.VIGOR: return Yellow;
            default: return Grey;          // tougher-chance, and anything new
        }
    }

    void EnsureLabels(int count)
    {
        if (_labels.Length >= count) return;

        var grown = new Label[count];
        Array.Copy(_labels, grown, _labels.Length);
        for (int i = _labels.Length; i < count; i++)
        {
            var l = new Label { MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", FontSize);
            l.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            l.AddThemeConstantOverride("outline_size", 3);
            AddChild(l);
            grown[i] = l;
        }
        _labels = grown;
    }
}
