using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Meridian59.Drawing2D;
using Meridian59.Common.Enums;
using Meridian59.Files;
using Meridian59.Files.BGF;
using Meridian59.Files.ROO;

/// <summary>
/// Unit conventions for Meridian 59 room geometry, established by
/// measurement rather than documentation:
///
///  - Wall X/Y are in FINENESS units, 1024 per grid square.
///  - Sector heights and RooWall.ClientLength are in XY/16 units
///    (measured: xyLength / ClientLength == 16.0 exactly on every wall
///    checked in barinn.roo).
///  - One texture tiles across one grid square, so UV = xy / 1024.
/// </summary>
public static class M59Geo
{
    public const float Fineness = 1024f;
    public const float HeightToXY = 16f;

    public static float FloorXY(RooSector s) => s == null ? 0f : (float)s.FloorHeight * HeightToXY;
    public static float CeilingXY(RooSector s) => s == null ? 0f : (float)s.CeilingHeight * HeightToXY;

    // --- sloped floors and ceilings --------------------------------------
    //
    // 4674 of the 30806 sectors carry a plane rather than a single height.
    // RooSector.CalculateSlopeHeight is z = (-Ax - By - D)/C, in the same
    // FINENESS units as FloorXY, and the flat case there is
    // FloorHeight * KODFINETOCLIENTFINE, which is FloorHeight * 16 - so
    // these are the same function, one of them just varies.

    /// <summary>Floor height at a point, following the slope if there is one.</summary>
    public static float FloorXY(RooSector s, float x, float y)
    {
        if (s == null) return 0f;
        var sl = s.SlopeInfoFloor;
        return sl == null ? (float)s.FloorHeight * HeightToXY : Plane(sl, x, y);
    }

    /// <summary>Ceiling height at a point, following the slope if there is one.</summary>
    public static float CeilingXY(RooSector s, float x, float y)
    {
        if (s == null) return 0f;
        var sl = s.SlopeInfoCeiling;
        return sl == null ? (float)s.CeilingHeight * HeightToXY : Plane(sl, x, y);
    }

    public static float Plane(RooSectorSlopeInfo sl, float x, float y)
        => (float)((-sl.A * x - sl.B * y - sl.D) / sl.C);

    // --- scrolling textures ----------------------------------------------
    //
    // 1788 sectors scroll their floor (water, lava), 5 their ceiling, and
    // 172 sidedefs scroll a wall. Ported from RooSector.GetSectorScrollSpeed
    // and its sidedef twin, which are protected so they cannot be called.
    //
    // Note the two use different constants: a sector scrolls at 12/6/2 ms
    // per pixel and a wall at 96/32/8, so a wall's "fast" is a sector's
    // "slow" and then some. Getting that backwards would be invisible in
    // one room and wrong everywhere else.

    /// <summary>Floor and ceiling scroll, in textures per second.</summary>
    public static void SectorScroll(TextureScrollSpeed speed, TextureScrollDirection dir,
                                    int texW, int texH, out float sx, out float sy)
        => ScrollRate(speed, dir, texW, texH, 12, 6, 2, out sx, out sy);

    /// <summary>
    /// Wall scroll, in textures per second.
    ///
    /// The two tables are not the same table with different constants,
    /// which is what this assumed. `RooSideDef.GetWallScrollSpeed`
    /// (:683-732) puts north and south on sp.X and east and west on
    /// sp.Y; `RooSector.GetSectorScrollSpeed` (:896-945) does the
    /// opposite. Laid side by side, the wall's table IS the sector's
    /// with the two components exchanged, in all eight directions - so
    /// the exchange is done here rather than by copying a second
    /// switch. Without it every cardinal direction came out turned a
    /// quarter, and south-west came out backwards.
    /// </summary>
    public static void WallScroll(TextureScrollSpeed speed, TextureScrollDirection dir,
                                  int texW, int texH, out float sx, out float sy)
    {
        ScrollRate(speed, dir, texW, texH, 96, 32, 8, out float a, out float b);
        sx = b; sy = a;
    }

