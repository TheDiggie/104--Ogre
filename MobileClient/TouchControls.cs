using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Touch input for the first-person views.
///
/// Left half of the screen is a floating movement stick: it appears where
/// your thumb lands and reports a direction until you lift. Right half is
/// look - drag to turn. Both track their own finger by index, so moving
/// and looking at the same time works, which is the whole point of
/// splitting the screen rather than using fixed on-screen buttons.
///
/// A TAP, though, targets on EITHER half. The split is about drags, not
/// about pixels, and it used to be about pixels: the stick's release
/// branch set no tap, so a touch that began left of the midline could
/// never become a target. That is not a missing convenience, it is a
/// whole half of the world you cannot talk to. Everything a target
/// enables is gated on one - Look, Buy, Trade, Get and Quest all act on
/// the library's TargetID (ActionBar.cs:265-271, GameView.cs:1511-1515)
/// - and the only other way to acquire one, NextTarget, considers
/// nothing but `IsAttackable || IsMinimapEnemy`
/// (DataController.cs:1326-1330, :1372-1388). So a shopkeeper or a quest
/// giver standing on the left was unreachable by either route, and
/// ReqNPCQuests has no action, no command and no hotbar slot behind which
/// to hide - it is target-or-nothing. The reference has no dead half:
/// every pixel of the window picks, because the press handler just rays
/// through it (ControllerInput.cpp:332-341 into PerformMouseOver at
/// :143-215), and its NextTarget is a key bound as a combat convenience
/// (:567-568), never the only road in.
///
/// What makes both work at once is that a stick touch which never MOVES
/// is not steering. The stick stays asleep until the finger has wandered
/// TapSlop, and a finger lifted before that reports a tap instead. The
/// cost is a 16px dead zone at the centre of a 120px stick - 13% of its
/// travel, which a thumb could not aim inside anyway, and which every
/// real stick has. Nothing is taken from the drag: the origin is still
/// where the thumb landed, so once awake the full range and the full
/// response are there, with no jump at the threshold.
///
/// Rejected, in order:
///
/// - Giving Quest and Look a second affordance of their own. It fixes
///   two of six (Buy, Trade, Get and Activate stay unreachable), and
///   there is nowhere to put them: the bottom row is already full at
///   1080 wide, which is why Autorun's own window is still parked
///   (notes/rulings.md, "Open questions").
/// - Widening what Next cycles to take in shopkeepers and quest givers.
///   NextTarget is the library's, shared with the reference, and its
///   narrowness is deliberate - it is the combat tab key, ordering guild
///   enemies ahead of monsters by distance (DataController.cs:1342-1370).
///   Making it walk every signpost and barkeep in the room to reach the
///   one NPC with a "!" would wreck the thing it is good at, and it is
///   library code besides.
/// - Shrinking the stick half, or moving the midline. Any line leaves a
///   region where a tap is impossible, and this one is in the right
///   place: a phone player rests a thumb on the left and keeps it there.
/// - Requiring a long press, or a two-finger tap, to target on the left.
///   Both are learnable and neither is discoverable, and the defect being
///   fixed is precisely that nothing told the player anything.
///
/// Mouse input drives the same values on desktop, so one code path serves
/// both.
/// </summary>
public sealed class TouchControls
{
    /// <summary>-1..1 left/right and forward/back from the movement stick.</summary>
    public Vector2 Move { get; private set; } = Vector2.Zero;
    /// <summary>
    /// How fast an accumulated look is spent, per second.
    ///
    /// A drag does not turn you; it adds to a debt that is paid off over
    /// the following frames. The reference does exactly this - a mouse
    /// delta only ever does `avatarYawDelta += 0.000125 * MouseAimDistance
    /// * dx` (ControllerInput.cpp:447), and the tick spends
    /// `MouseAimSpeed * 0.000325 * delta * milliseconds` of it
    /// (:916-917), taking the same fraction of what is left every tick.
    /// With the defaults of 75 and 45 (OgreClientConfig.h:59-60) that is
    /// 0.024375 of the remaining debt per millisecond, or about 41% of
    /// it in a sixty-hertz frame: the turn eases in, and coasts for a
    /// moment after the finger stops.
    ///
    /// Spending all of it the same frame, which is what this did, locks
    /// the view rigidly to the finger. The per-pixel gain was already
    /// right; the weight was missing.
    /// </summary>
    public const float LookSpend = 75f * 0.000325f * 1000f;

