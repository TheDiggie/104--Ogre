using System;
using System.Collections.Generic;
using System.IO;
using Meridian59.Files.ROO;

/// <summary>
/// Room decoration - the tufts of grass the reference scatters over every
/// grassy floor. It is the single most visible thing an outdoor room in
/// this renderer was missing: without it raza's meadow is a flat green
/// carpet, and the game's is stubbled all over.
///
/// The reference does it in <c>ControllerRoom::CreateDecoration</c>
/// (ControllerRoom.cpp:853-997), called once from LoadRoom at :485 - so
/// it is room-load work, not per-frame work, and so is this. The
/// mapping from floor texture to grass art is read out of
/// Resources/decoration/grass/grass.xml by
/// <c>ControllerRoom::LoadImproveData</c> (:1635-1725).
///
/// It is ON in the reference by default:
/// <c>DEFAULTVAL_ENGINE_DECORATIONINTENSITY = 20</c>
/// (OgreClientConfig.h:49), and <c>CreateDecoration</c> returns early
/// only when the intensity is zero or less (:872-873).
///
/// DIVERGENCE - the geometry. The reference builds each tuft as three
/// crossed double-sided quads, ten by ten Ogre units, rotated sixty
/// degrees apart about the vertical
/// (ControllerRoom.cpp:855-857 and :964-991). A star of crossed quads is
/// the standard trick for making a flat picture look the same from
/// every direction, and what it approximates is a billboard: whichever
/// way you walk round it, the silhouette stays one WIDTH across and one
/// HEIGHT tall. This renderer already draws a camera-facing billboard,
/// and draws it well (Renderer.Sprite / Renderer.DrawSprites), so a
/// clump here is ONE billboard of exactly those dimensions rather than
/// three quads. It is the closer approximation of the two, not the
/// cruder one: the crossed quads visibly thicken at the diagonals and a
/// billboard does not.
///
/// DIVERGENCE - determinism. The reference draws its random points from
/// the process-wide <c>MathUtil.Random</c> (:906, :921), which is
/// unseeded, so the same room is stubbled differently every time it is
/// entered. A room that changes under you when you walk out and back in
/// is a bug you can see, so every value here comes from a hash of the
/// leaf, triangle and clump index instead: same room, same grass,
/// forever, and no state shared with anything else.
/// </summary>
public sealed class M59Grass
{
    /// <summary>
    /// How much grass, mirroring the reference's
    /// <c>DecorationIntensity</c>. The density rule is
    /// <c>num = (int)(0.0000001 * intensity * area) + 1</c> per floor
    /// triangle (ControllerRoom.cpp:905), so this scales it linearly and
    /// zero turns it off exactly as the reference's early return does
    /// (:872-873).
    ///
    /// Twenty is the reference's default (OgreClientConfig.h:49) and it
    /// is cheap enough to be the default here too - see the measurements
    /// in README notes: the whole pass costs well under a millisecond in
    /// the grassiest rooms in the game, because that rule works out to
    /// about two clumps per grid square and the view cone reaches only a
    /// couple of hundred of them.
    /// </summary>
    public static int Intensity = 20;

    /// <summary>
    /// A clump's width and height in world units. The reference's WIDTH
    /// and HEIGHT are both 10.0f (ControllerRoom.cpp:855-856) in OGRE
    /// units, and an Ogre unit is the server's kod fineness, sixteen
    /// room units - the reference scales its points by
    /// CLIENTFINETOKODFINE, 64/1024, on the way in (:914,
    /// GeometryConstants.cs:60). So ten Ogre units is a hundred and
    /// sixty room units: a shade under a sixth of a grid square, and a
    /// fifth of the 800-unit eye height, which is about ankle deep.
    /// </summary>
    public const float ClumpSize = 10f * M59Geo.KodToRoom;

