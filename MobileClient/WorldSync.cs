using System;
using System.Collections.Generic;
using Meridian59.Common;
using Meridian59.Common.Constants;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Files;
using Meridian59.Files.ROO;

/// <summary>
/// Everything the live view does between the server's data model and the
/// renderer: rebuilding on a room change, mirroring the object list, and
/// stepping the avatar.
///
/// It is deliberately free of Godot types. This is the part of the client
/// most likely to be wrong - it is where the server's units meet the
/// renderer's, and where a wrong sign is a wall you walk through - and
/// keeping it here means Tools/Meridian59.Net8World can drive it with a
/// hand-built DataController and no socket, no protocol and no engine.
/// GameView is then only input, pixels and widgets.
/// </summary>
public sealed class WorldSync
{
    readonly ResourceManager _rm;

    public Renderer Renderer { get; private set; }
    public RooFile Room { get; private set; }
    /// <summary>Counts rebuilds, so a test can tell one from a no-op.</summary>
    public int RoomChanges { get; private set; }

    /// <summary>Default sprite height when the object does not say.</summary>
    public float SpriteHeight = 640f;

    /// <summary>
    /// Scrape along a wall instead of stopping dead against it. Off is the
    /// old behaviour, kept so the difference can be measured.
    /// </summary>
    public bool Sliding = true;

    public WorldSync(ResourceManager rm) { _rm = rm; }

    /// <summary>
    /// Rebuilds the renderer when the server puts us in a different room.
    /// Returns true if it rebuilt. Compares by reference: the library hands
    /// back the same RooFile instance for the same room, so this is a
    /// pointer check per frame rather than a string compare.
    /// </summary>
    public bool SyncRoom(RooFile current)
    {
        if (current == null || ReferenceEquals(current, Room)) return false;
        Room = current;
        Renderer = new Renderer(current, new TexCache(_rm));
        RoomChanges++;
        return true;
    }

    /// <summary>
    /// Mirrors the server's objects into the renderer, converting the
    /// server's coordinates to the room's. Skips the avatar - you do not
    /// see yourself - and anything with no art.
    /// </summary>
    public void SyncSprites(IEnumerable<RoomObject> objects, RoomObject avatar)
    {
        if (Renderer == null) return;
        Renderer.Sprites.Clear();
        if (objects == null) return;

        foreach (RoomObject o in objects)
        {
            if (o == null || o.Resource == null) continue;
            if (avatar != null && ReferenceEquals(o, avatar)) continue;

            Renderer.Sprites.Add(new Renderer.Sprite
            {
                X = M59Geo.KodToWorld(o.Position3D.X),
                Y = M59Geo.KodToWorld(o.Position3D.Z),   // world Y is Position3D.Z
                BaseZ = M59Geo.KodHeightToXY(o.Position3D.Y),
                Height = SpriteHeight,
                // The BGF rather than one frame: the renderer picks the
                // frame per view from the facing, so creatures turn as you
                // walk around them.
                Bgf = o.Resource,
                AngleUnits = o.AngleUnits,
                Group = o.Animation != null && o.Animation.CurrentGroup > 0 ? o.Animation.CurrentGroup : 1,
                Tag = o,
            });
        }
    }

    /// <summary>Camera position in room units, eye height included.</summary>
    public static void Camera(RoomObject avatar, out float x, out float y, out float z)
    {
        x = M59Geo.KodToWorld(avatar.Position3D.X);
        y = M59Geo.KodToWorld(avatar.Position3D.Z);
        z = M59Geo.KodHeightToXY(avatar.Position3D.Y) + Renderer.EyeHeight;
    }

