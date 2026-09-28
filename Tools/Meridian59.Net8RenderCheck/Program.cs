using System;using System.Collections.Generic;using System.IO;using System.Linq;
using Meridian59.Files;using Meridian59.Files.ROO;

// Two exhaustive checks on the renderer, both against what it actually
// draws rather than against a second copy of its arithmetic.
//
//   dotnet run --project . -- threads <resourceDir>
//   dotnet run --project . -- pick    <resourceDir>
//   dotnet run --project . -- all     <resourceDir>
static class RenderCheck
{
    /// <summary>The frame's silhouette in a colour the room cannot produce.</summary>
    static Tex Tag(Meridian59.Files.BGF.BgfFile bgf, int frame)
    {
        Tex t = Tex.FromSprite(bgf, frame);
        if (t == null) return null;
        for (int i=0;i<t.P.Length;i++)
            if ((t.P[i] >> 24) != 0) t.P[i] = 0xFFFF00FFu;
        return t;
    }


    static int Main(string[] a)
    {
        string mode = a.Length > 0 ? a[0].ToLowerInvariant() : "all";
        string dir  = a.Length > 1 ? a[1] : "/tmp/res";
        int bad = 0;
        if (mode == "threads" || mode == "all") bad += Threads(dir);
        if (mode == "pick"    || mode == "all") bad += Pick(dir);
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Threaded output must equal single-threaded output exactly. Columns
    /// are independent, so this should hold; it is checked rather than
    /// assumed because the scratch state that made it not hold was a field.
    /// </summary>

    static int Threads(string dir)
    {
        
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        var rooms = Directory.GetFiles(dir, "*.roo").OrderBy(x=>x).ToArray();
        Console.WriteLine($"{rooms.Length} rooms, {System.Environment.ProcessorCount} cores");

        const int W=480, H=270;
        int ok=0, diff=0, skip=0;
        var bad = new List<string>();
        foreach (string path in rooms)
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); }
            catch { skip++; continue; }
            if (roo.Walls.Count == 0) { skip++; continue; }

            var leaves = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3).ToList();
            if (leaves.Count == 0) { skip++; continue; }
            var big = leaves.OrderByDescending(l=>{double s=0;var v=l.Vertices;
                for(int i=0,j=v.Count-1;i<v.Count;j=i++) s+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                return Math.Abs(s*.5);}).First();
            float cx=big.Vertices.Average(v=>(float)v.X), cy=big.Vertices.Average(v=>(float)v.Y);

            var tex = new TexCache(rm);
            var r = new Renderer(roo, tex);
            float cz = M59Geo.FloorXY(r.SectorAtPoint(cx,cy)) + Renderer.EyeHeight;

            var one = new uint[W*H]; var many = new uint[W*H];
            bool roomBad = false;
            // Several headings: a single view can hide a band boundary bug.
            for (int k=0;k<8 && !roomBad;k++)
            {
                float ang = k * MathF.PI / 4f;
                r.Threaded = false; Array.Clear(one); int s1 = r.Render(one, W,H, cx,cy,cz, ang);
                r.Threaded = true;  Array.Clear(many); int s2 = r.Render(many, W,H, cx,cy,cz, ang);
                if (s1 != s2) roomBad = true;
                else for (int i=0;i<one.Length;i++) if (one[i]!=many[i]) { roomBad = true; break; }
            }
            if (roomBad) { diff++; bad.Add(Path.GetFileName(path)); } else ok++;
        }
        Console.WriteLine($"identical {ok}   DIFFERENT {diff}   skipped {skip}");
        foreach (var b in bad.Take(20)) Console.WriteLine("  differs: " + b);
        return diff;
    }

    /// <summary>
    /// Every pixel a sprite painted must be pickable, and no pixel it did
    /// not paint may pick it.
    /// </summary>
    static int Pick(string dir)
    {
        
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        string[] sprites = { "duskrat.bgf", "Knight.bgf", "cyclops.bgf" };
        string[] rooms = { "barinn.roo", "kc4.roo", "dvalley1.roo" };
        const int W=640, H=360;

        int checkedPx=0, missed=0, phantom=0, scenes=0;

        foreach (string room in rooms)
        foreach (string sprName in sprites)
        {
            var roo = new RooFile(Path.Combine(dir, room)); roo.ResolveResources(rm);
            var bgf = rm.GetObject(sprName);
            if (bgf == null) continue;

            var r = new Renderer(roo, new TexCache(rm));
            var big = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
                .OrderByDescending(l=>{double s=0;var v=l.Vertices;
                    for(int i=0,j=v.Count-1;i<v.Count;j=i++) s+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                    return Math.Abs(s*.5);}).First();
            float cx=big.Vertices.Average(v=>(float)v.X), cy=big.Vertices.Average(v=>(float)v.Y);
            float cz=M59Geo.FloorXY(r.SectorAtPoint(cx,cy))+Renderer.EyeHeight;

            for (int k=0;k<4;k++)
            {
                float ang = k * MathF.PI / 2f;
                // Bare room first: this is the mask's background.
                r.Sprites.Clear();
                var bare = new uint[W*H];
                r.Render(bare, W,H, cx,cy,cz, ang);

                // Scatter sprites, some of them through walls on purpose.
                var rng = new Random(k*97 + room.Length*13 + sprName.Length);
                for (int i=0;i<10;i++)
                {
                    float d = 600f + (float)rng.NextDouble()*9000f;
                    float t = ang + ((float)rng.NextDouble()-0.5f)*1.2f;
                    float sx = cx + MathF.Cos(t)*d, sy = cy + MathF.Sin(t)*d;
                    var sec = r.SectorAtPoint(sx, sy);
                    // Magenta stand-in with the real frame's alpha: comparing
                    // colours against a bare render counted a sprite pixel as
                    // unpainted whenever it happened to match the wall behind
                    // it. Shade scales channels, so G==0 and R==B survive it
                    // and the mask is exact.
                    var tex = Tag(bgf, rng.Next(0, bgf.Frames.Count));
                    if (tex == null) continue;
                    r.Sprites.Add(new Renderer.Sprite {
                        X=sx, Y=sy, BaseZ = sec!=null ? M59Geo.FloorXY(sec) : cz-Renderer.EyeHeight,
                        Height=600f, Texture=tex, Tag = i });
                }

                var with = new uint[W*H];
                r.Render(with, W,H, cx,cy,cz, ang);
                scenes++;

                for (int y=0;y<H;y++)
                for (int x=0;x<W;x++)
                {
                    uint c = with[y*W+x];
                    bool painted = ((c >> 8) & 0xFF) == 0
                                && ((c >> 16) & 0xFF) == (c & 0xFF)
                                && (c & 0xFF) != 0
                                && bare[y*W+x] != c;
                    var hit = r.Pick(x,y,W,H, cx,cy,cz, ang);
                    checkedPx++;
                    if (painted && hit == null) missed++;
                    else if (!painted && hit != null) phantom++;
                }
            }
        }
        Console.WriteLine($"{scenes} scenes, {checkedPx} pixels");
        Console.WriteLine($"  painted but not pickable : {missed}");
        Console.WriteLine($"  pickable but not painted : {phantom}");
        Console.WriteLine(missed==0 && phantom==0 ? "OK" : "MISMATCH");
        return missed + phantom;
    }
}
