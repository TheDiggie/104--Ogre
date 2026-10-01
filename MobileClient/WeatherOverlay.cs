using System;
using Godot;

/// <summary>
/// Rain and snow.
///
/// The server sends effect message 70 with an EffectType of 9 Raining,
/// 10 Snowing or 11 ClearWeather; `DataController` hands it to the
/// effect list (DataController.cs:2953-2956) and `Effects.HandleEffect`
/// sets or clears two flags (Effects.cs:324-334):
///
///     case EffectType.Raining:      raining.IsActive = true;
///     case EffectType.Snowing:      snowing.IsActive = true;
///     case EffectType.ClearWeather: snowing.IsActive = false;
///                                   raining.IsActive = false;
///
/// so weather is not a duration effect - it is two booleans that stay
/// on until the server clears them. The reference watches both
/// (ControllerRoom.cpp:93-96) and, in the handlers at
/// ControllerRoom.cpp:1202-1257 (snow) and :1259-1314 (rain), starts or
/// fade-stops a ParticleUniverse system.
///
/// WHERE THE REFERENCE PUTS IT, AND WHY THIS IS A 2D OVERLAY
/// ---------------------------------------------------------
/// The weather systems hang off `weatherNode`, which is created as a
/// child of the *camera* node, not of the room:
///
///     weatherNode = OgreClient::Singleton->CameraNodeOrbit
///                      ->createChildSceneNode(NAME_WEATHERNODE);
///     weatherNode->setPosition(Ogre::Vector3(0, 0, 0));
///                                        (ControllerRoom.cpp:65-67)
///
/// So in the reference the emitter box already travels with the eye and
/// is always centred on it: the weather is a box of falling particles
/// bolted to the camera, and the only thing the room contributes is the
/// height at which a particle is destroyed. That is what makes a
/// screen-space overlay an honest port rather than a cheat - the thing
/// being mirrored is camera-attached to begin with.
///
/// It has to be a screen-space overlay here, because this client's world
/// is drawn by a software column renderer into a pixel buffer
/// (GameView.RenderFrame) with no scene graph, no billboards and no
/// depth buffer to sort transparent sprites against. There is nowhere to
/// put a 3D particle. So the particles are simulated in 2D over the
/// finished picture, and the numbers below are taken from the
/// ParticleUniverse scripts so that what the player sees - density, fall
/// speed, lean, colour, blend mode, drift - is the same weather.
///
/// THE DIVERGENCE, STATED PLAINLY
/// ------------------------------
///   * No perspective, so no true depth. A per-drop depth factor stands
///     in: each particle gets a random "nearness" that scales its size
///     and its speed together, which is what perspective does to a
///     particle somewhere in the reference's 256-deep (rain) or
///     3072-deep (snow) emitter box. Drops do not pass behind walls or
///     pillars, because there is nothing for them to be behind.
///   * No room bounding box. The reference re-aims the OnPosition
///     observer at the room's own ceiling each time weather starts
///     (ControllerRoom.cpp:1224-1244 and :1282-1302):
///
///         BoundingBox3D^ bBox = Room->GetBoundingBox3D(true);
///         Ogre::Vector3 max = Util::ToOgreYZFlipped(bBox->Max) * SCALE;
///         observer->setPositionYThreshold(max.y + 5.0f);
///
///     which is what keeps a drop from being culled before it has come
///     down through the room. In screen space the equivalent statement
///     is "a particle lives until it leaves the bottom of the view", and
///     that is what happens here.
///   * Weather is not itself blurred by <see cref="ScreenEffects.Blur"/>.
///     In the reference the particles are scene objects, so a drunk
///     player sees the rain through the same COMPOSITOR_BLUR as the
///     room. Here the blur runs on the renderer's pixel buffer and the
///     weather is drawn over it afterwards, so the drops stay sharp.
///     Putting them behind the blur would mean rasterising every
///     particle into the buffer by hand, which is a renderer, and the
///     rules say not to touch that one.
///   * The particle textures are not loaded. `weather_rain.png` is two
///     solid white bars running the length of a 32x1050 strip with a
///     short fade-in at the leading end, drawn with `scene_blend add`
///     (weather.material:26-42), which at a billboard width of 0.12
///     world units comes to one bright additive hairline; that is drawn
///     directly. `weather_snow.png` is a single soft near-white blob,
///     RGB 252,252,254, peak alpha 166/255, falling off over the outer
///     third, drawn with `scene_blend alpha_blend`
///     (weather.material:1-24); a 16x16 copy of that falloff is built at
///     runtime rather than read off disk, so the overlay needs no
///     resource path.
///
/// Everything else is mirrored, including the thing that is easiest to
/// get wrong: ClearWeather does not stop the weather dead. The reference
/// calls `stopFade` and says why -
///
///     // fade stop it so it can be restarted without all particles
///     // deleted
///     particleSysRain->stopFade();     (ControllerRoom.cpp:1308-1310)
///
/// - so the sky stops producing drops and the ones already falling
/// finish their fall. <see cref="Clear"/> does exactly that. The hard
/// stop exists too, and is used in one place only: leaving a room
/// (`UnloadRoom`, ControllerRoom.cpp:500-514, plain `stop()` and
/// detach), where every particle vanishes at once. <see cref="Reset"/>
/// is that, and the room id is watched so it happens at the same moment.
/// </summary>
public partial class WeatherOverlay : Control
{
    // ---------------------------------------------------------------
    // Rain, from Resources/particles/weather_rain.pu
    // ---------------------------------------------------------------
    //
    //     emission_rate 500        time_to_live 1
    //     velocity      75         direction    0.2 -1 0
    //     angle         1          position     0 32 0
    //     box 256 wide x 1 high x 256 deep
    //     default_particle_width 0.12  default_particle_height 6
    //
    // The absolute numbers are world units and mean nothing on a screen,
    // so one calibration turns them into screen units and every other
    // number follows from it: the emitter's height above the eye, 32
    // units, is taken to be the height of the view. That is the only
    // free choice here. It is a defensible one - the emitter is at eye
    // level plus a bit, so the band the drops fall through is about a
    // screenful - and it fixes everything else by ratio:
    //
    //   fall speed  75 / 32 = 2.34 screen heights per second, i.e. a
    //               drop crosses the view in about 0.43s. Driving rain.
    //   streak      6 / 32 = 0.19 of the view tall at full nearness.
    //   lean        0.2 / 1 - direction is (0.2, -1, 0), so the drop
    //               drifts a fifth of a unit sideways for every unit it
    //               falls. Mirrored exactly; no calibration needed,
    //               being a ratio already.
    //   spread      angle 1 degree. Essentially none: rain falls
    //               straight. Mirrored, and it is why rain gets a
    //               near-zero jitter and snow gets a wide cone.
    const float RainFallPerSecond = 75f / 32f;
    const float RainStreakHeight = 6f / 32f;
    const float RainLean = 0.2f;
    const float RainSpreadDegrees = 1f;

