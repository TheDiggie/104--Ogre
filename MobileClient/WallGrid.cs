using System;
using System.Collections.Generic;
using Meridian59.Files.ROO;

/// <summary>
/// Uniform spatial grid over a room's walls, so a ray only tests the walls
/// in the cells it actually crosses instead of every wall in the room.
///
/// This is a filter, not a reordering: the walls it yields are a superset
/// of the ones a ray can possibly hit, and the caller still sorts by
/// distance. Output is therefore identical to testing every wall - which
/// is exactly how it was verified.
/// </summary>
public sealed class WallGrid
{
    readonly float _minX, _minY, _cell;
    readonly int _cols, _rows;
    readonly List<int>[] _cells;
    readonly RooWall[] _walls;
    readonly int[] _stamp;      // per-wall visit marker, avoids a HashSet
    int _tick;

    public int Cols => _cols;
    public int Rows => _rows;
    public float CellSize => _cell;

    public WallGrid(RooFile roo, float targetCell = 2048f)
    {
        _walls = roo.Walls.ToArray();

        float maxX = float.MinValue, maxY = float.MinValue;
        _minX = float.MaxValue; _minY = float.MaxValue;
        foreach (RooWall w in _walls)
        {
            _minX = MathF.Min(_minX, MathF.Min(w.X1, w.X2));
            maxX  = MathF.Max(maxX,  MathF.Max(w.X1, w.X2));
            _minY = MathF.Min(_minY, MathF.Min(w.Y1, w.Y2));
            maxY  = MathF.Max(maxY,  MathF.Max(w.Y1, w.Y2));
        }
        if (_walls.Length == 0) { _minX = _minY = 0; maxX = maxY = 1; }

        _cell = MathF.Max(64f, targetCell);
        _cols = Math.Max(1, (int)MathF.Ceiling((maxX - _minX) / _cell) + 1);
        _rows = Math.Max(1, (int)MathF.Ceiling((maxY - _minY) / _cell) + 1);

        _cells = new List<int>[_cols * _rows];
        _stamp = new int[_walls.Length];

        for (int i = 0; i < _walls.Length; i++)
        {
            RooWall w = _walls[i];
            int cx0 = Clamp((int)((MathF.Min(w.X1, w.X2) - _minX) / _cell), 0, _cols - 1);
            int cx1 = Clamp((int)((MathF.Max(w.X1, w.X2) - _minX) / _cell), 0, _cols - 1);
            int cy0 = Clamp((int)((MathF.Min(w.Y1, w.Y2) - _minY) / _cell), 0, _rows - 1);
            int cy1 = Clamp((int)((MathF.Max(w.Y1, w.Y2) - _minY) / _cell), 0, _rows - 1);
            // Bounding box rather than an exact walk: a few extra candidates
            // cost far less than the walls this removes.
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int k = cy * _cols + cx;
                    (_cells[k] ??= new List<int>(8)).Add(i);
                }
        }
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>
    /// Calls <paramref name="visit"/> once for each wall in a cell the ray
    /// crosses. Order is by cell along the ray, which is not the same as by
    /// distance - the caller sorts.
    /// </summary>
    public void ForEachCandidate(float ox, float oy, float dx, float dy, Action<RooWall> visit)
    {
        _tick++;

        // Cell the ray starts in, clamped: a camera just outside the room's
        // bounds still gets a sensible starting cell.
        float gx = (ox - _minX) / _cell, gy = (oy - _minY) / _cell;
        int cx = Clamp((int)MathF.Floor(gx), 0, _cols - 1);
        int cy = Clamp((int)MathF.Floor(gy), 0, _rows - 1);

        int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
        int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

        float tDeltaX = dx != 0 ? MathF.Abs(1f / dx) : float.MaxValue;
        float tDeltaY = dy != 0 ? MathF.Abs(1f / dy) : float.MaxValue;

        float tMaxX = dx > 0 ? (cx + 1 - gx) * tDeltaX
                    : dx < 0 ? (gx - cx) * tDeltaX : float.MaxValue;
        float tMaxY = dy > 0 ? (cy + 1 - gy) * tDeltaY
                    : dy < 0 ? (gy - cy) * tDeltaY : float.MaxValue;

        int guard = _cols + _rows + 2;
        while (guard-- > 0)
        {
            List<int> bucket = _cells[cy * _cols + cx];
            if (bucket != null)
                for (int i = 0; i < bucket.Count; i++)
                {
                    int wi = bucket[i];
                    if (_stamp[wi] == _tick) continue;
                    _stamp[wi] = _tick;
                    visit(_walls[wi]);
                }

            if (tMaxX < tMaxY) { tMaxX += tDeltaX; cx += stepX; if (cx < 0 || cx >= _cols) break; }
            else               { tMaxY += tDeltaY; cy += stepY; if (cy < 0 || cy >= _rows) break; }
        }
    }
}
