using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meridian59.Common;
using Meridian59.Common.Constants;
using Meridian59.Common.Enums;
using Meridian59.Files.BGF;
using Meridian59.Files.ROO;

/// <summary>
/// First-person renderer for Meridian 59 rooms. Pure C# - no Godot types -
/// so the same code backs both the in-game view and the offline PNG tool
/// that is used to check it.
///
/// Per screen column it collects every wall the ray crosses, sorts by
/// distance and walks them like a Doom-style portal renderer: a one-sided
/// wall (or a two-sided one that still carries a middle texture) closes the
/// column; otherwise only the upper and lower steps are drawn, the visible
/// window narrows, and the ray carries on. Floors and ceilings fill the rest.
///
/// Output is ARGB in a caller-supplied buffer.
/// </summary>
public sealed class Renderer
{
    /// <summary>
    /// Vertical field of view. Vertical, not horizontal, because the
    /// client is landscape now and the buffer's height is the fixed
    /// side: with a horizontal FOV, a wider screen kept the same swath
    /// and lost the sky and the floor instead - at 21:9 the vertical
    /// angle came out at 39 degrees and a duskrat two metres away
    /// filled the screen.
    ///
    /// Forty-five is the game's: `OgreClient.cpp:126` creates the
    /// camera and never calls setFOVy, so it keeps Ogre's default
    /// FOVy of 45 degrees, with the aspect coming from the viewport
    /// (`:374`). Sixty was my own number and it made every room look
    /// bigger than it is - a wider angle puts the walls further away.
    /// </summary>
    public const float FovY = 45f * MathF.PI / 180f;

    /// <summary>
    /// The horizontal field of view never goes below this. Forty-five
    /// vertical on a TALL screen is a 26-degree slit, and while this
    /// client is landscape now, a window taller than it is wide should
    /// still show a room rather than a corridor. Sixty rather than the
    /// old seventy-five so it cannot bind in landscape, where the
    /// game's own vertical angle has to be the one that decides:
    /// 45 vertical is 73 across at 16:9 and 84 at 21:9.
    /// </summary>
    public const float FovXMin = 60f * MathF.PI / 180f;

    /// <summary>
    /// Pixels per unit at unit depth. Whichever of the two fields of
    /// view is the binding one wins - vertical on a wide screen, the
    /// horizontal floor on a tall one - so the view widens with the
    /// screen instead of cropping.
    ///
    /// Render and Pick must agree on this to the last float: they used
    /// to compute it separately, and a picker that disagrees with the
    /// picture is a tap that lands on nothing.
    /// </summary>
    public static float Projection(int W, int H)
        => MathF.Min((H * 0.5f) / MathF.Tan(FovY * 0.5f),
                     (W * 0.5f) / MathF.Tan(FovXMin * 0.5f));
    /// <summary>
    /// How high the eye sits above the floor, in room units.
    ///
    /// 0.6 of a grid square was a guess and it was 23% too low, which is
    /// one of the reasons rooms read as bigger than they are: a low
    /// camera makes everything above it loom. The library has a number
    /// for this - <c>PLAYERHEIGHT = 50</c> in kod units
    /// (GeometryConstants.cs:167), used wherever it needs a sight line
    /// from someone's eyes rather than their feet
    /// (RoomObject.cs:1501-1502) - and 50 kod units is 800 room units.
    ///
    /// The reference does better and takes it from the avatar's own
    /// artwork, 93% of the drawn height (RemoteNode.cpp:405-425), so a
    /// short race sees from lower down. That was written and then taken
    /// out again: it needs the WHOLE composed body, base frame plus
    /// every sub-overlay, and an avatar missing its overlays composes
    /// short - which puts the camera on the floor and makes a rat loom
    /// over you. The reference is exposed to the same thing and gets
    /// away with it because its avatar is always fully dressed. A
    /// constant that is right for everyone is worth more than a
    /// measurement that is right for most and badly wrong for the
    /// rest, so this stays until the composed avatar can be trusted.
    /// </summary>
    public const float EyeHeight = GeometryConstants.PLAYERHEIGHT * M59Geo.HeightToXY;
    public const float FogFar = 4500f;

    /// <summary>
    /// Whether distant surfaces are darkened. Off, because the game does
    /// not do it.
    ///
    /// This renderer used to scale every texel by <c>FogFar / distance</c>,
    /// which is a plausible-looking invention and nothing more. The Ogre
    /// client turns fog off in as many words - "use only 1 directional
    /// light and no fog", `ControllerRoom.cpp:139`, followed by
    /// <c>setManageSceneFog(FOG_NONE)</c> - and the DirectX path disables
    /// D3DRS_FOGENABLE and D3DRS_RANGEFOGENABLE outright
    /// (`ImageBuilders.cpp:578,587`). Light in Meridian comes from the
    /// room's ambient and the avatar's own, both distance-independent, so
    /// a far wall in a lit room is exactly as bright as a near one. The
    /// falloff made long rooms read as much deeper than they are, which is
    /// half of "the rooms seem bigger than they are ingame".
    ///
    /// Kept as a switch rather than deleted so the two can be photographed
    /// side by side; the game's answer is false.
    /// </summary>
    public static bool DistanceFalloff = false;

    /// <summary>
    /// Whether liquid surfaces ripple. On; here so the two can be
    /// photographed against each other.
    /// </summary>
    public static bool Water = true;

    /// <summary>How much light reaches a surface this far away: all of it.</summary>
    public static float Falloff(float distance)
        => DistanceFalloff ? MathF.Min(1f, FogFar / MathF.Max(distance, 1f)) : 1f;

    /// <summary>
    /// How bright the room is, 0 to 1, from the server.
    ///
    /// The reference client does exactly one thing with light: it takes
    /// the larger of the room's AmbientLight and the avatar's own light
    /// (night vision and the like) and sets that as the scene's ambient,
    /// as a plain ratio of 255 - `ControllerRoom::AdjustAmbientLight`
    /// and `Util::LightIntensityToOgreRGB`. It does not use the per-
    /// sector light values at all, even though the library exposes them,
    /// so neither does this: a room is lit by its ambient and shaded by
    /// distance, and nothing else.
    /// </summary>
    public float Brightness = 1f;

    /// <summary>
    /// The sky behind every hole in the geometry, or null for the void.
    /// Which one it is comes from the server, not the room - see
    /// M59Sky - so the game sets this when RoomInfo arrives and again
    /// on BP_CHANGE_BACKGROUND.
    /// </summary>
    public M59Sky Sky;

    /// <summary>
    /// The sky colour for one pixel of a column, or <paramref
    /// name="fallback"/> when there is no sky loaded.
    ///
    /// The ray through row y drops by (y - horizon)/proj per unit of
    /// distance along the camera's forward axis, and this column's
    /// horizontal ray is 1/cosFix as long as that axis, so the vertical
    /// component of the direction is that slope times cosFix. Anchoring
    /// the sky to Horizon() rather than to a true rotation of the
    /// camera keeps it level with the geometry, whose own pitch is the
    /// same shear (Renderer.Horizon) - the reference has a real 3D
    /// camera and rotates both together.
    /// </summary>
    static uint SkyAt(M59Sky sky, uint fallback, float rayA, float cosFix,
                      int y, float horizon, float proj)
    {
        if (sky == null) return fallback;
        float up = (horizon - y) / proj * cosFix;
        return sky.Sample(MathF.Cos(rayA), MathF.Sin(rayA), up);
    }

    public struct Hit { public RooWall Wall; public float Dist, Along, Len; public bool Right; }

    /// <summary>A billboarded object standing on the floor at X,Y.</summary>
    public sealed class Sprite
    {
        public float X, Y;
        /// <summary>World height of the sprite's base (usually the floor).</summary>
        public float BaseZ;
        /// <summary>
        /// How tall the sprite stands, in world XY units. Zero or less
        /// means take it from the art, which is what the game does: the
        /// library sizes an object as its frame's pixels over the file's
        /// shrink factor (RenderInfo), and one of those units is 16 world
        /// units - the same relation wall textures give, where size over
        /// shrink of 64 spans a 1024-unit grid square.
        ///
        /// It matters: with a fixed height a duskrat and a cyclops are the
        /// same size. Measured, a rat comes out 0.57 grid squares tall, a
        /// Knight 0.88 and a cyclops 3.92, and a Knight's eyes then land at
        /// 68% of its height, which is about where eyes go.
        /// </summary>
        public float Height = 0f;