    // Density. `emission_rate 500` with `time_to_live 1` settles at
    // about 500 live drops - and the reference immediately overrides the
    // rate to a tenth of the configured quota
    // (ControllerRoom.cpp:230-240), which at the shipped
    // `visual_particle_quota 5000` is the same 500. Those 500 fill a
    // 256x256 box centred on the eye, of which a forward-facing frustum
    // holds something under a quarter, so the number actually in front
    // of the player is nearer 120 - and a good part of those are far
    // enough back to be a faint short dash rather than a streak.
    //
    // Expressed per million pixels so it reads the same on a phone and
    // on a tablet: 120 drops over a 1920x1080 view is 58 per Mpx.
    const float RainDropsPerMegapixel = 58f;

    // ---------------------------------------------------------------
    // Snow, from Resources/particles/weather_snow.pu
    // ---------------------------------------------------------------
    //
    //     emission_rate 500        time_to_live 10
    //     velocity      40..60     direction    0 -1 0
    //     angle         5..40      position     0 192 0
    //     box 3072 wide x 64 high x 3072 deep
    //     all_particle_dimensions 3..4
    //     affector LinearForce WindLeft  -0.6 0 0  (starts disabled)
    //     affector LinearForce WindRight  0.6 0 0  (starts disabled)
    //     two OnRandom observers, observe_interval 1, that enable one
    //     and disable the other; the second has random_threshold 0.6
    //
    // Same calibration, applied to snow's own emitter height of 192
    // units. That it comes out different from rain's is the point, not a
    // mistake: snow is released six times higher up, so in screen terms
    // it is six times further away, which is why it drifts down slowly
    // and in big soft flakes rather than streaking past.
    //
    //   fall speed  40..60 / 192 = 0.21..0.31 screen heights a second,
    //               so a flake takes three to five seconds to cross.
    //   size        3..4 / 192 = 1.6%..2.1% of the view at full
    //               nearness. On a 1080-high view, 17 to 23 pixels.
    //   spread      5..40 degrees off straight down, per flake. This,
    //               not the wind, is where most of snow's wander comes
    //               from, and it is mirrored per particle.
    const float SnowFallPerSecondMin = 40f / 192f;
    const float SnowFallPerSecondMax = 60f / 192f;
    const float SnowSizeMin = 3f / 192f;
    const float SnowSizeMax = 4f / 192f;
    const float SnowSpreadMinDegrees = 5f;
    const float SnowSpreadMaxDegrees = 40f;

