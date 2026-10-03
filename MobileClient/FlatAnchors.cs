using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Meridian59.Files.ROO;

/// <summary>
/// Where each floor and ceiling texture starts.
///
/// The game does not anchor a flat at the world origin. RooSubSector's
/// UpdateVertexUV measures a leaf's texture coordinates from the leaf's
/// own top-left corner:
///
///     uv.X = |vertex.Y - top| - (TextureY &lt;&lt; 4)
///     uv.Y = |vertex.X - left| - (TextureX &lt;&lt; 4)
///     uv *= 1/1024
///
/// where <c>left</c> and <c>top</c> start at zero and are only ever
/// lowered by a vertex, so they are zero unless the leaf reaches into
/// negative coordinates. That is why anchoring at the origin looks right
/// nearly everywhere: for a leaf whose vertices are all positive the two
/// are the same arithmetic. Measured over the 362 rooms, 8573 leaves of
/// 162787 anchor elsewhere, and 5404 of those - 3.32% of all leaves, in
/// 188 rooms - move by a fraction of a texture, which is the only kind
/// that shows. A whole texture's shift on a tiling texture is no shift.
///
/// Only the leaves that differ are stored, in a grid over their own
/// bounding box, so rooms that are entirely in positive coordinates cost
/// one comparison per pixel and nothing else.
/// </summary>
public sealed class FlatAnchors
{
    struct Leaf
    {
        public float Left, Top;
        public float MinX, MinY, MaxX, MaxY;
        public float[] Xs, Ys;
    }

    readonly Leaf[] _leaves;
    readonly List<int>[] _cells;
    readonly float _minX, _minY, _maxX, _maxY, _cell;
    readonly int _cols, _rows;

    public int Count => _leaves.Length;
    public bool Empty => _leaves.Length == 0;

