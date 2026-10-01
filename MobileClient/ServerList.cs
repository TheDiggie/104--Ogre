using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Which servers there are to log into, and which one was picked last.
///
/// The desktop client has never hardcoded a host. `UILogin.cpp:24-26`
/// fills its combobox by walking `Config->Connections` - the
/// &lt;connections&gt; block of configuration.xml, which
/// `Config.cs:472-505` parses into a ConnectionInfo apiece with a name,
/// a host, a port and a string dictionary - and `UILogin.cpp:88-93`
/// writes the chosen row's index back to
/// `Config->SelectedConnectionIndex`, which is what
/// `BaseClient.Connect` (`BaseClient.cs:114-141`) then reads for the
/// host, the port and the strings file.
///
/// The mobile client had none of that. `GameView.Host` and
/// `GameView.Port` were `[Export]` fields baked into the scene at
/// export time, and `Begin` built exactly one ConnectionInfo out of
/// them. A player on a phone could not move between the test server and
/// the live one, or follow a server that changed address, without
/// somebody rebuilding the app for them.
///
/// So this reads the connections the way the reference reads them, out
/// of the same file by the same parser, and adds nothing of its own
/// except one thing: the build-time host is kept as an entry too, and
/// kept FIRST when the file has nothing to say. That is what makes this
/// change invisible to a player who already had a working client - the
/// default selection is the server they were already reaching.
///
/// The pick is remembered by host and port rather than by index, in its
/// own small ConfigFile under user://. By address because an index into
/// configuration.xml means nothing once that file changes underneath it
/// - the reference stores an index because it also owns the file it
/// indexes into, and here we do not. In its own file because
/// OptionsPanel's settings.cfg is rewritten whole on every change
/// (`OptionsPanel.Keep`), and two writers on one ConfigFile is a race
/// that loses somebody's volume setting.
/// </summary>
public static class ServerList
{
    /// <summary>
    /// One row of the picker. The same four fields ConnectionInfo
    /// carries that actually decide where the socket goes
    /// (`ConnectionInfo.cs:60-99`): what to call it, where it is, and
    /// which string dictionary its strings come out of.
    /// </summary>
    public readonly struct Entry
    {
        public readonly string Name, Host, Strings;
        public readonly int Port;

        /// <summary>
        /// The account the file names for this server, if it names one.
        ///
        /// Carried because the reference carries it: `OnServerChanged`
        /// refills both login boxes from the newly selected entry
        /// (`UILogin.cpp:94-101`), since an account name is a per-server
        /// thing. These come out of configuration.xml, a file the player
        /// owns - LoginPrompt still writes no password of its own to
        /// disk, and nothing here puts one in that file.
        /// </summary>
        public readonly string Account, Secret;

        public Entry(string name, string host, int port, string strings,
                     string account = null, string secret = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? host : name;
            Host = host;
            Port = port;
            Strings = strings;
            Account = account;
            Secret = secret;
        }

        /// <summary>What is written to disk, and what two entries are compared by.</summary>
        public string Address => $"{Host}:{Port}";
    }

    const string StorePath = "user://server.cfg";
    const string StoreSection = "login";

    /// <summary>
    /// The list, and which row starts selected.
    ///
    /// <paramref name="host"/> and <paramref name="port"/> are the
    /// build-time ones, and <paramref name="strings"/> the string
    /// dictionary the view found on disk - the same value `Begin` used
    /// to put in its single ConnectionInfo. They are the fallback and
    /// they are also the default: whatever else the file offers, the
    /// selection lands on this address if it is present, so an existing
    /// player sees the picker already showing the server they know.
    ///
    /// A remembered pick from a previous session beats that, which is
    /// the whole point of remembering it.
    /// </summary>
    public static List<Entry> Load(string host, int port, string strings, out int selected)
    {
        var rows = new List<Entry>();
        string fallbackAddress = $"{host}:{port}";
        int fromFile = -1;

        // The reference's own source, read by the reference's own
        // parser. A throwaway Config rather than the client's: the
        // client's is loaded inside Begin, which is after login, and
        // Load CLEARS Connections - so reading the client's here would
        // mean either loading it twice or moving the load, and both are
        // ways to lose the connection Begin adds.
        try
        {
            var config = new Meridian59.Common.Config();
            config.Load(Meridian59.Common.Config.CONFIGFILE,
                        Meridian59.Common.Config.CONFIGFILE_ALT);

            foreach (ConnectionInfo info in config.Connections)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.Host)) continue;
                rows.Add(new Entry(info.Name, info.Host, info.Port,
                    string.IsNullOrWhiteSpace(info.StringDictionary) ? strings : info.StringDictionary,
                    info.Username, info.Password));
            }

            // The file's own selectedindex attribute, which is what the
            // reference starts on (`Config.cs:477`, `UILogin.cpp:42`).
            if (config.SelectedConnectionIndex >= 0 && config.SelectedConnectionIndex < rows.Count)
                fromFile = config.SelectedConnectionIndex;
        }
        catch (Exception e)
        {
            // No configuration.xml is the normal case on a phone - there
            // is no writable folder beside the executable to put one in.
            // It is not an error, it just means the build-time host is
            // all there is.
            GD.Print($"[ServerList] no connections from configuration.xml: {e.Message}");
        }

        // The build-time host, if the file did not already name it.
        // Inserted at the front when the file gave us nothing so the
        // list is never empty, appended otherwise so the file's own
        // ordering survives.
        bool have = false;
        foreach (Entry e in rows) if (e.Address == fallbackAddress) { have = true; break; }
        if (!have && !string.IsNullOrWhiteSpace(host))
            rows.Add(new Entry(host, host, port, strings));

        // Default: the build-time address, so nothing changes for a
        // player who already had this working. This deliberately BEATS
        // the file's own selectedindex, which is the opposite of the
        // reference's ordering and the right way round here - the
        // reference has no build-time host to be overruled, while this
        // client's exported Host is the server somebody deliberately
        // shipped the build for. A configuration.xml that happens to be
        // lying around must not silently point a working client somewhere
        // else, and the first run of this did exactly that: a harness
        // told to use 127.0.0.1 connected to 3.141.65.36 instead.
        selected = -1;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Address == fallbackAddress) { selected = i; break; }

        // The file's own selectedindex only decides when the build named
        // no host it recognises (`Config.cs:477`, `UILogin.cpp:42`).
        if (selected < 0) selected = fromFile >= 0 ? fromFile : 0;

        // The player's own last choice is the most specific of the three
        // and comes last.
        string remembered = Recall();
        if (!string.IsNullOrEmpty(remembered))
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Address == remembered) { selected = i; break; }

        return rows;
    }

    /// <summary>The address picked last time, or null.</summary>
    static string Recall()
    {
        try
        {
            var file = new ConfigFile();
            if (file.Load(StorePath) != Error.Ok) return null;
            return (string)file.GetValue(StoreSection, "address", "");
        }
        catch (Exception e) { GD.PrintErr($"[ServerList] load: {e.Message}"); return null; }
    }

    /// <summary>
    /// Remembers a pick. Written when it is made rather than on the way
    /// out, for the reason AliasStore and OptionsPanel both give: a
    /// phone client has no shutdown to hang a save on.
    /// </summary>
    public static void Remember(Entry entry)
    {
        try
        {
            var file = new ConfigFile();
            file.SetValue(StoreSection, "address", entry.Address);
            file.Save(StorePath);
        }
        catch (Exception e) { GD.PrintErr($"[ServerList] save: {e.Message}"); }
    }
}
