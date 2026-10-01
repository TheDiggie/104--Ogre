using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Drawing2D;
using Meridian59.Files;
using Meridian59.Files.BGF;

/// <summary>
/// Bridges Meridian 59's asset formats to Godot textures.
///
/// BGF frames are palette-indexed bytes; this maps them through
/// ColorTransformation.DefaultPalette into RGBA8 and wraps the result in
/// an ImageTexture, caching by (file, frame). Palette index 254 is the
/// transparency colour and already carries alpha 0 in the palette, so it
/// converts straight to a transparent pixel.
///
/// Pixel layout is top-down row-major with no stride padding - verified
/// against splash.bgf, which has readable text.
/// </summary>
public sealed class M59Assets
{
    public ResourceManager Resources { get; } = new ResourceManager();
    public string ResourceDir { get; private set; }
    public bool Ready { get; private set; }
    public string Error { get; private set; }

    /// <summary>
    /// True when Error is a thrown exception rather than a folder the
    /// player can go and choose again. The two want different
    /// treatment: one is a bug report, the other is a prompt.
    /// </summary>
    public bool Threw { get; private set; }

    readonly Dictionary<string, ImageTexture> _cache = new Dictionary<string, ImageTexture>();

    /// <summary>
    /// Points the resource manager at an installed client's resource folder.
    /// Returns false and sets Error if the folder is not usable.
    /// </summary>
    public bool Init(string resourceDir)
    {
        ResourceDir = resourceDir;
        if (string.IsNullOrWhiteSpace(resourceDir) || !Directory.Exists(resourceDir))
        {
            Error = $"Resource folder not found:\n{resourceDir}";
            Threw = false;
            return false;
        }

        try
        {
            // Everything lives flat in the resource folder in a normal install.
            Resources.Init(resourceDir, resourceDir, resourceDir, resourceDir,
                           resourceDir, resourceDir, resourceDir);
            Ready = true;
            return true;
        }
        catch (Exception e)
        {
            // The whole trace, not the message. On a phone this line IS
            // the debugger: the first Android build died here with
            // "PlatformNotSupportedException: Operation is not supported
            // on this platform." and nothing to say which call inside
            // ResourceManager.Init had thrown it, which cost a build
            // cycle and a guess. A message without a stack is a riddle.
            Error = e.ToString();
            Threw = true;
            return false;
        }
    }

    /// <summary>Texture for a room texture number (the XXXXX in grdXXXXX).</summary>
    public ImageTexture RoomTexture(ushort grdNumber, int frame = 0)
    {
        if (!Ready || grdNumber == 0) return null;
        string key = $"grd:{grdNumber}:{frame}";
        if (_cache.TryGetValue(key, out ImageTexture cached)) return cached;

        BgfFile bgf = null;
        try { bgf = Resources.GetRoomTexture(grdNumber); }
        catch (Exception e) { GD.PrintErr($"[M59Assets] grd{grdNumber}: {e.Message}"); }

        ImageTexture tex = FromBgf(bgf, frame);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>Texture for an object/sprite BGF by file name, e.g. "duskrat.bgf".</summary>
    public ImageTexture Object(string bgfName, int frame = 0)
    {
        if (!Ready || string.IsNullOrEmpty(bgfName)) return null;
        string key = $"obj:{bgfName}:{frame}";
        if (_cache.TryGetValue(key, out ImageTexture cached)) return cached;

        BgfFile bgf = null;
        try { bgf = Resources.GetObject(bgfName); }
        catch (Exception e) { GD.PrintErr($"[M59Assets] {bgfName}: {e.Message}"); }

        ImageTexture tex = FromBgf(bgf, frame);
        _cache[key] = tex;
        return tex;
    }

    /// <summary>Converts one BGF frame to a Godot texture, or null if it cannot.</summary>
    public static ImageTexture FromBgf(BgfFile bgf, int frame)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        if (frame < 0 || frame >= bgf.Frames.Count) frame = 0;

        BgfBitmap f = bgf.Frames[frame];
        byte[] idx;
        try
        {
            // Frames below BGF version 10 use a codec that only exists in
            // Windows x86 builds; those throw and are skipped.
            idx = f.IsCompressed ? f.Decompress(f.PixelData) : f.PixelData;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[M59Assets] frame {frame}: {e.Message}");
            return null;
        }

        int w = (int)f.Width, h = (int)f.Height;
        if (w <= 0 || h <= 0 || idx == null || idx.Length < w * h) return null;

        uint[] pal = ColorTransformation.DefaultPalette;
        var rgba = new byte[w * h * 4];
        for (int i = 0, n = w * h; i < n; i++)
        {
            uint argb = pal[idx[i]];
            int o = i * 4;
            rgba[o]     = (byte)(argb >> 16);
            rgba[o + 1] = (byte)(argb >> 8);
            rgba[o + 2] = (byte)argb;
            rgba[o + 3] = (byte)(argb >> 24);
        }

        Image img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// A composed picture as a Godot texture. <see cref="M59Compose"/>
    /// builds those - a whole object rather than one frame - and this is
    /// the only step that turns one into something the UI can draw.
    /// </summary>
    public static ImageTexture FromTex(Tex t)
    {
        if (t == null || t.W <= 0 || t.H <= 0 || t.P == null) return null;

        var rgba = new byte[t.W * t.H * 4];
        for (int i = 0, n = t.W * t.H; i < n; i++)
        {
            uint argb = t.P[i];
            int o = i * 4;
            rgba[o]     = (byte)(argb >> 16);
            rgba[o + 1] = (byte)(argb >> 8);
            rgba[o + 2] = (byte)argb;
            rgba[o + 3] = (byte)(argb >> 24);
        }

        Image img = Image.CreateFromData(t.W, t.H, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(img);
    }

    public int CachedTextures => _cache.Count;
}
