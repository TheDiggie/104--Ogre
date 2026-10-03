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
///
/// MODES. The two halves above are what the glass does when BOTH of its
/// jobs are left to it (MoveTouch and LookTouch). Either job can go to
/// a fixed control instead - a d-pad or a look stick drawn on the glass
/// (FixedControls, chosen by M59Hud.MovePad and M59Hud.LookStick) -
/// and then the glass has one job and no midline: with the pad on,
/// every finger the pad did not take is a look drag, wherever it
/// lands; with the stick on, every finger the stick did not take is
/// the floating stick, wherever it lands. The player's words were that
/// some people find the look stick sluggish and want to move with the
/// stick but look by touching anywhere; the other way round is there
/// for whoever wants it. With both jobs given away the glass only taps
/// (TapOnly). A tap targets in every mode.
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
    /// Moving is the glass's job: a finger that is not a look becomes
    /// the floating stick. Off when the d-pad has it (M59Hud.MovePad).
    /// Changing either mode lets every finger go, as a scheme change
    /// always did: a finger that was a stick must not wake up as a look.
    /// </summary>
    public bool MoveTouch
    {
        get => _moveTouch;
        set { if (_moveTouch != value) { _moveTouch = value; Drop(); } }
    }
    bool _moveTouch = true;

    /// <summary>
    /// Looking is the glass's job: a finger that is not the stick turns
    /// the camera by how far it is dragged. Off when the look stick has
    /// it (M59Hud.LookStick).
    /// </summary>
    public bool LookTouch
    {
        get => _lookTouch;
        set { if (_lookTouch != value) { _lookTouch = value; Drop(); } }
    }
    bool _lookTouch = true;

    /// <summary>
    /// Taps only: both jobs have gone to fixed controls, which are pieces
    /// of the HUD (FixedControls) and claim their own fingers before
    /// anything reaches here, so the whole glass does what both halves
    /// do with a finger that never moves - target what it touched.
    /// Everything the class comment says about a tap on EITHER half
    /// still holds, with no halves.
    /// </summary>
    public bool TapOnly => !_moveTouch && !_lookTouch;

    /// <summary>
    /// Whether some other handler holds a finger right now - the fixed
    /// pad or stick. Consulted by the mouse guards: Godot echoes every
    /// touch as a mouse event, and a thumb the pad has claimed still
    /// arrives here as a mouse press, which with no finger of our own
    /// down would read as a desktop click and target whatever is under
    /// the pad.
    /// </summary>
    public Func<bool> OtherFingerDown;

    /// <summary>
    /// A touch some other handler took has just lifted, so the mouse
    /// release that echoes it is on its way and is not a click.
    /// </summary>
    public void EchoComing() => _mouseIsEcho = true;

    /// <summary>Fingers down in TapOnly mode: where each landed, and whether it has wandered.</summary>
    readonly Dictionary<int, (Vector2 At, bool Moved)> _tapFingers = new Dictionary<int, (Vector2, bool)>();

    bool Foreign() => OtherFingerDown != null && OtherFingerDown();

    /// <summary>
    /// Drawn only once the stick is steering. A tap would otherwise
    /// flash a ring and a knob under the thumb for the frames it is
    /// down, which reads as a control that was about to do something.
    /// </summary>
    public bool StickActive => _moveFinger != -1 && _moveEngaged;
    public Vector2 StickOrigin => _moveOrigin;
    public Vector2 StickCurrent => _moveCurrent;

    /// <summary>
    /// Lets go of every finger and forgets what they owed.
    ///
    /// Called by the view while a panel, the drawer, the chat box or
    /// the HUD editor is up, in place of Handle. Two things have to
    /// happen and dropping the events alone does only one of them. The
    /// events must not reach the look handler: a drag that starts on
    /// the dead part of a card - a label, the gap between two rows, a
    /// slider's track - is not consumed by any Control, so it falls
    /// through to _UnhandledInput and spins the camera while the player
    /// is trying to set a volume. And a finger that was ALREADY looking
    /// when the panel opened must be released, or the turn it had
    /// banked keeps paying out under the panel and the first touch
    /// after it closes is read as that finger's release - a tap, which
    /// retargets whatever is under it.
    /// </summary>
    public void Drop()
    {
        _moveFinger = _tapFinger = _lookFinger = -1;
        _moveEngaged = _tapFingerMoved = _lookMoved = false;
        _tapped = false;
        _tapFingers.Clear();
        _mouseLook = false;
        Move = Vector2.Zero;
        _turn = _pitch = 0f;
    }

    /// <summary>
    /// Fingers that went down while something was up, and so belong to
    /// nothing in the world until they lift.
    ///
    /// This is the half of the fix that Drop alone misses, and it was
    /// measured: with the drawer open, a drag starting on the drawer's
    /// own backdrop still turned the camera by the same amount as with
    /// nothing open at all. The backdrop closes the drawer on the press.
    /// By the first drag event the drawer is shut, the gate in the view
    /// sees nothing up, and the gesture that closed the menu goes on to
    /// spin the world. A finger's fate is decided at the press, as it is
    /// everywhere else in this file; one that pressed into a menu is the
    /// menu's for its whole life, whatever the menu did with it.
    /// </summary>
    readonly HashSet<int> _eaten = new HashSet<int>();

    /// <summary>The index a finger or the mouse reports, on one scale.</summary>
    static int IndexOf(InputEvent e) => e switch
    {
        InputEventScreenTouch t => t.Index,
        InputEventScreenDrag d => d.Index,
        InputEventMouse => -2,
        _ => int.MinValue,
    };

    /// <summary>
    /// Records a press that landed while something was up. Call instead
    /// of Handle for every event while the gate is closed.
    /// </summary>
    public void Eat(InputEvent e)
    {
        if (e is InputEventScreenTouch t && t.Pressed) _eaten.Add(t.Index);
        else if (e is InputEventMouseButton mb && mb.Pressed) _eaten.Add(-2);
        else if (e is InputEventScreenTouch tr && !tr.Pressed) _eaten.Remove(tr.Index);
        else if (e is InputEventMouseButton mr && !mr.Pressed) _eaten.Remove(-2);
    }

    /// <summary>True, and forgets the finger on its release, when this
    /// event belongs to a finger that pressed into a menu.</summary>
    bool Eaten(InputEvent e)
    {
        int i = IndexOf(e);
        if (i == int.MinValue || !_eaten.Contains(i)) return false;
        if ((e is InputEventScreenTouch t && !t.Pressed) || (e is InputEventMouseButton mb && !mb.Pressed))
            _eaten.Remove(i);
        return true;
    }

    public void Handle(InputEvent e, Vector2 viewport)
    {
        if (Eaten(e)) return;
        if (TapOnly) { HandleTapOnly(e); return; }
        float mid = viewport.X * 0.5f;

        switch (e)
        {
            case InputEventScreenTouch t when t.Pressed:
            {
                // Which job this finger is offered. With both jobs here
                // the midline decides, as it always has; with one job
                // given to a fixed control the whole glass is the
                // other's, and a finger that lands on the pad or the
                // stick never arrives (FixedControls.Handle runs first).
                bool moveSide = _moveTouch && (!_lookTouch || t.Position.X < mid);
                bool lookSide = _lookTouch && (!_moveTouch || t.Position.X >= mid);
                if (moveSide && _moveFinger == -1)
                {
                    _moveFinger = t.Index;
                    _moveOrigin = _moveCurrent = t.Position;
                    _moveEngaged = false;
                }
                else if (lookSide && _lookFinger == -1)
                {
                    _lookFinger = t.Index;
                    _lookOrigin = t.Position;
                    _lookMoved = false;
                }
                // A second finger where the stick already is, or
                // anywhere while the glass has one job: it can only
                // tap. A second finger on the look HALF, with both jobs
                // here, is ignored as it always was.
                else if (_tapFinger == -1 && (moveSide || !_moveTouch))
                {
                    _tapFinger = t.Index;
                    _tapOrigin = t.Position;
                    _tapFingerMoved = false;
                }
                break;
            }

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
            // gesture arriving a second time, and is dropped. A finger
            // the pad or the stick holds counts (Foreign): with one job
            // here and one there, their thumb echoes here too.
            case InputEventMouseButton when _moveFinger != -1 || _lookFinger != -1 || _tapFinger != -1 || Foreign() || _mouseIsEcho:
                _mouseIsEcho = false;
                _mouseLook = false;
                break;

            case InputEventMouseMotion when _moveFinger != -1 || _lookFinger != -1 || _tapFinger != -1 || Foreign():
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                _mouseLook = mb.Pressed;
                if (mb.Pressed) { _mouseDownAt = mb.Position; _lookMoved = false; }
                else if (!_lookMoved) { _tapped = true; _tapAt = mb.Position; }
                break;

            // The mouse looks only while looking is the glass's job;
            // with the look stick on it can tap, as in HandleTapOnly.
            case InputEventMouseMotion mm when _mouseLook:
                if (_lookTouch)
                {
                    Reverse(ref _turn, mm.Relative.X);
                    Reverse(ref _pitch, -mm.Relative.Y * (InvertLook ? -1f : 1f));
                    _turn  += mm.Relative.X * LookSensitivity;
                    _pitch -= mm.Relative.Y * LookSensitivity * (InvertLook ? -1f : 1f);
                }
                if ((mm.Position - _mouseDownAt).Length() > TapSlop) _lookMoved = true;
                break;
        }
    }

    /// <summary>
    /// The glass with both jobs given away: every finger the pad and
    /// the stick did not take is a tap or nothing. The same slop and
    /// the same echo guards as the full handler, with no direction
    /// ever reported - Move stays zero and nothing is owed to TakeTurn.
    /// </summary>
    void HandleTapOnly(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t when t.Pressed:
                _tapFingers[t.Index] = (t.Position, false);
                break;

            case InputEventScreenTouch t:
                _mouseIsEcho = true;
                if (_tapFingers.TryGetValue(t.Index, out var f))
                {
                    if (!f.Moved) { _tapped = true; _tapAt = t.Position; }
                    _tapFingers.Remove(t.Index);
                }
                break;

            case InputEventScreenDrag d when _tapFingers.TryGetValue(d.Index, out var g):
                if (!g.Moved && (d.Position - g.At).Length() > TapSlop)
                    _tapFingers[d.Index] = (g.At, true);
                break;

            // The mouse, as in Handle: dropped while any finger is down
            // - ours or the pad's - because it is that finger again.
            case InputEventMouseButton when _tapFingers.Count > 0 || Foreign() || _mouseIsEcho:
                _mouseIsEcho = false;
                _mouseLook = false;
                break;

            case InputEventMouseMotion when _tapFingers.Count > 0 || Foreign():
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                _mouseLook = mb.Pressed;
                if (mb.Pressed) { _mouseDownAt = mb.Position; _lookMoved = false; }
                else if (!_lookMoved) { _tapped = true; _tapAt = mb.Position; }
                break;

            case InputEventMouseMotion mm when _mouseLook:
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