    /// <summary>
    /// Moves from one point to another in room units, sliding along the
    /// blocking wall if the direct line is refused and
    /// <paramref name="sliding"/> is set. Returns false, writing nothing
    /// worth using, when neither works.
    ///
    /// Only one retry: a corner blocks both the step and its slide, and
    /// chasing that with more retries buys a jitter, not a corner.
    ///
    /// Static and taking the room outright so the offline view can use the
    /// same movement as the live one instead of its own copy.
    /// </summary>
    public static bool TryMove(RooFile room, V2 from, V2 to, bool sliding,
                               float height, out V2 landed)
    {
        landed = to;
        if (room == null) return false;

        RooWall blocker = null;
        try
        {
            if (room.CanMoveInRoom(ref from, ref to, height, 0f, out blocker)) return true;
        }
        catch { return false; }

        // Walking into a wall at a slight angle and coming to a halt is the
        // difference between a room that feels solid and one that feels
        // sticky, and in corridors it is most of the walking you do.
        if (!sliding) return false;
        return Slide(room, blocker, from, height, ref landed);
    }

    static bool Slide(RooFile room, RooWall wall, V2 from, float height, ref V2 to)
    {
        if (wall == null) return false;

        float ex = wall.X2 - wall.X1, ey = wall.Y2 - wall.Y1;
        float len2 = ex * ex + ey * ey;
        if (len2 < 1e-6f) return false;

        float dx = to.X - from.X, dy = to.Y - from.Y;
        float t = (dx * ex + dy * ey) / len2;          // projection onto the wall
        var slid = new V2(from.X + ex * t, from.Y + ey * t);
        if (MathF.Abs(slid.X - from.X) < 0.01f && MathF.Abs(slid.Y - from.Y) < 0.01f)
            return false;                              // head-on: nothing to slide along

        try
        {
            if (!room.CanMoveInRoom(ref from, ref slid, height, 0f, out _)) return false;
        }
        catch { return false; }

        to = slid;
        return true;
    }

    /// <summary>
    /// Room units a body at <paramref name="kodSpeed"/> covers in
    /// <paramref name="seconds"/>. MOVEBASECOEFF is per millisecond.
    /// </summary>
    public static float StepKod(float kodSpeed, double seconds)
        => kodSpeed * GeometryConstants.MOVEBASECOEFF * 1000f * (float)seconds;

    /// <summary>
    /// Works out where a step would put the avatar, or reports that the
    /// room blocks it. Nothing is written unless it is clear, so a blocked
    /// step leaves the avatar exactly where it was.
    ///
    /// <paramref name="forward"/> and <paramref name="strafe"/> are -1..1.
    /// The heading is the avatar's own Angle, which is measured the way
    /// MathUtil.GetRadianForDirection measures it: counterclockwise from
    /// +X, so the direction is (cos, sin).
    /// </summary>
    public bool TryStep(RoomObject avatar, float forward, float strafe,
                        float kodSpeed, double seconds, out V3 destination)
    {
        destination = avatar != null ? avatar.Position3D : new V3(0, 0, 0);
        if (avatar == null || Room == null) return false;
        if (forward == 0f && strafe == 0f) return false;

        float step = StepKod(kodSpeed, seconds);
        float c = MathF.Cos(avatar.Angle), s = MathF.Sin(avatar.Angle);
        V3 p = avatar.Position3D;
        float nkx = p.X + (c * forward - s * strafe) * step;
        float nky = p.Z + (s * forward + c * strafe) * step;

        var from = new V2(M59Geo.KodToWorld(p.X), M59Geo.KodToWorld(p.Z));
        var to = new V2(M59Geo.KodToWorld(nkx), M59Geo.KodToWorld(nky));

        if (!TryMove(Room, from, to, Sliding, 0f, out V2 landed)) return false;
        to = landed;
        nkx = M59Geo.WorldToKod(to.X);
        nky = M59Geo.WorldToKod(to.Y);

        // Follow the floor, the way the library does for moving objects:
        // GetHeightAt returns room units and Position3D.Y is kod.
        float h = p.Y;
        try
        {
            RooSubSector leaf;
            h = (float)Room.GetHeightAt(to.X, to.Y, out leaf, true, true) * 0.0625f;
        }
        catch { }

        destination = new V3(nkx, h, nky);
        return true;
    }
}
