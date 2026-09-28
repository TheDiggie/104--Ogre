using System;using System.IO;
using Meridian59.Drawing2D;using Meridian59.Files.BGF;

// Decodes BGF sprite frames to PNG using only the ported core library.
// Proves the asset -> pixels path works off Windows, which is what any
// renderer sits on top of.
//
//   dotnet run --project Tools/Meridian59.Net8Export -- <file.bgf|dir> <outdir> [maxFrames]
//   dotnet run --project Tools/Meridian59.Net8Export -- --survey <dir>

static class Export
{
    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--survey") return Survey(args[1]);
        if (args.Length < 2) { Console.WriteLine("usage: <file.bgf|dir> <outdir> [maxFrames]   |   --survey <dir>"); return 2; }

        string src = args[0], outDir = args[1];
        int max = args.Length > 2 ? int.Parse(args[2]) : 8;
        Directory.CreateDirectory(outDir);

        string[] files = Directory.Exists(src)
            ? Directory.GetFiles(src, "*.bgf", SearchOption.AllDirectories)
            : new[] { src };

        int wrote = 0, skipped = 0;
        foreach (string f in files)
        {
            BgfFile bgf;
            try { bgf = new BgfFile(f); }
            catch (Exception e) { Console.WriteLine($"  ! {Path.GetFileName(f)}: {e.Message}"); skipped++; continue; }

            string stem = Path.GetFileNameWithoutExtension(f);
            for (int i = 0; i < bgf.Frames.Count && i < max; i++)
            {
                try
                {
                    string p = Path.Combine(outDir, files.Length == 1 ? $"{stem}_{i:D3}.png" : $"{stem}_{i:D3}.png");
                    WriteFrame(bgf.Frames[i], p);
                    wrote++;
                }
                catch (Exception e) { Console.WriteLine($"  ! {stem} frame {i}: {e.Message}"); skipped++; }
            }
        }
        Console.WriteLine($"{wrote} PNG(s) written to {outDir}, {skipped} skipped");
        return wrote > 0 ? 0 : 1;
    }

    static void WriteFrame(BgfBitmap f, string path)
    {
        byte[] idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData;
        int w = (int)f.Width, h = (int)f.Height;
        if (idx.Length < w * h) throw new Exception($"short pixel data: {idx.Length} < {w * h}");

        uint[] pal = ColorTransformation.DefaultPalette;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            // Pixel data is plain top-down, row-major, no stride padding.
            // (The "upside-down" note in BgfBitmap refers to BMP export,
            // which is a different layout - verified against splash.bgf.)
            int srcRow = y * w;
            for (int x = 0; x < w; x++)
            {
                uint argb = pal[idx[srcRow + x]];
                int o = (y * w + x) * 4;
                rgba[o]     = (byte)(argb >> 16);   // R
                rgba[o + 1] = (byte)(argb >> 8);    // G
                rgba[o + 2] = (byte)argb;           // B
                rgba[o + 3] = (byte)(argb >> 24);   // A
            }
        }
        Png.Write(path, w, h, rgba);
    }

    static int Survey(string dir)
    {
        var byVersion = new System.Collections.Generic.SortedDictionary<int, int>();
        int frames = 0, crush = 0, files = 0;
        foreach (string f in Directory.GetFiles(dir, "*.bgf", SearchOption.AllDirectories))
        {
            BgfFile b;
            try { b = new BgfFile(f); } catch { continue; }
            files++;
            int v = (int)b.Version;
            byVersion.TryGetValue(v, out int c); byVersion[v] = c + 1;
            foreach (var fr in b.Frames) { frames++; if (fr.IsCompressed && v <= 9) crush++; }
        }
        Console.WriteLine($"{files} BGF files, {frames} frames");
        foreach (var kv in byVersion) Console.WriteLine($"  version {kv.Key}: {kv.Value} files");
        Console.WriteLine($"  frames needing the Windows-only CRUSH codec: {crush}");
        return 0;
    }
}