    /// <summary>
    /// Room floor textures that get grass, and which art each one gets,
    /// as grass.xml says. The key is the grd number from the BGF
    /// filename, which is what <c>RooSector.FloorTexture</c> holds -
    /// the reference looks it up with exactly that
    /// (ControllerRoom.cpp:883).
    /// </summary>
    public sealed class Defs
    {
        public readonly Dictionary<ushort, Tex[]> ByFloorTexture =
            new Dictionary<ushort, Tex[]>();
        public int TextureCount;
    }

    // --- the clumps, bucketed for culling --------------------------------
    //
    // A big outdoor room makes thousands of these (measured: 10440 in
    // omar.roo at intensity 20, 6030 in raza.roo), and
    // Renderer.DrawSprites is O(sprites) before it has drawn anything -
    // it walks the whole list, transforms each into camera space and
    // sorts. Handing it ten thousand entries every frame to throw most
    // of them away is the thing that must not happen, so the clumps live
    // in a uniform grid and only the cells the camera can actually see
    // are visited.
    //
    // The cell is two grid squares across. Smaller cells cull tighter
    // and cost more cells to walk; at this size the grassiest room in
    // the game is a few hundred cells and the ones in view are a couple
    // of dozen.
    const float CellSize = 2f * M59Geo.Fineness;

    Renderer.Sprite[][] _cells;
    int _cx0, _cy0, _cnx, _cny;
    readonly List<(float d2, int cell)> _order = new List<(float, int)>(512);

    /// <summary>How many clumps this room has in total.</summary>
    public int Count { get; private set; }
    /// <summary>How many distinct pieces of grass art the room uses.</summary>
    public int TextureCount { get; private set; }

    /// <summary>
    /// How far grass is drawn, in world units. The reference has no such
    /// limit - it hands the whole decoration mesh to Ogre and lets the
    /// GPU and the BSP cull it - so this is a column renderer's
    /// concession, and it is set far enough out that it is hard to see
    /// it work: sixteen thousand units is sixteen grid squares, by which
    /// point a clump is four pixels tall on a 540-row frame.
    /// </summary>
    public static float Distance = 16f * M59Geo.Fineness;

    /// <summary>
    /// The most clumps drawn in one frame, whatever the distance says.
    /// A backstop rather than a working limit - the measured worst case
    /// in view is in the low hundreds - so that a room nobody has
    /// measured cannot turn into a stall. Cells are visited nearest
    /// first, so what a cap drops is always the furthest grass.
    /// </summary>
    public static int MaxPerFrame = 2000;

    // --- loading grass.xml ------------------------------------------------

    /// <summary>
    /// Where the decoration art might be, given the game's resource
    /// folder. The reference keeps it under the resource path in
    /// a folder named by RESOURCEGROUPDECORATION (ControllerRoom.cpp:1639-1645,
    /// Constants.h), and this mirrors TexCache.FindRoomTextures so that
    /// the offline tools and the game agree about where to look.
    /// </summary>
    public static string FindDir(string resourceDir)
    {
        foreach (string c in Candidates(resourceDir))
            if (c != null && File.Exists(Path.Combine(c, "grass.xml")))
                return c;
        return null;
    }

    static IEnumerable<string> Candidates(string resourceDir)
    {
        string env = Environment.GetEnvironmentVariable("M59GRASS");
        if (!string.IsNullOrEmpty(env)) yield return env;
        if (!string.IsNullOrEmpty(resourceDir))
        {
            yield return Path.Combine(resourceDir, "decoration", "grass");
            string up = Path.GetDirectoryName(
                resourceDir.TrimEnd(Path.DirectorySeparatorChar));
            if (up != null)
            {
                yield return Path.Combine(up, "decoration", "grass");
                yield return Path.Combine(up, "Resources", "decoration", "grass");
            }
        }
        yield return Path.Combine(Directory.GetCurrentDirectory(),
                                  "Resources", "decoration", "grass");
    }

