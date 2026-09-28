using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Meridian59.Common;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>
/// Checks the object composer against things that can be known without a
/// server: that a plain object composes to exactly the frame it would
/// have drawn before, that adding a part actually adds its pixels where
/// the library says they go, and that overlays and underlays end up on
/// the right side of the body.
/// </summary>
static class Program
{
    static string dir = "/tmp/res";
    static int fails = 0;
    static bool Verbose = true;

    static void Main(string[] args)
    {
        bool mode = args.Length > 0 && (args[0] == "probe" || args[0] == "crush" || args[0] == "sheet" || args[0] == "items");
        if (args.Length > 0 && !mode) dir = args[0];
        if (args.Length > 0 && args[0] == "probe") { Probe(); return; }
        if (args.Length > 0 && args[0] == "crush") { Crush(args.Length > 1 ? args[1] : dir); return; }
        if (args.Length > 0 && args[0] == "sheet") { Sheet(args.Length > 1 ? args[1] : "compose.png"); return; }
        if (args.Length > 0 && args[0] == "items")
        { Items(args.Length > 1 ? args[1] : "items.png", args.Skip(2).ToArray()); return; }
        PlainMatchesFrame();
        PartAppears();
        OverUnder();
        Console.WriteLine(fails == 0 ? "OK" : $"{fails} FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>
    /// How much of the art this client cannot decode at all. Frames come
    /// in two compressions: one the library undoes with .NET's own
    /// inflate, and CRUSH, which it can only undo in an x86 Windows build
    /// and throws on everywhere else - here, and on a phone. Counting
    /// them says whether that is a curiosity or a hole in the game.
    /// </summary>
    static void Crush(string where)
    {
        int files = 0, frames = 0, bad = 0, badFiles = 0, uncompressed = 0;
        var worst = new List<string>();

        foreach (string f in Directory.GetFiles(where, "*.bgf").OrderBy(x => x))
        {
            BgfFile b;
            try { b = new BgfFile(f); } catch { continue; }
            files++;
            int fileBad = 0;
            foreach (BgfBitmap fr in b.Frames)
            {
                frames++;
                if (!fr.IsCompressed) { uncompressed++; continue; }
                try { fr.Decompress(fr.PixelData); }
                catch { bad++; fileBad++; }
            }
            if (fileBad > 0)
            {
                badFiles++;
                if (worst.Count < 10) worst.Add($"{Path.GetFileName(f)} {fileBad}/{b.Frames.Count}");
            }
        }

        Console.WriteLine($"{files} files, {frames} frames, {uncompressed} stored uncompressed");
        Console.WriteLine($"{bad} frames ({100.0 * bad / Math.Max(1, frames):F2}%) in {badFiles} files cannot be decoded here");
        foreach (string w in worst) Console.WriteLine("  " + w);
    }

    /// <summary>
    /// A picture to look at: a body bare, the same body wearing a part
    /// over a hotspot, and the same underneath one. Composition is the
    /// kind of thing that passes every count and still looks wrong, so
    /// there is something to put an eye on.
    /// </summary>
    /// <summary>
    /// A row of ordinary objects - the things actually lying about a room
    /// and sitting in a pack - composed and scaled to a common height, so
    /// the pictures can be compared with what the game shows.
    /// </summary>
    static void Items(string path, string[] files)
    {
        if (files.Length == 0)
            files = new[] { "corncob.bgf", "carrot.bgf", "broccoli.bgf", "banana.bgf",
                            "cookie.bgf", "doubloon.bgf", "ankh.bgf", "dyebottle.bgf",
                            "book1.bgf", "duskrat.bgf" };

        const int tall = 140, gap = 10;
        var shots = new List<Tex>();
        var names = new List<string>();

        foreach (string f in files)
        {
            BgfFile bgf;
            try { bgf = Load(f); } catch { Console.WriteLine($"  {f}: missing"); continue; }
            RoomObject o = Make(bgf);
            Tex t = M59Compose.Build(o, out _, out float wh);
            if (t == null) { Console.WriteLine($"  {f}: nothing to draw"); continue; }
            shots.Add(Fit(t, tall));
            names.Add(Path.GetFileNameWithoutExtension(f));
            Console.WriteLine($"  {f}: {t.W}x{t.H}, {wh:F0} world units tall");
        }

        int w = gap, h = tall + gap * 2;
        foreach (Tex t in shots) w += t.W + gap;

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        { rgba[i * 4] = 60; rgba[i * 4 + 1] = 60; rgba[i * 4 + 2] = 70; rgba[i * 4 + 3] = 255; }

        int ox = gap;
        foreach (Tex t in shots)
        {
            // sat on a common baseline, the way they stand in the world
            int oy = gap + (tall - t.H);
            for (int y = 0; y < t.H; y++)
                for (int x = 0; x < t.W; x++)
                {
                    uint c = t.P[y * t.W + x];
                    if ((c >> 24) == 0) continue;
                    int o = ((y + oy) * w + ox + x) * 4;
                    rgba[o] = (byte)(c >> 16); rgba[o + 1] = (byte)(c >> 8);
                    rgba[o + 2] = (byte)c; rgba[o + 3] = 255;
                }
            ox += t.W + gap;
        }

        Png.Write(path, w, h, rgba);
        Console.WriteLine($"wrote {path} ({w}x{h})");
    }

    /// <summary>Nearest-neighbour shrink to a given height, keeping aspect.</summary>
    static Tex Fit(Tex t, int height)
    {
        if (t.H <= height) return t;
        int nw = Math.Max(1, t.W * height / t.H);
        var p = new uint[nw * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < nw; x++)
                p[y * nw + x] = t.P[(y * t.H / height) * t.W + (x * t.W / nw)];
        return new Tex { W = nw, H = height, P = p, Shrink = t.Shrink };
    }

    static void Sheet(string path)
    {
        BgfFile body = Load("bri.bgf");   // the first-person arm, which carries hotspots

        var shots = new List<Tex>();
        var names = new List<string>();

        RoomObject plain = Make(body);
        shots.Add(M59Compose.Build(plain, out _, out _)); names.Add("bare");

        // The real thing: the "ov" files are the held-item overlays the
        // server pins to a body's hand hotspot - an axe, a sword, a wand.
        // Nothing else in the game is composed onto a body.
        foreach (string held in new[] { "neruaxeov.bgf", "spirswordov.bgf", "wandov.bgf" })
        {
            BgfFile part;
            try { part = Load(held); } catch { continue; }
            if (!Dress(body, part, true, out _, out RoomObject armed, out _)) continue;
            shots.Add(M59Compose.Build(armed, out _, out _));
            names.Add(Path.GetFileNameWithoutExtension(held));
        }

        // An object whose art floats above its anchor: the box is mostly
        // empty and the art sits at the top of it.
        RoomObject arrow = Make(Load("arrowfir.bgf"));
        shots.Add(M59Compose.Build(arrow, out _, out _)); names.Add("arrowfir");

        int gap = 8;
        int w = gap, h = 0;
        foreach (Tex t in shots) { if (t == null) continue; w += t.W + gap; h = Math.Max(h, t.H); }
        h += gap * 2;

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            // a mid grey, so both the art and the empty parts of a box show
            rgba[i * 4] = 60; rgba[i * 4 + 1] = 60; rgba[i * 4 + 2] = 70; rgba[i * 4 + 3] = 255;
        }

        int ox = gap;
        for (int n = 0; n < shots.Count; n++)
        {
            Tex t = shots[n];
            if (t == null) continue;
            for (int y = 0; y < t.H; y++)
                for (int x = 0; x < t.W; x++)
                {
                    uint c = t.P[y * t.W + x];
                    if ((c >> 24) == 0) continue;
                    int o = ((y + gap) * w + ox + x) * 4;
                    rgba[o] = (byte)(c >> 16); rgba[o + 1] = (byte)(c >> 8);
                    rgba[o + 2] = (byte)c; rgba[o + 3] = 255;
                }
            Console.WriteLine($"  {names[n]}: {t.W}x{t.H} at x={ox}");
            ox += t.W + gap;
        }

        Png.Write(path, w, h, rgba);
        Console.WriteLine($"wrote {path} ({w}x{h})");
    }

    static void Probe()
    {
        var b = Load("bri.bgf");
        Console.WriteLine($"bri frames={b.Frames.Count} sets={b.FrameSets.Count} shrink={b.ShrinkFactor}");
        for (int g = 0; g <= Math.Min(3, b.FrameSets.Count); g++)
            for (int a = 0; a < 4096; a += 1024)
            {
                int i = b.GetFrameIndex(g, (ushort)a);
                int sp = (i >= 0 && i < b.Frames.Count) ? b.Frames[i].HotSpots.Count : -1;
                Console.WriteLine($"  g={g} a={a} idx={i} spots={sp}");
            }
        int tot = 0;
        for (int i = 0; i < b.Frames.Count; i++) if (b.Frames[i].HotSpots.Count > 0) tot++;
        Console.WriteLine($"frames with spots: {tot} of {b.Frames.Count}");
        var spotFrames = new List<int>();
        for (int i = 0; i < b.Frames.Count; i++) if (b.Frames[i].HotSpots.Count > 0) spotFrames.Add(i);
        Console.WriteLine("frames with spots: " + string.Join(",", spotFrames));
        var reachable = new HashSet<int>();
        for (int g = 1; g <= b.FrameSets.Count; g++)
            for (int a = 0; a < 4096; a += 32)
            { int i = b.GetFrameIndex(g, (ushort)a); if (i >= 0) reachable.Add(i); }
        Console.WriteLine("reachable frames: " + reachable.Count + " of " + b.Frames.Count);
        int both = 0; foreach (int i in spotFrames) if (reachable.Contains(i)) both++;
        Console.WriteLine("reachable frames that carry spots: " + both);

        var p = Load("icraftleatha.bgf");
        Console.WriteLine($"part frames={p.Frames.Count} sets={p.FrameSets.Count} idx(1,0)={p.GetFrameIndex(1, 0)} idx(0,0)={p.GetFrameIndex(0, 0)}");
    }

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) fails++;
    }

    static BgfFile Load(string name) => new BgfFile(Path.Combine(dir, name));

    static RoomObject Make(BgfFile bgf)
    {
        var o = new RoomObject();
        o.Resource = bgf;
        o.Tick(0, 1);      // settles frame indices and the appearance hash
        return o;
    }

    /// <summary>
    /// An object with no parts and no frame offsets must compose to
    /// exactly the picture the old single-frame path drew - same size,
    /// same pixels. That is the regression guard: composition must not
    /// quietly resize the objects that were already right.
    ///
    /// Objects WITH frame offsets are expected to differ, and the
    /// difference is the point. The offset is not a nudge applied to a
    /// frame-sized picture; it is part of the box. An arrow's art carries
    /// a Y offset of -200 at shrink 5, which makes the composed picture
    /// 208 pixels tall with the art at the bottom of it - the empty space
    /// above is how far above its anchor the arrow is meant to float. The
    /// old path threw that away and drew every object as though its art
    /// began at its feet.
    /// </summary>
    static void PlainMatchesFrame()
    {
        Console.WriteLine("plain objects compose to their own frame");
        int plain = 0, offsetted = 0, sizeDiffs = 0, pixelDiffs = 0, heightDiffs = 0, frameMissing = 0;

        foreach (string f in Directory.GetFiles(dir, "*.bgf").OrderBy(x => x))
        {
            BgfFile bgf;
            try { bgf = new BgfFile(f); } catch { continue; }
            if (bgf.Frames.Count == 0) continue;

            RoomObject o = Make(bgf);
            if (o.ViewerFrame == null) continue;

            Tex composed = M59Compose.Build(o, out float ww, out float wh);
            Tex single = Tex.FromSprite(bgf, o.ViewerFrameIndex);
            if (composed == null || single == null) continue;

            var ri = new RenderInfo(o, true, true);
            bool clamped = ri.Scaling != bgf.ShrinkFactor;   // quality cap kicked in
            bool offsets = o.ViewerFrame.XOffset != 0 || o.ViewerFrame.YOffset != 0;

            if (offsets || clamped) { offsetted++; continue; }
            plain++;

            if (composed.W != single.W || composed.H != single.H)
            {
                sizeDiffs++;
                Console.WriteLine($"    size: {Path.GetFileName(f)} {single.W}x{single.H} -> {composed.W}x{composed.H} dim {ri.Dimension.X}x{ri.Dimension.Y}");
                continue;
            }

            for (int i = 0; i < composed.P.Length; i++)
            {
                bool ca = (composed.P[i] >> 24) != 0, sa = (single.P[i] >> 24) != 0;
                // Colour is compared only where both say there is a pixel:
                // the composer leaves untouched pixels at zero and the
                // single frame writes the palette's transparent entry,
                // which are the same hole written two ways.
                if (ca != sa || (ca && (composed.P[i] & 0xFFFFFF) != (single.P[i] & 0xFFFFFF)))
                {
                    pixelDiffs++;
                    Console.WriteLine($"    pixels: {Path.GetFileName(f)} at {i % composed.W},{i / composed.W} " +
                        $"composed {composed.P[i]:X8} single {single.P[i]:X8} compressed={bgf.Frames[o.ViewerFrameIndex].IsCompressed}");
                    break;
                }
            }

            float oldH = single.H / (float)Math.Max(1, single.Shrink) * M59Geo.HeightToXY;
            if (MathF.Abs(oldH - wh) > 0.01f * MathF.Max(1f, oldH)) heightDiffs++;

            // The frame must be somewhere in the picture, not clipped out.
            if (ri.Bgf == null) frameMissing++;
        }

        Console.WriteLine($"    {plain} plain, {offsetted} carrying offsets or clamped by the quality cap");
        Check(plain > 100, $"{plain} objects composed with nothing to move them");
        Check(sizeDiffs == 0, $"{sizeDiffs} size differences");
        Check(pixelDiffs == 0, $"{pixelDiffs} pixel differences");
        Check(heightDiffs == 0, $"{heightDiffs} world-height differences from the single-frame rule");
        Check(frameMissing == 0, $"{frameMissing} objects lost their main frame");
        Check(offsetted > 0, $"{offsetted} objects the single-frame path placed or sized wrongly");
    }

    /// <summary>
    /// Dresses a body: pins a part to a hotspot, at a viewing angle where
    /// that hotspot is actually on the frame being drawn. Not every frame
    /// of a body carries every hotspot - a hand that is behind the body
    /// at this angle has none - so the angle is searched for rather than
    /// assumed.
    /// </summary>
    static bool Dress(BgfFile body, BgfFile part, bool over,
                      out RoomObject bare, out RoomObject dressed, out RenderInfo ri)
    {
        bare = null; dressed = null; ri = null;

        for (int g = 1; g <= body.FrameSets.Count; g++)
        for (int a = 0; a < 4096; a += 128)
        {
            var o = new RoomObject();
            o.Resource = body;
            // AnimationNone ignores writes to CurrentGroup - its setter
            // is empty - and takes the group through its own Group
            // property instead.
            var anim = new AnimationNone(); anim.Group = (ushort)g;
            o.Animation = anim;
            o.ViewerAngle = (ushort)a;
            o.Tick(0, 1);
            if (o.ViewerFrame == null) continue;

            foreach (BgfBitmapHotspot hs in o.ViewerFrame.HotSpots)
            {
                if (over ? hs.Index <= 0 : hs.Index >= 0) continue;

                var d = new RoomObject();
                d.Resource = body;
                var danim = new AnimationNone(); danim.Group = (ushort)g;
                d.Animation = danim;
                d.ViewerAngle = (ushort)a;
                var sub = new SubOverlay(0, new AnimationNone(), (byte)Math.Abs((int)hs.Index), 0, 0);
                sub.Resource = part;
                d.SubOverlays.Add(sub);
                d.Tick(0, 1);

                var r = new RenderInfo(d, true, true);
                if (r.SubBgf.Count != 1)
                {
                    if (Verbose)
                        Console.WriteLine($"    g={g} a={a} spot={hs.Index} -> subs={r.SubBgf.Count} " +
                            $"subViewerFrame={(sub.ViewerFrame == null ? "null" : "ok")} " +
                            $"subHotspot={(sub.ViewerHotspot == null ? "null" : sub.ViewerHotspot.Index.ToString())} " +
                            $"subParent={(sub.ViewerParent == null ? "null" : "sub")} " +
                            $"subRes={(sub.Resource == null ? "null" : "ok")}");
                    Verbose = false;
                    continue;
                }

                bare = o; dressed = d; ri = r;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds a body with hotspots and hangs a part on one, then checks the
    /// part's pixels turn up in the composed picture - and in the right
    /// place, which is the library's own origin for that part.
    /// </summary>
    static void PartAppears()
    {
        Console.WriteLine("a part lands where RenderInfo puts it");

        BgfFile body = Load("bri.bgf");   // the first-person arm, which carries hotspots
        BgfFile part = Load("icraftleatha.bgf");

        if (!Dress(body, part, true, out RoomObject bare, out RoomObject dressed, out RenderInfo ri))
        { Check(false, "found a body frame with an over hotspot to dress"); return; }
        Check(true, "found a body frame with an over hotspot to dress");

        Tex without = M59Compose.Build(bare, out _, out _);
        Tex with = M59Compose.Build(dressed, out _, out _);
        Check(with != null && without != null, "both composed");
        if (with == null || without == null) return;

        var s0 = ri.SubBgf[0];
        int px = (int)s0.Origin.X, py = (int)s0.Origin.Y;
        int pw = (int)s0.Size.X, ph = (int)s0.Size.Y;
        Check(pw >= 1 && ph >= 1, $"part size {pw}x{ph}");
        Check(px >= 0 && py >= 0 && px + pw <= with.W && py + ph <= with.H,
              $"part box {px},{py} {pw}x{ph} fits in {with.W}x{with.H}");

        int opaque = 0;
        for (int y = py; y < Math.Min(py + ph, with.H); y++)
            for (int x = px; x < Math.Min(px + pw, with.W); x++)
                if (x >= 0 && y >= 0 && (with.P[y * with.W + x] >> 24) != 0) opaque++;
        Check(opaque > 0, $"{opaque} pixels drawn inside the part's box");

        Check(with.W >= without.W && with.H >= without.H,
              $"picture grew or held: {without.W}x{without.H} -> {with.W}x{with.H}");
    }

    /// <summary>
    /// The sign of the hotspot decides whether a part is drawn over the
    /// body or behind it. Test the outcome, not the flag: paint the part
    /// solid and count how many of its pixels survive in the overlap.
    /// </summary>
    static void OverUnder()
    {
        Console.WriteLine("overlays win the overlap, underlays lose it");

        BgfFile body = Load("bri.bgf");   // the first-person arm, which carries hotspots
        // The body wearing a copy of itself: a synthetic pairing, but the
        // only one guaranteed to overlap heavily enough that the draw
        // order is measurable. A real sword covers a few dozen pixels of
        // a body; that is too few to tell "drawn behind" from "drawn a
        // little to the left".
        // The arm wearing a copy of itself: a fixture, not anything the
        // game composes, and the only pairing guaranteed to overlap
        // heavily enough that the draw order is measurable. A real weapon
        // covers a few dozen pixels of a hand; that is too few to tell
        // "drawn behind" from "drawn a little to the left".
        BgfFile part = Load("bri.bgf");

        float over = Overlap(body, part, true, out int overPixels);
        float under = Overlap(body, part, false, out int underPixels);

        Console.WriteLine($"    over: {over:P1} of {overPixels} overlapping pixels came from the part");
        Console.WriteLine($"    under: {under:P1} of {underPixels}");
        Check(overPixels > 100 && underPixels > 100, "both cases overlap the body enough to judge");
        // The underlay test is the exact one: drawn behind the body, it
        // must change nothing at all where the body is opaque. The
        // overlay test cannot be exact, because the part here is a copy
        // of the body's own art and a good share of the pixels it covers
        // it repaints in the same colour.
        Check(under == 0f, $"an underlay changes nothing behind the body ({under:P1})");
        Check(over > 0.25f, $"an overlay repaints the body in front of it ({over:P1})");
    }

    /// <summary>
    /// The share of pixels, in the part's box and where the body is
    /// already opaque, that the part changed. Only the overlap counts:
    /// outside it both kinds of part draw freely and tell you nothing
    /// about the order they were drawn in.
    /// </summary>
    static float Overlap(BgfFile body, BgfFile part, bool over, out int overlapping)
    {
        overlapping = 0;
        if (!Dress(body, part, over, out RoomObject bare, out RoomObject dressed, out RenderInfo ri))
            return 0f;

        Tex a = M59Compose.Build(bare, out _, out _);
        Tex b = M59Compose.Build(dressed, out _, out _);
        if (a == null || b == null) return 0f;

        // Where the body alone has pixels, in the dressed picture's
        // frame of reference: the two boxes can differ in size, so the
        // bare picture is located by the library's own origin for the
        // main frame in each.
        var bareRi = new RenderInfo(bare, true, true);
        int dx = Convert.ToInt32(ri.Origin.X - bareRi.Origin.X);
        int dy = Convert.ToInt32(ri.Origin.Y - bareRi.Origin.Y);

        var s = ri.SubBgf[0];
        int px = Math.Max(0, Convert.ToInt32(s.Origin.X)), py = Math.Max(0, Convert.ToInt32(s.Origin.Y));
        int pw = Convert.ToInt32(s.Size.X), ph = Convert.ToInt32(s.Size.Y);

        int changed = 0;
        for (int y = py; y < Math.Min(py + ph, b.H); y++)
            for (int x = px; x < Math.Min(px + pw, b.W); x++)
            {
                int ax = x - dx, ay = y - dy;
                if (ax < 0 || ay < 0 || ax >= a.W || ay >= a.H) continue;
                uint ap = a.P[ay * a.W + ax];
                if ((ap >> 24) == 0) continue;      // body has nothing here

                overlapping++;
                if (b.P[y * b.W + x] != ap) changed++;
            }
        return overlapping == 0 ? 0f : (float)changed / overlapping;
    }
}
