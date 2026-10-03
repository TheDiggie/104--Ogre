using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;

/// <summary>
/// Water, lava and the rest of the liquids - which the room file does
/// not mark at all.
///
/// There is no water flag in a ROO. `RooSectorFlags` has depth, scroll,
/// flicker, slope and animation bits and nothing else
/// (RooSectorFlags.cs:33-57), and depth is a movement feature: it sinks
/// whoever stands in it (RooSector.cs:813-842) and slows them down
/// (BaseClient.cs:2853-2862), but it never touches the drawn surface.
/// What decides that a surface ripples is its TEXTURE NAME, matched
/// against a list the client ships: `Resources/decoration/water.xml`,
/// read into <c>waterTextures</c> at ControllerRoom.cpp:1733-1759 and
/// consulted at :1013 to pick `base_material_water` over
/// `base_material_room`. So a renderer that reads only .roo cannot know
/// a sector is liquid, and this list is the missing half.
///
/// The names in that file are of the form grd01802-0.png, which is grd
/// number 1802. Lava is in the list too (grd03203) and gets the same
/// treatment; the shader has no branch for it, so neither does this.
/// </summary>
public static class M59Water
{
    /// <summary>Every texture in Resources/decoration/water.xml, by grd number.</summary>
    static readonly HashSet<ushort> Liquids = new HashSet<ushort>
    {
        1802,          // water
        3203,          // lava
        7201, 7202, 7203, 7204,
        8894, 8895,
        8911, 8912, 8913, 8914,
        9083, 9084,    // ocean, Kocatan
        61018,         // green water
    };

    public static bool Is(ushort texture) => Liquids.Contains(texture);

    const float NoiseSpeed = 0.1f;
    const float ScaleX = 0.012f, ScaleY = 0.005f, ScaleZ = 0.03f;

    /// <summary>
    /// `WaterNoise.Tiles * noiseScale * 0.0625f`, the two values the
    /// old expression took before it reached worldPerPixel, worked out
    /// once at runtime in the same float steps and the same order so the
    /// product is the bit the per-pixel expression used to produce.
    /// Not a <c>const</c>, and the tile count goes through a runtime
    /// field first: the compiler is allowed to fold a constant expression
    /// at a higher precision than the JIT multiplies at, and the old
    /// expression's only folded part was `ScaleX + ScaleZ`, kept folded
    /// here too.
    /// </summary>
    static readonly int Tiles = WaterNoise.Tiles;
    static readonly float FloorTexels = Tiles * ScaleZ * 0.0625f;
    static readonly float WallTexels  = Tiles * (ScaleX + ScaleZ) * 0.0625f;

