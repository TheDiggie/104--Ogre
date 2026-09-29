using System;
using System.Collections.Generic;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// Builds the picture of a room object the way the game builds it: the
/// main overlay with its suboverlays laid on top of and underneath it.
///
/// Why this exists: an object's art is not one frame. It is a main frame
/// with parts pinned to hotspots on it, each part its own BGF - the
/// player's own arm gripping whichever weapon they are holding, a body
/// with what it wears, a creature assembled from pieces. Drawing only the
/// main frame - which is what this client did before - drops every part,
/// and with it the per-part colour translation that dyes them.
///
/// Nothing here is invented. The layout arithmetic lives in the library's
/// RenderInfo, which is called rather than copied; the draw order and the
/// per-overlay palette come from ImageComposer, which runs three passes of
/// underlays, then the main frame, then three passes of overlays; and the
/// pixels are laid down by the library's own BgfBitmap scaler, so a frame
/// lands here exactly as it lands in the Ogre client.
/// </summary>
public static class M59Compose
{
    /// <summary>
    /// The composed picture, or null if there is nothing to draw.
    /// <paramref name="worldW"/> and <paramref name="worldH"/> come back in
    /// world XY units - RenderInfo.WorldSize is the size at shrink 1, which
    /// is what the Ogre client hands its billboard, and one of those units
    /// is 16 world units exactly as it is for a lone frame.
    /// </summary>
    public static Tex Build(RoomObject o, out float worldW, out float worldH)
    {
        worldW = worldH = 0f;
        if (o == null || o.Resource == null) return null;

        // Same arguments the Ogre client's RemoteNode2D passes: the frame
        // for the angle we are looking from, and the Y offset applied.
        var ri = new RenderInfo(o, true, true);
        Tex t = Raster(ri);
        if (t == null) return null;

        worldW = ri.WorldSize.X * M59Geo.HeightToXY;
        worldH = ri.WorldSize.Y * M59Geo.HeightToXY;
        return t;
    }

    /// <summary>
    /// The inventory icon for an object, composed the way the game's own
    /// inventory composes it - which is not the way a room object is
    /// composed. UIInventory.cpp sets, per slot: the front frame rather
    /// than the viewer's, no Y offset, no power-of-two padding, and the
    /// picture scaled into a 40x40 box and centred both ways. Using the
    /// world settings instead gives icons that face wherever you happen
    /// to be standing and sit at the top of a tall empty box.
    ///
    /// <paramref name="rootHotspot"/> composes from one part of the object
    /// downwards instead of the whole thing - the portrait in the game's
    /// avatar panel is the object composed from its HEAD hotspot, which is
    /// how a picture of a face comes out of a picture of a body. Zero, the
    /// default, means the whole object; a hotspot the object does not have
    /// falls back to the whole object, which is RenderInfo's own
    /// behaviour rather than something added here.
    /// </summary>
    public static Tex Icon(ObjectBase o, int size, byte rootHotspot = 0)
    {
        if (o == null || o.Resource == null || size < 1) return null;

        var ri = new RenderInfo(
            o,
            false,                 // ApplyYOffset
            rootHotspot,           // compose from this hotspot down
            RenderInfo.DEFAULTQUALITY,
            false,                 // ScalePow2
            (uint)size, (uint)size,
            true,                  // CenterVertical
            true);                 // CenterHorizontal
        return Raster(ri);
    }

    /// <summary>Draws a laid-out RenderInfo into a picture.</summary>
    /// <summary>
    /// The red edge the game puts round whatever you have targeted, in
    /// place: `ImageComposerGDI.DrawPostEffectTarget`, which is worth
    /// copying literally because it is stranger than it sounds.
    ///
    /// It walks the pixel buffer as one flat run and turns any opaque
    /// pixel whose **previous or next** pixel is fully transparent pure
    /// red. That is a horizontal edge test, not an outline: it catches
    /// the left and right sides of a shape and leaves the top and
    /// bottom alone, and it wraps across row ends, so the last pixel of
    /// a row is compared with the first of the next. The result is the
    /// red-sided silhouette the game has always had, and a tidier
    /// four-way outline would not look like it.
    ///
    /// The first and last pixel are skipped, as they are there.
    /// </summary>
    public static void Outline(Tex t)
    {
        if (t?.P == null) return;
        uint[] px = t.P;
        for (int i = 1; i < px.Length - 1; i++)
            if (px[i] != 0x00000000u && (px[i - 1] == 0x00000000u || px[i + 1] == 0x00000000u))
                px[i] = 0xFFFF0000u;
    }

