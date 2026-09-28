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
    public const float Fov = 75f * MathF.PI / 180f;
    public const float EyeHeight = 0.6f * M59Geo.Fineness;
    public const float FogFar = 4500f;

    public struct Hit { public RooWall Wall; public float Dist, Along; public bool Right; }

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
    }

    public RooSector SectorAtPoint(float x, float y) => SectorAt(_roo, x, y);

    /// <summary>Renders one frame into <paramref name="px"/> (length W*H, ARGB).</summary>
    public int Render(uint[] px, int W, int H, float camX, float camY, float camZ, float angle)
    {
        float proj = (W * 0.5f) / MathF.Tan(Fov * 0.5f);
        float horizon = Horizon(H, proj);
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
                         NoFlats, NoSample, Time);
                FillFlat(px, W, H, sx, Math.Max(yTop, floorY + 1), yBot, false,
                         near, camX, camY, camZ, horizon, proj, angle, rayA, _tex,
                         NoFlats, NoSample, Time);

                yTop = Math.Max(yTop, ceilY);
                yBot = Math.Min(yBot, floorY);
                if (yTop > yBot) { closed = true; break; }

                // WF_BACKWARDS is "draw bitmap right/left reversed", set on
                // 1108 sidedefs across the 362 rooms. The offset is applied
                // after the reversal, not reversed with it, or the texture
                // slides the wrong way along the wall.
                float along = (HonourBackwards && side != null && side.Flags.IsBackwards) ? -h.Along : h.Along;
                // The texture's own size and shrink set the scale, so the
                // UVs cannot be worked out until the texture is known - see
                // DrawWall. What travels is the distance along the wall and
                // the sidedef's offsets.
                // Animated wall textures are a different frame of the same
                // file, and the library keeps the group on the sidedef.
                ushort texGroup = side?.Animation != null ? side.Animation.CurrentGroup : (ushort)1;
                int xOff = h.Right ? h.Wall.RightXOffset : h.Wall.LeftXOffset;
                int yOff = h.Right ? h.Wall.RightYOffset : h.Wall.LeftYOffset;
                float fog = MathF.Min(1f, FogFar / perp);
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
                             fog, tpp, false, null, 0f, 0, side != null && side.Flags.IsNoVTile,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time);
                    _depth[sx] = perp;
                    closed = true;
                    break;
                }

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
                            DrawWall(px, W, H, sx, yTop, yBot, ceilY, floorY, nf, nc, mid,
                                     along, xOff, yOff, side.Flags.IsNormalTopDown, fog, tpp,
                                     false, null, 0f, 0, side.Flags.IsNoVTile,
                                     side.Flags.ScrollSpeed, side.Flags.ScrollDirection, Time);
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
                                Y0 = yTop, Y1 = yBot, SpanTopY = ceilY, SpanBotY = floorY,
                                SpanTopH = nc, SpanBotH = nf, Along = along, XOff = xOff,
                                YOff = yOff, TopDown = side.Flags.IsNormalTopDown,
                                NoVTile = side.Flags.IsNoVTile,
                                ScrollSpeed = side.Flags.ScrollSpeed,
                                ScrollDir = side.Flags.ScrollDirection,
                                Fog = fog, Tpp = tpp, T = mid });
                    }
                }

                float ff = M59Geo.FloorXY(far, hx, hy), fc = M59Geo.CeilingXY(far, hx, hy);

                if (fc < nc)
                {
                    int farCeilY = ScreenY(fc, camZ, horizon, proj, perp);
                    DrawWall(px, W, H, sx, yTop, Math.Min(yBot, farCeilY - 1), ceilY, farCeilY, fc, nc,
                             side != null ? _tex.Get(side.UpperTexture, texGroup) : null,
                             along, xOff, yOff, side == null || !side.Flags.IsAboveBottomUp,
                             fog, tpp, false, null, 0f, 0, false,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time);
                    yTop = Math.Max(yTop, farCeilY);
                }
                if (ff > nf)
                {
                    int farFloorY = ScreenY(ff, camZ, horizon, proj, perp);
                    DrawWall(px, W, H, sx, Math.Max(yTop, farFloorY), yBot, farFloorY, floorY, nf, ff,
                             side != null ? _tex.Get(side.LowerTexture, texGroup) : null,
                             along, xOff, yOff, side != null && side.Flags.IsBelowTopDown,
                             fog, tpp, false, null, 0f, 0, false,
                             side != null ? side.Flags.ScrollSpeed : TextureScrollSpeed.NONE,
                             side != null ? side.Flags.ScrollDirection : TextureScrollDirection.N,
                             Time);
                    yBot = Math.Min(yBot, farFloorY);
                }

                cur = far;
            }

            if (closed) sc.SolidCols++;
            else for (int y = yTop; y <= yBot && y < H; y++) if (y >= 0) px[y * W + sx] = 0xFF05050Au;
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
                    px[y * W + sx] = Shade(c | 0xFF000000u, p.Fog);
                    if (_spriteDepth != null) _spriteDepth[y * W + sx] = depth;
                }
            }
        }
    }

    /// <summary>
    /// Works out where a sprite lands on screen. Shared by drawing and by
    /// picking so the two cannot disagree about what is under a finger.
    /// </summary>
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
            Fog = MathF.Min(1f, FogFar / depth),
        };
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
        if (Sprites.Count == 0 || px_ < 0 || px_ >= W || py_ < 0 || py_ >= H) return null;
        if (_depth.Length < W) return null;

        float proj = (W * 0.5f) / MathF.Tan(Fov * 0.5f);
        float horizon = Horizon(H, proj);      // Pick must agree with Render
        float ca = MathF.Cos(-angle), sa = MathF.Sin(-angle);

        Sprite best = null;
        float bestDepth = float.MaxValue;

        foreach (Sprite sp in Sprites)
        {
            if (sp.Bgf == null && sp.Texture == null) continue;
            float rx = sp.X - camX, ry = sp.Y - camY;
            float depth = rx * ca - ry * sa;
            if (depth < 32f || depth >= bestDepth || depth >= _depth[px_]) continue;
            float lateral = rx * sa + ry * ca;

            if (!Place(sp, depth, lateral, W, camX, camY, camZ, proj, horizon, out Placed p))
                continue;

            int tx = TexelX(p, px_);
            if (tx < 0) continue;
            int ty = TexelY(p, py_);
            if (ty < 0) continue;
            if ((p.T.P[ty * p.T.W + tx] >> 24) == 0) continue;   // saw straight through

            best = sp; bestDepth = depth;
        }
        return best;
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
                         float time = 0f)
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
            if (t == null) c = Shade(0xFF5A5A62u, fog);
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
                if (noVTile && masked && v < 0f) continue;
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
    static void FillFlat(uint[] px, int W, int H, int sx, int y0, int y1, bool ceiling,
                         RooSector sec, float camX, float camY, float camZ,
                         float horizon, float proj, float angle, float rayA, TexCache tc,
                         bool skip, bool noSample, float time)
    {
        if (sec == null || skip) return;
        if (y0 < 0) y0 = 0;
        if (y1 > H - 1) y1 = H - 1;
        if (y0 > y1) return;

        RooSectorSlopeInfo slope = ceiling ? sec.SlopeInfoCeiling : sec.SlopeInfoFloor;
        float planeH = ceiling ? M59Geo.CeilingXY(sec) : M59Geo.FloorXY(sec);
        Tex t = tc.Get(ceiling ? sec.CeilingTexture : sec.FloorTexture);
        uint flat = ceiling ? 0xFF0B0B10u : 0xFF141418u;
        float texOffX = sec.TextureX * M59Geo.HeightToXY;
        float texOffY = sec.TextureY * M59Geo.HeightToXY;

        // Scrolling water and lava. The rate is in whole textures per
        // second, so it adds straight onto the sampled coordinates.
        float scrollU = 0f, scrollV = 0f;
        if (time != 0f && t != null &&
            (ceiling ? sec.Flags.IsScrollCeiling : sec.Flags.IsScrollFloor))
        {
            M59Geo.SectorScroll(sec.Flags.ScrollSpeed, sec.Flags.ScrollDirection,
                                t.W, t.H, out float sxr, out float syr);
            scrollU = sxr * time;
            scrollV = syr * time;
        }
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
            if (t == null) { px[y * W + sx] = flat; continue; }
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
            float fog = MathF.Min(1f, FogFar / MathF.Max(straight, 1f));
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
            px[y * W + sx] = Shade(
                noSample ? t.P[0]
                         : t.Sample((wy - texOffY) / M59Geo.Fineness + scrollU,
                                    (wx - texOffX) / M59Geo.Fineness + scrollV, texelsPerPixel),
                fog);
        }
    }
    static uint Shade(uint c, float f)
    {
        if (f >= 1f) return c;
        if (f < 0f) f = 0f;
        uint r = (uint)(((c >> 16) & 0xFF) * f), g = (uint)(((c >> 8) & 0xFF) * f), b = (uint)((c & 0xFF) * f);
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
            Wall = w, Dist = t, Along = s * MathF.Sqrt(ex * ex + ey * ey),
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
    static RooSector SectorAt(RooFile roo, float x, float y)
    {
        foreach (RooSubSector l in roo.BSPTreeLeaves)
        {
            if (l.Vertices == null || l.Vertices.Count < 3) continue;
            if (PointIn(l.Vertices, x, y)) return M59Geo.Sector(roo, l.SectorNum);
        }
        return roo.Sectors.Count > 0 ? roo.Sectors[0] : null;
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
