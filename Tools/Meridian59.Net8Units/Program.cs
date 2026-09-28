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
        Console.WriteLine(heightMismatch==0 && convBad==0 ? "OK" : "PROBLEM");
    }
}