    static void ScrollRate(TextureScrollSpeed speed, TextureScrollDirection dir,
                           int texW, int texH, int slow, int medium, int fast,
                           out float sx, out float sy)
    {
        sx = 0f; sy = 0f;
        if (texW <= 0 || texH <= 0 || speed == TextureScrollSpeed.NONE) return;

        float diag = MathF.Sqrt((float)texW * texW + (float)texH * texH);
        float length;
        switch (dir)
        {
            case TextureScrollDirection.N:  sx =  0f; sy = -1f; length = texH; break;
            case TextureScrollDirection.S:  sx =  0f; sy =  1f; length = texH; break;
            case TextureScrollDirection.E:  sx =  1f; sy =  0f; length = texW; break;
            case TextureScrollDirection.W:  sx = -1f; sy =  0f; length = texW; break;
            case TextureScrollDirection.NE: sx =  1f; sy = -1f; length = diag; break;
            case TextureScrollDirection.SE: sx =  1f; sy =  1f; length = diag; break;
            case TextureScrollDirection.SW: sx = -1f; sy =  1f; length = diag; break;
            case TextureScrollDirection.NW: sx = -1f; sy = -1f; length = diag; break;
            default: return;
        }

        switch (speed)
        {
            case TextureScrollSpeed.SLOW:   length *= slow; break;
            case TextureScrollSpeed.MEDIUM: length *= medium; break;
            case TextureScrollSpeed.FAST:   length *= fast; break;
            default: sx = 0f; sy = 0f; return;
        }

        // The library scales the direction to this length: one unit is one
        // whole texture scrolled in one second.
        if (length <= 0f) { sx = 0f; sy = 0f; return; }
        float target = 1000f / length;
        float have = MathF.Sqrt(sx * sx + sy * sy);
        if (have <= 0f) { sx = 0f; sy = 0f; return; }
        float k = target / have;
        sx *= k; sy *= k;
    }

    // --- kod (server) coordinates <-> room coordinates -------------------
    //
    // RoomObject.Position3D is in the server's own units, NOT room units.
    // The library converts with (X - 64) * 16 before touching a ROO (see
    // RoomObject.UpdatePosition and BaseClient.SendReqMoveMessage), and
    // stores height as roomHeight * 0.0625. These wrap that so the renderer
    // never sees a kod coordinate.
    public const float KodToRoom = 16f;
    public const float KodOrigin = 64f;

    public static float KodToWorld(float kod) => (kod - KodOrigin) * KodToRoom;
    public static float WorldToKod(float world) => world / KodToRoom + KodOrigin;
    /// <summary>Height only: no origin shift, just the scale.</summary>
    public static float KodHeightToXY(float kod) => kod * KodToRoom;
    public static float XYHeightToKod(float xy) => xy / KodToRoom;

    /// <summary>Bounds-checked lookup of the 1-based sector and sidedef numbers.</summary>
    public static RooSector Sector(RooFile roo, int num)
        => (num >= 1 && num <= roo.Sectors.Count) ? roo.Sectors[num - 1] : null;

    public static RooSideDef Side(RooFile roo, int num)
        => (num >= 1 && num <= roo.SideDefs.Count) ? roo.SideDefs[num - 1] : null;
}

/// <summary>
/// A decoded BGF frame as straight RGB, wrapping on sample, with a mip
/// chain.
///
/// Point-sampling a 128x128 stone texture across a floor at a grazing
/// angle aliases badly - it showed up as radial streaking across ceilings.
/// Each level is a box filter of the one above and the sampler picks a
/// level from how much world space one screen pixel covers, which is what
/// mipmapping is for.
/// </summary>
public sealed class Tex
{
    public int W, H;
    public uint[] P;

    /// <summary>
    /// The size the texture COUNTS as when a rate is worked out - how
    /// often it repeats along a wall, how far a scroll moves it, where
    /// the vertical origin sits. Normally its own, but a replacement
    /// room texture is the same picture at more pixels, so it keeps the
    /// original's: the reference's UVs come from the BGF's width and
    /// height (ControllerRoom.cpp:700-703 into RooWall.GetVertexData)
    /// and the PNG merely fills the same zero-to-one. Using the PNG's
    /// own size instead stretched every replaced texture by the ratio.
    /// </summary>
    public int UvW { get => _uvW > 0 ? _uvW : W; set => _uvW = value; }
    public int UvH { get => _uvH > 0 ? _uvH : H; set => _uvH = value; }
    int _uvW, _uvH;