        /// <summary>
        /// Art to draw. When set, the frame is chosen per view from the
        /// object's facing, so it turns as you walk around it.
        /// </summary>
        public BgfFile Bgf;
        /// <summary>Which way the object is facing, 0..4095.</summary>
        public ushort AngleUnits;
        /// <summary>Animation group, 1-based.</summary>
        public int Group = 1;

        /// <summary>Fixed art, used when Bgf is null. Never turns.</summary>
        public Tex Texture;

        /// <summary>
        /// How wide the sprite stands, in world XY units. Zero means take
        /// it from the art's aspect, which is right for a lone frame. A
        /// composed object carries its own width because the compose pads
        /// the picture sideways to keep the main overlay centred - see
        /// RenderInfo, which widens the box by the centring move - so its
        /// aspect is not the art's.
        /// </summary>
        public float Width = 0f;

        /// <summary>
        /// Hangs from <see cref="BaseZ"/> instead of standing on it. The
        /// game marks lamps, signs and the like as hanging and anchors
        /// them at the top; RemoteNode2D picks BBO_TOP_CENTER for exactly
        /// these. Standing them on the floor instead puts a chandelier in
        /// a puddle.
        /// </summary>
        public bool Hanging;

        /// <summary>
        /// Caller's handle on whatever this sprite stands for - the live
        /// view puts the server's RoomObject here so a tap can be turned
        /// back into a thing to look at or attack.
        /// </summary>
        public object Tag;

        /// <summary>
        /// The object's material, as a colour multiplier and an opacity.
        ///
        /// `RemoteNode2D.cpp` picks one of a dozen materials per object
        /// per frame, and `general.material` shows what each of them is:
        /// every one of them is the same pixel shader given a different
        /// `colormodifier` float4. Its last line is
        ///
        ///     pixel = float4(light * colormodifier.rgb * texcol.rgb,
        ///                    texcol.a * colormodifier.a);
        ///
        /// so black (shadowform) is rgb 0,0,0; the target is 5,3,3 - a
        /// red-biased brightening, on top of the red edge; mouseover is
        /// 3,5,3; and the three translucent materials are 1,1,1 with
        /// alpha 0.25, 0.5 and 0.75. The comment in ImageComposerOgre is
        /// worth keeping in mind: those numbers are opacity, not
        /// transparency, so translucent25 is the faintest of them.
        ///
        /// A material is not baked into the composed picture because it
        /// changes without the picture changing - a creature becomes the
        /// target, stops being it, and flashes in between - and baking
        /// it would mean a new composed copy for each.
        /// </summary>
        public float TintR = 1f, TintG = 1f, TintB = 1f, Opacity = 1f;
    }

    /// <summary>A sprite worked out in screen space: where it lands and how big.</summary>
    struct Placed
    {
        public Sprite S;
        public Tex T;
        public float Depth, Left, WPx, HPx, YTop, YBot, Fog;
    }

    /// <summary>Objects drawn after the walls, occluded by them.</summary>
    public readonly List<Sprite> Sprites = new List<Sprite>();

    /// <summary>Per-direction sprite frames, resolved as the view changes.</summary>
    public readonly SpriteCache SpriteFrames = new SpriteCache();

    readonly RooFile _roo;
    readonly TexCache _tex;
    readonly WallGrid _grid;
    readonly FlatAnchors _anchors;

    /// <summary>
    /// Anchor floor and ceiling textures per leaf, as the game does, and
    /// not at the world origin. Off reproduces what this renderer did
    /// before, which is what the reference renders were taken with.
    /// </summary>
    public bool LeafAnchoredFlats { get; set; } = true;
    Scratch[] _scratch = Array.Empty<Scratch>();
    readonly List<Masked> _order = new List<Masked>(256);
    float[] _spriteDepth;
    // Per-column distance to whatever closed that column, for sprite depth.
    float[] _depth = new float[0];

    /// <summary>Set false to fall back to testing every wall (reference path).</summary>
    public bool UseGrid { get; set; } = true;

    /// <summary>
    /// Splits the column loop across cores. Columns are independent - each
    /// writes its own pixels and its own depth slot - so the output is the
    /// same either way; this is checked room by room against the
    /// single-threaded path rather than assumed.
    /// </summary>
    public bool Threaded { get; set; } = true;

    /// <summary>
    /// Measurement knobs. Floor and ceiling fill is 25-60% of a frame and
    /// the sampler is about half of that, which is worth being able to
    /// re-measure rather than re-derive - see the performance note in the
    /// README for what that did and did not buy.
    /// </summary>
    public bool NoFlats { get; set; } = false;
    /// <summary>Takes texel 0 instead of sampling. See <see cref="NoFlats"/>.</summary>
    public bool NoSample { get; set; } = false;

    /// <summary>
    /// Honour WF_TRANSPARENT on two-sided walls: grates, railings and
    /// doorways are drawn with their transparency and you see past them.
    /// Off treats them as solid, which is what this renderer did before,
    /// and is how the difference gets measured.
    /// </summary>
    public bool SeeThroughWalls { get; set; } = true;

    /// <summary>
    /// Honour WF_BACKWARDS, the sidedef flag meaning "draw bitmap
    /// right/left reversed". Off is what this renderer did before, and is
    /// how the difference gets looked at.
    /// </summary>
    public bool HonourBackwards { get; set; } = true;

    /// <summary>
    /// Whether the left side of a wall starts its texture from the other
    /// end, as the library does. See the note at the call site.
    /// </summary>
    public static bool HonourSides = true;

    /// <summary>
    /// WF_NO_VTILE: the texture is drawn once and does not repeat up
    /// the wall. Off, every wall tiles, which is what this renderer
    /// did for solid walls and what Ashton spotted from the game.
    /// </summary>
    public bool HonourNoVTile { get; set; } = true;

    /// <summary>
    /// Seconds of animation time, for scrolling floors and walls - water,
    /// lava, the odd moving wall. Zero by default so every offline render
    /// and every check in Tools/ still produces the same picture; the live
    /// views set it each frame.
    /// </summary>
    public float Time { get; set; } = 0f;

    /// <summary>
    /// Looking up and down, in radians. Zero is level, and the whole
    /// renderer stays exactly as it was at zero.
    ///
    /// A column renderer cannot rotate the camera about its own X axis
    /// without giving up the one thing that makes it fast - that every
    /// wall is vertical on screen. What it can do is shift the horizon,
    /// which is the same trick Doom used: geometrically it is a sheared
    /// projection rather than a rotation, so it exaggerates as you look
    /// further, and it is clamped for that reason.
    /// </summary>
    public float Pitch { get; set; } = 0f;

    /// <summary>The most you can look up or down. Beyond this the shear shows.</summary>
    public const float MaxPitch = 0.55f;

    /// <summary>Screen row the horizon falls on, given the pitch.</summary>
    float Horizon(int H, float proj)
    {
        float p = Math.Clamp(Pitch, -MaxPitch, MaxPitch);
        return H * 0.5f + MathF.Tan(p) * proj;
    }

    /// <summary>
    /// Per-thread working state. The wall grid's visit marker lives here
    /// too, so two threads walking the same grid do not overwrite each
    /// other's stamps.
    /// </summary>
    sealed class Scratch
    {
        public readonly List<Hit> Hits = new List<Hit>(64);
        public readonly List<RooWall> Candidates = new List<RooWall>(64);
        public int[] Stamp;
        public int Tick;
        public int SolidCols;
        public readonly List<Masked> Masked = new List<Masked>(8);
    }

    /// <summary>
    /// A see-through wall met during the column walk, to be drawn once the
    /// walk has passed it.
    ///
    /// The walk goes front to back, and a wall you can see through has to
    /// be painted over whatever is behind it, so these are collected and
    /// drawn in reverse at the end of the column.
    /// </summary>
    struct Masked
    {
        public int Sx, Y0, Y1, SpanTopY, SpanBotY;
        public float Depth, SpanTopH, SpanBotH, Along, Fog, Tpp;
        public int XOff, YOff;
        public bool TopDown, NoVTile;
        public TextureScrollSpeed ScrollSpeed;
        public TextureScrollDirection ScrollDir;
        public Tex T;
    }

    static readonly Comparison<Hit> ByDistanceThenWall = (p, q) =>
    {
        int c = p.Dist.CompareTo(q.Dist);
        return c != 0 ? c : p.Wall.Num.CompareTo(q.Wall.Num);
    };

    public Renderer(RooFile roo, TexCache tex)
    {
        _roo = roo; _tex = tex;
        _grid = new WallGrid(roo);
        _anchors = new FlatAnchors(roo);
    }

