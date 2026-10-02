using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Godot;

/// <summary>
/// Gets the game's resource folder onto the device from the website, and
/// keeps it current.
///
/// The Android build used to carry the whole resource folder inside the
/// APK, which M59Paths.UnpackIfNeeded copied out on first run. That made
/// every build half a gigabyte, made every change to the game's data a
/// new APK to sideload, and made a player who was two builds behind
/// re-download data that had not changed. So the APK does not carry it
/// any more. The website publishes a manifest - one line per file with
/// its size and SHA-1 - and this class fetches the manifest on every
/// launch, works out which files are missing or different, and fetches
/// only those. The first launch fetches everything; the thousandth
/// fetches a manifest and nothing else.
///
/// The desktop and the harness never come here: a res://resource that
/// exists goes through the unpack path, and a folder the player pointed
/// us at (--res, or resource-path.txt) is found by M59Paths.Resolve first.
/// GameView.Boot has the one decision; this class only knows how to
/// sync its own folder, user://resource, which on this path is ours
/// alone.
///
/// System.Net.Http.HttpClient rather than Godot's HttpRequest node,
/// deliberately. The node runs one request at a time, lives in the
/// scene tree, and answers on the main thread through a signal - fine
/// for the updater's one small file, and exactly wrong for thousands of
/// files from a worker thread, where every request would be a round
/// trip through CallDeferred and the main thread would be in the loop
/// for all of them. HttpClient is plain .NET, is in Godot's runtime on
/// Android, and is used here the way it is meant to be used: one
/// instance, reused for the whole run, because each new one opens its
/// own connection pool and a pool per file is a sockets-exhausted
/// exception a few hundred files in.
/// </summary>
public static class ResourceSync
{
    /// <summary>
    /// Where the manifest lives. The format is a contract with the
    /// generator on the server side:
    ///
    ///     { "stamp": "&lt;40 hex sha1&gt;",   changes whenever any file does
    ///       "count": 4700, "bytes": 489000000,
    ///       "base": "https://meridian59.us/mobile/resources/",
    ///       "files": [ { "n": "barinn.roo", "s": 12345, "h": "&lt;sha1&gt;" }, ... ] }
    ///
    /// A file's URL is base + Uri.EscapeDataString(n). Names are flat.
    /// M59RESOURCES overrides the address, the way M59UPDATE does for
    /// the updater, so the harness can serve a manifest off loopback.
    /// </summary>
    public const string ManifestUrl = "https://meridian59.us/mobile/resources.json";

    /// <summary>
    /// The last manifest that was fully applied, kept beside the files
    /// it describes. Its ABSENCE means nothing is known - not "empty",
    /// not "fine" - for the same reason M59Paths.UnpackMarker treats its
    /// own absence as "unfinished": the only thing that can say a set of
    /// files is complete is a record written after the last one, and a
    /// folder with a .roo in it says nothing of the kind. It is written
    /// once, at the end, when every file is in place, and a run that
    /// dies before that leaves the previous record (or none) behind, so
    /// the next launch re-diffs and finishes the job.
    ///
    /// Inside the folder on purpose, like the unpack marker: clearing
    /// app data or deleting the folder takes it too, and there is no way
    /// to hold a record that outlives its files.
    /// </summary>
    public const string SyncedFile = "user://resource/.synced.json";

    /// <summary>
    /// A file is fetched to this suffix and renamed over the real name
    /// once its hash checks, so a half-written file never carries a name
    /// the library would try to open. The rename is the commit.
    /// </summary>
    const string PartSuffix = ".part";

    /// <summary>
    /// A read that gets no bytes for this long is a dead connection, and
    /// the request is cancelled and retried. It is a STALL timeout, not
    /// a total one: a 9 MB .bgf on a poor mobile link legitimately takes
    /// minutes, and a fixed per-request limit either fails that file or
    /// is so long that a dead socket holds the run for as long.
    /// </summary>
    static readonly TimeSpan Stall = TimeSpan.FromSeconds(30);

