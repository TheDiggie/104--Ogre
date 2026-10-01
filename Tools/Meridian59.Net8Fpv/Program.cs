using System;using System.Collections.Generic;using System.IO;using System.Linq;
using Meridian59.Files;using Meridian59.Files.ROO;

// Renders a first-person view to PNG using the SAME Renderer the game
// uses, so what is checked here is what ships.
//
//   dotnet run --project . -- <resourceDir> <room.roo> <out.png> [x y angleDeg] [w h]
static class Fpv
{
    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.WriteLine("usage: <resourceDir> <room.roo> <out.png> [x y angleDeg] [w h] [--sprite f.bgf] [--time s] [--solid] [--noflip]"); return 2; }

        // Options and their values are pulled out first: the positional
        // arguments below count on their own positions, and --time 3.0 used
        // to be read as the render width.
        string[] flags = a;
        var pos = new List<string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].StartsWith("--"))
            {
                if (a[i] == "--sprite" || a[i] == "--time" || a[i] == "--pitch" || a[i] == "--sky"
                    || a[i] == "--grass" || a[i] == "--bench") i++;
                if (a[i] == "--torch") i += 5;   // takes a value
                continue;
            }
            pos.Add(a[i]);
        }
        a = pos.ToArray();
        string dir = a[0], roomFile = a[1], outPath = a[2];
        int W = a.Length > 7 ? int.Parse(a[6]) : 960;
        int H = a.Length > 7 ? int.Parse(a[7]) : 540;

        var rm = new ResourceManager();
        rm.Init(dir, dir, dir, dir, dir, dir, dir);
        var roo = new RooFile(Path.Combine(dir, roomFile));
        roo.ResolveResources(rm);
        if (flags.Contains("--anchordbg"))
        {
            var fa = new FlatAnchors(roo);
            Console.WriteLine($"anchored leaves in this room: {fa.Count}");
            for (int i = 0; i < 5 && i + 3 < a.Length; i++) { }
        }
        var tc = new TexCache(rm);
        // The reference's default look: the replacement room textures,
        // when the player has them (see TexCache.RoomTextureDir).
        // --oldtex draws the original art instead, for comparing.
        if (!flags.Contains("--oldtex"))
        {
            tc.RoomTextureDir = TexCache.FindRoomTextures(dir);
            if (tc.RoomTextureDir != null) Console.WriteLine($"room textures from {tc.RoomTextureDir}");
        }
        var r = new Renderer(roo, tc);
        // --solid renders grates and railings as solid walls, which is what
        // this renderer did before it read WF_TRANSPARENT. For comparing.
        if (flags.Contains("--solid")) r.SeeThroughWalls = false;
        // --noflip ignores WF_BACKWARDS, for comparing.
        if (flags.Contains("--noflip")) r.HonourBackwards = false;
        // --worldflats anchors floors and ceilings at the world origin,
        // which is what this renderer did before it read
        // RooSubSector.UpdateVertexUV. For comparing.
        if (flags.Contains("--worldflats")) r.LeafAnchoredFlats = false;
        if (flags.Contains("--holes"))
        {
            // (one room; --holesall walks them all)
            int flagT=0, flagN=0, holeT=0, holeN=0, both=0, onlyFlag=0, onlyHole=0;
            foreach (var w in roo.Walls)
            foreach (var sd in new[]{ w.RightSide, w.LeftSide })
            {
                if (sd == null || sd.MiddleTexture == 0) continue;
                if (w.RightSide == null || w.LeftSide == null) continue;   // two-sided only
                bool fl = sd.Flags.IsTransparent && !sd.Flags.IsNoLookThrough;
                Tex mt = tc.Get(sd.MiddleTexture, 1);
                if (mt == null) continue;
                bool ho = mt.HasHoles;
                if (fl) flagT++; else flagN++;
                if (ho) holeT++; else holeN++;
                if (fl && ho) both++;
                else if (fl) onlyFlag++;
                else if (ho) onlyHole++;
            }
            Console.WriteLine($"  two-sided middles: flag says see-through {flagT}, solid {flagN}");
            Console.WriteLine($"                     art has holes {holeT}, none {holeN}");
            Console.WriteLine($"    agree {both}, flag only {onlyFlag}, holes only {onlyHole}");
        }

        if (flags.Contains("--secdbg"))
        {
            foreach (var g in roo.Sectors.GroupBy(x => x.CeilingTexture).OrderByDescending(x => x.Count()).Take(8))
                Console.WriteLine($"  ceiling {g.Key} x{g.Count()} {(g.First().ResourceCeiling == null ? "MISSING" : "ok")}");
            foreach (var g in roo.Sectors.GroupBy(x => x.FloorTexture).OrderByDescending(x => x.Count()).Take(6))
                Console.WriteLine($"  floor   {g.Key} x{g.Count()} {(g.First().ResourceFloor == null ? "MISSING" : "ok")}");
        }

        // --sky <set> puts one of the shipped skyboxes behind the holes
        // in the geometry. The game picks the set from the background
        // BGF the server sends; here it is named directly.
        int ski = Array.IndexOf(flags, "--sky");
        if (ski >= 0 && ski + 1 < flags.Length)
        {
            string skyDir = M59Sky.FindDir(dir);
            r.Sky = M59Sky.Load(skyDir, flags[ski + 1]);
            Console.WriteLine(r.Sky != null
                ? $"sky {r.Sky.Name} from {skyDir}"
                : $"no sky: {flags[ski + 1]} not found under {skyDir ?? "(nowhere)"}");
        }

        // --torch <x> <y> <z> <intensity 0-255> <rgb555 hex> puts a point
        // light in the room, to see the shader's light loop work.
        int tri = Array.IndexOf(flags, "--torch");
        if (tri >= 0 && tri + 5 < flags.Length)
        {
            float ratio = float.Parse(flags[tri + 4]) / 255f;
            float range = (120f + 460f * ratio) * 16f;
            ushort c = Convert.ToUInt16(flags[tri + 5], 16);
            r.Lights.Add(new Renderer.Light {
                X = float.Parse(flags[tri + 1]), Y = float.Parse(flags[tri + 2]),
                Z = float.Parse(flags[tri + 3]),
                R = ((c >> 10) & 31) / 31f, G = ((c >> 5) & 31) / 31f, B = (c & 31) / 31f,
                Range = range, R2 = range * range });
            Console.WriteLine($"torch range {range:F0} world units");
        }

        // --time <seconds> advances scrolling floors and walls.
        int ti = Array.IndexOf(flags, "--time");
        if (ti >= 0 && ti + 1 < flags.Length) r.Time = float.Parse(flags[ti + 1]);
        // --pitch <radians> looks up or down.
        int pi = Array.IndexOf(flags, "--pitch");
        if (pi >= 0 && pi + 1 < flags.Length) r.Pitch = float.Parse(flags[pi + 1]);

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

        // Optional sprite test: --sprite <file.bgf> drops a ring of them
        // around the camera at known distances, including some that should
        // end up behind walls.
        int si = Array.IndexOf(flags, "--sprite");
        if (si >= 0 && si + 1 < flags.Length)
        {
            var bgf = rm.GetObject(flags[si + 1]);
            if (bgf == null) Console.WriteLine($"  ! could not load sprite {flags[si + 1]}");
            else Console.WriteLine($"  {flags[si + 1]}: {bgf.Frames.Count} frames, {bgf.FrameSets.Count} groups");
            Tex st = Tex.FromSprite(bgf);
            if (st == null) Console.WriteLine($"  ! could not decode {flags[si + 1]}");
            else
            {
                // A line straight ahead at increasing distance, plus one far
                // beyond the wall in that direction - that last one must not
                // be visible if depth occlusion works.
                float fa = angle;
                float lx = -MathF.Sin(fa), ly = MathF.Cos(fa);   // left of the camera
                int n = 0;
                foreach (float d in new[] { 1500f, 3000f, 5000f, 7000f, 14000f })
                {
                    float off = (n++ - 2) * 700f;                 // fan them out sideways
                    float sxw = camX + MathF.Cos(fa) * d + lx * off;
                    float syw = camY + MathF.Sin(fa) * d + ly * off;
                    var sec = r.SectorAtPoint(sxw, syw);
                    r.Sprites.Add(new Renderer.Sprite {
                        X = sxw, Y = syw,
                        BaseZ = sec != null ? M59Geo.FloorXY(sec) : camZ - Renderer.EyeHeight,
                        Height = 0f, Bgf = bgf, AngleUnits = 0, Group = 1 });
                    Console.WriteLine($"  sprite at {d,6:F0}  sector={(sec == null ? "outside room" : "ok")}");
                }
            }
        }
        // The room's grass - the reference's default (CreateDecoration,
        // called from ControllerRoom.cpp:485, with
        // DEFAULTVAL_ENGINE_DECORATIONINTENSITY = 20). --nograss leaves
        // it out, for comparing; --grass <n> mirrors the reference's
        // DecorationIntensity setting.
        int gi = Array.IndexOf(flags, "--grass");
        if (gi >= 0 && gi + 1 < flags.Length) M59Grass.Intensity = int.Parse(flags[gi + 1]);
        if (flags.Contains("--nograss")) M59Grass.Intensity = 0;
        if (M59Grass.Intensity > 0)
        {
            string gd = M59Grass.FindDir(dir);
            var gdefs = M59Grass.LoadDefs(gd);
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            r.Grass = M59Grass.Build(roo, gdefs, M59Grass.Intensity);
            sw0.Stop();
            Console.WriteLine(r.Grass == null
                ? $"no grass: defs {(gdefs == null ? "missing" : "ok")} from {gd ?? "(nowhere)"}"
                : $"grass {r.Grass.Count} clumps, {r.Grass.TextureCount} textures, "
                  + $"built in {sw0.Elapsed.TotalMilliseconds:F1} ms");
        }

        var px = new uint[W * H];
        int closed = r.Render(px, W, H, camX, camY, camZ, angle);

        // --bench <n> renders the same frame n times and reports the
        // median, which is how the cost of a change to the renderer is
        // actually established rather than guessed at.
        int bi = Array.IndexOf(flags, "--bench");
        if (bi >= 0 && bi + 1 < flags.Length)
        {
            int n = int.Parse(flags[bi + 1]);
            var ms = new double[n];
            for (int i = 0; i < n; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                r.Render(px, W, H, camX, camY, camZ, angle);
                sw.Stop();
                ms[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(ms);
            Console.WriteLine($"bench {W}x{H} n={n}: median {ms[n / 2]:F2} ms, "
                + $"min {ms[0]:F2}, max {ms[n - 1]:F2}, {r.DecorationDrawn} clumps drawn");
        }

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