    /// <summary>
    /// The texture's own shrink factor. Meridian scales wall textures by
    /// shrink/size rather than at a fixed rate, so a 512x512 at shrink 4
    /// covers twice the wall a 128x128 at shrink 2 does. See
    /// RooWall.GetVertexData, which is the game's own d3drender.c.
    /// </summary>
    public int Shrink = 1;

    /// <summary>
    /// Whether any texel carries the transparent key (palette index
    /// 254). This, not a flag on the sidedef, is what decides whether
    /// you can see past a wall in the reference: every wall part gets
    /// the same cloned base_material_room, whose pass is
    /// `alpha_rejection greater_equal 64` (general.material:263), and
    /// index 254 reaches the GPU as alpha 0 while every other palette
    /// entry is forced opaque (ColorTransformations.cs:256-257,
    /// :401, :485). A rejected fragment writes no colour and no depth,
    /// so the room behind shows through in exactly the hole texels.
    /// </summary>
    public bool HasHoles;

    /// <summary>Level 0 is P; each subsequent level is half size.</summary>
    int[] _lw, _lh;
    uint[][] _levels;

    public int LevelCount => _levels?.Length ?? 1;

    /// <summary>
    /// The mip level to sample at, and its size. For a blit that walks
    /// texels itself rather than calling Sample - a sprite - so it can
    /// read the same reduced copy a wall would.
    /// </summary>
    public uint[] Level(float texelsPerPixel, out int lw, out int lh)
    {
        lw = W; lh = H;
        if (_levels == null || texelsPerPixel <= 1f) return P;
        int lod = 0;
        float t = texelsPerPixel;
        while (t >= 2f && lod < _levels.Length - 1) { t *= 0.5f; lod++; }
        lw = _lw[lod]; lh = _lh[lod];
        return _levels[lod];
    }

    public uint Sample(float u, float v)
    {
        int x = (int)((u - MathF.Floor(u)) * W);
        int y = (int)((v - MathF.Floor(v)) * H);
        if (x < 0) x = 0; else if (x >= W) x = W - 1;
        if (y < 0) y = 0; else if (y >= H) y = H - 1;
        return P[y * W + x];
    }

    /// <summary>
    /// Samples with an explicit level of detail. <paramref name="texelsPerPixel"/>
    /// is roughly how many texels one screen pixel spans; 1 means level 0.
    /// </summary>
    public uint Sample(float u, float v, float texelsPerPixel)
    {
        if (_levels == null || texelsPerPixel <= 1f) return Sample(u, v);

        int lod = 0;
        float t = texelsPerPixel;
        while (t >= 2f && lod < _levels.Length - 1) { t *= 0.5f; lod++; }

        uint[] lp = _levels[lod];
        int lw = _lw[lod], lh = _lh[lod];
        int x = (int)((u - MathF.Floor(u)) * lw);
        int y = (int)((v - MathF.Floor(v)) * lh);
        if (x < 0) x = 0; else if (x >= lw) x = lw - 1;
        if (y < 0) y = 0; else if (y >= lh) y = lh - 1;
        return lp[y * lw + x];
    }

    /// <summary>
    /// Rebuilds the reduced copies. For a caller that has written over
    /// P after the fact - the render check paints a frame's silhouette
    /// in a colour the room cannot produce, and the copies have to say
    /// the same thing or the two passes disagree about what is there.
    /// </summary>
    public void RebuildMips() => BuildMips();

    /// <summary>
    /// Snaps every texel's alpha to nothing or to opaque at a threshold,
    /// on every reduced copy as well as the full one.
    ///
    /// This is what an alpha-tested material does, and some of the art
    /// this renderer draws is drawn by one: `base_material`, which every
    /// grass material derives from, rejects a fragment whose alpha is
    /// under 64 and draws the rest at full opacity with no blending
    /// (general.material:287-293). The GPU applies that test to the
    /// FILTERED sample, which is why the reduced copies have to be keyed
    /// too - the sprite blit treats any non-zero alpha as solid, so a
    /// mip texel that averaged out to a quarter coverage would otherwise
    /// come out as an opaque block and a receding tuft would grow a
    /// square halo.
    /// </summary>
    public void KeyAlpha(uint threshold)
    {
        if (_levels == null) { Key(P, threshold); return; }
        foreach (uint[] lv in _levels) Key(lv, threshold);
    }

