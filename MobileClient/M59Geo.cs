using System;
using System.Collections.Generic;
using Meridian59.Drawing2D;
using Meridian59.Files;
using Meridian59.Files.BGF;
using Meridian59.Files.ROO;

/// <summary>
/// Unit conventions for Meridian 59 room geometry, established by
/// measurement rather than documentation:
///
///  - Wall X/Y are in FINENESS units, 1024 per grid square.
///  - Sector heights and RooWall.ClientLength are in XY/16 units
///    (measured: xyLength / ClientLength == 16.0 exactly on every wall
///    checked in barinn.roo).
///  - One texture tiles across one grid square, so UV = xy / 1024.
/// </summary>
public static class M59Geo
{
    public const float Fineness = 1024f;
    public const float HeightToXY = 16f;

    public static float FloorXY(RooSector s) => s == null ? 0f : (float)s.FloorHeight * HeightToXY;
    public static float CeilingXY(RooSector s) => s == null ? 0f : (float)s.CeilingHeight * HeightToXY;

    /// <summary>Bounds-checked lookup of the 1-based sector and sidedef numbers.</summary>
    public static RooSector Sector(RooFile roo, int num)
        => (num >= 1 && num <= roo.Sectors.Count) ? roo.Sectors[num - 1] : null;

    public static RooSideDef Side(RooFile roo, int num)
        => (num >= 1 && num <= roo.SideDefs.Count) ? roo.SideDefs[num - 1] : null;
}

/// <summary>
/// A decoded BGF frame as straight RGB, wrapping on sample, with a mip
/// chain.
///
/// Point-sampling a 128x128 stone texture across a floor at a grazing
/// angle aliases badly - it showed up as radial streaking across ceilings.
/// Each level is a box filter of the one above and the sampler picks a
/// level from how much world space one screen pixel covers, which is what
/// mipmapping is for.
/// </summary>
public sealed class Tex
{
    public int W, H;
    public uint[] P;

    /// <summary>Level 0 is P; each subsequent level is half size.</summary>
    int[] _lw, _lh;
    uint[][] _levels;

    public int LevelCount => _levels?.Length ?? 1;

    public uint Sample(float u, float v)
    {
        int x = (int)((u - MathF.Floor(u)) * W);
        int y = (int)((v - MathF.Floor(v)) * H);
        if (x < 0) x = 0; else if (x >= W) x = W - 1;
        if (y < 0) y = 0; else if (y >= H) y = H - 1;
        return P[y * W + x];
    }

    /// <summary>
    /// Samples with an explicit level of detail. <paramref name="texelsPerPixel"/>
    /// is roughly how many texels one screen pixel spans; 1 means level 0.
    /// </summary>
    public uint Sample(float u, float v, float texelsPerPixel)
    {
        if (_levels == null || texelsPerPixel <= 1f) return Sample(u, v);

        int lod = 0;
        float t = texelsPerPixel;
        while (t >= 2f && lod < _levels.Length - 1) { t *= 0.5f; lod++; }

        uint[] lp = _levels[lod];
        int lw = _lw[lod], lh = _lh[lod];
        int x = (int)((u - MathF.Floor(u)) * lw);
        int y = (int)((v - MathF.Floor(v)) * lh);
        if (x < 0) x = 0; else if (x >= lw) x = lw - 1;
        if (y < 0) y = 0; else if (y >= lh) y = lh - 1;
        return lp[y * lw + x];
    }

    void BuildMips()
    {
        var lv = new List<uint[]> { P };
        var lw = new List<int> { W };
        var lh = new List<int> { H };
        int cw = W, ch = H;
        uint[] cur = P;
        while (cw > 4 && ch > 4)
        {
            int nw = cw >> 1, nh = ch >> 1;
            var next = new uint[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    uint a = cur[(y * 2) * cw + x * 2];
                    uint b = cur[(y * 2) * cw + x * 2 + 1];
                    uint c = cur[(y * 2 + 1) * cw + x * 2];
                    uint d = cur[(y * 2 + 1) * cw + x * 2 + 1];
                    uint r = ((a >> 16 & 0xFF) + (b >> 16 & 0xFF) + (c >> 16 & 0xFF) + (d >> 16 & 0xFF)) >> 2;
                    uint g = ((a >> 8 & 0xFF) + (b >> 8 & 0xFF) + (c >> 8 & 0xFF) + (d >> 8 & 0xFF)) >> 2;
                    uint bl = ((a & 0xFF) + (b & 0xFF) + (c & 0xFF) + (d & 0xFF)) >> 2;
                    next[y * nw + x] = 0xFF000000u | (r << 16) | (g << 8) | bl;
                }
            lv.Add(next); lw.Add(nw); lh.Add(nh);
            cur = next; cw = nw; ch = nh;
        }
        _levels = lv.ToArray(); _lw = lw.ToArray(); _lh = lh.ToArray();
    }

    public static Tex From(BgfFile bgf, int frame = 0)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        if (frame < 0 || frame >= bgf.Frames.Count) frame = 0;
        BgfBitmap f = bgf.Frames[frame];
        byte[] idx;
        try { idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData; }
        catch { return null; }
        int w = (int)f.Width, h = (int)f.Height;
        if (idx == null || w <= 0 || h <= 0 || idx.Length < w * h) return null;
        uint[] pal = ColorTransformation.DefaultPalette;
        var p = new uint[w * h];
        for (int i = 0; i < w * h; i++) p[i] = pal[idx[i]] | 0xFF000000u;
        var t = new Tex { W = w, H = h, P = p };
        t.BuildMips();
        return t;
    }

    /// <summary>
    /// Like <see cref="From"/> but keeps transparency. Palette index 254 is
    /// Meridian's transparent colour and already carries alpha 0, so object
    /// sprites must not have alpha forced opaque the way wall and floor
    /// textures are. No mip chain: averaging across transparent texels
    /// bleeds the cyan key into the edges.
    /// </summary>
    public static Tex FromSprite(BgfFile bgf, int frame = 0)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        if (frame < 0 || frame >= bgf.Frames.Count) frame = 0;
        BgfBitmap f = bgf.Frames[frame];
        byte[] idx;
        try { idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData; }
        catch { return null; }
        int w = (int)f.Width, h = (int)f.Height;
        if (idx == null || w <= 0 || h <= 0 || idx.Length < w * h) return null;
        uint[] pal = ColorTransformation.DefaultPalette;
        var p = new uint[w * h];
        for (int i = 0; i < w * h; i++) p[i] = pal[idx[i]];   // alpha preserved
        return new Tex { W = w, H = h, P = p };
    }
}

/// <summary>Caches room textures by grd number.</summary>
public sealed class TexCache
{
    readonly ResourceManager _rm;
    readonly Dictionary<ushort, Tex> _c = new Dictionary<ushort, Tex>();
    public TexCache(ResourceManager rm) { _rm = rm; }
    public int Count => _c.Count;

    public Tex Get(ushort num)
    {
        if (num == 0) return null;
        if (_c.TryGetValue(num, out Tex t)) return t;
        Tex built = null;
        try { built = Tex.From(_rm.GetRoomTexture(num)); } catch { }
        _c[num] = built;
        return built;
    }
}
