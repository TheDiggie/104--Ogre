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

    public float StickRadius = 120f;
    public float LookSensitivity = 0.006f;

    float _turn;
    int _moveFinger = -1, _lookFinger = -1;
    Vector2 _moveOrigin, _moveCurrent;
    bool _mouseLook;

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
                }
                break;

            case InputEventScreenTouch t:                       // released
                if (t.Index == _moveFinger) { _moveFinger = -1; Move = Vector2.Zero; }
                if (t.Index == _lookFinger) _lookFinger = -1;
                break;

            case InputEventScreenDrag d when d.Index == _moveFinger:
                _moveCurrent = d.Position;
                Move = Clamp((_moveCurrent - _moveOrigin) / StickRadius);
                break;

            case InputEventScreenDrag d when d.Index == _lookFinger:
                _turn += d.Relative.X * LookSensitivity;
                break;

            // Desktop: right mouse button or drag to look.
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                _mouseLook = mb.Pressed;
                break;

            case InputEventMouseMotion mm when _mouseLook:
                _turn += mm.Relative.X * LookSensitivity;
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
