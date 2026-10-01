using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// Enough of a PNG reader for the art this client reads directly: eight
/// bits a channel, greyscale, palette, RGB or RGBA, not interlaced,
/// which covers all thirty skybox cube faces and all 443 replacement
/// room textures. Godot could decode them, but the check tools run
/// headless and the renderer is shared with them.
/// </summary>
public static class M59Png
{
    /// <summary>
    /// Decodes to ARGB, or null if the file is not a PNG this
    /// understands. Never throws for a malformed file - the caller
    /// falls back to the original art.
    /// </summary>
    public static uint[] Read(string path, out int W, out int H)
    {
        W = H = 0;
        byte[] d;
        try { d = File.ReadAllBytes(path); } catch { return null; }
        if (d.Length < 8 || d[0] != 0x89 || d[1] != 'P') return null;

        int bpp = 0, colour = -1;
        byte[] plte = null, trns = null;
        var idat = new MemoryStream();
        for (int i = 8; i + 8 <= d.Length; )
        {
            int len = (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
            string type = "" + (char)d[i + 4] + (char)d[i + 5] + (char)d[i + 6] + (char)d[i + 7];
            int at = i + 8;
            if (len < 0 || at + len > d.Length) return null;
            if (type == "IHDR")
            {
                W = (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];
                H = (d[at + 4] << 24) | (d[at + 5] << 16) | (d[at + 6] << 8) | d[at + 7];
                int depth = d[at + 8]; colour = d[at + 9];
                int interlace = d[at + 12];
                if (depth != 8 || interlace != 0) return null;
                switch (colour)
                {
                    case 0: bpp = 1; break;          // greyscale
                    case 2: bpp = 3; break;          // rgb
                    case 3: bpp = 1; break;          // palette index
                    case 4: bpp = 2; break;          // grey and alpha
                    case 6: bpp = 4; break;          // rgba
                    default: return null;
                }
            }
            else if (type == "PLTE") { plte = new byte[len]; Array.Copy(d, at, plte, 0, len); }
            else if (type == "tRNS") { trns = new byte[len]; Array.Copy(d, at, trns, 0, len); }
            else if (type == "IDAT") idat.Write(d, at, len);
            else if (type == "IEND") break;
            i = at + len + 4;
        }
        if (W <= 0 || H <= 0 || bpp == 0) return null;
        if (colour == 3 && plte == null) return null;

        byte[] raw;
        try
        {
            idat.Position = 2;                       // past the zlib header
            using var inf = new DeflateStream(idat, CompressionMode.Decompress);
            using var outp = new MemoryStream(H * (W * bpp + 1));
            inf.CopyTo(outp);
            raw = outp.ToArray();
        }
        catch { return null; }

        int stride = W * bpp;
        if (raw.Length < H * (stride + 1)) return null;
        var px = new uint[W * H];
        var line = new byte[stride];
        var prev = new byte[stride];
        int o = 0;
        for (int y = 0; y < H; y++)
        {
            byte filter = raw[o++];
            Buffer.BlockCopy(raw, o, line, 0, stride); o += stride;
            for (int p = 0; p < stride; p++)
            {
                int a = p >= bpp ? line[p - bpp] : 0;
                int b = prev[p];
                int c = p >= bpp ? prev[p - bpp] : 0;
                int add;
                switch (filter)
                {
                    case 0: add = 0; break;
                    case 1: add = a; break;
                    case 2: add = b; break;
                    case 3: add = (a + b) >> 1; break;
                    default:
                        int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
                        add = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                        break;
                }
                line[p] = (byte)(line[p] + add);
            }
            for (int x = 0; x < W; x++)
            {
                int p = x * bpp;
                uint al = 0xFF, r, g, b;
                switch (colour)
                {
                    case 0: r = g = b = line[p]; break;
                    case 4: r = g = b = line[p]; al = line[p + 1]; break;
                    case 3:
                    {
                        int ix = line[p] * 3;
                        if (ix + 2 >= plte.Length) { r = g = b = 0; break; }
                        r = plte[ix]; g = plte[ix + 1]; b = plte[ix + 2];
                        if (trns != null && line[p] < trns.Length) al = trns[line[p]];
                        break;
                    }
                    case 6: al = line[p + 3]; r = line[p]; g = line[p + 1]; b = line[p + 2]; break;
                    default: r = line[p]; g = line[p + 1]; b = line[p + 2]; break;
                }
                px[y * W + x] = (al << 24) | (r << 16) | (g << 8) | b;
            }
            var t = prev; prev = line; line = t;
        }
        return px;
    }
}
