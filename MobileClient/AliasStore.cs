using System;
using Godot;
using Meridian59.Common;
using Meridian59.Data.Models;

/// <summary>
/// The player's chat aliases - where they are kept, and the expansion
/// that turns one into a real command when it is typed.
///
/// WHAT THE REFERENCE DOES
///
/// An alias is a key/value pair of plain strings. The library holds them
/// on the config object itself, as a `KeyValuePairStringList` named
/// Aliases (`Meridian59/Common/Config.cs:119`, :307), reads them out of
/// the configuration file's &lt;aliases&gt; block - one
/// &lt;alias key="..." value="..."/&gt; per entry
/// (`Config.cs:598-619`) - and writes them straight back out in the same
/// shape (`Config.cs:715-726`). The shipped file seeds three:
/// chuckle, giggle and laugh, each expanding to an emote
/// (`Meridian59.Ogre.Client/configuration.template.xml:40-44`).
///
/// The list keeps itself sorted by key: the config constructor calls
/// `aliases.SortByKey()` (`Config.cs:345-346`), and once a BaseList is
/// in sorted mode every Add is routed through a sorted Insert
/// (`BaseList.cs:88-90`, `KeyValuePairStringList.cs:74-89`). That is why
/// the editor has no reorder - there is no order to choose. An alias
/// lands where its key puts it, alphabetically.
///
/// WHY THIS FILE EXISTS AT ALL
///
/// `Config.Load` on this client does read the &lt;aliases&gt; block if a
/// configuration.xml happens to be sitting next to the executable
/// (`GameView.cs` loads the config for exactly that reason). But nothing
/// here ever calls `Config.Save`, and on a phone there is no writable
/// file beside the executable to save to - the app's own sandbox is all
/// there is. So aliases had no way to survive being typed.
///
/// They are persisted the way this client persists its other state: a
/// Godot `ConfigFile` under user://, which is what `HotbarStore` does
/// with the action buttons for the same reason. The in-memory home is
/// still the library's `Config.Aliases`, unchanged, because that is the
/// list `ChatCommand.Parse` reads and the list a hotbar alias button
/// would resolve against (`BaseClient.cs:292-296`).
/// </summary>
public static class AliasStore
{
    const string Path = "user://aliases.cfg";
    const string Section = "aliases";

    /// <summary>
    /// Fills <paramref name="config"/>.Aliases from the saved file.
    ///
    /// When there is nothing saved and nothing loaded from a
    /// configuration.xml, the reference's three shipped aliases are
    /// seeded (`configuration.template.xml:41-43`) - a player who has
    /// never opened the editor should still be able to type "chuckle",
    /// because on the desktop client they always could.
    /// </summary>
    public static void Load(Config config)
    {
        if (config?.Aliases == null) return;

        try
        {
            var file = new ConfigFile();
            if (file.Load(Path) == Error.Ok && file.HasSectionKey(Section, "pairs"))
            {
                string[] rows = file.GetValue(Section, "pairs").AsStringArray();
                if (rows != null && rows.Length > 0)
                {
                    // A saved set replaces whatever the configuration
                    // file had: the editor is the newer word on the
                    // subject, and a deleted alias must stay deleted.
                    config.Aliases.Clear();
                    foreach (string row in rows)
                    {
                        // Tab separated. A key is one word and a value
                        // is a command line - neither can hold a tab,
                        // and only the FIRST tab splits, because the
                        // value is free text either way.
                        int cut = row.IndexOf('\t');
                        if (cut <= 0) continue;
                        string key = row.Substring(0, cut);
                        string val = row.Substring(cut + 1);
                        if (key.Length == 0 || val.Length == 0) continue;
                        if (config.Aliases.GetIndexByKey(key) != -1) continue;
                        config.Aliases.Add(new KeyValuePairString(key, val));
                    }
                    return;
                }
            }
        }
        catch (Exception e) { GD.PrintErr($"[AliasStore] load: {e.Message}"); }

        if (config.Aliases.Count == 0) Seed(config);
    }

    /// <summary>The three the reference ships with.</summary>
    static void Seed(Config config)
    {
        config.Aliases.Add(new KeyValuePairString("chuckle", "emote chuckles."));
        config.Aliases.Add(new KeyValuePairString("giggle", "emote giggles."));
        config.Aliases.Add(new KeyValuePairString("laugh", "emote laughs."));
    }

