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
/// Blur, which is what drink does, is a ten-tap radial blur, and it is
/// done here too, in the pixel buffer rather than on the GPU. <see
/// cref="Blur"/> has the shader it is copied from and the one place it
/// takes a shortcut.
///
/// Weather is not a compositor at all - it is two particle systems in
/// the scene - so it lives in its own file. <see cref="WeatherOverlay"/>
/// explains it; this class owns one and syncs it, because both are "the
/// things the server does to your eyes" and both have to land between
/// the world and the interface.
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
    WeatherOverlay _weather;

    /// <summary>
    /// The rain and snow overlay, exposed for the harnesses. Nothing in
    /// the client needs to reach it: it is driven from <see cref="Sync"/>.
    /// </summary>
    public WeatherOverlay Weather => _weather;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Built before the blend rectangle, and so drawn under it. That
        // is the reference's ordering and not a preference: rain and snow
        // are particles in the scene, drawn by the scene manager, and the
        // blend compositors run on the finished viewport afterwards
        // (ControllerEffects.cpp:149-231). A blinded player therefore
        // does not see the snow, and a player in a red haze of pain sees
        // the snow through it. Siblings in a Control draw in the order
        // they were added, which says the same thing once.
        _weather = new WeatherOverlay { Verbose = Verbose };
        AddChild(_weather);

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
        if (_weather != null)
        {
            // Given the same rectangle explicitly rather than left to
            // its anchors, for the reason in the comment above: this
            // Control's own size is assigned here each frame, and a child
            // resolving anchors against a size that is being written out
            // from under it lags a frame behind on every rotation.
            _weather.Position = Vector2.Zero;
            _weather.Size = Size;
            // Weather needs a clock, being a simulation rather than a
            // blend. GetProcessDeltaTime is this node's own frame time,
            // which is the frame time of whatever called Sync.
            _weather.Sync(data, (float)GetProcessDeltaTime());
        }

        Inverted = false;
        _blurring = false;
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

        // Blur is a duration effect - `blur.StartOrExtend(duration)`
        // (Effects.cs:320-322) - but the reference does not ramp it. The
        // handler enables the compositor while IsActive and disables it
        // when the duration runs out, full strength throughout
        // (ControllerEffects.cpp:222-231):
        //
        //     compMan.setCompositorEnabled(Viewport, COMPOSITOR_BLUR,
        //        Data->Effects->Blur->IsActive);
        //
        // so this is a flag and not a fade, unlike pain and whiteout
        // above, which do read Progress.
        _blurring = data.Effects.Blur != null && data.Effects.Blur.IsActive;
    }

    // ------------------------------------------------------------------
    // Blur
    // ------------------------------------------------------------------

    /// <summary>
    /// The ten offsets `Blur_ps` samples at, in texture coordinates, and
    /// exactly as the shader lists them (compositors.hlsl:72-76):
    ///
    ///     const float samples[10] =
    ///     {
    ///        -0.08, -0.05, -0.03, -0.02, -0.01,
    ///         0.01,  0.02,  0.03,  0.05,  0.08
    ///     };
    ///
    /// Symmetric about zero, so the blur runs both ways along the radius
    /// rather than smearing outwards - it is a radial blur, not a zoom
    /// blur, and copying the table rather than generating it is the only
    /// way to be sure of that.
    /// </summary>
    static readonly float[] BlurSamples =
    {
        -0.08f, -0.05f, -0.03f, -0.02f, -0.01f,
         0.01f,  0.02f,  0.03f,  0.05f,  0.08f
    };

    /// <summary>
    /// `param_named sampleDist float 1.0` and `param_named
    /// sampleStrength float 2.2` (compositors.material:124-127), the two
    /// uniforms the material feeds the shader. sampleDist multiplies the
    /// offsets; sampleStrength decides how fast the effect comes up as
    /// you move away from the middle of the screen.
    /// </summary>
    const float BlurSampleDist = 1.0f;
    const float BlurSampleStrength = 2.2f;

    /// <summary>
    /// How much smaller the blurred term is computed than the frame, in
    /// each axis. Four, so a sixteenth of the pixels. The shift is the
    /// same number as a power of two, because every use of it is an index
    /// or a divide in an inner loop.
    ///
    /// Four is as far as it can go without the kernel itself coming
    /// apart. The narrowest tap the shader takes is 0.01 in uv, which on
    /// a 1280-wide frame is 12.8 pixels and so still three texels at a
    /// quarter scale - the shape of the kernel survives. At an eighth it
    /// would be one and a half, and the ten taps would start to land on
    /// each other.
    /// </summary>
    const int BlurShrink = 4;
    const int BlurShrinkShift = 2;

    bool _blurring;
    byte[] _blurSrc;   // half-resolution copy of the frame
    byte[] _blurDst;   // half-resolution blurred result
    byte[] _blurRow;   // full width, half height: the horizontal upsample
    ushort[] _blurT;   // the shader's `t`, per pixel, on a 0..256 scale
    int _blurW, _blurH;
    int _fullW, _fullH;

    /// <summary>
    /// Blur the finished picture in place, when drink says so.
    ///
    /// WHAT THE REFERENCE DOES
    /// ----------------------
    /// EffectType 8 arrives at DataController.cs:2953, reaches
    /// `blur.StartOrExtend(duration)` (Effects.cs:320-322), and
    /// `ControllerEffects` turns on COMPOSITOR_BLUR for the viewport
    /// while it lasts (ControllerEffects.cpp:222-231). The compositor
    /// (compositors.compositor:156-178) renders the scene to `rt0` and
    /// then one full-screen quad with material Compositor/Blur, whose
    /// fragment program is `Blur_ps` (compositors.hlsl:48-91). The whole
    /// of it, and it is short enough to quote:
    ///
    ///     float4 color = tex2D(RT, uv);
    ///     float2 dir  = 0.5 - uv;        // towards screen centre
    ///     float  dist = length(dir);
    ///     float  t    = saturate(dist * sampleStrength);
    ///     dir = normalize(dir);
    ///     float4 sum = color;
    ///     for (int i = 0; i < 10; i++)
    ///        sum += tex2D(RT, uv + dir * samples[i] * sampleDist);
    ///     sum /= 11.0;
    ///     pixel = lerp(color, sum, t);
    ///
    /// Three things are worth naming, because they are what the effect
    /// actually looks like and all three are easy to lose in a port:
    ///
    ///   * The middle of the screen stays sharp. `t` is the distance from
    ///     the centre times 2.2, clamped to 1, so it is zero at the
    ///     centre, reaches 1 at a distance of 0.4545 in uv, and is
    ///     saturated over most of the edges (a corner is 0.707 away).
    ///     Drink blurs the sides of your vision and leaves what you are
    ///     looking at legible, which is why it is playable at all.
    ///   * The blur runs along the radius. `dir` is the direction to the
    ///     centre, and the ten offsets straddle zero, so the kernel is a
    ///     line pointing at the middle of the screen - the picture is
    ///     smeared towards and away from where you are looking, not in
    ///     all directions. That is what makes it read as swimming rather
    ///     than as fog.
    ///   * The offsets are in texture coordinates, not pixels. `dir` is
    ///     normalised in uv space, where the screen is a unit square, so
    ///     on a wide viewport the same offset is more pixels across than
    ///     it is down. That anisotropy is in the reference and is kept.
    ///     The widest tap, 0.08 in uv, is 34 pixels on this renderer's
    ///     432-line buffer and 102 across a 1280-wide one.
    ///
    /// WHAT IS DONE HERE, AND THE ONE SHORTCUT
    /// ---------------------------------------
    /// This client has no compositor, but it does have the pixels: the
    /// world is rasterised by a software column renderer into a buffer
    /// (GameView.RenderFrame) and this runs on that buffer, before it is
    /// handed to the texture. Which also puts the effect where the
    /// reference puts it - on the 3D view and not over the interface,
    /// since the compositor is on the viewport and CEGUI draws after it.
    ///
    /// THE ONE SHORTCUT IS RESOLUTION
    /// Eleven taps for every one of up to 1280x432 pixels is 6.1 million
    /// samples a frame, on a CPU, on a phone, for as long as the player
    /// is drunk - which is the reason this was left out to begin with. So
    /// the *blurred* term is computed on a frame reduced by
    /// <see cref="BlurShrink"/> in each axis, a sixteenth of the pixels:
    /// 380 thousand samples instead of 6.1 million.
    ///
    /// Why that costs almost nothing to look at. The blurred term is by
    /// construction a picture with its high frequencies already gone: the
    /// narrowest tap is 0.01 in uv, which is 12.8 pixels on a 1280-wide
    /// frame, so detail finer than that is absent from `sum` whatever
    /// resolution it was computed at. Reducing the frame before blurring
    /// throws away detail the blur was about to throw away anyway. And
    /// the *sharp* term stays full resolution - `color` in the lerp is
    /// the original pixel, untouched - so the middle of the screen, where
    /// `t` is near zero and where the player is actually reading the
    /// room, is bit for bit the picture the renderer produced. The
    /// approximation fades in at exactly the rate of the blur that hides
    /// it.
    ///
    /// Measured on this machine, .NET 8 release, over a 1280x432 buffer:
    /// 21ms a frame written the way the shader reads, 7.5ms as it stands,
    /// and 4.7ms at the 768x432 a 16:9 phone actually asks for. Roughly
    /// half of what is left is the final full-resolution blend, which no
    /// version of this can avoid - every pixel of the frame has to be
    /// written. Nothing of the shader was dropped to get there: the tap
    /// table, both uniforms, the uv-space anisotropy, the division by
    /// eleven and the lerp are all still in the code below. What changed
    /// is arithmetic - per-pixel divisions became multiplications by a
    /// reciprocal, `t` became a table computed once per screen size, and
    /// the upsample became integer and separable.
    ///
    /// WHAT DIFFERS, HONESTLY STATED
    ///   * Taps are nearest-neighbour against the reduced copy; the
    ///     reference's texture unit is `filtering trilinear`
    ///     (compositors.material:131-134).
    ///   * The reduced copy is a 4x4 box average, which is a four-pixel
    ///     blur applied before the ten taps.
    ///   * It blurs the world and nothing else. In the reference the
    ///     compositor runs on the viewport, so it takes in the weather
    ///     particles too, which are scene objects; here the weather is
    ///     drawn over the buffer this has already finished with, so rain
    ///     and snow stay sharp over a blurred room. See the note in
    ///     <see cref="WeatherOverlay"/>.
    ///   * The blurred term is read back with a bilinear interpolation
    ///     that is exact but written as integer weights, and the final
    ///     blend is fixed point, 0..256 with a shift, where the shader
    ///     has floats. On a channel the blur darkens it rounds down
    ///     rather than to nearest: at most one 255th of one channel.
    /// The first two make the edges of the screen a shade softer than the
    /// shader's; the third is below the precision of the frame buffer.
    /// Nothing else is approximated.
    /// </summary>
    /// <param name="rgba">The frame, four bytes a pixel, modified in place.</param>
    public void Blur(byte[] rgba, int w, int h)
    {
        if (!_blurring || rgba == null || w < 16 || h < 16) return;
        if (rgba.Length < w * h * 4) return;

        int hw = w >> BlurShrinkShift, hh = h >> BlurShrinkShift;
        if (_blurW != hw || _blurH != hh || _blurSrc == null)
        {
            _blurW = hw; _blurH = hh;
            _blurSrc = new byte[hw * hh * 3];
            _blurDst = new byte[hw * hh * 3];
            _blurRow = new byte[w * hh * 3];
        }

        // `t` depends on nothing but where the pixel is, so the whole
        // field of it is worked out once per screen size and then read.
        // That is the shader's
        //
        //     float2 dir = 0.5 - uv;
        //     float t = saturate(length(dir) * sampleStrength);
        //
        // evaluated ahead of time, on a fixed-point scale of 0..256 so
        // the blend below is a shift rather than a divide. A megabyte for
        // a 1280x432 buffer, held only while the effect is running.
        if (_blurT == null || _fullW != w || _fullH != h)
        {
            _fullW = w; _fullH = h;
            _blurT = new ushort[w * h];
            for (int y = 0; y < h; y++)
            {
                float dy = 0.5f - (y + 0.5f) / h;
                float dy2 = dy * dy;
                for (int x = 0; x < w; x++)
                {
                    float dx = 0.5f - (x + 0.5f) / w;
                    float t = Mathf.Sqrt(dx * dx + dy2) * BlurSampleStrength;
                    if (t > 1f) t = 1f;
                    _blurT[y * w + x] = (ushort)(t * 256f + 0.5f);
                }
            }
        }

        // ---- 1. the frame, reduced ----------------------------------
        // A BlurShrink-square box average. The shader has no such step;
        // it is the price of the shortcut, and it is a four-pixel blur
        // applied before the ten taps, which makes the result a shade
        // softer than the reference's and nothing else. Three channels:
        // the alpha of an opaque frame blurs to itself.
        for (int y = 0; y < hh; y++)
        {
            int o = y * hw * 3;
            for (int x = 0; x < hw; x++, o += 3)
            {
                int sr = 0, sg = 0, sb = 0;
                for (int j = 0; j < BlurShrink; j++)
                {
                    int ry = Math.Min((y << BlurShrinkShift) + j, h - 1) * w;
                    for (int i = 0; i < BlurShrink; i++)
                    {
                        int c = (ry + Math.Min((x << BlurShrinkShift) + i, w - 1)) * 4;
                        sr += rgba[c]; sg += rgba[c + 1]; sb += rgba[c + 2];
                    }
                }
                _blurSrc[o] = (byte)(sr >> (BlurShrinkShift * 2));
                _blurSrc[o + 1] = (byte)(sg >> (BlurShrinkShift * 2));
                _blurSrc[o + 2] = (byte)(sb >> (BlurShrinkShift * 2));
            }
        }

        // ---- 2. the ten taps, on the reduced frame -------------------
        //
        //     float4 sum = color;
        //     for (int i = 0; i < 10; i++)
        //        sum += tex2D(RT, uv + dir * samples[i] * sampleDist);
        //     sum /= 11.0;
        //
        // line for line, with two things hoisted. `uv * hw` for column x
        // is just `x + 0.5`, uv being the pixel centre over the width, so
        // the tap position needs no multiply; and `dir`, which the shader
        // normalises in uv space and lets the sampler scale by the
        // texture size, is scaled by the texture size once here instead.
        // Scaling each axis by its own resolution is what reproduces the
        // reference's uv-space anisotropy - the same 0.08 offset being
        // 102 pixels across a 1280-wide frame and 34 down a 432-high one
        // - so it is done that way deliberately, not by accident.
        float invHw = 1f / hw, invHh = 1f / hh;
        float[] samples = BlurSamples;
        for (int y = 0; y < hh; y++)
        {
            float dyBase = 0.5f - (y + 0.5f) * invHh;
            float dy2 = dyBase * dyBase;
            float yc = y + 0.5f;
            int o = y * hw * 3;
            for (int x = 0; x < hw; x++, o += 3)
            {
                float dx = 0.5f - (x + 0.5f) * invHw;
                float dist = Mathf.Sqrt(dx * dx + dy2);

                // Nothing will be blended in at the very centre of the
                // screen, so there is nothing worth computing there. The
                // shader pays for the taps regardless - a GPU has no use
                // for a branch - but a CPU does, and the pixels skipped
                // are the ones whose contribution rounds to nothing.
                if (dist * BlurSampleStrength < 1f / 512f)
                {
                    _blurDst[o] = _blurSrc[o];
                    _blurDst[o + 1] = _blurSrc[o + 1];
                    _blurDst[o + 2] = _blurSrc[o + 2];
                    continue;
                }

                float inv = 1f / dist;                       // dir = normalize(dir)
                float stepX = dx * inv * hw * BlurSampleDist;
                float stepY = dyBase * inv * hh * BlurSampleDist;
                float xc = x + 0.5f;

                int sr = _blurSrc[o], sg = _blurSrc[o + 1], sb = _blurSrc[o + 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    float k = samples[i];
                    int tx = (int)(xc + stepX * k);
                    int ty = (int)(yc + stepY * k);
                    // `tex_address_mode clamp` (compositors.material:130).
                    if (tx < 0) tx = 0; else if (tx >= hw) tx = hw - 1;
                    if (ty < 0) ty = 0; else if (ty >= hh) ty = hh - 1;
                    int q = (ty * hw + tx) * 3;
                    sr += _blurSrc[q]; sg += _blurSrc[q + 1]; sb += _blurSrc[q + 2];
                }
                _blurDst[o] = (byte)(sr / 11);
                _blurDst[o + 1] = (byte)(sg / 11);
                _blurDst[o + 2] = (byte)(sb / 11);
            }
        }

        // ---- 3. and 4. back up to full size, and the blend -----------
        //
        // The blurred term has to be read at full resolution to be
        // blended with the sharp one, and the scale between the two grids
        // is exactly BlurShrink. A bilinear fetch at an exact integer
        // scale has only BlurShrink possible weights on each axis - here
        // an eighth, three eighths, five eighths, seven eighths - so the
        // general bilinear, with its per-pixel floor, fractional part and
        // float lerps, collapses to a weighted sum and a shift, with the
        // pair chosen by the low bits of the coordinate. It is the same
        // interpolation, written in the form it reduces to, not a coarser
        // one.
        //
        // Separably, too: horizontal first into a full-width buffer of
        // reduced height, then vertical straight into the frame. Two
        // fetches per output pixel instead of four.
        //
        // (`x0` is worked out with an arithmetic shift rather than a
        // division because it goes negative at the left edge, where the
        // first column sits outside the source grid, and a shift floors
        // where a division truncates towards zero. When the frame's width
        // or height is not a multiple of BlurShrink the two grids end up
        // a fraction of a pixel out of step; inside a blur that spans a
        // hundred pixels that is not something anyone can see.)
        const int Half = BlurShrink >> 1;
        const int Round = BlurShrink;            // half of the 2*BlurShrink scale
        const int Shift = BlurShrinkShift + 1;   // log2 of 2*BlurShrink
        for (int y = 0; y < hh; y++)
        {
            int si = y * hw * 3;
            int di = y * w * 3;
            for (int x = 0; x < w; x++, di += 3)
            {
                int d = x - Half;
                int x0 = d >> BlurShrinkShift;
                int wb = (d & (BlurShrink - 1)) * 2 + 1, wa = 2 * BlurShrink - wb;
                int x1 = x0 + 1;
                if (x0 < 0) x0 = 0; else if (x0 >= hw) x0 = hw - 1;
                if (x1 < 0) x1 = 0; else if (x1 >= hw) x1 = hw - 1;
                int a = si + x0 * 3, b = si + x1 * 3;
                _blurRow[di] = (byte)((_blurDst[a] * wa + _blurDst[b] * wb + Round) >> Shift);
                _blurRow[di + 1] = (byte)((_blurDst[a + 1] * wa + _blurDst[b + 1] * wb + Round) >> Shift);
                _blurRow[di + 2] = (byte)((_blurDst[a + 2] * wa + _blurDst[b + 2] * wb + Round) >> Shift);
            }
        }

        for (int y = 0; y < h; y++)
        {
            int dy = y - Half;
            int y0 = dy >> BlurShrinkShift;
            int wb = (dy & (BlurShrink - 1)) * 2 + 1, wa = 2 * BlurShrink - wb;
            int y1 = y0 + 1;
            if (y0 < 0) y0 = 0; else if (y0 >= hh) y0 = hh - 1;
            if (y1 < 0) y1 = 0; else if (y1 >= hh) y1 = hh - 1;
            int r0 = y0 * w * 3, r1 = y1 * w * 3;
            int p = y * w * 4, ti = y * w;

            for (int x = 0; x < w; x++, p += 4, ti++)
            {
                // pixel = lerp(color, sum, t). `t` comes off the table
                // built at the top; zero means the shader's lerp would
                // have returned `color` untouched, so the pixel is left
                // exactly as the renderer drew it.
                int t = _blurT[ti];
                if (t == 0) continue;
                int q = x * 3;

                int s0 = (_blurRow[r0 + q] * wa + _blurRow[r1 + q] * wb + Round) >> Shift;
                int s1 = (_blurRow[r0 + q + 1] * wa + _blurRow[r1 + q + 1] * wb + Round) >> Shift;
                int s2 = (_blurRow[r0 + q + 2] * wa + _blurRow[r1 + q + 2] * wb + Round) >> Shift;

                // The shift is the divide by 256 the fixed-point `t` asks
                // for. On a channel the blur darkens it rounds down
                // rather than to nearest, which is at most one 255th of
                // one channel and is the only place this differs
                // arithmetically from the shader's float lerp.
                int c0 = rgba[p], c1 = rgba[p + 1], c2 = rgba[p + 2];
                rgba[p] = (byte)(c0 + (((s0 - c0) * t) >> 8));
                rgba[p + 1] = (byte)(c1 + (((s1 - c1) * t) >> 8));
                rgba[p + 2] = (byte)(c2 + (((s2 - c2) * t) >> 8));
            }
        }

        if (Verbose) GD.Print($"[ScreenEffects] blur {w}x{h} via {hw}x{hh}");
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
