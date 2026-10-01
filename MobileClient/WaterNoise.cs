using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// The noise the water shader reads, carried in the source.
///
/// `base_material_water` binds Resources/shader/noise.dds as its first
/// texture unit (general.material:414-445) and `water_ps` samples it to
/// bend the surface normal (general.hlsl:298-302). Without it there is
/// no ripple, so it has to reach the phone somehow, and a loose PNG in
/// the project would need Godot's import step to have run - which the
/// headless export that builds the APK does not do. Embedded, it is
/// simply there, and it decodes with nothing but the base class
/// library, so the offline render checks can use it too.
///
/// Halved from the original 256 to 128 on each side. The noise is
/// sampled across roughly 83 world units per tile on one axis and 33 on
/// the other, at a render height of 432 pixels; the second half of the
/// detail was never going to reach a screen.
/// </summary>
public static class WaterNoise
{
    const int Size = 128;

    /// <summary>The noise's side, in texels - what one tile of u or v spans.</summary>
    public const int Tiles = Size;

    /// <summary>Raw 128x128 RGB, deflated.</summary>
    const string Packed =
        "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";

    static byte[] _rgb;
    static bool _tried;

    /// <summary>
    /// The reduced copies. Level 0 is <see cref="_rgb"/> itself, each
    /// one after it half the size, down to 1x1 - eight levels at 128,
    /// a third again as much memory as the base.
    /// </summary>
    static byte[][] _mips;
    static int[] _mipSize;

    /// <summary>Decodes once. False means the caller draws flat water.</summary>
    public static bool Ready()
    {
        if (_tried) return _rgb != null;
        _tried = true;
        try
        {
            byte[] packed = Convert.FromBase64String(Packed);
            var outBuf = new byte[Size * Size * 3];
            using (var ms = new MemoryStream(packed))
            using (var z = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < outBuf.Length)
                {
                    int n = z.Read(outBuf, got, outBuf.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got != outBuf.Length) return false;
            }
            _rgb = outBuf;
            BuildMips();
        }
        catch { _rgb = null; _mips = null; }
        return _rgb != null;
    }

    /// <summary>
    /// Box-filters the chain down to 1x1, once, when the noise decodes.
    /// Eagerly, for the same reason <c>M59Compose</c> builds its mips
    /// eagerly: a lazy chain is state that two threads can race on, and
    /// the renderer walks columns on every core it has.
    /// </summary>
    static void BuildMips()
    {
        int levels = 1;
        for (int n = Size; n > 1; n >>= 1) levels++;
        _mips = new byte[levels][];
        _mipSize = new int[levels];
        _mips[0] = _rgb; _mipSize[0] = Size;
        for (int l = 1; l < levels; l++)
        {
            int ps = _mipSize[l - 1], ns = ps >> 1;
            byte[] prev = _mips[l - 1], next = new byte[ns * ns * 3];
            for (int y = 0; y < ns; y++)
                for (int x = 0; x < ns; x++)
                    for (int c = 0; c < 3; c++)
                        next[(y * ns + x) * 3 + c] = (byte)((
                            prev[((2 * y) * ps + 2 * x) * 3 + c] +
                            prev[((2 * y) * ps + 2 * x + 1) * 3 + c] +
                            prev[((2 * y + 1) * ps + 2 * x) * 3 + c] +
                            prev[((2 * y + 1) * ps + 2 * x + 1) * 3 + c] + 2) >> 2);
            _mips[l] = next; _mipSize[l] = ns;
        }
    }

