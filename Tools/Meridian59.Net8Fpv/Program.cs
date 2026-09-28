using System;using System.IO;using System.Linq;
using Meridian59.Files;using Meridian59.Files.ROO;

// Renders a first-person view to PNG using the SAME Renderer the game
// uses, so what is checked here is what ships.
//
//   dotnet run --project . -- <resourceDir> <room.roo> <out.png> [x y angleDeg] [w h]
static class Fpv
{
    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.WriteLine("usage: <resourceDir> <room.roo> <out.png> [x y angleDeg] [w h]"); return 2; }
        string dir = a[0], roomFile = a[1], outPath = a[2];
        int W = a.Length > 7 ? int.Parse(a[6]) : 960;
        int H = a.Length > 7 ? int.Parse(a[7]) : 540;

        var rm = new ResourceManager();
        rm.Init(dir, dir, dir, dir, dir, dir, dir);
        var roo = new RooFile(Path.Combine(dir, roomFile));
        roo.ResolveResources(rm);
        var tc = new TexCache(rm);
        var r = new Renderer(roo, tc);

        float camX, camY, angle;
        if (a.Length >= 6)
        {
            camX = float.Parse(a[3]); camY = float.Parse(a[4]);
            angle = float.Parse(a[5]) * MathF.PI / 180f;
        }
        else
        {
            RooSubSector big = roo.BSPTreeLeaves
                .Where(l => l.Vertices != null && l.Vertices.Count >= 3)
                .OrderByDescending(l => {
                    double s = 0; var v = l.Vertices;
                    for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
                        s += (double)v[j].X * v[i].Y - (double)v[i].X * v[j].Y;
                    return Math.Abs(s * 0.5);
                }).FirstOrDefault();
            if (big == null) { Console.WriteLine("no usable leaf"); return 1; }
            camX = big.Vertices.Average(v => (float)v.X);
            camY = big.Vertices.Average(v => (float)v.Y);
            angle = 0f;
        }

        float camZ = M59Geo.FloorXY(r.SectorAtPoint(camX, camY)) + Renderer.EyeHeight;
        var px = new uint[W * H];
        int closed = r.Render(px, W, H, camX, camY, camZ, angle);

        var rgba = new byte[W * H * 4];
        for (int i = 0; i < px.Length; i++)
        {
            uint c = px[i];
            rgba[i*4] = (byte)(c >> 16); rgba[i*4+1] = (byte)(c >> 8);
            rgba[i*4+2] = (byte)c;       rgba[i*4+3] = 255;
        }
        Png.Write(outPath, W, H, rgba);
        Console.WriteLine($"{roomFile} @ ({camX:F0},{camY:F0}) {angle*180/MathF.PI:F0}deg  {closed}/{W} columns closed, {tc.Count} textures -> {outPath}");
        return 0;
    }
}
