using System;
using System.Collections.Generic;
using System.Linq;
using Meridian59.Common;
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

    readonly RooFile _roo;
    readonly TexCache _tex;
    readonly List<Hit> _hits = new List<Hit>(64);

    public Renderer(RooFile roo, TexCache tex) { _roo = roo; _tex = tex; }

    public RooSector SectorAtPoint(float x, float y) => SectorAt(_roo, x, y);

    /// <summary>Renders one frame into <paramref name="px"/> (length W*H, ARGB).</summary>
    public int Render(uint[] px, int W, int H, float camX, float camY, float camZ, float angle)
    {
        float proj = (W * 0.5f) / MathF.Tan(Fov * 0.5f);
        float horizon = H * 0.5f;
        RooSector camSector = SectorAt(_roo, camX, camY);
        int solidCols = 0;

        for (int sx = 0; sx < W; sx++)
        {
            float camOff = (sx - W * 0.5f) / proj;
            float rayA = angle + MathF.Atan(camOff);
            float cosFix = MathF.Cos(rayA - angle);
            float rdx = MathF.Cos(rayA), rdy = MathF.Sin(rayA);

            CollectHits(_roo, camX, camY, rdx, rdy, _hits);

            int yTop = 0, yBot = H - 1;
            RooSector cur = camSector;
            bool closed = false;

            foreach (Hit h in _hits)
            {
                if (yTop > yBot) break;
                float perp = MathF.Max(1f, h.Dist * cosFix);

                RooSector near = M59Geo.Sector(_roo, h.Right ? h.Wall.RightSectorNum : h.Wall.LeftSectorNum);
                RooSector far  = M59Geo.Sector(_roo, h.Right ? h.Wall.LeftSectorNum  : h.Wall.RightSectorNum);
                RooSideDef side = M59Geo.Side(_roo, h.Right ? h.Wall.RightSideNum : h.Wall.LeftSideNum);
                if (near == null) near = cur;

                float nf = M59Geo.FloorXY(near), nc = M59Geo.CeilingXY(near);
                int ceilY  = ScreenY(nc, camZ, horizon, proj, perp);
                int floorY = ScreenY(nf, camZ, horizon, proj, perp);

                FillFlat(px, W, H, sx, yTop, Math.Min(yBot, ceilY - 1), true,
                         near, camX, camY, camZ, horizon, proj, angle, rayA, _tex);
                FillFlat(px, W, H, sx, Math.Max(yTop, floorY + 1), yBot, false,
                         near, camX, camY, camZ, horizon, proj, angle, rayA, _tex);

                yTop = Math.Max(yTop, ceilY);
                yBot = Math.Min(yBot, floorY);
                if (yTop > yBot) { closed = true; break; }

                float u = (h.Along + (h.Right ? h.Wall.RightXOffset : h.Wall.LeftXOffset) * M59Geo.HeightToXY) / M59Geo.Fineness;
                float fog = MathF.Min(1f, FogFar / perp);

                if (far == null)
                {
                    DrawWall(px, W, H, sx, yTop, yBot, ceilY, floorY, nf, nc,
                             side != null ? _tex.Get(side.MiddleTexture) : null, u, fog);
                    closed = true;
                    break;
                }

                // A two-sided wall that still carries a middle texture is a
                // solid wall stored with sectors on both sides.
                if (side != null && side.MiddleTexture != 0)
                {
                    Tex mid = _tex.Get(side.MiddleTexture);
                    if (mid != null)
                    {
                        DrawWall(px, W, H, sx, yTop, yBot, ceilY, floorY, nf, nc, mid, u, fog);
                        closed = true;
                        break;
                    }
                }

                float ff = M59Geo.FloorXY(far), fc = M59Geo.CeilingXY(far);

                if (fc < nc)
                {
                    int farCeilY = ScreenY(fc, camZ, horizon, proj, perp);
                    DrawWall(px, W, H, sx, yTop, Math.Min(yBot, farCeilY - 1), ceilY, farCeilY, fc, nc,
                             side != null ? _tex.Get(side.UpperTexture) : null, u, fog);
                    yTop = Math.Max(yTop, farCeilY);
                }
                if (ff > nf)
                {
                    int farFloorY = ScreenY(ff, camZ, horizon, proj, perp);
                    DrawWall(px, W, H, sx, Math.Max(yTop, farFloorY), yBot, farFloorY, floorY, nf, ff,
                             side != null ? _tex.Get(side.LowerTexture) : null, u, fog);
                    yBot = Math.Min(yBot, farFloorY);
                }

                cur = far;
            }

            if (closed) solidCols++;
            else for (int y = yTop; y <= yBot && y < H; y++) if (y >= 0) px[y * W + sx] = 0xFF05050Au;
        }
        return solidCols;
    }

    static int ScreenY(float worldH, float camZ, float horizon, float proj, float perp)
        => (int)MathF.Round(horizon - (worldH - camZ) * proj / perp);

    static void DrawWall(uint[] px, int W, int H, int sx, int y0, int y1,
                         int spanTopY, int spanBotY, float spanBotH, float spanTopH,
                         Tex t, float u, float fog)
    {
        if (y0 < 0) y0 = 0;
        if (y1 > H - 1) y1 = H - 1;
        float span = Math.Max(1f, spanBotY - spanTopY);
        for (int y = y0; y <= y1; y++)
        {
            uint c;
            if (t == null) c = Shade(0xFF5A5A62u, fog);
            else
            {
                float f = (y - spanTopY) / span;                 // 0 at top of span
                float worldH = spanTopH + f * (spanBotH - spanTopH);
                float v = -worldH / M59Geo.Fineness;             // textures run upward
                // Meridian stores room textures with the axes swapped
                // relative to how they decode as an image: the texture's
                // X axis runs UP the wall and its Y axis runs ALONG it.
                // Verified against grd11065 (a panelled door) and grd02033
                // (a hatch in mossy stone) - with u,v the masonry courses
                // came out vertical and the banner's fleur-de-lis lay on
                // their sides.
                c = Shade(t.Sample(v, u), fog);
            }
            px[y * W + sx] = c;
        }
    }
    static void FillFlat(uint[] px, int W, int H, int sx, int y0, int y1, bool ceiling,
                         RooSector sec, float camX, float camY, float camZ,
                         float horizon, float proj, float angle, float rayA, TexCache tc)
    {
        if (sec == null) return;
        if (y0 < 0) y0 = 0;
        if (y1 > H - 1) y1 = H - 1;
        if (y0 > y1) return;

        float planeH = ceiling ? M59Geo.CeilingXY(sec) : M59Geo.FloorXY(sec);
        Tex t = tc.Get(ceiling ? sec.CeilingTexture : sec.FloorTexture);
        uint flat = ceiling ? 0xFF0B0B10u : 0xFF141418u;
        float cosFix = MathF.Cos(rayA - angle);
        float rdx = MathF.Cos(rayA), rdy = MathF.Sin(rayA);

        for (int y = y0; y <= y1; y++)
        {
            if (t == null) { px[y * W + sx] = flat; continue; }
            float dy = y - horizon;
            if (MathF.Abs(dy) < 0.5f) { px[y * W + sx] = flat; continue; }
            float straight = MathF.Abs((camZ - planeH) * proj / dy);
            float d = straight / MathF.Max(0.2f, cosFix);
            float wx = camX + rdx * d, wy = camY + rdy * d;
            float fog = MathF.Min(1f, FogFar / MathF.Max(straight, 1f));
            // Same axis swap as walls - grd02011 is a floor of tall stone
            // slabs and rendered as wide ones until y,x were used.
            px[y * W + sx] = Shade(t.Sample(wy / M59Geo.Fineness, wx / M59Geo.Fineness), fog);
        }
    }
    static uint Shade(uint c, float f)
    {
        if (f >= 1f) return c;
        if (f < 0f) f = 0f;
        uint r = (uint)(((c >> 16) & 0xFF) * f), g = (uint)(((c >> 8) & 0xFF) * f), b = (uint)((c & 0xFF) * f);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }
    static void CollectHits(RooFile roo, float ox, float oy, float dx, float dy, List<Hit> outHits)
    {
        outHits.Clear();
        foreach (RooWall w in roo.Walls)
        {
            float x1 = w.X1, y1 = w.Y1, x2 = w.X2, y2 = w.Y2;
            float ex = x2 - x1, ey = y2 - y1;
            float den = dx * ey - dy * ex;
            if (MathF.Abs(den) < 1e-6f) continue;
            float t = ((x1 - ox) * ey - (y1 - oy) * ex) / den;
            float s = ((x1 - ox) * dy - (y1 - oy) * dx) / den;
            if (t <= 1f || s < 0f || s > 1f) continue;
            outHits.Add(new Hit {
                Wall = w, Dist = t, Along = s * MathF.Sqrt(ex * ex + ey * ey),
                Right = (ex * (oy - y1) - ey * (ox - x1)) > 0f
            });
        }
        outHits.Sort((p, q) => p.Dist.CompareTo(q.Dist));
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
