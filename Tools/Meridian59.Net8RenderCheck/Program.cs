using System;using System.Collections.Generic;using System.IO;using System.Linq;
using Meridian59.Files;using Meridian59.Files.ROO;using Meridian59.Common.Enums;

// Two exhaustive checks on the renderer, both against what it actually
// draws rather than against a second copy of its arithmetic.
//
//   dotnet run --project . -- threads <resourceDir>
//   dotnet run --project . -- pick       <resourceDir>
//   dotnet run --project . -- seethrough <resourceDir>
//   dotnet run --project . -- grate      <resourceDir>
//   dotnet run --project . -- slope      <resourceDir>
//   dotnet run --project . -- scroll     <resourceDir>
//   dotnet run --project . -- anim       <resourceDir>
//   dotnet run --project . -- wade       <resourceDir>
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
        // The reduced copies were built from the real art, and a sprite
        // far enough away reads one of those - so they have to be told
        // about the tag too, or the frame is painted in its own colours
        // and the mask finds nothing.
        t.RebuildMips();
        return t;
    }


    /// <summary>
    /// The vector repack against the scalar one it replaced, on random
    /// pixels, both ways round, plus what it bought. Neither shipped
    /// oracle looks at the final buffer at all - they stop at the
    /// renderer's own ARGB words - so without this the one loop every
    /// pixel of every frame goes through had no check on it.
    /// </summary>
    static int RepackCheck()
    {
        const int W = 1280, H = 1080, N = W * H;
        var px = new uint[N];
        var rnd = new Random(7);
        for (int i = 0; i < N; i++) px[i] = (uint)rnd.Next();
        var a = new byte[N * 4];
        var b = new byte[N * 4];
        int bad = 0;
        foreach (bool flip in new[] { false, true })
        {
            Array.Clear(a); Array.Clear(b);
            Repack.ToRgba(px, a, N, flip);
            Repack.ToRgbaScalar(px, b, N, flip);
            int wrong = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) wrong++;
            Console.WriteLine($"repack flip={flip}: {(wrong == 0 ? "identical" : wrong + " BYTES DIFFER")}");
            bad += wrong == 0 ? 0 : 1;
        }

        for (int w = 0; w < 20; w++) { Repack.ToRgba(px, a, N, false); Repack.ToRgbaScalar(px, b, N, false); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < 100; k++) Repack.ToRgbaScalar(px, b, N, false);
        sw.Stop(); double sc = sw.Elapsed.TotalMilliseconds / 100;
        sw.Restart();
        for (int k = 0; k < 100; k++) Repack.ToRgba(px, a, N, false);
        sw.Stop(); double ve = sw.Elapsed.TotalMilliseconds / 100;
        Console.WriteLine($"repack {W}x{H}: scalar {sc:F3} ms  vector {ve:F3} ms  {sc / Math.Max(ve, 0.0001):F2}x" +
                          $"  (accelerated={System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated})");
        return bad;
    }

    /// <summary>
    /// What the first frame in a new room costs, with the texture cache
    /// cold and with it warmed ahead of time. The hitch on walking
    /// through a door is this number, and no other check looks at it:
    /// every oracle here renders the same room many times and so pays
    /// the decode once, in a frame it does not time.
    /// </summary>
    static void Cold(string dir)
    {
        string[] rooms = System.IO.Directory.GetFiles(dir, "*.roo");
        Array.Sort(rooms);
        var rm = new ResourceManager(); rm.Init(dir, dir, dir, dir, dir, dir, dir);
        int W = 1280, H = 432;
        var px = new uint[W * H];
        Console.WriteLine($"{"room",-22} {"cold 1st",9} {"warm 1st",9} {"settled",9}");
        double tc = 0, tw = 0, ts = 0; int n = 0;
        foreach (string path in rooms)
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); }
            catch (Exception e) { Console.WriteLine($"  skip {System.IO.Path.GetFileName(path)}: {e.Message}"); continue; }
            if (roo.Sectors == null || roo.Sectors.Count == 0) continue;
            float cx, cy, cz;
            if (!Spot(roo, out cx, out cy, out cz)) continue;

            // A FRESH ResourceManager for each of the two, because it
            // caches the BGFs behind the TexCache: measuring the warm
            // run second against a manager the cold run had already
            // filled would have credited the warm-up with work the cold
            // run did. That is the whole measurement, so it is the one
            // thing here worth being careful about.
            var rm1 = new ResourceManager(); rm1.Init(dir, dir, dir, dir, dir, dir, dir);
            var rm2 = new ResourceManager(); rm2.Init(dir, dir, dir, dir, dir, dir, dir);
            var r1 = new Renderer(roo, new TexCache(rm1));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            r1.Render(px, W, H, cx, cy, cz, 0f);
            sw.Stop(); double cold = sw.Elapsed.TotalMilliseconds;
            for (int k = 0; k < 10; k++) r1.Render(px, W, H, cx, cy, cz, 0f);
            sw.Restart();
            for (int k = 0; k < 10; k++) r1.Render(px, W, H, cx, cy, cz, 0f);
            sw.Stop(); double settled = sw.Elapsed.TotalMilliseconds / 10;

            var r2 = new Renderer(roo, new TexCache(rm2));
            r2.Warm();
            sw.Restart();
            r2.Render(px, W, H, cx, cy, cz, 0f);
            sw.Stop(); double warm = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine($"{System.IO.Path.GetFileName(path),-22} {cold,9:F1} {warm,9:F1} {settled,9:F1}");
            tc += cold; tw += warm; ts += settled; n++;
            if (n >= 24) break;
        }
        if (n > 0)
            Console.WriteLine($"{"mean of " + n,-22} {tc / n,9:F1} {tw / n,9:F1} {ts / n,9:F1}");
    }

    /// <summary>
    /// Where a settled frame's time goes, phase by phase, at the phone's
    /// buffer size and on one thread so the sums mean something. A dozen
    /// sprites stand in the room, as they do in play, so the depth
    /// buffers are reset and the sprite pass runs. This is the mode that
    /// NAMES the eater; the cold mode only says how big the meal is.
    /// </summary>
    static void Prof(string dir, bool lights)
    {
        string[] rooms = System.IO.Directory.GetFiles(dir, "*.roo");
        Array.Sort(rooms);
        var rm = new ResourceManager(); rm.Init(dir, dir, dir, dir, dir, dir, dir);
        const int W = 1280, H = 432;
        var px = new uint[W * H];
        var bgf = rm.GetObject("Knight.bgf");
        var sum = new double[Renderer.PhCount];
        double plainSum = 0;
        int n = 0;
        Console.Write($"{"room",-16}");
        foreach (string nm in Renderer.PhaseNames) Console.Write($"{nm,8}");
        Console.WriteLine($"{"untimed",8}");
        // ROOMS=a5,badland1 narrows the run to those rooms, for an A/B
        // that has to be repeated to be believed on a shared machine.
        string only = Environment.GetEnvironmentVariable("ROOMS");
        foreach (string path in rooms)
        {
            if (only != null && Array.IndexOf(only.Split(','), System.IO.Path.GetFileNameWithoutExtension(path)) < 0) continue;
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            if (roo.Sectors == null || roo.Sectors.Count == 0) continue;
            if (!Spot(roo, out float cx, out float cy, out float cz)) continue;
            var r = new Renderer(roo, new TexCache(rm)) { Threaded = false };
            if (Environment.GetEnvironmentVariable("NOANCHOR") != null) r.LeafAnchoredFlats = false;
            if (Environment.GetEnvironmentVariable("NOSAMPLE") != null) r.NoSample = true;
            if (Environment.GetEnvironmentVariable("NOWATER") != null) Renderer.Water = false;
            // Sunlit: every wall orientation gets its own factor, which is
            // the case that could thrash a table cache. See LutCache.
            if (Environment.GetEnvironmentVariable("SUN") != null)
            { r.SunLight = 0.7f; r.SunX = 0.3f; r.SunY = 0.2f; r.SunZ = 0.93f; r.Brightness = 0.8f; }
            var rng = new Random(5);
            for (int i = 0; i < 12 && bgf != null; i++)
            {
                float d = 600f + (float)rng.NextDouble() * 6000f;
                float t = ((float)rng.NextDouble() - 0.5f) * 1.2f;
                float sx2 = cx + MathF.Cos(t) * d, sy2 = cy + MathF.Sin(t) * d;
                var sec = r.SectorAtPoint(sx2, sy2);
                var tex = Tex.FromSprite(bgf, rng.Next(0, bgf.Frames.Count));
                if (tex == null) continue;
                r.Sprites.Add(new Renderer.Sprite {
                    X = sx2, Y = sy2, BaseZ = sec != null ? M59Geo.FloorXY(sec, sx2, sy2) : cz - Renderer.EyeHeight,
                    Height = 600f, Texture = tex, Tag = i });
            }
            if (lights)
                for (int i = 0; i < 4; i++)
                {
                    float d = 400f + i * 900f, t = (i - 1.5f) * 0.4f;
                    r.Lights.Add(new Renderer.Light { X = cx + MathF.Cos(t) * d, Y = cy + MathF.Sin(t) * d,
                        Z = cz, R = 1f, G = 0.8f, B = 0.5f, Range = 1500f, R2 = 1500f * 1500f });
                }
            for (int k = 0; k < 6; k++) r.Render(px, W, H, cx, cy, cz, 0f);
            // Best of several blocks: the box is shared.
            var best = new double[Renderer.PhCount];
            for (int i = 0; i < best.Length; i++) best[i] = double.MaxValue;
            for (int rep = 0; rep < 5; rep++)
            {
                Array.Clear(Renderer.Phase);
                Renderer.Profile = true;
                for (int k = 0; k < 6; k++) r.Render(px, W, H, cx, cy, cz, 0f);
                Renderer.Profile = false;
                double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency / 6;
                if (Renderer.Phase[Renderer.PhTotal] * ms < best[Renderer.PhTotal])
                    for (int i = 0; i < best.Length; i++) best[i] = Renderer.Phase[i] * ms;
            }
            // And the frame with the timers off, which is the number the
            // phases are a breakdown of; the timers themselves cost a
            // few calls per wall crossed and the difference is theirs.
            double plain = double.MaxValue;
            var sw = new System.Diagnostics.Stopwatch();
            for (int rep = 0; rep < 5; rep++)
            {
                sw.Restart();
                for (int k = 0; k < 6; k++) r.Render(px, W, H, cx, cy, cz, 0f);
                sw.Stop();
                plain = Math.Min(plain, sw.Elapsed.TotalMilliseconds / 6);
            }
            plainSum += plain;
            Console.Write($"{System.IO.Path.GetFileName(path),-16}");
            for (int i = 0; i < best.Length; i++) { Console.Write($"{best[i],8:F2}"); sum[i] += best[i]; }
            Console.Write($"{plain,8:F2}");
            if (Environment.GetEnvironmentVariable("PROFDBG") != null)
            {
                double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency / 30;
                Console.Write($"  grid {Renderer.DbgGrid * ms:F2} test {Renderer.DbgTest * ms:F2} cand/col {Renderer.DbgCand / 30.0 / W:F1} hits/col {Renderer.DbgHits / 30.0 / W:F1}");
                Console.Write($" wallpx {Renderer.DbgWallPx / 30.0 / (W * H) * 100:F0}% {best[Renderer.PhWalls] * 1e6 / Math.Max(1, Renderer.DbgWallPx / 30.0):F1}ns flatpx {Renderer.DbgFlatPx / 30.0 / (W * H) * 100:F0}% {best[Renderer.PhFlats] * 1e6 / Math.Max(1, Renderer.DbgFlatPx / 30.0):F1}ns");
                Console.Write($" nullpx {Renderer.DbgNullPx / 30.0 / (W * H) * 100:F0}% slowpx {Renderer.DbgSlowPx / 30.0 / (W * H) * 100:F0}% liquid {Renderer.DbgLiquidPx / 30.0 / (W * H) * 100:F0}% slope {Renderer.DbgSlopePx / 30.0 / (W * H) * 100:F0}%");
                Console.Write($" masked n/frame {Renderer.DbgMaskedN / 30.0:F0} sort {Renderer.DbgMaskedSort * ms:F2}ms px {Renderer.DbgMaskedPx / 30.0 / (W * H) * 100:F1}%");
                Renderer.DbgLiquidPx = Renderer.DbgSlopePx = Renderer.DbgMaskedN = Renderer.DbgMaskedSort = Renderer.DbgMaskedPx = 0;
                Renderer.DbgGrid = Renderer.DbgTest = Renderer.DbgCand = Renderer.DbgHits = Renderer.DbgWallPx = Renderer.DbgFlatPx = Renderer.DbgNullPx = Renderer.DbgSlowPx = 0;
            }
            Console.WriteLine();
            if (++n >= 24) break;
        }
        Console.Write($"{"mean of " + n,-16}");
        for (int i = 0; i < sum.Length; i++) Console.Write($"{sum[i] / Math.Max(1, n),8:F2}");
        Console.WriteLine($"{plainSum / Math.Max(1, n),8:F2}");
        Console.Write($"{"share",-16}");
        for (int i = 0; i < sum.Length; i++) Console.Write($"{100 * sum[i] / Math.Max(1e-9, sum[Renderer.PhTotal]),7:F0}%");
        Console.WriteLine();
    }

    /// <summary>
    /// One room, one knob, A against B in alternating blocks inside one
    /// process - the only way a 2 ms difference can be believed on a
    /// shared machine whose load moves the whole frame by more than
    /// that between two runs. Knobs: anchor (LeafAnchoredFlats),
    /// water (Renderer.Water), none (A and B the same, to see the
    /// noise floor). Prints min and median of each side.
    /// </summary>
    static void AB(string dir, string room, string knob)
    {
        var rm = new ResourceManager(); rm.Init(dir, dir, dir, dir, dir, dir, dir);
        string path = System.IO.Path.Combine(dir, room + ".roo");
        var roo = new RooFile(path); roo.ResolveResources(rm);
        if (!Spot(roo, out float cx, out float cy, out float cz)) { Console.WriteLine("no spot"); return; }
        const int W = 1280, H = 432;
        var px = new uint[W * H];
        var bgf = rm.GetObject("Knight.bgf");
        // Two renderers, because a band's texture memo remembers whether
        // a flat is liquid and would not notice the water knob moving
        // under it; each side keeps its own.
        var rs = new Renderer[2];
        for (int side = 0; side < 2; side++)
        {
            var r0 = new Renderer(roo, new TexCache(rm)) { Threaded = false };
            var rng = new Random(5);
            for (int i = 0; i < 12 && bgf != null; i++)
            {
                float d = 600f + (float)rng.NextDouble() * 6000f;
                float t = ((float)rng.NextDouble() - 0.5f) * 1.2f;
                float sx2 = cx + MathF.Cos(t) * d, sy2 = cy + MathF.Sin(t) * d;
                var sec = r0.SectorAtPoint(sx2, sy2);
                var tex = Tex.FromSprite(bgf, rng.Next(0, bgf.Frames.Count));
                if (tex == null) continue;
                r0.Sprites.Add(new Renderer.Sprite {
                    X = sx2, Y = sy2, BaseZ = sec != null ? M59Geo.FloorXY(sec, sx2, sy2) : cz - Renderer.EyeHeight,
                    Height = 600f, Texture = tex, Tag = i });
            }
            rs[side] = r0;
        }
        Renderer r = rs[0];
        void Set(bool on)
        {
            r = rs[on ? 0 : 1];
            if (knob == "anchor") r.LeafAnchoredFlats = on;
            else if (knob == "water") Renderer.Water = on;
        }
        for (int k = 0; k < 10; k++) { Set(true); r.Render(px, W, H, cx, cy, cz, 0f); Set(false); r.Render(px, W, H, cx, cy, cz, 0f); }
        var a = new List<double>(); var b = new List<double>();
        var sw = new System.Diagnostics.Stopwatch();
        for (int rep = 0; rep < 30; rep++)
            foreach (bool on in new[] { true, false })
            {
                Set(on);
                sw.Restart();
                for (int k = 0; k < 4; k++) r.Render(px, W, H, cx, cy, cz, 0f);
                sw.Stop();
                (on ? a : b).Add(sw.Elapsed.TotalMilliseconds / 4);
            }
        Set(true);
        a.Sort(); b.Sort();
        Console.WriteLine($"{room} {knob}: A(on)  min {a[0]:F2} med {a[a.Count / 2]:F2}   B(off) min {b[0]:F2} med {b[b.Count / 2]:F2}   diff min {a[0] - b[0]:F2} med {a[a.Count / 2] - b[b.Count / 2]:F2}");
    }

    /// <summary>
    /// Odd buffer sizes, every room: the phone's own 2400x1080 and
    /// 1280x720, a 1000x450 that divides into nothing, and a few
    /// absurd ones, single-threaded against threaded and with the pick
    /// grid run over each, so an index that is only right when W is a
    /// multiple of the band count or H is even cannot hide. Also the
    /// bytes a settled single-threaded frame allocates, which should be
    /// none: every room is rendered twice more and the allocation
    /// counter read around the second.
    /// </summary>
    static int Sizes(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir, dir, dir, dir, dir, dir, dir);
        var rooms = Directory.GetFiles(dir, "*.roo").OrderBy(x => x).ToArray();
        var bgf = rm.GetObject("Knight.bgf");
        var sizes = new (int W, int H)[] { (1280, 720), (2400, 1080), (1000, 450), (97, 31), (3, 2), (1, 1), (1281, 433) };
        int bad = 0, frames = 0; long allocMax = 0, allocThreaded = 0, allocN = 0; string allocRoom = "";
        var sky = M59Sky.FindDir(dir) != null ? M59Sky.Load(M59Sky.FindDir(dir), "skya") : null;
        foreach (string path in rooms)
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            if (roo.Sectors == null || roo.Sectors.Count == 0) continue;
            if (!Spot(roo, out float cx, out float cy, out float cz)) continue;
            var r1 = new Renderer(roo, new TexCache(rm)) { Threaded = false, Sky = sky };
            var r2 = new Renderer(roo, new TexCache(rm)) { Threaded = true, Sky = sky };
            var rng = new Random(path.Length);
            for (int i = 0; i < 12 && bgf != null; i++)
            {
                float d = 400f + (float)rng.NextDouble() * 6000f;
                float t = (float)rng.NextDouble() * 6.2832f;
                float sx2 = cx + MathF.Cos(t) * d, sy2 = cy + MathF.Sin(t) * d;
                var sec = r1.SectorAtPoint(sx2, sy2);
                var tex = Tex.FromSprite(bgf, rng.Next(0, bgf.Frames.Count));
                if (tex == null) continue;
                foreach (var r in new[] { r1, r2 })
                    r.Sprites.Add(new Renderer.Sprite {
                        X = sx2, Y = sy2, BaseZ = sec != null ? M59Geo.FloorXY(sec, sx2, sy2) : cz - Renderer.EyeHeight,
                        Height = 600f, Texture = tex, Tag = i, Opacity = i % 5 == 0 ? 0.5f : 1f });
            }
            foreach (var r in new[] { r1, r2 })
            {
                r.Time = 12.5f; r.Brightness = 0.8f; r.SunLight = 0.7f; r.SunX = 0.3f; r.SunY = 0.2f; r.SunZ = 0.93f;
                r.Lights.Add(new Renderer.Light { X = cx + 500f, Y = cy, Z = cz, R = 1f, G = 0.7f, B = 0.4f, Range = 1800f, R2 = 1800f * 1800f });
            }
            foreach (var (W, H) in sizes)
            {
                var a = new uint[W * H]; var b = new uint[W * H];
                for (int k = 0; k < 2; k++)
                {
                    float ang = k * 1.9f;
                    r1.Pitch = r2.Pitch = (k - 0.5f) * 0.3f;
                    try
                    {
                        int s1 = r1.Render(a, W, H, cx, cy, cz, ang);
                        int s2 = r2.Render(b, W, H, cx, cy, cz, ang);
                        frames++;
                        if (s1 != s2) { Console.WriteLine($"  {Path.GetFileName(path)} {W}x{H}: solid cols {s1} vs {s2}"); bad++; }
                        int diff = 0;
                        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
                        if (diff != 0) { Console.WriteLine($"  {Path.GetFileName(path)} {W}x{H}: {diff} px differ single vs threaded"); bad++; }
                        for (int gy = 0; gy < H; gy += Math.Max(1, H / 7)) for (int gx = 0; gx < W; gx += Math.Max(1, W / 9))
                        { foreach (var sp in r1.PickAll(gx, gy, W, H, cx, cy, cz, ang)) { } r1.DebugFlatDepth(gx, gy); }
                        r1.PickAll(W - 1, H - 1, W, H, cx, cy, cz, ang);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"  {Path.GetFileName(path)} {W}x{H} k={k}: {e.GetType().Name}: {e.Message}");
                        bad++;
                    }
                }
            }
            // Allocation of a settled single-threaded frame at the phone size.
            {
                var px = new uint[1280 * 432];
                r1.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                r1.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                r1.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
                if (alloc > allocMax) { allocMax = alloc; allocRoom = Path.GetFileName(path); }
                // And the threaded frame, process-wide: Parallel.For's
                // own bookkeeping is in this number, so it is a floor to
                // know, not a zero to demand.
                r2.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                r2.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                long tb = GC.GetTotalAllocatedBytes(true);
                r2.Render(px, 1280, 432, cx, cy, cz, 0.3f);
                long ta = GC.GetTotalAllocatedBytes(true) - tb;
                allocThreaded += ta; allocN++;
            }
        }
        Console.WriteLine($"sizes: {frames} frames, {bad} problems; worst settled single-thread frame allocation {allocMax} B {allocRoom}; threaded mean {allocThreaded / Math.Max(1, allocN)} B/frame");
        Console.WriteLine(bad == 0 ? "OK" : "PROBLEM");
        return bad;
    }

    /// <summary>
    /// Every pixel of every room, as a hash, so an optimisation can be
    /// held to "the same picture" and not merely to "the same picture as
    /// itself on another thread". Writes the file when it does not
    /// exist, compares against it when it does. Sprites, point lights, a
    /// non-zero clock and the pick path are all in the scene so every
    /// branch of the hot path is under the hash.
    /// </summary>
    static int Golden(string dir, string file)
    {
        var rm = new ResourceManager(); rm.Init(dir, dir, dir, dir, dir, dir, dir);
        var rooms = Directory.GetFiles(dir, "*.roo").OrderBy(x => x).ToArray();
        var bgf = rm.GetObject("Knight.bgf");
        const int W = 640, H = 360;
        var px = new uint[W * H];
        var lines = new List<string>();
        // GOLDSKY set puts a skybox behind the holes on the sunlit pass,
        // so the sky path is under the hash too; it is a different
        // picture, so it belongs in a golden file of its own.
        M59Sky sky = null;
        if (Environment.GetEnvironmentVariable("GOLDSKY") != null)
        {
            string skyDir = M59Sky.FindDir(dir);
            sky = skyDir != null ? M59Sky.Load(skyDir, "skya") : null;
            Console.WriteLine(sky != null ? "sky: skya" : "sky: NOT FOUND");
        }
        foreach (string path in rooms)
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            if (roo.Sectors == null || roo.Sectors.Count == 0) continue;
            if (!Spot(roo, out float cx, out float cy, out float cz)) continue;
            var r = new Renderer(roo, new TexCache(rm)) { Threaded = false };
            var rng = new Random(path.Length);
            for (int i = 0; i < 12 && bgf != null; i++)
            {
                float d = 400f + (float)rng.NextDouble() * 6000f;
                float t = (float)rng.NextDouble() * 6.2832f;
                float sx2 = cx + MathF.Cos(t) * d, sy2 = cy + MathF.Sin(t) * d;
                var sec = r.SectorAtPoint(sx2, sy2);
                var tex = Tex.FromSprite(bgf, rng.Next(0, bgf.Frames.Count));
                if (tex == null) continue;
                r.Sprites.Add(new Renderer.Sprite {
                    X = sx2, Y = sy2, BaseZ = sec != null ? M59Geo.FloorXY(sec, sx2, sy2) : cz - Renderer.EyeHeight,
                    Height = 600f, Texture = tex, Tag = i, Opacity = i % 5 == 0 ? 0.5f : 1f,
                    TintR = i % 7 == 0 ? 5f : 1f, TintG = i % 7 == 0 ? 3f : 1f, TintB = i % 7 == 0 ? 3f : 1f });
            }
            for (int pass = 0; pass < 2; pass++)
            {
                r.Lights.Clear();
                if (pass == 1)
                {
                    r.Time = 12.5f; r.Brightness = 0.8f; r.SunLight = 0.7f; r.SunX = 0.3f; r.SunY = 0.2f; r.SunZ = 0.93f;
                    for (int i = 0; i < 3; i++)
                    {
                        float d = 500f + i * 1200f, t = (i - 1f) * 0.7f;
                        r.Lights.Add(new Renderer.Light { X = cx + MathF.Cos(t) * d, Y = cy + MathF.Sin(t) * d,
                            Z = cz, R = 1f, G = 0.7f, B = 0.4f, Range = 1800f, R2 = 1800f * 1800f });
                    }
                }
                else { r.Time = 0f; r.Brightness = 1f; r.SunLight = 0f; }
                r.Sky = pass == 1 ? sky : null;
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * MathF.PI / 2f;
                    r.Pitch = (k - 1.5f) * 0.2f;
                    Array.Clear(px);
                    int solid = r.Render(px, W, H, cx, cy, cz, ang);
                    ulong h = 14695981039346656037UL;
                    for (int i = 0; i < px.Length; i++) { h ^= px[i]; h *= 1099511628211UL; }
                    int picks = 0;
                    for (int gy = 20; gy < H; gy += 60) for (int gx = 20; gx < W; gx += 60)
                        foreach (var sp in r.PickAll(gx, gy, W, H, cx, cy, cz, ang)) { picks = picks * 31 + (int)sp.Tag + gx; }
                    lines.Add($"{Path.GetFileName(path)} {pass} {k} {h:X16} {solid} {picks}");
                }
            }
        }
        if (!File.Exists(file))
        {
            File.WriteAllLines(file, lines);
            Console.WriteLine($"wrote {lines.Count} golden frames to {file}");
            return 0;
        }
        var old = File.ReadAllLines(file);
        int bad = 0;
        var oldSet = new HashSet<string>(old);
        // Keyed by room/pass/heading so a differing frame can say WHAT
        // differs: the picture (hash), what closed the columns, or only
        // which sprites the grid of taps reached.
        var oldBy = new Dictionary<string, string[]>();
        foreach (string l in old) { var f = l.Split(' '); if (f.Length == 6) oldBy[f[0] + " " + f[1] + " " + f[2]] = f; }
        var perRoom = new SortedDictionary<string, (int px, int picks)>();
        foreach (string l in lines) if (!oldSet.Contains(l))
        {
            var f = l.Split(' ');
            string why = "new frame";
            if (oldBy.TryGetValue(f[0] + " " + f[1] + " " + f[2], out var o))
                why = (o[3] != f[3] ? "pixels " : "") + (o[4] != f[4] ? "solidcols " : "") + (o[5] != f[5] ? "picks" : "");
            perRoom.TryGetValue(f[0], out var c);
            perRoom[f[0]] = (c.px + (why.Contains("pixels") ? 1 : 0), c.picks + (why.Contains("picks") ? 1 : 0));
            if (bad < 10) Console.WriteLine("  differs: " + l + "  [" + why.Trim() + "]");
            bad++;
        }
        foreach (var kv in perRoom) Console.WriteLine($"  {kv.Key}: {kv.Value.px} frames with pixel changes, {kv.Value.picks} with pick changes");
        Console.WriteLine($"{lines.Count} frames, {bad} differ from {file}" + (old.Length != lines.Count ? $" (file has {old.Length})" : ""));
        Console.WriteLine(bad == 0 && old.Length == lines.Count ? "IDENTICAL" : "DIFFERENT");
        return bad == 0 && old.Length == lines.Count ? 0 : 1;
    }

    /// <summary>A point inside the room, with a floor under it.</summary>
    static bool Spot(RooFile roo, out float cx, out float cy, out float cz)
    {
        cx = cy = cz = 0f;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var w in roo.Walls)
        {
            minX = Math.Min(minX, Math.Min(w.P1.X, w.P2.X));
            maxX = Math.Max(maxX, Math.Max(w.P1.X, w.P2.X));
            minY = Math.Min(minY, Math.Min(w.P1.Y, w.P2.Y));
            maxY = Math.Max(maxY, Math.Max(w.P1.Y, w.P2.Y));
        }
        if (minX > maxX) return false;
        for (int i = 0; i < 48; i++)
        {
            float fx = minX + (maxX - minX) * ((i % 8) + 0.5f) / 8f;
            float fy = minY + (maxY - minY) * ((i / 8) + 0.5f) / 6f;
            RooSector sec = new Renderer(roo, null).SectorAtPoint(fx, fy);
            if (sec == null) continue;
            cx = fx; cy = fy; cz = M59Geo.FloorXY(sec) + Renderer.EyeHeight;
            return true;
        }
        return false;
    }

    static int Main(string[] a)
    {
        string mode = a.Length > 0 ? a[0].ToLowerInvariant() : "all";
        string dir  = a.Length > 1 ? a[1] : "/tmp/res";
        int bad = 0;
        if (mode == "repack"  || mode == "all") bad += RepackCheck();
        if (mode == "cold") Cold(dir);
        if (mode == "prof") Prof(dir, a.Length > 2 && a[2] == "lights");
        if (mode == "sizes") bad += Sizes(dir);
        if (mode == "ab") AB(dir, a.Length > 2 ? a[2] : "badland1", a.Length > 3 ? a[3] : "none");
        if (mode == "golden") bad += Golden(dir, a.Length > 2 ? a[2] : "/tmp/golden.txt");
        if (mode == "threads" || mode == "all") bad += Threads(dir);
        if (mode == "pick"    || mode == "all") bad += Pick(dir);
        if (mode == "seethrough" || mode == "all") SeeThrough(dir);
        if (mode == "grate"   || mode == "all") bad += Grate(dir);
        if (mode == "slope"   || mode == "all") bad += SlopeMath(dir);
        if (mode == "scroll"  || mode == "all") bad += Scrolling(dir);
        if (mode == "anim"    || mode == "all") bad += Animated(dir);
        if (mode == "anchor"  || mode == "all") Anchor(dir);
        if (mode == "sky"     || mode == "all") bad += Sky(dir);
        if (mode == "holes"   || mode == "all") Holes(dir);
        if (mode == "lintel"  || mode == "all") bad += Lintel(dir);
        if (mode == "flatdepth" || mode == "all") bad += FlatDepth(dir);
        if (mode == "wade"    || mode == "all") bad += Wade(dir);
        if (mode == "roomtex" || mode == "all") bad += RoomTex(dir);
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Sprites are no longer drawn through floors and ceilings.
    ///
    /// The lintel check above closed the walls: the window a column
    /// leaves open past each upper and lower part. Floors were never in
    /// it. A raised platform's top, a stair tread or the lip of a pit is
    /// a floor span NEARER than what stands beyond it that closes no
    /// column and draws no wall part, so a creature on the far side had
    /// its legs painted over it. The reference cannot: a floor fragment
    /// writes depth like any other (general.material:257-285) and the
    /// billboard is depth-tested per pixel. The renderer now keeps a
    /// per-pixel flat depth and tests sprites, labels and taps against
    /// it (Renderer._flatDepth).
    ///
    /// Three things are proved here, in order of how much they matter:
    ///
    /// 1. A creature standing on a LEVEL floor in the camera's own BSP
    ///    leaf loses NOTHING to the test. The leaf is convex, so no wall
    ///    and no other floor lies between it and the eye, and the only
    ///    flat under its rows is the one it stands on, which is farther
    ///    than it at every row above its feet. This is the "feet" case
    ///    the test has no epsilon for, and it is the one that would be
    ///    wrong everywhere if the row conventions ever drifted. Zero
    ///    pixels, exactly.
    /// 2. Across the game the test takes SOMETHING back - rendering each
    ///    scene with and without it, as the lintel check does - or it is
    ///    not doing anything.
    /// 3. What it costs: settled frames with sprites, with and without.
    ///    The buffer is still written and reset either way (that is what
    ///    the fill and the store cost); the knob only turns the test off.
    /// </summary>
    /// <summary>Whether a point lies inside a BSP leaf's convex polygon, either winding.</summary>
    static bool InLeaf(RooSubSector leaf, float x, float y)
    {
        var v = leaf.Vertices;
        int pos = 0, neg = 0;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
        {
            double s = ((double)v[i].X - v[j].X) * (y - v[j].Y) - ((double)v[i].Y - v[j].Y) * (x - v[j].X);
            if (s > 0) pos++; else if (s < 0) neg++;
        }
        return pos == 0 || neg == 0;
    }

    static int FlatDepth(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        string[] sprites = { "duskrat.bgf", "Knight.bgf", "cyclops.bgf" };
        const int W=640, H=360;
        int scenes=0, through=0, total=0;
        int feetScenes=0, feetTotal=0, feetLost=0;
        var worst = new List<(double frac,string where)>();
        var lostWhere = new List<string>();

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x).Take(60))
        foreach (string sprName in sprites)
        {
            RooFile roo; try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var bgf = rm.GetObject(sprName);
            if (bgf == null) continue;
            var r = new Renderer(roo, new TexCache(rm));
            var big = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
                .OrderByDescending(l=>{double s2=0;var v=l.Vertices;
                    for(int i=0,j=v.Count-1;i<v.Count;j=i++) s2+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                    return Math.Abs(s2*.5);}).FirstOrDefault();
            if (big == null) continue;
            float cx=big.Vertices.Average(v=>(float)v.X), cy=big.Vertices.Average(v=>(float)v.Y);
            var csec = r.SectorAtPoint(cx,cy); if (csec == null) continue;
            float cz=M59Geo.FloorXY(csec)+Renderer.EyeHeight;

            // 1. The feet case: sprites inside this leaf, on its floor,
            // when that floor is level. A point a fraction of the way
            // from the centroid to a vertex is inside a convex polygon.
            // The WHOLE billboard has to be inside, not just its base:
            // the first run of this put a duskrat's centre in the leaf
            // and its left flank over the merchant's counter, and the
            // test rightly took 377 pixels of flank back - the counter
            // top is nearer than the flank, as it would be to a depth
            // buffer. Nothing can be nearer than a billboard that lies
            // wholly inside the convex leaf it stands in.
            //
            // Not in a depth sector: the game stands a creature there
            // BELOW the floor, by the sector depth, and the floor - the
            // water surface - rightly takes the submerged part. That is
            // the wade check's case, with the loss solved and counted;
            // here it would be either a creature planted at the surface,
            // which the game never does, or a loss this check cannot
            // tell from a bug.
            if (csec.SlopeInfoFloor == null && csec.Flags.SectorDepth == RooSectorFlags.DepthType.Depth0)
            {
                for (int k=0;k<4;k++)
                {
                    float ang = k * MathF.PI / 2f;
                    // Across the view, which is the way a billboard runs.
                    float lx = -MathF.Sin(ang), ly = MathF.Cos(ang);
                    var rng = new Random(k*31 + path.Length*7 + sprName.Length);
                    r.Sprites.Clear();
                    for (int i=0;i<8;i++)
                    {
                        var v = big.Vertices[rng.Next(big.Vertices.Count)];
                        float f = 0.15f + 0.8f*(float)rng.NextDouble();
                        float sx2 = cx + ((float)v.X - cx)*f, sy2 = cy + ((float)v.Y - cy)*f;
                        if (!ReferenceEquals(r.SectorAtPoint(sx2, sy2), csec)) continue;
                        var tex = Tag(bgf, rng.Next(0, bgf.Frames.Count));
                        if (tex == null) continue;
                        // Half the billboard's width in world units, as
                        // Place sizes it: the height over the art's
                        // aspect.
                        float half = 600f * tex.W / MathF.Max(1, tex.H) * 0.5f;
                        if (!InLeaf(big, sx2 + lx*half, sy2 + ly*half)
                         || !InLeaf(big, sx2 - lx*half, sy2 - ly*half)) continue;
                        r.Sprites.Add(new Renderer.Sprite {
                            X=sx2, Y=sy2, BaseZ = M59Geo.FloorXY(csec), Height=600f, Texture=tex, Tag = i });
                    }
                    if (r.Sprites.Count == 0) continue;
                    Renderer.ClipFlats = false;
                    var loose = new uint[W*H]; r.Render(loose, W,H, cx,cy,cz, ang);
                    Renderer.ClipFlats = true;
                    var tight = new uint[W*H]; r.Render(tight, W,H, cx,cy,cz, ang);
                    feetScenes++;
                    int here = 0;
                    for (int i=0;i<W*H;i++)
                    {
                        bool a = Tagged(loose[i]), b = Tagged(tight[i]);
                        if (a) feetTotal++;
                        if (a && !b) { feetLost++; here++; }
                    }
                    if (here > 0 && lostWhere.Count < 5)
                        lostWhere.Add($"{here} px  {Path.GetFileName(path)} {sprName} {k*90}deg");
                }
            }

            // 2. Across the room: what the test takes back.
            for (int k=0;k<4;k++)
            {
                float ang = k * MathF.PI / 2f;
                var rng = new Random(k*97 + path.Length*13 + sprName.Length);
                r.Sprites.Clear();
                for (int i=0;i<12;i++)
                {
                    float d = 600f + (float)rng.NextDouble()*9000f;
                    float t = ang + ((float)rng.NextDouble()-0.5f)*1.2f;
                    float sx2 = cx + MathF.Cos(t)*d, sy2 = cy + MathF.Sin(t)*d;
                    var sec = r.SectorAtPoint(sx2, sy2);
                    var tex = Tag(bgf, rng.Next(0, bgf.Frames.Count));
                    if (tex == null) continue;
                    // ON the floor, which on a ramp is the surface under
                    // the point and not the sector's base height: planted
                    // at the base, as the lintel check plants them, a rat
                    // on bergleader's ramp stood 930 units under the
                    // surface, deeper than it is tall, and this count was
                    // mostly of buried sprites. The game's heights come
                    // from GetHeightAt, which follows the slope - and
                    // takes the sector depth off, so a creature in water
                    // stands under the surface (RooSector.cs:813-841).
                    r.Sprites.Add(new Renderer.Sprite {
                        X=sx2, Y=sy2, BaseZ = sec!=null ? sec.CalculateFloorHeight(sx2, sy2, true) : cz-Renderer.EyeHeight,
                        Height=600f, Texture=tex, Tag = i });
                }
                if (r.Sprites.Count == 0) continue;

                Renderer.ClipFlats = false;
                var loose = new uint[W*H]; r.Render(loose, W,H, cx,cy,cz, ang);
                Renderer.ClipFlats = true;
                var tight = new uint[W*H]; r.Render(tight, W,H, cx,cy,cz, ang);
                scenes++;
                int here = 0;
                for (int i=0;i<W*H;i++)
                {
                    bool a = Tagged(loose[i]), b = Tagged(tight[i]);
                    if (a) total++;
                    if (a && !b) { through++; here++; }
                }
                if (here > 0)
                    worst.Add(((double)here/Math.Max(1,W*H), $"{Path.GetFileName(path)} {sprName} {k*90}deg"));
            }
        }
        Console.WriteLine($"{feetScenes} scenes of creatures on the level floor of the eye's own leaf, {feetTotal} sprite pixels");
        Console.WriteLine($"  lost to the flat depth test: {feetLost}");
        foreach (string s in lostWhere) Console.WriteLine("    " + s);
        Console.WriteLine($"{scenes} scenes with sprites, {total} sprite pixels");
        Console.WriteLine($"  drawn through a floor or ceiling before: {through}"
                        + (total>0 ? $"  ({100.0*through/total:F1}% of them)" : ""));
        foreach (var w in worst.OrderByDescending(x=>x.frac).Take(5))
            Console.WriteLine($"    {100*w.frac,5:F2}% of the frame  {w.where}");

        // 3. The cost. Frames with a dozen sprites in them, settled, at a
        // phone's shape; the knob leaves the buffer written and reset
        // either way, so the difference is the test alone and the
        // absolute number is the frame.
        {
            const int BW = 1280, BH = 576;
            var px = new uint[BW * BH];
            double onMs = 0, offMs = 0; int n = 0;
            foreach (string room in new[] { "barinn.roo", "kc4.roo", "dvalley1.roo", "bergauc.roo" })
            {
                string path = Path.Combine(dir, room);
                if (!File.Exists(path)) continue;
                RooFile roo; try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
                var bgf = rm.GetObject("Knight.bgf"); if (bgf == null) break;
                var r = new Renderer(roo, new TexCache(rm));
                var big = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
                    .OrderByDescending(l=>{double s2=0;var v=l.Vertices;
                        for(int i=0,j=v.Count-1;i<v.Count;j=i++) s2+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                        return Math.Abs(s2*.5);}).FirstOrDefault();
                if (big == null) continue;
                float cx=big.Vertices.Average(v=>(float)v.X), cy=big.Vertices.Average(v=>(float)v.Y);
                var csec = r.SectorAtPoint(cx,cy); if (csec == null) continue;
                float cz=M59Geo.FloorXY(csec)+Renderer.EyeHeight;
                var rng = new Random(5);
                for (int i=0;i<12;i++)
                {
                    float d = 600f + (float)rng.NextDouble()*6000f;
                    float t = ((float)rng.NextDouble()-0.5f)*1.2f;
                    float sx2 = cx + MathF.Cos(t)*d, sy2 = cy + MathF.Sin(t)*d;
                    var sec = r.SectorAtPoint(sx2, sy2);
                    var tex = Tex.FromSprite(bgf, rng.Next(0, bgf.Frames.Count));
                    if (tex == null) continue;
                    r.Sprites.Add(new Renderer.Sprite {
                        X=sx2, Y=sy2, BaseZ = sec!=null ? M59Geo.FloorXY(sec, sx2, sy2) : cz-Renderer.EyeHeight,
                        Height=600f, Texture=tex, Tag = i });
                }
                // The best of several short blocks, alternating the
                // knob: the box this runs on is shared and a mean of
                // forty frames moved by more than the thing being
                // measured.
                var sw = new System.Diagnostics.Stopwatch();
                double bestOn = double.MaxValue, bestOff = double.MaxValue;
                for (int k = 0; k < 10; k++) r.Render(px, BW, BH, cx, cy, cz, 0f);
                for (int rep = 0; rep < 8; rep++)
                foreach (bool on in new[] { true, false })
                {
                    Renderer.ClipFlats = on;
                    sw.Restart();
                    for (int k = 0; k < 10; k++) r.Render(px, BW, BH, cx, cy, cz, 0f);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds / 10;
                    if (on) bestOn = Math.Min(bestOn, ms); else bestOff = Math.Min(bestOff, ms);
                }
                onMs += bestOn; offMs += bestOff; n++;
                Renderer.ClipFlats = true;
            }
            if (n > 0)
                Console.WriteLine($"  {BW}x{BH}, 12 sprites, best of 8 blocks: test on {onMs/n:F2} ms  off {offMs/n:F2} ms  per frame, mean of {n} rooms");
        }

        bool ok = feetLost == 0 && (total == 0 || through > 0);
        Console.WriteLine(ok ? "OK" : "PROBLEM");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Wading. A sector flagged with a depth (RooSectorFlags.cs:35-38,
    /// shallow / deep / very deep) stands its objects LOWER than its
    /// floor: the library takes FINENESS/5, 2/5 or 3/5 off the height
    /// (RooFile.cs:120-127, RooSector.cs:813-841), RoomObject asks for
    /// its height with that depth on (RoomObject.cs:1081-1082,
    /// :1184-1185) and the reference puts the scene node at that
    /// Position3D unchanged (RemoteNode.cpp:510-515), with the camera a
    /// child of the same node (:406-425). The water surface - the floor
    /// at its undepthed height - writes depth like any flat
    /// (general.material:414-445), so a wader is seen from the waterline
    /// up. The flatdepth check above plants nothing in a depth sector
    /// and proves nothing about this; this one plants creatures where
    /// the game does and counts:
    ///
    /// 1. Nothing is painted below the waterline. The row the surface
    ///    crosses the billboard on is solved here from the camera and
    ///    the sector alone - not from the renderer - and no tagged
    ///    pixel may lie more than a row under it.
    /// 2. Above the waterline, nothing is lost: the whole billboard is
    ///    inside the camera's own convex leaf, so the water is the only
    ///    flat nearer than the creature's feet, and the clip-on and
    ///    clip-off renders must agree on every row above the cut.
    /// 3. Something IS under water in frame, or the scene is not a
    ///    test: the feet row is below the surface row and the surface
    ///    row is inside the frame.
    ///
    /// A floor whose texture the resource set lacks is skipped and
    /// counted: the reference builds no floor for it either
    /// (ControllerRoom.cpp:789-790), so there is no surface to hide
    /// behind, and the fixture set here is missing most of the water
    /// (grd08895 and the 8911 family among them).
    /// </summary>
    static int Wade(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        string[] sprites = { "duskrat.bgf", "Knight.bgf" };
        const int W=640, H=360;
        float proj = Renderer.Projection(W, H), horizon = H * 0.5f;
        int scenes=0, below=0, aboveLost=0, aboveGained=0, hidden=0, shown=0;
        int roomsWithDepth=0, sectorsNoTex=0, sectorsOk=0, byDepth1=0, byDepth2=0, byDepth3=0;
        var worst = new List<string>();

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo; try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var deep = roo.Sectors.Where(s => s.Flags.SectorDepth != RooSectorFlags.DepthType.Depth0).ToList();
            if (deep.Count == 0) continue;
            roomsWithDepth++;
            var tc = new TexCache(rm);
            var withTex = new HashSet<RooSector>();
            foreach (var s in deep)
            {
                if (tc.Get(s.FloorTexture) != null) { withTex.Add(s); sectorsOk++; }
                else sectorsNoTex++;
            }
            if (withTex.Count == 0) continue;
            // The camera's leaf: the roomiest leaf of a depth sector whose
            // floor will be drawn. The eye wades too, as the avatar's does.
            var leaf = roo.BSPTreeLeaves
                .Where(l => l.Sector != null && withTex.Contains(l.Sector) && l.Vertices != null && l.Vertices.Count >= 3)
                .OrderByDescending(l => { double s2=0; var v=l.Vertices;
                    for (int i=0,j=v.Count-1;i<v.Count;j=i++) s2+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                    return Math.Abs(s2*.5); }).FirstOrDefault();
            if (leaf == null) continue;
            RooSector csec = leaf.Sector;
            float cx = leaf.Vertices.Average(v=>(float)v.X), cy = leaf.Vertices.Average(v=>(float)v.Y);
            float cz = csec.CalculateFloorHeight(cx, cy, true) + Renderer.EyeHeight;
            switch (csec.Flags.SectorDepth)
            {
                case RooSectorFlags.DepthType.Depth1: byDepth1++; break;
                case RooSectorFlags.DepthType.Depth2: byDepth2++; break;
                default: byDepth3++; break;
            }
            var r = new Renderer(roo, tc);

            foreach (string sprName in sprites)
            {
                var bgf = rm.GetObject(sprName); if (bgf == null) continue;
                for (int k=0;k<4;k++)
                {
                    float ang = k * MathF.PI / 2f;
                    float ca = MathF.Cos(-ang), sa = MathF.Sin(-ang);
                    float lx = -MathF.Sin(ang), ly = MathF.Cos(ang);
                    var rng = new Random(k*17 + path.Length*3 + sprName.Length);
                    // The room with nothing in it, for the mask - see the
                    // pixel loop. The eye does not move between scenes.
                    r.Sprites.Clear();
                    var bare = new uint[W*H]; r.Render(bare, W,H, cx,cy,cz, ang);
                    for (int i=0;i<6;i++)
                    {
                        // One creature per scene, so every tagged pixel is
                        // its own. A point part way from the centroid to a
                        // vertex is inside the convex leaf; the flanks are
                        // checked as the feet check checks them.
                        var v = leaf.Vertices[rng.Next(leaf.Vertices.Count)];
                        float f = 0.2f + 0.75f*(float)rng.NextDouble();
                        float sx2 = cx + ((float)v.X - cx)*f, sy2 = cy + ((float)v.Y - cy)*f;
                        if (!ReferenceEquals(r.SectorAtPoint(sx2, sy2), csec)) continue;
                        float rx = sx2 - cx, ry = sy2 - cy;
                        float depth = rx * ca - ry * sa;
                        if (depth < 400f) continue;
                        var tex = Tag(bgf, rng.Next(0, bgf.Frames.Count));
                        if (tex == null) continue;
                        const float Height = 600f;
                        float half = Height * tex.W / MathF.Max(1, tex.H) * 0.5f;
                        if (!InLeaf(leaf, sx2 + lx*half, sy2 + ly*half)
                         || !InLeaf(leaf, sx2 - lx*half, sy2 - ly*half)) continue;

                        // Where the game stands it, and where the water is.
                        float feetZ = csec.CalculateFloorHeight(sx2, sy2, true);
                        float surfZ = csec.CalculateFloorHeight(sx2, sy2, false);
                        float feetRow = horizon + (cz - feetZ) * proj / depth;
                        float surfRow = horizon + (cz - surfZ) * proj / depth;
                        if (!(feetRow > surfRow + 1f) || surfRow >= H - 1 || surfRow <= 0f) continue;

                        r.Sprites.Clear();
                        r.Sprites.Add(new Renderer.Sprite { X=sx2, Y=sy2, BaseZ=feetZ, Height=Height, Texture=tex, Tag=i });
                        Renderer.ClipFlats = false;
                        var loose = new uint[W*H]; r.Render(loose, W,H, cx,cy,cz, ang);
                        Renderer.ClipFlats = true;
                        var tight = new uint[W*H]; r.Render(tight, W,H, cx,cy,cz, ang);
                        scenes++;
                        int under = 0, lost = 0, gained = 0, hid = 0, kept = 0;
                        int cut = (int)MathF.Floor(surfRow);
                        var firstUnder = new List<string>();
                        for (int y=0;y<H;y++)
                        for (int x=0;x<W;x++)
                        {
                            // A pixel is the creature's only where the bare
                            // room painted something else: palette entry 5
                            // is FF800080, pure purple, and a floor that has
                            // it (grd11036 in marion, 63 texels) shades to a
                            // colour the tag test alone cannot tell from a
                            // shaded magenta sprite. The pick oracle makes
                            // the same comparison for the same reason.
                            bool a = Tagged(loose[y*W+x]) && loose[y*W+x] != bare[y*W+x];
                            bool b = Tagged(tight[y*W+x]) && tight[y*W+x] != bare[y*W+x];
                            if (b && y > cut + 1)
                            {
                                under++;
                                if (firstUnder.Count < 3)
                                {
                                    // What the water should have put there: the
                                    // pixel's flat depth after the clipped render,
                                    // against the creature's own.
                                    float fd = r.DebugFlatDepth(x, y);
                                    // The floor the ray through this pixel meets at
                                    // the surface height, and which sector that is.
                                    float dyRow = (y - horizon) / proj;
                                    float dRow = dyRow > 0 ? (cz - surfZ) / dyRow : float.NaN;
                                    float rdx = MathF.Cos(ang) , rdy = MathF.Sin(ang);
                                    var hitSec = float.IsNaN(dRow) ? null : r.SectorAtPoint(cx + rdx*dRow, cy + rdy*dRow);
                                    firstUnder.Add($"        ({x},{y}) tight={tight[y*W+x]:X8} loose={loose[y*W+x]:X8} bare={bare[y*W+x]:X8}"
                                        + $" spriteDepth={depth:F0} flatDepth={fd:F0}"
                                        + $" surfaceRayDist={dRow:F0} sectorThere={(hitSec==null?"none":hitSec.Num.ToString())}"
                                        + $" floorTex={(hitSec==null?0:hitSec.FloorTexture)} depthFlag={(hitSec==null?"-":hitSec.Flags.SectorDepth.ToString())}"
                                        + $" camSector={csec.Num} camFloorTex={csec.FloorTexture} camFloorH={csec.FloorHeight} sloped={(csec.SlopeInfoFloor!=null)}");
                                }
                            }
                            if (y < cut - 1) { if (a && !b) lost++; if (b && !a) gained++; }
                            if (a && !b) hid++;
                            if (b) kept++;
                        }
                        below += under; aboveLost += lost; aboveGained += gained; hidden += hid; shown += kept;
                        if ((under > 0 || lost > 0 || gained > 0) && worst.Count < 8)
                        {
                            worst.Add($"{Path.GetFileName(path)} {sprName} {k*90}deg {csec.Flags.SectorDepth}: "
                                    + $"{under} px under the waterline (row {surfRow:F1}), {lost} lost / {gained} gained above it"
                                    + $" [sprite at ({sx2:F0},{sy2:F0}) feetZ={feetZ:F0} surfZ={surfZ:F0} depth={depth:F0} eye=({cx:F0},{cy:F0},{cz:F0})]");
                            worst.AddRange(firstUnder);
                        }
                    }
                }
            }
            Renderer.ClipFlats = true;
        }
        Console.WriteLine($"{roomsWithDepth} rooms with depth sectors; {sectorsOk} sectors with a floor texture here, {sectorsNoTex} without (skipped: no floor, no surface)");
        Console.WriteLine($"{scenes} scenes of a creature wading, eye wading too ({byDepth1} rooms shallow, {byDepth2} deep, {byDepth3} very deep)");
        Console.WriteLine($"  painted below the waterline with the clip on: {below}");
        Console.WriteLine($"  lost above the waterline: {aboveLost}, gained: {aboveGained}");
        Console.WriteLine($"  hidden by the water: {hidden} of {hidden+shown} sprite pixels");
        foreach (string s in worst) Console.WriteLine("    " + s);
        bool ok = scenes > 0 && below == 0 && aboveLost == 0 && aboveGained == 0 && hidden > 0;
        Console.WriteLine(ok ? "OK" : "PROBLEM");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// The cube faces are laid out by a convention read off the art
    /// rather than quoted from Ogre, so it is checked rather than
    /// trusted: the sky is continuous, therefore two directions a hair
    /// apart must give nearly the same colour even when they land on
    /// different faces. A face stored rotated, mirrored or on the wrong
    /// axis breaks that at the seam and nowhere else, which is exactly
    /// what this walks.
    ///
    /// The bar is the picture's own roughness: the same test run at
    /// pairs that stay INSIDE one face gives the noise floor, and a
    /// seam is only wrong if it is far worse than that.
    /// </summary>
    static int Sky(string dir)
    {
        string skyDir = M59Sky.FindDir(dir);
        if (skyDir == null) { Console.WriteLine("  no sky art here, skipped"); return 0; }

        int bad = 0;
        foreach (string set in new[] { "skya", "skyb", "skyc", "skyd", "redsky" })
        {
            M59Sky sky = M59Sky.Load(skyDir, set);
            if (sky == null) { Console.WriteLine($"  {set}: missing a face"); bad++; continue; }

            // A direction just either side of a boundary, and the same
            // pair rotated well clear of one, over the whole sphere.
            const float Eps = 0.004f;
            double seam = 0, inside = 0; int seamN = 0, insideN = 0, worst = 0;
            string where = "";
            for (int i = 0; i < 20000; i++)
            {
                // A deterministic spread over the sphere.
                double t = i * 2.399963229728653;               // golden angle
                double z = 1.0 - 2.0 * (i + 0.5) / 20000.0;
                double r = Math.Sqrt(Math.Max(0, 1 - z * z));
                float x = (float)(r * Math.Cos(t)), y = (float)(r * Math.Sin(t)), zz = (float)z;

                // Nudge along each axis in turn; a nudge that crosses a
                // face boundary is a seam sample, one that does not is
                // the noise floor.
                for (int ax = 0; ax < 3; ax++)
                {
                    float dx = ax == 0 ? Eps : 0, dy = ax == 1 ? Eps : 0, dz = ax == 2 ? Eps : 0;
                    uint c0 = sky.Sample(x - dx, y - dy, zz - dz);
                    uint c1 = sky.Sample(x + dx, y + dy, zz + dz);
                    int d = Diff(c0, c1);
                    if (Face(x - dx, y - dy, zz - dz) != Face(x + dx, y + dy, zz + dz))
                    {
                        seam += d; seamN++;
                        if (d > worst) { worst = d; where = $"({x:F3},{y:F3},{zz:F3})"; }
                    }
                    else { inside += d; insideN++; }
                }
            }
            double sAvg = seamN > 0 ? seam / seamN : 0, iAvg = insideN > 0 ? inside / insideN : 0;
            // Ten times the picture's own roughness is a seam you would
            // see; anything at or under that is the art, not the layout.
            bool ok = seamN > 0 && sAvg <= Math.Max(4.0, iAvg * 10.0);
            if (!ok) bad++;
            Console.WriteLine($"  {set}: across {seamN} seam samples {sAvg:F2}, "
                            + $"within a face {iAvg:F2}, worst {worst} at {where}"
                            + (ok ? "" : "   <-- the faces do not join"));
        }
        Console.WriteLine(bad == 0 ? "OK" : "PROBLEM");
        return bad;
    }

    /// <summary>A pixel painted by a tagged sprite, after shading.</summary>
    static bool Tagged(uint c)
    {
        uint r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        return g == 0 && r == b && r > 0;
    }

    static int Diff(uint a, uint b)
    {
        int r = Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255));
        int g = Math.Abs((int)((a >>  8) & 255) - (int)((b >>  8) & 255));
        int bl= Math.Abs((int)( a        & 255) - (int)( b        & 255));
        return Math.Max(r, Math.Max(g, bl));
    }

    /// <summary>Which of the six the direction lands on, for the seam test.</summary>
    static int Face(float x, float y, float z)
    {
        float ox = x, oy = z, oz = y;
        float ax = Math.Abs(ox), ay = Math.Abs(oy), az = Math.Abs(oz);
        if (ax >= ay && ax >= az) return ox > 0 ? 0 : 1;
        if (ay >= az)             return oy > 0 ? 2 : 3;
        return oz < 0 ? 4 : 5;
    }

    /// <summary>
    /// Whether a two-sided wall's middle can be seen past is decided in
    /// the reference per TEXEL, not per sidedef: every wall part gets
    /// the same cloned base_material_room with
    /// `alpha_rejection greater_equal 64` (general.material:263) and
    /// palette index 254 arrives as alpha 0
    /// (ColorTransformations.cs:256-257). The Ogre client never reads
    /// WF_TRANSPARENT or WF_NOLOOKTHROUGH at all - their one reader in
    /// the whole solution is RooWall.IsBlockingSight
    /// (RooWall.cs:1037-1046), which is targeting and camera collision.
    ///
    /// So the renderer keys off the art. This counts how often the art
    /// and the flags say the same thing, which is what makes the change
    /// safe to make.
    /// </summary>
    static void Holes(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        int agree=0, flagOnly=0, holesOnly=0, art=0;
        var names = new List<string>();
        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo; try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var tc = new TexCache(rm);
            foreach (RooWall w in roo.Walls)
            {
                if (w.RightSide == null || w.LeftSide == null) continue;
                foreach (RooSideDef sd in new[]{ w.RightSide, w.LeftSide })
                {
                    if (sd == null || sd.MiddleTexture == 0) continue;
                    Tex mt = tc.Get(sd.MiddleTexture, 1);
                    if (mt == null) continue;
                    art++;
                    bool fl = sd.Flags.IsTransparent && !sd.Flags.IsNoLookThrough;
                    if (fl == mt.HasHoles) agree++;
                    else if (fl) { flagOnly++; if (names.Count < 8) names.Add($"{Path.GetFileName(path)} grd{sd.MiddleTexture} flagged but solid art"); }
                    else { holesOnly++; if (names.Count < 8) names.Add($"{Path.GetFileName(path)} grd{sd.MiddleTexture} holes but unflagged"); }
                }
            }
        }
        Console.WriteLine($"{art} two-sided middles whose art is present here");
        Console.WriteLine($"  flags and art agree                : {agree}");
        Console.WriteLine($"  flagged see-through, art is solid  : {flagOnly}");
        Console.WriteLine($"  art has holes, not flagged         : {holesOnly}");
        foreach (string n in names) Console.WriteLine($"    {n}");
    }

    /// <summary>
    /// A sprite must be hidden by every surface in front of it, not only
    /// by the one that closed its column.
    ///
    /// The upper part of a wall above a doorway is nearer than a
    /// creature beyond it and closes nothing, so a renderer that tests
    /// against one depth a column paints the creature's head over the
    /// lintel. The reference cannot do that: every room fragment writes
    /// depth (general.material:257-285 has no scene_blend and no
    /// `depth_write off`) and a billboard is depth-tested like anything
    /// else (RemoteNode2D.cpp:11-39).
    ///
    /// This renders each scene twice, once with the column's window
    /// honoured and once without, and counts what the window takes back.
    /// </summary>
    static int Lintel(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        string[] sprites = { "duskrat.bgf", "Knight.bgf", "cyclops.bgf" };
        const int W=640, H=360;
        int scenes=0, through=0, total=0;
        var worst = new List<(double frac,string where)>();

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x).Take(60))
        foreach (string sprName in sprites)
        {
            RooFile roo; try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var bgf = rm.GetObject(sprName);
            if (bgf == null) continue;
            var r = new Renderer(roo, new TexCache(rm));
            var big = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
                .OrderByDescending(l=>{double s2=0;var v=l.Vertices;
                    for(int i=0,j=v.Count-1;i<v.Count;j=i++) s2+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                    return Math.Abs(s2*.5);}).FirstOrDefault();
            if (big == null) continue;
            float cx=big.Vertices.Average(v=>(float)v.X), cy=big.Vertices.Average(v=>(float)v.Y);
            var csec = r.SectorAtPoint(cx,cy); if (csec == null) continue;
            float cz=M59Geo.FloorXY(csec)+Renderer.EyeHeight;

            for (int k=0;k<4;k++)
            {
                float ang = k * MathF.PI / 2f;
                var rng = new Random(k*97 + path.Length*13 + sprName.Length);
                r.Sprites.Clear();
                for (int i=0;i<12;i++)
                {
                    float d = 600f + (float)rng.NextDouble()*9000f;
                    float t = ang + ((float)rng.NextDouble()-0.5f)*1.2f;
                    float sx2 = cx + MathF.Cos(t)*d, sy2 = cy + MathF.Sin(t)*d;
                    var sec = r.SectorAtPoint(sx2, sy2);
                    var tex = Tag(bgf, rng.Next(0, bgf.Frames.Count));
                    if (tex == null) continue;
                    r.Sprites.Add(new Renderer.Sprite {
                        X=sx2, Y=sy2, BaseZ = sec!=null ? M59Geo.FloorXY(sec) : cz-Renderer.EyeHeight,
                        Height=600f, Texture=tex, Tag = i });
                }
                if (r.Sprites.Count == 0) continue;

                Renderer.ClipSprites = false;
                var loose = new uint[W*H]; r.Render(loose, W,H, cx,cy,cz, ang);
                Renderer.ClipSprites = true;
                var tight = new uint[W*H]; r.Render(tight, W,H, cx,cy,cz, ang);
                scenes++;
                int here = 0;
                for (int i=0;i<W*H;i++)
                {
                    // The tag survives shading as green 0 with red
                    // equal to blue, which is what Pick relies on too -
                    // an exact colour test would find nothing, because
                    // the ambient weight scales every channel.
                    bool a = Tagged(loose[i]), b = Tagged(tight[i]);
                    if (a) total++;
                    if (a && !b) { through++; here++; }
                }
                if (here > 0)
                    worst.Add(((double)here/Math.Max(1,W*H), $"{Path.GetFileName(path)} {sprName} {k*90}deg"));
            }
        }
        Console.WriteLine($"{scenes} scenes with sprites, {total} sprite pixels");
        Console.WriteLine($"  drawn through an upper or lower part before: {through}"
                        + (total>0 ? $"  ({100.0*through/total:F1}% of them)" : ""));
        foreach (var w in worst.OrderByDescending(x=>x.frac).Take(5))
            Console.WriteLine($"    {100*w.frac,5:F2}% of the frame  {w.where}");
        // Nothing to prove if the fixture has no sprite art; a run that
        // finds sprites and takes none back would mean the clip does
        // nothing, which is the failure this exists to catch.
        bool ok = total == 0 || through > 0;
        Console.WriteLine(ok ? "OK" : "PROBLEM");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Every replacement room texture decodes, and is the same picture
    /// as the BGF it replaces.
    ///
    /// The reference's default look is these 443 PNGs, loaded as
    /// textures under the very name the room loader asks for
    /// (OgreClient.cpp:685-686, RooFile.cs:2300-2303) so the BGF is
    /// never decoded (Util.h:401-403). They are read here by a PNG
    /// reader of this client's own, which is worth checking: a decode
    /// that silently produced rubbish would paint rubbish on every wall
    /// in the game.
    ///
    /// "The same picture" is tested as the mean colour. A replacement
    /// is hand-redrawn, so it is not the same pixels; but a wall that
    /// was brown stays brown, and a reader that mangled the filter or
    /// the palette would not land anywhere near.
    /// </summary>
    static int RoomTex(string dir)
    {
        string texDir = TexCache.FindRoomTextures(dir);
        if (texDir == null) { Console.WriteLine("  no replacement room textures here, skipped"); return 0; }

        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        int read=0, failed=0, compared=0, near=0, far=0;
        double worst=0; string worstWhere="";
        var names = new List<string>();

        foreach (string path in Directory.GetFiles(texDir, "grd*.png").OrderBy(x=>x))
        {
            uint[] px;
            try { px = M59Png.Read(path, out int w, out int h);
                  if (px == null || w <= 0 || h <= 0) { failed++; if (names.Count<6) names.Add(Path.GetFileName(path)); continue; }
                  read++;
                  // Only the texels that are actually drawn. A decal or
                  // a grate is mostly hole on both sides, and the two
                  // sides fill a hole with different nothings - the
                  // palette's key here, alpha there - so averaging them
                  // in would compare the two kinds of nothing.
                  double r=0,g=0,b=0; long n=0;
                  for (int i=0;i<px.Length;i++)
                  { if ((px[i]>>24) < 64) continue; r += (px[i]>>16)&255; g += (px[i]>>8)&255; b += px[i]&255; n++; }
                  if (n == 0) continue;
                  r/=n; g/=n; b/=n;

                  // The BGF behind it, by the name the loader would use.
                  string stem = Path.GetFileNameWithoutExtension(path);
                  int dash = stem.LastIndexOf('-');
                  if (dash <= 0) continue;
                  if (!int.TryParse(stem.Substring(dash+1), out int frame)) continue;
                  if (!ushort.TryParse(stem.Substring(3, dash-3), out ushort num)) continue;
                  var bgf = rm.GetRoomTexture(num);
                  if (bgf == null || frame >= bgf.Frames.Count) continue;
                  Tex orig = Tex.From(bgf, frame);
                  if (orig == null) continue;
                  double r2=0,g2=0,b2=0; long n2=0;
                  for (int i=0;i<orig.P.Length;i++)
                  { if (orig.P[i] == Tex.Void) continue;
                    r2 += (orig.P[i]>>16)&255; g2 += (orig.P[i]>>8)&255; b2 += orig.P[i]&255; n2++; }
                  if (n2 == 0) continue;
                  r2/=n2; g2/=n2; b2/=n2;
                  compared++;
                  double d = Math.Max(Math.Abs(r-r2), Math.Max(Math.Abs(g-g2), Math.Abs(b-b2)));
                  if (d <= 64) near++;
                  else { far++; if (d > worst) { worst = d;
                         worstWhere = $"{Path.GetFileName(path)}: png ({r:F0},{g:F0},{b:F0}) bgf ({r2:F0},{g2:F0},{b2:F0})"; } }
            }
            catch { failed++; if (names.Count<6) names.Add(Path.GetFileName(path)); }
        }
        Console.WriteLine($"{read} replacement room textures read, {failed} would not decode");
        foreach (string n in names) Console.WriteLine($"    {n}");
        Console.WriteLine($"  {compared} compared against their BGF: {near} the same picture, {far} not");
        if (far > 0) Console.WriteLine($"  worst: {worstWhere}");
        bool ok = failed == 0 && (compared == 0 || far * 20 <= compared);
        Console.WriteLine(ok ? "OK" : "PROBLEM");
        return ok ? 0 : 1;
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
        // PICKROOMS=a.roo,b.roo looks at other rooms with the same test.
        string env = Environment.GetEnvironmentVariable("PICKROOMS");
        if (!string.IsNullOrEmpty(env)) rooms = env.Split(',');
        const int W=640, H=360;

        int checkedPx=0, missed=0, phantom=0, scenes=0;

        foreach (string room in rooms)
        foreach (string sprName in sprites)
        {
            var roo = new RooFile(Path.Combine(dir, room)); roo.ResolveResources(rm);
            var bgf = rm.GetObject(sprName);
            if (bgf == null) continue;

            var r = new Renderer(roo, new TexCache(rm));
            if (Environment.GetEnvironmentVariable("NOSEETHRU") != null) r.SeeThroughWalls = false;
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

                int sceneMissed = 0, scenePhantom = 0;
                var first = new List<string>();
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
                    bool bad = false;
                    if (painted && hit == null) { missed++; sceneMissed++; bad = true; }
                    else if (!painted && hit != null) { phantom++; scenePhantom++; bad = true; }
                    if (bad && first.Count < 4)
                    {
                        var sec = hit != null ? r.SectorAtPoint(hit.X, hit.Y) : null;
                        first.Add($"      ({x},{y}) {(painted ? "painted,unpickable" : "pickable,unpainted")}"
                                + $" with={c:X8} bare={bare[y*W+x]:X8}"
                                + (hit != null ? $" sprite#{hit.Tag} at ({hit.X:F0},{hit.Y:F0}) baseZ={hit.BaseZ:F0}"
                                               + $" sector={(sec==null?"none":sec.Num.ToString())}"
                                               + $" floorTex={(sec==null?0:sec.FloorTexture)} ceilTex={(sec==null?0:sec.CeilingTexture)}" : ""));
                    }
                }
                if (sceneMissed + scenePhantom > 0 || Environment.GetEnvironmentVariable("PICKDIAG") != null)
                {
                    Console.WriteLine($"  {room} {sprName} {k*90}deg: missed {sceneMissed} phantom {scenePhantom}"
                                    + $" (masked spans this frame: {r.LastMaskedSpans})");
                    foreach (string s in first) Console.WriteLine(s);
                }
            }
        }
        Console.WriteLine($"{scenes} scenes, {checkedPx} pixels");
        Console.WriteLine($"  painted but not pickable : {missed}");
        Console.WriteLine($"  pickable but not painted : {phantom}");
        Console.WriteLine(missed==0 && phantom==0 ? "OK" : "MISMATCH");
        return missed + phantom;
    }

    /// <summary>
    /// What honouring WF_TRANSPARENT on two-sided walls actually changes.
    /// Reports rather than fails: this is a fidelity measurement, not a
    /// pass/fail, and the number that matters alongside it is that letting
    /// people see through fences does not leave columns unclosed.
    /// </summary>
    /// <summary>
    /// What per-leaf flat anchoring is actually worth, in pixels.
    ///
    /// The library measures a floor's texture from the leaf's own top-left
    /// corner rather than from the world origin, which only differs for a
    /// leaf reaching into negative coordinates - 8573 leaves of 162787,
    /// and 5404 of those by a fraction of a texture. Counting leaves says
    /// nothing about whether you can see it, so this stands on each
    /// anchored leaf and renders the room both ways.
    /// </summary>
    static void Anchor(string dir)
    {
 var rm=new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
 const int W=480,H=270;
 var best=new List<(double pct,string room,float x,float y,float deg)>();
 int roomsWith=0, roomsChecked=0, views=0; double totalPct=0;

 foreach(string p in Directory.GetFiles(dir,"*.roo").OrderBy(x=>x)){
  RooFile roo; try{ roo=new RooFile(p); roo.ResolveResources(rm);}catch{continue;}
  var anchors=new FlatAnchors(roo);
  if(anchors.Empty) continue;
  roomsChecked++;

  var r=new Renderer(roo,new TexCache(rm));

  // stand on the leaves that actually anchor elsewhere
  var stand=new List<(float x,float y)>();
  foreach(var leaf in roo.BSPTreeLeaves){
   if(leaf.Vertices==null||leaf.Vertices.Count<3) continue;
   float left=0f,top=0f;
   foreach(var v in leaf.Vertices){ if(v.X<left) left=(int)v.X; if(v.Y<top) top=(int)v.Y; }
   if(left==0f&&top==0f) continue;
   if(MathF.Abs(left%1024f)<0.5f&&MathF.Abs(top%1024f)<0.5f) continue;   // whole textures do not move
   stand.Add((leaf.Vertices.Average(v=>(float)v.X), leaf.Vertices.Average(v=>(float)v.Y)));
   if(stand.Count>=8) break;
  }
  if(stand.Count==0) continue;

  double worst=0; float bx=0,by=0,bd=0;
  foreach(var (cx,cy) in stand){
   var sec=r.SectorAtPoint(cx,cy); if(sec==null) continue;
   float cz=M59Geo.FloorXY(sec)+Renderer.EyeHeight;
   for(int k=0;k<8;k++){
    float ang=k*MathF.PI/4f;
    var off=new uint[W*H]; r.LeafAnchoredFlats=false; r.Render(off,W,H,cx,cy,cz,ang);
    var on =new uint[W*H]; r.LeafAnchoredFlats=true;  r.Render(on ,W,H,cx,cy,cz,ang);
    views++;
    int d=0; for(int i=0;i<off.Length;i++) if(off[i]!=on[i]) d++;
    double pct=100.0*d/off.Length;
    totalPct+=pct;
    if(pct>worst){ worst=pct; bx=cx; by=cy; bd=ang*180f/MathF.PI; }
   }
  }
  if(worst>0.01){ roomsWith++; best.Add((worst,Path.GetFileName(p),bx,by,bd)); }
 }

 // What it costs. A bbox test per flat pixel everywhere, and a point in
 // polygon test only inside the anchored leaves' own bounding box, so the
 // rooms to time are the ones that actually have some.
 {
  // The worst case rather than the first case: the room and spot where
  // the anchoring changed the most pixels, so the lookup is running on
  // nearly every flat pixel drawn.
  RooFile timed=null; float tx=0,ty=0;
  var hot=best.OrderByDescending(x=>x.pct).FirstOrDefault();
  if(hot.room!=null){
   try{ timed=new RooFile(Path.Combine(dir,hot.room)); timed.ResolveResources(rm);
        tx=hot.x; ty=hot.y; }catch{ timed=null; }
  }
  if(timed!=null){
   var r2=new Renderer(timed,new TexCache(rm));
   var px=new uint[W*H];
   var sec2=r2.SectorAtPoint(tx,ty);
   float cz2=(sec2!=null?M59Geo.FloorXY(sec2):0f)+Renderer.EyeHeight;
   for(int w2=0;w2<20;w2++){ r2.LeafAnchoredFlats=false; r2.Render(px,W,H,tx,ty,cz2,0f);
                             r2.LeafAnchoredFlats=true;  r2.Render(px,W,H,tx,ty,cz2,0f); }
   var sw=System.Diagnostics.Stopwatch.StartNew();
   for(int i2=0;i2<60;i2++){ r2.LeafAnchoredFlats=false; r2.Render(px,W,H,tx,ty,cz2,0f); }
   double offMs=sw.Elapsed.TotalMilliseconds/60.0; sw.Restart();
   for(int i2=0;i2<60;i2++){ r2.LeafAnchoredFlats=true; r2.Render(px,W,H,tx,ty,cz2,0f); }
   double onMs=sw.Elapsed.TotalMilliseconds/60.0;
   Console.WriteLine($"  cost in {hot.room} at its worst view: {offMs:F2} ms at the origin, {onMs:F2} ms per leaf ({(onMs/Math.Max(0.0001,offMs)-1)*100:F0}% more)");
  }
 }

 Console.WriteLine($"{roomsChecked} rooms have leaves anchored away from the origin; {roomsWith} where it changes the picture");
 Console.WriteLine($"  {views} views, {totalPct/Math.Max(1,views):F2}% of pixels different on average");
 foreach(var b in best.OrderByDescending(x=>x.pct).Take(10))
  Console.WriteLine($"  {b.pct,6:F1}% of pixels  {b.room,-22} {b.x:F0} {b.y:F0} {b.deg:F0}");
    }

    static void SeeThrough(string dir)
    {
 var rm=new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
 const int W=480,H=270;
 var best=new List<(double pct,string room,float x,float y,float deg)>();
 int roomsWith=0, roomsChecked=0; long colsOff=0, colsOn=0, views=0;
 foreach(string p in Directory.GetFiles(dir,"*.roo").OrderBy(x=>x)){
  RooFile roo; try{ roo=new RooFile(p); roo.ResolveResources(rm);}catch{continue;}
  roomsChecked++;
  var r=new Renderer(roo,new TexCache(rm));
  var leaves=roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3).ToList();
  if(leaves.Count==0) continue;
  double worst=0; float bx=0,by=0,bd=0;
  foreach(var leaf in leaves.OrderByDescending(l=>{double s=0;var v=l.Vertices;
      for(int i=0,j=v.Count-1;i<v.Count;j=i++) s+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
      return Math.Abs(s*.5);}).Take(6)){
   float cx=leaf.Vertices.Average(v=>(float)v.X), cy=leaf.Vertices.Average(v=>(float)v.Y);
   var sec=r.SectorAtPoint(cx,cy); if(sec==null) continue;
   float cz=M59Geo.FloorXY(sec)+Renderer.EyeHeight;
   for(int k=0;k<8;k++){
    float ang=k*MathF.PI/4f;
    var off=new uint[W*H]; r.SeeThroughWalls=false; int co=r.Render(off,W,H,cx,cy,cz,ang);
    var on =new uint[W*H]; r.SeeThroughWalls=true;  int cn=r.Render(on ,W,H,cx,cy,cz,ang);
    colsOff+=co; colsOn+=cn; views++;
    int d=0; for(int i=0;i<off.Length;i++) if(off[i]!=on[i]) d++;
    double pct=100.0*d/off.Length;
    if(pct>worst){ worst=pct; bx=cx; by=cy; bd=ang*180f/MathF.PI; }
   }
  }
  if(worst>0.01){ roomsWith++; best.Add((worst,Path.GetFileName(p),bx,by,bd)); }
 }
 Console.WriteLine($"{roomsChecked} rooms, {roomsWith} where see-through walls change the picture");
 Console.WriteLine($"  columns closed, solid walls : {100.0*colsOff/(views*W),5:F2}%");
 Console.WriteLine($"  columns closed, see-through : {100.0*colsOn /(views*W),5:F2}%   ({views} views)");
 foreach(var b in best.OrderByDescending(x=>x.pct).Take(12))
  Console.WriteLine($"  {b.pct,6:F1}% of pixels  {b.room,-22} {b.x:F0} {b.y:F0} {b.deg:F0}");
    }

    /// <summary>
    /// A creature in front of a grate is not behind it.
    ///
    /// See-through walls are drawn after the sprites, because a grate has
    /// to cover what is behind it - so without a per-pixel sprite depth
    /// they cover everything, including creatures standing in front. This
    /// walks a test sprite through the crypt fence in toscrypt2 and reads
    /// how much of it survives.
    ///
    /// The measure is against the sprite's own falloff rather than an
    /// absolute count: an unobstructed billboard shrinks as 1/d^2, so a
    /// step below that is the fence eating it. Crossing the fence the
    /// count should fall off a cliff; approaching it, it should not.
    /// </summary>
    static int Grate(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        string path = Path.Combine(dir, "toscrypt2.roo");
        if (!File.Exists(path)) { Console.WriteLine("toscrypt2.roo not present, skipping"); return 0; }

        var roo = new RooFile(path); roo.ResolveResources(rm);
        var r = new Renderer(roo, new TexCache(rm));
        float cx = 12060, cy = 12587;
        var sec = r.SectorAtPoint(cx, cy);
        if (sec == null) { Console.WriteLine("camera outside the room, skipping"); return 0; }
        float cz = M59Geo.FloorXY(sec) + Renderer.EyeHeight;

        const int W = 640, H = 360;
        var tex = new Tex { W = 1, H = 1, P = new uint[]{ 0xFFFF00FFu } };
        var px = new uint[W*H];

        // The fence sits between 1600 and 1800 units straight ahead.
        float[] d = { 1400f, 1600f, 1800f };
        var seen = new int[d.Length];
        for (int i = 0; i < d.Length; i++)
        {
            r.Sprites.Clear();
            var s2 = r.SectorAtPoint(cx + d[i], cy);
            r.Sprites.Add(new Renderer.Sprite {
                X = cx + d[i], Y = cy,
                BaseZ = s2 != null ? M59Geo.FloorXY(s2) : cz - Renderer.EyeHeight,
                Height = 700f, Texture = tex });
            r.Render(px, W, H, cx, cy, cz, 0f);
            int n = 0;
            for (int k = 0; k < px.Length; k++)
            {
                uint c = px[k];
                if (((c>>8)&0xFF) == 0 && ((c>>16)&0xFF) == (c&0xFF) && (c&0xFF) > 0) n++;
            }
            seen[i] = n;
        }

        double predictNear = seen[0] * (d[0]/d[1]) * (d[0]/d[1]);   // 1400 -> 1600
        double predictFar  = seen[1] * (d[1]/d[2]) * (d[1]/d[2]);   // 1600 -> 1800
        double nearRatio = seen[1] / predictNear;
        double farRatio  = seen[2] / predictFar;

        Console.WriteLine($"  in front of the fence : {seen[1],6} px, {nearRatio*100,5:F0}% of the 1/d^2 prediction");
        Console.WriteLine($"  behind it             : {seen[2],6} px, {farRatio*100,5:F0}% of the 1/d^2 prediction");

        int bad = 0;
        if (nearRatio < 0.95) { Console.WriteLine("  FAIL the fence is eating a sprite in front of it"); bad++; }
        if (farRatio > 0.80)  { Console.WriteLine("  FAIL the fence is not covering a sprite behind it"); bad++; }
        Console.WriteLine(bad == 0 ? "OK" : "PROBLEM");
        return bad;
    }

    /// <summary>
    /// The closed form for where a screen row meets a sloped floor, against
    /// a bisection that just walks the ray until it crosses the plane.
    ///
    /// The plumbing around it is ordinary; the algebra is the part that
    /// could be quietly wrong, and a wrong sign there would put a slope's
    /// texture somewhere plausible-looking but not where the geometry is.
    /// </summary>
    static int SlopeMath(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        int tested = 0, bad = 0;
        double worst = 0;

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            foreach (RooSector sec in roo.Sectors)
            {
                var slope = sec.SlopeInfoFloor;
                if (slope == null) continue;

                // A camera somewhere above the plane, looking about.
                float cx = 10000f, cy = 12000f;
                float planeAt = M59Geo.Plane(slope, cx, cy);
                float camZ = planeAt + 800f;

                for (int k = 0; k < 6; k++)
                {
                    float ang = k * MathF.PI / 3f;
                    float rdx = MathF.Cos(ang), rdy = MathF.Sin(ang);
                    for (float sSlope = 0.05f; sSlope < 1.2f; sSlope += 0.17f)
                    {
                        if (!Renderer.SolveSlope(slope, cx, cy, camZ, rdx, rdy, sSlope, 1f, out float d))
                            continue;
                        if (d > 1e6f) continue;
                        tested++;

                        // Bisection: the height the ray is at, minus the
                        // plane's height, changes sign at the answer.
                        Func<float,float> gap = dd =>
                            (camZ - sSlope*dd) - M59Geo.Plane(slope, cx + rdx*dd, cy + rdy*dd);
                        float lo = 0f, hi = d * 2f;
                        if (gap(lo) * gap(hi) > 0f) continue;      // no crossing bracketed
                        for (int i = 0; i < 80; i++)
                        {
                            float mid = 0.5f*(lo+hi);
                            if (gap(lo) * gap(mid) <= 0f) hi = mid; else lo = mid;
                        }
                        float found = 0.5f*(lo+hi);
                        double err = Math.Abs(found - d) / Math.Max(1.0, Math.Abs(found));
                        if (err > worst) worst = err;
                        if (err > 1e-3) bad++;
                    }
                }
            }
        }
        Console.WriteLine($"  slope solve: {tested} rays, {bad} disagree with a bisection");
        Console.WriteLine($"  worst relative error: {worst:E2}");
        Console.WriteLine(bad == 0 ? "OK" : "PROBLEM");
        return bad;
    }

    /// <summary>
    /// Scrolling floors, ceilings and walls - water, lava, the odd moving
    /// wall. Renders each room that has any at two different times and
    /// counts the ones whose picture actually changes.
    ///
    /// This is a "does it do anything" check rather than a correctness one:
    /// the rate comes from the library's own formula, but which way a river
    /// flows on screen is a mapping between compass directions and texture
    /// axes that nothing here can confirm.
    /// </summary>
    static int Scrolling(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        const int W = 320, H = 180;
        int roomsWith = 0, roomsWithArt = 0, roomsMoving = 0, viewsChecked = 0;

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }

            bool any = roo.Sectors.Any(s2 => s2.Flags.ScrollSpeed != TextureScrollSpeed.NONE
                                          && (s2.Flags.IsScrollFloor || s2.Flags.IsScrollCeiling))
                    || roo.SideDefs.Any(sd => sd.Flags.ScrollSpeed != TextureScrollSpeed.NONE);
            if (!any) continue;
            roomsWith++;

            var tcx = new TexCache(rm);
            bool haveArt = roo.Sectors.Any(s2 => s2.Flags.ScrollSpeed != TextureScrollSpeed.NONE
                        && ((s2.Flags.IsScrollFloor && tcx.Get(s2.FloorTexture) != null)
                         || (s2.Flags.IsScrollCeiling && tcx.Get(s2.CeilingTexture) != null)));
            if (haveArt) roomsWithArt++;

            var r = new Renderer(roo, tcx);
            bool moved = false;

            // Stand in each of the biggest leaves and look all round: the
            // scrolling surface has to be on screen for this to show.
            foreach (var leaf in roo.BSPTreeLeaves
                     .Where(l=>l.Vertices!=null&&l.Vertices.Count>=3&&l.Sector!=null)
                     .OrderByDescending(l=>{double s3=0;var v=l.Vertices;
                        for(int i=0,j=v.Count-1;i<v.Count;j=i++) s3+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                        return Math.Abs(s3*.5);}).Take(12))
            {
                if (moved) break;
                float cx = leaf.Vertices.Average(v=>(float)v.X), cy = leaf.Vertices.Average(v=>(float)v.Y);
                var sec = r.SectorAtPoint(cx, cy);
                if (sec == null) continue;
                float cz = M59Geo.FloorXY(sec) + Renderer.EyeHeight;

                for (int k = 0; k < 8 && !moved; k++)
                {
                    float ang = k * MathF.PI / 4f;
                    var a0 = new uint[W*H]; r.Time = 0f; r.Render(a0, W,H, cx,cy,cz, ang);
                    var a1 = new uint[W*H]; r.Time = 3f; r.Render(a1, W,H, cx,cy,cz, ang);
                    viewsChecked++;
                    for (int i = 0; i < a0.Length; i++)
                        if (a0[i] != a1[i]) { moved = true; break; }
                }
            }
            if (moved) roomsMoving++;
        }

        Console.WriteLine($"  rooms with something set to scroll : {roomsWith}");
        Console.WriteLine($"  of those, whose art is present here: {roomsWithArt}");
        Console.WriteLine($"  rooms where the picture moves      : {roomsMoving}   ({viewsChecked} views)");
        // Most scrolling surfaces are water and lava whose textures are not
        // in a partial resource folder, so they render as missing-texture
        // grey and cannot move. The check is that it moves where the art
        // exists, not that it moves everywhere.
        Console.WriteLine(roomsMoving > 0 ? "OK" : "PROBLEM");
        return roomsMoving > 0 ? 0 : 1;
    }

    /// <summary>
    /// Animated wall textures. The library drives these by moving the
    /// sidedef's animation to another group and picking a different frame
    /// of the same file; this renderer used frame 0 every time, which is a
    /// torch that never flickers.
    ///
    /// Renders a room, ticks it forward, renders again, and counts the
    /// rooms whose picture changes.
    /// </summary>
    static int Animated(string dir)
    {
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);
        const int W = 320, H = 180;
        int roomsWith = 0, roomsWithArt = 0, roomsMoving = 0, roomsLibraryBound = 0;

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }

            var animated = roo.SideDefs.Where(sd => sd.Animation != null).ToList();
            if (animated.Count == 0) continue;
            roomsWith++;

            var tcx = new TexCache(rm);
            // Only a MIDDLE texture can actually animate, and that is the
            // library's doing, not this renderer's: RooSideDef.ResolveResources
            // sets the animation's GroupMax from ResourceMiddle alone
            // (RooSideDef.cs:357-358), and the two branches that would set it
            // from the lower or upper texture are commented out in the library
            // (:361-365). With GroupMax left at 0 the cycle never wraps - it
            // only resets when CurrentGroup reaches GroupMax
            // (AnimationCycle.cs:281-284) - so the group runs away to 20, 40,
            // 60, GetFrameIndex returns -1 for all of them, and the wall shows
            // frame 0 for ever. The Ogre client reads the same animation off
            // the same sidedef, so its torches sit just as still. Counting
            // those rooms as failures here would be asking this port to be
            // better than the thing it is a port of.
            bool haveArt = false, haveLowerUpperOnly = false;
            foreach (var sd in animated)
            {
                bool midMoves = false, edgeMoves = false;
                try
                {
                    if (sd.MiddleTexture != 0)
                    {
                        var b = rm.GetRoomTexture(sd.MiddleTexture);
                        if (b != null && b.FrameSets.Count > 1) midMoves = true;
                    }
                    foreach (ushort n in new[]{ sd.UpperTexture, sd.LowerTexture })
                    {
                        if (n == 0) continue;
                        var b = rm.GetRoomTexture(n);
                        if (b != null && b.FrameSets.Count > 1) edgeMoves = true;
                    }
                }
                catch { }
                if (midMoves) haveArt = true;
                else if (edgeMoves) haveLowerUpperOnly = true;
            }
            if (haveArt) roomsWithArt++;
            else if (haveLowerUpperOnly) roomsLibraryBound++;

            var r = new Renderer(roo, tcx);
            bool moved = false;

            foreach (var leaf in roo.BSPTreeLeaves
                     .Where(l=>l.Vertices!=null&&l.Vertices.Count>=3&&l.Sector!=null)
                     .OrderByDescending(l=>{double s3=0;var v=l.Vertices;
                        for(int i=0,j=v.Count-1;i<v.Count;j=i++) s3+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                        return Math.Abs(s3*.5);}).Take(10))
            {
                if (moved) break;
                float cx = leaf.Vertices.Average(v=>(float)v.X), cy = leaf.Vertices.Average(v=>(float)v.Y);
                var sec = r.SectorAtPoint(cx, cy);
                if (sec == null) continue;
                float cz = M59Geo.FloorXY(sec) + Renderer.EyeHeight;

                for (int k = 0; k < 8 && !moved; k++)
                {
                    float ang = k * MathF.PI / 4f;
                    var a0 = new uint[W*H]; r.Render(a0, W,H, cx,cy,cz, ang);
                    // Walk the room's clock forward past any plausible
                    // animation period.
                    for (int step = 1; step <= 40; step++)
                        try { roo.Tick(step * 250.0, 250.0); } catch { }
                    var a1 = new uint[W*H]; r.Render(a1, W,H, cx,cy,cz, ang);
                    for (int i = 0; i < a0.Length; i++)
                        if (a0[i] != a1[i]) { moved = true; break; }
                }
            }
            if (moved) roomsMoving++;
        }

        // The rooms that animate mostly want art that is not in a partial
        // resource folder, so the mechanism is also checked directly: a
        // multi-frame texture must give a different picture for a
        // different animation group. That is this renderer's own code
        // path, independent of which rooms happen to be installed.
        int multi = 0, distinct = 0;
        var tc2 = new TexCache(rm);
        foreach (string f in Directory.GetFiles(dir, "grd*.bgf").OrderBy(x=>x))
        {
            if (!ushort.TryParse(Path.GetFileNameWithoutExtension(f).Substring(3), out ushort num)) continue;
            Meridian59.Files.BGF.BgfFile b;
            try { b = rm.GetRoomTexture(num); } catch { continue; }
            if (b == null || b.Frames.Count < 2 || b.FrameSets.Count < 2) continue;
            multi++;

            var byGroup = new HashSet<string>();
            for (ushort g = 1; g <= Math.Min(4, b.FrameSets.Count); g++)
            {
                Tex t = tc2.Get(num, g);
                if (t == null) continue;
                // A cheap signature of the frame actually handed back.
                long sum = 0;
                for (int i = 0; i < t.P.Length; i += 37) sum += t.P[i];
                byGroup.Add($"{t.W}x{t.H}:{sum}");
            }
            if (byGroup.Count > 1) distinct++;
        }
        Console.WriteLine($"  multi-frame textures present       : {multi}, of which {distinct} give a different picture per group");

        Console.WriteLine($"  rooms with an animated sidedef     : {roomsWith}");
        Console.WriteLine($"  of those, with an animated MIDDLE  : {roomsWithArt}");
        Console.WriteLine($"  only lower/upper, which the library cannot drive: {roomsLibraryBound}");
        Console.WriteLine($"  rooms where the picture changes    : {roomsMoving}");
        bool roomsOk = roomsWithArt == 0 || roomsMoving > 0;
        bool mechOk = multi == 0 || distinct > 0;
        Console.WriteLine(roomsOk && mechOk ? "OK" : "PROBLEM");
        return roomsOk && mechOk ? 0 : 1;
    }
}