    public RooSector SectorAtPoint(float x, float y) => SectorAt(_roo, x, y);

    /// <summary>Renders one frame into <paramref name="px"/> (length W*H, ARGB).</summary>
    public int Render(uint[] px, int W, int H, float camX, float camY, float camZ, float angle)
    {
        float proj = Projection(W, H);
        float horizon = Horizon(H, proj);

        // Kept so a caller can put something on top of the frame - a name
        // over a player's head, say - without repeating the projection or
        // guessing at the camera.
        _lastW = W; _lastH = H;
        _lastCamX = camX; _lastCamY = camY; _lastCamZ = camZ;
        _lastAngle = angle; _lastProj = proj; _lastHorizon = horizon;
        RooSector camSector = SectorAt(_roo, camX, camY);
        int solidCols = 0;

        if (_depth.Length < W) _depth = new float[W];
        for (int i = 0; i < W; i++) _depth[i] = float.MaxValue;

        // One band of columns per core, each with its own scratch. Bands are
        // contiguous so each thread touches a stride of the pixel buffer
        // rather than interleaving cache lines with its neighbours.
        int bands = Threaded ? Math.Min(System.Environment.ProcessorCount, Math.Max(1, W / 48)) : 1;
        EnsureScratch(bands);

        // Where a sprite is, and how far away, so see-through walls drawn
        // afterwards know which pixels they must not cover.
        if (Sprites.Count > 0)
        {
            if (_spriteDepth == null || _spriteDepth.Length < W * H) _spriteDepth = new float[W * H];
            Array.Clear(_spriteDepth, 0, W * H);
        }

        if (bands <= 1)
        {
            RenderBand(_scratch[0], px, W, H, 0, W, camX, camY, camZ, angle,
                       proj, horizon, camSector);
        }
        else
        {
            int per = (W + bands - 1) / bands;
            Parallel.For(0, bands, b =>
            {
                int x0 = b * per, x1 = Math.Min(W, x0 + per);
                if (x0 < x1)
                    RenderBand(_scratch[b], px, W, H, x0, x1, camX, camY, camZ,
                               angle, proj, horizon, camSector);
            });
        }
        for (int b = 0; b < bands; b++) solidCols += _scratch[b].SolidCols;

        DrawSprites(px, W, H, camX, camY, camZ, angle, proj, horizon);
        DrawMasked(px, W, H, bands);
        return solidCols;
    }

    void EnsureScratch(int bands)
    {
        if (_scratch.Length < bands)
        {
            var next = new Scratch[bands];
            Array.Copy(_scratch, next, _scratch.Length);
            for (int i = _scratch.Length; i < bands; i++)
                next[i] = new Scratch { Stamp = new int[_grid != null ? _grid.WallCount : 0] };
            _scratch = next;
        }
        for (int i = 0; i < bands; i++) { _scratch[i].SolidCols = 0; _scratch[i].Masked.Clear(); }
    }

