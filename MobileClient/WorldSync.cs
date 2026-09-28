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

        bool clear;
        try { clear = Room.CanMoveInRoom(ref from, ref to, 0f, 0f, out _); }
        catch { clear = Renderer != null && Renderer.SectorAtPoint(to.X, to.Y) != null; }
        if (!clear) return false;

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
