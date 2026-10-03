using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meridian59.Files.ROO;

/// <summary>
/// Uniform spatial grid over a room's walls, so a ray only tests the walls
/// in the cells it actually crosses instead of every wall in the room.
///
/// This is a filter, not a reordering: the walls it yields are a superset
/// of the ones a ray can possibly hit, and the caller still sorts by
/// distance. Output is therefore identical to testing every wall - which
/// is exactly how it was verified.
///
/// The cells are stored flat: one array of wall indices, cell after cell,
/// and one array of where each cell's run starts (<c>_start[k]</c> to
/// <c>_start[k + 1]</c>). A list a cell used to cost a reference, a
/// length and a bounds check per element, in memory scattered by the
/// allocator; now a column's whole walk reads two arrays forwards. The
/// walls come out in the same order they always did - cell by cell along
/// the ray, and within a cell by wall number - which matters because the
/// caller's sort is by distance and two walls at one distance keep the
/// order they arrived in.
/// </summary>
public sealed class WallGrid
{
    readonly float _minX, _minY, _cell;
    readonly int _cols, _rows;
    readonly int[] _start;     // _cols * _rows + 1 entries
    readonly int[] _ids;       // wall indices, cell by cell
    readonly RooWall[] _walls;
    public int Cols => _cols;
    public int Rows => _rows;
    public float CellSize => _cell;
    public int WallCount => _walls.Length;

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

        // Two passes: count each cell's walls, then lay them out. Walls
        // are visited by number so each cell's run is in wall order.
        int cellCount = _cols * _rows;
        var counts = new int[cellCount];
        var cx0s = new int[_walls.Length]; var cx1s = new int[_walls.Length];
        var cy0s = new int[_walls.Length]; var cy1s = new int[_walls.Length];
        for (int i = 0; i < _walls.Length; i++)
        {
            RooWall w = _walls[i];
            cx0s[i] = Clamp((int)((MathF.Min(w.X1, w.X2) - _minX) / _cell), 0, _cols - 1);
            cx1s[i] = Clamp((int)((MathF.Max(w.X1, w.X2) - _minX) / _cell), 0, _cols - 1);
            cy0s[i] = Clamp((int)((MathF.Min(w.Y1, w.Y2) - _minY) / _cell), 0, _rows - 1);
            cy1s[i] = Clamp((int)((MathF.Max(w.Y1, w.Y2) - _minY) / _cell), 0, _rows - 1);
            // Bounding box rather than an exact walk: a few extra candidates
            // cost far less than the walls this removes.
            for (int cy = cy0s[i]; cy <= cy1s[i]; cy++)
                for (int cx = cx0s[i]; cx <= cx1s[i]; cx++)
                    counts[cy * _cols + cx]++;
        }
        _start = new int[cellCount + 1];
        for (int k = 0; k < cellCount; k++) _start[k + 1] = _start[k] + counts[k];
        _ids = new int[_start[cellCount]];
        var fill = new int[cellCount];
        for (int i = 0; i < _walls.Length; i++)
            for (int cy = cy0s[i]; cy <= cy1s[i]; cy++)
                for (int cx = cx0s[i]; cx <= cx1s[i]; cx++)
                {
                    int k = cy * _cols + cx;
                    _ids[_start[k] + fill[k]++] = i;
                }
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>
    /// Appends every wall in a cell the ray crosses to <paramref name="outWalls"/>.
    /// Order is by cell along the ray, which is not the same as by distance -
    /// the caller sorts.
    ///
    /// The visit marker is passed in rather than held as a field so that
    /// several threads can traverse the same grid at once, each with its own
    /// stamp buffer. Size it to <see cref="WallCount"/>.
    /// </summary>
    public void Collect(float ox, float oy, float dx, float dy,
                        List<RooWall> outWalls, int[] stamp, ref int tick)
        => Collect(ox, oy, dx, dy, outWalls, null, stamp, ref tick);

    /// <summary>
    /// The same walk handing back wall INDICES - positions in the room's
    /// wall list, which is what <see cref="Wall"/> takes - so the caller
    /// can test a candidate against coordinates it keeps in flat arrays
    /// and touch the RooWall object only for the few that hit. Most
    /// candidates miss, and each one used to cost the load of a
    /// scattered object for its Num before its endpoints were read.
    /// </summary>
    public void Collect(float ox, float oy, float dx, float dy,
                        List<int> outIndices, int[] stamp, ref int tick)
        => Collect(ox, oy, dx, dy, null, outIndices, stamp, ref tick);

    /// <summary>The wall at an index <see cref="Collect(float,float,float,float,List{int},int[],ref int)"/> handed back.</summary>
    public RooWall Wall(int index) => _walls[index];

    void Collect(float ox, float oy, float dx, float dy,
                 List<RooWall> outWalls, List<int> outIndices, int[] stamp, ref int tick)
    {
        // The stamps are read through a bare reference below, so the
        // buffer's size is checked here once rather than per wall.
        if (stamp.Length < _walls.Length)
            throw new ArgumentException("stamp buffer smaller than WallCount", nameof(stamp));
        // The stamps say "seen this column" by holding the column's
        // tick, so a tick that came round again to a value the buffer
        // still held from its first time through would have hidden a
        // wall. Two billion columns is a few hours of play on one band;
        // the buffer is wiped and the count restarted before that.
        if (tick == int.MaxValue) { Array.Clear(stamp); tick = 0; }
        tick++;
        int t = tick;

        // Cell the ray starts in, clamped: a camera just outside the room's
        // bounds still gets a sensible starting cell. The divide by the
        // cell is kept as a divide: which cell a point two cells' edges
        // apart lands in decides which walls are offered, and a reciprocal
        // can round the other way at a boundary.
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

        ref int start0 = ref MemoryMarshal.GetArrayDataReference(_start);
        ref int ids0   = ref MemoryMarshal.GetArrayDataReference(_ids);
        ref int stamp0 = ref MemoryMarshal.GetArrayDataReference(stamp);
        RooWall[] walls = _walls;
        int cols = _cols, rows = _rows;

        int guard = cols + rows + 2;
        while (guard-- > 0)
        {
            int k = cy * cols + cx;
            int a = Unsafe.Add(ref start0, k), b = Unsafe.Add(ref start0, k + 1);
            for (int i = a; i < b; i++)
            {
                int wi = Unsafe.Add(ref ids0, i);
                ref int s = ref Unsafe.Add(ref stamp0, wi);
                if (s == t) continue;
                s = t;
                if (outIndices != null) outIndices.Add(wi);
                else outWalls.Add(walls[wi]);
            }

            if (tMaxX < tMaxY) { tMaxX += tDeltaX; cx += stepX; if (cx < 0 || cx >= cols) break; }
            else               { tMaxY += tDeltaY; cy += stepY; if (cy < 0 || cy >= rows) break; }
        }
    }
}
