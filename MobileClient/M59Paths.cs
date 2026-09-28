using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// Works out where the game's resource folder is, per platform, and gets
/// it onto the device if it shipped inside the export.
///
/// The library's ResourceManager reads with System.IO, so it needs a real
/// directory. On desktop that is the installed client's own resource
/// folder. On Android nothing outside the app is readable and res:// lives
/// inside the .pck rather than on disk, so the files have to be copied out
/// to user:// once, with Godot's FileAccess, before the library can see
/// them.
/// </summary>
public static class M59Paths
{
    /// <summary>Where resources are unpacked to when they ship in the export.</summary>
    public const string UserResource = "user://resource";
    /// <summary>Where they sit inside the export, if bundled.</summary>
    public const string PackedResource = "res://resource";

    /// <summary>
    /// Resource folder to hand to the library, or null if none was found.
    /// <paramref name="preferred"/> is an inspector override and wins.
    /// </summary>
    public static string Resolve(string preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred))
            return preferred;

        foreach (string c in Candidates())
            if (Directory.Exists(c) && HasContent(c)) return c;

        return null;
    }

    static bool HasContent(string dir)
    {
        try
        {
            foreach (string _ in Directory.EnumerateFiles(dir, "*.roo")) return true;
            foreach (string _ in Directory.EnumerateFiles(dir, "*.bgf")) return true;
        }
        catch { }
        return false;
    }

    static IEnumerable<string> Candidates()
    {
        // Unpacked or user-supplied copy, on every platform.
        yield return ProjectSettings.GlobalizePath(UserResource);

        // An installed Windows client.
        string local = null;
        try
        {
            local = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
        }
        catch { }
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, "Meridian-104", "resource");
            yield return Path.Combine(local, "Meridian59", "resource");
        }

        string home = null;
        try { home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile); }
        catch { }
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".meridian-104", "resource");

        // Exported next to the executable, the way a portable build ships.
        string exe = OS.GetExecutablePath();
        if (!string.IsNullOrEmpty(exe))
        {
            string d = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(d)) yield return Path.Combine(d, "resource");
        }
    }

    /// <summary>
    /// Copies res://resource out to user://resource if it shipped in the
    /// export and has not been unpacked yet. Returns the number of files
    /// written, 0 if there was nothing to do, -1 on failure.
    ///
    /// Called before Resolve on first run. Reading res:// needs Godot's
    /// FileAccess - inside an APK these are entries in the .pck, not files
    /// on disk, and System.IO cannot see them at all.
    /// </summary>
    public static int UnpackIfNeeded(Action<string> progress = null)
    {
        string dest = ProjectSettings.GlobalizePath(UserResource);
        if (Directory.Exists(dest) && HasContent(dest)) return 0;

        using var src = DirAccess.Open(PackedResource);
        if (src == null) return 0;                    // nothing bundled

        try { Directory.CreateDirectory(dest); }
        catch (Exception e) { GD.PrintErr($"[M59Paths] {dest}: {e.Message}"); return -1; }

        string[] names = src.GetFiles();
        int written = 0;
        foreach (string name in names)
        {
            // The exporter renames imported files; raw game data is passed
            // through untouched, so anything with .import is not ours.
            if (name.EndsWith(".import", StringComparison.OrdinalIgnoreCase)) continue;

            using Godot.FileAccess f = Godot.FileAccess.Open(
                $"{PackedResource}/{name}", Godot.FileAccess.ModeFlags.Read);
            if (f == null) { GD.PrintErr($"[M59Paths] could not read {name}"); continue; }

            try
            {
                File.WriteAllBytes(Path.Combine(dest, name), f.GetBuffer((long)f.GetLength()));
                written++;
                if (progress != null && (written % 25 == 0 || written == names.Length))
                    progress($"unpacking {written}/{names.Length}");
            }
            catch (Exception e) { GD.PrintErr($"[M59Paths] {name}: {e.Message}"); }
        }

        GD.Print($"[M59Paths] unpacked {written} files to {dest}");
        return written;
    }

    /// <summary>A sentence to show when nothing was found, naming the places looked.</summary>
    public static string NotFoundMessage()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("No Meridian resource folder found. Looked in:");
        foreach (string c in Candidates()) sb.AppendLine("  " + c);
        sb.AppendLine();
        sb.AppendLine("Copy an installed client's 'resource' folder to:");
        sb.AppendLine("  " + ProjectSettings.GlobalizePath(UserResource));
        return sb.ToString();
    }
}