    /// <summary>Renders columns [x0, x1) - the body of the old single loop.</summary>
    void RenderBand(Scratch sc, uint[] px, int W, int H, int x0, int x1,
                    float camX, float camY, float camZ, float angle,
                    float proj, float horizon, RooSector camSector)
    {
        for (int sx = x0; sx < x1; sx++)
        {
            float camOff = (sx - W * 0.5f) / proj;
            float rayA = angle + MathF.Atan(camOff);
            float cosFix = MathF.Cos(rayA - angle);
            float rdx = MathF.Cos(rayA), rdy = MathF.Sin(rayA);

            CollectHits(_roo, camX, camY, rdx, rdy, sc);

            int yTop = 0, yBot = H - 1;
            RooSector cur = camSector;
            bool closed = false;

            foreach (Hit h in sc.Hits)
            {
                if (yTop > yBot) break;
                float perp = MathF.Max(1f, h.Dist * cosFix);

                RooSector near = M59Geo.Sector(_roo, h.Right ? h.Wall.RightSectorNum : h.Wall.LeftSectorNum);
                RooSector far  = M59Geo.Sector(_roo, h.Right ? h.Wall.LeftSectorNum  : h.Wall.RightSectorNum);
                RooSideDef side = M59Geo.Side(_roo, h.Right ? h.Wall.RightSideNum : h.Wall.LeftSideNum);
                // The space this column's ray is travelling through is what
                // the visible floor and ceiling belong to, and the walk has
                // been tracking that all along - it starts at the camera's
                // own sector, which comes from the BSP, and follows portals.
                // Deriving it instead from which side of the wall the camera
                // is on agrees 110397 times out of 110871 and is a geometric
                // test that gives way at boundaries and coincident walls.
                // Trust the walk; keep the side test for which sidedef's
                // textures to use.
                if (cur != null) near = cur;
                else if (near == null) near = cur;

                // Heights at the point this column's ray meets the wall, so
                // a sloped sector's floor and ceiling meet the wall where
                // they actually do. A column renderer gets this almost free:
                // every column already has its own hit point.
                float hx = camX + rdx * h.Dist, hy = camY + rdy * h.Dist;
                float nf = M59Geo.FloorXY(near, hx, hy), nc = M59Geo.CeilingXY(near, hx, hy);
                int ceilY  = ScreenY(nc, camZ, horizon, proj, perp);
                int floorY = ScreenY(nf, camZ, horizon, proj, perp);

                FillFlat(px, W, H, sx, yTop, Math.Min(yBot, ceilY - 1), true,
                         near, camX, camY, camZ, horizon, proj, angle, rayA, _tex,
                         NoFlats, NoSample, Time, LeafAnchoredFlats ? _anchors : null,
                         Brightness, Sky);
                FillFlat(px, W, H, sx, Math.Max(yTop, floorY + 1), yBot, false,
                         near, camX, camY, camZ, horizon, proj, angle, rayA, _tex,
                         NoFlats, NoSample, Time, LeafAnchoredFlats ? _anchors : null,
                         Brightness, Sky);

                yTop = Math.Max(yTop, ceilY);
                yBot = Math.Min(yBot, floorY);
                // Closed because the floor and the ceiling met on screen.
                // The depth has to be recorded even so: it is what stops
                // a sprite being drawn through the geometry, and a
                // column that closed without a wall left it at
                // float.MaxValue, so every creature behind that point
                // passed the test. A rat on the far side of a rise came
                // through the hill.
                if (yTop > yBot) { _depth[sx] = perp; closed = true; break; }

                // Which end of the wall the texture starts from depends on
                // which side of it you are standing on. `GetVertexData`
                // takes P1 as the u origin for the right side
                // (RooWall.cs:1104 `RI.P0.X = P1.X; RI.P3.X = P2.X;`) and
                // P2 for the left (RooWall.cs:1156, the same two lines with
                // the points exchanged), then assigns u identically for
                // both. `Split` says so out loud while fixing up offsets:
                // "Do this backwards, because client exchanges vertices of
                // negative walls" (RooWall.cs:1441). The hit's Along is
                // measured from P1 either way, so the left side counts back
                // from the far end. Without this every left-facing wall in
                // the game wore its texture mirrored.
                //
                // WF_BACKWARDS, "draw bitmap right/left reversed", is the
                // same question asked again. The library bakes the offset
                // into u1 and then swaps the two ends (RooWall.cs:1289),
                // which works out to measuring from the OTHER end with the
                // offset still added the same way - not to negating the
                // distance, which is what this renderer did and which
                // slides the texture by a whole wall length. So the two
                // conditions simply combine: the texture starts at P1 when
                // exactly one of "left side" and "backwards" holds.
                bool backwards = HonourBackwards && side != null && side.Flags.IsBackwards;
                bool fromP1 = HonourSides ? (h.Right ^ backwards) : !backwards;
                float along = fromP1 ? h.Along : h.Len - h.Along;
                // The texture's own size and shrink set the scale, so the
                // UVs cannot be worked out until the texture is known - see
                // DrawWall. What travels is the distance along the wall and
                // the sidedef's offsets.
                // Animated wall textures are a different frame of the same
                // file, and the library keeps the group on the sidedef.
                // The animation frame belongs to the MIDDLE texture and
                // nothing else. `RooSideDef.OnAnimationPropertyChanged`
                // (:266-270) calls SetMiddleTexture alone when the group
                // moves; the upper and lower keep whatever frame they
                // were last given by a texture change or an ApplyChange.
                // Feeding the group to all three made the strip above
                // and below an animating door flicker where the game
                // holds it still.
                ushort texGroup = side?.Animation != null ? side.Animation.CurrentGroup : (ushort)1;
                const ushort EdgeGroup = 1;
                int xOff = h.Right ? h.Wall.RightXOffset : h.Wall.LeftXOffset;
                int yOff = h.Right ? h.Wall.RightYOffset : h.Wall.LeftYOffset;
                float fog = Falloff(perp) * Brightness;
                // World units one screen pixel spans on this wall. Turning
                // that into texels needs the texture's shrink, so DrawWall
                // finishes it - the old constant here quietly assumed
                // shrink 2, which is merely the commonest.
                float tpp = perp / proj;

                if (far == null)
                {
                    DrawWall(px, W, H, sx, yTop, yBot, ceilY, floorY, nf, nc,
                             side != null ? _tex.Get(side.MiddleTexture, texGroup) : null,
                             along, xOff, yOff, side != null && side.Flags.IsNormalTopDown,
                             fog, tpp, false, null, 0f, 0,
                             HonourNoVTile && side != null && side.Flags.IsNoVTile,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time, Sky, rayA, cosFix, horizon, proj);
                    _depth[sx] = perp;
                    closed = true;
                    break;
                }

                float ff = M59Geo.FloorXY(far, hx, hy), fc = M59Geo.CeilingXY(far, hx, hy);

                if (fc < nc)
                {
                    int farCeilY = ScreenY(fc, camZ, horizon, proj, perp);
                    // No upper texture means no upper part: the gap
                    // shows the room beyond, which the rest of this
                    // column walk will draw. Filling it here painted a
                    // band over the view through a doorway.
                    Tex upper = side != null ? _tex.Get(side.UpperTexture, EdgeGroup) : null;
                    if (upper != null)
                    DrawWall(px, W, H, sx, yTop, Math.Min(yBot, farCeilY - 1), ceilY, farCeilY, fc, nc,
                             upper,
                             along, xOff, yOff, side == null || !side.Flags.IsAboveBottomUp,
                             fog, tpp, false, null, 0f, 0, false,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time);
                    // Nobody draws the band when there is no texture for
                    // it, and yTop moves past it either way - so those
                    // rows kept whatever the last frame left there and
                    // smeared as you turned. The reference has nothing
                    // to draw there either (`ControllerRoom.cpp:690`);
                    // behind it is the skybox, which is what shows here.
                    else for (int y = Math.Max(0, yTop);
                              y < Math.Min(H, Math.Min(yBot + 1, farCeilY)); y++)
                        px[y * W + sx] = SkyAt(Sky, Tex.Void, rayA, cosFix, y, horizon, proj);
                    yTop = Math.Max(yTop, farCeilY);
                }
                if (ff > nf)
                {
                    int farFloorY = ScreenY(ff, camZ, horizon, proj, perp);
                    Tex lower = side != null ? _tex.Get(side.LowerTexture, EdgeGroup) : null;
                    if (lower != null)
                    DrawWall(px, W, H, sx, Math.Max(yTop, farFloorY), yBot, farFloorY, floorY, nf, ff,
                             lower,
                             along, xOff, yOff, side != null && side.Flags.IsBelowTopDown,
                             fog, tpp, false, null, 0f, 0, false,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time);
                    else for (int y = Math.Max(0, Math.Max(yTop, farFloorY));
                              y < Math.Min(H, yBot + 1); y++)
                        px[y * W + sx] = SkyAt(Sky, Tex.Void, rayA, cosFix, y, horizon, proj);
                    yBot = Math.Min(yBot, farFloorY);
                }

                // The middle span of a two-sided wall is the OPENING, not
                // the near sector's whole wall. `RooWall.GetVertexData`
                // bounds it by z1..z2 (RooWall.cs:1129-1132), and
                // `CalculateWallSideHeights` fills those with the HIGHER of
                // the two floors and the LOWER of the two ceilings
                // (RooWall.cs:719-720, :800-801) - the see-through aperture,
                // exactly. Drawing it across the near span instead stretched
                // a doorway's grate from this room's floor to this room's
                // ceiling, and hid the fact that the upper and lower parts
                // above and below it were never drawn at all.
                //
                // Which is why the upper and lower parts are drawn first now:
                // they leave yTop and yBot sitting on the opening.
                float midTopH = MathF.Min(nc, fc), midBotH = MathF.Max(nf, ff);
                int midTopY = ScreenY(midTopH, camZ, horizon, proj, perp);
                int midBotY = ScreenY(midBotH, camZ, horizon, proj, perp);

                // A two-sided wall carrying a middle texture is either a
                // solid wall stored with sectors on both sides, or a grate,
                // railing or doorway you are meant to see through. The room
                // says which: WF_TRANSPARENT means "has some transparency"
                // and WF_NOLOOKTHROUGH means "even so, you cannot see
                // past it". 33788 of the 40586 such walls across all 362
                // rooms are the see-through kind, and every one of them
                // used to be a solid wall.
                if (side != null && side.MiddleTexture != 0)
                {
                    bool seeThrough = SeeThroughWalls
                                   && side.Flags.IsTransparent && !side.Flags.IsNoLookThrough;

                    if (!seeThrough)
                    {
                        Tex mid = _tex.Get(side.MiddleTexture, texGroup);
                        if (mid != null)
                        {
                            DrawWall(px, W, H, sx,
                                     Math.Max(yTop, midTopY), Math.Min(yBot, midBotY),
                                     midTopY, midBotY, midBotH, midTopH, mid,
                                     along, xOff, yOff, side.Flags.IsNormalTopDown, fog, tpp,
                                     false, null, 0f, 0,
                                     HonourNoVTile && side.Flags.IsNoVTile,
                                     side.Flags.ScrollSpeed, side.Flags.ScrollDirection, Time,
                                     Sky, rayA, cosFix, horizon, proj);
                            _depth[sx] = perp;
                            closed = true;
                            break;
                        }
                    }
                    else
                    {
                        Tex mid = _tex.GetMasked(side.MiddleTexture, texGroup);
                        if (mid != null)
                            sc.Masked.Add(new Masked {
                                Sx = sx, Depth = perp,
                                Y0 = Math.Max(yTop, midTopY), Y1 = Math.Min(yBot, midBotY),
                                SpanTopY = midTopY, SpanBotY = midBotY,
                                SpanTopH = midTopH, SpanBotH = midBotH, Along = along, XOff = xOff,
                                YOff = yOff, TopDown = side.Flags.IsNormalTopDown,
                                NoVTile = HonourNoVTile && side.Flags.IsNoVTile,
                                ScrollSpeed = side.Flags.ScrollSpeed,
                                ScrollDir = side.Flags.ScrollDirection,
                                Fog = fog, Tpp = tpp, T = mid });
                    }
                }

                cur = far;
            }

            if (closed) sc.SolidCols++;
            else
            {
                // The walk ran out of walls. Nothing is nearer than
                // this, so the depth is what the column reached rather
                // than the untouched float.MaxValue - the same reason as
                // above.
                _depth[sx] = _depth[sx] == float.MaxValue ? 1e9f : _depth[sx];
                for (int y = yTop; y <= yBot && y < H; y++) if (y >= 0)
                    px[y * W + sx] = SkyAt(Sky, Tex.Void, rayA, cosFix, y, horizon, proj);
            }
        }
    }

    /// <summary>
    /// Draws the see-through walls the column walk collected, after the
    /// sprites, far to near.
    ///
    /// It has to be after the sprites and it has to know where they are:
    /// a grate is drawn over whatever is behind it, and a creature standing
    /// in front of one is not behind it. So the sprite pass records a depth
    /// per pixel and a grate skips any pixel a nearer sprite already owns.
    /// Doing this per column at the end of the walk, before sprites, drew
    /// every grate over every creature regardless of which was nearer.
    /// </summary>
    void DrawMasked(uint[] px, int W, int H, int bands)
    {
        int total = 0;
        for (int b = 0; b < bands; b++) total += _scratch[b].Masked.Count;
        if (total == 0) return;

        _order.Clear();
        if (_order.Capacity < total) _order.Capacity = total;
        for (int b = 0; b < bands; b++) _order.AddRange(_scratch[b].Masked);
        // Far first, so a near grate covers a far one. Ties broken on the
        // column to keep the order independent of how the bands were split.
        _order.Sort((p, q) =>
        {
            int c = q.Depth.CompareTo(p.Depth);
            return c != 0 ? c : p.Sx.CompareTo(q.Sx);
        });

        bool haveSprites = _spriteDepth != null && Sprites.Count > 0;
        foreach (Masked m in _order)
        {
            DrawWall(px, W, H, m.Sx, m.Y0, m.Y1, m.SpanTopY, m.SpanBotY,
                     m.SpanBotH, m.SpanTopH, m.T, m.Along, m.XOff, m.YOff, m.TopDown,
                     m.Fog, m.Tpp, true, haveSprites ? _spriteDepth : null, m.Depth, W,
                     m.NoVTile, m.ScrollSpeed, m.ScrollDir, Time);
        }
    }

