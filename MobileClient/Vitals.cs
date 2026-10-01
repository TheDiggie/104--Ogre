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
///
/// WHAT IS NOT THE GAME'S is everything around the fill, and that half
/// was instrumentation: four slabs with the condition's name in a
/// coloured box jammed against its numbers, all four sitting straight
/// on the world. The numbers are the most-read thing in the client and
/// they were the hardest part of it to read.
///
/// So the four are a GROUP now, on one plate in the panels' own warm
/// near-black with the panels' lit inner edge - the plate is what gives
/// the block its own contrast, which a HUD needs and a panel does not:
/// this sits over a bright stone floor as often as a dark ceiling, and
/// a bar that reads against one has to read against the other. Inside
/// it every row is the same shape: the condition's name in a fixed
/// gutter on the left, so the four bars start on one line; the bar; and
/// the numbers right-aligned at the bar's end, so the eye goes to one
/// column for all four rather than hunting along four different ones.
/// The name is small and dim because you read it once; the numbers are
/// bright because you read them every second.
///
/// A low bar gets a MARK as well as the game's blink - a pulsing warm
/// ring around it and its numbers in the same warm colour - because the
/// blink alone is a brightness change on a colour you are not looking
/// at, and three of the four bars pulse for ordinary reasons. The ring
/// is outside the fill, so nothing the game decides is touched.
/// </summary>
public partial class Vitals : Control
{
    [Export] public float BarWidth = 170f;
    [Export] public float BarHeight = 16f;
    [Export] public int FontSize = 13;

    /// <summary>
    /// The gutter the names sit in, left of every bar. Fixed, because
    /// the point of it is that all four bars begin at the same x - a
    /// name set beside its bar puts each bar wherever that word ended.
    /// </summary>
    const float NameW = 76f;
    /// <summary>Inside the plate, on every edge.</summary>
    const float Pad = 8f;
    /// <summary>Between the name gutter and the bar.</summary>
    const float NameGap = 6f;
    /// <summary>Between two bars. Wider than the old 4: four bars with
    /// a hairline of gap between them read as one striped block.</summary>
    const float RowGap = 9f;

    DataController _data;

    /// <summary>
    /// The empty part of a bar. Opaque, because the fills are the
    /// game's own and two of them are dark - toughness is 0x444444 and
    /// the shadow bar under it was 0x141418 at three-quarter alpha, so
    /// against a bright ceiling the difference between full and empty
    /// was a guess. The reference does not have this problem: its bars
    /// sit on a solid CEGUI panel. Changing the fills instead would
    /// mean inventing colours the game already chose.
    /// </summary>
    static readonly Color Back  = new Color(0.055f, 0.051f, 0.045f, 1f);
    /// <summary>The trough's rim. Warm, like the panels' rules.</summary>
    static readonly Color Edge  = M59Skin.Rule;

    /// <summary>
    /// The mark on a bar that is low: a ring around it and its numbers
    /// in the same colour. Not a bar colour - it never touches the
    /// fill, which is the game's.
    /// </summary>
    static readonly Color Warn = new Color(1f, 0.80f, 0.45f);

    // UI_COLOURRECT_BAR_*, as the client defines them.
    static readonly Color Red    = new Color(0x80 / 255f, 0f, 0f);
    static readonly Color Blue   = new Color(0f, 0f, 0x80 / 255f);
    static readonly Color Yellow = new Color(0x70 / 255f, 0x70 / 255f, 0f);
    static readonly Color Grey   = new Color(0x44 / 255f, 0x44 / 255f, 0x44 / 255f);

    /// <summary>Below this share the bar blinks, as the game's does.</summary>
    public const float LowWater = 0.333f;

    Label[] _names = Array.Empty<Label>();
    Label[] _values = Array.Empty<Label>();
    double _clock;

