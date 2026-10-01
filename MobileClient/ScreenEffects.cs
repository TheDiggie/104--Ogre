using System;
using Godot;
using Meridian59.Data;
using Meridian59.Drawing2D;

/// <summary>
/// The things the server does to your eyes.
///
/// `ControllerEffects` hangs a full-screen Ogre compositor off each one
/// (ControllerEffects.cpp:149-310). Four of them - blind, pain,
/// whiteout and the two colour translations - are the same shader with
/// a different colour: `Blend_ps` (compositors.hlsl:19-36), which is a
/// plain alpha blend of one colour over the finished picture, and the
/// comment there says as much: "used by blind, whiteout, pain and
/// others". So they come across as one coloured rectangle laid over
/// the view, and the only work is getting each colour right.
///
///   blind     black, fully opaque (ControllerEffects.cpp:156-160)
///   pain      red, 0.3 fading to 0 across the effect
///             (OgreListeners.cpp:140)
///   whiteout  white, 1 fading to 0 (OgreListeners.cpp:168)
///   xlat      the colour the translation stands for, below
///
/// Invert, which is what a bonk on the head does
/// (compositors.hlsl:39-45), is not a blend. It is one minus the
/// picture, and this renderer owns its own pixels, so it is done there
/// rather than with a shader - which also puts it where the reference
/// puts it, on the 3D view and not over the buttons. <see
/// cref="Inverted"/> is what says so.
///
/// Blur, which is what drink does, is a ten-tap radial blur, and a
/// software renderer at 432 lines has better uses for ten samples a
/// pixel. It is left out, and said so here rather than pretended away.
///
/// Blind is the one that matters for fairness: a blinded player in a
/// client that ignores the effect keeps full sight of the room, which
/// is a divergence in the player's favour and reads as a cheat.
/// </summary>
public partial class ScreenEffects : Control
{
    /// <summary>Prints what it is applying, for the harnesses.</summary>
    [Export] public bool Verbose = false;

    /// <summary>
    /// Whether the picture should be shown inverted. Read by the view,
    /// which inverts its own pixel buffer - see the note above.
    /// </summary>
    public bool Inverted { get; private set; }

    ColorRect _blend;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _blend = new ColorRect { Color = new Color(0, 0, 0, 0), Visible = false };
        _blend.SetAnchorsPreset(LayoutPreset.FullRect);
        _blend.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_blend);

    }

    /// <summary>
    /// Reads the effects off the data layer once a frame. They are
    /// checked in the order the reference enables its compositors, and
    /// the strongest wins, because one rectangle cannot hold two
    /// colours and blind over anything is still blind.
    /// </summary>
    public void Sync(DataController data)
    {
        // A Control hung off a CanvasLayer is not laid out by anything,
        // so its anchors resolve to nothing and it reports a size of
        // zero - which is exactly as much of the screen as it then
        // covers. The size is taken from the viewport each frame
        // instead; it also has to follow a rotation or a resize.
        Vector2 view = GetViewportRect().Size;
        // The UI layer is scaled and shifted to keep the interface off
        // the curve of the phone's glass (see SafeArea), and a rectangle
        // measured in the layer's own coordinates would be inset by the
        // same amount - leaving a lit frame round a blinded screen. The
        // effect belongs to the whole screen, so the layer's transform
        // is undone here.
        // The layer this sits on is its own and is not inset for the
        // glass, but the check is kept: if it is ever moved onto the
        // interface's layer the effect must still cover the screen
        // rather than the interface's inset rectangle.
        Vector2 scale = Vector2.One, offset = Vector2.Zero;
        if (GetParent() is CanvasLayer layer) { scale = layer.Scale; offset = layer.Offset; }
        if (scale.X <= 0f) scale.X = 1f;
        if (scale.Y <= 0f) scale.Y = 1f;

        Position = new Vector2(-offset.X / scale.X, -offset.Y / scale.Y);
        Size = new Vector2(view.X / scale.X, view.Y / scale.Y);
        _blend.Position = Vector2.Zero;
        _blend.Size = Size;

        Inverted = false;
        if (data?.Effects == null) { _blend.Visible = false; return; }

        float r = 0f, g = 0f, b = 0f, a = 0f;

        if (data.Effects.XLatOverride != null && data.Effects.XLatOverride.IsActive)
            Xlat(data.Effects.XLatOverride.XLat, ref r, ref g, ref b, ref a);

        if (data.Effects.FlashXLat != null && data.Effects.FlashXLat.IsActive)
            Xlat(data.Effects.FlashXLat.XLat, ref r, ref g, ref b, ref a);

        if (data.Effects.Pain != null && data.Effects.Pain.IsActive)
        {
            float pa = 0.3f - 0.3f * (float)data.Effects.Pain.Progress;
            if (pa > a) { r = 1f; g = 0f; b = 0f; a = pa; }
        }

        if (data.Effects.Whiteout != null && data.Effects.Whiteout.IsActive)
        {
            float wa = 1f - (float)data.Effects.Whiteout.Progress;
            if (wa > a) { r = 1f; g = 1f; b = 1f; a = wa; }
        }

        if (data.Effects.Blind != null && data.Effects.Blind.IsActive)
        { r = 0f; g = 0f; b = 0f; a = 1f; }

        if (Verbose && a > 0.001f)
            GD.Print($"[ScreenEffects] blend {r:0.0},{g:0.0},{b:0.0} a {a:0.00} size {Size}");
        _blend.Color = new Color(r, g, b, a);
        _blend.Visible = a > 0.001f;

        Inverted = data.Effects.Invert != null && data.Effects.Invert.IsActive;
    }

    /// <summary>
    /// The colour a palette translation blends over the screen, from
    /// `GetBlendColorForXlat` (ControllerEffects.cpp:109-147). The
    /// translations are named for what they do - BLEND70RED is red at
    /// 0.7 - so rather than copying thirty case labels, the two runs of
    /// ten are worked out from their own ordering and the eight odd ones
    /// are listed.
    /// </summary>
    static void Xlat(uint xlat, ref float r, ref float g, ref float b, ref float a)
    {
        float nr = 0f, ng = 0f, nb = 0f, na = 0f;

        if (xlat >= ColorTransformation.BLEND10RED && xlat <= ColorTransformation.BLEND100RED)
        { nr = 1f; na = 0.1f * (xlat - ColorTransformation.BLEND10RED + 1); }
        else if (xlat >= ColorTransformation.BLEND10WHITE && xlat <= ColorTransformation.BLEND100WHITE)
        { nr = ng = nb = 1f; na = 0.1f * (xlat - ColorTransformation.BLEND10WHITE + 1); }
        else if (xlat == ColorTransformation.BLEND25RED)   { nr = 1f; na = 0.25f; }
        else if (xlat == ColorTransformation.BLEND25BLUE)  { nb = 1f; na = 0.25f; }
        else if (xlat == ColorTransformation.BLEND25GREEN) { ng = 1f; na = 0.25f; }
        else if (xlat == ColorTransformation.BLEND50BLUE)  { nb = 1f; na = 0.5f; }
        else if (xlat == ColorTransformation.BLEND50GREEN) { ng = 1f; na = 0.5f; }
        else if (xlat == ColorTransformation.BLEND75RED)   { nr = 1f; na = 0.75f; }
        else if (xlat == ColorTransformation.BLEND75BLUE)  { nb = 1f; na = 0.75f; }
        else if (xlat == ColorTransformation.BLEND75GREEN) { ng = 1f; na = 0.75f; }
        else return;

        if (na > a) { r = nr; g = ng; b = nb; a = na; }
    }
}
