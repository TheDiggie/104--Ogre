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
    /// <summary>A folder the player pointed us at, remembered between runs.</summary>
    public const string SavedPathFile = "user://resource-path.txt";

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
        // The same content check the candidates and Remember both
        // apply. Without it a folder that merely exists was accepted,
        // and an empty one took the client all the way to the login
        // screen before anything went wrong - which reads as a broken
        // client rather than the wrong folder.
        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred)
            && HasContent(preferred))
            return preferred;

        foreach (string c in Candidates())
            if (Directory.Exists(c) && HasContent(c)) return c;

        return null;
    }

    /// <summary>
    /// Whether a folder looks like a resource folder at all.
    ///
    /// This is the right question to ask of a folder somebody handed us
    /// - Resolve's candidates, and whatever is typed into
    /// ResourcePrompt - and it is emphatically NOT the question to ask of
    /// our own copy while it is being made. See UnpackMarker for the
    /// months of blank world that answer cost.
    /// </summary>
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

    /// <summary>
    /// Remembers a folder the player chose, so being told once is enough.
    /// Returns false if it does not look like a resource folder.
    /// </summary>
    public static bool Remember(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        dir = dir.Trim().Trim('"');
        if (!Directory.Exists(dir) || !HasContent(dir)) return false;

        try
        {
            using Godot.FileAccess f = Godot.FileAccess.Open(SavedPathFile, Godot.FileAccess.ModeFlags.Write);
            if (f == null) return true;          // usable now, just not remembered
            f.StoreString(dir);
        }
        catch (Exception e) { GD.PrintErr($"[M59Paths] could not remember {dir}: {e.Message}"); }
        return true;
    }

    static string Saved()
    {
        try
        {
            using Godot.FileAccess f = Godot.FileAccess.Open(SavedPathFile, Godot.FileAccess.ModeFlags.Read);
            return f?.GetAsText()?.Trim();
        }
        catch { return null; }
    }

    static IEnumerable<string> Candidates()
    {
        // A folder the player pointed us at on an earlier run wins: they
        // know where their install is and we evidently did not.
        string saved = Saved();
        if (!string.IsNullOrWhiteSpace(saved)) yield return saved;

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
    /// The completion marker, written LAST and nowhere else.
    ///
    /// It used to be that "the folder exists and holds one .roo or .bgf"
    /// was the whole test - see HasContent, which is still the right
    /// question to ask of a folder the PLAYER pointed us at, and was
    /// entirely the wrong one to ask of our own copy. The folder is
    /// created first and the files are written in pack order, so that
    /// test turned true a second or two into a copy that takes minutes:
    /// ~470MB out of the .pck. Background the app, run the battery down
    /// or fill the disk in that window and the next launch believed the
    /// job was done. The player then got a working login screen and a
    /// blank world behind a live HUD, because RenderFrame draws nothing
    /// without a room (GameView.cs:3274 returns before the room is ever
    /// built) - and not one word about why. Clearing app data was the
    /// only way out and nothing said so.
    ///
    /// So the marker is a separate file, written after the last byte of
    /// the last file, and it carries the stamp of the pack it came from
    /// (see PackStamp) so a new APK's data actually replaces the old.
    /// Its absence means "unfinished", which is a thing we can resume.
    ///
    /// It lives inside the unpacked folder deliberately: clearing app
    /// data or deleting the folder takes the marker with it, and there
    /// is no way to end up with a marker that outlives its files.
    /// </summary>
    public const string UnpackMarker = "user://resource/.unpacked";

    /// <summary>
    /// Free space we insist on having left over, on top of what the copy
    /// itself needs. A device with nothing to spare is a device that will
    /// fail at some other write later on, and Android's own housekeeping
    /// starts killing things well before zero.
    /// </summary>
    // Shared with ResourceSync, which fills the same folder from the
    // network and owes the player the same refusal before the first byte.
    internal const long Headroom = 32L * 1024 * 1024;

    /// <summary>
    /// How an unpack turned out. A bool could not say the two things
    /// the caller has to tell apart: nothing happened because nothing
    /// needed to (Ok, Written 0) and nothing happened because the
    /// device refused (Problem set).
    ///
    /// The old signature returned an int - files written, 0 for nothing
    /// to do, -1 for failure - and the caller threw it away
    /// (GameView.Boot called it for its side effects), so a -1 from a
    /// failed CreateDirectory and a disk that filled up on file two
    /// both went to the login screen as though all was well.
    /// </summary>
    public sealed class UnpackReport
    {
        /// <summary>True when the data is on disk and the marker is written.</summary>
        public bool Ok;
        /// <summary>Files written by this run.</summary>
        public int Written;
        /// <summary>Files an earlier run had already written correctly.</summary>
        public int Resumed;
        /// <summary>Bytes written by this run.</summary>
        public long Bytes;
        /// <summary>A sentence for the player, or null when Ok.</summary>
        public string Problem;
        /// <summary>
        /// Files the exporter converted, so their originals are not in
        /// the pack at all and cannot be copied out. See LostSources.
        /// </summary>
        public List<string> Lost = new List<string>();
    }

    // One entry per real file in the bundled pack, gathered once. The
    // stamp needs the whole list and so does the copy, and walking the
    // .pck twice to get the same answer twice would only give them a
    // chance to disagree.
    sealed class PackEntry
    {
        public string Pack;      // res://resource/... as FileAccess wants it
        public string Rel;       // path under the destination, '/' separated
        public long Length;      // -1 if the entry could not even be opened
    }

    static List<PackEntry> _pack;
    static List<string> _lost;

    /// <summary>
    /// Every file in res://resource, subfolders included, plus the ones
    /// that are NOT there any more because Godot imported them.
    ///
    /// Both halves of this were bugs. The old walk was a single
    /// GetFiles() on the top level, so anything in a subfolder never
    /// reached the device - and the game's own resource folder does have
    /// subfolders (rooms, sounds, music, mails, strings, bgfobjects,
    /// bgftextures) even when a normal install keeps its files flat.
    ///
    /// The second half is worse and was established by exporting a pack
    /// and reading it rather than by reasoning about it. Godot IMPORTS
    /// .ogg, .wav and .png: the pack gets
    /// `.godot/imported/AMBCave.ogg-&lt;md5&gt;.oggvorbisstr` plus a
    /// 175-byte `resource/AMBCave.ogg.import`, and the original .ogg is
    /// not in the pack at ALL. DirAccess.GetFiles() on res://resource in
    /// an exported build then lists "AMBCave.ogg.import" and no
    /// "AMBCave.ogg" - so the old `.import` skip was skipping the only
    /// trace of the file, and every sound and every piece of music was
    /// missing on the phone with nothing anywhere saying so.
    ///
    /// The cure is the one MobileClient/sky/ already uses: an
    /// `importer="keep"` sidecar makes Godot pass the file through, and
    /// the same export then stores `res://resource/AMBCave.ogg` itself
    /// and no sidecar. (A `.gdignore` in the folder does NOT work - it
    /// takes the whole folder out of the export, include_filter and
    /// all; that was tried and measured.) stage-resource.sh writes those
    /// sidecars, and this walk records any surviving `.import` so a pack
    /// staged without it is reported instead of going quiet.
    /// </summary>
    static void Scan()
    {
        if (_pack != null) return;
        _pack = new List<PackEntry>();
        _lost = new List<string>();

        var sidecars = new List<string>();
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk("", sidecars, have);

        // A sidecar with no source beside it is a file the exporter ate.
        // On desktop, where res:// is a real folder, BOTH the source and
        // its .import are listed and this finds nothing, which is right.
        foreach (string s in sidecars)
        {
            string source = s.Substring(0, s.Length - ".import".Length);
            if (!have.Contains(source)) _lost.Add(source);
        }
    }

    static void Walk(string rel, List<string> sidecars, HashSet<string> have)
    {
        string dir = rel.Length == 0 ? PackedResource : $"{PackedResource}/{rel}";
        using var d = DirAccess.Open(dir);
        if (d == null) return;

        foreach (string name in d.GetFiles())
        {
            string child = rel.Length == 0 ? name : $"{rel}/{name}";

            if (name.EndsWith(".import", StringComparison.OrdinalIgnoreCase))
            {
                sidecars.Add(child);
                continue;
            }

            // Length now, from the pack, because the copy compares it
            // against what is already on disk to decide what to resume.
            // GetLength reads the .pck's own header; it does not read
            // the file.
            long len = -1;
            using (Godot.FileAccess f = Godot.FileAccess.Open(
                       $"{dir}/{name}", Godot.FileAccess.ModeFlags.Read))
                if (f != null) len = (long)f.GetLength();

            have.Add(child);
            _pack.Add(new PackEntry { Pack = $"{dir}/{name}", Rel = child, Length = len });
        }

        foreach (string sub in d.GetDirectories())
            Walk(rel.Length == 0 ? sub : $"{rel}/{sub}", sidecars, have);
    }

    /// <summary>
    /// What identifies this build's game data, for the marker to carry.
    ///
    /// The app version alone would not do: project.godot had no version
    /// at all and the Android preset's version/code was 1, so every
    /// build past the first shipped new resources to a device that would
    /// never look at them again. Content alone would not do either -
    /// two files swapped for two of the same size and count is not a
    /// stretch when the game's data is regenerated by tools.
    ///
    /// So it is both: the declared version, and the pack's own shape.
    /// Bump application/config/version and the data is replaced; forget
    /// to bump it and changed data still replaces itself.
    /// </summary>
    public static string PackStamp()
    {
        Scan();
        long total = 0;
        foreach (PackEntry e in _pack) if (e.Length > 0) total += e.Length;

        string ver = "";
        try { ver = ProjectSettings.GetSetting("application/config/version", "").AsString(); }
        catch { }

        return $"version={ver} files={_pack.Count} bytes={total}";
    }

    /// <summary>
    /// Names of files that shipped only as Godot's own converted copy,
    /// so their originals could not be unpacked. Empty is the healthy
    /// answer; anything in here is a packaging fault, not a device one,
    /// and on this client it means silence where there should be sound.
    /// </summary>
    public static List<string> LostSources()
    {
        Scan();
        return _lost;
    }

    static string ReadMarker()
    {
        try
        {
            using Godot.FileAccess f = Godot.FileAccess.Open(
                UnpackMarker, Godot.FileAccess.ModeFlags.Read);
            return f?.GetAsText()?.Trim();
        }
        catch { return null; }
    }

    /// <summary>
    /// Whether there is bundled game data still to be copied out. Cheap
    /// enough for boot: it walks the pack's directory entries and asks
    /// for their lengths, and reads no file contents.
    ///
    /// Note what it does NOT ask: whether the destination has a .roo in
    /// it. That question is what let a half-finished copy pass for a
    /// finished one. The only thing that says "finished" is the marker,
    /// and the only thing that says "still current" is its stamp.
    /// </summary>
    public static bool NeedsUnpack()
    {
        using (var src = DirAccess.Open(PackedResource))
            if (src == null) return false;            // nothing bundled

        string done = ReadMarker();
        return done == null || done != PackStamp();
    }

    /// <summary>
    /// Copies res://resource out to user://resource, resuming a copy an
    /// earlier run did not finish and replacing one an older build left
    /// behind. Returns what happened; see UnpackReport.
    ///
    /// Called before Resolve on first run. Reading res:// needs Godot's
    /// FileAccess - inside an APK these are entries in the .pck, not
    /// files on disk, and System.IO cannot see them at all.
    ///
    /// Three things are deliberate here:
    ///
    /// - Space is checked BEFORE the first byte. The old loop caught
    ///   each write, logged e.Message to logcat and carried on, so a
    ///   full disk produced one failure per file for hundreds of files,
    ///   left the part-written ones on disk, and still returned a
    ///   written count above zero. Nobody saw logcat and the caller
    ///   ignored the count.
    /// - A write that fails stops the run. Whatever stopped the first
    ///   file will stop the rest, and the honest thing to tell the
    ///   player is the first message, once, on the screen.
    /// - The marker goes on at the end, and only if nothing failed.
    /// </summary>
    public static UnpackReport UnpackIfNeeded(Action<string> progress = null)
    {
        var r = new UnpackReport();

        using (var src = DirAccess.Open(PackedResource))
            if (src == null) { r.Ok = true; return r; }    // nothing bundled

        Scan();
        r.Lost = _lost;

        string stamp = PackStamp();
        string was = ReadMarker();
        if (was == stamp) { r.Ok = true; return r; }       // already done, and current

        string dest = ProjectSettings.GlobalizePath(UserResource);
        try { Directory.CreateDirectory(dest); }
        catch (Exception e)
        {
            r.Problem = "The game data folder could not be created:\n" + dest +
                        "\n\n" + e.Message;
            return r;
        }

        // A marker that exists but reads differently is our own folder
        // from an older build, so files that build had and this one does
        // not are stale and go. Without a marker we cannot tell our own
        // half-copy from a folder the player filled in by hand, and
        // deleting a player's files on a guess is not a trade worth
        // making - the copy below overwrites what it needs either way.
        if (was != null) Prune(dest);

        // What is still missing, or is there at the wrong length because
        // the process died in the middle of writing it. Skipping the
        // ones that are already right is what makes an interrupted
        // 470MB copy cost minutes instead of starting over.
        var todo = new List<PackEntry>();
        long need = 0;
        foreach (PackEntry e in _pack)
        {
            string path = Path.Combine(dest, e.Rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && e.Length >= 0 && fi.Length == e.Length) { r.Resumed++; continue; }
            }
            catch { }
            todo.Add(e);
            if (e.Length > 0) need += e.Length;
        }

        // GetSpaceLeft returns 0 when the platform cannot answer, which
        // is not the same as "no space" - refusing to install on a
        // device that merely declines to say would be worse than the bug
        // this is here to prevent.
        long free = SpaceLeft();
        if (free > 0 && free < need + Headroom)
        {
            r.Problem =
                $"Not enough free space to install the game data.\n\n" +
                $"Needed: {Mb(need + Headroom)}\nFree: {Mb(free)}\n\n" +
                "Free some space and start the game again - it will carry on " +
                "from where it stopped.";
            return r;
        }

        foreach (PackEntry e in todo)
        {
            byte[] bytes;
            using (Godot.FileAccess f = Godot.FileAccess.Open(
                       e.Pack, Godot.FileAccess.ModeFlags.Read))
            {
                if (f == null)
                {
                    r.Problem = $"The game data in this build cannot be read:\n{e.Rel}\n\n" +
                                "Reinstalling the app is the fix.";
                    return r;
                }
                bytes = f.GetBuffer((long)f.GetLength());
            }

            string path = Path.Combine(dest, e.Rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex)
            {
                r.Problem =
                    $"Installing the game data stopped at {e.Rel}:\n\n{ex.Message}\n\n" +
                    $"{r.Written} of {todo.Count} files were written. Free some space " +
                    "and start the game again - it will carry on from here.";
                return r;
            }

            r.Written++;
            if (e.Length > 0) r.Bytes += e.Length;
            if (progress != null && (r.Written % 25 == 0 || r.Written == todo.Count))
                progress($"Installing game data: {r.Written} of {todo.Count}");
        }

        // LAST. Everything above has to have happened for this line to
        // be reached, which is the whole point of the marker.
        try
        {
            using Godot.FileAccess f = Godot.FileAccess.Open(
                UnpackMarker, Godot.FileAccess.ModeFlags.Write);
            if (f == null)
            {
                r.Problem = "The game data was copied but could not be marked as " +
                            "installed:\n" + ProjectSettings.GlobalizePath(UnpackMarker);
                return r;
            }
            f.StoreString(stamp);
        }
        catch (Exception e)
        {
            r.Problem = "The game data was copied but could not be marked as " +
                        $"installed:\n\n{e.Message}";
            return r;
        }

        GD.Print($"[M59Paths] unpacked {r.Written} files ({Mb(r.Bytes)}), " +
                 $"{r.Resumed} already present, to {dest}");
        if (_lost.Count > 0)
            GD.PrintErr($"[M59Paths] {_lost.Count} file(s) shipped only as Godot's " +
                        $"converted copy and could not be unpacked, first: {_lost[0]}");

        r.Ok = true;
        return r;
    }

    /// <summary>
    /// Removes files an older build left behind that this one does not
    /// ship. Only ever called where the marker proves the folder is ours.
    /// </summary>
    static void Prune(string dest)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PackEntry e in _pack)
            keep.Add(Path.Combine(dest, e.Rel.Replace('/', Path.DirectorySeparatorChar)));

        try
        {
            foreach (string f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
            {
                // Our own marker is not in the pack list and is rewritten
                // at the end regardless, so leaving it be costs nothing
                // and deleting it mid-run would only widen the window
                // where a crash looks like a fresh install.
                if (string.Equals(Path.GetFileName(f), ".unpacked", StringComparison.Ordinal))
                    continue;
                if (keep.Contains(f)) continue;
                try { File.Delete(f); } catch { }
            }
        }
        catch (Exception e) { GD.PrintErr($"[M59Paths] pruning {dest}: {e.Message}"); }
    }

    /// <summary>
    /// Free space where the game data goes, or 0 if the platform will
    /// not say. DirAccess answers for the disk its current directory is
    /// on, so it has to be opened on a path that exists - user:// always
    /// does, the folder underneath it may not yet.
    /// </summary>
    internal static long SpaceLeft()
    {
        try
        {
            using var d = DirAccess.Open("user://");
            return d == null ? 0 : (long)d.GetSpaceLeft();
        }
        catch { return 0; }
    }

    internal static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} MB";

    /// <summary>
    /// Whether this build carries the game data inside the export at
    /// all. The question GameView.Boot asks first, because it picks the
    /// whole path: a build with data unpacks it, a build without fetches
    /// it (ResourceSync), and nothing is gained by asking the second
    /// question of a build that can answer the first.
    /// </summary>
    public static bool IsBundled()
    {
        using var src = DirAccess.Open(PackedResource);
        return src != null;
    }

    /// <summary>Where the skybox faces sit inside the export.</summary>
    public const string PackedSky = "res://sky";
    /// <summary>Where they are copied to on a device that cannot read res:// as files.</summary>
    public const string UserSky = "user://sky";

    /// <summary>
    /// A real directory holding the six-faces-per-set skybox art, or
    /// null if there is none.
    ///
    /// The faces ship with the game rather than with the player's
    /// resource folder: they are the Ogre client's own
    /// Resources/sky/*.png (sky.material:1-89), which an installed
    /// Meridian client does not have. They are marked importer="keep"
    /// so Godot passes them through instead of turning them into
    /// textures, because M59Sky decodes them itself - it is shared with
    /// the headless check tools and cannot use Godot's image loader.
    ///
    /// On desktop res:// is a real folder and nothing needs copying. On
    /// Android it lives inside the .pck, so the faces are copied out
    /// once, the same way the bundled resource folder is.
    /// </summary>
    public static string SkyDir()
    {
        string here = ProjectSettings.GlobalizePath(PackedSky);
        if (Directory.Exists(here) && File.Exists(Path.Combine(here, "skya_fr.png")))
            return here;

        string dest = ProjectSettings.GlobalizePath(UserSky);
        if (Directory.Exists(dest) && File.Exists(Path.Combine(dest, "skya_fr.png")))
            return dest;

        using var src = DirAccess.Open(PackedSky);
        if (src == null) return null;                 // no sky in this build

        try { Directory.CreateDirectory(dest); }
        catch (Exception e) { GD.PrintErr($"[M59Paths] {dest}: {e.Message}"); return null; }

        int written = 0;
        foreach (string name in src.GetFiles())
        {
            if (name.EndsWith(".import", StringComparison.OrdinalIgnoreCase)) continue;
            using Godot.FileAccess f = Godot.FileAccess.Open(
                $"{PackedSky}/{name}", Godot.FileAccess.ModeFlags.Read);
            if (f == null) continue;
            try { File.WriteAllBytes(Path.Combine(dest, name), f.GetBuffer((long)f.GetLength())); written++; }
            catch (Exception e) { GD.PrintErr($"[M59Paths] {name}: {e.Message}"); }
        }
        GD.Print($"[M59Paths] unpacked {written} sky faces to {dest}");
        return written > 0 ? dest : null;
    }

    /// <summary>A sentence to show when nothing was found, naming the places looked.</summary>
    public static string NotFoundMessage()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("No Meridian resource folder found. Looked in:");
        foreach (string c in Candidates()) sb.AppendLine("  " + c);
        sb.AppendLine();
        sb.AppendLine("Either copy an installed client's 'resource' folder to:");
        sb.AppendLine("  " + ProjectSettings.GlobalizePath(UserResource));
        sb.AppendLine("or type where yours is below.");
        return sb.ToString();
    }
}