    /// <summary>
    /// Radians to turn in <paramref name="seconds"/>, taken off the
    /// accumulated look. See LookSpend.
    /// </summary>
    public float TakeTurn(double seconds) => Spend(ref _turn, seconds);

    /// <summary>
    /// Radians to look up or down, taken off the accumulated look the
    /// same way - the reference smooths its camera pitch with the very
    /// same step (ControllerInput.cpp:450-451). Dragging up and down
    /// doing nothing is the sort of thing that makes a control scheme
    /// feel broken rather than limited.
    /// </summary>
    public float TakePitch(double seconds) => Spend(ref _pitch, seconds);

    static void Reverse(ref float debt, float move)
    {
        if (move != 0f && MathF.Sign(move) != MathF.Sign(debt)) debt = 0f;
    }

    static float Spend(ref float debt, double seconds)
    {
        if (debt == 0f) return 0f;
        float step = debt * LookSpend * (float)seconds;
        // Never overshoot: the reference clamps the remainder at zero
        // from whichever side it approached (ControllerInput.cpp:919-925).
        if (MathF.Abs(step) >= MathF.Abs(debt)) { step = debt; debt = 0f; }
        else debt -= step;
        return step;
    }

    /// <summary>
    /// A tap on EITHER half - a finger put down and lifted without really
    /// moving. Used to target what you touched. Consumed by reading.
    /// </summary>
    public bool TakeTap(out Vector2 position)
    {
        position = _tapAt;
        bool had = _tapped;
        _tapped = false;
        return had;
    }

    /// <summary>Pixels a finger may wander and still count as a tap.</summary>
    public float TapSlop = 16f;

    public float StickRadius = 120f;
    public float LookSensitivity = 0.006f;
    /// <summary>Drag up to look down, for those who want it that way.</summary>
    public bool InvertLook;

    float _turn, _pitch;
    int _moveFinger = -1, _lookFinger = -1;
    /// <summary>
    /// A second finger on the stick half, while the stick itself is
    /// busy: a player steering with the left thumb who reaches in with
    /// another finger to point at something. It can only ever tap.
    /// </summary>
    int _tapFinger = -1;
    Vector2 _moveOrigin, _moveCurrent, _tapOrigin;
    Vector2 _lookOrigin, _tapAt, _mouseDownAt;
    /// <summary>
    /// The stick has woken: this finger has travelled past TapSlop, so
    /// it is steering and its release is not a tap. Sticky for the life
    /// of the touch - a thumb that pushes forward and comes back to
    /// where it started has still walked, and must not also target.
    /// </summary>
    bool _moveEngaged;
    bool _tapFingerMoved;
    bool _lookMoved, _tapped;
    bool _mouseLook;

    /// <summary>The next mouse event is this device echoing a touch.</summary>
    bool _mouseIsEcho;

    /// <summary>
    /// Drawn only once the stick is steering. A tap would otherwise
    /// flash a ring and a knob under the thumb for the frames it is
    /// down, which reads as a control that was about to do something.
    /// </summary>
    public bool StickActive => _moveFinger != -1 && _moveEngaged;
    public Vector2 StickOrigin => _moveOrigin;
    public Vector2 StickCurrent => _moveCurrent;