    static void Key(uint[] p, uint threshold)
    {
        if (p == null) return;
        for (int i = 0; i < p.Length; i++)
            p[i] = (p[i] >> 24) >= threshold ? (p[i] | 0xFF000000u) : (p[i] & 0x00FFFFFFu);
    }

    void BuildMips()
    {
        var lv = new List<uint[]> { P };
        var lw = new List<int> { W };
        var lh = new List<int> { H };
        int cw = W, ch = H;
        uint[] cur = P;
        while (cw > 4 && ch > 4)
        {
            int nw = cw >> 1, nh = ch >> 1;
            var next = new uint[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    uint a = cur[(y * 2) * cw + x * 2];
                    uint b = cur[(y * 2) * cw + x * 2 + 1];
                    uint c = cur[(y * 2 + 1) * cw + x * 2];
                    uint d = cur[(y * 2 + 1) * cw + x * 2 + 1];
                    // Weighted by alpha, and alpha averaged on its own.
                    // A flat average pulls the transparent key's colour
                    // into every edge texel, which is how a sprite gets
                    // a halo of whatever the key happens to be; the
                    // reference's own mip chain is built by Ogre from
                    // premultiplied data and does not.
                    uint aa = a >> 24, ab = b >> 24, ac = c >> 24, ad = d >> 24;
                    uint wsum = aa + ab + ac + ad;
                    uint r, g, bl;
                    if (wsum == 0) { r = g = bl = 0; }
                    else
                    {
                        r = ((a >> 16 & 0xFF) * aa + (b >> 16 & 0xFF) * ab
                           + (c >> 16 & 0xFF) * ac + (d >> 16 & 0xFF) * ad) / wsum;
                        g = ((a >> 8 & 0xFF) * aa + (b >> 8 & 0xFF) * ab
                           + (c >> 8 & 0xFF) * ac + (d >> 8 & 0xFF) * ad) / wsum;
                        bl = ((a & 0xFF) * aa + (b & 0xFF) * ab
                            + (c & 0xFF) * ac + (d & 0xFF) * ad) / wsum;
                    }
                    next[y * nw + x] = ((wsum >> 2) << 24) | (r << 16) | (g << 8) | bl;
                }
            lv.Add(next); lw.Add(nw); lh.Add(nh);
            cur = next; cw = nw; ch = nh;
        }
        _levels = lv.ToArray(); _lw = lw.ToArray(); _lh = lh.ToArray();
    }

    /// <summary>Meridian's transparent palette index.</summary>
    public const byte Transparent = 254;

    /// <summary>
    /// What the renderer shows where there is nothing: the same near-black
    /// used when a column reaches no wall at all.
    /// </summary>
    public const uint Void = 0xFF05050Au;

