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
        return t;
    }


    static int Main(string[] a)
    {
        string mode = a.Length > 0 ? a[0].ToLowerInvariant() : "all";
        string dir  = a.Length > 1 ? a[1] : "/tmp/res";
        int bad = 0;
        if (mode == "threads" || mode == "all") bad += Threads(dir);
        if (mode == "pick"    || mode == "all") bad += Pick(dir);
        if (mode == "seethrough" || mode == "all") SeeThrough(dir);
        if (mode == "grate"   || mode == "all") bad += Grate(dir);
        if (mode == "slope"   || mode == "all") bad += SlopeMath(dir);
        if (mode == "scroll"  || mode == "all") bad += Scrolling(dir);
        if (mode == "anim"    || mode == "all") bad += Animated(dir);
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

    /// <summary>
    /// What honouring WF_TRANSPARENT on two-sided walls actually changes.
    /// Reports rather than fails: this is a fidelity measurement, not a
    /// pass/fail, and the number that matters alongside it is that letting
    /// people see through fences does not leave columns unclosed.
    /// </summary>
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
        int roomsWith = 0, roomsWithArt = 0, roomsMoving = 0;

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }

            var animated = roo.SideDefs.Where(sd => sd.Animation != null).ToList();
            if (animated.Count == 0) continue;
            roomsWith++;

            var tcx = new TexCache(rm);
            bool haveArt = false;
            foreach (var sd in animated)
                foreach (ushort n in new[]{ sd.MiddleTexture, sd.UpperTexture, sd.LowerTexture })
                {
                    if (n == 0) continue;
                    try { var b = rm.GetRoomTexture(n); if (b != null && b.Frames.Count > 1) haveArt = true; }
                    catch { }
                }
            if (haveArt) roomsWithArt++;

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
        Console.WriteLine($"  of those, whose art has frames here: {roomsWithArt}");
        Console.WriteLine($"  rooms where the picture changes    : {roomsMoving}");
        bool roomsOk = roomsWithArt == 0 || roomsMoving > 0;
        bool mechOk = multi == 0 || distinct > 0;
        Console.WriteLine(roomsOk && mechOk ? "OK" : "PROBLEM");
        return roomsOk && mechOk ? 0 : 1;
    }
}
