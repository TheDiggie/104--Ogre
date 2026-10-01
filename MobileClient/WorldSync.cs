using System;
using System.Collections.Generic;
using Meridian59.Common;
using Meridian59.Common.Interfaces;
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

    /// <summary>
    /// The brazier's flame and the news globe's black hole - the two 3D
    /// models of the reference's fourteen that carry a particle system.
    /// See <see cref="ObjectParticles"/> for what they are, where they
    /// come from and how they differ.
    /// </summary>
    readonly ObjectParticles _particles = new ObjectParticles();
    /// <summary>Particle sprites drawn last frame, for a test to look at.</summary>
    public int ParticlesDrawn => _particles.Drawn;

    readonly ComposeCache _compose = new ComposeCache();
    /// <summary>Composed pictures held, for a test to look at.</summary>
    public int ComposedCount => _compose.Count;

    public WorldSync(ResourceManager rm) { _rm = rm; }

    /// <summary>
    /// Rebuilds the renderer when the server puts us in a different room.
    /// Returns true if the room changed. Compares by reference: the
    /// library hands back the same RooFile instance for the same room, so
    /// this is a pointer check per frame rather than a string compare.
    ///
    /// A null room - the server sent us somewhere whose .roo the client
    /// does not have, so `RoomInfo.ResourceRoom` never resolved - UNLOADS
    /// and returns true. It used to `return false` with the old RooFile
    /// still in `Room`, which left the player in a ghost of the room they
    /// had left: the caller's own null guard passed, the minimap was
    /// rebuilt from the old room's walls, and the status read the new
    /// room - while `BaseClient.CurrentRoom` IS
    /// `Data.RoomInformation.ResourceRoom` (RootClient.cs:98) and so was
    /// null, which fails `TryMove`'s guard (BaseClient.cs:2830) and froze
    /// the player where they stood with no message anywhere. The
    /// reference unloads unconditionally on every Player message and only
    /// then refuses, with a logged error
    /// (ControllerRoom.cpp:1604-1611, LoadRoom :396-410, UnloadRoom
    /// :497-535): an empty scene, and it says so.
    /// </summary>
    public bool SyncRoom(RooFile current)
    {
        if (ReferenceEquals(current, Room)) return false;
        Room = current;
        // Unload before anything else, and whether or not there is a new
        // room to put up - as UnloadRoom does. Object ids belong to a
        // room, so a flame must not carry over to whatever now has the
        // same id, and a renderer left standing over a room we are no
        // longer in is the ghost this used to leave behind.
        _particles.Reset();
        if (current == null)
        {
            Renderer = null;
            RoomChanges++;
            return true;
        }
        // The replacement room textures, when the player has them. Found
        // once: the folder does not move, and a miss must not be looked
        // for again on every room change. See TexCache.RoomTextureDir.
        if (!_texLooked) { _texDir = TexCache.FindRoomTextures(RootPath); _texLooked = true; }
        Renderer = new Renderer(current, new TexCache(_rm) { RoomTextureDir = _texDir })
                   { Sky = _sky };
        // The room's grass. Generated HERE and only here, because the
        // reference generates it once per room load too - LoadRoom calls
        // CreateDecoration at ControllerRoom.cpp:485 and times it
        // separately from the geometry - and because a room whose grass
        // moved every frame would shimmer. The definitions are looked for
        // once: the folder does not move, and a machine without the
        // decoration art must not pay for the miss on every room change.
        if (!_grassLooked)
        {
            _grassDefs = M59Grass.LoadDefs(M59Grass.FindDir(RootPath));
            _grassLooked = true;
        }
        Renderer.Grass = M59Grass.Build(current, _grassDefs, M59Grass.Intensity);
        RoomChanges++;
        return true;
    }

    /// <summary>The grass mappings and art, once they have been looked for.</summary>
    M59Grass.Defs _grassDefs;
    bool _grassLooked;

    /// <summary>Where the sky faces are, once one has been looked for.</summary>
    string _skyDir;
    bool _skyLooked;
    /// <summary>The set the current sky was built from, so it is loaded once.</summary>
    string _skySet;
    M59Sky _sky;

    /// <summary>
    /// The background the server asked for. It arrives with RoomInfo
    /// (RoomInfo.cs:806-807) and can be replaced at any time by
    /// BP_CHANGE_BACKGROUND (DataController.cs:2371-2375), so this is
    /// called with whatever the data model currently says rather than
    /// only on a room change - the reference calls UpdateSky from both
    /// places too (ControllerRoom.cpp:427-428 and :1628-1631).
    /// </summary>
    public void SetBackground(string bgfFile)
    {
        string set = M59Sky.SetFor(bgfFile);
        if (set == _skySet) return;
        _skySet = set;
        if (!_skyLooked) { _skyDir = SkyDir ?? M59Sky.FindDir(RootPath); _skyLooked = true; }
        _sky = set == null ? null : M59Sky.Load(_skyDir, set);
        if (Renderer != null) Renderer.Sky = _sky;
    }

    /// <summary>Where the game's resources were loaded from, for finding the sky.</summary>
    public string RootPath;

    string _texDir;
    bool _texLooked;

    /// <summary>
    /// The folder holding the skybox faces, when the caller knows it.
    /// The game does - they ship with it, not with the player's resource
    /// folder - and M59Paths.SkyDir has to unpack them on Android, which
    /// needs Godot; the check tools leave this null and let M59Sky look.
    /// </summary>
    public string SkyDir;

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

    /// <summary>
    /// One point light per object whose light is on, the way
    /// Util::UpdateFromILightOwner builds them (Util.h:229-278).
    ///
    /// The colour is the 5-5-5 packed LightColor (Util.h:203-211) and is
    /// NOT scaled by intensity - intensity only sets the radius, which
    /// is 120 + 460 * intensity/255 Ogre units, times 0.12 for a
    /// highlight light (Util.h:263-272). Ogre units are world units over
    /// sixteen (V3.ConvertToWorld, V3.cs:346-351), so the radius comes
    /// back to world units by multiplying by sixteen.
    ///
    /// The light sits at half the object's height, which is where the
    /// reference puts a 2D object's (RemoteNode2D.cpp:136-143); the
    /// avatar's own is included, unlike its sprite, because a torch in
    /// your own hand lights the room in front of you.
    /// </summary>
    void AddLight(ILightOwner o, V3 position)
    {
        LightingInfo li = o?.LightingInfo;
        if (li == null || !li.IsLightOn || li.LightIntensity == 0) return;

        float ratio = li.LightIntensity / 255f;
        float range = 120f + 460f * ratio;
        if (li.IsLightHighlight) range *= 0.12f;
        range *= 16f;                                   // Ogre units to world units
        if (range <= 0f) return;

        ushort c = li.LightColor;
        Renderer.Lights.Add(new Renderer.Light
        {
            X = M59Geo.KodToWorld(position.X),
            Y = M59Geo.KodToWorld(position.Z),
            Z = M59Geo.KodHeightToXY(position.Y) + 50f * 16f,
            R = ((c >> 10) & 31) / 31f,
            G = ((c >> 5) & 31) / 31f,
            B = (c & 31) / 31f,
            Range = range,
            R2 = range * range,
        });
    }

    public void SyncSprites(IEnumerable<RoomObject> objects, RoomObject avatar,
                            IEnumerable<Projectile> projectiles = null)
    {
        if (Renderer == null) return;
        Renderer.Sprites.Clear();
        Renderer.Lights.Clear();
        if (objects == null) return;

        V2 eye = avatar != null ? avatar.Position2D : new V2(0f, 0f);

        // The viewer, in world units, for the particle systems' shading
        // and draw order. The avatar's own position: this is a first
        // person view, so it is where the camera is to within eye height.
        _particles.Begin(Seconds,
            avatar != null ? M59Geo.KodToWorld(avatar.Position3D.X) : 0f,
            avatar != null ? M59Geo.KodToWorld(avatar.Position3D.Z) : 0f,
            Renderer);

        foreach (RoomObject o in objects)
        {
            if (o == null || o.Resource == null) continue;
            // Before the avatar is skipped: you do not see yourself, but
            // a torch in your own hand still lights the room in front of
            // you, and the reference attaches the light to the object
            // whether or not its sprite is drawn.
            AddLight(o, o.Position3D);
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

            // The object's particle system, if its model has one, after
            // its own sprite so that a flame is listed above the thing it
            // burns on. The group is the same one the reference gates on
            // (RemoteNode3D.cpp:322-345).
            _particles.Add(o.ID, o.OverlayFile,
                o.Animation != null ? o.Animation.CurrentGroup : 0,
                sp.X, sp.Y, sp.BaseZ, o, Renderer.Sprites);
        }
        _particles.End();

        AddProjectiles(projectiles, eye);
    }

    /// <summary>
    /// The arrows and fireballs in flight.
    ///
    /// The library owns them entirely: `HandleShoot` resolves the source
    /// and the target, refuses a projectile missing either, and
    /// `DataController.Tick` moves it every frame
    /// (`DataController.cs:1078`). All that was missing was drawing
    /// them, so combat happened with nothing visible between the bow
    /// and the body.
    ///
    /// The frame is the library's choice, not ours: `UpdateViewerAngle`
    /// works out which way the thing is presented from here, and the
    /// sprite cache is asked for exactly that frame. Letting the
    /// renderer pick from an angle, as it does for a creature, would
    /// subtract the viewer's angle a second time.
    /// </summary>
    void AddProjectiles(IEnumerable<Projectile> projectiles, V2 eye)
    {
        if (projectiles == null) return;

        foreach (Projectile p in projectiles)
        {
            if (p?.Resource == null) continue;

            // A spell effect carries its own light too
            // (ProjectileNode2D.cpp:48, :132-138).
            AddLight(p, p.Position3D);

            p.UpdateViewerAngle(eye);

            int group = p.Animation != null && p.Animation.CurrentGroup > 0
                ? p.Animation.CurrentGroup : 1;

            // The frame's own vertical offset. `ProjectileNode2D.cpp:122`
            // lifts the billboard by -YOffset/shrink on top of the
            // bottom-centre origin, which is what puts an arrow at the
            // height it was loosed from rather than at its feet. Ignoring
            // it had bolts skimming the floor.
            float lift = 0f;
            int fi = p.Resource.GetFrameIndex(group, p.ViewerAngle);
            if (fi >= 0 && fi < p.Resource.Frames.Count)
                lift = -p.Resource.Frames[fi].YOffset
                     / MathF.Max(1f, p.Resource.ShrinkFactor) * M59Geo.HeightToXY;

            var sp = new Renderer.Sprite
            {
                X = M59Geo.KodToWorld(p.Position3D.X),
                Y = M59Geo.KodToWorld(p.Position3D.Z),
                BaseZ = M59Geo.KodHeightToXY(p.Position3D.Y) + lift,
                // From the art, like any other sprite: an arrow is not
                // a person-sized thing and must not be drawn as one.
                Height = 0f,
                Texture = Renderer.SpriteFrames.Get(p.Resource, group, p.ViewerAngle),
                Tag = p,
            };

            if (sp.Texture != null) Renderer.Sprites.Add(sp);
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

        // TARGET is handled in the picture, not here - see below.
        //
        // WHAT THE REFERENCE DOES: `RemoteNode2D.cpp:173-215` picks
        // `base_material_target` for a target, which is `colormodifier 5
        // 3 3 1` (`Resources/shader/general.material:333-346`) - a
        // red-biased BRIGHTENING of the whole sprite. There is no edge:
        // `ImageComposerOgre<T>::DrawPostEffects` is empty
        // (`ImageComposerOgre.cpp:156-158`), and the red edge that does
        // exist, `ImageComposerGDI.DrawPostEffectTarget`
        // (`Drawing2D/ImageComposerGDI.cs:186`), is called by nothing
        // (`ImageComposerGDI.DrawPostEffects`, :142-149, handles only
        // DitherInvis and Black).
        //
        // WHAT THIS CLIENT DOES, AND WHY: the red outline is ASHTON'S
        // STANDING RULING (notes/rulings.md: "A target that is clicked to
        // attack is outlined in red"), not the reference's behaviour. It
        // stays. Applying the (5,3,3) modifier as well ran on top of the
        // baked edge and flooded the target pink with a red rim, so the
        // ruling wins and the modifier is dropped for a target.
        // `_compose.Get(o, eye, o.IsTarget)` asks for the edge;
        // M59Compose.Outline draws it, copying the shape of the GDI
        // routine that the reference itself never runs.

        // TARGET, in the reference's place in the chain rather than
        // left out of it. `RemoteNode2D.cpp:173-215` tests Invisible,
        // Black, IsTarget, IsHighlighted, IsFlashing, then the
        // translucencies, and the first match wins - so a target is
        // drawn as a target and is NOT also faint or pulsing. Leaving
        // it out of the chain meant a targeted translucent thing kept
        // its 25% opacity with a red edge round it, and a flashing one
        // went on pulsing; the reference shows both solid.
        //
        // Nothing is set here because the edge is already in the
        // picture: `_compose.Get(o, eye, o.IsTarget)`. The point of the
        // branch is to STOP.
        else if (o.IsTarget) { }

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

    /// <summary>
    /// How tall the avatar is drawn, in room units, or zero if there is
    /// nothing to measure yet. Kept between frames because it is the
    /// camera height and the camera must not bob when a frame of the
    /// walk animation happens to be shorter.
    /// </summary>
    float _avatarHeight;

    /// <summary>Camera position in room units, eye height included.</summary>
    public void Camera(RoomObject avatar, out float x, out float y, out float z)
    {
        x = M59Geo.KodToWorld(avatar.Position3D.X);
        y = M59Geo.KodToWorld(avatar.Position3D.Z);

        // The eye is 93% of the avatar's own drawn height, as the
        // reference has it (RemoteNode.cpp:406-425) - see Renderer.Eye,
        // which refuses a measurement that is not believable. The
        // reference only moves its camera when the answer shifts by
        // more than sixteen units (:423-425); the same threshold here
        // stops the view bobbing on an animation frame.
        float h = Measured(avatar);
        if (h > 0f && MathF.Abs(h - _avatarHeight) > 16f) _avatarHeight = h;

        z = M59Geo.KodHeightToXY(avatar.Position3D.Y) + Renderer.Eye(_avatarHeight);
    }

    /// <summary>The avatar's drawn height, composed body and all.</summary>
    float Measured(RoomObject avatar)
    {
        if (Renderer == null || avatar == null) return 0f;
        try
        {
            ComposeCache.Entry c = Composed ? _compose.Get(avatar, avatar.Position2D, false) : null;
            if (c != null && c.WorldH > 0f) return c.WorldH;
            var probe = new Renderer.Sprite { Bgf = avatar.Resource, Group = 1,
                                              AngleUnits = avatar.ViewerAngle };
            return Renderer.WorldHeight(probe);
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Moves from one point to another in room units, using the
    /// library's own VerifyMove - the same call BaseClient.TryMove makes
    /// for the live avatar (BaseClient.cs:2868 into RooFile.cs:1541).
    ///
    /// This used to be a hand-rolled version: one attempt, then one
    /// projection of the step onto the blocking wall, and a speed of
    /// zero. All three were wrong against the reference. VerifyMove
    /// slides with the wall's own SlideAlong and then, if that is
    /// refused too, rotates the step by eleven and a quarter degrees
    /// either way up to eight times (:1568-1588) - which is what gets
    /// you out of a corner instead of stuck in it. And the speed is not
    /// decoration: CanMoveInRoom divides by it for the fall-across-a-
    /// sector term (RooFile.cs:1916-1926), and zero trips the guard
    /// there that sets it to nine million, removing the term outright.
    ///
    /// Static and taking the room outright so the offline view uses the
    /// same movement as the live one rather than its own copy.
    /// </summary>
    /// <param name="height">
    /// The mover's current elevation in room units - NOT a body height.
    /// The library passes Start.Y here in its own VerifyMove, and the
    /// value seeds the step-up and fall checks: zero means the collision
    /// thinks you are standing at world height zero, which in a room
    /// whose floor is at three thousand is a long way underground.
    /// </param>
    /// <param name="kodSpeed">
    /// The mover's speed in the server's units, which is what the fall
    /// term wants. USER_WALKING_SPEED and USER_RUNNING_SPEED are 25 and
    /// 55 (MovementSpeed.cs:35).
    /// </param>
    public static bool TryMove(RooFile room, V2 from, V2 to, bool sliding,
                               float height, out V2 landed, float kodSpeed = 25f)
    {
        landed = to;
        if (room == null) return false;

        try
        {
            if (!sliding)
            {
                // Without sliding there is nothing to ask VerifyMove
                // for: it always slides. The direct question is
                // CanMoveInRoom, which is what it asks first.
                return room.CanMoveInRoom(ref from, ref to, height, kodSpeed, out _);
            }

            var start = new V3(from.X, height, from.Y);
            V2 moved = room.VerifyMove(ref start, ref to, kodSpeed);
            if (moved.X == 0f && moved.Y == 0f) return false;
            landed = new V2(from.X + moved.X, from.Y + moved.Y);
            return true;
        }
        catch { return false; }
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

// ---------------------------------------------------------------------
// The two classes below live in THIS file on purpose. WorldSync.cs is
// linked, one file at a time, into Tools/Meridian59.Net8World's project,
// and that project lists its sources by name; a class in a file of its
// own would leave that tool unable to build. Keeping them here costs
// nothing and does not need the tool's project file touched.
// ---------------------------------------------------------------------

/// <summary>
/// The two particle effects the reference hangs on room objects: a flame
/// on the brazier and a black hole over the news globe.
///
/// WHAT THE REFERENCE DOES
/// -----------------------
/// A 3D model may carry a list of ParticleUniverse systems in its XML.
/// Fourteen models ship (Resources/models/*); exactly two use the list:
///
///     brazier.xml:10-12  mp_torch  at (0, 33, 0), groups 2 3 4 5 6 7
///     news.xml:10-13      blackHole at (0, 50, 0), groups 1
///
/// `RemoteNode3D::CreateParticles` (RemoteNode3D.cpp:195-235) builds one
/// system per entry, moves every technique to the entry's position
/// (:215-216), attaches it to the object's node (:222) and creates it
/// DISABLED (:228). `RemoteNode3D::UpdateParticle` (:322-345) then turns
/// it on when the object's <c>Animation.CurrentGroup</c> is one of the
/// listed groups and off when it is not, and is called once at creation
/// (:231) and again whenever the object's Animation property changes
/// (`OnRoomObjectPropertyChanged`, :86-97). So the
/// brazier burns in groups 2-7 and is cold in group 1 - it is a lit and
/// an unlit brazier - and the news globe's black hole runs in group 1.
///
/// Which object gets which model is decided by name: the object's overlay
/// file with ".bgf" swapped for ".xml" must exist in the models folder
/// (ControllerRoom.cpp:1482-1486, RemoteNode3D.cpp:10-18). This client
/// draws every object from its BGF, so the same name is the key here.
///
/// The models' units are Ogre units, and an Ogre unit is one server
/// position unit: `RemoteNode::RefreshPosition` sets the scene node from
/// <c>Position3D</c> unscaled (RemoteNode.cpp:510-518, Util.h:58-61). The
/// renderer's world is sixteen times that (M59Geo.KodToRoom), so every
/// length below is multiplied by <see cref="U"/>. The brazier mesh is 28.3
/// units tall and 12.7 across (its mesh bounds, brazier.mesh), so a
/// flame at y=33 sits just above the bowl; the globe's stand is 42 tall
/// and the black hole hangs at 50.
///
/// WHAT THIS DOES INSTEAD
/// ----------------------
/// The renderer has no scene graph to hang a particle system on: it is a
/// column renderer whose only things-in-the-world are billboard sprites
/// (Renderer.Sprites). So each live particle is one more billboard in
/// that list, made fresh every frame by <see cref="Add"/> - the same idea
/// WeatherOverlay uses for rain and snow, except that those are bolted
/// to the camera (ControllerRoom.cpp:65-67) and these are bolted to an
/// object, so they live in world space and go through the renderer's
/// own depth test, perspective and wall occlusion. That is better than an
/// overlay would be: a flame behind a pillar is behind it.
///
/// The simulation is mirrored from the two .pu scripts: emission rates,
/// lifetimes, speeds, sizes, colour ramps, the particle quota, the
/// texture frame choice. The divergences, all forced by the sprite pass
/// and stated here so nobody has to find them by looking:
///
///  1. NO ADDITIVE BLEND. Every one of the reference's particle materials
///     is `scene_blend add` (particles.material:9, :26, :45); the sprite
///     pass can only alpha-blend a whole sprite at one opacity, and a
///     texel is either drawn or skipped (Renderer.DrawSprites). An
///     additive colour c is therefore drawn as the hue c / max(c) at an
///     opacity of max(c): the same colour, the same brightness envelope,
///     but it darkens what it lies over instead of only brightening it,
///     and two overlapping particles do not sum. Soft edges become a
///     hard cut at a brightness threshold, for the same reason.
///  2. NO LIGHTING. The particle materials say `lighting off`; a sprite
///     is shaded by the room's light like any object. The tint is scaled
///     up by the reciprocal of that shading (capped) so a flame is not
///     dimmed by a dark room - fire is brightest in one.
///  3. FLAMES DO NOT BILLBOARD-SPIN. PU billboards face the camera and so
///     do these. The black hole's second technique is `oriented_self`
///     streaks, which lie along their own velocity in 3D
///     (blackHole.pu:39); a sprite cannot be rotated, so each streak
///     is drawn as a short run of round dots laid along the same 3D
///     segment, which perspective then foreshortens correctly.
///  4. The ParticleUniverse plug-in is not in this tree, only the
///     scripts. Three behaviours below are therefore from its
///     documentation, not read from its source, and are marked as such:
///     a Scale affector's value is a rate in units per second, a
///     Gravity affector pulls toward the system's own position with an
///     inverse-square law, and an emitter with no time_to_live gets 10
///     seconds. If any of those is wrong the shapes change and the
///     numbers in the scripts do not.
///  5. Curved dynamic attributes (`dyn_curved_linear`) are interpolated
///     linearly between their control points.
///
/// Both systems keep their particles in world space. PU's default is
/// `keep_local false`, and neither script sets it.
/// </summary>
public sealed class ObjectParticles
{
    /// <summary>Ogre (server) units to renderer world units: M59Geo.KodToRoom.</summary>
    const float U = 16f;

    // ---------------------------------------------------------------
    // Which objects, from the model XML
    // ---------------------------------------------------------------

    enum Kind { Torch, BlackHole }

    /// <summary>One `<particle>` entry of a model's XML.</summary>
    sealed class Def
    {
        public Kind Kind;
        /// <summary>The particle's `position` y (brazier.xml:11, news.xml:11).</summary>
        public float Y;
        /// <summary>The `groups` list (brazier.xml:12, news.xml:12).</summary>
        public int FirstGroup, LastGroup;
    }

    // brazier.xml:10-12 - y 33, groups "2 3 4 5 6 7".
    // news.xml:11-13    - y 50, groups "1".
    static readonly Dictionary<string, Def> Models = new Dictionary<string, Def>(StringComparer.OrdinalIgnoreCase)
    {
        ["brazier.xml"] = new Def { Kind = Kind.Torch,     Y = 33f, FirstGroup = 2, LastGroup = 7 },
        ["news.xml"]    = new Def { Kind = Kind.BlackHole, Y = 50f, FirstGroup = 1, LastGroup = 1 },
    };

    /// <summary>
    /// The model entry for an overlay file, or null. The reference's own
    /// test is the file name with BGF replaced by XML existing in the
    /// models folder (ControllerRoom.cpp:1482-1486); of those folders only two
    /// carry particles.
    /// </summary>
    static Def Find(string overlayFile)
    {
        if (string.IsNullOrEmpty(overlayFile)) return null;
        int dot = overlayFile.LastIndexOf('.');
        string xml = (dot >= 0 ? overlayFile.Substring(0, dot) : overlayFile) + ".xml";
        return Models.TryGetValue(xml, out Def d) ? d : null;
    }

    // ---------------------------------------------------------------
    // The textures
    // ---------------------------------------------------------------

    static Tex[] _fire;
    static Tex _flare;

    /// <summary>
    /// Builds the four flame frames and the flare from the baked art (see
    /// <see cref="ObjectParticleArt"/>). Colour is left white-ish: the
    /// scripts' Colour affectors are applied as a sprite tint per frame.
    /// </summary>
    static void EnsureArt()
    {
        if (_fire != null) return;

        byte[] fb = Convert.FromBase64String(ObjectParticleArt.FireB64);
        var fire = new Tex[4];
        for (int f = 0; f < 4; f++)
            fire[f] = Bake(fb, f * 64 * 64, 64, 1.8f, 0.16f);
        _fire = fire;

        // The flare is a round glow whose edge falls away over a wide
        // band; keyed at a low level so the disc has a soft-looking rim
        // rather than only its hot core.
        _flare = Bake(Convert.FromBase64String(ObjectParticleArt.FlareB64), 0, 32, 1.2f, 0.10f);
    }

    /// <summary>
    /// One greyscale frame to a keyed sprite texture: texels under
    /// <paramref name="cut"/> are dropped (alpha-tested, as the sprite
    /// pass can do nothing softer - divergence 1), the rest are drawn at a
    /// brightness boosted by <paramref name="gain"/>.
    /// </summary>
    static Tex Bake(byte[] src, int offset, int size, float gain, float cut)
    {
        var p = new uint[size * size];
        for (int i = 0; i < p.Length; i++)
        {
            float l = src[offset + i] / 255f;
            if (l < cut) { p[i] = 0; continue; }
            uint v = (uint)Math.Min(255f, l * gain * 255f);
            p[i] = 0xFF000000u | (v << 16) | (v << 8) | v;
        }
        var t = new Tex { W = size, H = size, P = p, Shrink = 1 };
        t.RebuildMips();
        // Mip texels average alpha, and the sprite blit treats any
        // non-zero alpha as solid, so the reduced copies are keyed too.
        t.KeyAlpha(128);
        return t;
    }

    // ---------------------------------------------------------------
    // The simulation
    // ---------------------------------------------------------------

    // mp_torch.pu:6    visual_particle_quota 10
    // mp_torch.pu:15   emission_rate 60
    // mp_torch.pu:16   angle 5
    // mp_torch.pu:17-21 time_to_live 0.3..0.7
    // mp_torch.pu:22-26 velocity 4.8..5.4
    // mp_torch.pu:27-31 all_particle_dimensions 12..18
    // mp_torch.pu:32   end_texture_coords_range 3
    const int TorchQuota = 10;
    const float TorchRate = 60f;
    const float TorchConeDegrees = 5f;

    // blackHole.pu:5   visual_particle_quota 2500 (first technique)
    // blackHole.pu:12-17 SphereSurface, rate 200, velocity 3, radius 12
    // blackHole.pu:24-27 Gravity 2700
    // blackHole.pu:28-31 Scale xyz_scale -4.5
    // blackHole.pu:7-8 default particle 12 x 12
    const int HoleQuota = 2500;
    const float HoleRate = 200f;
    const float HoleSpeed = 3f;
    const float HoleRadius = 12f;
    const float HoleGravity = 2700f;
    const float HoleShrink = 4.5f;
    const float HoleSize = 12f;
    // Divergence 4: no time_to_live in the script, so PU's default.
    const float HoleLife = 10f;
    // The inverse-square pull is singular at the centre. PU has no
    // softening that this tree can show; a floor keeps a particle that
    // falls straight through from being flung out at infinite speed.
    const float HoleMinDist = 2.5f;

    // blackHole.pu:41-51 second technique: streaks
    // rate 50, angle 360, ttl 4, velocity 1, width 2..4, height 45..60
    // x_scale -1.2 (:59-62), y_scale -45 (:63-66)
    const float RayRate = 50f;
    const float RayLife = 4f;

    // ---------------------------------------------------------------
    // The cost budget. Measured with the shipping Renderer (barinn.roo,
    // 300 frames interleaved per scenario, medians, noisy 2-core box):
    // the sprite pass has a fixed ~0.8 ms the moment any sprite exists,
    // and after that the price is FILL, not count - the black hole's
    // fat new sparks cost far more than its old shrunken ones. The
    // simulation is ~0.15 ms. At 960x540 one globe 2.5 squares away
    // added ~5.0 ms at full script density (1,126 sprites) and three
    // added ~11.7 ms; with the budget below one adds ~3.2 ms and three
    // still ~3.2 ms (a braziers-only room stays at ~1 ms).
    // ---------------------------------------------------------------

    /// <summary>One grid square in world units.</summary>
    const float Grid = 1024f;

    /// <summary>
    /// Inside this the systems run at the script's own density. Beyond
    /// it the emission rate and quota fall as FullDist / distance, which
    /// is how the object shrinks on screen, so a far object gets fewer
    /// and smaller particles. Cost: none near. Bought: a globe 12
    /// squares off runs ~220 sprites for ~0.2 ms instead of ~1,100.
    /// </summary>
    const float ParticleFullDist = 5f * Grid;

    /// <summary>
    /// Beyond this an object's system does not run at all: not stepped,
    /// not drawn, its particles dropped. A flame twenty squares off is a
    /// couple of pixels. Cost: a flame that pops in when you come within
    /// range and ramps up over ~0.7 s. Bought: nothing at all is spent
    /// on distant braziers.
    /// </summary>
    const float ParticleCullDist = 20f * Grid;

    /// <summary>Density never falls below this fraction inside the cull range.</summary>
    const float ParticleMinScale = 0.2f;

    /// <summary>
    /// Ceiling on particle sprites per frame across every system, shared
    /// max-min fairly in <see cref="End"/> (a small system gets all it
    /// asks for, a greedy one the rest) rather than nearest-first, which
    /// let one globe starve every brazier.
    /// Cost: about a third of the near globe's sparks are not drawn (the
    /// rest are drawn denser to compensate; side by side with the full
    /// 1,126 the ball reads the same) and the streaks never fit; in a
    /// room crowded with globes they split what the braziers leave.
    /// Bought: near-globe cost 5.0 -> ~3.2 ms at 960x540 (~3.5 -> ~2.1
    /// at 768x432), and a hard ceiling however many
    /// globes are in range - three cost the same as one.
    /// </summary>
    public const int MaxParticleSprites = 360;

    /// <summary>
    /// The black hole's streaks are drawn only inside this and only as
    /// <see cref="RayDots"/> dots instead of ten. They are ~50 flashes a
    /// second at a colour of about (0.04, 0.03, 0.19) - the least visible
    /// thing in the effect and over half its sprites at full density.
    /// Cost: the faint spokes are lost whenever the sparks alone fill
    /// the cap, which is every near view. Bought: ~570 sprites.
    /// </summary>
    const float RayMaxDist = 8f * Grid;
    const int RayDots = 4;

    struct Particle
    {
        public float X, Y, Z;          // world units
        public float VX, VY, VZ;       // world units per second
        public float Age, Life;
        public float W, H;             // world units
        public float W0, H0;           // Ogre units at birth
        public int Frame;
    }

    struct Ray
    {
        public float X, Y, Z;
        public float DX, DY, DZ;       // unit direction
        public float Age;
        public float W0, H0;           // Ogre units at birth
    }

    /// <summary>One object's running system(s).</summary>
    sealed class Emitter
    {
        public Def Def;
        public bool On;
        public Random Rng;
        public float Acc, RayAcc;
        public bool Seen;
        public readonly List<Particle> Live = new List<Particle>();
        public readonly List<Ray> Rays = new List<Ray>();
    }

    readonly Dictionary<uint, Emitter> _emitters = new Dictionary<uint, Emitter>();
    readonly List<uint> _dead = new List<uint>();

    /// <summary>What this frame's Add calls decided to draw, sorted and emitted by End.</summary>
    struct Cand
    {
        public Emitter E;
        public float Dist;
        /// <summary>Sprites it would draw with no cap: what the fair share is measured against.</summary>
        public int Demand;
        public object Tag;
        public List<Renderer.Sprite> Into;
        public float Comp, ToX, ToY;
    }
    readonly List<Cand> _cands = new List<Cand>();
    static readonly Comparison<Cand> ByDemand = (a, b) =>
    {
        int d = a.Demand.CompareTo(b.Demand);
        return d != 0 ? d : a.Dist.CompareTo(b.Dist);
    };

    /// <summary>
    /// overlay file name -> model entry (null for the great majority of
    /// objects), so the per-object, per-frame test costs one hash and no
    /// string is built. Bounded: names come from a fixed resource set.
    /// </summary>
    readonly Dictionary<string, Def> _defCache = new Dictionary<string, Def>();

    double _last = double.NaN;
    float _dt, _eyeX, _eyeY;
    Renderer _r;

    /// <summary>How many particle sprites the last frame produced, for a test to check.</summary>
    public int Drawn { get; private set; }

    /// <summary>
    /// Leaves a room: every system stops at once and every particle is
    /// gone, as `UnloadRoom` clears the room's nodes (ControllerRoom.cpp:498-520) and
    /// `RemoteNode3D`'s destructor destroys their systems
    /// (RemoteNode3D.cpp:60-71).
    /// </summary>
    public void Reset()
    {
        _emitters.Clear();
        _cands.Clear();
        _last = double.NaN;
        Drawn = 0;
    }

    /// <summary>
    /// Starts a frame. <paramref name="seconds"/> is the view's clock (the
    /// step is its change since the last frame, capped so a stall does
    /// not fire a second's worth of particles in one go); the eye is
    /// where the viewer stands in world units, for the light and the
    /// draw-order nudge.
    /// </summary>
    public void Begin(double seconds, float eyeX, float eyeY, Renderer renderer)
    {
        _dt = double.IsNaN(_last) ? 0f : (float)Math.Clamp(seconds - _last, 0.0, 0.25);
        _last = seconds;
        _eyeX = eyeX; _eyeY = eyeY;
        _r = renderer;
        Drawn = 0;
        _cands.Clear();
        if (_emitters.Count == 0) return;
        foreach (Emitter e in _emitters.Values) e.Seen = false;
    }

    /// <summary>
    /// Drops the systems of objects that left the room this frame, which
    /// is what the reference does when the object's node is destroyed.
    /// </summary>
    public void End()
    {
        if (_emitters.Count == 0) { _cands.Clear(); return; }

        // Max-min fair share of the cap (water-filling). Smallest demand
        // first; each system may take up to an equal share of what is
        // left, and whatever a small one does not use rolls on to the
        // larger ones. A brazier asks for ~10 sprites and always gets
        // them; the globe (demand in the thousands) gets the remainder.
        // Before this the cap went to the nearest object first, so one
        // globe's 2,500-particle quota ate all 360 and every brazier in
        // the room drew nothing (371 sprites, no flames; the same two
        // braziers alone drew 24 and both burned). Ties go to the
        // nearer object. Ceiling division: the last systems still get a
        // sprite or two when there are more systems than the cap.
        if (_cands.Count > 1) _cands.Sort(ByDemand);
        for (int i = 0; i < _cands.Count; i++)
        {
            Cand c = _cands[i];
            int left = _cands.Count - i;
            int remaining = MaxParticleSprites - Drawn;
            if (remaining <= 0) break;
            int room = Math.Min(c.Demand, (remaining + left - 1) / left);
            if (c.E.Def.Kind == Kind.Torch) EmitTorch(c.E, c.Into, c.Tag, c.Comp, c.ToX, c.ToY, room);
            else EmitHole(c.E, c.Into, c.Tag, c.Comp, room);
        }
        _cands.Clear();

        _dead.Clear();
        foreach (var kv in _emitters) if (!kv.Value.Seen) _dead.Add(kv.Key);
        foreach (uint id in _dead) _emitters.Remove(id);
    }

    /// <summary>
    /// Advances the object's system, if it has one, and appends this
    /// frame's particles to <paramref name="into"/> as sprites.
    ///
    /// <paramref name="group"/> is the object's current animation group
    /// (`roomObject->Animation->CurrentGroup`, RemoteNode3D.cpp:328).
    /// The sprites carry <paramref name="tag"/> - the object itself - so a
    /// tap on a flame lands on the brazier, as it would in the reference
    /// where the particle is part of the object's node, rather than being
    /// swallowed by a sprite that belongs to nothing.
    /// </summary>
    public void Add(uint id, string overlayFile, int group, float wx, float wy, float baseZ,
                    object tag, List<Renderer.Sprite> into)
    {
        if (overlayFile == null) return;
        if (!_defCache.TryGetValue(overlayFile, out Def def))
        {
            if (_defCache.Count > 512) _defCache.Clear();
            def = Find(overlayFile);
            _defCache[overlayFile] = def;
        }
        if (def == null) return;

        // Distance to the viewer, which is also what the shading below
        // needs. Beyond the cull range the system is not run, and what it
        // held is dropped so it costs nothing to keep.
        float nx = _eyeX - wx, ny = _eyeY - wy;
        float l = MathF.Sqrt(nx * nx + ny * ny);
        if (l > ParticleCullDist)
        {
            if (_emitters.TryGetValue(id, out Emitter far))
            {
                far.Seen = true; far.On = false;
                far.Live.Clear(); far.Rays.Clear(); far.Acc = far.RayAcc = 0f;
            }
            return;
        }
        EnsureArt();

        if (!_emitters.TryGetValue(id, out Emitter e))
        {
            // Seeded by the object so a run is repeatable frame for
            // frame, which a screenshot comparison depends on.
            e = new Emitter { Def = def, Rng = new Random(unchecked((int)id * 7919 + 17)) };
            _emitters[id] = e;
        }
        e.Seen = true;

        // The gate: RemoteNode3D::UpdateParticle (:322-345). Turning off
        // is `stop()`, which is immediate - the particles do not finish
        // their lives - the same reading WeatherOverlay.Reset makes.
        bool want = group >= def.FirstGroup && group <= def.LastGroup;
        if (!want) { e.On = false; e.Live.Clear(); e.Rays.Clear(); e.Acc = e.RayAcc = 0f; return; }
        e.On = true;

        // Density by distance: how big the object is on screen.
        float scale = Math.Clamp(ParticleFullDist / MathF.Max(l, 1f), ParticleMinScale, 1f);
        float ox = wx, oy = wy, oz = baseZ + def.Y * U;
        if (def.Kind == Kind.Torch) StepTorch(e, ox, oy, oz, scale);
        else StepHole(e, ox, oy, oz, scale, l <= RayMaxDist);

        // Sprite light compensation, divergence 2: what the renderer's
        // ordinary object shading (ambient plus sun, facing the viewer)
        // would do to this sprite, undone, capped at 2.5x.
        float dist = l;
        if (l > 0f) { nx /= l; ny /= l; }
        float shade = _r != null ? _r.Lit(nx, ny, 0f, Renderer.ObjectAmbientWeight, Renderer.ObjectSunWeight) : 1f;
        float comp = 1f / MathF.Max(0.4f, shade);

        // A hair toward the viewer, so the flame sorts in front of the
        // brazier sprite it sits on rather than tying with it: the sort
        // is by depth alone and a tie is broken arbitrarily.
        float toX = l > 0f ? nx * 24f : 0f, toY = l > 0f ? ny * 24f : 0f;
        if (def.Kind != Kind.Torch) toX = toY = 0f;

        // Emitted by End, once every object's distance is known and the
        // cap can be spent nearest-first.
        int demand = e.Live.Count + (def.Kind == Kind.Torch ? 0 : e.Rays.Count * RayDots);
        _cands.Add(new Cand { E = e, Dist = dist, Demand = demand, Tag = tag, Into = into, Comp = comp, ToX = toX, ToY = toY });
    }

    // ---- brazier flame ---------------------------------------------

    void StepTorch(Emitter e, float ox, float oy, float oz, float scale)
    {
        float dt = _dt;
        Random rng = e.Rng;

        // Age and move the ones already flying.
        for (int i = e.Live.Count - 1; i >= 0; i--)
        {
            Particle p = e.Live[i];
            p.Age += dt;
            if (p.Age >= p.Life) { e.Live.RemoveAt(i); continue; }

            float f = p.Age / p.Life;
            p.X += p.VX * dt; p.Y += p.VY * dt; p.Z += p.VZ * dt;

            // The Scale affector (mp_torch.pu:41-55). Divergence 4: the
            // curve's value is taken as a change in dimension per second.
            // x: 0.3 until half-life, then down to -3 at the end;
            // y: 15 falling to -3 by 60% of life and staying there.
            p.W = MathF.Max(1f, p.W + Curve(f, 0f, 0.3f, 0.5f, 0.3f, 1f, -3f) * dt * U);
            p.H = MathF.Max(1f, p.H + Curve(f, 0f, 15f, 0.6f, -3f) * dt * U);
            e.Live[i] = p;
        }

        // Emit. The quota is a hard ceiling on live particles
        // (mp_torch.pu:6); an emitter that is full simply does not emit,
        // it does not save the debt up for later.
        // A far flame is a small one: the quota, which is what bounds
        // the count, shrinks with the object (never under 3).
        int quota = Math.Max(3, (int)MathF.Round(TorchQuota * scale));
        e.Acc += TorchRate * dt;
        while (e.Acc >= 1f)
        {
            e.Acc -= 1f;
            if (e.Live.Count >= quota) { e.Acc = 0f; break; }

            // A point emitter: everything starts at the entry's position.
            // Direction is up, within the cone of `angle 5`
            // (mp_torch.pu:16); PU's angle is the full spread about the
            // direction, so the tilt is random up to it.
            float tilt = (float)(rng.NextDouble() * TorchConeDegrees * Math.PI / 180.0);
            float az = (float)(rng.NextDouble() * Math.PI * 2.0);
            float speed = Lerp(4.8f, 5.4f, (float)rng.NextDouble()) * U;
            float size = Lerp(12f, 18f, (float)rng.NextDouble());
            e.Live.Add(new Particle
            {
                X = ox, Y = oy, Z = oz,
                VX = MathF.Sin(tilt) * MathF.Cos(az) * speed,
                VY = MathF.Sin(tilt) * MathF.Sin(az) * speed,
                VZ = MathF.Cos(tilt) * speed,
                Life = Lerp(0.3f, 0.7f, (float)rng.NextDouble()),
                W0 = size, H0 = size, W = size * U, H = size * U,
                // `end_texture_coords_range 3`: one of frames 0..3,
                // chosen at birth.
                Frame = rng.Next(0, 4),
            });
        }
    }

    void EmitTorch(Emitter e, List<Renderer.Sprite> into, object tag, float comp, float toX, float toY, int room)
    {
        foreach (Particle p in e.Live)
        {
            if (room <= 0) return;
            float f = p.Age / p.Life;

            // The Colour affector (mp_torch.pu:34-39): black at birth,
            // full orange from 30% to 50% of life, black again at death.
            // Additive black is nothing, so the envelope is the opacity
            // (divergence 1).
            float env = Curve(f, 0f, 0f, 0.3f, 1f, 0.5f, 1f, 1f, 0f);
            if (env <= 0.02f) continue;

            into.Add(new Renderer.Sprite
            {
                X = p.X + toX, Y = p.Y + toY,
                // Centred on the particle: a PU billboard's origin is its
                // middle, a sprite's is the foot.
                BaseZ = p.Z - p.H * 0.5f,
                Width = p.W, Height = p.H,
                Texture = _fire[p.Frame],
                Opacity = 0.9f * env,
                // (1, 0.45098, 0.235294), mp_torch.pu:37.
                TintR = 1f * comp, TintG = 0.45098f * comp, TintB = 0.235294f * comp,
                Tag = tag,
            });
            Drawn++; room--;
        }
    }

    // ---- news globe black hole -------------------------------------

    void StepHole(Emitter e, float ox, float oy, float oz, float scale, bool rays)
    {
        float dt = _dt;
        Random rng = e.Rng;

        // Several sub-steps: the pull is strong and a 60 Hz step is not
        // small enough for a particle passing near the centre.
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / 0.02f));
        float h = dt / steps;

        for (int s = 0; s < steps && dt > 0f; s++)
        {
            for (int i = e.Live.Count - 1; i >= 0; i--)
            {
                Particle p = e.Live[i];
                p.Age += h;

                // The Scale affector: xyz_scale -4.5 (blackHole.pu:30)
                // shrinks it 4.5 Ogre units a second from 12, and a
                // particle with no size left is not drawn and is done
                // with (divergence 4).
                float size = HoleSize - HoleShrink * p.Age;
                if (p.Age >= p.Life || size <= 0f) { e.Live.RemoveAt(i); continue; }
                p.W = p.H = size * U;

                // The Gravity affector: 2700 toward the system's own
                // position, inverse square (blackHole.pu:24-27,
                // divergence 4), floored near the centre.
                float dx = ox - p.X, dy = oy - p.Y, dz = oz - p.Z;
                float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                float dOgre = MathF.Max(HoleMinDist, d / U);
                float a = HoleGravity / (dOgre * dOgre) * U;
                if (d > 1e-3f)
                {
                    p.VX += dx / d * a * h; p.VY += dy / d * a * h; p.VZ += dz / d * a * h;
                }
                p.X += p.VX * h; p.Y += p.VY * h; p.Z += p.VZ * h;
                e.Live[i] = p;
            }

            // Rate and quota follow the object's size on screen; the
            // sparks already in flight are left to finish.
            int quota = (int)(HoleQuota * scale);
            e.Acc += HoleRate * scale * h;
            while (e.Acc >= 1f)
            {
                e.Acc -= 1f;
                if (e.Live.Count >= quota) { e.Acc = 0f; break; }

                // SphereSurface: a random point on the sphere of radius
                // 12 (blackHole.pu:12-17), leaving along its own normal
                // at velocity 3.
                Vector3f n = UnitSphere(rng);
                e.Live.Add(new Particle
                {
                    X = ox + n.X * HoleRadius * U,
                    Y = oy + n.Y * HoleRadius * U,
                    Z = oz + n.Z * HoleRadius * U,
                    VX = n.X * HoleSpeed * U, VY = n.Y * HoleSpeed * U, VZ = n.Z * HoleSpeed * U,
                    Life = HoleLife,
                    W0 = HoleSize, H0 = HoleSize, W = HoleSize * U, H = HoleSize * U,
                });
            }
        }

        // Streaks, blackHole.pu:33-75: a point emitter sending them off in
        // every direction (angle 360) at velocity 1, for 4 seconds.
        if (!rays) { e.Rays.Clear(); e.RayAcc = 0f; return; }
        float rdt = dt;
        for (int i = e.Rays.Count - 1; i >= 0; i--)
        {
            Ray r = e.Rays[i];
            r.Age += rdt;
            // y_scale -45 and x_scale -1.2 (:59-66): the length is gone
            // in under a third of a second, which is why these are
            // flashes and not lines.
            float hOgre = r.H0 - 45f * r.Age;
            float wOgre = r.W0 - 1.2f * r.Age;
            if (r.Age >= RayLife || hOgre <= 0f || wOgre <= 0f) { e.Rays.RemoveAt(i); continue; }
            e.Rays[i] = r;
        }
        e.RayAcc += RayRate * rdt;
        while (e.RayAcc >= 1f)
        {
            e.RayAcc -= 1f;
            Vector3f n = UnitSphere(rng);
            e.Rays.Add(new Ray
            {
                X = ox, Y = oy, Z = oz, DX = n.X, DY = n.Y, DZ = n.Z,
                W0 = Lerp(2f, 4f, (float)rng.NextDouble()),
                H0 = Lerp(45f, 60f, (float)rng.NextDouble()),
            });
        }
    }

    void EmitHole(Emitter e, List<Renderer.Sprite> into, object tag, float comp, int room)
    {
        // Over the cap: draw an even share of the sparks, not the first
        // ones. The list is in birth order, so a cut off the end would
        // keep only the oldest - the smallest, shrunk to nothing - and
        // drop the fat new ones that make the ball.
        int have = e.Live.Count, take = Math.Min(have, room), err = 0;
        // The ones left are drawn a little denser to make up, so the ball
        // thins less than the count says (capped: opacity tops out at 1).
        float boost = take < have ? MathF.Min(1.5f, (float)have / Math.Max(1, take)) : 1f;
        foreach (Particle p in e.Live)
        {
            if (take < have) { err += take; if (err < have) continue; err -= have; }
            if (room <= 0) return;
            float f = p.Age / p.Life;

            // The Colour affector (blackHole.pu:18-23): deep blue
            // (0, 0, 0.2) at birth, brightening to (0.8, 0.8, 1) at 90%
            // of the life, white at the end. A spark dies at 27% of a
            // ten second life, so it only ever gets to the middle of
            // that ramp.
            float r, g, b;
            if (f < 0.9f) { float k = f / 0.9f; r = g = 0.8f * k; b = Lerp(0.2f, 1f, k); }
            else { float k = (f - 0.9f) / 0.1f; r = g = Lerp(0.8f, 1f, k); b = 1f; }

            // Additive colour to hue + opacity, divergence 1.
            float peak = MathF.Max(r, MathF.Max(g, b));
            if (peak <= 0.02f) continue;
            into.Add(new Renderer.Sprite
            {
                X = p.X, Y = p.Y, BaseZ = p.Z - p.H * 0.5f,
                Width = p.W, Height = p.H,
                Texture = _flare,
                Opacity = MathF.Min(1f, 0.9f * peak * boost),
                TintR = r / peak * comp, TintG = g / peak * comp, TintB = b / peak * comp,
                Tag = tag,
            });
            Drawn++; room--;
        }

        // Streaks as runs of dots, divergence 3. Fewer, larger dots than
        // the script's length calls for (see RayDots), and the first to go
        // when the cap bites.
        const int Dots = RayDots;
        foreach (Ray ray in e.Rays)
        {
            if (room < Dots) return;
            float hW = (ray.H0 - 45f * ray.Age) * U;
            float wW = (ray.W0 - 1.2f * ray.Age) * U;

            // The Colour affector, `colour_operation multiply`
            // (blackHole.pu:67-74): fades in over the first fifth of the
            // 4 second life, holds, fades out over the last fifth.
            float f = ray.Age / RayLife;
            float fade = Curve(f, 0f, 0f, 0.2f, 1f, 0.8f, 1f, 1f, 0f);
            if (fade <= 0.01f) continue;

            // The emitter's colour (0.1, 0.1, 1) times the streak
            // texture's own body colour, about (112, 88, 48) of 255
            // (pump_streak_03.png), times the fade: a very dim blue.
            float cr = 0.1f * 0.44f * fade, cg = 0.1f * 0.345f * fade, cb = 1f * 0.19f * fade;
            float peak = MathF.Max(cr, MathF.Max(cg, cb));

            float spacing = hW / Dots;
            float dot = MathF.Max(wW, spacing * 0.8f);
            for (int k = 0; k < Dots; k++)
            {
                float t = ((k + 0.5f) / Dots - 0.5f) * hW;
                float zc = ray.Z + ray.DZ * t;
                into.Add(new Renderer.Sprite
                {
                    X = ray.X + ray.DX * t, Y = ray.Y + ray.DY * t,
                    BaseZ = zc - dot * 0.5f,
                    Width = dot, Height = dot,
                    Texture = _flare,
                    Opacity = peak,
                    TintR = cr / peak * comp, TintG = cg / peak * comp, TintB = cb / peak * comp,
                    Tag = tag,
                });
                Drawn++; room--;
            }
        }
    }

    // ---- small helpers ---------------------------------------------

    struct Vector3f { public float X, Y, Z; }

    static Vector3f UnitSphere(Random rng)
    {
        // Uniform on the sphere: z uniform in [-1,1], azimuth uniform.
        float z = (float)(rng.NextDouble() * 2.0 - 1.0);
        float a = (float)(rng.NextDouble() * Math.PI * 2.0);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3f { X = r * MathF.Cos(a), Y = r * MathF.Sin(a), Z = z };
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// A piecewise-linear dynamic attribute: control points as (x, y)
    /// pairs, held flat beyond either end. Divergence 5: PU's
    /// `dyn_curved_linear` is a spline through the same points.
    /// </summary>
    static float Curve(float x, params float[] xy)
    {
        if (x <= xy[0]) return xy[1];
        for (int i = 2; i < xy.Length; i += 2)
        {
            if (x <= xy[i])
            {
                float t = (x - xy[i - 2]) / (xy[i] - xy[i - 2]);
                return Lerp(xy[i - 1], xy[i + 1], t);
            }
        }
        return xy[xy.Length - 1];
    }
}

/// <summary>
/// The two particle textures, baked from the reference's own art rather
/// than drawn from imagination.
///
///  - <c>FireB64</c>: <c>Resources/particles/mp_fire_02_2x2.dds</c>, the
///    1024x1024 BC-compressed sheet <c>mp_torch.pu</c> names as its
///    material (`particles.material:1-17`, `mp_torch.pu:7`). It is four
///    512x512 flame frames in a 2x2 grid (`texture_coords_rows 2`,
///    `texture_coords_columns 2`, `mp_torch.pu:10-11`). Each frame is
///    reduced to 64x64 and kept as its brightest channel: the sheet is
///    greyscale light on black, and the colour comes from the script's
///    Colour affector, not the picture (`mp_torch.pu:34-39`). Frames are
///    stored row-major, 4096 bytes each - frame 0 top-left, 1 top-right,
///    2 bottom-left, 3 bottom-right, the order ParticleUniverse indexes
///    a sheet in.
///  - <c>FlareB64</c>: <c>Resources/particles/pump_flare_04.png</c>, the
///    256x256 soft round flare `PUMediaPack/Flare_04` uses
///    (`particles.material:19-36`) for the black hole's inflowing sparks
///    (`blackHole.pu:6`). Reduced to 32x32, one byte per texel.
///
/// Why baked into the source and not read from Resources/: the resource
/// folder a player points the client at holds the game's BGFs and ROOs
/// (M59Paths.HasContent), not the Ogre client's own particle folder, and
/// on Android res:// is an entry in a pack rather than a path. Twenty
/// kilobytes of source is cheaper than a lookup that can come up empty.
/// Regenerate by resizing those two files as above (Lanczos) and
/// base64-encoding the raw bytes.
/// </summary>
static class ObjectParticleArt
{
    internal const string FireB64 =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQMDAwICAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAgcNEA4NCQUCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAABAwsXIyglHhcNBAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAECBQ0bKjY9OCshFAcBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEDCBEYJDY9" +
        "Qj0tIRUHAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIFDRgcNZpcPFpdLx8TBgEAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQMIEyIqVcD/tIStkS8dEQQBAAAAAAAAAAAAAAAAAAAA" +
        "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" +
        "Q4KIamfN+k8SEgUBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIHDhYeGjDd/nslOjs2LCUiHx0cIi1vo5Sg+LUUFggC" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEFDxoeIBWq/4IRKzY1Kh8gIB4YFB0hPKOguvtVDg4DAQAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAACBxUfIw6F/4EBDx4lIBUPFBkVDQsTHBZ1tN/BFxIGAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAgcSHwhm/3EAAwYMDwsGBQgKCAQECA4JK7P2ZwsOAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAED" +
        "CwdU11AAAwECAwMCAQECAwIBAQIDCAJ42x0RBwEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwIheyIAAwAAAAEB" +
        "AAAAAAAAAAAAAAMFIUsNCwMBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAAAwAAAAAAAAAAAAAAAAAAAAAB" +
        "BAYCCQMBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAgAAAAAAAAAAAAAAAAAAAAAAAAECBAIBAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAQEBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAACAgMDAgICAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAwYJCgoJ" +
        "BwQDAQEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAgcJCRAUFhQQCwYDAgEAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQQGNltGNyskIR0VDQcEAgEAAQEBAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEFByik2baSakcwJB8WDQcDAwMDAgAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAgkQK5/49dq3iVszIx8VDQgGBAMBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAIFEiAwfuj/+OfSql4vJx4UDAcEAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAABAweMDdg0P/9/PzpnFU5KhsPCQQCAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIIFCQ0OU2+" +
        "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" +
        "AAAAAAAAAAAAAAEGCiCs/4UZLCckGg8KDRMWGh8jJystMTc5NS0nIyUqMTxBxNskDAcBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAC" +
        "BAkRkf+TFyomJRwSDRATFRcZGyIoKzA1NTApIyAkKC84L7nJFgsFAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQIHAlb2nhMpJiUe" +
        "FA8SFBUUExQbJSgrLy8sJh8dISctNSe3sAkKAwEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAwIPu7MLJSUjHRYQEBMTEA0MEhsh" +
        "IiIjIh4ZGB0kKS8fuI8BCQIBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEDADzHHRUdHBkTDQsNDQoHBwoQFBUTEhIREBMYHiIk" +
        "EaZbAAYBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAWEQDFBMRDQcGBgYFAwMEBwgICAcHBwgLERUWFAlWIAADAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAHBQgKCQYDAgICAQEAAQIDAgIBAgIDBQkLCwkFCQMBAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAABAAICAwMCAQABAQAAAAAAAQEAAAAAAQIDBAQDAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAQEBAQAAAAAAAAAAAAAAAAAAAAAAAQEBAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQIDAgIBAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQMHCQkFAgEAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQQKEBISDAUCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAgULExYWFREJAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAABAQECBAcOERERExUSCQIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQEB" +
        "AgQGCA4TFDBZXi8VEAYCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQIDBggMEBsiJnXU9veP" +
        "Fg0EAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQMHCw4SHCw4RJv4//f/mBEJAgAAAAAAAAAA" +
        "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" +
        "IhkRDRAWGiIlIRwZDtOCAAUBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgoDSuc5HiYmIxwSCggNFBkfIBsW" +
        "CCuwFwICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEDDASVVBAhIRwTCQQDBw0RFBQRDAMaGQACAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgQCJD0OGhcRCQQBAQMFBwgIBgQDAAABAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAwIEDQ8NCAQBAAABAQICAgEBAAEBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAACAwQGBAMBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAEBAQEBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQIBAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAgEAAgQCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABBRArMhcCBAQBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAABBAlMwtu3YhMDBQEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAQQRg/b8/vGkLAMFAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEEDXjt9fv6" +
        "+r80BAQBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgVA1fXv/Pr1syEDAwAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADC3n27Ob7+fB/BwUBAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQETnP3h3Pj80S4EAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACACPC+dLX/PhuBwYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "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" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAwkSC7yOKTU+5dIUEAoDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAEFDQoz4FkyLI/zLA0NBAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAgYRA27GMTU621EHEAUBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADCBIFlHMj" +
        "HnRpBBEGAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQMJCxpuHhsfPw8PBgEAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAwgJERURDREPCQQBAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABBQYIBwYFBQQCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAECAgIBAQEBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    internal const string FlareB64 =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AQECAgQEAQIBAQAAAAAAAAAAAAAAAAAAAAAAAAAAAQIDAwQEBwcEBAMDAgEAAAAAAAAAAAAAAAAAAAAAAAIDBAQGBgcLCgYGBgUE" +
        "AwEAAAAAAAAAAAAAAAAAAAECAwUGBwgJCQ4OCgkIBwcFAwIBAAAAAAAAAAAAAAABAgQFBwkKCwwMExIMDAsJCgcFBAIBAAAAAAAA" +
        "AAAAAAMFBggJCw4OEBAXFxAQDg4MCQgFAwIAAAAAAAAAAQMDAwYJCQsOERITExwcFBMSEg4MCggFAwMCAQAAAAAAAwcICAkNDxAU" +
        "FxcYIiIYFxgTEA4LCQgJBwMAAAAAAAABBAoQEA4SFBYbHR0qKh0dHBYTEA4QEAoFAgAAAAAAAQMFBwsUGhgXGh8mJTY2JScfGRUY" +
        "GxUMBwUCAQAAAAABAwUICw0VISUjJC83Sko4MCIiKCIWDgsIBQMBAAAAAAEEBgkMEBIXIzQ8QFJvb1I/PDclFxIQDAkGAwEAAAAA" +
        "AQQHCQwPFBgcJ0JmhbSzhWdEJxsYFBAMCgcEAgAAAAABBAgKDhIWGyItPma29PS2Zz4tIhwXEg8LCAUCAAAAAAIEBwsOEhcbIy0+" +
        "Z7X09LVnQC4jHBcSDgoHBQIAAAAAAgQHCg0QFBgcJ0NlhLS0hWVCJxwZFBANCgcEAgAAAAABBAYIDA8SFyQ1Oj5Ubm5VPzs0JBcS" +
        "EAwKBwQCAAAAAAEDBggLDhUhJiEiMTlISDoxIyImIRUNCwkGAwEAAAAAAQMFBwsUGhcVGSAoJjU0JikfGRUXGhQLBwUDAQAAAAAA" +
        "AQQKDw8NEBMWHB8dKSkcIB0WFBANEBAKBAEAAAAAAAACBwgICQsOEBQYGBchIhcZGhQQDwwJCAgHAgAAAAAAAAMDAwUICgsOERMT" +
        "ExwcExMUEg4MCggFAwMCAAAAAAAAAAACBAYICQsODhAQFhcQDxAPDAkIBgQCAAAAAAAAAAAAAAECBAUHCQsLDAwSEgwMCwsJBwUE" +
        "AwEAAAAAAAAAAAAAAAECAwUHCAkJCQ4OCQkICAgFAwIAAAAAAAAAAAAAAAAAAAABAwQFBgcHCgsHBgUFBQMCAAAAAAAAAAAAAAAA" +
        "AAAAAAABAgMEBAQHBwQEAwMCAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAEBAQUFAQIBAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
}