    public static Tex From(BgfFile bgf, int frame = 0)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        if (frame < 0 || frame >= bgf.Frames.Count) frame = 0;
        BgfBitmap f = bgf.Frames[frame];
        byte[] idx;
        try { idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData; }
        catch { return null; }
        int w = (int)f.Width, h = (int)f.Height;
        if (idx == null || w <= 0 || h <= 0 || idx.Length < w * h) return null;
        uint[] pal = ColorTransformation.DefaultPalette;
        var p = new uint[w * h];
        bool holes = false;
        for (int i = 0; i < w * h; i++)
        {
            byte c = idx[i];
            // Index 254 is Meridian's transparent key, and in the palette it
            // is 0x0000FFFF - alpha 0 over bright cyan. Forcing alpha opaque
            // here, which walls and floors need, would paint that cyan on
            // screen. The game never shows it: `d3drender.c` rejects the key
            // colour outright. Nothing in this renderer's opaque path can see
            // through a wall, so the honest stand-in is the void colour the
            // column walk itself uses when there is nothing to draw.
            if (c == Transparent) { p[i] = Void; holes = true; }
            else p[i] = pal[c] | 0xFF000000u;
        }
        var t = new Tex { W = w, H = h, P = p, HasHoles = holes,
                          Shrink = Math.Max(1, (int)bgf.ShrinkFactor) };
        t.BuildMips();
        return t;
    }

    /// <summary>
    /// Like <see cref="From"/> but keeps transparency. Palette index 254 is
    /// Meridian's transparent colour and already carries alpha 0, so object
    /// sprites must not have alpha forced opaque the way wall and floor
    /// textures are.
    ///
    /// These carry a mip chain like everything else. They used not to,
    /// on the grounds that averaging across transparent texels bleeds
    /// the key colour into the edges - which is true of a flat average
    /// and is why the average here is weighted by alpha. Without one, a
    /// distant grate or creature was point-sampled from full resolution
    /// and crawled as you moved; the reference filters everything
    /// trilinearly with sixteen-times anisotropy
    /// (OgreClient.cpp:181, :198-200).
    /// </summary>
    public static Tex FromSprite(BgfFile bgf, int frame = 0)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        if (frame < 0 || frame >= bgf.Frames.Count) frame = 0;
        BgfBitmap f = bgf.Frames[frame];
        byte[] idx;
        try { idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData; }
        catch { return null; }
        int w = (int)f.Width, h = (int)f.Height;
        if (idx == null || w <= 0 || h <= 0 || idx.Length < w * h) return null;
        uint[] pal = ColorTransformation.DefaultPalette;
        var p = new uint[w * h];
        for (int i = 0; i < w * h; i++) p[i] = pal[idx[i]];   // alpha preserved
        var sprite = new Tex { W = w, H = h, P = p, Shrink = Math.Max(1, (int)bgf.ShrinkFactor) };
        sprite.BuildMips();
        return sprite;
    }
}

/// <summary>
/// Caches object sprite frames by (file, group, frame index).
///
/// A BGF holds a frame set per animation group and, inside each, one frame
/// per facing direction. Which one to draw depends on where the viewer is
/// standing relative to the object, so the same creature needs a different
/// frame as you walk around it - resolving that per object per frame is
/// what makes them turn.
/// </summary>
public sealed class SpriteCache
{
    readonly Dictionary<BgfFile, Dictionary<int, Tex>> _c =
        new Dictionary<BgfFile, Dictionary<int, Tex>>();

    public int Count
    {
        get { int n = 0; foreach (var d in _c.Values) n += d.Count; return n; }
    }

    /// <summary>
    /// Frame for an object drawn from a given viewing angle.
    /// <paramref name="viewAngle"/> is in Meridian angle units (0..4095)
    /// and is the object's facing relative to the viewer.
    /// </summary>
    public Tex Get(BgfFile bgf, int group, ushort viewAngle)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;

        // No frame, no picture. This used to clamp an invalid index to
        // zero, which drew group 1's front frame instead - a creature
        // whose art lacks the group the server named froze in a wrong
        // pose facing the wrong way and stripped of its weapon and
        // clothes, rather than not being drawn.
        //
        // The library's answer is null all the way down:
        // GetFrameIndex returns -1 for a group the art has not got and
        // for a facing that group has no frame for (BgfFile.cs:490-514),
        // RoomObject.UpdateFrameIndices then leaves ViewerFrame null
        // (RoomObject.cs:1742-1746), RenderInfo.Calculate does nothing
        // without a main frame (Drawing2D/RenderInfo.cs:303),
        // RemoteNode2D::UpdateMaterial returns early on a null image
        // (RemoteNode2D.cpp:165-167) and the billboard keeps the zero
        // dimensions it was created with (:30-31). The object is simply
        // not drawn.
        //
        // Every caller here already copes with null - Place returns
        // false (Renderer.cs:1249), WorldHeight falls back to the
        // label's own fallback height (:1174), and the projectile sync
        // only adds a sprite whose texture came back
        // (WorldSync.cs:363). Measured over the 629 BGFs with frames:
        // the clamp fired on all 20128 lookups for a group the art has
        // not got, and on 242 of 16976 lookups for a group it HAS -
        // those being a facing that group carries no frame for, which
        // GetFrameIndex refuses in the same way and for which the
        // reference therefore draws nothing either. Those 242 are in
        // four files (gshnecbk, gshnecov, maulov - worn parts, which
        // reach the screen through the composed path and not through
        // this cache - and hist_king1, whose index really does run past
        // its frame list), so none of them is a room object that was
        // relying on the clamp to be drawn at all.
        int idx = bgf.GetFrameIndex(group < 1 ? 1 : group, viewAngle);
        if (idx < 0 || idx >= bgf.Frames.Count) return null;