    static Tex Raster(RenderInfo ri)
    {
        if (ri == null || ri.Bgf == null) return null;

        // Rounded, not ceilinged: the library's own composers size the
        // bitmap with Convert.ToInt32, and a dimension of 478.00003 is a
        // 478-pixel picture there. Rounding it up instead leaves a column
        // of nothing down one side and shifts nothing else, which is just
        // enough to look like a rendering bug rather than an arithmetic
        // one.
        int w = Convert.ToInt32(ri.Dimension.X);
        int h = Convert.ToInt32(ri.Dimension.Y);
        if (w <= 0 || h <= 0) return null;
        // A composed player runs to a few hundred pixels a side; anything
        // past this is a misread rather than art worth an allocation.
        if ((long)w * h > 4096L * 4096L) return null;

        var p = new uint[w * h];
        unsafe
        {
            fixed (uint* buf = p)
            {
                // Underlays first, in the library's pass order, then the
                // main frame, then the overlays. Getting this wrong puts a
                // weapon in front of the hand it belongs behind.
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDERUNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDEROVER);

                Blit(ri.Bgf, buf, w, h, ri.Origin, ri.Size, ri.BgfColor);

                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVERUNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVEROVER);
            }
        }

        return new Tex { W = w, H = h, P = p, Shrink = 1 };
    }

    static unsafe void Pass(RenderInfo ri, uint* buf, int w, int h, HotSpotType kind)
    {
        foreach (SubOverlay.RenderInfo s in ri.SubBgf)
            if (s.HotspotType == kind && s.Bgf != null && s.SubOverlay != null)
                Blit(s.Bgf, buf, w, h, s.Origin, s.Size, s.SubOverlay.ColorTranslation);
    }

    static unsafe void Blit(Meridian59.Files.BGF.BgfBitmap frame, uint* buf, int w, int h,
                            Meridian59.Common.V2 origin, Meridian59.Common.V2 size, byte palette)
    {
        // Round first, then judge. The library's scaler cannot place a
        // frame at a negative origin - its own composers return rather
        // than clamp, and a clamp would shift the part sideways relative
        // to the rest, losing the pose to save a limb. But the origin
        // arrives as a float that has been through a scale and a
        // translate, and "negative" can mean -0.0000019: the dye bottle's
        // icon came out that way and vanished entirely. Rounding is what
        // the library does with these numbers everywhere else, and it
        // turns that hair into the zero it plainly is.
        int ox = Convert.ToInt32(origin.X), oy = Convert.ToInt32(origin.Y);
        int sw = Convert.ToInt32(size.X), sh = Convert.ToInt32(size.Y);
        if (ox < 0 || oy < 0) return;
        if (sw < 1 || sh < 1) return;

        // Some frames are CRUSH-compressed, and the library can only
        // undo that in an x86 Windows build - it throws everywhere else,
        // including here and on a phone. One undecodable part must not
        // cost the whole object its picture, so the part is skipped and
        // the rest of the object is still drawn.
        try
        {
            if (frame.IsCompressed) frame.IsCompressed = false;
        }
        catch { return; }

        // Rounded, not cast: a cast truncates, and a part whose width
        // comes out at 453.9998 then loses its last column.
        frame.FillPixelDataAsA8R8G8B8TransparencyBlackScaled(
            buf, (uint)w, (uint)h,
            (uint)ox, (uint)oy, (uint)sw, (uint)sh,
            palette);
    }
}

/// <summary>
/// Composed pictures, keyed the way the library keys its own image cache:
/// on the object's viewer appearance hash, which changes when anything
/// that affects the picture does - the frame, the facing, the overlays,
/// their colours. A player who draws a sword gets a new hash and a new
/// picture; one who merely walks towards you does not, until the frame
/// changes.
/// </summary>
public sealed class ComposeCache
{
    public sealed class Entry { public Tex Tex; public float WorldW, WorldH; }

    readonly Dictionary<ulong, Entry> _c = new Dictionary<ulong, Entry>();
    public int Count => _c.Count;

    /// <summary>
    /// The composed picture for an object as seen from
    /// <paramref name="viewer"/>. Updates the object's viewer angle first,
    /// because that is what decides which frames the library picks.
    /// </summary>
    public Entry Get(RoomObject o, Meridian59.Common.V2 viewer, bool outlined = false)
    {
        if (o == null || o.Resource == null) return null;

        o.UpdateViewerAngle(ref viewer);

        // The hash and the frame indices are recalculated in the object's
        // own tick, not here, so setting the angle above shows up on the
        // next one. That is the library's behaviour rather than a
        // shortcut: the Ogre client sets the angle the same way and waits
        // for ViewerAppearanceChanged. It costs one tick of lag on a
        // turning object and saves recomposing a picture per frame.
        // The outlined picture is a second entry rather than a second
        // cache: the same object is the target for a moment and then is
        // not, and both versions are worth keeping while that happens.
        ulong key = o.ViewerAppearanceHash | (outlined ? 0x1_0000_0000UL : 0UL);
        if (_c.TryGetValue(key, out Entry e)) return e;

        Tex t = M59Compose.Build(o, out float ww, out float wh);
        if (t == null) return null;
        if (outlined) M59Compose.Outline(t);

        // The cache is per room and rooms are small, but a crowd of
        // players in a lot of poses is not, so it is not unbounded.
        if (_c.Count > 512) _c.Clear();

        e = new Entry { Tex = t, WorldW = ww, WorldH = wh };
        _c[key] = e;
        return e;
    }

    public void Clear() => _c.Clear();
}