    // The wind. A ParticleUniverse LinearForce with the default
    // `force_application add` adds force*time to the velocity each step,
    // so `force_vector -0.6 0 0` is a sideways acceleration of 0.6 world
    // units per second squared, and the same calibration divides it by
    // 192. Over a four-second fall that builds to about 2.4 units a
    // second of sideways drift against 50 of downward - a lean of a few
    // degrees that slowly reverses. Subtle in the reference and subtle
    // here; it is mirrored because it is the difference between snow
    // that falls and snow that is blown about.
    //
    // Which way it blows is decided by the pair of OnRandom observers.
    // `observe_interval 1` gives each flake a roll a second; the second
    // observer carries `random_threshold 0.6`, so it fires the less
    // often of the two. Mirrored as a per-flake coin flip once a second
    // weighted 0.6 to the left.
    const float SnowWindAccel = 0.6f / 192f;
    const float SnowWindInterval = 1f;
    const float SnowWindLeftChance = 0.6f;

    // Density. `emission_rate 500` at `time_to_live 10` settles near
    // 5000 live flakes - and here the reference's quota override matters
    // in the other direction, because 5000 is exactly the shipped quota,
    // so the system runs right at its ceiling. Those 5000 are spread
    // through a box 3072 on a side, twelve times wider than rain's, so
    // per unit of volume snow is roughly fourteen times sparser. But the
    // visible volume is much bigger too - the flakes are released 192
    // units up instead of 32 and fall for ten seconds - and the far ones
    // are small and numerous rather than absent. The count below is
    // chosen so the overlay shows about as many flakes at once as a
    // forward frustum through that box does: on the order of a hundred,
    // most of them small.
    //
    // 110 flakes over a 1920x1080 view is 53 per Mpx.
    const float SnowFlakesPerMegapixel = 53f;

    // Nearness. Stands in for depth, as described in the class comment.
    // The range is deliberately wide for snow, whose emitter box is
    // twelve times deeper than rain's, and narrower for rain, whose
    // drops live one second and so never get far from the eye.
    const float RainNearMin = 0.45f;
    const float SnowNearMin = 0.30f;

    /// <summary>Prints what it is doing, for the harnesses.</summary>
    [Export] public bool Verbose = false;

    /// <summary>
    /// Off by default and turned on by <see cref="Sync"/>. The reference
    /// gates both handlers on a config flag -
    /// `!OgreClient::Singleton->Config->DisableWeatherEffects`
    /// (ControllerRoom.cpp:1208 and :1266) - so a player who has turned
    /// weather off gets the effect message and ignores it. This client
    /// has no such setting yet; the switch is here so that when one
    /// appears there is one place to wire it, and so the harness can
    /// drive the overlay without a data layer.
    /// </summary>
    [Export] public bool Enabled = true;

    /// <summary>
    /// A particle. One struct for both kinds: rain and snow differ in
    /// their numbers and in how they are drawn, not in what has to be
    /// remembered about them.
    /// </summary>
    struct Drop
    {
        public float X, Y;        // position, in fractions of the view
        public float VX, VY;      // velocity, view fractions per second
        public float Near;        // depth stand-in, 0..1
        public float Size;        // snow: diameter as a view fraction
        public float WindTimer;   // snow: seconds until the next roll
        public float WindSign;    // snow: which way the wind blows
    }

    Drop[] _rain = Array.Empty<Drop>();
    Drop[] _snow = Array.Empty<Drop>();
    int _rainLive, _snowLive;

    // Whether the sky is still producing. Separate from the particle
    // count, because that is precisely what `stopFade` means: emission
    // stops, the living carry on.
    bool _rainEmitting, _snowEmitting;