    /// <summary>
    /// A WRAPPING, BILINEAR, MIPPED sample - which is `filtering
    /// bilinear`, Ogre's default, and that is what this unit asks for.
    ///
    /// Addressing first, because that part has not changed: the water
    /// material names no `tex_address_mode` for its noise unit
    /// (general.material:434-438), so it is Ogre's default WRAP, and
    /// the noise is read over tens of tiles - the u coordinate alone
    /// advances 0.012 per Ogre unit, a tile every 83. Clamping it would
    /// make one enormous smear. Its neighbour, the diffuse unit, is
    /// `tex_address_mode clamp` on purpose, being indexed by a
    /// reflection vector; the two are not the same question.
    ///
    /// FILTERING, which this used not to do. The unit names no
    /// `filtering` either, so it takes Ogre's default - linear min,
    /// linear mag, point mip - and that is deliberate in this file
    /// rather than an oversight: `base_material_invisible` DOES write a
    /// line for its own copy of noise.dds, `filtering linear linear
    /// none` (general.material:400-403), which is the same bilinear
    /// with the mip chain switched off. So the water's noise is
    /// bilinear AND mipped, and the only sampler in the shipped
    /// materials that is point-sampled is none of them.
    ///
    /// It was fair to ask whether this renderer should follow, since it
    /// point-samples the room's own art (`Tex.Sample`) and the comment
    /// here used to say so. The answer is that the art and the noise are
    /// different kinds of texture. Room art is palette-indexed with a
    /// key colour: index 254 reaches the renderer as alpha 0, and
    /// blending it with its neighbours bleeds the key into the picture -
    /// which is why `Tex` keys every reduced copy rather than averaging
    /// through it. The noise is not art and has no key: it is a vector
    /// field, three channels of a bump that the shader normalizes, and
    /// averaging two of its texels is exactly what the hardware does.
    /// It is also one sample per liquid pixel, not one per pixel of the
    /// room, so the cost lands only where liquid is drawn.
    ///
    /// What it was doing instead was reading one texel of a 128-wide
    /// noise at whatever rate the surface happened to be advancing,
    /// which on a floor seen at a distance is several texels per screen
    /// pixel: adjacent pixels got uncorrelated bumps, so the reflection
    /// jumped, and the surface read as salt and pepper rather than as
    /// ripple.
    ///
    /// MEASURED, on swamp1 from the room's largest leaf facing 45
    /// degrees, as the share of horizontally adjacent liquid pixels
    /// differing by more than 32/255 in green, with the liquid held at
    /// the same brightness either side so the ambient fix does not move
    /// the figure:
    ///
    ///     point-sampled, no mips   18.4%
    ///     bilinear + mips          15.7%
    ///     no bump at all            8.5%   <- the floor of this metric
    ///     plain tiled texture       5.0%
    ///
    /// So the noise's own contribution fell by a bit over a quarter, from
    /// 9.9 points of contrast to 7.2. The 8.5% that is left when the bump
    /// is removed entirely is NOT the noise: it is the diffuse unit,
    /// which `Tex.Sample` reads nearest at level 0 the way this renderer
    /// reads all room art. base_material_water names no `filtering` for
    /// that unit either, so the reference bilinear-filters it too; that
    /// is the renderer's standing art-sampling divergence rather than
    /// this one, and it is left alone here.
    ///
    /// <paramref name="texelsPerPixel"/> is how many noise texels one
    /// screen pixel spans; the level is picked from it the way
    /// <c>Tex.Sample</c> picks one, so the two samplers do not disagree
    /// about what "a pixel wide" means.
    /// </summary>
    public static void Sample(float u, float v, float texelsPerPixel,
                              out float r, out float g, out float b)
    {
        if (_rgb == null) { r = g = 0.5f; b = 1f; return; }

        byte[] p = _rgb;
        int n = Size;
        if (_mips != null && texelsPerPixel > 1f)
        {
            int lod = 0;
            float t = texelsPerPixel;
            while (t >= 2f && lod < _mips.Length - 1) { t *= 0.5f; lod++; }
            p = _mips[lod]; n = _mipSize[lod];
        }
        if (n == 1) { r = p[0] / 255f; g = p[1] / 255f; b = p[2] / 255f; return; }

        // Bilinear, at texel centres, wrapping on both axes. The half
        // texel is what makes it a filter of the four texels AROUND the
        // sample point rather than of the four down and to the right of
        // it, which would shift the whole field by half a texel.
        float fx = u * n - 0.5f, fy = v * n - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int x1 = Wrap(x0 + 1, n), y1 = Wrap(y0 + 1, n);
        x0 = Wrap(x0, n); y0 = Wrap(y0, n);

        int i00 = (y0 * n + x0) * 3, i10 = (y0 * n + x1) * 3;
        int i01 = (y1 * n + x0) * 3, i11 = (y1 * n + x1) * 3;
        float w00 = (1f - ax) * (1f - ay), w10 = ax * (1f - ay);
        float w01 = (1f - ax) * ay,        w11 = ax * ay;

        r = (p[i00] * w00 + p[i10] * w10 + p[i01] * w01 + p[i11] * w11) / 255f;
        g = (p[i00 + 1] * w00 + p[i10 + 1] * w10 + p[i01 + 1] * w01 + p[i11 + 1] * w11) / 255f;
        b = (p[i00 + 2] * w00 + p[i10 + 2] * w10 + p[i01 + 2] * w01 + p[i11 + 2] * w11) / 255f;
    }

    static int Wrap(int i, int n)
    {
        i %= n;
        return i < 0 ? i + n : i;
    }
}