    public FlatAnchors(RooFile roo, float targetCell = 1024f)
    {
        var found = new List<Leaf>();

        foreach (RooSubSector leaf in roo.BSPTreeLeaves)
        {
            if (leaf.Vertices == null || leaf.Vertices.Count < 3) continue;

            // The library's own arithmetic, including the truncation to
            // int and the fact that a positive vertex never moves these.
            float left = 0f, top = 0f;
            foreach (var v in leaf.Vertices)
            {
                if (v.X < left) left = (int)v.X;
                if (v.Y < top) top = (int)v.Y;
            }
            if (left == 0f && top == 0f) continue;

            int n = leaf.Vertices.Count;
            var l = new Leaf
            {
                Left = left, Top = top,
                Xs = new float[n], Ys = new float[n],
                MinX = float.MaxValue, MinY = float.MaxValue,
                MaxX = float.MinValue, MaxY = float.MinValue,
            };
            for (int i = 0; i < n; i++)
            {
                l.Xs[i] = leaf.Vertices[i].X;
                l.Ys[i] = leaf.Vertices[i].Y;
                l.MinX = MathF.Min(l.MinX, l.Xs[i]); l.MaxX = MathF.Max(l.MaxX, l.Xs[i]);
                l.MinY = MathF.Min(l.MinY, l.Ys[i]); l.MaxY = MathF.Max(l.MaxY, l.Ys[i]);
            }
            found.Add(l);
        }

        _leaves = found.ToArray();
        if (_leaves.Length == 0)
        {
            _cells = Array.Empty<List<int>>();
            _cell = 1f; _cols = _rows = 0;
            return;
        }

        _minX = _minY = float.MaxValue; _maxX = _maxY = float.MinValue;
        foreach (Leaf l in _leaves)
        {
            _minX = MathF.Min(_minX, l.MinX); _maxX = MathF.Max(_maxX, l.MaxX);
            _minY = MathF.Min(_minY, l.MinY); _maxY = MathF.Max(_maxY, l.MaxY);
        }

        _cell = MathF.Max(256f, targetCell);
        _cols = Math.Max(1, (int)((_maxX - _minX) / _cell) + 1);
        _rows = Math.Max(1, (int)((_maxY - _minY) / _cell) + 1);
        _cells = new List<int>[_cols * _rows];

        for (int i = 0; i < _leaves.Length; i++)
        {
            Leaf l = _leaves[i];
            int x0 = Col(l.MinX), x1 = Col(l.MaxX), y0 = Row(l.MinY), y1 = Row(l.MaxY);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int k = y * _cols + x;
                    (_cells[k] ??= new List<int>(4)).Add(i);
                }
        }
    }

    int Col(float x) => Math.Clamp((int)((x - _minX) / _cell), 0, _cols - 1);
    int Row(float y) => Math.Clamp((int)((y - _minY) / _cell), 0, _rows - 1);

    /// <summary>
    /// The corner a flat's texture starts from at this point. False - the
    /// common case, and the whole of most rooms - means the origin.
    /// </summary>
    public bool TryAnchor(float x, float y, out float left, out float top)
    {
        int ignored = -1;
        return TryAnchor(x, y, ref ignored, out left, out top);
    }

    /// <summary>
    /// The same, remembering which leaf answered last.
    ///
    /// The caller asks once per PIXEL and a floor span walks across one
    /// leaf at a time, so the leaf that answered the previous pixel
    /// answers this one nearly always. Testing it first - its bounding
    /// box, then <see cref="Inside"/> - skips the grid lookup and the
    /// cell's whole candidate list. In the worst room in the game this
    /// table is 60% of the frame and the memo takes a tenth off the
    /// whole render.
    ///
    /// It is a HINT and nothing more: a miss falls through to the same
    /// search, and the answer is the same leaf either way because the
    /// BSP's leaves partition the room and no point lies in two of them.
    /// Checked rather than argued - 362 rooms x 8 headings byte-identical,
    /// single-threaded and threaded.
    ///
    /// <paramref name="memo"/> therefore belongs to the CALLER, not to
    /// this object: the renderer splits its columns across cores and a
    /// memo shared between them is a torn read away from pairing one
    /// leaf's index with another's polygon. It lives in the renderer's
    /// per-band scratch for that reason.
    ///
    /// Before any of that, the point is looked up in the SETTLED grid
    /// (see <see cref="Settle"/>): a fine grid whose cells each know
    /// whether every point in them has the same answer - one leaf, or
    /// none - and only a cell that an edge runs through reaches the
    /// polygon test at all. In badland1 that is one pixel in ten.
    /// </summary>
    public bool TryAnchor(float x, float y, ref int memo, out float left, out float top)
    {
        left = top = 0f;
        if (_leaves.Length == 0) return false;
        // Written so that a NaN fails: the search below would have found
        // nothing for one, and the settled grid must say the same.
        if (!(x >= _minX && x <= _maxX && y >= _minY && y <= _maxY)) return false;

        Settled fine = _fine ?? Settle();
        int cols = fine.Cols;
        int fx = (int)((x - _minX) * fine.Inv), fy = (int)((y - _minY) * fine.Inv);
        if (fx >= cols) fx = cols - 1;
        if (fy >= fine.Rows) fy = fine.Rows - 1;
        int k = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(fine.Cells), fy * cols + fx);
        if (k >= 0)
        {
            ref Leaf s = ref _leaves[k];
            left = s.Left; top = s.Top; memo = k;
            return true;
        }
        if (k == Nothing) return false;

        if ((uint)memo < (uint)_leaves.Length)
        {
            ref Leaf m = ref _leaves[memo];
            if (x >= m.MinX && x <= m.MaxX && y >= m.MinY && y <= m.MaxY && Inside(ref m, x, y))
            { left = m.Left; top = m.Top; return true; }
        }

        List<int> here = _cells[Row(y) * _cols + Col(x)];
        if (here == null) return false;

        for (int c = 0; c < here.Count; c++)
        {
            int i = here[c];
            ref Leaf l = ref _leaves[i];
            if (x < l.MinX || x > l.MaxX || y < l.MinY || y > l.MaxY) continue;
            if (!Inside(ref l, x, y)) continue;
            left = l.Left; top = l.Top;
            memo = i;
            return true;
        }
        return false;
    }

    /// <summary>
    /// What a span-walking caller remembers between two pixels: the leaf
    /// memo <see cref="TryAnchor(float,float,ref int,out float,out float)"/>
    /// keeps, and the SETTLED CELL the last pixel fell in with that
    /// cell's answer. One per band, next to the memo it extends - see
    /// Renderer.Scratch for why it cannot live on this object.
    /// </summary>
    public struct Cursor
    {
        public int Memo;
        /// <summary>The fine cell the last pixel was in; -1 before any.</summary>
        public int CellX, CellY;
        /// <summary>That cell's entry (a leaf index, or Nothing); never Mixed.</summary>
        public int Cell;
        public float Left, Top;
        public static Cursor Start => new Cursor { Memo = -1, CellX = -1, CellY = -1, Cell = Mixed };
    }

    /// <summary>
    /// <see cref="TryAnchor(float,float,ref int,out float,out float)"/>
    /// with the cell remembered as well as the leaf.
    ///
    /// A floor span's pixels walk a line through the room a few units
    /// apart, and the settled grid's cells are hundreds of units across,
    /// so pixel after pixel lands in the cell the one before did. A
    /// settled cell's answer is a function of the cell alone - that is
    /// what settled MEANS, see <see cref="Settle"/> - so when the cell
    /// is the one just asked and it was settled, the answer is the one
    /// just given, and the grid, the leaf table and their cache lines
    /// are not touched again. The memo is set exactly as the per-pixel
    /// lookup set it (to the leaf on a hit, untouched on Nothing), so a
    /// later mixed cell sees the same hint it would have. A mixed cell
    /// is never remembered: every pixel of one runs the full search,
    /// as before. Byte-identical by construction and held so by the
    /// golden frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAnchor(float x, float y, ref Cursor c, out float left, out float top)
    {
        // Written so that a NaN fails: the search below would have found
        // nothing for one, and the settled grid must say the same.
        if (!(x >= _minX && x <= _maxX && y >= _minY && y <= _maxY)) { left = top = 0f; return false; }
        Settled fine = _fine;
        if (fine == null) return TryAnchorSlow(x, y, ref c, out left, out top);
        int fx = (int)((x - _minX) * fine.Inv), fy = (int)((y - _minY) * fine.Inv);
        if (fx >= fine.Cols) fx = fine.Cols - 1;
        if (fy >= fine.Rows) fy = fine.Rows - 1;
        if (fx == c.CellX && fy == c.CellY)
        {
            // The same settled cell as the last pixel: its answer.
            left = c.Left; top = c.Top;
            if (c.Cell >= 0) { c.Memo = c.Cell; return true; }
            return false;
        }
        return TryAnchorCell(x, y, fx, fy, fine, ref c, out left, out top);
    }

    /// <summary>The settled grid not yet built: build it, then look up.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    bool TryAnchorSlow(float x, float y, ref Cursor c, out float left, out float top)
    {
        left = top = 0f;
        if (_leaves.Length == 0) return false;
        Settled fine = Settle();
        int fx = (int)((x - _minX) * fine.Inv), fy = (int)((y - _minY) * fine.Inv);
        if (fx >= fine.Cols) fx = fine.Cols - 1;
        if (fy >= fine.Rows) fy = fine.Rows - 1;
        return TryAnchorCell(x, y, fx, fy, fine, ref c, out left, out top);
    }

    /// <summary>
    /// A pixel in a cell other than the cursor's: the per-pixel lookup's
    /// own steps from the cell read on, remembering the cell when it is
    /// settled. A settled cell - a leaf, or nothing - is answered here;
    /// a mixed one goes to <see cref="TryAnchorMixed"/>, which is the
    /// old search and is kept out of line so this stays small enough to
    /// inline into the span loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryAnchorCell(float x, float y, int fx, int fy, Settled fine, ref Cursor c, out float left, out float top)
    {
        int k = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(fine.Cells), fy * fine.Cols + fx);
        if (k >= 0)
        {
            // A leaf index the grid was built from, so in range.
            ref Leaf s = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_leaves), k);
            left = s.Left; top = s.Top; c.Memo = k;
            c.CellX = fx; c.CellY = fy; c.Cell = k; c.Left = left; c.Top = top;
            return true;
        }
        if (k == Nothing)
        {
            left = top = 0f;
            c.CellX = fx; c.CellY = fy; c.Cell = Nothing; c.Left = 0f; c.Top = 0f;
            return false;
        }
        return TryAnchorMixed(x, y, ref c, out left, out top);
    }

    /// <summary>
    /// The search a mixed cell needs, as
    /// <see cref="TryAnchor(float,float,ref int,out float,out float)"/>
    /// runs it from its memo test down. The cell is forgotten so the
    /// next pixel in it searches too.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    bool TryAnchorMixed(float x, float y, ref Cursor c, out float left, out float top)
    {
        left = top = 0f;
        c.CellX = -1; c.CellY = -1; c.Cell = Mixed;

        int memo = c.Memo;
        if ((uint)memo < (uint)_leaves.Length)
        {
            ref Leaf m = ref _leaves[memo];
            if (x >= m.MinX && x <= m.MaxX && y >= m.MinY && y <= m.MaxY && Inside(ref m, x, y))
            { left = m.Left; top = m.Top; return true; }
        }

        List<int> here = _cells[Row(y) * _cols + Col(x)];
        if (here == null) return false;

        for (int i0 = 0; i0 < here.Count; i0++)
        {
            int i = here[i0];
            ref Leaf l = ref _leaves[i];
            if (x < l.MinX || x > l.MaxX || y < l.MinY || y > l.MaxY) continue;
            if (!Inside(ref l, x, y)) continue;
            left = l.Left; top = l.Top;
            c.Memo = i;
            return true;
        }
        return false;
    }

    /// <summary>Crossing count. Leaves are convex, but not assumed to be.</summary>
    static bool Inside(ref Leaf l, float x, float y)
    {
        bool inside = false;
        float[] xs = l.Xs, ys = l.Ys;
        int n = xs.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = xs[i], yi = ys[i], xj = xs[j], yj = ys[j];
            if ((yi > y) != (yj > y) &&
                x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    // ----------------------------------------------------------------
    // The settled grid.
    //
    // The polygon test above is exact about what it does and that is
    // the whole trouble with it: every floor pixel of an anchored room
    // pays four comparisons, a leaf's edges and a divide to learn which
    // leaf it is in, and nearly every one of them is nowhere near an
    // edge. In badland1 the anchored leaves average nine thousand units
    // across, and the test was a third of the frame.
    //
    // So the room is cut into cells of a few hundred units, and each
    // cell is SETTLED once, when the table is first asked: either one
    // leaf contains the whole cell, or no leaf touches it, or an edge
    // runs through it. The first two kinds answer from the cell alone.
    // Only the third - the cells an edge crosses, one in ten here -
    // reaches the search that used to run for every pixel.
    //
    // WHY THE ANSWER IS THE SAME BIT FOR BIT. The search's result is a
    // function of the point and nothing else, and away from an edge it
    // is the geometric truth: its only inexact step is the x against
    // the edge's x at that height, a float expression whose error over
    // this game's coordinates is under a tenth of a unit, so it can
    // only disagree with geometry within a tenth of a unit of an edge.
    // A cell is settled "inside L" only when the cell GROWN BY A MARGIN
    // of two units lies inside L, with L checked convex so that its
    // four corners inside means all of it is, in doubles; and settled
    // "nothing" only when no leaf meets the grown cell. Every point of
    // such a cell is then two units from any edge, and the search would
    // have said the same. The margin also covers the point landing in
    // the neighbouring cell through the rounding of (x - minX) * inv,
    // which at this grid's size is a quarter of a unit at the most. A
    // leaf that is not convex, or two leaves that overlap, or an edge
    // within the margin, make the cell "mixed" and the old path runs.
    //
    // Built lazily, under a lock, published through one reference: the
    // offline checks build a Renderer, and so one of these, dozens of
    // times a room for a sector lookup that never asks it.
    // ----------------------------------------------------------------

    const short Mixed = -1, Nothing = -2;
    const float Margin = 2f;
    const int FineCap = 32768;

    /// <summary>
    /// The grid and its shape, one object so a reader that sees the
    /// reference sees the dimensions that go with it - on the phone's
    /// weak memory ordering as well as on the desktop's.
    /// </summary>
    sealed class Settled
    {
        public short[] Cells;
        public float Inv;
        public int Cols, Rows;
    }

    Settled _fine;
    readonly object _fineGate = new object();

    Settled Settle()
    {
        Settled have = _fine;
        if (have != null) return have;
        lock (_fineGate)
        {
            if (_fine != null) return _fine;

            float w = MathF.Max(1f, _maxX - _minX), h = MathF.Max(1f, _maxY - _minY);
            // Cells of 256 units, or larger if that would need more than
            // the cap; always a power of two so the inverse is exact.
            float cell = 256f;
            while ((long)(w / cell + 1) * (long)(h / cell + 1) > FineCap) cell *= 2f;
            int cols = (int)(w / cell) + 1, rows = (int)(h / cell) + 1;
            var grid = new short[cols * rows];
            bool indexable = _leaves.Length <= short.MaxValue;
            var convex = new bool[_leaves.Length];
            for (int i = 0; i < _leaves.Length; i++) convex[i] = IsConvex(ref _leaves[i]);

            for (int cy = 0; cy < rows; cy++)
                for (int cx = 0; cx < cols; cx++)
                {
                    // The grown cell, in doubles.
                    double x0 = _minX + cx * (double)cell - Margin, x1 = _minX + (cx + 1) * (double)cell + Margin;
                    double y0 = _minY + cy * (double)cell - Margin, y1 = _minY + (cy + 1) * (double)cell + Margin;
                    int owner = -1; bool mixed = false;
                    // Candidates from the coarse grid's cells under this one.
                    int gx0 = ColD(x0), gx1 = ColD(x1), gy0 = RowD(y0), gy1 = RowD(y1);
                    for (int gy = gy0; gy <= gy1 && !mixed; gy++)
                        for (int gx = gx0; gx <= gx1 && !mixed; gx++)
                        {
                            List<int> here = _cells[gy * _cols + gx];
                            if (here == null) continue;
                            foreach (int i in here)
                            {
                                ref Leaf l = ref _leaves[i];
                                if (l.MaxX < x0 || l.MinX > x1 || l.MaxY < y0 || l.MinY > y1) continue;
                                if (i == owner) continue;
                                int rel = Relate(ref l, convex[i], x0, y0, x1, y1);
                                if (rel == 0) continue;                 // apart
                                if (rel == 1 && owner < 0 && indexable) { owner = i; continue; }
                                mixed = true; break;                    // crosses, second owner, or not convex
                            }
                        }
                    grid[cy * cols + cx] = mixed ? Mixed : owner >= 0 ? (short)owner : Nothing;
                }

            var built = new Settled { Cells = grid, Inv = 1f / cell, Cols = cols, Rows = rows };
            Volatile.Write(ref _fine, built);
            return built;
        }
    }

    int ColD(double x) => Math.Clamp((int)((x - _minX) / _cell), 0, _cols - 1);
    int RowD(double y) => Math.Clamp((int)((y - _minY) / _cell), 0, _rows - 1);

    /// <summary>
    /// Strictly convex and simple enough to trust: every consecutive
    /// cross product has the same sign and none is zero.
    /// </summary>
    static bool IsConvex(ref Leaf l)
    {
        float[] xs = l.Xs, ys = l.Ys;
        int n = xs.Length;
        if (n < 3) return false;
        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n, k = (i + 2) % n;
            double cx = ((double)xs[j] - xs[i]) * ((double)ys[k] - ys[j])
                      - ((double)ys[j] - ys[i]) * ((double)xs[k] - xs[j]);
            if (cx == 0) return false;
            int s = cx > 0 ? 1 : -1;
            if (sign == 0) sign = s; else if (s != sign) return false;
        }
        return true;
    }

    /// <summary>
    /// How a leaf stands to a rectangle: 0 apart, 1 the rectangle is
    /// wholly inside the leaf, 2 anything else - including a leaf that
    /// is not convex, which is never trusted to contain anything.
    /// </summary>
    static int Relate(ref Leaf l, bool convex, double x0, double y0, double x1, double y1)
    {
        float[] xs = l.Xs, ys = l.Ys;
        int n = xs.Length;
        if (!convex) return 2;
        // Winding, so "inside" is a known sign of the edge cross product.
        double area = 0;
        for (int i = 0, j = n - 1; i < n; j = i++) area += ((double)xs[j] * ys[i]) - ((double)xs[i] * ys[j]);
        double inSign = area > 0 ? 1 : -1;

        bool allInside = true, separated = false;
        for (int i = 0, j = n - 1; i < n && !separated; j = i++)
        {
            double ex = (double)xs[i] - xs[j], ey = (double)ys[i] - ys[j];
            int outCount = 0;
            for (int c = 0; c < 4; c++)
            {
                double px = (c & 1) == 0 ? x0 : x1, py = (c & 2) == 0 ? y0 : y1;
                double cr = (ex * (py - ys[j]) - ey * (px - xs[j])) * inSign;
                if (cr <= 0) { allInside = false; outCount++; }
            }
            if (outCount == 4) separated = true;
        }
        if (separated) return 0;
        if (allInside) return 1;
        // Not separated by any edge of the leaf; try the rectangle's own
        // sides, which with the leaf's edges is a complete test for two
        // convex shapes.
        bool left = true, right = true, below = true, above = true;
        for (int i = 0; i < n; i++)
        {
            if (xs[i] >= x0) left = false;
            if (xs[i] <= x1) right = false;
            if (ys[i] >= y0) below = false;
            if (ys[i] <= y1) above = false;
        }
        return (left || right || below || above) ? 0 : 2;
    }
}