    /// <summary>
    /// Draws the sprite list as camera-facing billboards, back to front,
    /// testing each column against the wall depth recorded during the main
    /// pass. Transparent texels are skipped rather than blended, which is
    /// what the palette's index 254 means.
    /// </summary>
    void DrawSprites(uint[] px, int W, int H, float camX, float camY, float camZ,
                     float angle, float proj, float horizon)
    {
        if (Sprites.Count == 0) return;

        float ca = MathF.Cos(-angle), sa = MathF.Sin(-angle);
        var order = new List<(float depth, Sprite s, float lateral)>(Sprites.Count);

        foreach (Sprite sp in Sprites)
        {
            if (sp.Bgf == null && sp.Texture == null) continue;
            float rx = sp.X - camX, ry = sp.Y - camY;
            // Into camera space: +depth is straight ahead.
            float depth = rx * ca - ry * sa;
            float lateral = rx * sa + ry * ca;
            if (depth < 32f) continue;                       // behind or on top of us
            order.Add((depth, sp, lateral));
        }
        order.Sort((a, b) => b.depth.CompareTo(a.depth));    // far first

        foreach (var (depth, sp, lateral) in order)
        {
            if (!Place(sp, depth, lateral, W, camX, camY, camZ, proj, horizon, out Placed p))
                continue;

            int x0 = (int)MathF.Floor(p.Left);
            int x1 = (int)MathF.Ceiling(p.Left + p.WPx);
            if (x1 < 0 || x0 >= W) continue;

            for (int sx = Math.Max(0, x0); sx <= Math.Min(W - 1, x1); sx++)
            {
                if (depth >= _depth[sx]) continue;           // behind a wall
                int tx = TexelX(p, sx);
                if (tx < 0) continue;

                int yA = Math.Max(0, (int)MathF.Floor(p.YTop));
                int yB = Math.Min(H - 1, (int)MathF.Ceiling(p.YBot));
                for (int y = yA; y <= yB; y++)
                {
                    int ty = TexelY(p, y);
                    if (ty < 0) continue;
                    uint c = p.T.P[ty * p.T.W + tx];
                    if ((c >> 24) == 0) continue;            // transparent texel
                    uint lit = Material(Shade(c | 0xFF000000u, p.Fog), sp);
                    // Opacity one is the ordinary case and must cost
                    // nothing; anything less is blended over whatever the
                    // walls and floor already put there.
                    px[y * W + sx] = sp.Opacity >= 1f
                        ? lit
                        : Blend(px[y * W + sx], lit, sp.Opacity);
                    if (_spriteDepth != null) _spriteDepth[y * W + sx] = depth;
                }
            }
        }
    }

    /// <summary>
    /// Works out where a sprite lands on screen. Shared by drawing and by
    /// picking so the two cannot disagree about what is under a finger.
    /// </summary>
    /// <summary>
    /// How tall a sprite actually stands, in world units - its own
    /// height where it has one, otherwise the art's, which is the
    /// frame's pixels over the file's shrink factor. The name tag asks
    /// for this: the game puts a name at the top of the drawn image
    /// (`RemoteNode.cpp` UpdateNamePosition), so it has to know how
    /// tall the image is.
    /// </summary>
    public float WorldHeight(Sprite sp)
    {
        if (sp == null) return 0f;
        if (sp.Height > 0f) return sp.Height;
        Tex t = sp.Texture;
        if (t == null && sp.Bgf != null) t = SpriteFrames.Get(sp.Bgf, sp.Group, sp.AngleUnits);
        return t == null ? 0f : t.H / (float)Math.Max(1, t.Shrink) * M59Geo.HeightToXY;
    }

    /// <summary>The sprite drawn for an object, or null.</summary>
    public Sprite SpriteFor(object tag)
    {
        if (tag == null) return null;
        foreach (Sprite sp in Sprites) if (ReferenceEquals(sp.Tag, tag)) return sp;
        return null;
    }

    bool Place(Sprite sp, float depth, float lateral, int W,
               float camX, float camY, float camZ, float proj, float horizon,
               out Placed p)
    {
        p = default;
        Tex t = sp.Texture;
        if (sp.Bgf != null)
        {
            // Which side of the object we are looking at: its facing,
            // minus the direction from it to us.
            float toViewer = MathF.Atan2(camY - sp.Y, camX - sp.X);
            int viewerUnits = (int)(toViewer / (2f * MathF.PI) * GeometryConstants.MAXANGLE);
            int rel = (sp.AngleUnits - viewerUnits) % GeometryConstants.MAXANGLE;
            if (rel < 0) rel += GeometryConstants.MAXANGLE;
            t = SpriteFrames.Get(sp.Bgf, sp.Group, (ushort)rel);
        }
        if (t == null) return false;

        float scale = proj / depth;
        float cxs = W * 0.5f + lateral * scale;
        float worldH = sp.Height > 0f
            ? sp.Height
            : t.H / (float)Math.Max(1, t.Shrink) * M59Geo.HeightToXY;
        float hPx = worldH * scale;
        if (hPx < 1f) return false;
        float wPx = sp.Width > 0f ? sp.Width * scale : hPx * t.W / MathF.Max(1, t.H);
        float anchor = horizon - (sp.BaseZ - camZ) * scale;
        float yTop = sp.Hanging ? anchor : anchor - hPx;

        p = new Placed {
            S = sp, T = t, Depth = depth,
            Left = cxs - wPx * 0.5f, WPx = wPx, HPx = hPx,
            YTop = yTop, YBot = yTop + hPx,
            Fog = Falloff(depth) * Brightness,
        };
        return true;
    }

    int _lastW, _lastH;
    float _lastCamX, _lastCamY, _lastCamZ, _lastAngle, _lastProj, _lastHorizon;

    /// <summary>
    /// Where a world point lands on the last frame, in that frame's pixel
    /// buffer. False when it is behind the camera, off the sides, or
    /// behind a wall - the last of those from the same per-column depth
    /// the sprites are clipped against, so a name does not hang in front
    /// of the wall its owner is standing behind.
    /// </summary>
    public bool Project(float x, float y, float z, out float sx, out float sy)
    {
        sx = sy = 0f;
        if (_lastW <= 0) return false;

        float dx = x - _lastCamX, dy = y - _lastCamY;
        float c = MathF.Cos(-_lastAngle), s = MathF.Sin(-_lastAngle);
        float depth = dx * c - dy * s;          // along the view direction
        float lateral = dx * s + dy * c;

        if (depth < 1f) return false;

        float scale = _lastProj / depth;
        sx = _lastW * 0.5f + lateral * scale;
        sy = _lastHorizon - (z - _lastCamZ) * scale;

        if (sx < 0f || sx >= _lastW) return false;

        int col = (int)sx;
        if (col >= 0 && col < _depth.Length && depth > _depth[col]) return false;

        return true;
    }

    /// <summary>Texture column under a screen column, or -1 if outside.</summary>
    static int TexelX(in Placed p, int sx)
    {
        float u = (sx + 0.5f - p.Left) / MathF.Max(1f, p.WPx);
        if (u < 0f || u >= 1f) return -1;
        int tx = (int)(u * p.T.W);
        return tx < 0 ? 0 : (tx >= p.T.W ? p.T.W - 1 : tx);
    }

    /// <summary>Texture row under a screen row, or -1 if outside.</summary>
    static int TexelY(in Placed p, int y)
    {
        float v = (y + 0.5f - p.YTop) / MathF.Max(1f, p.HPx);
        if (v < 0f || v >= 1f) return -1;
        int ty = (int)(v * p.T.H);
        return ty < 0 ? 0 : (ty >= p.T.H ? p.T.H - 1 : ty);
    }

