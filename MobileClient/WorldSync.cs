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

    /// <summary>
    /// Override for sprite height in world units. Zero, the default, means
    /// each object is sized from its own art - see Renderer.Sprite.Height.
    /// </summary>
    public float SpriteHeight = 0f;

    /// <summary>
    /// Scrape along a wall instead of stopping dead against it. Off is the
    /// old behaviour, kept so the difference can be measured.
    /// </summary>
    public bool Sliding = true;

    /// <summary>
    /// Draw objects as the game composes them, with their suboverlays and
    /// per-part colours, instead of the main frame alone. Off falls back
    /// to the single frame, which is what this client did before and what
    /// the reference renders were taken with.
    /// </summary>
    public bool Composed = true;

    readonly ComposeCache _compose = new ComposeCache();
    /// <summary>Composed pictures held, for a test to look at.</summary>
    public int ComposedCount => _compose.Count;

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
    /// <summary>
    /// Seconds since the view started, for the one material that moves.
    /// Set by the caller each frame; zero is a still picture.
    /// </summary>
    public double Seconds;

    public void SyncSprites(IEnumerable<RoomObject> objects, RoomObject avatar)
    {
        if (Renderer == null) return;
        Renderer.Sprites.Clear();
        if (objects == null) return;

        V2 eye = avatar != null ? avatar.Position2D : new V2(0f, 0f);

        foreach (RoomObject o in objects)
        {
            if (o == null || o.Resource == null) continue;
            if (avatar != null && ReferenceEquals(o, avatar)) continue;

            var sp = new Renderer.Sprite
            {
                X = M59Geo.KodToWorld(o.Position3D.X),
                Y = M59Geo.KodToWorld(o.Position3D.Z),   // world Y is Position3D.Z
                BaseZ = M59Geo.KodHeightToXY(o.Position3D.Y),
                Height = SpriteHeight,
                // Hanging objects are pinned by their top, not their base.
                // The flag overlaps some player types in the original
                // server, which is why the Ogre client excludes players
                // from it in its vanilla build - so do we, rather than
                // leave a player dangling.
                Hanging = o.Flags != null && o.Flags.IsHanging && !o.Flags.IsPlayer,
                Tag = o,
            };

            // The whole object - body, clothes, weapon, shield - rather
            // than the body's frame alone. Falls back to the plain frame
            // if the compose comes back empty, because a Knight with no
            // sword still beats no Knight.
            // IsTarget is the library's own flag, set when TargetID is
            // set, and the game draws a red edge round whatever carries
            // it - RemoteNode2D picks a different material for it.
            ComposeCache.Entry c = Composed ? _compose.Get(o, eye, o.IsTarget) : null;
            if (c != null)
            {
                sp.Texture = c.Tex;
                sp.Width = c.WorldW;
                if (SpriteHeight <= 0f) sp.Height = c.WorldH;
            }
            else
            {
                // The BGF rather than one frame: the renderer picks the
                // frame per view from the facing, so creatures turn as you
                // walk around them.
                sp.Bgf = o.Resource;
                sp.AngleUnits = o.AngleUnits;
                sp.Group = o.Animation != null && o.Animation.CurrentGroup > 0 ? o.Animation.CurrentGroup : 1;
            }

            Material(o, ref sp);

            Renderer.Sprites.Add(sp);
        }
    }

    /// <summary>
    /// Which material the object is drawn with, as RemoteNode2D picks it.
    ///
    /// That file is one if-chain, and the order is the whole of it: the
    /// first match wins, so a target that is also flashing is drawn as a
    /// target, and a shadowform that is also the target stays black. The
    /// chain is copied here in its own order rather than rearranged into
    /// something tidier.
    ///
    /// The numbers are `general.material`'s, where every one of these
    /// materials is the same pixel shader handed a different
    /// `colormodifier`. Two of the twelve do not survive the trip: the
    /// game's invisible material is a refraction shader that samples the
    /// scene behind the object and warps it with a scrolling noise
    /// texture, which a span renderer has nothing to sample from, so it
    /// is drawn as a faint ghost and said so here rather than pretended
    /// otherwise; and mouseover (3,5,3, a green-biased brightening)
    /// needs a pointer to hover, which a finger is not.
    /// </summary>
    void Material(RoomObject o, ref Renderer.Sprite sp)
    {
        ObjectFlags f = o.Flags;
        if (f == null) return;

        // INVISIBLE
        if (f.Drawing == ObjectFlags.DrawingType.Invisible)
            sp.Opacity = 0.15f;

        // BLACK (shadowform)
        else if (f.Drawing == ObjectFlags.DrawingType.Black)
            sp.TintR = sp.TintG = sp.TintB = 0f;

        // TARGET
        else if (o.IsTarget)
        { sp.TintR = 5f; sp.TintG = 3f; sp.TintB = 3f; }

        // MOUSEOVER would go here.

        // FLASHING - the only material that moves. Its sintime is bound
        // to sintime_0_2pi with a factor of 2, so it runs a full cycle
        // every two seconds, and the shader turns that into a brightness
        // between 0.4 and 1.0: `texcol.rgb *= (0.4 + 0.6 * abs(sintime))`.
        // Every other material passes a constant 1.0 there, which is why
        // that line does nothing anywhere else.
        else if (f.IsFlashing)
        {
            float pulse = 0.4f + 0.6f * MathF.Abs(MathF.Sin((float)(Seconds * Math.PI)));
            sp.TintR = sp.TintG = sp.TintB = pulse;
        }

        // TRANSLUCENT. The comment in ImageComposerOgre is not an aside:
        // these are opacities despite the names, so translucent25 is the
        // faintest of the three rather than the strongest.
        else if (f.Drawing == ObjectFlags.DrawingType.Translucent75) sp.Opacity = 0.75f;
        else if (f.Drawing == ObjectFlags.DrawingType.Translucent50) sp.Opacity = 0.50f;
        else if (f.Drawing == ObjectFlags.DrawingType.Translucent25) sp.Opacity = 0.25f;

        // DITHERINVIS (the logoff ghost) and DITHERTRANS both take the
        // 50% material in the game rather than a dither of their own.
        else if (f.Drawing == ObjectFlags.DrawingType.DitherInvis
              || f.Drawing == ObjectFlags.DrawingType.DitherTrans) sp.Opacity = 0.50f;
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
    /// <param name="height">
    /// The mover's current elevation in room units - NOT a body height.
    /// The library passes Start.Y here in its own VerifyMove, and the
    /// value seeds the step-up and fall checks: zero means the collision
    /// thinks you are standing at world height zero, which in a room whose
    /// floor is at three thousand is a long way underground.
    /// </param>
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

    // A TryStep lived here that moved a RoomObject a frame's worth. The
    // live view no longer wants it - BaseClient.TryMove does that job
    // properly, with resting, paralysis, vigor, buffs, water depth and
    // object collision - and the offline view works in world units with a
    // camera rather than a RoomObject. Nothing was left using it except
    // its own test, so it went.
}
