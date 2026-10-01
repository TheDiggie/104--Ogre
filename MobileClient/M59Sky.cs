using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// The sky behind everything that was never drawn.
///
/// A room does not say whether it is outdoors. The ROO carries no
/// backdrop and no outdoor bit - RooInfoFlags has three depth-override
/// bits and nothing else (RoomInfoFlags.cs:30-33) - so a sector is open
/// to the sky exactly when its ceiling texture is 0, which leaves it
/// with no resource and no material (RooSector.cs:663-691). The same is
/// true of a wall part: an upper or lower with texture 0 gets no
/// material either (RooSideDef.cs:343-350), and the Ogre client's
/// CreateSectorPart and CreateWallPart both return early rather than
/// build geometry for it (ControllerRoom.cpp:789-790 and :688-690).
/// There is no "this pixel is sky" test anywhere: the sky is simply
/// whatever is behind the holes, so this renderer paints it in the same
/// places it used to paint Tex.Void.
///
/// WHICH sky comes from the server, not the room. RoomInfo carries a
/// backgroundFileRID (RoomInfo.cs:171-172) resolved to a BGF name
/// (:806-807), and BP_CHANGE_BACKGROUND (150) replaces it mid-session
/// (BackgroundMessage.cs:24-25, DataController.cs:2371-2375). The Ogre
/// client never samples that BGF: it uses the name as a key for one of
/// five cubic skybox materials (ControllerRoom.cpp:1327-1343, names at
/// ControllerRoom.h:67-71, materials at Constants.h:100-106), whose
/// faces ship as Resources/sky/{skya,skyb,skyc,skyd,redsky}_{fr,bk,lf,
/// rt,up,dn}.png (sky.material:1-89). So do we - the cube faces are the
/// art, and the BGF name only picks the set.
///
/// DIVERGENCE: the reference's default is not the skybox at all but
/// Caelum's procedural dome, with the skybox kept for
/// DisableNewSky (ControllerRoom.cpp:1346-1349, default false per
/// OgreClientConfig.h:52). A procedural sky with a clock, cloud layers
/// and a starfield is a renderer of its own; the cubic skybox is the
/// path the same client still ships and its art is shipped with it, so
/// that is the one mirrored here.
///
/// The faces are lit at full brightness and untouched by distance,
/// matching `lighting off` on every sky material (sky.material:7-8,
/// 25-26, 43-44, 61-62, 79-80) and the fact that the Ogre client sets
/// no fog at all (ControllerRoom.cpp:139-142).
/// </summary>
public sealed class M59Sky
{
    // Face order as the directions below index it.
    const int Rt = 0, Lf = 1, Up = 2, Dn = 3, Fr = 4, Bk = 5;
    static readonly string[] Suffix = { "rt", "lf", "up", "dn", "fr", "bk" };

    readonly uint[][] _face = new uint[6][];
    readonly int[] _w = new int[6];
    readonly int[] _h = new int[6];

    /// <summary>The set this was built from, e.g. "skya". For logging.</summary>
    public string Name { get; private set; }

    M59Sky() { }

    /// <summary>
    /// The BGF name the server sent to one of the five shipped sets, by
    /// the same Contains tests the reference uses
    /// (ControllerRoom.cpp:1327-1343). A name that matches none of them
    /// gets no sky, exactly as the reference leaves the skybox off.
    /// </summary>
    public static string SetFor(string bgfFile)
    {
        if (string.IsNullOrEmpty(bgfFile)) return null;
        if (bgfFile.Contains("skya.bgf")) return "skya";
        if (bgfFile.Contains("skyb.bgf")) return "skyb";
        if (bgfFile.Contains("skyc.bgf")) return "skyc";
        if (bgfFile.Contains("skyd.bgf")) return "skyd";
        if (bgfFile.Contains("redsky.bgf")) return "redsky";
        return null;
    }

