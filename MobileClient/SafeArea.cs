using Godot;

/// <summary>
/// Keeps the interface off the edges of the screen.
///
/// Two different problems, one answer:
///
///  - The cutouts the system knows about - a camera hole, a status bar,
///    a gesture bar. Android reports those, and Godot passes them on as
///    `DisplayServer.GetDisplaySafeArea()`, in real screen pixels.
///  - The curve of the glass, which nothing reports. A phone's corners
///    are rounded and its long edges often fall away, and a button in
///    the corner is half gone or simply cannot be pressed. That is what
///    <c>extra</c> is for: a flat margin, taken from the short side so
///    it means the same thing whichever way the phone is held.
///
/// The whole interface layer is scaled and shifted rather than every
/// panel being taught about insets. A panel lays itself out against the
/// viewport as it always has; the layer it lives on is what sits inside
/// the safe rectangle. The world underneath is NOT inset - the game
/// should fill the glass, curve and all - and taps still land where
/// they look, because Godot puts input through the layer's transform.
/// </summary>
public static class SafeArea
{
    /// <summary>
    /// The margin for the curve, as a share of the screen's short side.
    /// Rounded corners eat about ten to fifteen device-independent
    /// pixels on the phones this is aimed at.
    /// </summary>
    public const float CurveFraction = 0.025f;

    /// <summary>
    /// Insets in viewport units: left, top, right, bottom. The system's
    /// safe area arrives in screen pixels and the viewport is stretched,
    /// so it has to be converted before it means anything here.
    /// </summary>
    public static Vector4 Insets(Vector2 viewport)
    {
        float extra = Mathf.Min(viewport.X, viewport.Y) * CurveFraction;
        var pad = new Vector4(extra, extra, extra, extra);

        // Only the handhelds are asked. A desktop - and xvfb, which is
        // what the harness runs on - answers with the DISPLAY's size
        // rather than the window's: 1280x1024 against a 2340x1080
        // window, which reads as a thousand pixels of cutout on the
        // right and shrinks the whole interface to a corner. That is
        // exactly what the first cut of this did.
        string os = OS.GetName();
        if (os != "Android" && os != "iOS") return pad;

        Vector2I window = DisplayServer.WindowGetSize();
        if (window.X <= 0 || window.Y <= 0) return pad;

        Rect2I safe;
        try { safe = DisplayServer.GetDisplaySafeArea(); }
        catch (System.Exception) { return pad; }

        if (safe.Size.X <= 0 || safe.Size.Y <= 0) return pad;
        // A safe area bigger than the window is not about this window.
        if (safe.Size.X > window.X || safe.Size.Y > window.Y) return pad;
        float sx = viewport.X / window.X, sy = viewport.Y / window.Y;
        float left   = Mathf.Max(0, safe.Position.X) * sx;
        float top    = Mathf.Max(0, safe.Position.Y) * sy;
        float right  = Mathf.Max(0, window.X - (safe.Position.X + safe.Size.X)) * sx;
        float bottom = Mathf.Max(0, window.Y - (safe.Position.Y + safe.Size.Y)) * sy;

        // However confident the system sounds, a fifth of the screen
        // is not an inset, it is a bug - and a bug that eats the
        // interface is worse than a button under the curve.
        float capX = viewport.X * 0.2f, capY = viewport.Y * 0.2f;
        return new Vector4(
            Mathf.Min(left + extra, capX), Mathf.Min(top + extra, capY),
            Mathf.Min(right + extra, capX), Mathf.Min(bottom + extra, capY));
    }

    /// <summary>
    /// Puts a layer's contents inside the safe rectangle. The scale is
    /// uniform - the same on both axes - because a layer squashed on
    /// one axis turns every round icon into an egg.
    /// </summary>
    public static void Apply(CanvasLayer layer, Vector2 viewport)
    {
        if (layer == null || viewport.X < 1f || viewport.Y < 1f) return;

        Vector4 i = Insets(viewport);
        float w = Mathf.Max(1f, viewport.X - i.X - i.Z);
        float h = Mathf.Max(1f, viewport.Y - i.Y - i.W);
        float s = Mathf.Min(w / viewport.X, h / viewport.Y);

        layer.Scale = new Vector2(s, s);
        layer.Offset = new Vector2(
            i.X + (w - viewport.X * s) * 0.5f,
            i.Y + (h - viewport.Y * s) * 0.5f);
    }
}
