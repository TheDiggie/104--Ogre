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
        var r2 = w.Renderer;

        // Movement, in the shape the offline view uses it: world units, a
        // camera, and WorldSync.TryMove. The live view hands this job to
        // BaseClient.TryMove instead, which does rather more.
        float stepLen = WorldSync.StepKod(55f, 0.016) * M59Geo.KodToRoom;

        int moved = 0, blocked = 0, overshot = 0;
        for (int i = 0; i < 360; i++)
        {
            float ang = i * MathF.PI / 180f;
            var from = new V2(wx, wy);
            var to = new V2(wx + MathF.Cos(ang) * stepLen, wy + MathF.Sin(ang) * stepLen);
            if (WorldSync.TryMove(roo, from, to, false, kodH * M59Geo.KodToRoom, out V2 landed))
            {
                moved++;
                float d = MathF.Sqrt((landed.X - wx) * (landed.X - wx) + (landed.Y - wy) * (landed.Y - wy));
                if (d > stepLen + 0.01f) overshot++;
            }
            else blocked++;
        }
        Check(moved > 0, "some directions are walkable");
        Check(overshot == 0, $"no step goes further than asked ({overshot} did)");
        Console.WriteLine($"  360 headings: {moved} walkable, {blocked} blocked by the room");

        // Walking each heading until the room refuses. Ending up outside
        // the room would mean the step and the collision disagree about
        // units.
        int escaped = 0, stuckAt = 0;
        for (int i = 0; i < 360; i += 3)
        {
            float ang = i * MathF.PI / 180f;
            float px = wx, py = wy;
            int steps = 0;
            while (steps < 4000)
            {
                var from = new V2(px, py);
                var to = new V2(px + MathF.Cos(ang) * stepLen, py + MathF.Sin(ang) * stepLen);
                var sec2 = r2.SectorAtPoint(px, py);
                float hNow = sec2 != null ? M59Geo.FloorXY(sec2, px, py) : 0f;
                if (!WorldSync.TryMove(roo, from, to, false, hNow, out V2 landed)) break;
                px = landed.X; py = landed.Y;
                steps++;
                if (r2.SectorAtPoint(px, py) == null) { escaped++; break; }
            }
            if (steps < 4000) stuckAt++;
        }
        Check(escaped == 0, $"walking never leaves the room ({escaped} headings escaped)");
        Console.WriteLine($"  walked 120 headings to a wall: {stuckAt} stopped, {120 - stuckAt} ran the full distance");

        // Sliding: hold a heading for a fixed number of frames and measure
        // how far you travel with it on and off.
        int better = 0, worse = 0, outside = 0;
        for (int i = 0; i < 360; i += 3)
        {
            float ang = i * MathF.PI / 180f;
            float[] travelled = new float[2];
            for (int mode = 0; mode < 2; mode++)
            {
                float px = wx, py = wy, dist = 0;
                for (int f = 0; f < 600; f++)
                {
                    var from = new V2(px, py);
                    var to = new V2(px + MathF.Cos(ang) * stepLen, py + MathF.Sin(ang) * stepLen);
                    var sec2 = r2.SectorAtPoint(px, py);
                    float hNow = sec2 != null ? M59Geo.FloorXY(sec2, px, py) : 0f;
                    if (!WorldSync.TryMove(roo, from, to, mode == 1, hNow, out V2 landed)) continue;
                    dist += MathF.Sqrt((landed.X - px) * (landed.X - px) + (landed.Y - py) * (landed.Y - py));
                    px = landed.X; py = landed.Y;
                    if (r2.SectorAtPoint(px, py) == null) { outside++; break; }
                }
                travelled[mode] = dist;
            }
            if (travelled[1] > travelled[0] + 0.5f) better++;
            else if (travelled[1] < travelled[0] - 0.5f) worse++;
        }
        Check(outside == 0, $"sliding never leaves the room ({outside} did)");
        Check(worse == 0, $"sliding never costs distance ({worse} headings went backwards)");
        Check(better > 0, "sliding gets you further along at least some walls");
        Console.WriteLine($"  sliding: further on {better} of 120 headings, never shorter");

        // Run over walk matches the server's own speed constants.
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

        // The live client's own resource wiring, with no socket involved.
        // BaseClient.Init assumes subfolders (strings, rooms, bgfobjects,
        // ...) while an installed client is flat, and it creates the
        // missing folders as it goes - so getting this wrong shows up as
        // a world with no textures rather than as an error.
        var probe = new M59Client();
        probe.Config.ResourcesPath = dir;
        probe.Init();
        Check(probe.ResourceManager.GetRoom("barinn.roo") != null,
              "the client's own resource manager finds a room");
        Check(probe.ResourceManager.GetRoomTexture(2011) != null,
              "the client's own resource manager finds a room texture");

        // The string file was hardcoded to the usual name. Finding the one
        // that is actually there beats guessing, and guessing wrong makes
        // every room and object name from the server resolve to nothing.
        string strings = M59Client.FindStringDictionary(dir);
        // Files, not folders - an earlier run with the wrong layout can
        // have left an empty 'strings' directory behind.
        bool anyRsb = Directory.EnumerateFiles(dir, "*.rsb").Any()
                   || (Directory.Exists(Path.Combine(dir, "strings"))
                       && Directory.EnumerateFiles(Path.Combine(dir, "strings"), "*.rsb").Any());
        Check(strings.EndsWith(".rsb", StringComparison.OrdinalIgnoreCase),
              $"string file resolves to something plausible ({strings})");
        Console.WriteLine($"  (string file: {strings}{(anyRsb ? "" : ", none present here so this is the fallback")})");

        // The client's clock. RootClient.Tick advances GameTick and then
        // calls Update; calling Update on its own - which is what a Godot
        // _Process does - leaves Span at zero for ever. Span is what every
        // timed thing in the library multiplies by, including the step in
        // BaseClient.TryMove, so a client that never ticks cannot move.
        Check(probe.GameTick.Span == 0.0, "a fresh client has not ticked yet");
        probe.GameTick.Tick();
        System.Threading.Thread.Sleep(5);
        probe.GameTick.Tick();
        Check(probe.GameTick.Span > 0.0, "ticking advances the clock");
        Console.WriteLine($"  client clock: {probe.GameTick.Span:F1} ms after a tick, " +
                          "0 without one - and a span of 0 means TryMove's step is 0");

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
