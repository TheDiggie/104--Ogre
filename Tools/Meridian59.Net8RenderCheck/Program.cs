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

    static int Main(string[] a)
    {
        string mode = a.Length > 0 ? a[0].ToLowerInvariant() : "all";
        string dir  = a.Length > 1 ? a[1] : "/tmp/res";
        int bad = 0;
        if (mode == "repack"  || mode == "all") bad += RepackCheck();
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
        if (mode == "roomtex" || mode == "all") bad += RoomTex(dir);
        return bad == 0 ? 0 : 1;
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