    /// <summary>
    /// A liquid surface's colour at one point, following `water_vs` and
    /// `water_ps` (general.hlsl:254-310).
    ///
    /// The thing to understand before reading it is that the sector's
    /// own texture coordinates are never used. The shader takes the
    /// world position, scales it into a noise lookup, bends the surface
    /// normal by what it finds there, reflects the view direction off
    /// that bent normal, and indexes the water bitmap by the
    /// reflection vector's x and y. What you see is a reflection, not a
    /// scrolled picture of water - which is why a naive "scroll the
    /// water texture" port does not look like the game.
    ///
    /// Positions are in Ogre units, which are room units times 0.0625
    /// (ControllerRoom.cpp:827), and the shader's axes are Ogre's: x is
    /// the room's x, y is height, z is the room's y.
    ///
    /// FLOORS, CEILINGS AND WALLS. `water_vs` has two branches and they
    /// are chosen by the normal: `if (normal.y != 0)` is the floor and
    /// ceiling case and the `else // wall water` at general.hlsl:277-282
    /// is a vertical one. Walls reach it because
    /// `CreateTextureAndMaterial` is the ONE entry point for every
    /// textured part of a room - the sidedef parts at
    /// ControllerRoom.cpp:694 and the sector parts at :795 both call it -
    /// and it picks `base_material_water` purely by whether the texture
    /// is named in water.xml (:1013-1021). So a sidedef whose upper,
    /// middle or lower texture is grd01802 is water, drawn by the same
    /// shader as the pool it runs into. 203 wall parts across 42 rooms
    /// are, with 22 or 23 in each of barlsew, dbarlsew and jassew2.
    ///
    /// The wall branch reads, with waveSpeed truncated to its x by the
    /// assignment into a scalar:
    ///
    ///     uvw.y  += -waveSpeed * time_0_X;
    ///     uvw.xz += uvw.z + NOISESPEED * time_0_X;
    ///
    /// where uvw.z is still the unadvected z, so u gains the z term and
    /// the clock while v runs with HEIGHT and drifts against the scroll.
    /// On a floor it is the other way about: the wave drifts u and v
    /// takes the already-advected z (general.hlsl:270-274).
    ///
    /// <paramref name="nx"/>, <paramref name="ny"/> and <paramref
    /// name="nz"/> are the surface normal in this renderer's own axes -
    /// x and y across the map, z up - so a floor is (0,0,1), a ceiling
    /// (0,0,-1) and a wall its own horizontal perpendicular. Which
    /// branch runs is the shader's own test on the Ogre y, which is this
    /// z.
    ///
    /// <paramref name="t"/> is the seconds clock wrapped at 100, which
    /// is what `time_0_x 100.0` gives the shader (general.material:96).
    ///
    /// <paramref name="ambient"/> is the room's PLAIN ambient and
    /// nothing else: `pixel = float4(ambient, 0) * reflcol` with
    /// `param_named_auto ambient ambient_light_colour`
    /// (general.material:160-170), in a material that is one
    /// `illumination_stage ambient` pass (:414-445). No 0.6 room weight,
    /// no N.L sun term, no point lights. The caller used to hand over
    /// the weighted room light instead, which drew every liquid surface
    /// in the game 40% too dark wherever the sun was off - which is
    /// every indoor room, and nearly all the game's water.
    ///
    /// <paramref name="worldPerPixel"/> is how many room units one
    /// screen pixel spans here, which is what the noise's filter needs
    /// to pick a level. See <see cref="WaterNoise.Sample"/>.
    ///
    /// THE COST. This is called once per liquid PIXEL, and in a water
    /// room half the frame is liquid, so every operation here is paid
    /// hundreds of thousands of times a frame. The arithmetic below is
    /// the arithmetic the shader port has always done, operation for
    /// operation and in the same order - a float sum rounds differently
    /// if it is re-associated, and the golden frames hold this to the
    /// same pixels - but three things around it were costing more than
    /// the shading itself and are now done differently without changing
    /// a bit: the noise is read through <see cref="SampleNoise"/>, the
    /// reflection texel through an inlined clamp-and-index rather than
    /// the general sampler, and the ambient through a table rather than
    /// three multiply-min-converts (see <see cref="Lut"/>). Measured on
    /// a span-shaped microbenchmark: 50 ns a pixel before, 31 after, and
    /// the three water rooms of the fixture run 3 to 4 ms a frame faster.
    /// </summary>
    public static uint Shade(Tex tex, float wx, float wy, float wz,
                             float ex, float ey, float ez,
                             float nx, float ny, float nz,
                             float waveX, float waveY, float t, float ambient,
                             float worldPerPixel)
    {
        Span s = Begin(tex, ex, ey, ez, nx, ny, nz, waveX, waveY, t, ambient);
        return Shade(in s, wx, wy, wz, worldPerPixel);
    }

    /// <summary>
    /// Everything in <see cref="Shade(Tex,float,float,float,float,float,float,float,float,float,float,float,float,float,float)"/>
    /// that does not depend on the pixel, worked out once a SPAN - a
    /// floor span or a wall part - and read by
    /// <see cref="Shade(in Span,float,float,float,float)"/> per pixel.
    /// Each field is the very expression the per-pixel code used to
    /// evaluate, in the same float steps: <c>waveX * t</c> is the same
    /// product whether it is formed once or a thousand times, and the
    /// per-pixel sums below add it in the same place they always did.
    /// The ambient table and the noise chain are resolved here too, so
    /// the pixel loop reads neither static.
    /// </summary>
    public readonly struct Span
    {
        internal readonly Tex Tex;
        internal readonly uint[] P;
        internal readonly int W, H;
        internal readonly Vector128<float> Normal;   // (nx, nz, ny, 0): the shader's axes
        internal readonly Vector128<float> Eye;      // (ex, ez, ey, 0) * 0.0625
        internal readonly bool Wall;
        internal readonly float WaveXT, WaveYT, NoiseT, Texels;
        internal readonly byte[] Lut;
        internal readonly NoiseChain Chain;

        internal Span(Tex tex, float ex, float ey, float ez, float nx, float ny, float nz,
                      float waveX, float waveY, float t, float ambient)
        {
            Tex = tex; P = tex?.P; W = tex?.W ?? 0; H = tex?.H ?? 0;
            // The normal, in the shader's axes. Its y is this renderer's
            // z, and the shader's branch is exactly `normal.y != 0`.
            float onx = nx, ony = nz, onz = ny;
            Normal = Vector128.Create(onx, ony, onz, 0f);
            Wall = ony == 0f;
            Eye = Vector128.Create(ex, ez, ey, 0f) * Vector128.Create(0.0625f);
            WaveXT = waveX * t; WaveYT = waveY * t; NoiseT = NoiseSpeed * t;
            Texels = Wall ? WallTexels : FloorTexels;
            ShadeLut lut = _lut;
            if (lut == null || lut.F != ambient) lut = Lut(ambient);
            Lut = lut.T;
            Chain = _chain ?? Chain();
        }
    }