    /// <summary>
    /// Reads grass.xml and the PNGs beside it, or returns null if the art
    /// is not on this machine.
    ///
    /// Null is the whole of the fallback: no grass at all, exactly as the
    /// reference does when the file is missing - it logs
    /// "grass.xml decoration file missing" and returns, leaving
    /// grassMaterials empty so CreateDecoration's lookup never hits
    /// (ControllerRoom.cpp:1648-1656). Drawing a coloured stand-in
    /// instead would put something on screen that is in no version of
    /// the game.
    ///
    /// The structure of the file is two halves, read in the same order
    /// the reference reads them (:1668-1723): named material SETS, then
    /// MAPPINGS from a floor texture's grd number to one of those sets.
    /// The shipped file has one set, id 0, holding grass3 through
    /// grass8, and eight mappings - 2301, 2303-2306, 11000, 20201 and
    /// 20228.
    /// </summary>
    public static Defs LoadDefs(string dir)
    {
        if (dir == null) return null;
        string xml = Path.Combine(dir, "grass.xml");
        if (!File.Exists(xml)) return null;

        // Material name -> art. The names in grass.xml are Ogre material
        // names ("decoration/grass/3"), and grass.material resolves each
        // to one PNG beside it by
        // `set_texture_alias diffuseMap grassN.png` - the whole of that
        // file is eight such one-line materials, so the mapping is the
        // trailing number rather than anything needing a parser for
        // Ogre's material syntax.
        var sets = new Dictionary<uint, List<string>>();
        var art = new Dictionary<string, Tex>();
        try
        {
            using var r = System.Xml.XmlReader.Create(xml);
            if (!r.ReadToFollowing("grass")) return null;
            if (r.ReadToFollowing("sets") && r.ReadToDescendant("set"))
            {
                do
                {
                    if (!uint.TryParse(r["id"], out uint setid)) continue;
                    var names = new List<string>();
                    // ReadToDescendant moves the reader INTO the set, so
                    // the sibling walk below has to come back out; the
                    // reference relies on the same reader positioning.
                    if (r.ReadToDescendant("material"))
                        do { names.Add(r["name"]); } while (r.ReadToNextSibling("material"));
                    sets[setid] = names;
                } while (r.ReadToNextSibling("set"));
            }

            var defs = new Defs();
            if (r.ReadToFollowing("mappings") && r.ReadToDescendant("texture"))
            {
                do
                {
                    if (!uint.TryParse(r["id"], out uint texid)) continue;
                    if (!uint.TryParse(r["set"], out uint setid)) continue;
                    if (texid > ushort.MaxValue) continue;
                    if (!sets.TryGetValue(setid, out List<string> names)) continue;
                    var list = new List<Tex>(names.Count);
                    foreach (string n in names)
                    {
                        Tex t = Art(dir, n, art);
                        if (t != null) list.Add(t);
                    }
                    if (list.Count > 0) defs.ByFloorTexture[(ushort)texid] = list.ToArray();
                } while (r.ReadToNextSibling("texture"));
            }
            defs.TextureCount = art.Count;
            return defs.ByFloorTexture.Count > 0 ? defs : null;
        }
        catch { return null; }      // a malformed file is no grass, not a crash
    }

    /// <summary>
    /// One grass picture, cached by material name.
    ///
    /// The alpha is KEYED rather than blended, because that is what the
    /// reference's material does: grass.material derives every one of its
    /// materials from <c>base_material</c>, whose pass is
    /// `alpha_rejection greater_equal 64` with no scene_blend
    /// (general.material:287-293). A fragment under the threshold is
    /// discarded and everything else is drawn at full opacity, so the
    /// honest reading is a hard key at 64 - which also keeps the sprite
    /// blit on its fast opaque path.
    ///
    /// The key is re-applied to the reduced copies as well. The GPU
    /// alpha-tests the FILTERED sample, so a mip texel that averaged out
    /// to a quarter coverage is gone there too; without this the tufts
    /// grew a square opaque halo as they receded, because
    /// Renderer.DrawSprites treats any non-zero alpha as solid.
    /// </summary>
    static Tex Art(string dir, string material, Dictionary<string, Tex> cache)
    {
        if (string.IsNullOrEmpty(material)) return null;
        if (cache.TryGetValue(material, out Tex hit)) return hit;
        cache[material] = null;                        // remember a miss too

        // "decoration/grass/3" -> grass3.png, as grass.material says.
        int slash = material.LastIndexOf('/');
        string leaf = slash >= 0 ? material.Substring(slash + 1) : material;
        string path = Path.Combine(dir, "grass" + leaf + ".png");
        if (!File.Exists(path)) return null;

        uint[] px = M59Png.Read(path, out int w, out int h);
        if (px == null || w <= 0 || h <= 0) return null;

        var t = new Tex { W = w, H = h, P = px, Shrink = 1, HasHoles = true };
        t.RebuildMips();
        t.KeyAlpha(64);                                // general.material:293
        cache[material] = t;
        return t;
    }