    /// <summary>
    /// The plate the four bars sit on. Built once: the same outer dark
    /// edge and lit inner edge <see cref="M59Skin.Window"/> gives a
    /// panel, which is what makes a flat rectangle read as a thing in
    /// front of the world rather than a hole in it. Slightly
    /// translucent, because the HUD is the margin and the world is the
    /// game - but only slightly, or the bright floor comes through it.
    /// </summary>
    static StyleBoxFlat _plate;
    static StyleBoxFlat Plate()
    {
        if (_plate != null) return _plate;
        var s = new StyleBoxFlat
        {
            BgColor = new Color(M59Skin.Card.R, M59Skin.Card.G, M59Skin.Card.B, 0.88f),
            AntiAliasing = true,
        };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)M59Skin.Radius;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 1;
        s.BorderColor = M59Skin.EdgeLit;
        s.ShadowColor = new Color(0, 0, 0, 0.5f);
        s.ShadowSize = 8;
        return _plate = s;
    }

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

        // Every field _Draw actually reads has to be in here, or the bar
        // keeps the picture it drew before. ValueRenderMin is the fill's
        // floor - the library raises PropertyChanged for it in its own
        // right (`Meridian59/Data/Models/StatNumeric.cs:203-215`) and the
        // server does move it, so a vital whose floor shifts while its
        // current and maximum stay put changes fill without changing this
        // signature. ResourceName is the same story one level up: it is
        // resolved later than the numbers arrive, in Stat's ResolveStrings
        // (`Meridian59/Data/Models/Stat.cs:176-187`, set at
        // `Stat.cs:259-264`), so a bar that arrived nameless and was named
        // afterwards stayed nameless on screen.
        var sb = new System.Text.StringBuilder();
        foreach (StatNumeric s in data.AvatarCondition)
            sb.Append(s.Num).Append(':').Append(s.ValueCurrent).Append('/')
              .Append(s.ValueMaximum).Append('/').Append(s.ValueRenderMax).Append('/')
              .Append(s.ValueRenderMin).Append('/').Append(s.ResourceName).Append(';');

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

        float pitch = BarHeight + RowGap;
        float barX = Left + Pad + NameW + NameGap;
        float plateW = Pad + NameW + NameGap + BarWidth + Pad;
        float plateH = Pad * 2f + pitch * count - RowGap;
        DrawStyleBox(Plate(), new Rect2(Left, Top, plateW, plateH));

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

            float pulse = 0.5f + 0.5f * MathF.Sin((float)_clock * 6f);
            Color c = Colour(s.Num);
            if (low)
            {
                // The blink: the game runs a highlight animation on the
                // bar. A brightness pulse is the same idea with the tools
                // to hand.
                c = c.Lerp(new Color(1f, 1f, 1f), 0.35f * pulse);
            }

            float row = Top + Pad + pitch * i;
            var bar = new Rect2(barX, row, BarWidth, BarHeight);
            DrawRect(bar, Back);
            DrawRect(new Rect2(barX + 1f, row + 1f, (BarWidth - 2f) * fill, BarHeight - 2f), c);
            DrawRect(bar, Edge, false, 1f);

            // The low mark, outside the bar: a ring that pulses with the
            // fill. A player glancing at the corner of the eye sees the
            // shape change, not only the brightness - which is the part
            // the game's own blink cannot say on a phone, where the bar
            // is a third the size it is on a monitor.
            if (low)
                DrawRect(bar.Grow(2f), new Color(Warn.R, Warn.G, Warn.B, 0.35f + 0.5f * pulse), false, 2f);

            // The name as well as the numbers. `UIAvatar.cpp` puts the
            // condition's own icon beside each bar - Resource.Frames[0]
            // - and that icon is the only label a bar gets there. Here
            // three stats are colour-coded and everything else is grey,
            // so a condition the server invents is a grey bar with two
            // numbers on it and no way to tell what it measures. The
            // library resolves ResourceName for exactly this, and the
            // character sheet already uses it.
            //
            // Name and numbers are two labels, not one string: the name
            // belongs in the gutter and subordinate, the numbers at the
            // bar's end and bright, and one label cannot be in two
            // places or two colours.
            string name = s.ResourceName;
            Label label = _names[i];
            label.Text = string.IsNullOrWhiteSpace(name) ? "" : name;
            label.Position = new Vector2(Left + Pad, row);
            label.Size = new Vector2(NameW, BarHeight);
            label.Visible = true;

            Label value = _values[i];
            value.Text = $"{s.ValueCurrent} / {max}";
            // Inset from the bar's right rim so the digits are not
            // against the edge, and sized to the whole bar so the right
            // alignment lands in the same column on all four rows.
            value.Position = new Vector2(barX + 4f, row);
            value.Size = new Vector2(BarWidth - 8f, BarHeight);
            value.AddThemeColorOverride("font_color", low ? Warn : M59Skin.Text);
            value.Visible = true;
            i++;
        }

        for (; i < _names.Length; i++) { _names[i].Visible = false; _values[i].Visible = false; }
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
        if (_names.Length >= count) return;

        var names = new Label[count];
        var values = new Label[count];
        Array.Copy(_names, names, _names.Length);
        Array.Copy(_values, values, _values.Length);
        for (int i = _names.Length; i < count; i++)
        {
            names[i] = Text(FontSize, M59Skin.GoldDim, HorizontalAlignment.Left);
            values[i] = Text(FontSize + 1, M59Skin.Text, HorizontalAlignment.Right);
        }
        _names = names;
        _values = values;
    }

    /// <summary>
    /// One of the two labels a row carries.
    ///
    /// ClipText, and that is not decoration: a Label's minimum size is
    /// the size of its text, and Godot clamps a Control's Size UP to
    /// its minimum - so a condition with a long name would quietly
    /// widen its own label past the gutter and over the bar. Clipped,
    /// the label is exactly the box it is given and a long name is
    /// elided. See M59Skin.Empty for the same trap, met the other way
    /// round.
    ///
    /// The outline stays. These sit on the plate, which is nearly
    /// opaque, but the plate's own edge is thin and a digit that
    /// touches it needs the separation.
    /// </summary>
    Label Text(int size, Color colour, HorizontalAlignment align)
    {
        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        l.AddThemeConstantOverride("outline_size", 3);
        AddChild(l);
        return l;
    }
}
