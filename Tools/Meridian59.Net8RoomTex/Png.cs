using System;using System.IO;using System.IO.Compression;

/// <summary>
/// Minimal RGBA PNG writer. No dependencies - System.IO.Compression gives
/// raw deflate, and the zlib header and adler32 are added by hand.
/// </summary>
static class Png
{
    public static void Write(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // colour type: RGBA
        Chunk(fs, "IHDR", ihdr);

        // filter byte 0 in front of every scanline
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0, o = 0; y < h; y++)
        {
            raw[o++] = 0;
            Buffer.BlockCopy(rgba, y * w * 4, raw, o, w * 4);
            o += w * 4;
        }
        Chunk(fs, "IDAT", Zlib(raw));
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x01);              // zlib header
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
            ds.Write(data, 0, data.Length);
        uint a = 1, b = 0;
        foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));  ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length);
        s.Write(len, 0, 4);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, body, 4, data.Length);
        s.Write(body, 0, body.Length);
        var crc = new byte[4]; BE(crc, 0, (int)Crc32(body));
        s.Write(crc, 0, 4);
    }

    static void BE(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static uint[] crcTable;
    static uint Crc32(byte[] data)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                crcTable[n] = c;
            }
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte v in data) crc = crcTable[(crc ^ v) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