        if (!_c.TryGetValue(bgf, out var byFrame))
            _c[bgf] = byFrame = new Dictionary<int, Tex>();

        if (!byFrame.TryGetValue(idx, out Tex t))
        {
            t = Tex.FromSprite(bgf, idx);
            byFrame[idx] = t;
        }
        return t;
    }

    public void Clear() => _c.Clear();
}

/// <summary>Caches room textures by grd number.</summary>
public sealed class TexCache
{
    readonly ResourceManager _rm;
    // Read from several render threads at once; the fast path has to be
    // lock-free, and decoding is serialised because ResourceManager is not
    // itself known to be thread-safe.
    readonly ConcurrentDictionary<long, Tex> _c = new ConcurrentDictionary<long, Tex>();
    readonly object _buildLock = new object();
    public TexCache(ResourceManager rm) { _rm = rm; }

    /// <summary>
    /// The folder of replacement room textures, or null to use the
    /// BGFs. This is the reference's DEFAULT look, not an extra: it
    /// loads the roomtextures group at startup (OgreClient.cpp:685-686,
    /// Constants.h:52) as Ogre textures named exactly what the room
    /// loader asks for - RooFile.GetNameForTexture returns
    /// `Filename-Frame.png` (RooFile.cs:2300-2303) - and
    /// CreateTextureA8R8G8B8 then returns before it ever decodes the
    /// BGF, because a texture of that name already exists
    /// (Util.h:401-403). DisableNewRoomTex defaults to false
    /// (OgreClientConfig.h:50).
    ///
    /// DIVERGENCE: they are not shipped. There are 443 of them and they
    /// come to 424 megabytes, which is not an APK. A player who copies
    /// their desktop client's resource folder across gets them; anyone
    /// else gets the original art, which is what this renderer has
    /// always drawn. The UVs are unaffected either way - the reference
    /// takes them from the BGF's own width and height
    /// (ControllerRoom.cpp:700-703) and the PNG merely fills the same
    /// zero-to-one.
    /// </summary>
    public string RoomTextureDir;

    /// <summary>
    /// Where a replacement folder might be, given the game's resource
    /// folder. The reference keeps it beside the rest of its resources.
    /// </summary>
    public static string FindRoomTextures(string resourceDir)
    {
        foreach (string c in RoomTextureCandidates(resourceDir))
            if (c != null && System.IO.Directory.Exists(c)
                && System.IO.Directory.EnumerateFiles(c, "grd*.png").GetEnumerator().MoveNext())
                return c;
        return null;
    }

    static System.Collections.Generic.IEnumerable<string> RoomTextureCandidates(string resourceDir)
    {
        string env = Environment.GetEnvironmentVariable("M59ROOMTEX");
        if (!string.IsNullOrEmpty(env)) yield return env;
        if (!string.IsNullOrEmpty(resourceDir))
        {
            yield return System.IO.Path.Combine(resourceDir, "roomtextures");
            string up = System.IO.Path.GetDirectoryName(
                resourceDir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            if (up != null)
            {
                yield return System.IO.Path.Combine(up, "roomtextures");
                yield return System.IO.Path.Combine(up, "Resources", "roomtextures");
            }
        }
        yield return System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "Resources", "roomtextures");
    }

