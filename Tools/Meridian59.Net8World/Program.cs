using System;using System.Collections.Generic;using System.IO;using System.Linq;
using Meridian59.Common;using Meridian59.Common.Enums;
using Meridian59.Data.Models;using Meridian59.Files;using Meridian59.Files.ROO;

// Drives WorldSync - the part of the live client between the server's data
// model and the renderer - with hand-built RoomObjects. No socket, no
// protocol, no engine. What is under test is the code this repo wrote, not
// the library's message handling.
//
//   dotnet run --project . -- <resourceDir>
static class World
{
    static int fail = 0;
    static void Check(bool ok, string what)
    {
        if (!ok) { fail++; Console.WriteLine("  FAIL " + what); }
    }

    static int Main(string[] a)
    {
        string dir = a.Length > 0 ? a[0] : "/tmp/res";
        var rm = new ResourceManager(); rm.Init(dir,dir,dir,dir,dir,dir,dir);

        var rooms = new[]{ "barinn.roo", "kc4.roo", "dvalley1.roo" }
            .Where(r => File.Exists(Path.Combine(dir, r)))
            .Select(r => { var f = new RooFile(Path.Combine(dir, r)); f.ResolveResources(rm); return f; })
            .ToArray();
        if (rooms.Length < 2) { Console.WriteLine("need at least two rooms"); return 2; }

        var w = new WorldSync(rm);

        // --- room changes -------------------------------------------------
        Check(w.SyncRoom(null) == false, "null room is not a change");
        Check(w.SyncRoom(rooms[0]) && w.RoomChanges == 1, "first room builds a renderer");
        Check(w.SyncRoom(rooms[0]) == false && w.RoomChanges == 1, "same room does not rebuild");
        Check(w.SyncRoom(rooms[1]) && w.RoomChanges == 2, "new room rebuilds");
        Check(w.Renderer != null && ReferenceEquals(w.Room, rooms[1]), "renderer follows the room");

        // --- sprites ------------------------------------------------------
        var roo = rooms[0];
        w.SyncRoom(roo);
        var bgf = rm.GetObject("duskrat.bgf");
        Check(bgf != null, "duskrat.bgf loads");

        var avatar = Obj(1, bgf, 700f, 12f, 800f);
        var near   = Obj(2, bgf, 710f, 12f, 800f);
        var artless= Obj(3, null, 720f, 12f, 800f);
        var list = new List<RoomObject>{ avatar, near, artless };

        w.SyncSprites(list, avatar);
        Check(w.Renderer.Sprites.Count == 1, $"avatar and artless object skipped (got {w.Renderer.Sprites.Count})");
        var sp = w.Renderer.Sprites[0];
        Check(ReferenceEquals(sp.Tag, near), "sprite carries the object it stands for");
        Check(Math.Abs(sp.X - (710f - 64f) * 16f) < 0.01f, "kod X converted to room X");
        Check(Math.Abs(sp.Y - (800f - 64f) * 16f) < 0.01f, "kod Y comes from Position3D.Z");
        Check(Math.Abs(sp.BaseZ - 12f * 16f) < 0.01f, "height scaled, not shifted");
        Check(sp.AngleUnits == near.AngleUnits, "facing passed through");

        w.SyncSprites(null, avatar);
        Check(w.Renderer.Sprites.Count == 0, "a null object list empties the sprites");

        // --- stepping -----------------------------------------------------
        // Stand in the middle of the biggest room and walk every direction.
        var leaf = roo.BSPTreeLeaves.Where(l=>l.Vertices!=null&&l.Vertices.Count>=3)
            .OrderByDescending(l=>{double s=0;var v=l.Vertices;
                for(int i=0,j=v.Count-1;i<v.Count;j=i++) s+=(double)v[j].X*v[i].Y-(double)v[i].X*v[j].Y;
                return Math.Abs(s*.5);}).First();
        float wx = leaf.Vertices.Average(v=>(float)v.X), wy = leaf.Vertices.Average(v=>(float)v.Y);
        RooSubSector lf;
        float kodH = (float)roo.GetHeightAt(wx, wy, out lf, true, true) * 0.0625f;
        var me = new RoomObject();
        me.Position3D = new V3(M59Geo.WorldToKod(wx), kodH, M59Geo.WorldToKod(wy));

        Check(!w.TryStep(me, 0f, 0f, 25f, 0.016, out _), "standing still is not a step");

        int moved = 0, blocked = 0, sank = 0, teleported = 0;
        float expect = WorldSync.StepKod(25f, 0.016);
        for (int i = 0; i < 360; i++)
        {
            me.Angle = i * MathF.PI / 180f;
            V3 before = me.Position3D;
            if (w.TryStep(me, 1f, 0f, 25f, 0.016, out V3 to))
            {
                moved++;
                float d = MathF.Sqrt((to.X-before.X)*(to.X-before.X) + (to.Z-before.Z)*(to.Z-before.Z));
                if (MathF.Abs(d - expect) > 0.01f) teleported++;
                // Standing on the floor, not inside or above it.
                float roomH = (float)roo.GetHeightAt(M59Geo.KodToWorld(to.X), M59Geo.KodToWorld(to.Z), out lf, true, true);
                if (MathF.Abs(M59Geo.KodHeightToXY(to.Y) - roomH) > 0.01f) sank++;
            }
            else blocked++;
        }
        Check(moved > 0, "some directions are walkable");
        Check(teleported == 0, $"every step is exactly one step ({teleported} were not)");
        Check(sank == 0, $"every step lands on the floor ({sank} did not)");
        Console.WriteLine($"  360 headings: {moved} walkable, {blocked} blocked by the room");

        // One step from the middle of a big room hits nothing, so walk each
        // heading until the room says no. Ending up outside the room would
        // mean the collision check and the step disagree about units.
        int escaped = 0, stuckAt = 0;
        for (int i = 0; i < 360; i += 3)
        {
            var walker = new RoomObject();
            walker.Position3D = new V3(M59Geo.WorldToKod(wx), kodH, M59Geo.WorldToKod(wy));
            walker.Angle = i * MathF.PI / 180f;
            int steps = 0;
            while (steps < 4000 && w.TryStep(walker, 1f, 0f, 55f, 0.016, out V3 to))
            {
                walker.Position3D = to;
                steps++;
                if (w.Renderer.SectorAtPoint(M59Geo.KodToWorld(to.X), M59Geo.KodToWorld(to.Z)) == null)
                { escaped++; break; }
            }
            if (steps < 4000) stuckAt++;
        }
        Check(escaped == 0, $"walking never leaves the room ({escaped} headings escaped)");

        // Sliding: hold forward for a fixed number of frames and measure
        // how far you actually travel. Walking into a wall at an angle
        // should keep you moving along it; head-on it should not.
        int better = 0, worse = 0, outside = 0;
        for (int i = 0; i < 360; i += 3)
        {
            float[] travelled = new float[2];
            for (int mode = 0; mode < 2; mode++)
            {
                w.Sliding = mode == 1;
                var walker = new RoomObject();
                walker.Position3D = new V3(M59Geo.WorldToKod(wx), kodH, M59Geo.WorldToKod(wy));
                walker.Angle = i * MathF.PI / 180f;
                float dist = 0;
                for (int f = 0; f < 600; f++)
                {
                    if (!w.TryStep(walker, 1f, 0f, 55f, 0.016, out V3 t)) continue;
                    var p0 = walker.Position3D;
                    dist += MathF.Sqrt((t.X-p0.X)*(t.X-p0.X) + (t.Z-p0.Z)*(t.Z-p0.Z));
                    walker.Position3D = t;
                    if (w.Renderer.SectorAtPoint(M59Geo.KodToWorld(t.X), M59Geo.KodToWorld(t.Z)) == null)
                    { outside++; break; }
                }
                travelled[mode] = dist;
            }
            if (travelled[1] > travelled[0] + 0.5f) better++;
            else if (travelled[1] < travelled[0] - 0.5f) worse++;
        }
        w.Sliding = true;
        Check(outside == 0, $"sliding never leaves the room ({outside} did)");
        Check(worse == 0, $"sliding never costs distance ({worse} headings went backwards)");
        Check(better > 0, "sliding gets you further along at least some walls");
        Console.WriteLine($"  sliding: further on {better} of 120 headings, never shorter");
        Console.WriteLine($"  walked 120 headings to a wall: {stuckAt} stopped, {120 - stuckAt} ran the full distance");

        // A blocked step must not move the avatar at all.
        var trapped = new RoomObject();
        trapped.Position3D = new V3(-9999f, 0f, -9999f);     // far outside
        V3 was = trapped.Position3D;
        w.TryStep(trapped, 1f, 0f, 25f, 0.016, out _);
        Check(trapped.Position3D.Equals(was), "a blocked step leaves the avatar alone");

        // Running covers more ground than walking, in the same ratio the
        // server's own speed constants give.
        float walk = WorldSync.StepKod((float)MovementSpeed.Walk, 1.0);
        float run  = WorldSync.StepKod((float)MovementSpeed.Run, 1.0);
        Check(MathF.Abs(run / walk - 55f / 25f) < 0.001f, "run:walk matches the server's constants");

        // What passing the real elevation to the room's collision changes.
        // The library's own VerifyMove passes Start.Y there; this used to
        // pass zero, which tells the step-up and fall checks you are
        // standing at world height zero.
        int same = 0, nowAllowed = 0, nowBlocked = 0, tried = 0;
        foreach (string path in Directory.GetFiles(dir, "*.roo").OrderBy(x=>x).Take(120))
        {
            RooFile room;
            try { room = new RooFile(path); room.ResolveResources(rm); } catch { continue; }
            var rr = new Renderer(room, new TexCache(rm));
            foreach (var lf2 in room.BSPTreeLeaves
                     .Where(l=>l.Vertices!=null&&l.Vertices.Count>=3&&l.Sector!=null).Take(10))
            {
                float sx2 = lf2.Vertices.Average(v=>(float)v.X), sy2 = lf2.Vertices.Average(v=>(float)v.Y);
                var sec2 = rr.SectorAtPoint(sx2, sy2);
                if (sec2 == null) continue;
                float h2 = M59Geo.FloorXY(sec2);
                for (int k = 0; k < 16; k++)
                {
                    float ang2 = k * MathF.PI / 8f;
                    var f2 = new V2(sx2, sy2);
                    var t2 = new V2(sx2 + MathF.Cos(ang2) * 900f, sy2 + MathF.Sin(ang2) * 900f);
                    bool atZero = WorldSync.TryMove(room, f2, t2, false, 0f, out _);
                    bool atReal = WorldSync.TryMove(room, f2, t2, false, h2, out _);
                    tried++;
                    if (atZero == atReal) same++;
                    else if (atReal) nowAllowed++;
                    else nowBlocked++;
                }
            }
        }
        Console.WriteLine($"  collision elevation: {tried} moves, {same} unchanged, " +
                          $"{nowAllowed} now allowed, {nowBlocked} now blocked");

        Console.WriteLine(fail == 0 ? "OK" : $"{fail} FAILURES");
        return fail == 0 ? 0 : 1;
    }

    static RoomObject Obj(uint id, Meridian59.Files.BGF.BgfFile bgf, float kx, float kh, float ky)
    {
        var o = new RoomObject();
        o.ID = id;
        o.Position3D = new V3(kx, kh, ky);
        o.AngleUnits = (ushort)(id * 311 % 4096);
        if (bgf != null) o.Resource = bgf;
        return o;
    }
}
