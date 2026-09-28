using System;
using System.Collections.Generic;
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
        left = top = 0f;
        if (_leaves.Length == 0) return false;
        if (x < _minX || x > _maxX || y < _minY || y > _maxY) return false;

        List<int> here = _cells[Row(y) * _cols + Col(x)];
        if (here == null) return false;

        foreach (int i in here)
        {
            Leaf l = _leaves[i];
            if (x < l.MinX || x > l.MaxX || y < l.MinY || y > l.MaxY) continue;
            if (!Inside(l, x, y)) continue;
            left = l.Left; top = l.Top;
            return true;
        }
        return false;
    }

    /// <summary>Crossing count. Leaves are convex, but not assumed to be.</summary>
    static bool Inside(Leaf l, float x, float y)
    {
        bool inside = false;
        int n = l.Xs.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = l.Xs[i], yi = l.Ys[i], xj = l.Xs[j], yj = l.Ys[j];
            if ((yi > y) != (yj > y) &&
                x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
