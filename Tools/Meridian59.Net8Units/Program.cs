using System;using System.IO;using System.Linq;
using Meridian59.Files;using Meridian59.Files.ROO;using Meridian59.Data.Models;using Meridian59.Common;
using Real = System.Single;

// Two things the live view now depends on and nothing checked:
//
// 1. GetHeightAt returns heights on the same scale as RooSector.FloorHeight.
//    GameView sets the avatar's height from GetHeightAt * 0.0625 and the
//    renderer reads FloorXY = FloorHeight * 16. Written the first time
//    against raw FloorHeight, which failed on 11399 of 13367 samples by a
//    factor of exactly 16: GetHeightAt already returns room units. So
//    FloorXY is the thing it has to agree with, and the chain
//    GetHeightAt -> *0.0625 -> Position3D.Y -> *16 -> renderer closes.
//
// 2. M59Geo's kod <-> room conversion matches the library's own, which does
//    the same arithmetic in RoomObject.UpdatePosition and, separately, in
//    BaseClient.SendReqMoveMessage via CoordinateX (X * 16 - 1024).
static class U
{
    /// <summary>
    /// Which way round the world is.
    ///
    /// Nothing inside the renderer can answer this: mirror the whole view
    /// and every texture, sprite and collision test still agrees with
    /// itself. The only external reference is the top-down map tool, whose
    /// convention - room X to image columns, room Y to image rows, so +Y
    /// runs DOWN the map - was checked against the wiki's own dvalley1 map
    /// using an asymmetric notch to line up.
    ///
    /// So: standing at the origin facing +X is facing right on that map,
    /// and the player's right hand points to +Y. An object at +Y must
    /// therefore be drawn right of centre. This pins the renderer to the
    /// map, and the map to something a human has looked at.
    /// </summary>
    static int Handedness(string dir, ResourceManager rm)
    {
        var rooms = Directory.GetFiles(dir, "*.roo").OrderBy(x=>x).Take(25);
        int bad = 0, seen = 0, unseen = 0;
        foreach (string path in rooms)
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var leaf = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
                .OrderByDescending(l=>{double s=0;var v=l.Vertices;
                    for(int i=0,j=v.Count-1;i<v.Count;j=i++) s+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                    return Math.Abs(s*.5);}).FirstOrDefault();
            if (leaf == null) continue;
            float cx=leaf.Vertices.Average(v=>(float)v.X), cy=leaf.Vertices.Average(v=>(float)v.Y);
            var sec0 = M59Geo.Sector(roo, 0);
            var r = new Renderer(roo, new TexCache(rm));
            var s0 = r.SectorAtPoint(cx, cy);
            if (s0 == null) continue;
            float cz = M59Geo.FloorXY(s0) + Renderer.EyeHeight;

            const int W=320, H=180;
            var tex = new Tex { W=2, H=2, P = new uint[]{0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF} };

            // Facing +X, an object off to +Y belongs on the right.
            foreach (float side in new[]{ 600f, -600f })
            {
                r.Sprites.Clear();
                r.Sprites.Add(new Renderer.Sprite {
                    X = cx + 2500f, Y = cy + side,
                    BaseZ = cz - Renderer.EyeHeight, Height = 500f, Texture = tex });
                var px = new uint[W*H];
                r.Render(px, W,H, cx,cy,cz, 0f);

                // Where did it actually land? Scan rather than assume a
                // column, so a sprite that is simply not visible is told
                // apart from one on the wrong side.
                long sum = 0; int n = 0;
                for (int x=0; x<W; x++)
                for (int y=0; y<H; y+=4)
                    if (r.Pick(x,y,W,H, cx,cy,cz, 0f) != null) { sum += x; n++; break; }

                if (n == 0) { unseen++; continue; }
                float mean = (float)sum / n;
                seen++;
                bool onRight = mean > W * 0.5f;
                if (onRight != (side > 0)) bad++;
            }
        }
        Console.WriteLine($"  (handedness: {seen} sprites placed, {unseen} not visible)");
        return bad;
    }

    static void Main(string[] a)
    {
        string dir = a.Length > 0 ? a[0] : "/tmp/res";
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);

        int sampled=0, heightMismatch=0, roomsDone=0;
        double worst=0; string worstWhere="";

        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x))
        {
            RooFile roo;
            try { roo = new RooFile(path); roo.ResolveResources(rm); } catch { continue; }
            var leaves = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3).ToList();
            if (leaves.Count == 0) continue;
            roomsDone++;

            foreach (var leaf in leaves.Take(40))
            {
                float x = leaf.Vertices.Average(v=>(float)v.X);
                float y = leaf.Vertices.Average(v=>(float)v.Y);
                RooSubSector got;
                Real h = roo.GetHeightAt(x, y, out got, true, false);
                if (got?.Sector == null) continue;
                sampled++;
                double expect = M59Geo.FloorXY(got.Sector);
                double d = Math.Abs(h - expect);
                if (d > worst) { worst = d; worstWhere = $"{Path.GetFileName(path)} leaf@({x:F0},{y:F0}) got {h} sector {expect}"; }
                // Sloped sectors legitimately vary across a leaf; a flat one
                // must match exactly.
                if (got.Sector.SlopeInfoFloor == null && d > 0.001) heightMismatch++;
            }
        }
        Console.WriteLine($"{roomsDone} rooms, {sampled} height samples");
        Console.WriteLine($"  GetHeightAt vs FloorXY mismatches : {heightMismatch}");
        Console.WriteLine($"  largest difference seen       : {worst}  ({worstWhere})");

        // Kod <-> room, against the library's second implementation.
        int convBad = 0;
        var obj = new RoomObject();
        var rnd = new Random(7);
        for (int i=0;i<20000;i++)
        {
            float kx = (float)(rnd.NextDouble()*60000);
            float ky = (float)(rnd.NextDouble()*60000);
            obj.Position3D = new V3(kx, 0f, ky);
            // BaseClient.SendReqMoveMessage does exactly this before asking
            // the ROO for a sector.
            float libX = obj.CoordinateX * 16f - 1024f;
            float libY = obj.CoordinateY * 16f - 1024f;
            float mineX = M59Geo.KodToWorld(obj.CoordinateX);
            float mineY = M59Geo.KodToWorld(obj.CoordinateY);
            if (libX != mineX || libY != mineY) convBad++;
            // and the round trip
            if (Math.Abs(M59Geo.WorldToKod(M59Geo.KodToWorld(kx)) - kx) > 0.01f) convBad++;
            if (Math.Abs(M59Geo.XYHeightToKod(M59Geo.KodHeightToXY(kx)) - kx) > 0.01f) convBad++;
        }
        Console.WriteLine($"  kod/room conversion mismatches: {convBad} of 60000");

        int handBad = Handedness(dir, rm);
        Console.WriteLine($"  handedness disagreements      : {handBad}");
        Console.WriteLine(heightMismatch==0 && convBad==0 && handBad==0 ? "OK" : "PROBLEM");
    }
}
