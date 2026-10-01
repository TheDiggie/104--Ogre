using System;
using System.Collections.Generic;

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
    /// <paramref name="t"/> is the seconds clock wrapped at 100, which
    /// is what `time_0_x 100.0` gives the shader (general.material:96).
    /// </summary>
    public static uint Shade(Tex tex, float wx, float wy, float wz,
                             float ex, float ey, float ez,
                             float waveX, float waveY, float t, float ambient,
                             bool ceiling = false)
    {
        if (tex == null) return 0xFF000000u;

        const float NoiseSpeed = 0.1f;
        const float ScaleX = 0.012f, ScaleY = 0.005f, ScaleZ = 0.03f;

        // Ogre object space: (roomX, height, roomY) * 0.0625.
        float px = wx * 0.0625f, py = wz * 0.0625f, pz = wy * 0.0625f;

        float u = px * ScaleX + waveX * t;
        float w = pz * ScaleZ + waveY * t;
        // The already-advected z term feeds the v coordinate, which is
        // what makes floor water flow diagonally. It reads like a typo
        // and is not one: general.hlsl:274.
        float v = py * ScaleY + w + NoiseSpeed * t;

        WaterNoise.Sample(u, v, out float nr, out float ng, out float nb);

        float bx = (2f * nr - 1f) * 0.15f;
        float by = 0.8f * MathF.Abs(2f * ng - 1f) + 0.2f;
        float bz = (2f * nb - 1f) * 0.15f;

        // The face normal, which the shader takes as an interpolated
        // vertex attribute and this renderer knows outright: straight up
        // in Ogre's axes for a floor, straight down for a ceiling
        // (RooSubSector.cs:519, :536 give the same two).
        float face = ceiling ? -1f : 1f;
        float nx = bx, ny = face + by, nz = bz;
        float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 1e-6f) len = 1f;
        nx /= len; ny /= len; nz /= len;

        // View direction, camera to surface, in the same axes.
        float vx = px - ex * 0.0625f, vy = py - ez * 0.0625f, vz = pz - ey * 0.0625f;
        float vlen = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (vlen < 1e-6f) vlen = 1f;
        vx /= vlen; vy /= vlen; vz /= vlen;

        float d = 2f * (vx * nx + vy * ny + vz * nz);
        float rx = vx - d * nx, ry = vy - d * ny;

        // The bitmap is indexed by the reflection vector's x and y, with
        // clamped addressing - the material's texunit1 says
        // tex_address_mode clamp (general.material:414-445).
        uint texel = tex.Sample(Clamp01(rx), Clamp01(ry), 1f);

        // Only the room's ambient multiplies it. No lights, no
        // colour modifier: `pixel = float4(ambient, 0) * reflcol`.
        return Renderer.Shade(texel | 0xFF000000u, ambient);
    }

    static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
