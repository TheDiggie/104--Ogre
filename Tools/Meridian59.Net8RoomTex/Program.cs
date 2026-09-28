using System;using System.Collections.Generic;using System.IO;using System.Linq;
using Meridian59.Common;using Meridian59.Drawing2D;
using Meridian59.Files;using Meridian59.Files.BGF;using Meridian59.Files.ROO;

// Renders a room's floor with its REAL textures, using exactly the UV
// math RoomView.cs uses in Godot. Lets the texture pipeline be checked
// by eye without running Godot.
static class RoomTex
{
    const float FINENESS = 1024f;

    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.WriteLine("usage: <resourceDir> <room.roo> <out.png> [size]"); return 2; }
        string dir = a[0], room = a[1], outPath = a[2];
        int size = a.Length > 3 ? int.Parse(a[3]) : 900;

        var rm = new ResourceManager();
        rm.Init(dir, dir, dir, dir, dir, dir, dir);

        var roo = new RooFile(Path.Combine(dir, room));
        roo.ResolveResources(rm);

        float minX = roo.Walls.Min(w => (float)Math.Min(w.X1, w.X2));
        float maxX = roo.Walls.Max(w => (float)Math.Max(w.X1, w.X2));
        float minY = roo.Walls.Min(w => (float)Math.Min(w.Y1, w.Y2));
        float maxY = roo.Walls.Max(w => (float)Math.Max(w.Y1, w.Y2));

        const int pad = 16;
        float spanX = Math.Max(1f, maxX - minX), spanY = Math.Max(1f, maxY - minY);
        float fit = (size - 2f * pad) / Math.Max(spanX, spanY);
        int W = (int)(spanX * fit) + 2 * pad, H = (int)(spanY * fit) + 2 * pad;

        var px = new uint[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = 0xFF101012;

        var texCache = new Dictionary<ushort, Tex>();
        int leaves = 0, textured = 0, missing = 0;

        foreach (RooSubSector leaf in roo.BSPTreeLeaves)
        {
            Polygon poly = leaf.Vertices;
            if (poly == null || poly.Count < 3) continue;
            leaves++;

            // RooSubSector.Sector is never assigned by the loader, so
            // resolve it from the 1-based SectorNum ourselves.
            RooSector sec = (leaf.SectorNum >= 1 && leaf.SectorNum <= roo.Sectors.Count)
                ? roo.Sectors[leaf.SectorNum - 1] : null;
            ushort texNum = sec != null ? sec.FloorTexture : (ushort)0;
            Tex tex = GetTex(rm, texCache, texNum);
            if (tex == null) missing++; else textured++;

            float offX = sec != null ? sec.TextureX : 0f;
            float offY = sec != null ? sec.TextureY : 0f;

            var sx = new float[poly.Count]; var sy = new float[poly.Count];
            for (int i = 0; i < poly.Count; i++)
            {
                sx[i] = (poly[i].X - minX) * fit + pad;
                sy[i] = (poly[i].Y - minY) * fit + pad;
            }
            FillPoly(px, W, H, sx, sy, (fx, fy) =>
            {
                if (tex == null) return 0xFF2A2A30;
                float wx = (fx - pad) / fit + minX + offX;
                float wy = (fy - pad) / fit + minY + offY;
                // Axes swapped - Meridian's room textures run Y along
                // world X. See MobileClient/Renderer.cs.
                return tex.Sample(wy / FINENESS, wx / FINENESS);
            });
        }

        foreach (RooWall w in roo.Walls)
        {
            bool pass = w.RightSectorNum != 0 && w.LeftSectorNum != 0;
            Line(px, W, H,
                 (int)((w.X1 - minX) * fit) + pad, (int)((w.Y1 - minY) * fit) + pad,
                 (int)((w.X2 - minX) * fit) + pad, (int)((w.Y2 - minY) * fit) + pad,
                 pass ? 0xFF4A90D9 : 0xFF1A1A1A);
        }

        var rgba = new byte[W * H * 4];
        for (int i = 0; i < px.Length; i++)
        {
            uint c = px[i];
            rgba[i*4] = (byte)(c >> 16); rgba[i*4+1] = (byte)(c >> 8);
            rgba[i*4+2] = (byte)c;       rgba[i*4+3] = (byte)(c >> 24);
        }
        Png.Write(outPath, W, H, rgba);
        Console.WriteLine($"{Path.GetFileName(room)}: {leaves} leaves, {textured} textured, {missing} untextured, {texCache.Count(k => k.Value != null)} textures -> {outPath} ({W}x{H})");
        return 0;
    }

    class Tex
    {
        public int W, H; public uint[] P;
        public uint Sample(float u, float v)
        {
            int x = (int)Math.Floor((u - Math.Floor(u)) * W);
            int y = (int)Math.Floor((v - Math.Floor(v)) * H);
            if (x < 0) x = 0; if (x >= W) x = W - 1;
            if (y < 0) y = 0; if (y >= H) y = H - 1;
            return P[y * W + x];
        }
    }

    static Tex GetTex(ResourceManager rm, Dictionary<ushort, Tex> cache, ushort num)
    {
        if (num == 0) return null;
        if (cache.TryGetValue(num, out Tex t)) return t;
        Tex built = null;
        try
        {
            BgfFile b = rm.GetRoomTexture(num);
            if (b != null && b.Frames.Count > 0)
            {
                BgfBitmap f = b.Frames[0];
                byte[] idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData;
                int w = (int)f.Width, h = (int)f.Height;
                if (idx != null && idx.Length >= w * h && w > 0 && h > 0)
                {
                    uint[] pal = ColorTransformation.DefaultPalette;
                    var p = new uint[w * h];
                    for (int i = 0; i < w * h; i++) p[i] = pal[idx[i]] | 0xFF000000u;
                    built = new Tex { W = w, H = h, P = p };
                }
            }
        }
        catch (Exception e) { Console.WriteLine($"  ! grd{num:D5}: {e.GetType().Name}: {e.Message}"); }
        if (built == null) Console.WriteLine($"  ! grd{num:D5}: no texture built");
        cache[num] = built;
        return built;
    }

    static void FillPoly(uint[] px, int W, int H, float[] xs, float[] ys, Func<float,float,uint> shade)
    {
        float fminY = ys.Min(), fmaxY = ys.Max();
        int y0 = Math.Max(0, (int)Math.Floor(fminY)), y1 = Math.Min(H - 1, (int)Math.Ceiling(fmaxY));
        int n = xs.Length;
        var xints = new List<float>();
        for (int y = y0; y <= y1; y++)
        {
            float cy = y + 0.5f;
            xints.Clear();
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float yi = ys[i], yj = ys[j];
                if ((yi <= cy && yj > cy) || (yj <= cy && yi > cy))
                    xints.Add(xs[i] + (cy - yi) / (yj - yi) * (xs[j] - xs[i]));
            }
            if (xints.Count < 2) continue;
            xints.Sort();
            for (int k = 0; k + 1 < xints.Count; k += 2)
            {
                int xa = Math.Max(0, (int)Math.Ceiling(xints[k] - 0.5f));
                int xb = Math.Min(W - 1, (int)Math.Floor(xints[k + 1] - 0.5f));
                for (int x = xa; x <= xb; x++) px[y * W + x] = shade(x + 0.5f, cy);
            }
        }
    }

    static void Line(uint[] px, int w, int h, int x0, int y0, int x1, int y1, uint c)
    {
        int dx = Math.Abs(x1-x0), sx = x0<x1?1:-1, dy = -Math.Abs(y1-y0), sy = y0<y1?1:-1, err = dx+dy;
        while (true) {
            if (x0>=0&&x0<w&&y0>=0&&y0<h) px[y0*w+x0]=c;
            if (x0==x1&&y0==y1) break;
            int e2=2*err;
            if (e2>=dy){err+=dy;x0+=sx;}
            if (e2<=dx){err+=dx;y0+=sy;}
        }
    }
}