    /// <summary>
    /// Loads the six faces of a set. <paramref name="dir"/> is a folder
    /// holding them; returns null if any face is missing or unreadable,
    /// because a cube with a hole in it is worse than no sky.
    /// </summary>
    public static M59Sky Load(string dir, string set)
    {
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(set)) return null;
        var s = new M59Sky { Name = set };
        for (int i = 0; i < 6; i++)
        {
            string p = Path.Combine(dir, set + "_" + Suffix[i] + ".png");
            if (!File.Exists(p)) return null;
            try { s._face[i] = M59Png.Read(p, out s._w[i], out s._h[i]); }
            catch { return null; }
            if (s._face[i] == null) return null;
        }
        return s;
    }

    /// <summary>
    /// The folders a sky set might live in, given the game's resource
    /// folder. An installed client keeps them under its own
    /// Resources/sky; the check tools run against a bare resource dump
    /// and find them in the repository instead.
    /// </summary>
    public static string FindDir(string resourceDir)
    {
        foreach (string c in Candidates(resourceDir))
            if (c != null && Directory.Exists(c) && File.Exists(Path.Combine(c, "skya_fr.png")))
                return c;
        return null;
    }

    static System.Collections.Generic.IEnumerable<string> Candidates(string resourceDir)
    {
        string env = Environment.GetEnvironmentVariable("M59SKY");
        if (!string.IsNullOrEmpty(env)) yield return env;
        if (!string.IsNullOrEmpty(resourceDir))
        {
            yield return Path.Combine(resourceDir, "sky");
            string up = Path.GetDirectoryName(resourceDir.TrimEnd(Path.DirectorySeparatorChar));
            if (up != null)
            {
                yield return Path.Combine(up, "sky");
                yield return Path.Combine(up, "Resources", "sky");
            }
        }
        // The check tools run from the repository against a bare
        // resource dump that has no sky folder of its own.
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Resources", "sky");
        yield return Path.Combine(AppContext.BaseDirectory, "sky");
    }

    /// <summary>
    /// The colour of the sky in a direction, given in this renderer's
    /// world axes: x and y across the map, z up.
    ///
    /// The Ogre scene puts the map's X on its own X and the map's Y on
    /// its Z, with height on Y (V3.ConvertToWorld, V3.cs:346-351), so
    /// the cube is anchored to the world the same way and a turn of the
    /// camera moves the sky by exactly that turn.
    ///
    /// The face layout is not quoted from a convention; it is read off
    /// the shipped art. Laid out as a cross - lf, fr, rt, bk across the
    /// middle with up above fr and dn below it - the horizon runs
    /// unbroken through all four side faces and the poles meet their
    /// neighbours without a rotation, so the faces are stored in the
    /// orientation that cross implies, and the coordinates below are
    /// derived from it.
    /// </summary>
    public uint Sample(float x, float y, float z)
    {
        // Into the Ogre axes the art was authored in: right, up, back.
        float ox = x, oy = z, oz = y;
        float ax = MathF.Abs(ox), ay = MathF.Abs(oy), az = MathF.Abs(oz);

        int f; float u, v;
        if (ax >= ay && ax >= az)
        {
            if (ox > 0) { float d = ax; f = Rt; u = 0.5f + 0.5f * (oz / d); v = 0.5f - 0.5f * (oy / d); }
            else        { float d = ax; f = Lf; u = 0.5f - 0.5f * (oz / d); v = 0.5f - 0.5f * (oy / d); }
        }
        else if (ay >= az)
        {
            if (oy > 0) { float d = ay; f = Up; u = 0.5f + 0.5f * (ox / d); v = 0.5f - 0.5f * (oz / d); }
            else        { float d = ay; f = Dn; u = 0.5f + 0.5f * (ox / d); v = 0.5f - 0.5f * (oz / d); }
        }
        else
        {
            if (oz < 0) { float d = az; f = Fr; u = 0.5f + 0.5f * (ox / d); v = 0.5f - 0.5f * (oy / d); }
            else        { float d = az; f = Bk; u = 0.5f - 0.5f * (ox / d); v = 0.5f - 0.5f * (oy / d); }
        }

        // The materials clamp (sky.material), so a coordinate that lands
        // a hair outside a face from rounding takes its edge texel
        // rather than wrapping to the far side of the picture.
        int w = _w[f], h = _h[f];
        int sx = (int)(u * w); if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
        int sy = (int)(v * h); if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
        return _face[f][sy * w + sx];
    }

}