    /// <summary>
    /// The replacement for one texture frame, or null if there is none.
    ///
    /// A replacement carries its holes as ALPHA rather than as the
    /// palette's index 254, and the reference reads it the same way
    /// either way: base_material_room alpha-tests at 64
    /// (general.material:263) whatever filled the texture. So anything
    /// under that threshold becomes the void here, and the texture
    /// reports its holes - which is what decides whether a wall can be
    /// seen past.
    ///
    /// Masked lookups are left to the BGF. Those sample the transparent
    /// key as alpha zero rather than as a colour, and that is the
    /// original's palette index; a replacement would have to be
    /// re-keyed rather than just read.
    /// </summary>
    Tex Replacement(BgfFile bgf, int frame, bool masked)
    {
        if (masked || RoomTextureDir == null || bgf == null) return null;
        string name = RooFile.GetNameForTexture(bgf, frame);
        string path = System.IO.Path.Combine(RoomTextureDir, name);
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            uint[] px = M59Png.Read(path, out int w, out int h);
            if (px == null || w <= 0 || h <= 0) return null;
            bool holes = false;
            for (int i = 0; i < px.Length; i++)
            {
                if ((px[i] >> 24) >= 64) { px[i] |= 0xFF000000u; continue; }
                px[i] = Tex.Void; holes = true;
            }
            // The shrink factor stays the BGF's: it is what relates the
            // texture to the wall, and the replacement is the same
            // picture at more pixels, not a different size of wall.
            BgfBitmap f = bgf.Frames[frame];
            var t = new Tex { W = w, H = h, P = px, HasHoles = holes,
                              UvW = (int)f.Width, UvH = (int)f.Height,
                              Shrink = Math.Max(1, (int)bgf.ShrinkFactor) };
            t.RebuildMips();
            return t;
        }
        catch { return null; }
    }
    public int Count => _c.Count;

    readonly ConcurrentDictionary<long, Tex> _masked = new ConcurrentDictionary<long, Tex>();

    /// <summary>The still texture, which is what floors and ceilings use.</summary>
    public Tex Get(ushort num) => Get(num, 1, _c, false);

    /// <summary>
    /// A wall texture at its current animation group. 118 sidedefs across
    /// the rooms carry WF_HAS_ANIMATED, and the library drives them by
    /// picking a different frame of the same file - see RooSideDef, which
    /// does GetFrameIndex(animation.CurrentGroup, 0). Frame 0 every time
    /// is a torch that never flickers.
    /// </summary>
    public Tex Get(ushort num, ushort group) => Get(num, group, _c, false);

    /// <summary>
    /// Same texture with its transparency kept, for the walls that are
    /// meant to be seen through. Room textures normally have alpha forced
    /// opaque - a floor has no holes in it - so a grate drawn with
    /// <see cref="Get"/> is a solid sheet of cyan.
    ///
    /// It carries a mip chain like everything else - <see cref="Tex.FromSprite"/>
    /// builds one and <see cref="Replacement"/> rebuilds one. The comment
    /// here used to say it had none, on the grounds that averaging across
    /// transparent texels bleeds the key colour into the edges; that is
    /// true of a FLAT average and is exactly why <c>BuildMips</c> weights
    /// colour by alpha. The chain went in with the sprites' and this line
    /// was not updated with it.
    /// </summary>
    public Tex GetMasked(ushort num, ushort group = 1) => Get(num, group, _masked, true);

    Tex Get(ushort num, ushort group, ConcurrentDictionary<long, Tex> cache, bool masked)
    {
        if (num == 0) return null;

        // Keyed on the file and the frame, so an animated wall does not
        // rebuild its texture every time the group comes round again.
        BgfFile bgf = null;
        int frame = 0;
        if (group > 1)
        {
            try
            {
                bgf = _rm.GetRoomTexture(num);
                if (bgf != null) frame = bgf.GetFrameIndex(group, 0);
                if (frame < 0) frame = 0;
            }
            catch { }
        }

        long key = ((long)num << 20) | (uint)frame;
        if (cache.TryGetValue(key, out Tex t)) return t;
        lock (_buildLock)
        {
            if (cache.TryGetValue(key, out t)) return t;
            Tex built = null;
            try
            {
                bgf ??= _rm.GetRoomTexture(num);
                if (frame >= (bgf?.Frames.Count ?? 0)) frame = 0;
                built = Replacement(bgf, frame, masked)
                     ?? (masked ? Tex.FromSprite(bgf, frame) : Tex.From(bgf, frame));
            }
            catch { }
            cache[key] = built;
            return built;
        }
    }
}