    /// <summary>
    /// The sprite under a screen pixel, nearest first, or null. Only counts
    /// a hit on an opaque texel - tapping through the gap under a rat's
    /// belly should reach whatever is behind it - and respects the wall
    /// depth from the last frame, so you cannot target through a wall.
    ///
    /// Call after Render with the same camera: it reads the depth buffer
    /// that Render filled.
    /// </summary>
    public Sprite Pick(int px_, int py_, int W, int H,
                       float camX, float camY, float camZ, float angle)
    {
        List<Sprite> all = PickAll(px_, py_, W, H, camX, camY, camZ, angle);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// Every sprite under a screen pixel, nearest first.
    ///
    /// One tap can land on several things standing in a line, and the
    /// game lets you walk back through them: `ControllerInput.cpp:153-215`
    /// collects every ray hit, sorts them by distance, and hands the
    /// whole list to `DataController.ClickTarget`, which takes the first
    /// one you have not already picked (DataController.cs:1403-1441).
    /// Tapping twice therefore reaches the creature standing behind the
    /// one in front. Returning only the nearest, as this did, made the
    /// thing at the back unselectable.
    ///
    /// The same rules as before decide what counts as a hit: an opaque
    /// texel only, so tapping the gap under a rat's belly reaches what
    /// is behind it, and never past the wall depth the last frame left.
    /// </summary>
    public List<Sprite> PickAll(int px_, int py_, int W, int H,
                                float camX, float camY, float camZ, float angle)
    {
        var hits = new List<Sprite>();
        if (Sprites.Count == 0 || px_ < 0 || px_ >= W || py_ < 0 || py_ >= H) return hits;
        if (_depth.Length < W) return hits;

        float proj = Projection(W, H);
        float horizon = Horizon(H, proj);      // Pick must agree with Render
        float ca = MathF.Cos(-angle), sa = MathF.Sin(-angle);

        var depths = new List<float>();

        foreach (Sprite sp in Sprites)
        {
            if (sp.Bgf == null && sp.Texture == null) continue;
            float rx = sp.X - camX, ry = sp.Y - camY;
            float depth = rx * ca - ry * sa;
            if (depth < 32f || depth >= _depth[px_]) continue;
            float lateral = rx * sa + ry * ca;

            if (!Place(sp, depth, lateral, W, camX, camY, camZ, proj, horizon, out Placed p))
                continue;

            int tx = TexelX(p, px_);
            if (tx < 0) continue;
            int ty = TexelY(p, py_);
            if (ty < 0) continue;
            if ((p.T.P[ty * p.T.W + tx] >> 24) == 0) continue;   // saw straight through

            hits.Add(sp);
            depths.Add(depth);
        }

        // Nearest first, which is the order ClickTarget expects.
        for (int i = 1; i < hits.Count; i++)
            for (int j = i; j > 0 && depths[j] < depths[j - 1]; j--)
            {
                (depths[j], depths[j - 1]) = (depths[j - 1], depths[j]);
                (hits[j], hits[j - 1]) = (hits[j - 1], hits[j]);
            }
        return hits;
    }

    /// <summary>
    /// Where a screen row's ray meets a sloped plane, as a horizontal
    /// distance from the camera.
    ///
    /// A sloped floor is Ax + By + Cz + D = 0. Down a screen column the ray
    /// through row y drops by <paramref name="sSlope"/> per unit of
    /// perpendicular distance, so the point at horizontal distance d is
    /// (camX + rdx*d, camY + rdy*d, camZ - sSlope*cosFix*d). Substituting
    /// and solving for d costs one division, the same as the flat case.
    ///
    /// Public because the closed form is the part worth checking, and
    /// Net8RenderCheck checks it against a bisection.
    /// </summary>
    public static bool SolveSlope(RooSectorSlopeInfo slope,
                                  float camX, float camY, float camZ,
                                  float rdx, float rdy, float sSlope, float cosFix,
                                  out float d)
    {
        d = 0f;
        float num = -(float)(slope.A * camX + slope.B * camY + slope.C * camZ + slope.D);
        float den = (float)(slope.A * rdx + slope.B * rdy) - (float)slope.C * sSlope * cosFix;
        if (MathF.Abs(den) < 1e-6f) return false;
        d = num / den;
        return d > 0f;
    }

    static int ScreenY(float worldH, float camZ, float horizon, float proj, float perp)
        => (int)MathF.Round(horizon - (worldH - camZ) * proj / perp);

    static void DrawWall(uint[] px, int W, int H, int sx, int y0, int y1,
                         int spanTopY, int spanBotY, float spanBotH, float spanTopH,
                         Tex t, float along, int xOffset, int yOffset, bool topDown,
                         float fog, float texelsPerPixel = 1f,
                         bool masked = false,
                         float[] spriteDepth = null, float depth = 0f, int stride = 0,
                         bool noVTile = false,
                         TextureScrollSpeed scrollSpeed = TextureScrollSpeed.NONE,
                         TextureScrollDirection scrollDir = TextureScrollDirection.N,
                         float time = 0f,
                         M59Sky sky = null, float rayA = 0f, float cosFix = 1f,
                         float horizon = 0f, float proj = 1f)
    {
        if (y0 < 0) y0 = 0;
        if (y1 > H - 1) y1 = H - 1;
        float span = Math.Max(1f, spanBotY - spanTopY);

        // Meridian stores room textures with the axes swapped relative to
        // how they decode as an image: the texture's X axis runs UP the
        // wall and its Y axis runs ALONG it. The library's own UV code says
        // the same thing arithmetically - the along-wall coordinate divides
        // by the texture's HEIGHT and the up-wall one by its WIDTH.
        //
        // Scale is the texture's shrink over its size, not a constant: a
        // 512x512 at shrink 4 covers twice the wall a 128x128 at shrink 2
        // does. This renderer used a flat 1/1024, which is exactly right
        // when size/shrink is 64 and wrong for 903 of 8357 wall middles.
        // Ported from RooWall.GetVertexData, itself a port of the game's
        // d3drender.c.
        float u = 0f, vBase = 0f, vPerHeight = 0f, tpp = texelsPerPixel;
        if (t != null)
        {
            float shrink = t.Shrink;
            // Texels per screen pixel: the caller hands over world units per
            // pixel, which only becomes texels once the texture's shrink is
            // known. Hoisted out of the row loop - it does not vary down a
            // column and it was costing a multiply and a divide per pixel.
            tpp = texelsPerPixel * shrink / M59Geo.HeightToXY;
            u = (along / M59Geo.HeightToXY + xOffset) * shrink / t.H;

            float perWorld = shrink / (t.W * M59Geo.HeightToXY);
            float yOff = yOffset * shrink / t.W;
            if (topDown)
            {
                // Origin at the top of this wall part, texture running down.
                vBase = spanTopH * perWorld - yOff;
                vPerHeight = -perWorld;
            }
            else
            {
                // Origin at the bottom, texture running up.
                vBase = 1f - yOff + spanBotH * perWorld;
                vPerHeight = -perWorld;
            }

            // A moving wall. The rate is whole textures per second, and the
            // axes are the texture's own: its X runs up the wall (v here)
            // and its Y along it (u).
            if (time != 0f && scrollSpeed != TextureScrollSpeed.NONE)
            {
                M59Geo.WallScroll(scrollSpeed, scrollDir, t.W, t.H,
                                  out float sxr, out float syr);
                vBase += sxr * time;
                u     += syr * time;
            }
        }

        for (int y = y0; y <= y1; y++)
        {
            uint c;
            // A part with no texture is not drawn at all - the library
            // clears the texture and the material together
            // (`RooSideDef.cs:472`) and the Ogre client returns from
            // CreateSidePart before making anything
            // (`ControllerRoom.cpp:686`). So what is behind the missing
            // quad shows through, which is the sky. A grey fill instead
            // made every missing texture look like a wall that is there.
            if (t == null) c = SkyAt(sky, Tex.Void, rayA, cosFix, y, horizon, proj);
            else
            {
                float f = (y - spanTopY) / span;                 // 0 at top of span
                float worldH = spanTopH + f * (spanBotH - spanTopH);
                float v = vBase + worldH * vPerHeight;
                // WF_NO_VTILE: the texture does not repeat up the wall. The
                // library gets there by clipping the geometry so the UV
                // never goes negative; a column renderer just declines to
                // draw above the first tile. Middle parts only - the game's
                // own comment says it makes strange holes anywhere else.
                //
                // Only on walls actually drawn see-through. The flag's
                // comment says it "must be transparent", but 235 of the
                // 2301 sidedefs carrying it are not flagged so, and
                // clipping a solid wall would leave a hole you can see the
                // void through. A tiled texture is the better of the two
                // wrongs.
                if (noVTile && (v < 0f || v >= 1f))
                {
                    // One tile, and nothing above or below it. The
                    // library gets there by clipping the quad so the UV
                    // never leaves [0,1] (`RooWall.cs:1304`); a column
                    // renderer just declines the texels outside it.
                    //
                    // A see-through wall lets the column carry on and
                    // shows whatever is behind. A solid one has nothing
                    // behind it: the library's shortened quad ends, and
                    // what the reference shows past the end of the
                    // geometry is the skybox - so that, rather than the
                    // previous frame's pixels.
                    if (!masked) px[y * W + sx] = SkyAt(sky, 0xFF000000u, rayA, cosFix, y, horizon, proj);
                    continue;
                }
                uint texel = masked ? t.Sample(v, u) : t.Sample(v, u, tpp);
                // A see-through wall keeps the palette's transparent index,
                // which carries alpha 0; those texels are skipped, not
                // blended, the same as sprites.
                if (masked && (texel >> 24) == 0) continue;
                // A sprite nearer than this wall keeps its pixel: a creature
                // standing in front of a grate is not behind it.
                if (spriteDepth != null)
                {
                    float sd = spriteDepth[y * stride + sx];
                    if (sd > 0f && sd < depth) continue;
                }
                c = Shade(texel | 0xFF000000u, fog);
            }
            px[y * W + sx] = c;
        }
    }
    /// <summary>
    /// The texture coordinate of a point on a sloped floor or ceiling,
    /// following `RooSubSector.UpdateVertexUV` exactly.
    ///
    /// The slope carries its own texture frame: an origin P0 at
    /// (X0, Y0) on the plane, and two axis endpoints P1 and P2 one
    /// FINENESS away along the stored texture angle and its
    /// perpendicular. A point's two coordinates are its perpendicular
    /// distances from the two axis lines, measured in the plane - so the
    /// texture lies flat on the slope and turns with it, rather than
    /// being projected down from above.
    ///
    /// The signs, the halved sector offsets and the 1/(64&lt;&lt;4) scale
    /// are the library's, quirks included: the sloped branch halves
    /// TextureX and TextureY and adds them, where the level branch uses
    /// them whole and subtracts. RooSubSector.cs:448 against :492.
    /// </summary>
    public static void SlopeUV(RooSectorSlopeInfo sl, float wx, float wy, float camZ,
                               float texOffX, float texOffY, out float u, out float v)
    {
        float pz = M59Geo.Plane(sl, wx, wy);
        float p0x = (float)sl.P0.X, p0y = (float)sl.P0.Y, p0z = (float)sl.P0.Z;
        float d1x = wx - p0x, d1y = wy - p0y, d1z = pz - p0z;
        float d2x = (float)sl.P1.X - p0x, d2y = (float)sl.P1.Y - p0y, d2z = (float)sl.P1.Z - p0z;
        float d3x = (float)sl.P2.X - p0x, d3y = (float)sl.P2.Y - p0y, d3z = (float)sl.P2.Z - p0z;

        float du = Perp(d1x, d1y, d1z, d2x, d2y, d2z);
        float dv = Perp(d1x, d1y, d1z, d3x, d3y, d3z);

        // The library adds the sector offsets halved, then flips the sign
        // of the whole thing - offset included - on the far side of each
        // axis. Order matters; doing it the other way round moves the
        // texture by twice the offset across the axis line.
        du += texOffY * 0.5f;
        dv += texOffX * 0.5f;

        // Which side of each axis the point is on, decided in plan view
        // from the normalised 2D vectors, as the library does.
        float vlen = MathF.Sqrt(d1x * d1x + d1y * d1y); if (vlen == 0f) vlen = 1f;
        float vx = d1x / vlen, vy = d1y / vlen;
        float ulen = MathF.Sqrt(d2x * d2x + d2y * d2y); if (ulen == 0f) ulen = 1f;
        float vlen2 = MathF.Sqrt(d3x * d3x + d3y * d3y); if (vlen2 == 0f) vlen2 = 1f;
        if (vx * (d2x / ulen) + vy * (d2y / ulen) <= 0f) dv = -dv;
        if (vx * (d3x / vlen2) + vy * (d3y / vlen2) > 0f) du = -du;

        u = du / M59Geo.Fineness;
        v = dv / M59Geo.Fineness;
    }

    /// <summary>Distance from a point to the line through the origin along an axis.</summary>
    static float Perp(float px, float py, float pz, float ax, float ay, float az)
    {
        float den = ax * ax + ay * ay + az * az;
        if (den == 0f) den = 1f;
        float k = (px * ax + py * ay + pz * az) / den;
        float ex = px - k * ax, ey = py - k * ay, ez = pz - k * az;
        return MathF.Sqrt(ex * ex + ey * ey + ez * ez);
    }

    static void FillFlat(uint[] px, int W, int H, int sx, int y0, int y1, bool ceiling,
                         RooSector sec, float camX, float camY, float camZ,
                         float horizon, float proj, float angle, float rayA, TexCache tc,
                         bool skip, bool noSample, float time, FlatAnchors anchors,
                         float bright, M59Sky sky)
    {
        if (sec == null || skip) return;
        if (y0 < 0) y0 = 0;
        if (y1 > H - 1) y1 = H - 1;
        if (y0 > y1) return;

        RooSectorSlopeInfo slope = ceiling ? sec.SlopeInfoCeiling : sec.SlopeInfoFloor;
        float planeH = ceiling ? M59Geo.CeilingXY(sec) : M59Geo.FloorXY(sec);
        ushort texNum = ceiling ? sec.CeilingTexture : sec.FloorTexture;
        Tex t = tc.Get(texNum);

        // Whether this surface is a liquid, which the room file does not
        // say - see M59Water. Its own scroll speed becomes the wave's
        // drift rather than a scroll of the picture: CreateMaterialWater
        // never calls setScrollAnimation, it sets waveSpeed to
        // 0.3 * -scroll (Util.h:766-767), and the sector bitmap's UVs
        // never move at all.
        bool liquid = Water && WaterNoise.Ready() && M59Water.Is(texNum);
        float waveX = 0f, waveY = 0f;
        uint flat = ceiling ? 0xFF0B0B10u : 0xFF141418u;
        float texOffX = sec.TextureX * M59Geo.HeightToXY;
        float texOffY = sec.TextureY * M59Geo.HeightToXY;

        // Scrolling water and lava. The rate is in whole textures per
        // second, so it adds straight onto the sampled coordinates.
        float scrollU = 0f, scrollV = 0f;
        if (t != null && (ceiling ? sec.Flags.IsScrollCeiling : sec.Flags.IsScrollFloor))
        {
            M59Geo.SectorScroll(sec.Flags.ScrollSpeed, sec.Flags.ScrollDirection,
                                t.W, t.H, out float sxr, out float syr);
            if (liquid) { waveX = -0.3f * sxr; waveY = -0.3f * syr; }
            else if (time != 0f) { scrollU = sxr * time; scrollV = syr * time; }
        }
        // The shader's clock is time_0_x with a period of 100 seconds
        // (general.material:96), so it is a sawtooth, not a ramp.
        float waterTime = time - 100f * MathF.Floor(time / 100f);
        float cosFix = MathF.Cos(rayA - angle);
        float rdx = MathF.Cos(rayA), rdy = MathF.Sin(rayA);

        // A sloped plane is Ax + By + Cz + D = 0. Walking a screen column,
        // the ray through row y drops by s = (y - horizon)/proj per unit of
        // perpendicular distance, so the point at horizontal distance d is
        // (camX + rdx*d, camY + rdy*d, camZ - s*cosFix*d). Substituting and
        // solving for d costs one division, the same as the flat case.
        float cosFixMax = MathF.Max(0.2f, cosFix);

        for (int y = y0; y <= y1; y++)
        {
            // Texture 0 leaves the sector with no resource and no
            // material (RooSector.cs:663-691) and CreateSectorPart
            // builds nothing for it (ControllerRoom.cpp:789-790), so
            // the skybox is what fills the hole. Floors as well as
            // ceilings: the early-out is the same for both.
            if (t == null)
            { px[y * W + sx] = SkyAt(sky, flat, rayA, cosFix, y, horizon, proj); continue; }
            float dy = y - horizon;
            if (MathF.Abs(dy) < 0.5f) { px[y * W + sx] = flat; continue; }

            float straight, d;
            if (slope != null)
            {
                if (!SolveSlope(slope, camX, camY, camZ, rdx, rdy, dy / proj, cosFixMax, out d))
                { px[y * W + sx] = flat; continue; }
                straight = d * cosFixMax;
            }
            else
            {
                straight = MathF.Abs((camZ - planeH) * proj / dy);
                d = straight / cosFixMax;
            }
            float wx = camX + rdx * d, wy = camY + rdy * d;

            if (liquid)
            {
                float surfaceZ = slope != null ? M59Geo.Plane(slope, wx, wy) : planeH;
                px[y * W + sx] = M59Water.Shade(t, wx, wy, surfaceZ,
                                                camX, camY, camZ,
                                                waveX, waveY, waterTime, bright, ceiling);
                continue;
            }

            float fog = Falloff(straight) * bright;
            // How much world space one screen pixel covers here, in texels.
            // Rows near the horizon cover enormous distances, which is what
            // made ceilings streak before mipmapping.
            float texelsPerPixel = (straight / MathF.Max(1f, MathF.Abs(dy))) * t.W / M59Geo.Fineness;
            // Same axis swap as walls - grd02011 is a floor of tall stone
            // slabs and rendered as wide ones until y,x were used. The
            // library says the same thing: RooSubSector.UpdateVertexUV
            // takes the texture's X from the world Y and vice versa.
            //
            // The per-sector offset comes from there too, and was ignored:
            // 1420 of the 30806 sectors carry one, and their floors and
            // ceilings were sliding by up to a texture's width.
            // The leaf's own corner, when it has one. See FlatAnchors:
            // the library measures from there, not from the origin, and
            // for a leaf sitting in positive coordinates the two are the
            // same thing.
            float anchorX = 0f, anchorY = 0f;
            if (anchors != null && !anchors.Empty)
                anchors.TryAnchor(wx, wy, out anchorX, out anchorY);

            float su, sv;
            if (slope != null)
            {
                // A sloped floor does not take its texture from the world's
                // x and y. `RooSubSector.UpdateVertexUV` (RooSubSector.cs:
                // 402-483) measures each point's perpendicular distance
                // from two lines drawn on the plane itself: the slope's own
                // texture origin P0, and the axes P0->P1 and P0->P2 that
                // `RooSectorSlopeInfo.Calculate` builds from the stored
                // texture angle (RooSectorSlopeInfo.cs:313-346). Feeding it
                // world x and y instead - which is what this renderer did -
                // gives a texture that ignores the slope's rotation and
                // shears as the plane tilts.
                //
                // 4674 of the 30806 sectors in the game carry a slope, so
                // this is not a corner case; it is every ramp, hillside and
                // sloping roof in Meridian.
                SlopeUV(slope, wx, wy, camZ, texOffX, texOffY, out su, out sv);
            }
            else
            {
                su = (wy - anchorY - texOffY) / M59Geo.Fineness;
                sv = (wx - anchorX - texOffX) / M59Geo.Fineness;
            }

            px[y * W + sx] = Shade(
                noSample ? t.P[0]
                         : t.Sample(su + scrollU, sv + scrollV, texelsPerPixel),
                fog);
        }
    }
    /// <summary>
    /// The object's colormodifier applied to a lit texel: multiply and
    /// clamp, which is what the shader's saturate does on the way out.
    /// Skipped entirely when the object is drawn plainly, which is
    /// nearly always.
    /// </summary>
    static uint Material(uint c, Sprite sp)
    {
        if (sp.TintR == 1f && sp.TintG == 1f && sp.TintB == 1f) return c;
        uint r = (uint)MathF.Min(255f, ((c >> 16) & 0xFF) * sp.TintR);
        uint g = (uint)MathF.Min(255f, ((c >> 8) & 0xFF) * sp.TintG);
        uint b = (uint)MathF.Min(255f, (c & 0xFF) * sp.TintB);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    /// <summary>Source over destination at the given opacity.</summary>
    static uint Blend(uint under, uint over, float a)
    {
        if (a <= 0f) return under;
        float b = 1f - a;
        uint r = (uint)(((over >> 16) & 0xFF) * a + ((under >> 16) & 0xFF) * b);
        uint g = (uint)(((over >> 8) & 0xFF) * a + ((under >> 8) & 0xFF) * b);
        uint bl = (uint)((over & 0xFF) * a + (under & 0xFF) * b);
        return 0xFF000000u | (r << 16) | (g << 8) | bl;
    }

    internal static uint Shade(uint c, float f)
    {
        if (f == 1f) return c;
        if (f < 0f) f = 0f;
        // Over one is allowed and clipped per channel. The game's own
        // brightness is a FACTOR on the room's light, clamped to 1.8
        // (`Util.h:218-222`), and Ogre's scene ambient has no ceiling
        // at one either - so a slider that could only ever darken was
        // a slider that did nothing in a lit room.
        uint r = (uint)MathF.Min(255f, ((c >> 16) & 0xFF) * f);
        uint g = (uint)MathF.Min(255f, ((c >> 8) & 0xFF) * f);
        uint b = (uint)MathF.Min(255f, (c & 0xFF) * f);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }
    void CollectHits(RooFile roo, float ox, float oy, float dx, float dy, Scratch sc)
    {
        List<Hit> outHits = sc.Hits;
        outHits.Clear();
        if (UseGrid && _grid != null)
        {
            sc.Candidates.Clear();
            _grid.Collect(ox, oy, dx, dy, sc.Candidates, sc.Stamp, ref sc.Tick);
            for (int i = 0; i < sc.Candidates.Count; i++)
                TestWall(sc.Candidates[i], ox, oy, dx, dy, outHits);
        }
        else
        {
            foreach (RooWall w in roo.Walls) TestWall(w, ox, oy, dx, dy, outHits);
        }
        // Sort by distance, then by wall number. List.Sort is unstable, so
        // without the second key two walls at exactly equal distance - a
        // corner, or coincident walls - get ordered by however they were
        // iterated, and the renderer picks a different one depending on
        // whether the grid or the full wall list fed it. That made grid and
        // brute-force output differ on 5 of 362 rooms. The tiebreak makes
        // the result independent of iteration order.
        outHits.Sort(ByDistanceThenWall);
    }

    static void TestWall(RooWall w, float ox, float oy, float dx, float dy, List<Hit> outHits)
    {
        float x1 = w.X1, y1 = w.Y1, x2 = w.X2, y2 = w.Y2;
        float ex = x2 - x1, ey = y2 - y1;
        float den = dx * ey - dy * ex;
        if (MathF.Abs(den) < 1e-6f) return;
        float t = ((x1 - ox) * ey - (y1 - oy) * ex) / den;
        float s = ((x1 - ox) * dy - (y1 - oy) * dx) / den;
        if (t <= 1f || s < 0f || s > 1f) return;
        outHits.Add(new Hit {
            Wall = w, Dist = t, Len = MathF.Sqrt(ex * ex + ey * ey),
            Along = s * MathF.Sqrt(ex * ex + ey * ey),
            Right = (ex * (oy - y1) - ey * (ox - x1)) > 0f
        });
    }
    static float Area(RooSubSector l)
    {
        double s = 0; var v = l.Vertices;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            s += (double)v[j].X * v[i].Y - (double)v[i].X * v[j].Y;
        return (float)Math.Abs(s * 0.5);
    }
    /// <summary>
    /// Which sector a point is in, by the library's own BSP descent.
    ///
    /// This used to walk every leaf in the room with a crossing test
    /// and, finding none, hand back sector 0 - which is not a
    /// neighbouring sector, it is whichever one happens to be first in
    /// the file, and the whole frame's floors and ceilings then came
    /// from somewhere else entirely. Standing exactly on a leaf
    /// boundary is enough to do it, because the crossing test is strict
    /// on both sides.
    ///
    /// `RooFile.GetSubSectorAt` (:1284-1300) is the descent the library
    /// itself uses for every height query, with a defined rule at the
    /// boundary - `side >= 0` goes right - and a null when there really
    /// is nothing. It is also O(log n) rather than O(leaves), and it
    /// runs once a frame plus once per flat span.
    ///
    /// The old scan stays as the fallback for the one case the tree
    /// cannot answer: a room whose BSP is empty.
    /// </summary>
    static RooSector SectorAt(RooFile roo, float x, float y)
    {
        RooSubSector leaf = null;
        try { leaf = roo.GetSubSectorAt(x, y); } catch { }
        if (leaf != null) return M59Geo.Sector(roo, leaf.SectorNum);

        foreach (RooSubSector l in roo.BSPTreeLeaves)
        {
            if (l.Vertices == null || l.Vertices.Count < 3) continue;
            if (PointIn(l.Vertices, x, y)) return M59Geo.Sector(roo, l.SectorNum);
        }
        return null;
    }
    static bool PointIn(Polygon p, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            float xi = p[i].X, yi = p[i].Y, xj = p[j].X, yj = p[j].Y;
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
}
