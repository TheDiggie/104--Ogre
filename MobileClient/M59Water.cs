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
    /// </summary>
    public static uint Shade(Tex tex, float wx, float wy, float wz,
                             float ex, float ey, float ez,
                             float nx, float ny, float nz,
                             float waveX, float waveY, float t, float ambient,
                             float worldPerPixel)
    {
        if (tex == null) return 0xFF000000u;

        const float NoiseSpeed = 0.1f;
        const float ScaleX = 0.012f, ScaleY = 0.005f, ScaleZ = 0.03f;

        // Ogre object space: (roomX, height, roomY) * 0.0625.
        float px = wx * 0.0625f, py = wz * 0.0625f, pz = wy * 0.0625f;

        // The normal, in the shader's axes. Its y is this renderer's z,
        // and the shader's branch is exactly `normal.y != 0`.
        float onx = nx, ony = nz, onz = ny;
        bool wall = ony == 0f;

        float u, v;
        if (!wall)
        {
            u = px * ScaleX + waveX * t;
            float w = pz * ScaleZ + waveY * t;
            // The already-advected z term feeds the v coordinate, which is
            // what makes floor water flow diagonally. It reads like a typo
            // and is not one: general.hlsl:274.
            v = py * ScaleY + w + NoiseSpeed * t;
        }
        else
        {
            // general.hlsl:277-282. uvw.z has not been touched yet, so
            // the term u picks up is the plain pz * ScaleZ.
            u = px * ScaleX + pz * ScaleZ + NoiseSpeed * t;
            v = py * ScaleY - waveX * t;
        }

        // How fast the noise coordinate moves per screen pixel, which is
        // what sets the filter level. A floor's pixel step is horizontal,
        // so u moves with ScaleX and v with ScaleZ; a wall's can be
        // either along the wall, where u takes both horizontal scales, or
        // up it, where v takes the much smaller ScaleY. The larger of the
        // two is the bound in both cases.
        float noiseScale = wall ? ScaleX + ScaleZ : ScaleZ;
        float noiseTexels = WaterNoise.Tiles * noiseScale * 0.0625f * worldPerPixel;

        WaterNoise.Sample(u, v, noiseTexels, out float nr, out float ng, out float nb);

        float bx = (2f * nr - 1f) * 0.15f;
        float by = 0.8f * MathF.Abs(2f * ng - 1f) + 0.2f;
        float bz = (2f * nb - 1f) * 0.15f;

        // `bump = normalize(normal + bump)` - the face normal the shader
        // takes as an interpolated vertex attribute, which this renderer
        // knows outright. Straight up in Ogre's axes for a floor,
        // straight down for a ceiling (RooSubSector.cs:519, :536 give the
        // same two); for a wall it is horizontal, which is what puts the
        // zero in normal.y that the vertex shader branches on.
        float bnx = onx + bx, bny = ony + by, bnz = onz + bz;
        float len = MathF.Sqrt(bnx * bnx + bny * bny + bnz * bnz);
        if (len < 1e-6f) len = 1f;
        bnx /= len; bny /= len; bnz /= len;

        // View direction, camera to surface, in the same axes.
        float vx = px - ex * 0.0625f, vy = py - ez * 0.0625f, vz = pz - ey * 0.0625f;
        float vlen = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (vlen < 1e-6f) vlen = 1f;
        vx /= vlen; vy /= vlen; vz /= vlen;

        float d = 2f * (vx * bnx + vy * bny + vz * bnz);
        float rx = vx - d * bnx, ry = vy - d * bny;

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