    // Fractional spawn budget carried between frames. Without it, a
    // frame at 60fps that is owed 2.4 new drops would round to 2 every
    // time and the rain would come out 17% thin.
    float _rainOwed, _snowOwed;

    readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    Texture2D _flake;
    uint _room;
    bool _hadRoom;

    /// <summary>True while anything is on screen or being emitted.</summary>
    public bool Active => _rainEmitting || _snowEmitting || _rainLive > 0 || _snowLive > 0;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _rng.Randomize();

        // Rain is `scene_blend add` (weather.material:26-42): a drop
        // brightens what is behind it and never darkens it, which is why
        // rain reads as silver against a dark room and vanishes against
        // a bright sky. Snow is `scene_blend alpha_blend`
        // (weather.material:1-24), the Godot default, so it is drawn in
        // a second pass with its own material rather than fighting over
        // one.
        //
        // Both share this node's transform, so both get the full-screen
        // rectangle the class sets up, and neither can be laid out into
        // the interface's inset area by accident.
        _rainCanvas = new WeatherLayer(DrawRain)
        {
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        _rainCanvas.SetAnchorsPreset(LayoutPreset.FullRect);
        _rainCanvas.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_rainCanvas);

        _snowCanvas = new WeatherLayer(DrawSnow);
        _snowCanvas.SetAnchorsPreset(LayoutPreset.FullRect);
        _snowCanvas.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_snowCanvas);

        _flake = BuildFlake();
    }

    WeatherLayer _rainCanvas, _snowCanvas;

    /// <summary>
    /// A child whose only job is to own a blend mode and call back into
    /// the overlay to draw. Two of these is how one node draws an
    /// additive pass and an alpha pass, which is what the two materials
    /// in weather.material ask for.
    /// </summary>
    partial class WeatherLayer : Control
    {
        readonly Action<CanvasItem> _draw;
        public WeatherLayer(Action<CanvasItem> draw) { _draw = draw; }
        public override void _Draw() { _draw(this); }
    }

    /// <summary>
    /// The snow sprite. `weather_snow.png` is a 512x512 near-white blob
    /// - RGB 252,252,254, alpha peaking at 166 in the middle and falling
    /// to nothing over the outer third - so a flake is a soft dot, not a
    /// disc. Rebuilt here at 16x16 with the same falloff rather than
    /// loaded, because a 512x512 texture to draw a 20-pixel flake is a
    /// quarter of a megabyte of phone memory for nothing, and because it
    /// keeps the overlay free of any dependency on where the game's
    /// resources happen to live.
    ///
    /// The colour and the peak alpha are the texture's; the shape is
    /// smoothstep over the outer third, which is what the measured
    /// falloff looks like.
    /// </summary>
    static Texture2D BuildFlake()
    {
        const int n = 16;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f;
                float dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Flat to two thirds out, then smoothly to zero. 166/255
                // is the texture's own peak.
                float a = d >= 1f ? 0f : (d <= 0.66f ? 1f : 1f - Mathf.SmoothStep(0.66f, 1f, d));
                img.SetPixel(x, y, new Color(252f / 255f, 252f / 255f, 254f / 255f, a * (166f / 255f)));
            }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Read the two flags off the data layer once a frame and step the
    /// simulation. Mirrors the pair of property-changed handlers the
    /// reference registers at ControllerRoom.cpp:93-96; being polled
    /// rather than event-driven changes nothing, because both handlers
    /// only ever look at the current value of `IsActive`.
    /// </summary>
    public void Sync(Meridian59.Data.DataController data, float delta)
    {
        Vector2 view = GetViewportRect().Size;

        // Leaving a room is the one place the reference kills the
        // weather outright instead of fading it: `UnloadRoom` calls
        // plain `stop()` on both systems and detaches them
        // (ControllerRoom.cpp:500-514). Nothing here is told about room
        // changes, so the room id is watched for one. Without this, a
        // teleport out of a blizzard would carry a screenful of flakes
        // into the dungeon you arrived in.
        uint room = data?.RoomInformation?.RoomID ?? 0;
        if (_hadRoom && room != _room) Reset();
        _room = room;
        _hadRoom = true;

        bool wantRain = Enabled && data?.Effects?.Raining != null && data.Effects.Raining.IsActive;
        bool wantSnow = Enabled && data?.Effects?.Snowing != null && data.Effects.Snowing.IsActive;

        if (wantRain != _rainEmitting || wantSnow != _snowEmitting)
        {
            if (Verbose)
                GD.Print($"[Weather] rain {_rainEmitting}->{wantRain} snow {_snowEmitting}->{wantSnow}");
            _rainEmitting = wantRain;
            _snowEmitting = wantSnow;
            // Nothing is thrown away when a flag goes false. That is
            // `stopFade`, and the comment in the reference explains why
            // it is worth the bookkeeping: "fade stop it so it can be
            // restarted without all particles deleted"
            // (ControllerRoom.cpp:1250-1251, :1308-1309).
        }

        Step(view, delta);

        // Two child canvases, so both need telling. A frame with nothing
        // on it still gets one redraw, to clear the last one.
        _rainCanvas?.QueueRedraw();
        _snowCanvas?.QueueRedraw();
    }

    /// <summary>
    /// The hard stop: every particle gone at once, emission off. This is
    /// `particleSysRain->stop()` plus detach, which the reference does
    /// only when unloading a room (ControllerRoom.cpp:500-514).
    /// </summary>
    public void Reset()
    {
        _rainLive = _snowLive = 0;
        _rainEmitting = _snowEmitting = false;
        _rainOwed = _snowOwed = 0f;
    }

    /// <summary>
    /// The soft stop: `stopFade`. Emission stops and the particles
    /// already falling run out their lives. Kept separate from <see
    /// cref="Reset"/> because the reference keeps them separate, and
    /// used for ClearWeather, which is the common case.
    /// </summary>
    public void Clear()
    {
        _rainEmitting = _snowEmitting = false;
    }

    /// <summary>
    /// Advance every particle by <paramref name="delta"/> and spawn what
    /// the emission rate owes. Positions are held in fractions of the
    /// view rather than pixels so that a rotation or a resize does not
    /// teleport the weather, and so the density constants above mean the
    /// same thing on any screen.
    /// </summary>
    void Step(Vector2 view, float delta)
    {
        if (view.X < 1f || view.Y < 1f) return;
        // A long stall - a room load, the app coming back from the
        // background - must not be integrated in one step, or every
        // particle jumps off the bottom of the screen at once and the
        // weather visibly restarts. A sixth of a second is the most any
        // one step is allowed to be worth.
        if (delta > 1f / 6f) delta = 1f / 6f;
        if (delta <= 0f) return;

        float megapixels = view.X * view.Y / 1_000_000f;
        int rainCap = Mathf.Max(1, Mathf.RoundToInt(RainDropsPerMegapixel * megapixels));
        int snowCap = Mathf.Max(1, Mathf.RoundToInt(SnowFlakesPerMegapixel * megapixels));
        if (_rain.Length < rainCap) Array.Resize(ref _rain, rainCap);
        if (_snow.Length < snowCap) Array.Resize(ref _snow, snowCap);
        if (_rainLive > rainCap) _rainLive = rainCap;
        if (_snowLive > snowCap) _snowLive = snowCap;

        // ---- rain -------------------------------------------------
        // The cap is the steady-state population, and a drop crosses the
        // view in RainStreakHeight-independent time, so the rate that
        // holds the population there is cap / crossing-time. Working it
        // out rather than writing it down keeps the two numbers from
        // drifting apart if either is retuned.
        if (_rainEmitting)
        {
            float life = 1f / RainFallPerSecond;          // seconds to cross
            _rainOwed += rainCap / life * delta;
            while (_rainOwed >= 1f && _rainLive < rainCap)
            {
                _rainOwed -= 1f;
                _rain[_rainLive++] = SpawnRain(true);
            }
            if (_rainLive >= rainCap) _rainOwed = 0f;
        }

        for (int i = 0; i < _rainLive; i++)
        {
            ref Drop d = ref _rain[i];
            d.X += d.VX * delta;
            d.Y += d.VY * delta;

            // "A particle lives until it leaves the view" - the screen
            // -space reading of the OnPosition observer the reference
            // aims at the room's ceiling (ControllerRoom.cpp:1282-1302).
            // The streak is drawn upwards from its head, so it is gone
            // once its head is a streak-length past the bottom.
            if (d.Y - RainStreakHeight * d.Near > 1f || d.X < -0.3f || d.X > 1.3f)
            {
                // Recycled rather than freed while it is still raining:
                // the reference's emitter keeps the population up, and
                // reusing the slot avoids shuffling the array.
                if (_rainEmitting) d = SpawnRain(false);
                else _rain[i--] = _rain[--_rainLive];
            }
        }

        // ---- snow -------------------------------------------------
        if (_snowEmitting)
        {
            float life = 1f / ((SnowFallPerSecondMin + SnowFallPerSecondMax) * 0.5f);
            _snowOwed += snowCap / life * delta;
            while (_snowOwed >= 1f && _snowLive < snowCap)
            {
                _snowOwed -= 1f;
                _snow[_snowLive++] = SpawnSnow(true);
            }
            if (_snowLive >= snowCap) _snowOwed = 0f;
        }

        for (int i = 0; i < _snowLive; i++)
        {
            ref Drop d = ref _snow[i];

            // The wind, as described at SnowWindAccel: a roll a second
            // per flake, weighted 0.6 to the left, then a steady
            // sideways acceleration until the next roll. The two
            // LinearForce affectors are mutually exclusive in the script
            // - each handler enables one and disables the other
            // (weather_snow.pu, the two OnRandom observers) - so one
            // sign, not a sum.
            d.WindTimer -= delta;
            if (d.WindTimer <= 0f)
            {
                d.WindTimer += SnowWindInterval;
                d.WindSign = _rng.Randf() < SnowWindLeftChance ? -1f : 1f;
            }
            d.VX += d.WindSign * SnowWindAccel * d.Near * delta;

            d.X += d.VX * delta;
            d.Y += d.VY * delta;

            if (d.Y - d.Size > 1f || d.X < -0.3f || d.X > 1.3f)
            {
                if (_snowEmitting) d = SpawnSnow(false);
                else _snow[i--] = _snow[--_snowLive];
            }
        }
    }

    /// <summary>
    /// A new raindrop. <paramref name="seeded"/> starts it at a random
    /// height instead of above the view, which is the screen-space form
    /// of the script's `fast_forward 1 1` (weather_rain.pu:3): the
    /// reference runs a second of simulation before the first frame, so
    /// rain begins as rain rather than as an empty sky filling from the
    /// top. Every drop spawned to replace a dead one starts above the
    /// view, where the emitter is.
    /// </summary>
    Drop SpawnRain(bool seeded)
    {
        // Nearness scales size and speed together, which is the one
        // thing perspective does that matters here.
        float near = _rng.RandfRange(RainNearMin, 1f);
        float fall = RainFallPerSecond * near;
        // `angle 1` - one degree of spread. Kept because it is in the
        // script, not because anyone will see it.
        float spread = Mathf.DegToRad(_rng.RandfRange(-RainSpreadDegrees, RainSpreadDegrees));
        // The lean is a ratio of horizontal to vertical travel, so it
        // scales with the fall speed and needs no calibration; the view
        // is wider than it is tall, so a fraction-of-view horizontal
        // speed has to be divided by the aspect to come out as the same
        // physical angle.
        Vector2 view = GetViewportRect().Size;
        float aspect = view.Y > 0f ? view.X / view.Y : 1f;
        float drift = (RainLean + Mathf.Tan(spread)) * fall / Mathf.Max(0.001f, aspect);

        return new Drop
        {
            // Spawned across a band wider than the view, because a drop
            // that leans sideways as it falls has to be able to enter
            // from off the edge - otherwise the upwind side of the
            // screen is permanently dry.
            X = _rng.RandfRange(-0.3f, 1.3f),
            Y = seeded ? _rng.RandfRange(-RainStreakHeight, 1f) : -RainStreakHeight * near - _rng.Randf() * 0.1f,
            VX = drift,
            VY = fall,
            Near = near,
        };
    }

    /// <summary>
    /// A new snowflake. Snow has no `fast_forward` in its script, but it
    /// does have `time_to_live 10` against rain's 1, so an unseeded
    /// start would leave the player looking at an empty sky for the
    /// several seconds it takes the first flakes to come down. Seeded on
    /// the first fill for the same reason rain is.
    /// </summary>
    Drop SpawnSnow(bool seeded)
    {
        float near = _rng.RandfRange(SnowNearMin, 1f);
        float fall = _rng.RandfRange(SnowFallPerSecondMin, SnowFallPerSecondMax) * near;
        // `angle dyn_random { min 5 max 40 }` - a wide cone, and where
        // most of snow's wander comes from. The magnitude is drawn from
        // the script's range and the side is a coin flip.
        float spread = Mathf.DegToRad(_rng.RandfRange(SnowSpreadMinDegrees, SnowSpreadMaxDegrees))
                       * (_rng.Randf() < 0.5f ? -1f : 1f);
        Vector2 view = GetViewportRect().Size;
        float aspect = view.Y > 0f ? view.X / view.Y : 1f;
        float size = _rng.RandfRange(SnowSizeMin, SnowSizeMax) * near;

        return new Drop
        {
            X = _rng.RandfRange(-0.3f, 1.3f),
            Y = seeded ? _rng.RandfRange(-size, 1f) : -size - _rng.Randf() * 0.2f,
            VX = Mathf.Tan(spread) * fall / Mathf.Max(0.001f, aspect),
            VY = fall,
            Near = near,
            Size = size,
            // A fresh roll, but not all on the same second: the
            // reference's observers are per particle and the particles
            // were not born together.
            WindTimer = _rng.Randf() * SnowWindInterval,
            WindSign = _rng.Randf() < SnowWindLeftChance ? -1f : 1f,
        };
    }

    /// <summary>
    /// Rain, additively. `weather_rain.png` is two solid white bars
    /// running the whole 1050-pixel length of the strip with a fade-in
    /// over the top tenth, in a billboard 0.12 world units wide - which
    /// is well under a pixel - so a drop on screen is one bright
    /// hairline. The billboard is `oriented_self` with
    /// `billboard_origin bottom_center` (weather_rain.pu:14-18), meaning
    /// it is turned to lie along its own velocity and hangs behind the
    /// point it has reached; the line below is drawn from the drop's
    /// position back along its velocity, which is the same thing.
    ///
    /// The fade-in at the leading end is drawn as two segments rather
    /// than a gradient: a full-brightness tail and a dimmer head. Two
    /// lines is cheaper than a gradient mesh and, at a streak a pixel
    /// wide, indistinguishable.
    /// </summary>
    void DrawRain(CanvasItem into)
    {
        if (_rainLive <= 0) return;
        Vector2 view = Size;
        if (view.X < 1f || view.Y < 1f) return;

        for (int i = 0; i < _rainLive; i++)
        {
            ref Drop d = ref _rain[i];
            var head = new Vector2(d.X * view.X, d.Y * view.Y);
            // Back along the velocity, scaled so the streak is the
            // script's 6 units long whatever the drop's lean.
            float len = RainStreakHeight * d.Near * view.Y;
            var dir = new Vector2(d.VX * view.X, d.VY * view.Y).Normalized();
            Vector2 tail = head - dir * len;

            // Additive white. The texture is pure 255 white and the
            // material adds, so brightness is all there is; nearness
            // stands in for the dimming that distance and a sub-pixel
            // billboard do, and keeps the far rain from reading as a
            // wall of white lines.
            float bright = 0.35f + 0.45f * d.Near;
            var lit = new Color(bright, bright, bright, 1f);
            // Width: 0.12 units against a 6-unit length is a 1:50 strip,
            // so a pixel. Kept at a pixel rather than scaled by
            // nearness, because a line thinner than a pixel on this
            // renderer's 432-line buffer is a line that flickers.
            into.DrawLine(tail + dir * len * 0.12f, head, lit, 1f, false);
            // The head, where the texture fades in.
            into.DrawLine(tail, tail + dir * len * 0.12f, lit * 0.4f, 1f, false);
        }
    }

    /// <summary>
    /// Snow, alpha-blended. One soft dot per flake, at the size the
    /// script asks for, using the falloff measured off
    /// `weather_snow.png`. `billboard_origin top_center`
    /// (weather_snow.pu:9-12) puts the sprite below the particle's
    /// position; it is centred here instead, because at a 20-pixel flake
    /// the half-flake difference is not a thing anyone can see and
    /// centring keeps the cull test above symmetrical.
    /// </summary>
    void DrawSnow(CanvasItem into)
    {
        if (_snowLive <= 0 || _flake == null) return;
        Vector2 view = Size;
        if (view.X < 1f || view.Y < 1f) return;

        for (int i = 0; i < _snowLive; i++)
        {
            ref Drop d = ref _snow[i];
            // A flake is round, so its size is measured against the
            // view's height in both axes - a fraction of the width would
            // come out as an ellipse on a phone in landscape.
            float px = Mathf.Max(2f, d.Size * view.Y);
            var at = new Vector2(d.X * view.X - px * 0.5f, d.Y * view.Y - px * 0.5f);
            into.DrawTextureRect(_flake, new Rect2(at, new Vector2(px, px)), false);
        }
    }
}
