using System;
using Godot;
using Meridian59.Files.ROO;

/// <summary>
/// A corner map of the room you are in, with you on it.
///
/// Walking 362 rooms with no idea which way you came in is the main thing
/// that makes the offline build tiring to use. This draws the room's walls
/// once into a texture when the room loads - some rooms have six thousand
/// walls and redrawing those every frame would cost more than the game
/// does - and then only the marker moves.
///
/// Room X and Y map to image columns and rows, which is the convention the
/// top-down map tool uses and which was checked against the wiki's own
/// map for dvalley1.
/// </summary>
public partial class MiniMap : Control
{
    // MapSize, not Size: Control already has one.
    [Export] public int MapSize = 220;
    [Export] public float Margin = 12f;

    ImageTexture _tex;
    Button _toggle;
    float _minX, _minY, _scale;
    bool _shown = true;

    // Solid walls, walls you can walk through, and you.
    static readonly Color Solid  = new Color(0.85f, 0.85f, 0.90f, 0.95f);
    static readonly Color Portal = new Color(0.45f, 0.48f, 0.55f, 0.80f);
    static readonly Color Back   = new Color(0.04f, 0.04f, 0.06f, 0.72f);
    static readonly Color You    = new Color(1.00f, 0.82f, 0.35f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _toggle = new Button { Text = "Map" };
        _toggle.Pressed += () => { _shown = !_shown; QueueRedraw(); };
        AddChild(_toggle);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_toggle == null) return;
        Vector2 v = GetViewportRect().Size;
        _toggle.Size = new Vector2(70, 40);
        _toggle.Position = new Vector2(v.X - 70 - Margin, v.Y - 40 - Margin);
    }

    /// <summary>Draws a room's walls into the map texture. Call on room change.</summary>
    public void Build(RooFile roo)
    {
        _tex = null;
        if (roo == null || roo.Walls.Count == 0) return;

        float maxX = float.MinValue, maxY = float.MinValue;
        _minX = float.MaxValue; _minY = float.MaxValue;
        foreach (RooWall w in roo.Walls)
        {
            _minX = MathF.Min(_minX, MathF.Min(w.X1, w.X2));
            maxX  = MathF.Max(maxX,  MathF.Max(w.X1, w.X2));
            _minY = MathF.Min(_minY, MathF.Min(w.Y1, w.Y2));
            maxY  = MathF.Max(maxY,  MathF.Max(w.Y1, w.Y2));
        }

        float spanX = MathF.Max(1f, maxX - _minX), spanY = MathF.Max(1f, maxY - _minY);
        const float pad = 6f;
        _scale = (MapSize - 2f * pad) / MathF.Max(spanX, spanY);

        var img = Image.CreateEmpty(MapSize, MapSize, false, Image.Format.Rgba8);
        img.Fill(Back);

        foreach (RooWall w in roo.Walls)
        {
            bool passable = w.RightSectorNum != 0 && w.LeftSectorNum != 0;
            Line(img,
                 (int)((w.X1 - _minX) * _scale + pad), (int)((w.Y1 - _minY) * _scale + pad),
                 (int)((w.X2 - _minX) * _scale + pad), (int)((w.Y2 - _minY) * _scale + pad),
                 passable ? Portal : Solid);
        }

        _tex = ImageTexture.CreateFromImage(img);
        QueueRedraw();
    }

    /// <summary>Where the map puts a world point, in this control's space.</summary>
    Vector2 ToMap(float wx, float wy, Vector2 origin)
        => origin + new Vector2((wx - _minX) * _scale + 6f, (wy - _minY) * _scale + 6f);

    float _px, _py, _angle;

    /// <summary>Moves the marker. Cheap - no texture work.</summary>
    public void SetPlayer(float worldX, float worldY, float angle)
    {
        _px = worldX; _py = worldY; _angle = angle;
        if (_shown) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_tex == null || !_shown) return;

        Vector2 v = GetViewportRect().Size;
        var origin = new Vector2(v.X - MapSize - Margin, Margin);
        DrawTexture(_tex, origin);

        Vector2 me = ToMap(_px, _py, origin);
        // Clamped, so standing just outside the room's bounds still shows.
        me = new Vector2(Mathf.Clamp(me.X, origin.X, origin.X + MapSize),
                         Mathf.Clamp(me.Y, origin.Y, origin.Y + MapSize));

        // A wedge rather than a dot: which way you are facing is most of
        // what a map is for.
        float c = MathF.Cos(_angle), s = MathF.Sin(_angle);
        var tip  = me + new Vector2(c, s) * 9f;
        var left = me + new Vector2(c * -0.5f - s * 0.5f, s * -0.5f + c * 0.5f) * 9f;
        var right= me + new Vector2(c * -0.5f + s * 0.5f, s * -0.5f - c * 0.5f) * 9f;
        DrawColoredPolygon(new[] { tip, left, right }, You);
    }

    /// <summary>Bresenham, because Image has no line drawing.</summary>
    static void Line(Image img, int x0, int y0, int x1, int y1, Color c)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = (dx - dy) + 4;
        while (guard-- > 0)
        {
            if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h) img.SetPixel(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
