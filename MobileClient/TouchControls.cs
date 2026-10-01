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
/// Mouse input drives the same values on desktop, so one code path serves
/// both.
/// </summary>
public sealed class TouchControls
{
    /// <summary>-1..1 left/right and forward/back from the movement stick.</summary>
    public Vector2 Move { get; private set; } = Vector2.Zero;
    /// <summary>Radians to turn this frame, consumed by reading it.</summary>
    public float TakeTurn() { float t = _turn; _turn = 0f; return t; }

    /// <summary>
    /// Radians to look up or down this frame, consumed by reading it.
    /// Dragging up and down doing nothing is the sort of thing that makes
    /// a control scheme feel broken rather than limited.
    /// </summary>
    public float TakePitch() { float t = _pitch; _pitch = 0f; return t; }

    /// <summary>
    /// A tap on the look half - a finger put down and lifted without
    /// really moving. Used to target what you touched. Consumed by reading.
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
    Vector2 _moveOrigin, _moveCurrent;
    Vector2 _lookOrigin, _tapAt, _mouseDownAt;
    bool _lookMoved, _tapped;
    bool _mouseLook;

    /// <summary>The next mouse event is this device echoing a touch.</summary>
    bool _mouseIsEcho;

    public bool StickActive => _moveFinger != -1;
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
                if (t.Index == _moveFinger) { _moveFinger = -1; Move = Vector2.Zero; }
                if (t.Index == _lookFinger)
                {
                    if (!_lookMoved) { _tapped = true; _tapAt = t.Position; }
                    _lookFinger = -1;
                }
                break;

            case InputEventScreenDrag d when d.Index == _moveFinger:
                _moveCurrent = d.Position;
                Move = Clamp((_moveCurrent - _moveOrigin) / StickRadius);
                break;

            case InputEventScreenDrag d when d.Index == _lookFinger:
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