    /// <summary>How often a file is tried before the run gives up on it.</summary>
    const int Tries = 3;

    /// <summary>Progress is reported at most this often, in bytes.</summary>
    const long ProgressEvery = 1024 * 1024;

    /// <summary>The one client. See the class comment.</summary>
    static readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient
    {
        // Covers the manifest and each file's headers; the body is
        // governed by Stall. Generous, because a phone that has just
        // woken its radio can take a few seconds to make the first
        // connection, and the point of a timeout here is a server that
        // never answers, not one that is slow to.
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Whether a run is in progress. GameView guards with its own
    /// _unpacking flag, which is the one that stops a second TAP; this
    /// is the one that stops a second CALLER, since two runs on the same
    /// files would rename over each other's .part files.
    /// </summary>
    static int _running;

    /// <summary>One file as the manifest and the local record describe it.</summary>
    public sealed class Entry
    {
        public string Name;
        public long Size;
        public string Hash;
    }

    internal sealed class Manifest
    {
        public string Stamp = "";
        public string Base = "";
        public List<Entry> Files = new List<Entry>();
        public long Bytes;
    }

    /// <summary>
    /// How a sync turned out, modelled on M59Paths.UnpackReport for the
    /// same reason that exists: Ok with nothing downloaded and a refusal
    /// are different answers, and the caller has to tell them apart.
    /// </summary>
    public sealed class Report
    {
        /// <summary>True when the folder is complete and playable.</summary>
        public bool Ok;
        /// <summary>A sentence for the player, or null when Ok.</summary>
        public string Problem;
        /// <summary>
        /// One line for the login card and the chat, saying what the
        /// sync did - the equivalent of Updater.Said. Set when Ok.
        /// </summary>
        public string Note = "";
        /// <summary>Files the manifest listed.</summary>
        public int Checked;
        /// <summary>Files fetched this run.</summary>
        public int Downloaded;
        /// <summary>Bytes fetched this run.</summary>
        public long Bytes;
        /// <summary>Stale files removed from the folder.</summary>
        public int Deleted;
        /// <summary>Files already right and left alone.</summary>
        public int Skipped;
        /// <summary>The manifest could not be fetched and the last set is being used.</summary>
        public bool Offline;
    }

    /// <summary>
    /// What a run intends to do, worked out before a byte of game data
    /// is fetched. Split from Apply because the first run asks the
    /// player before downloading, and the question needs the number -
    /// which only the manifest knows.
    /// </summary>
    public sealed class Plan
    {
        /// <summary>Set when the run is already decided: a refusal, or nothing to do.</summary>
        public Report Report;
        /// <summary>
        /// Whether to ask before downloading. True only when nothing of
        /// the manifest is on the device at all - a first run. A folder
        /// with even one right file in it is a run that was already
        /// agreed to and interrupted, and is resumed without asking.
        /// </summary>
        public bool Ask;
        /// <summary>Files to fetch, and their total.</summary>
        public int NeedCount;
        public long NeedBytes;
        /// <summary>Files in the manifest.</summary>
        public int Count;

        internal Manifest Remote;
        internal List<Entry> Todo;
        internal int Skipped;
    }

    /// <summary>The manifest address in force: the override, or the real one.</summary>
    public static string Where()
    {
        string w = System.Environment.GetEnvironmentVariable("M59RESOURCES");
        return string.IsNullOrWhiteSpace(w) ? ManifestUrl : w.Trim();
    }

    /// <summary>
    /// Whether a folder Resolve found is the sync's own, which on a
    /// build without bundled data is user://resource, whatever is in
    /// it. GameView.Boot asks this before letting Resolve's answer go
    /// to the game, since Resolve would find the folder and go
    /// straight on, and it would never be checked against the website
    /// again.
    ///
    /// By PATH, not by what is inside. The first cut of this looked for
    /// the sync's record or the unpack marker, and the harness caught
    /// it at once: a first download that dies before the record is
    /// written leaves a folder with no marker and a thousand .bgf files
    /// in it, Resolve's HasContent is satisfied by one of them, and the
    /// client played a room whose .roo was ten bytes long. That is the
    /// bug M59Paths.UnpackMarker's comment cost months on, in a new
    /// coat. Nothing else writes to user://resource on this path - a
    /// desktop player's own folder is found through the saved path or
    /// --res, which Resolve tries first - so the folder is ours by
    /// construction and the only question is whether it is complete,
    /// which is Prepare's to answer.
    /// </summary>
    public static bool Owns(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(ProjectSettings.GlobalizePath(M59Paths.UserResource))
                    .TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // ---- phase one: the manifest and the diff --------------------

    /// <summary>
    /// Fetches the manifest and works out what has to change. Worker
    /// thread; reads the folder, writes nothing to it.
    ///
    /// The fast path is the whole reason the stamp exists: on every
    /// launch after the first, the remote stamp equals the recorded one,
    /// and the only work is one stat per file to be sure nothing has
    /// gone missing. The recorded hash is trusted when the size matches
    /// - hashing half a gigabyte on every launch would be a check so
    /// expensive nobody would ship it, and a file that changed on disk
    /// without changing size and without the stamp moving is a
    /// corruption this check is not for.
    /// </summary>
    public static Plan Prepare(Action<string> progress = null)
    {
        var p = new Plan();
        string dir = ProjectSettings.GlobalizePath(M59Paths.UserResource);
        Manifest local = ReadLocal(dir);

        progress?.Invoke("Checking for game data updates...");
        Manifest remote;
        string why;
        try { remote = Fetch(Where(), out why); }
        catch (Exception e) { remote = null; why = $"The game data list could not be read:\n{e.Message}"; }

        if (remote == null)
        {
            // A server that is down must not stop someone who already
            // has the data. The record says what a complete set is; if
            // every file in it is here at its length, play. A finished
            // unpack from a build that carried its data counts too -
            // that marker was written after the last byte, which is the
            // same promise.
            if (local != null && Complete(dir, local, out int have))
            {
                GD.Print($"[ResourceSync] offline ({why.Replace('\n', ' ')}); " +
                         $"{have} files present, playing with them");
                p.Report = new Report
                {
                    Ok = true, Offline = true, Checked = have, Skipped = have,
                    Note = "Offline - playing with the game data you have.",
                };
                return p;
            }
            if (local == null && File.Exists(Path.Combine(dir, ".unpacked")))
            {
                GD.Print($"[ResourceSync] offline ({why.Replace('\n', ' ')}); " +
                         "an unpacked set is present, playing with it");
                p.Report = new Report
                {
                    Ok = true, Offline = true,
                    Note = "Offline - playing with the game data you have.",
                };
                return p;
            }
            p.Report = new Report { Problem = why };
            return p;
        }

        p.Remote = remote;
        p.Count = remote.Files.Count;

        // The diff. Three ways a file is needed: it is not here, its
        // size is wrong (a download the process died in the middle of,
        // or a file somebody truncated), or the hash on record is not
        // the hash the server has now. A file at the right size with NO
        // record is hashed here rather than fetched: that is what a
        // crash mid-run leaves behind - thousands of files fully written
        // and no .synced.json to vouch for them - and re-downloading
        // them would make the resume cost nearly the whole run.
        var known = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (local != null)
            foreach (Entry e in local.Files) known[e.Name] = e;

        p.Todo = new List<Entry>();
        int present = 0;
        foreach (Entry e in remote.Files)
        {
            string path = Path.Combine(dir, e.Name);
            long size = -1;
            try { var fi = new FileInfo(path); if (fi.Exists) size = fi.Length; } catch { }

            if (size == e.Size)
            {
                present++;
                if (known.TryGetValue(e.Name, out Entry k) && k.Hash == e.Hash) { p.Skipped++; continue; }
                if (!known.ContainsKey(e.Name) && HashOf(path) == e.Hash) { p.Skipped++; continue; }
            }
            p.Todo.Add(e);
            p.NeedBytes += e.Size;
        }
        p.NeedCount = p.Todo.Count;
        p.Ask = local == null && present == 0;

        if (p.Todo.Count == 0 && local != null && local.Stamp == remote.Stamp && !HasStale(dir, remote))
        {
            // The every-launch answer: one manifest, N stats, nothing
            // else. Said as a line rather than silently, for the reason
            // Updater says "you have the newest build" - a check that
            // ran and found nothing and a check that never ran must not
            // look the same on the login card.
            GD.Print($"[ResourceSync] current: {remote.Files.Count} files, stamp {remote.Stamp}");
            p.Report = new Report
            {
                Ok = true, Checked = remote.Files.Count, Skipped = p.Skipped,
                Note = $"Game data is up to date ({remote.Files.Count:N0} files).",
            };
        }
        return p;
    }

    // ---- phase two: the download ----------------------------------

    /// <summary>
    /// Carries out a Plan: fetches what it listed, verifies each file,
    /// removes what the manifest no longer names, and writes the record
    /// last. Worker thread. Reports through <paramref name="progress"/>
    /// every file or every megabyte, whichever is the quieter.
    /// </summary>
    public static Report Apply(Plan p, Action<string> progress = null)
    {
        if (p.Report != null) return p.Report;
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new Report { Problem = "The game data is already being updated." };
        try { return Run(p, progress); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    static Report Run(Plan p, Action<string> progress)
    {
        var r = new Report { Checked = p.Count, Skipped = p.Skipped };
        Manifest remote = p.Remote;
        string dir = ProjectSettings.GlobalizePath(M59Paths.UserResource);

        try { Directory.CreateDirectory(dir); }
        catch (Exception e)
        {
            r.Problem = "The game data folder could not be created:\n" + dir + "\n\n" + e.Message;
            return r;
        }

        // Leftovers from a run that died mid-file. They are not resumed
        // - a .part that stopped at byte 3,000,000 of 9,000,000 could be
        // completed with a Range request, but a hash mismatch on the
        // result would cost the whole file again anyway, and the saving
        // is one partial file per crash.
        try
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*" + PartSuffix))
                try { File.Delete(f); } catch { }
        }
        catch { }

        // Space BEFORE the first byte, as UnpackIfNeeded does and for
        // the reason given there: a full disk should be one sentence on
        // the screen, not one failure per file in logcat. GetSpaceLeft
        // answers 0 when the platform will not say, which is not "no
        // space".
        long free = M59Paths.SpaceLeft();
        if (free > 0 && free < p.NeedBytes + M59Paths.Headroom)
        {
            r.Problem =
                $"Not enough free space to download the game data.\n\n" +
                $"Needed: {M59Paths.Mb(p.NeedBytes + M59Paths.Headroom)}\nFree: {M59Paths.Mb(free)}\n\n" +
                "Free some space and start the game again - it will carry on " +
                "from where it stopped.";
            return r;
        }

        long total = p.NeedBytes, done = 0, lastSaid = -ProgressEvery;
        int n = 0;
        if (p.Todo.Count > 0) progress?.Invoke(Line(1, p.Todo.Count, 0, total));
        foreach (Entry e in p.Todo)
        {
            n++;
            string path = Path.Combine(dir, e.Name);
            string part = path + PartSuffix;
            string url = remote.Base + Uri.EscapeDataString(e.Name);

            // Progress is a closure over the file's running count so a
            // 9 MB file moves the bar on its own rather than looking
            // like a stall for as long as it takes.
            int at = n; long before = done;
            Action<long> tick = got =>
            {
                long now = before + got;
                if (now - lastSaid < ProgressEvery && now < total) return;
                lastSaid = now;
                progress?.Invoke(Line(at, p.Todo.Count, now, total));
            };

            string fail = null;
            for (int t = 1; t <= Tries; t++)
            {
                fail = Download(url, part, e, tick);
                if (fail == null) break;
                GD.Print($"[ResourceSync] {e.Name} try {t}: {fail}");
                try { File.Delete(part); } catch { }
                // Short, and growing: a server that just dropped one
                // connection is usually fine a second later, and one
                // that is not will not be helped by hammering it.
                if (t < Tries) Thread.Sleep(1000 * t);
            }
            if (fail != null)
            {
                r.Problem =
                    $"Downloading the game data stopped at {e.Name}:\n\n{fail}\n\n" +
                    $"{r.Downloaded} of {p.Todo.Count} files were fetched. Check the " +
                    "connection and try again - it will carry on from here.";
                return r;
            }

            try { File.Move(part, path, true); }
            catch (Exception ex)
            {
                r.Problem = $"Installing the game data stopped at {e.Name}:\n\n{ex.Message}";
                return r;
            }

            r.Downloaded++;
            r.Bytes += e.Size;
            done += e.Size;
            tick(e.Size);
        }

        // Files the manifest no longer names. Only on this path, where
        // the folder is ours and nobody else's: the unpack path prunes
        // only behind its marker for the same reason, and a folder the
        // player pointed us at is never touched by either. Our own
        // records stay - the sync's, and the unpack marker, which an
        // APK that carries data again would read.
        r.Deleted = Prune(dir, remote);

        // LAST, as the unpack marker is. Everything above has happened
        // for this line to be reached.
        string why = WriteLocal(dir, remote);
        if (why != null) { r.Problem = why; return r; }

        GD.Print($"[ResourceSync] synced {r.Downloaded} files ({M59Paths.Mb(r.Bytes)}), " +
                 $"{r.Skipped} already right, {r.Deleted} removed, stamp {remote.Stamp}");
        r.Ok = true;
        r.Note = r.Downloaded == 0
            ? $"Game data is up to date ({p.Count:N0} files)."
            : r.Skipped == 0
                ? $"Game data downloaded: {r.Downloaded:N0} files, {M59Paths.Mb(r.Bytes)}."
                : $"Game data updated: {r.Downloaded:N0} file(s), {M59Paths.Mb(r.Bytes)}.";
        return r;
    }

    /// <summary>"Downloading 312 / 4,700 - 84.2 of 467 MB".</summary>
    static string Line(int file, int files, long bytes, long total)
        => $"Downloading {file:N0} / {files:N0} - " +
           $"{bytes / (1024.0 * 1024.0):0.#} of {total / (1024.0 * 1024.0):0.#} MB";

    /// <summary>
    /// One file, streamed to its .part and hashed as it arrives, so the
    /// check costs no second read. Returns null on success or a
    /// sentence saying what went wrong, which the caller retries on.
    /// </summary>
    static string Download(string url, string part, Entry e, Action<long> tick)
    {
        try
        {
            using var cts = new CancellationTokenSource(Stall);
            using HttpResponseMessage resp = _http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
                return $"the server answered {(int)resp.StatusCode}";

            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA1);
            long got = 0;
            using (Stream body = resp.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult())
            using (var file = new FileStream(part, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 1 << 16))
            {
                byte[] buf = new byte[1 << 16];
                while (true)
                {
                    cts.CancelAfter(Stall);
                    int k = body.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult();
                    if (k <= 0) break;
                    file.Write(buf, 0, k);
                    sha.AppendData(buf, 0, k);
                    got += k;
                    if (got > e.Size) return $"the server sent more than the {e.Size} bytes expected";
                    tick(got);
                }
            }
            if (got != e.Size) return $"the server sent {got} bytes, not {e.Size}";
            string hash = Hex(sha.GetHashAndReset());
            if (hash != e.Hash) return "the file arrived with the wrong checksum";
            return null;
        }
        catch (OperationCanceledException) { return "the connection stalled"; }
        catch (HttpRequestException ex) { return "the server could not be reached: " + ex.Message; }
        catch (Exception ex) { return ex.Message; }
    }

    // ---- the manifest ----------------------------------------------

    /// <summary>
    /// The remote manifest, or null with <paramref name="why"/> set to
    /// the sentence for the player. The three failures that actually
    /// happen are told apart the way Updater.Answered tells them apart,
    /// because each is a different five-second fix once somebody knows
    /// which it was: nothing at the address (404), the server not
    /// reached at all, and a file that is there and will not parse.
    /// </summary>
    static Manifest Fetch(string url, out string why)
    {
        why = null;
        byte[] body;
        try
        {
            using HttpResponseMessage resp = _http.GetAsync(url).GetAwaiter().GetResult();
            if ((int)resp.StatusCode == 404)
            {
                why = $"There is nothing at the game data address (404):\n{url}";
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                why = $"The game data server answered {(int)resp.StatusCode} for:\n{url}";
                return null;
            }
            body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException)
        {
            why = $"The game data server could not be reached:\n{url}\n\n{e.Message}\n\n" +
                  "Check the connection and try again.";
            return null;
        }

        Manifest m;
        try
        {
            // TrimStart on U+FEFF, as Updater does and for the same
            // reason: the file lives on a server somebody may edit by
            // hand, and an editor that writes a byte order mark writes
            // a manifest that is correct everywhere and parses nowhere.
            string text = System.Text.Encoding.UTF8.GetString(body).TrimStart('﻿');
            m = Parse(text, out string bad);
            if (m == null) { why = $"The game data list could not be read:\n{bad}"; return null; }
        }
        catch (Exception e)
        {
            why = $"The game data list could not be read:\n{e.Message}";
            return null;
        }

        // The base is content from the network naming where the client
        // will fetch from, so it does not get to pick a scheme: https,
        // unless the manifest itself was fetched over plain http - which
        // only an M59RESOURCES override can arrange, and is the harness.
        bool plain = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        if (!(m.Base.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || (plain && m.Base.StartsWith("http://", StringComparison.OrdinalIgnoreCase))))
        {
            why = "The game data list does not name an https download address.";
            return null;
        }
        if (!m.Base.EndsWith("/")) m.Base += "/";
        return m;
    }

    /// <summary>
    /// The manifest's JSON into a Manifest, with the names checked. The
    /// contract says names are flat, and this is where that is enforced
    /// rather than trusted: a name with a separator in it, or one that
    /// starts with a dot, is a path this client would write wherever
    /// the server said, and the server is not the one that gets to
    /// decide that.
    /// </summary>
    static Manifest Parse(string text, out string bad)
    {
        bad = null;
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { bad = "not a JSON object"; return null; }

        var m = new Manifest();
        if (root.TryGetProperty("stamp", out JsonElement s)) m.Stamp = s.GetString() ?? "";
        if (root.TryGetProperty("base", out JsonElement b)) m.Base = b.GetString() ?? "";
        if (m.Stamp.Length == 0) { bad = "no stamp"; return null; }
        if (!root.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
        { bad = "no files list"; return null; }

        foreach (JsonElement f in files.EnumerateArray())
        {
            var e = new Entry
            {
                Name = f.TryGetProperty("n", out JsonElement n) ? n.GetString() : null,
                Size = f.TryGetProperty("s", out JsonElement sz) && sz.TryGetInt64(out long v) ? v : -1,
                Hash = f.TryGetProperty("h", out JsonElement h) ? (h.GetString() ?? "").ToLowerInvariant() : "",
            };
            if (string.IsNullOrEmpty(e.Name) || e.Size < 0 || e.Hash.Length != 40)
            { bad = $"a malformed entry ({e.Name ?? "unnamed"})"; return null; }
            if (e.Name.IndexOfAny(new[] { '/', '\\' }) >= 0 || e.Name.StartsWith(".") || e.Name.Contains(".."))
            { bad = $"a name this client will not write ({e.Name})"; return null; }
            m.Files.Add(e);
            m.Bytes += e.Size;
        }
        return m;
    }

    /// <summary>The record on disk, or null when there is none or it will not parse.</summary>
    static Manifest ReadLocal(string dir)
    {
        string path = Path.Combine(dir, ".synced.json");
        try
        {
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path).TrimStart('﻿'), out _);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ResourceSync] {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes the record, to a temporary name and then over the real
    /// one, so a crash in the middle of this write cannot leave a record
    /// that parses as a shorter set. Returns a sentence on failure.
    /// </summary>
    static string WriteLocal(string dir, Manifest m)
    {
        string path = Path.Combine(dir, ".synced.json");
        try
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();
                    w.WriteString("stamp", m.Stamp);
                    w.WriteNumber("count", m.Files.Count);
                    w.WriteNumber("bytes", m.Bytes);
                    w.WriteString("base", m.Base);
                    w.WriteStartArray("files");
                    foreach (Entry e in m.Files)
                    {
                        w.WriteStartObject();
                        w.WriteString("n", e.Name);
                        w.WriteNumber("s", e.Size);
                        w.WriteString("h", e.Hash);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                File.WriteAllBytes(path + PartSuffix, ms.ToArray());
            }
            File.Move(path + PartSuffix, path, true);
            return null;
        }
        catch (Exception e)
        {
            return "The game data was downloaded but could not be marked as " +
                   $"installed:\n\n{e.Message}";
        }
    }

    /// <summary>Every file the record names is on disk at its length.</summary>
    static bool Complete(string dir, Manifest m, out int have)
    {
        have = 0;
        foreach (Entry e in m.Files)
        {
            try
            {
                var fi = new FileInfo(Path.Combine(dir, e.Name));
                if (!fi.Exists || fi.Length != e.Size) return false;
            }
            catch { return false; }
            have++;
        }
        return true;
    }

    /// <summary>
    /// Whether the folder holds anything the manifest does not name.
    /// Asked on the fast path so a stale file is still removed on the
    /// launch after the manifest dropped it, rather than lingering until
    /// something else changes.
    /// </summary>
    static bool HasStale(string dir, Manifest m)
    {
        var keep = Keep(m);
        try
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (!keep.Contains(Path.GetRelativePath(dir, f))) return true;
        }
        catch { }
        return false;
    }

    static HashSet<string> Keep(Manifest m)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal) { ".synced.json", ".unpacked" };
        foreach (Entry e in m.Files) keep.Add(e.Name);
        return keep;
    }

    /// <summary>
    /// Removes what the manifest does not name, subfolders included -
    /// the unpack path wrote rooms/, sounds/ and the rest, and a device
    /// moving from a build that carried its data to one that fetches it
    /// is left with those under a flat manifest. Emptied folders go
    /// too. Only ever called on our own folder; see Run.
    /// </summary>
    static int Prune(string dir, Manifest m)
    {
        var keep = Keep(m);
        int gone = 0;
        try
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (keep.Contains(Path.GetRelativePath(dir, f))) continue;
                try { File.Delete(f); gone++; } catch (Exception e) { GD.PrintErr($"[ResourceSync] {f}: {e.Message}"); }
            }
            foreach (string d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
        catch (Exception e) { GD.PrintErr($"[ResourceSync] pruning {dir}: {e.Message}"); }
        return gone;
    }

    static string HashOf(string path)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            using var f = File.OpenRead(path);
            return Hex(sha.ComputeHash(f));
        }
        catch { return ""; }
    }

    static string Hex(byte[] b)
    {
        var sb = new System.Text.StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}
