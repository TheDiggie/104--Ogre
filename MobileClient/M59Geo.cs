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

    /// <summary>Sectors and sidedefs are 1-based and never wired up by the loader.</summary>
    public static RooSector Sector(RooFile roo, int num)
        => (num >= 1 && num <= roo.Sectors.Count) ? roo.Sectors[num - 1] : null;

    public static RooSideDef Side(RooFile roo, int num)
        => (num >= 1 && num <= roo.SideDefs.Count) ? roo.SideDefs[num - 1] : null;
}

/// <summary>A decoded BGF frame as straight RGB, wrapping on sample.</summary>
public sealed class Tex
{
    public int W, H;
    public uint[] P;

    public uint Sample(float u, float v)
    {
        int x = (int)((u - MathF.Floor(u)) * W);
        int y = (int)((v - MathF.Floor(v)) * H);
        if (x < 0) x = 0; else if (x >= W) x = W - 1;
        if (y < 0) y = 0; else if (y >= H) y = H - 1;
        return P[y * W + x];
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
