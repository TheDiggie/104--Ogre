using System;
using Godot;

/// <summary>
/// The fixed controls: a d-pad bottom-left and a look stick
/// bottom-right, each always on the glass while it is chosen, both
/// pieces of the HUD.
///
/// WHY FIXED CONTROLS. TouchControls is "touch anywhere": the stick
/// appears wherever the left thumb lands and the glass turns by how
/// far a finger is dragged. That is the better scheme for most hands
/// and it stays the default - but the player's words were that some
/// people do not like touch-anywhere controls, and every mobile game
/// the HUD editor copies offers a fixed pad as the alternative. A fixed
/// control is something to LOOK at: it is where it was last time, it
/// shows which way it is pushed, and a thumb that has left it knows
/// where to go back to.
///
/// TWO SWITCHES, NOT ONE. The pad (M59Hud.MovePad) and the stick
/// (M59Hud.LookStick) are chosen separately, because the second round
/// of player's words split them: "some people don't like using the
/// joystick to look around, they said it's sluggish - let people use
/// the joystick to move but touch anywhere to look", and the reverse
/// for whoever wants it. Each piece is on the glass, Live in the
/// editor, and claiming fingers exactly when its own switch is on;
/// TouchControls takes whatever is left of the glass in the matching
/// mode (its MoveTouch and LookTouch).
///
/// THE D-PAD IS THE KEYBOARD'S TOUCH FORM, and means exactly what the
/// keys mean. The reference's movement keys are W/S/A/D
/// (OISKeyBinding.cpp:34-37) and they build the direction vector in
/// ControllerInput.cpp:681-704: forward and back add minus and plus
/// the avatar's own z axis, left and right add minus and plus its x
/// axis - STRAFE, not turn; turning is the separate rotate-key pair
/// (KC_LEFT/KC_RIGHT, OISKeyBinding.cpp:40-41, applied at
/// ControllerInput.cpp:975-983). The floating stick already reports in
/// those terms - X is strafe and Y is forward/back, read at
/// GameView.ApplyInput ("strafe += stick.X; fwd -= stick.Y") - so
/// <see cref="Move"/> is reported on the same axes and the view adds
/// the two together. One movement path, two ways to push it.
///
/// Eight directions, not four, from a thumb that rolls: the pad is cut
/// into eight sectors of 45 degrees centred on the cardinals, and a
/// diagonal is the normalised sum of its two neighbours - which is
/// what two keys held together give in the reference, since the
/// vector is normalised at ControllerInput.cpp:712. The centre is dead,
/// as every real pad's centre is.
///
/// THE LOOK STICK TURNS AT A RATE, not by a drag's distance. Hold it
/// right and you keep turning; let it centre and you stop. Full
/// deflection is the keyboard's rotate speed (GameView.TurnSpeed, the
/// reference's KEYROTATESPEED - 3 radians a second) scaled by the same
/// Look speed option the drag uses, and up and down pitch the view
/// with the same Invert look option. The rate is applied by the view,
/// which owns the delta; this reports <see cref="Look"/> as a
/// deflection in -1..1 on each axis.
///
/// TWO THUMBS. Each control tracks its own finger by index, as
/// TouchControls does, so a thumb on the pad and a thumb on the stick
/// work at once. The events arrive through GameView._UnhandledInput -
/// the same door the floating controls use - so the gate there (a
/// panel, the drawer, the chat box, the editor) closes this too, and
/// nothing moves under a menu. This node itself is MouseFilter.Ignore:
/// it draws, and it never eats a touch of its own, because a touch
/// that misses both controls is still the touch layer's - a tap that
/// targets, or a drag in whichever mode the other switch left it.
///
/// PIECES. "dpad" and "lookstick" are registered with M59Hud and sized,
/// placed, faded and hidden by the arrange screen like everything else.
/// Each is Live only while its switch is on, so the editor shows no
/// handle for a control that is not there. Their defaults are
/// found rather than assumed: the pad sits above the chat block's
/// natural rect, the stick left of the combat arc's, each a gutter
/// away, so neither covers the chat buttons, Auto or the arc on the
/// default layout; what a particular hand wants beyond that is what
/// the editor is for.
/// </summary>
public partial class FixedControls : Control
{
    public const string PadId = "dpad", StickId = "lookstick";

