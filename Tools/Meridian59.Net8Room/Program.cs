using System;using System.IO;
using Meridian59.Files.ROO;

// Draws a ROO's wall geometry as a top-down floor plan PNG, using only
// the ported core library. A renderer needs this geometry to be correct
// and correctly oriented; a picture is the cheapest way to check.
//
//   dotnet run --project Tools/Meridian59.Net8Room -- <file.roo|dir> <outdir> [size]

static class Room
{
    const uint WALL_SOLID = 0xFF1A1A1A;   // blocks movement and sight
    const uint WALL_PASS  = 0xFF4A90D9;   // sector on both sides - a doorway or step
    const uint BG         = 0xFFF5F2EA;

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: <file.roo|dir> <outdir> [size]"); return 2; }
        string src = args[0], outDir = args[1];
        int size = args.Length > 2 ? int.Parse(args[2]) : 900;
        Directory.CreateDirectory(outDir);

        string[] files = Directory.Exists(src)
            ? Directory.GetFiles(src, "*.roo", SearchOption.AllDirectories)
            : new[] { src };

        int ok = 0, bad = 0;
        foreach (string f in files)
        {
            try { Draw(new RooFile(f), Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".png"), size); ok++; }
            catch (Exception e) { Console.WriteLine($"  ! {Path.GetFileName(f)}: {e.Message}"); bad++; }
        }
        Console.WriteLine($"{ok} room(s) drawn to {outDir}, {bad} failed");
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    static void Draw(RooFile roo, string path, int size)
    {
        if (roo.Walls.Count == 0) throw new Exception("no walls");

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (RooWall w in roo.Walls)
        {
            minX = Math.Min(minX, Math.Min(w.X1, w.X2)); maxX = Math.Max(maxX, Math.Max(w.X1, w.X2));
            minY = Math.Min(minY, Math.Min(w.Y1, w.Y2)); maxY = Math.Max(maxY, Math.Max(w.Y1, w.Y2));
        }

        const int pad = 16;
        double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
        double scale = (size - 2.0 * pad) / Math.Max(spanX, spanY);
        int w2 = (int)(spanX * scale) + 2 * pad, h2 = (int)(spanY * scale) + 2 * pad;

        var px = new uint[w2 * h2];
        for (int i = 0; i < px.Length; i++) px[i] = BG;

        foreach (RooWall wall in roo.Walls)
        {
            bool passable = wall.RightSectorNum != 0 && wall.LeftSectorNum != 0;
            Line(px, w2, h2,
                 // ROO x/y map straight onto image columns/rows - verified
                 // against the wiki map for dvalley1, which has an
                 // asymmetric notch and a distinctive crack to line up.
                 (int)((wall.X1 - minX) * scale) + pad,
                 (int)((wall.Y1 - minY) * scale) + pad,
                 (int)((wall.X2 - minX) * scale) + pad,
                 (int)((wall.Y2 - minY) * scale) + pad,
                 passable ? WALL_PASS : WALL_SOLID);
        }

        var rgba = new byte[w2 * h2 * 4];
        for (int i = 0; i < px.Length; i++)
        {
            uint c = px[i];
            rgba[i * 4] = (byte)(c >> 16); rgba[i * 4 + 1] = (byte)(c >> 8);
            rgba[i * 4 + 2] = (byte)c;     rgba[i * 4 + 3] = (byte)(c >> 24);
        }
        Png.Write(path, w2, h2, rgba);
    }

    static void Line(uint[] px, int w, int h, int x0, int y0, int x1, int y1, uint c)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h) px[y0 * w + x0] = c;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
