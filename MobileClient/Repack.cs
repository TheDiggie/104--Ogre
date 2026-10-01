using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// The last thing that happens to a frame: the renderer's ARGB words
/// turned into the RGBA bytes Godot's texture wants.
///
/// This is not a hot loop because it is clever; it is a hot loop
/// because it runs over every pixel of every frame and does almost
/// nothing to each one. At 1280x1080 that is 1.38 million iterations a
/// frame, four byte stores each, and on a phone the per-pixel work is
/// dwarfed by the loop itself.
///
/// So it is done sixteen bytes at a time. An ARGB uint in memory on a
/// little-endian machine is B,G,R,A, and the destination wants
/// R,G,B,255 - which is one byte shuffle per four pixels, plus the
/// alpha written as a constant. <c>Vector128.Shuffle</c> maps to
/// PSHUFB on x86 and TBL on ARM, and an index outside the vector
/// yields zero, which is what makes the alpha lane free: it comes out
/// zero and is then OR'd to 255, or, in the inverted case, subtracted
/// from 255 and so lands on 255 by itself.
///
/// The scalar path is kept and is what runs where no vector unit is
/// claimed. Both are checked against each other in
/// Tools/Meridian59.Net8RenderCheck -- repack.
/// </summary>
public static class Repack
{
    /// <summary>B,G,R per pixel pulled into R,G,B; the alpha lane asks
    /// for byte 255, which does not exist, so it comes out zero.</summary>
    static readonly Vector128<byte> Pick = Vector128.Create(
        (byte)2, 1, 0, 255, 6, 5, 4, 255, 10, 9, 8, 255, 14, 13, 12, 255);

    static readonly Vector128<byte> Opaque = Vector128.Create(
        (byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    static readonly Vector128<byte> Ones = Vector128.Create((byte)255);

    /// <summary>
    /// Writes <paramref name="count"/> pixels of <paramref name="src"/>
    /// into <paramref name="dst"/> as RGBA. With <paramref name="flip"/>
    /// the three colour channels are inverted - the reference's
    /// Invert_ps, one minus the picture with the alpha left alone.
    /// </summary>
    public static void ToRgba(uint[] src, byte[] dst, int count, bool flip)
    {
        if (src == null || dst == null) return;
        if (count > src.Length) count = src.Length;
        if (count * 4 > dst.Length) count = dst.Length / 4;
        if (count <= 0) return;

        // The generic Vector128.Shuffle is NOT an intrinsic in .NET 8
        // unless the index vector is a compile-time constant, and a
        // static readonly field is not one: it compiled to a software
        // loop that was slower than the scalar code it replaced, which
        // is the whole reason the measurement is in the oracle. The
        // platform calls below are intrinsics unconditionally - PSHUFB
        // on x86, TBL on ARM - and both already answer zero for an
        // index outside the vector, which is what the alpha lane wants.
        bool wide128 = Ssse3.IsSupported || AdvSimd.Arm64.IsSupported;

        int i = 0;
        if (wide128)
        {
            // Four pixels a step. The tail is whatever does not divide.
            // Read and written through Unsafe rather than through a
            // Span per step. Vector128.Create(ReadOnlySpan) and
            // CopyTo(Span) each bounds-check and re-form the span every
            // iteration, which on this loop cost MORE than the scalar
            // path it was meant to replace - measured, not assumed:
            // 21.3 ms against 13.2 ms for a 1280x1080 frame.
            int wide = count & ~3;
            ref byte s0 = ref Unsafe.As<uint, byte>(ref MemoryMarshal.GetArrayDataReference(src));
            ref byte d0 = ref MemoryMarshal.GetArrayDataReference(dst);
            for (; i < wide; i += 4)
            {
                Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(
                    ref Unsafe.Add(ref s0, i * 4));
                Vector128<byte> s = Ssse3.IsSupported
                    ? Ssse3.Shuffle(v, Pick)
                    : AdvSimd.Arm64.VectorTableLookup(v, Pick);
                // Inverted: 255 - channel, and 255 - 0 is the opaque
                // alpha this wants anyway, so nothing else is needed.
                s = flip ? Vector128.Subtract(Ones, s) : (s | Opaque);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref d0, i * 4), s);
            }
        }

        for (; i < count; i++)
        {
            uint c = src[i];
            byte r = (byte)(c >> 16), g = (byte)(c >> 8), b = (byte)c;
            if (flip) { r = (byte)(255 - r); g = (byte)(255 - g); b = (byte)(255 - b); }
            int o = i * 4;
            dst[o] = r;
            dst[o + 1] = g;
            dst[o + 2] = b;
            dst[o + 3] = 255;
        }
    }

    /// <summary>The scalar path on its own, for the check tool to
    /// compare the vector one against.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ToRgbaScalar(uint[] src, byte[] dst, int count, bool flip)
    {
        for (int i = 0; i < count; i++)
        {
            uint c = src[i];
            byte r = (byte)(c >> 16), g = (byte)(c >> 8), b = (byte)c;
            if (flip) { r = (byte)(255 - r); g = (byte)(255 - g); b = (byte)(255 - b); }
            int o = i * 4;
            dst[o] = r;
            dst[o + 1] = g;
            dst[o + 2] = b;
            dst[o + 3] = 255;
        }
    }
}