    /// <summary>
    /// Points, at scale 1. The pad is 172 across - four arms of 44, the
    /// thumb's minimum, around a 40 centre - which is in the 160-180 the
    /// guides give for a pad; the stick ring is 150 with a 52 knob.
    /// </summary>
    public const float PadSize = 172f, StickSize = 150f, Knob = 26f;

    /// <summary>The gutter kept from the pieces the defaults hang off, and from the glass's edge.</summary>
    const float Gutter = 16f;

    /// <summary>The pad is on the glass. Set by the view each frame from M59Hud.MovePad.</summary>
    public bool ShowPad
    {
        get => _showPad;
        set
        {
            if (_showPad == value) return;
            _showPad = value;
            // Only the pad's finger: a thumb on the stick is not
            // concerned with a pad coming or going.
            if (_padFinger != -1) { _padFinger = -1; _padSector = -1; Move = Vector2.Zero; }
            QueueRedraw();
        }
    }
    bool _showPad;

    /// <summary>The look stick is on the glass. Set by the view each frame from M59Hud.LookStick.</summary>
    public bool ShowStick
    {
        get => _showStick;
        set
        {
            if (_showStick == value) return;
            _showStick = value;
            if (_stickFinger != -1) { _stickFinger = -1; Look = Vector2.Zero; }
            QueueRedraw();
        }
    }
    bool _showStick;

    /// <summary>Either control is on the glass.</summary>
    public bool Active => _showPad || _showStick;

    /// <summary>-1..1: X is strafe, Y is forward/back with screen-down positive, as TouchControls.Move.</summary>
    public Vector2 Move { get; private set; }
    /// <summary>-1..1 deflection of the look stick: X right, Y screen-down.</summary>
    public Vector2 Look { get; private set; }

    /// <summary>Fingers these controls hold, for the touch layer's mouse-echo guard.</summary>
    public bool Holding => _padFinger != -1 || _stickFinger != -1;

    /// <summary>Raised when a finger this layer held lifts; the touch layer's echo guard listens.</summary>
    public event Action Lifted;

    /// <summary>Fraction of the pad's radius that is dead at the centre.</summary>
    const float PadDead = 0.18f;
    /// <summary>Fraction of the stick's radius that is dead at the centre.</summary>
    const float StickDead = 0.12f;

    int _padFinger = -1, _stickFinger = -1;
    /// <summary>Sector 0..7 clockwise from up, or -1 for the dead centre.</summary>
    int _padSector = -1;

    Part _pad, _stick;
    Rect2 _padRect, _stickRect;
    float _scalePad = 1f, _scaleStick = 1f;
    /// <summary>What the last redraw was queued for; a change in any of them queues another.</summary>
    Rect2 _stampPad, _stampStick;
    int _stampFlags = -1;

    /// <summary>
    /// One drawn control. A piece's node is what M59Hud.Dress fades and
    /// hides, and a piece whose parts are drawn by its parent has no
    /// node to hand over - so each control is its own small node,
    /// positioned at its rect and painted in its own coordinates.
    /// </summary>
    sealed partial class Part : Control
    {
        public Action<Part> Painter;
        public override void _Draw() => Painter?.Invoke(this);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = Vector2.Zero;

        _pad = new Part { Name = "dpad", MouseFilter = MouseFilterEnum.Ignore, Painter = DrawPad };
        _stick = new Part { Name = "lookstick", MouseFilter = MouseFilterEnum.Ignore, Painter = DrawStick };
        AddChild(_pad);
        AddChild(_stick);