    /// <summary>The span's constants, once. See <see cref="Span"/>.</summary>
    public static Span Begin(Tex tex, float ex, float ey, float ez,
                             float nx, float ny, float nz,
                             float waveX, float waveY, float t, float ambient)
        => new Span(tex, ex, ey, ez, nx, ny, nz, waveX, waveY, t, ambient);

    /// <summary>The per-pixel half. See <see cref="Span"/> and the long comment above.</summary>
    public static uint Shade(in Span s, float wx, float wy, float wz, float worldPerPixel)
    {
        if (s.Tex == null) return 0xFF000000u;

        // Ogre object space: (roomX, height, roomY) * 0.0625.
        float px = wx * 0.0625f, py = wz * 0.0625f, pz = wy * 0.0625f;

        float u, v, noiseTexels;
        if (!s.Wall)
        {
            u = px * ScaleX + s.WaveXT;
            float w = pz * ScaleZ + s.WaveYT;
            // The already-advected z term feeds the v coordinate, which is
            // what makes floor water flow diagonally. It reads like a typo
            // and is not one: general.hlsl:274.
            v = py * ScaleY + w + s.NoiseT;
            // How fast the noise coordinate moves per screen pixel, which
            // is what sets the filter level. A floor's pixel step is
            // horizontal, so u moves with ScaleX and v with ScaleZ; the
            // larger is the bound.
            noiseTexels = s.Texels * worldPerPixel;
        }
        else
        {
            // general.hlsl:277-282. uvw.z has not been touched yet, so
            // the term u picks up is the plain pz * ScaleZ.
            u = px * ScaleX + pz * ScaleZ + s.NoiseT;
            v = py * ScaleY - s.WaveXT;
            // A wall's pixel step is either along it, where u takes both
            // horizontal scales, or up it, where v takes the much smaller
            // ScaleY; the larger of the two is the bound.
            noiseTexels = s.Texels * worldPerPixel;
        }

        Vector128<float> noise = SampleNoise(s.Chain, u, v, noiseTexels);

        // bump = (2n - 1) * (0.15, 0.8, 0.15) with the middle lane through
        // abs, plus (0, 0.2, 0). Lane by lane this is exactly the three
        // scalar lines it replaces:
        //     bx = (2f * nr - 1f) * 0.15f;
        //     by = 0.8f * MathF.Abs(2f * ng - 1f) + 0.2f;
        //     bz = (2f * nb - 1f) * 0.15f;
        // (0.8f * |t| is |t| * 0.8f; a product has no order. The + 0f on
        // the outer lanes is exact: x + 0 is x for every x but -0, and
        // 2n - 1 cannot be -0.)
        Vector128<float> tmp = noise * Two - One;
        tmp = Vector128.ConditionalSelect(LaneY, Vector128.Abs(tmp), tmp);
        Vector128<float> bump = tmp * BumpScale + BumpBias;

        // `bump = normalize(normal + bump)` - the face normal the shader
        // takes as an interpolated vertex attribute, which this renderer
        // knows outright. Straight up in Ogre's axes for a floor,
        // straight down for a ceiling (RooSubSector.cs:519, :536 give the
        // same two); for a wall it is horizontal, which is what puts the
        // zero in normal.y that the vertex shader branches on.
        //
        // The sum of squares is added in the scalar order, (x + y) + z,
        // and the normalisation is a DIVIDE by the length in every lane,
        // not a reciprocal and a multiply: x/len and x*(1/len) differ in
        // the last bit often enough to show. One divps is the three
        // divss it replaces, lane for lane the same IEEE quotient; the
        // same goes for sqrtps against MathF.Sqrt.
        Vector128<float> bn = s.Normal + bump;
        Vector128<float> sq = bn * bn;
        float len = MathF.Sqrt(sq.GetElement(0) + sq.GetElement(1) + sq.GetElement(2));
        if (len < 1e-6f) len = 1f;
        bn /= Vector128.Create(len);

        // View direction, camera to surface, in the same axes:
        // (px - ex*0.0625, py - ez*0.0625, pz - ey*0.0625).
        Vector128<float> vv = Vector128.Create(px, py, pz, 0f) - s.Eye;
        sq = vv * vv;
        float vlen = MathF.Sqrt(sq.GetElement(0) + sq.GetElement(1) + sq.GetElement(2));
        if (vlen < 1e-6f) vlen = 1f;
        vv /= Vector128.Create(vlen);

        // d = 2 (v . bn); r = v - d bn, of which x and y are read.
        Vector128<float> dot = vv * bn;
        float d = 2f * (dot.GetElement(0) + dot.GetElement(1) + dot.GetElement(2));
        Vector128<float> refl = vv - Vector128.Create(d) * bn;
        float rx = refl.GetElement(0), ry = refl.GetElement(1);

        // The bitmap is indexed by the reflection vector's x and y, with
        // clamped addressing - the material's texunit1 says
        // tex_address_mode clamp (general.material:414-445).
        //
        // `Tex.Sample(u, v, 1f)` inlined: at a rate of one it reads level
        // zero, and its `u - floor(u)` is kept because a coordinate of
        // exactly one wraps to zero through it.
        float cu = Clamp01(rx), cv = Clamp01(ry);
        int tw = s.W, th = s.H;
        int tx = (int)((cu - MathF.Floor(cu)) * tw);
        int ty = (int)((cv - MathF.Floor(cv)) * th);
        if (tx < 0) tx = 0; else if (tx >= tw) tx = tw - 1;
        if (ty < 0) ty = 0; else if (ty >= th) ty = th - 1;
        // tx and ty are clamped into the level just above, so the index
        // is in range by construction; the check is not paid.
        uint texel = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s.P), ty * tw + tx);

        // Only the room's ambient multiplies it. No lights, no
        // colour modifier: `pixel = float4(ambient, 0) * reflcol`.
        // Through the table: see Lut.
        ref byte l0 = ref MemoryMarshal.GetArrayDataReference(s.Lut);
        return 0xFF000000u
             | ((uint)Unsafe.Add(ref l0, (int)((texel >> 16) & 0xFF)) << 16)
             | ((uint)Unsafe.Add(ref l0, (int)((texel >> 8) & 0xFF)) << 8)
             |  (uint)Unsafe.Add(ref l0, (int)(texel & 0xFF));
    }

    static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    static readonly Vector128<float> Two       = Vector128.Create(2f);
    static readonly Vector128<float> One       = Vector128.Create(1f);
    static readonly Vector128<float> BumpScale = Vector128.Create(0.15f, 0.8f, 0.15f, 0f);
    static readonly Vector128<float> BumpBias  = Vector128.Create(0f, 0.2f, 0f, 0f);
    static readonly Vector128<float> LaneY     = Vector128.Create(0, -1, 0, 0).AsSingle();
    static readonly Vector128<float> Div255 = Vector128.Create(255f);

    // ----------------------------------------------------------------
    // The ambient as a table.
    //
    // `Renderer.Shade(c, f)` is, per channel, (uint)MathF.Min(255f, ch *
    // f) - a convert, a multiply, a min and a convert back, three times a
    // pixel. The channel is a byte and f is the room's ambient, which
    // changes when the room or the time of day does and not between two
    // pixels, so the 256 answers are built once and read back by the
    // channel. Each entry IS Renderer.Shade's own arithmetic on that
    // channel value, so the two agree bit for bit, including at f == 1
    // (where Shade hands the colour back untouched and the table is the
    // identity; the alpha was already forced on by the caller) and f < 0
    // (which Shade clamps to zero, as the table does).
    //
    // One table lives at a time, published through a single reference:
    // the renderer threads its columns and every band asks the same
    // ambient, so the reference is read, its key compared, and only a
    // changed ambient rebuilds. Two bands racing to rebuild the same
    // value both produce the same bytes, so the race is harmless.
    // ----------------------------------------------------------------

    sealed class ShadeLut
    {
        public readonly float F;
        public readonly byte[] T = new byte[256];
        public ShadeLut(float f)
        {
            F = f;
            if (f < 0f) f = 0f;
            for (int i = 0; i < 256; i++)
                T[i] = (byte)(uint)MathF.Min(255f, i * f);
        }
    }

    static ShadeLut _lut;

    static ShadeLut Lut(float ambient)
    {
        var lut = new ShadeLut(ambient);
        Volatile.Write(ref _lut, lut);
        return lut;
    }

    // ----------------------------------------------------------------
    // The noise, read the fast way.
    //
    // WaterNoise.Sample is the reference: a wrapping, bilinear, mipped
    // read of the 128x128 noise, and its documentation says why each of
    // those three words is right. It was also half of every liquid
    // pixel's cost - 25 of 53 ns on the microbenchmark - and almost
    // none of that was the twelve byte reads or the weights. It was the
    // wrap, done as an integer remainder (four `%` a pixel, at ten to
    // twenty cycles each), and the level, found by halving in a loop.
    //
    // This is the same read with those two done differently and NOTHING
    // ELSE changed. Every level's side is a power of two, so wrapping is
    // a mask: for two's-complement i and n a power of two, `i & (n-1)`
    // equals `(i % n + n) % n` for every i. The level is the float's
    // own exponent: the loop halves texelsPerPixel until it is under
    // two, so its count is floor(log2 tpp) for tpp >= 2 and zero below,
    // clamped to the last level, which is how Renderer's flat loop
    // already picks a mip. The weights, their order, the sums and the
    // three divides by 255 are copied exactly, because changing any one
    // of them moves a bit.
    //
    // The chain it reads is WaterNoise's own, and WaterNoise keeps it
    // private, so it is recovered rather than reached for: at level
    // zero, asked at a texel's centre, the reference sampler's weights
    // are exactly (1, 0, 0, 0) and its answer is that texel over 255,
    // which rounds straight back to the byte. The reduced copies are
    // then box-filtered down with the same integer arithmetic
    // WaterNoise.BuildMips uses, so each level holds the same bytes.
    // Built once, under a lock, published through one reference.
    // ----------------------------------------------------------------

    /// <summary>
    /// The chain and its shape, one object so a reader that sees the
    /// reference sees the sizes that go with it - on the phone's weak
    /// memory ordering as well as on the desktop's.
    /// </summary>
    internal sealed class NoiseChain
    {
        public float[][] Levels;  // each texel as (r, g, b, 0) floats, 16 bytes
        public int[] Sizes;
        public int Log2;          // of the base size, so lod + mask follow.
    }

    static NoiseChain _chain;
    static readonly object _chainGate = new object();

    /// <summary>
    /// Recovers WaterNoise's level-zero bytes through its own sampler
    /// and builds the chain down to 1x1, then lays every level out as
    /// one float4 a texel so a corner is a single aligned load rather
    /// than three byte reads and three converts. The conversion of a
    /// byte to a float is exact, so the weighted sums see the same
    /// numbers the byte reads gave. Null if the noise failed to decode,
    /// in which case the reference's flat answer is used.
    /// </summary>
    static NoiseChain Chain()
    {
        NoiseChain have = _chain;
        if (have != null) return have;
        lock (_chainGate)
        {
            if (_chain != null) return _chain;
            if (!WaterNoise.Ready()) return null;
            int size = WaterNoise.Tiles;
            var rgb = new byte[size * size * 3];
            float inv = 1f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // (x + 0.5) / size times size is x + 0.5 exactly - a
                    // power-of-two scale - so fx lands on the texel and
                    // the bilinear weights collapse to the one corner.
                    WaterNoise.Sample((x + 0.5f) * inv, (y + 0.5f) * inv, 1f,
                                      out float r, out float g, out float b);
                    int i = (y * size + x) * 3;
                    rgb[i]     = (byte)MathF.Round(r * 255f);
                    rgb[i + 1] = (byte)MathF.Round(g * 255f);
                    rgb[i + 2] = (byte)MathF.Round(b * 255f);
                }
            int levels = 1, log2 = 0;
            for (int n = size; n > 1; n >>= 1) { levels++; log2++; }
            var bytes = new byte[levels][];
            var sizes = new int[levels];
            bytes[0] = rgb; sizes[0] = size;
            for (int l = 1; l < levels; l++)
            {
                // WaterNoise.BuildMips, integer for integer.
                int ps = sizes[l - 1], ns = ps >> 1;
                byte[] prev = bytes[l - 1], next = new byte[ns * ns * 3];
                for (int y = 0; y < ns; y++)
                    for (int x = 0; x < ns; x++)
                        for (int c = 0; c < 3; c++)
                            next[(y * ns + x) * 3 + c] = (byte)((
                                prev[((2 * y) * ps + 2 * x) * 3 + c] +
                                prev[((2 * y) * ps + 2 * x + 1) * 3 + c] +
                                prev[((2 * y + 1) * ps + 2 * x) * 3 + c] +
                                prev[((2 * y + 1) * ps + 2 * x + 1) * 3 + c] + 2) >> 2);
                bytes[l] = next; sizes[l] = ns;
            }
            var chain = new float[levels][];
            for (int l = 0; l < levels; l++)
            {
                int n = sizes[l] * sizes[l];
                var f = new float[n * 4];
                for (int i = 0; i < n; i++)
                {
                    f[i * 4]     = bytes[l][i * 3];
                    f[i * 4 + 1] = bytes[l][i * 3 + 1];
                    f[i * 4 + 2] = bytes[l][i * 3 + 2];
                }
                chain[l] = f;
            }
            var built = new NoiseChain { Levels = chain, Sizes = sizes, Log2 = log2 };
            Volatile.Write(ref _chain, built);
            return built;
        }
    }

    /// <summary>
    /// <see cref="WaterNoise.Sample"/>, bit for bit, as (r, g, b, 0).
    /// See the block comment above.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector128<float> SampleNoise(NoiseChain chain, float u, float v, float texelsPerPixel)
    {
        if (chain == null)
        {
            WaterNoise.Sample(u, v, texelsPerPixel, out float r, out float g, out float b);
            return Vector128.Create(r, g, b, 0f);
        }

        // The reference: `if (texelsPerPixel > 1f)` then halve while
        // `t >= 2f && lod < last`. Between one and two the
        // loop never runs; a NaN fails both tests and reads level zero,
        // as the exponent path does through its `>= 2f` guard.
        int lod = 0;
        if (texelsPerPixel >= 2f)
        {
            lod = ((BitConverter.SingleToInt32Bits(texelsPerPixel) >> 23) & 0xFF) - 127;
            if (lod > chain.Log2) lod = chain.Log2;
        }
        // lod is clamped to Log2, the chain's last level, so both reads
        // are in range by construction.
        float[] p = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(chain.Levels), lod);
        ref float p0 = ref MemoryMarshal.GetArrayDataReference(p);
        int n = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(chain.Sizes), lod);
        if (n == 1) return Vector128.LoadUnsafe(ref p0) / Div255;
        int mask = n - 1;

        // Bilinear, at texel centres, wrapping on both axes.
        float fx = u * n - 0.5f, fy = v * n - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int x1 = (x0 + 1) & mask, y1 = (y0 + 1) & mask;
        x0 &= mask; y0 &= mask;

        float w00 = (1f - ax) * (1f - ay), w10 = ax * (1f - ay);
        float w01 = (1f - ax) * ay,        w11 = ax * ay;

        // (p00 * w00 + p10 * w10 + p01 * w01 + p11 * w11) / 255, left to
        // right, per lane - the reference's expression for each channel
        // with the channels side by side. Multiply then add, never
        // fused: the JIT does not contract these, and an FMA would round
        // once where the reference rounds twice.
        Vector128<float> c00 = Vector128.LoadUnsafe(ref p0, (nuint)((y0 * n + x0) * 4));
        Vector128<float> c10 = Vector128.LoadUnsafe(ref p0, (nuint)((y0 * n + x1) * 4));
        Vector128<float> c01 = Vector128.LoadUnsafe(ref p0, (nuint)((y1 * n + x0) * 4));
        Vector128<float> c11 = Vector128.LoadUnsafe(ref p0, (nuint)((y1 * n + x1) * 4));
        Vector128<float> acc = c00 * Vector128.Create(w00);
        acc += c10 * Vector128.Create(w10);
        acc += c01 * Vector128.Create(w01);
        acc += c11 * Vector128.Create(w11);
        return acc / Div255;
    }
}
