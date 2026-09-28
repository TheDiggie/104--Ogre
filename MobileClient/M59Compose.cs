using System;
using System.Collections.Generic;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// Builds the picture of a room object the way the game builds it: the
/// main overlay with its suboverlays laid on top of and underneath it.
///
/// Why this exists: an object's art is not one frame. A player is a body
/// plus whatever they are wearing and holding, each its own BGF pinned to
/// a hotspot on the frame below it, and a good many monsters are put
/// together the same way. Drawing only the main frame - which is what this
/// client did before - shows a Knight with no sword and no shield, and
/// misses the colour translation that dyes the parts.
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
        if (ri.Bgf == null) return null;

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
                // shield in front of the body it hangs behind.
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDERUNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_UNDEROVER);

                Blit(ri.Bgf, buf, w, h, ri.Origin, ri.Size, ri.BgfColor);

                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVERUNDER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVER);
                Pass(ri, buf, w, h, HotSpotType.HOTSPOT_OVEROVER);
            }
        }

        worldW = ri.WorldSize.X * M59Geo.HeightToXY;
        worldH = ri.WorldSize.Y * M59Geo.HeightToXY;
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
        // The library's scaler cannot place a frame at a negative origin -
        // its own composers return rather than clamp, and a clamp here
        // would shift the part sideways relative to the rest. Skipping
        // loses a limb; drawing it in the wrong place loses the pose.
        if (origin.X < 0f || origin.Y < 0f) return;
        if (size.X < 1f || size.Y < 1f) return;

        // Some frames are CRUSH-compressed, and the library can only
        // undo that in an x86 Windows build - it throws everywhere else,
        // including here and on a phone. One undecodable part must not
        // cost the whole object its picture, so the part is skipped and
        // the rest of the body is still drawn.
        try
        {
            if (frame.IsCompressed) frame.IsCompressed = false;
        }
        catch { return; }

        // Convert rather than cast, again: a cast truncates, and a part
        // whose width comes out at 453.9998 then loses its last column.
        frame.FillPixelDataAsA8R8G8B8TransparencyBlackScaled(
            buf, (uint)w, (uint)h,
            Convert.ToUInt32(origin.X), Convert.ToUInt32(origin.Y),
            Convert.ToUInt32(size.X), Convert.ToUInt32(size.Y),
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

    readonly Dictionary<uint, Entry> _c = new Dictionary<uint, Entry>();
    public int Count => _c.Count;

    /// <summary>
    /// The composed picture for an object as seen from
    /// <paramref name="viewer"/>. Updates the object's viewer angle first,
    /// because that is what decides which frames the library picks.
    /// </summary>
    public Entry Get(RoomObject o, Meridian59.Common.V2 viewer)
    {
        if (o == null || o.Resource == null) return null;

        o.UpdateViewerAngle(ref viewer);

        // The hash and the frame indices are recalculated in the object's
        // own tick, not here, so setting the angle above shows up on the
        // next one. That is the library's behaviour rather than a
        // shortcut: the Ogre client sets the angle the same way and waits
        // for ViewerAppearanceChanged. It costs one tick of lag on a
        // turning object and saves recomposing a picture per frame.
        uint key = o.ViewerAppearanceHash;
        if (_c.TryGetValue(key, out Entry e)) return e;

        Tex t = M59Compose.Build(o, out float ww, out float wh);
        if (t == null) return null;

        // The cache is per room and rooms are small, but a crowd of
        // players in a lot of poses is not, so it is not unbounded.
        if (_c.Count > 512) _c.Clear();

        e = new Entry { Tex = t, WorldW = ww, WorldH = wh };
        _c[key] = e;
        return e;
    }

    public void Clear() => _c.Clear();
}