        M59Hud.Register(PadId, "Move pad", _pad);
        M59Hud.Register(StickId, "Look stick", _stick);
        M59Hud.Changed += Layout;
    }

    public override void _ExitTree() => M59Hud.Changed -= Layout;

    public override void _Process(double delta)
    {
        // By hand, every frame, as the other pieces do: the defaults
        // hang off the chat's and the arc's natural rects, which are
        // written when THEY lay out, and the player's drag arrives
        // through M59Hud.Changed. Cheap: two rects and a string.
        Layout();
    }

    /// <summary>Lets go of both fingers. Called when the gate closes.</summary>
    public void Drop()
    {
        bool held = Holding;
        _padFinger = _stickFinger = -1;
        _padSector = -1;
        Move = Look = Vector2.Zero;
        if (held) QueueRedraw();
    }

    static float HudScale(string id)
    {
        M59Hud.Piece p = M59Hud.Get(id);
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>Whether a piece is on the glass for a thumb: its switch on, and not hidden by the player.</summary>
    bool Usable(string id) => Chosen(id) && !M59Hud.Editing && M59Hud.Shows(id);

    /// <summary>The switch behind a piece.</summary>
    bool Chosen(string id) => id == PadId ? _showPad : _showStick;

    void Layout()
    {
        Vector2 v = GetViewportRect().Size;
        Size = v;

        M59Hud.Piece pad = M59Hud.Get(PadId), stick = M59Hud.Get(StickId);
        if (pad == null || stick == null) return;

        // Not Live while its switch is off: the editor draws no handle
        // for a piece that is not Live (HudEditor.Drawn), and the
        // player's offsets are kept in the model regardless, so a
        // switch back finds the control where it was put. Each on its
        // own - the pad can be there without the stick and the stick
        // without the pad.
        pad.Live = _showPad;
        stick.Live = _showStick;
        _pad.Visible = _showPad;
        _stick.Visible = _showStick;
        if (!Active) return;

        _scalePad = HudScale(PadId);
        _scaleStick = HudScale(StickId);
        float ps = PadSize * _scalePad, ss = StickSize * _scaleStick;

        // The pad: above the chat block, on the left edge. The chat's
        // NATURAL rect, not where the player moved it - a default that
        // followed another piece's drag would move when that piece was
        // dragged, which is not what "default" means.
        M59Hud.Piece chat = M59Hud.Get("chat");
        float padBottom = chat != null && chat.Natural.Size.Y > 2f
            ? chat.Natural.Position.Y - Gutter
            : v.Y - Gutter;
        var padNat = new Rect2(Gutter, Mathf.Round(padBottom - ps), ps, ps);

        // The stick: left of the combat arc, on the bottom edge. The
        // arc's natural box starts at its leftmost seat (ActionButtons.
        // Natural), which on the default layout is the Door button.
        M59Hud.Piece combat = M59Hud.Get("combat");
        float stickRight = combat != null && combat.Natural.Size.X > 2f
            ? combat.Natural.Position.X - Gutter
            : v.X - Gutter;
        var stickNat = new Rect2(Mathf.Round(stickRight - ss), Mathf.Round(v.Y - Gutter - ss), ss, ss);

        _padRect = M59Hud.Place(PadId, padNat, v);
        _stickRect = M59Hud.Place(StickId, stickNat, v);

        _pad.Position = _padRect.Position; _pad.Size = _padRect.Size;
        _stick.Position = _stickRect.Position; _stick.Size = _stickRect.Size;

        // Dress sets Visible false for a hidden piece outside the
        // editor and never sets it back - the other pieces re-show
        // themselves on every layout for the same reason. A piece whose
        // switch is off stays off whatever Dress would say.
        _pad.Visible = _showPad;
        _stick.Visible = _showStick;
        if (_showPad) M59Hud.Dress(PadId);
        if (_showStick) M59Hud.Dress(StickId);

        // A finger on a control the player just hid, or that the editor
        // just took over, is let go: a pad that cannot be seen must not
        // go on walking.
        if (_padFinger != -1 && !Usable(PadId)) { _padFinger = -1; _padSector = -1; Move = Vector2.Zero; }
        if (_stickFinger != -1 && !Usable(StickId)) { _stickFinger = -1; Look = Vector2.Zero; }

        // Compared as values, not as a string: the interpolated stamp
        // this was cost ~700 bytes a frame for the life of the pad
        // (notes/godot-ui.md, "A string built to compare").
        int flags = (M59Hud.Editing ? 1 : 0) | (_showPad ? 2 : 0) | (_showStick ? 4 : 0);
        if (_padRect != _stampPad || _stickRect != _stampStick || flags != _stampFlags)
        {
            _stampPad = _padRect; _stampStick = _stickRect; _stampFlags = flags;
            _pad.QueueRedraw(); _stick.QueueRedraw();
        }
    }

    // ---- input ------------------------------------------------------

    /// <summary>
    /// Offers an event to the two controls. True when a control took
    /// it - a press inside one, or a drag or release of a finger one
    /// already holds - and the touch layer should not see it.
    /// <paramref name="at"/> is the event's position in THIS layer's
    /// coordinates: the interface layer is inset by SafeArea and the
    /// events arrive in viewport coordinates, so the view converts
    /// (GameView.UiPoint) before asking.
    /// </summary>
    public bool Handle(InputEvent e, Vector2 at)
    {
        if (!Active) return false;
        switch (e)
        {
            case InputEventScreenTouch t when t.Pressed:
                if (_padFinger == -1 && Usable(PadId) && Inside(_padRect, at))
                {
                    _padFinger = t.Index;
                    PadAt(at);
                    return true;
                }
                if (_stickFinger == -1 && Usable(StickId) && Inside(_stickRect, at))
                {
                    _stickFinger = t.Index;
                    StickAt(at);
                    return true;
                }
                return false;

            case InputEventScreenTouch t:
                if (t.Index == _padFinger)
                {
                    _padFinger = -1; _padSector = -1; Move = Vector2.Zero;
                    _pad.QueueRedraw();
                    Lifted?.Invoke();
                    return true;
                }
                if (t.Index == _stickFinger)
                {
                    _stickFinger = -1; Look = Vector2.Zero;
                    _stick.QueueRedraw();
                    Lifted?.Invoke();
                    return true;
                }
                return false;

            case InputEventScreenDrag d when d.Index == _padFinger:
                PadAt(at);
                return true;

            case InputEventScreenDrag d when d.Index == _stickFinger:
                StickAt(at);
                return true;
        }
        return false;
    }

    /// <summary>A circle, not the box: the corners of a round control are not part of it.</summary>
    static bool Inside(Rect2 r, Vector2 at)
    {
        float radius = r.Size.X * 0.5f;
        return (at - r.GetCenter()).Length() <= radius;
    }

    /// <summary>
    /// Which way the thumb is pushing. The thumb is not required to stay
    /// inside the pad once it has pressed it: a thumb that pushes
    /// forward hard rolls past the rim, and a pad that let go at the
    /// rim would stop the player exactly when they meant it most.
    /// </summary>
    void PadAt(Vector2 at)
    {
        Vector2 d = at - _padRect.GetCenter();
        float radius = _padRect.Size.X * 0.5f;
        int sector = -1;
        if (d.Length() > radius * PadDead)
        {
            // Clockwise from up, screen coordinates: atan2 of (x, -y)
            // puts up at 0 and right at a quarter turn.
            float a = MathF.Atan2(d.X, -d.Y);
            if (a < 0f) a += MathF.Tau;
            sector = (int)MathF.Floor((a + MathF.Tau / 16f) / (MathF.Tau / 8f)) % 8;
        }
        if (sector != _padSector) { _padSector = sector; _pad.QueueRedraw(); }
        Move = sector < 0 ? Vector2.Zero : SectorMove(sector);
    }

    /// <summary>The unit vector of a sector, on the stick's axes.</summary>
    static Vector2 SectorMove(int s)
    {
        const float h = 0.70710678f;
        switch (s)
        {
            case 0: return new Vector2(0f, -1f);     // forward
            case 1: return new Vector2(h, -h);
            case 2: return new Vector2(1f, 0f);      // strafe right
            case 3: return new Vector2(h, h);
            case 4: return new Vector2(0f, 1f);      // back
            case 5: return new Vector2(-h, h);
            case 6: return new Vector2(-1f, 0f);     // strafe left
            case 7: return new Vector2(-h, -h);
        }
        return Vector2.Zero;
    }

    void StickAt(Vector2 at)
    {
        float radius = _stickRect.Size.X * 0.5f - Knob * _scaleStick;
        Vector2 d = (at - _stickRect.GetCenter()) / Mathf.Max(1f, radius);
        float len = d.Length();
        if (len > 1f) d /= len;
        // Dead at the centre, and the live range rescaled to start at
        // zero past it, so the first movement out of the dead zone is a
        // small turn rather than a jump.
        if (len < StickDead) d = Vector2.Zero;
        else d *= (Mathf.Min(len, 1f) - StickDead) / (1f - StickDead) / Mathf.Min(len, 1f);
        Look = d;
        _stick.QueueRedraw();
    }

    // ---- drawing ----------------------------------------------------

    static Color With(Color c, float a) => new Color(c.R, c.G, c.B, a);

    /// <summary>
    /// A cross of two rounded arms with an arrow at each tip, the pushed
    /// arm (or both, on a diagonal) lit in gold. The fills are faint on
    /// purpose: the pad sits on the world and the world has to show
    /// through it.
    /// </summary>
    void DrawPad(Part c)
    {
        float s = c.Size.X;
        if (s < 2f) return;
        float arm = Mathf.Round(s * 0.33f);         // arm thickness
        float r = Mathf.Round(arm * 0.3f);          // corner radius
        Vector2 ctr = new Vector2(s, s) * 0.5f;

        var fill = With(M59Skin.Card, 0.38f);
        var edge = With(M59Skin.GoldDim, 0.75f);
        var lit = With(M59Skin.Gold, 0.42f);
        var arrow = With(M59Skin.GoldDim, 0.9f);
        var arrowLit = M59Skin.GoldBright;

        // ONE polygon for the plate, not two arms laid across each
        // other: a translucent fill drawn twice is twice as dark where
        // the arms cross, and the crossing is the one part of the pad
        // that should look like nothing in particular.
        float h = (s - arm) * 0.5f;
        var cross = new[]
        {
            new Vector2(h, 0f), new Vector2(h + arm, 0f), new Vector2(h + arm, h), new Vector2(s, h),
            new Vector2(s, h + arm), new Vector2(h + arm, h + arm), new Vector2(h + arm, s), new Vector2(h, s),
            new Vector2(h, h + arm), new Vector2(0f, h + arm), new Vector2(0f, h), new Vector2(h, h),
        };
        c.DrawColoredPolygon(cross, fill);
        var outline = new Vector2[cross.Length + 1];
        Array.Copy(cross, outline, cross.Length);
        outline[cross.Length] = cross[0];
        c.DrawPolyline(outline, edge, 1.5f, true);

        // The pushed arms. A sector is one arm (even) or two (odd); the
        // arms stop at the centre plate, so a diagonal lights two arms
        // and never paints the centre twice.
        if (_padSector >= 0)
        {
            var litBox = Rounded(lit, With(M59Skin.Gold, 0.9f), r);
            foreach (int a in ArmsOf(_padSector))
                c.DrawStyleBox(litBox, ArmRect(a, s, arm));
        }

        // Arrows: up, right, down, left.
        float tip = Mathf.Round(arm * 0.34f);
        for (int a = 0; a < 4; a++)
        {
            bool on = _padSector >= 0 && Array.IndexOf(ArmsOf(_padSector), a) >= 0;
            Vector2 dir = a switch { 0 => Vector2.Up, 1 => Vector2.Right, 2 => Vector2.Down, _ => Vector2.Left };
            Vector2 side = new Vector2(-dir.Y, dir.X);
            Vector2 apex = ctr + dir * (s * 0.5f - tip * 0.9f);
            Vector2 baseC = apex - dir * tip;
            c.DrawColoredPolygon(new[] { apex, baseC + side * tip * 0.8f, baseC - side * tip * 0.8f },
                                 on ? arrowLit : arrow);
        }

        // The centre: a small ring, which is the dead zone made visible.
        c.DrawArc(ctr, s * 0.5f * PadDead, 0f, MathF.Tau, 24, With(M59Skin.GoldDim, 0.6f), 2f, true);
    }

    /// <summary>Which of the four arms a sector lights: 0 up, 1 right, 2 down, 3 left.</summary>
    static int[] ArmsOf(int sector) => sector switch
    {
        0 => new[] { 0 }, 1 => new[] { 0, 1 }, 2 => new[] { 1 }, 3 => new[] { 1, 2 },
        4 => new[] { 2 }, 5 => new[] { 2, 3 }, 6 => new[] { 3 }, 7 => new[] { 3, 0 },
        _ => Array.Empty<int>(),
    };

    /// <summary>One arm, from the centre plate's edge out to the rim.</summary>
    static Rect2 ArmRect(int arm, float s, float thick)
    {
        float half = (s - thick) * 0.5f;
        return arm switch
        {
            0 => new Rect2(half, 0f, thick, half),
            1 => new Rect2(half + thick, half, half, thick),
            2 => new Rect2(half, half + thick, thick, half),
            _ => new Rect2(0f, half, half, thick),
        };
    }

    static StyleBoxFlat Rounded(Color fill, Color edge, float radius)
    {
        var b = new StyleBoxFlat { BgColor = fill, AntiAliasing = true, BorderColor = edge };
        b.BorderWidthTop = b.BorderWidthBottom = b.BorderWidthLeft = b.BorderWidthRight = 1;
        b.CornerRadiusTopLeft = b.CornerRadiusTopRight =
        b.CornerRadiusBottomLeft = b.CornerRadiusBottomRight = (int)radius;
        return b;
    }

    /// <summary>
    /// A ring and a knob, the knob where the thumb is. The floating
    /// stick draws the same two shapes (TouchControls.Draw); this one
    /// is in the skin's colours because it is furniture, there all the
    /// time, and a white ring that never goes away would be a smudge.
    /// </summary>
    void DrawStick(Part c)
    {
        float s = c.Size.X;
        if (s < 2f) return;
        Vector2 ctr = new Vector2(s, s) * 0.5f;
        float radius = s * 0.5f;
        float knob = Knob * _scaleStick;
        bool held = _stickFinger != -1;

        c.DrawCircle(ctr, radius - 1f, With(M59Skin.Card, 0.30f));
        c.DrawArc(ctr, radius - 1.5f, 0f, MathF.Tau, 64, With(M59Skin.GoldDim, 0.75f), 2f, true);
        // Four ticks, so the ring reads as a stick's well and not as a
        // button, and so the centre can be found with the thumb lifted.
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI * 0.5f;
            Vector2 d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            c.DrawLine(ctr + d * (radius - 12f), ctr + d * (radius - 4f), With(M59Skin.GoldDim, 0.8f), 2f);
        }

        Vector2 k = ctr + Look * (radius - knob);
        c.DrawCircle(k, knob, With(held ? M59Skin.Gold : M59Skin.GoldDim, held ? 0.62f : 0.42f));
        c.DrawArc(k, knob - 1f, 0f, MathF.Tau, 40, With(held ? M59Skin.GoldBright : M59Skin.Gold, 0.9f), 2f, true);
    }
}