    public void Handle(InputEvent e, Vector2 viewport)
    {
        float mid = viewport.X * 0.5f;

        switch (e)
        {
            case InputEventScreenTouch t when t.Pressed:
                if (t.Position.X < mid && _moveFinger == -1)
                {
                    _moveFinger = t.Index;
                    _moveOrigin = _moveCurrent = t.Position;
                    _moveEngaged = false;
                }
                else if (t.Position.X < mid && _tapFinger == -1)
                {
                    _tapFinger = t.Index;
                    _tapOrigin = t.Position;
                    _tapFingerMoved = false;
                }
                else if (t.Position.X >= mid && _lookFinger == -1)
                {
                    _lookFinger = t.Index;
                    _lookOrigin = t.Position;
                    _lookMoved = false;
                }
                break;

            case InputEventScreenTouch t:                       // released
                // The emulated mouse release arrives just after this
                // one, by which time no finger is down and the guard
                // below would let it through - as a tap, at the place
                // the thumb left, which retargets whatever is there.
                _mouseIsEcho = true;
                if (t.Index == _moveFinger)
                {
                    // Lifted without ever waking the stick: the thumb
                    // pointed at something instead of steering with it.
                    // Move was never anything but zero for this touch,
                    // so there is nothing to undo and nothing lurched.
                    if (!_moveEngaged) { _tapped = true; _tapAt = t.Position; }
                    _moveFinger = -1;
                    _moveEngaged = false;
                    Move = Vector2.Zero;
                }
                if (t.Index == _tapFinger)
                {
                    if (!_tapFingerMoved) { _tapped = true; _tapAt = t.Position; }
                    _tapFinger = -1;
                }
                if (t.Index == _lookFinger)
                {
                    if (!_lookMoved) { _tapped = true; _tapAt = t.Position; }
                    _lookFinger = -1;
                }
                break;

            case InputEventScreenDrag d when d.Index == _moveFinger:
                _moveCurrent = d.Position;
                // A touch reports drags for three pixels of thumb roll as
                // readily as for a push, and until this wakes the stick
                // those three pixels used to be a step of walking: a tap
                // that nudged the avatar. Measured from the ORIGIN, not
                // frame to frame, so a slow push crosses it exactly once.
                if ((_moveCurrent - _moveOrigin).Length() > TapSlop) _moveEngaged = true;
                Move = _moveEngaged ? Clamp((_moveCurrent - _moveOrigin) / StickRadius)
                                    : Vector2.Zero;
                break;

            case InputEventScreenDrag d when d.Index == _tapFinger:
                if ((d.Position - _tapOrigin).Length() > TapSlop) _tapFingerMoved = true;
                break;

            case InputEventScreenDrag d when d.Index == _lookFinger:
                // A reversal drops what is still owed rather than
                // fighting it, which is how the reference stops dead
                // when you change direction (ControllerInput.cpp:443-444
                // and :822-823).
                Reverse(ref _turn, d.Relative.X);
                Reverse(ref _pitch, -d.Relative.Y * (InvertLook ? -1f : 1f));
                _turn  += d.Relative.X * LookSensitivity;
                _pitch -= d.Relative.Y * LookSensitivity * (InvertLook ? -1f : 1f);   // drag up, look up
                if ((d.Position - _lookOrigin).Length() > TapSlop) _lookMoved = true;
                break;

            // Desktop: a left drag looks around.
            //
            // Godot raises a mouse event for every touch as well, by
            // default and for a good reason - a Button only reacts to
            // mouse events, so without it nothing on screen could be
            // pressed. But it means a thumb on the movement stick
            // arrives here TWICE: once as a screen drag, which moves
            // you, and once as mouse motion, which turned the camera.
            // One push forward and you were looking at the ceiling with
            // no way back but a drag on the other half. So a mouse
            // event that arrives while a finger is down is the same
            // gesture arriving a second time, and is dropped.
            case InputEventMouseButton when _moveFinger != -1 || _lookFinger != -1 || _mouseIsEcho:
                _mouseIsEcho = false;
                _mouseLook = false;
                break;

            case InputEventMouseMotion when _moveFinger != -1 || _lookFinger != -1:
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                _mouseLook = mb.Pressed;
                if (mb.Pressed) { _mouseDownAt = mb.Position; _lookMoved = false; }
                else if (!_lookMoved) { _tapped = true; _tapAt = mb.Position; }
                break;

            case InputEventMouseMotion mm when _mouseLook:
                Reverse(ref _turn, mm.Relative.X);
                Reverse(ref _pitch, -mm.Relative.Y * (InvertLook ? -1f : 1f));
                _turn  += mm.Relative.X * LookSensitivity;
                _pitch -= mm.Relative.Y * LookSensitivity * (InvertLook ? -1f : 1f);
                if ((mm.Position - _mouseDownAt).Length() > TapSlop) _lookMoved = true;
                break;
        }
    }

    static Vector2 Clamp(Vector2 v)
    {
        float len = v.Length();
        return len > 1f ? v / len : v;
    }

    /// <summary>Draws the stick so there is something to aim at.</summary>
    public void Draw(CanvasItem c)
    {
        if (!StickActive) return;
        var ring = new Color(1, 1, 1, 0.22f);
        var knob = new Color(1, 1, 1, 0.40f);
        c.DrawArc(_moveOrigin, StickRadius, 0, MathF.Tau, 48, ring, 3f, true);
        Vector2 k = _moveOrigin + Clamp((_moveCurrent - _moveOrigin) / StickRadius) * StickRadius;
        c.DrawCircle(k, 28f, knob);
    }
}