    // --- generating the clumps -------------------------------------------

    /// <summary>
    /// Scatters clumps over every grassy floor in a room, once. Returns
    /// null when there is no grass art, no grassy floor, or the intensity
    /// is off - all three of which the reference treats the same way, by
    /// simply producing no decoration geometry.
    ///
    /// This is <c>CreateDecoration</c>'s first half
    /// (ControllerRoom.cpp:875-933) with the geometry replaced. The walk
    /// is the same: every BSP leaf, its floor texture looked up in the
    /// grass mapping, and then the leaf fanned into triangles from its
    /// first vertex, which is how the reference triangulates it (:889-903
    /// picks vertices 0, i+1, i+2, and CreateGeometryChunk fans the same
    /// way at :840-847).
    /// </summary>
    public static M59Grass Build(RooFile roo, Defs defs, int intensity)
    {
        if (roo == null || defs == null || intensity <= 0) return null;
        if (roo.BSPTreeLeaves == null) return null;

        var clumps = new List<Renderer.Sprite>(1024);
        var used = new HashSet<Tex>();
        int leafIndex = 0;

        foreach (RooSubSector leaf in roo.BSPTreeLeaves)
        {
            leafIndex++;
            if (leaf == null || leaf.Sector == null) continue;
            var verts = leaf.Vertices;
            if (verts == null || verts.Count < 3) continue;
            if (!defs.ByFloorTexture.TryGetValue(leaf.Sector.FloorTexture, out Tex[] items))
                continue;                              // ControllerRoom.cpp:883-884

            for (int i = 0; i < verts.Count - 2; i++)
            {
                float ax = verts[0].X, ay = verts[0].Y;
                float bx = verts[i + 1].X, by = verts[i + 1].Y;
                float cx = verts[i + 2].X, cy = verts[i + 2].Y;

                // MathUtil.TriangleArea (MathUtil.cs:338-344) is
                // 0.5 * cross, which is SIGNED - a clockwise leaf gives
                // a negative area and the reference then asks for zero
                // clumps plus one. The absolute value is the only
                // reading that makes the "+ 1" mean what its comment
                // says it means, so the magnitude is used here.
                float area = 0.5f * MathF.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay));

                // The density rule, ControllerRoom.cpp:904-905: clumps
                // scale with the triangle's area and the intensity, and
                // the "+ 1" puts at least one clump on every triangle
                // however thin it is - which is why a grassy room is
                // never bald even at intensity 1.
                int num = (int)(0.0000001f * intensity * area) + 1;

                for (int k = 0; k < num; k++)
                {
                    // Two randoms per point, as MathUtil.RandomPointInTriangle
                    // wants (MathUtil.cs:311-329) - but from a hash of
                    // where we are rather than a shared generator, so the
                    // room comes out the same every time. See the class
                    // comment.
                    uint h = Hash((uint)leafIndex, (uint)i, (uint)k);
                    float r1 = Unit(h);
                    float r2 = Unit(h * 1664525u + 1013904223u);

                    // MathUtil.cs:318-328 verbatim: the square root is
                    // what makes the distribution even over the triangle
                    // rather than bunched at the first vertex.
                    float s = MathF.Sqrt(r1);
                    float c1 = 1f - s, c2 = s * (1f - r2), c3 = r2 * s;
                    float px = c1 * ax + c2 * bx + c3 * cx;
                    float py = c1 * ay + c2 * by + c3 * cy;

                    // The floor's height under the point, following the
                    // slope if the sector has one - the reference calls
                    // CalculateFloorHeight(x, y, false) (:911), which is
                    // exactly M59Geo.FloorXY's slope-aware overload.
                    // No scaling follows: the reference's :914 converts
                    // to Ogre units and this renderer works in the room's
                    // own FINENESS units throughout.
                    float pz = M59Geo.FloorXY(leaf.Sector, px, py);

                    // Which of the set's pictures. The reference's index
                    // is `Convert.ToInt32(rnd * (items.Length - 1))`
                    // (:925-926) - note ToInt32 ROUNDS a double, so all
                    // six of the default set's materials can come up,
                    // with the first and last drawn half as often as the
                    // rest. Reproduced rather than tidied: a tidied
                    // version would change the mix of art on screen.
                    float rf = Unit(h * 22695477u + 1u) * (items.Length - 1);
                    int pick = (int)MathF.Round(rf, MidpointRounding.ToEven);
                    if (pick < 0) pick = 0;
                    else if (pick >= items.Length) pick = items.Length - 1;

                    used.Add(items[pick]);
                    clumps.Add(new Renderer.Sprite {
                        X = px, Y = py, BaseZ = pz,
                        Texture = items[pick],
                        // Both from the reference's WIDTH and HEIGHT
                        // (:855-856). Given explicitly rather than left
                        // to the art's aspect, because the art is a
                        // 256x256 PNG with no shrink factor to derive a
                        // world size from.
                        Width = ClumpSize, Height = ClumpSize,
                    });
                }
            }
        }

        if (clumps.Count == 0) return null;
        var g = new M59Grass { Count = clumps.Count, TextureCount = used.Count };
        g.Bucket(clumps);
        return g;
    }

    /// <summary>
    /// A well-mixed value from three small integers. Any cheap integer
    /// hash does; this is the usual xorshift-multiply chain over the
    /// three large primes that mesh code conventionally uses for grid
    /// coordinates.
    /// </summary>
    static uint Hash(uint a, uint b, uint c)
    {
        uint h = a * 73856093u ^ b * 19349663u ^ c * 83492791u;
        h ^= h >> 16; h *= 0x7feb352du;
        h ^= h >> 15; h *= 0x846ca68bu;
        h ^= h >> 16;
        return h;
    }

    /// <summary>The top 24 bits of a hash as a fraction in [0, 1).</summary>
    static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    void Bucket(List<Renderer.Sprite> clumps)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (Renderer.Sprite s in clumps)
        {
            if (s.X < minX) minX = s.X;
            if (s.X > maxX) maxX = s.X;
            if (s.Y < minY) minY = s.Y;
            if (s.Y > maxY) maxY = s.Y;
        }
        _cx0 = (int)MathF.Floor(minX / CellSize);
        _cy0 = (int)MathF.Floor(minY / CellSize);
        _cnx = (int)MathF.Floor(maxX / CellSize) - _cx0 + 1;
        _cny = (int)MathF.Floor(maxY / CellSize) - _cy0 + 1;

        var lists = new List<Renderer.Sprite>[_cnx * _cny];
        foreach (Renderer.Sprite s in clumps)
        {
            int cx = (int)MathF.Floor(s.X / CellSize) - _cx0;
            int cy = (int)MathF.Floor(s.Y / CellSize) - _cy0;
            int ix = cy * _cnx + cx;
            (lists[ix] ??= new List<Renderer.Sprite>(8)).Add(s);
        }
        _cells = new Renderer.Sprite[_cnx * _cny][];
        for (int i = 0; i < lists.Length; i++)
            if (lists[i] != null) _cells[i] = lists[i].ToArray();
    }

    // --- per-frame culling -----------------------------------------------

    /// <summary>
    /// Adds the clumps the camera can see to <paramref name="into"/>,
    /// nearest cells first.
    ///
    /// This is the whole reason grass is affordable in a column
    /// renderer. Renderer.DrawSprites transforms and sorts every entry
    /// it is given before it knows whether any of them is on screen, so
    /// the list it is given has to be short: a room with ten thousand
    /// clumps has a couple of hundred within a sixteen-square view cone,
    /// and this finds those without touching the rest. Cells are tested
    /// as bounding spheres against the two side planes of the frustum
    /// and against the distance limit, and only a surviving cell's
    /// clumps are looked at individually.
    ///
    /// The reference needs none of this: its decoration is one static
    /// mesh per material and Ogre culls it by bounding box on the GPU's
    /// terms.
    /// </summary>
    public void Collect(float camX, float camY, float angle, float halfFovX,
                        float maxDist, int maxCount, List<Renderer.Sprite> into)
    {
        if (_cells == null || maxDist <= 0f || maxCount <= 0) return;

        // Camera space: +depth straight ahead, lateral to the side. The
        // same rotation Renderer.DrawSprites uses, so a clump that
        // passes here lands where this expects it to.
        float ca = MathF.Cos(-angle), sa = MathF.Sin(-angle);

        // The side planes, as normals in (depth, lateral). A point is
        // outside the left plane when lateral*cos - depth*sin exceeds
        // zero, so a sphere of radius R is wholly outside when it
        // exceeds R. A little slack on the angle keeps a clump whose
        // billboard straddles the screen edge.
        float hf = MathF.Min(halfFovX + 0.15f, MathF.PI * 0.49f);
        float pc = MathF.Cos(hf), ps = MathF.Sin(hf);

        float cellR = CellSize * 0.70711f;             // half diagonal
        float maxD2 = maxDist * maxDist;
        // A clump is one billboard wide, so its own bounding radius is
        // half of that plus its height above the floor.
        const float ClumpR = ClumpSize;

        int r = (int)(maxDist / CellSize) + 1;
        int ccx = (int)MathF.Floor(camX / CellSize) - _cx0;
        int ccy = (int)MathF.Floor(camY / CellSize) - _cy0;

        _order.Clear();
        for (int cy = Math.Max(0, ccy - r); cy <= Math.Min(_cny - 1, ccy + r); cy++)
        for (int cx = Math.Max(0, ccx - r); cx <= Math.Min(_cnx - 1, ccx + r); cx++)
        {
            int ix = cy * _cnx + cx;
            if (_cells[ix] == null) continue;

            float mx = (_cx0 + cx + 0.5f) * CellSize - camX;
            float my = (_cy0 + cy + 0.5f) * CellSize - camY;
            float d2 = mx * mx + my * my;
            if (d2 > (maxDist + cellR) * (maxDist + cellR)) continue;

            float depth = mx * ca - my * sa;
            float lat = mx * sa + my * ca;
            if (depth < -cellR) continue;                       // behind us
            if (lat * pc - depth * ps > cellR) continue;         // off the left
            if (-lat * pc - depth * ps > cellR) continue;        // off the right
            _order.Add((d2, ix));
        }
        if (_order.Count == 0) return;
        // Nearest first, so a cap can only ever cost the furthest grass.
        _order.Sort((a, b) => a.d2.CompareTo(b.d2));

        foreach (var (_, ix) in _order)
        {
            foreach (Renderer.Sprite s in _cells[ix])
            {
                float dx = s.X - camX, dy = s.Y - camY;
                float d2 = dx * dx + dy * dy;
                if (d2 > maxD2) continue;
                float depth = dx * ca - dy * sa;
                float lat = dx * sa + dy * ca;
                if (depth < -ClumpR) continue;
                if (lat * pc - depth * ps > ClumpR) continue;
                if (-lat * pc - depth * ps > ClumpR) continue;
                into.Add(s);
            }
            if (into.Count >= maxCount) return;
        }
    }
}