    /// <summary>
    /// Writes the list out. Called on every change rather than on the
    /// way out, for the reason HotbarStore gives: a phone client is not
    /// closed, it is swiped away, and there is no shutdown to hang a
    /// save on.
    /// </summary>
    public static void Save(Config config)
    {
        if (config?.Aliases == null) return;

        try
        {
            var rows = new System.Collections.Generic.List<string>();
            foreach (KeyValuePairString a in config.Aliases)
            {
                if (a == null) continue;
                if (string.IsNullOrEmpty(a.Key) || string.IsNullOrEmpty(a.Value)) continue;
                rows.Add(a.Key + "\t" + a.Value);
            }

            var file = new ConfigFile();
            file.Load(Path); // a missing file is the first run, not an error
            file.SetValue(Section, "pairs", rows.ToArray());
            file.Save(Path);
        }
        catch (Exception e) { GD.PrintErr($"[AliasStore] save: {e.Message}"); }
    }

    /// <summary>
    /// Expands a leading alias, exactly the way `ChatCommand.Parse`
    /// does it (`Meridian59/Data/Models/ChatCommand/ChatCommand.cs:66-86`).
    ///
    /// The rules there, and they are worth stating because two of them
    /// surprise people:
    ///
    ///   The text is trimmed and then the FIRST word is looked up. If
    ///   there is no space at all the whole line is the key and the
    ///   alias value replaces it outright (`:69-76`). If there is a
    ///   space, the part before it is the key (`:79-82`).
    ///
    ///   The lookup is `GetItemByKey`, which compares with `==` on
    ///   strings (`KeyValuePairStringList.cs:47-53`). So matching is
    ///   CASE SENSITIVE - "Chuckle" is not "chuckle".
    ///
    ///   Arguments are NOT substituted. There is no $1, no %s, no
    ///   placeholder of any kind. The remainder of the line is simply
    ///   glued onto the end of the value - and glued is the word,
    ///   because `:85` appends `lower.Substring(idx + 1)`, which is the
    ///   text AFTER the space with the space left behind. So an alias
    ///   meant to take an argument has to carry its own trailing space
    ///   in its value. That is why every alias the reference ships ends
    ///   in a full stop and takes nothing: the shipped set is the set
    ///   the quirk does not bite. It is reproduced here rather than
    ///   fixed, because a player's aliases were written against it.
    ///
    ///   Expansion happens once. The result is not looked up again, so
    ///   an alias cannot expand to another alias.
    ///
    /// The one departure is the case of the first word, and it is the
    /// departure this client already makes for command words. An Android
    /// keyboard sentence-cases the start of a line by default, so "chuckle"
    /// arrives as "Chuckle" and the reference's `==` finds nothing -
    /// which is the same silence `GameView.Commandable` was written to
    /// stop. So when the exact key misses, a case-insensitive key is
    /// accepted, and only if exactly one alias matches that way: two
    /// aliases differing only in case are a set the player built on the
    /// reference's rules, and guessing between them would be worse than
    /// doing nothing.
    /// </summary>
    public static string Expand(Config config, string text)
    {
        if (config?.Aliases == null || string.IsNullOrEmpty(text)) return text;

        string line = text.Trim();
        if (line.Length == 0) return text;

        // ChatCommand.DELIMITER, the space, is what splits the key off.
        int idx = line.IndexOf(ChatCommand.DELIMITER);
        string key = idx == -1 ? line : line.Substring(0, idx);

        KeyValuePairString hit = Find(config, key);
        if (hit == null) return text;

        // :74-76 for the one-word case, :84-85 for the rest. The
        // remainder deliberately loses its leading space, as above.
        return idx == -1 ? hit.Value : hit.Value + line.Substring(idx + 1);
    }

    /// <summary>
    /// The reference's exact-key lookup first, then the one-and-only
    /// case-insensitive match. See Expand for why the second pass is
    /// here and why it refuses to guess.
    /// </summary>
    static KeyValuePairString Find(Config config, string key)
    {
        KeyValuePairString exact = config.Aliases.GetItemByKey(key);
        if (exact != null) return exact;

        KeyValuePairString loose = null;
        foreach (KeyValuePairString a in config.Aliases)
        {
            if (a?.Key == null) continue;
            if (!string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (loose != null) return null;   // ambiguous: do nothing
            loose = a;
        }
        return loose;
    }
}
