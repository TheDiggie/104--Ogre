using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// Remembers the hotbar between sessions, per character.
///
/// The game keeps these in its own client configuration - a
/// &lt;actionbuttons player="..."&gt; block per character, one
/// &lt;actionbutton num type name numofsamename&gt; per button, read at
/// `OgreClientConfig.cpp:581` and written at `:1190`. It is saved on
/// shutdown and on the quit message, and loaded when a character is
/// chosen. Without it the hotbar is whatever the seed put there, every
/// time, and binding a spell to it is a gesture that lasts until you
/// close the app - which is the state this client was in.
///
/// Four fields are stored and no more, which is the same four the game
/// stores. What is deliberately **not** stored is as interesting:
///
///   Data - the spell, skill or item itself. It is re-resolved from the
///   server by name: `DataController` walks the button list as the
///   inventory, the spells and the skills arrive and calls SetToItem,
///   SetToSpell or SetToSkill on any button whose type and name match
///   (`DataController.cs:2438`, `:2517`, `:2558`). So a restored button
///   is deliberately dataless, and the server fills it in. A stored
///   object id would be worse than useless - ids are per session.
///
///   Label - the key the slot is bound to, which the game re-derives
///   from its key bindings on login and a phone does not have at all.
///
/// An item is matched by name **and** NumOfSameName, because names are
/// not unique in this game and the library says so in its own comment.
/// </summary>
public static class HotbarStore
{
    const string Path = "user://hotbar.cfg";

    /// <summary>
    /// Writes this character's buttons. Called when the set changes
    /// rather than on the way out: a phone client is not closed, it is
    /// swiped away, and there is no shutdown to hang a save on.
    /// </summary>
    public static void Save(DataController data)
    {
        if (data?.ActionButtons == null || !data.ActionButtons.HasPlayerName) return;

        try
        {
            var file = new ConfigFile();
            file.Load(Path); // a missing file is not an error, it is the first run

            var rows = new List<string>();
            foreach (ActionButtonConfig b in data.ActionButtons)
            {
                if (b == null || b.ButtonType == ActionButtonType.Unset) continue;
                // Tab separated: a button's name is a thing the server
                // chose and may hold anything typographic, but not a tab.
                //
                // For an alias, Name is the alias KEY and nothing else -
                // `SetToAlias` copies `Item.Key` into name and leaves
                // NumOfSameName at zero
                // (`Meridian59/Data/Models/ActionButtonConfig.cs:267-278`).
                // The key is therefore the whole of what identifies an
                // alias button on disk, which is exactly what the game
                // stores: its &lt;actionbutton&gt; element carries num, type,
                // name and numofsamename and no more
                // (`Meridian59.Ogre.Client/OgreClientConfig.cpp:1206-1211`),
                // and the expansion is not stored with the button - it is
                // looked up again from the alias list on the way back in,
                // so editing an alias changes every button bound to it.
                rows.Add($"{b.Num}\t{b.ButtonType}\t{b.NumOfSameName}\t{b.Name}");
            }

            file.SetValue(Key(data.ActionButtons.PlayerName), "buttons", rows.ToArray());
            file.Save(Path);
        }
        catch (Exception e) { GD.PrintErr($"[HotbarStore] save: {e.Message}"); }
    }

    /// <summary>
    /// Fills the list from this character's saved buttons. Returns false
    /// when there is nothing saved, which is the caller's cue to seed.
    ///
    /// Anything unreadable is skipped rather than thrown: a stored set
    /// that has gone bad should cost you your buttons, not your login.
    /// </summary>
    public static bool Load(DataController data)
    {
        if (data?.ActionButtons == null || !data.ActionButtons.HasPlayerName) return false;

        try
        {
            var file = new ConfigFile();
            if (file.Load(Path) != Error.Ok) return false;

            string section = Key(data.ActionButtons.PlayerName);
            if (!file.HasSectionKey(section, "buttons")) return false;

            string[] rows = (string[])file.GetValue(section, "buttons");
            if (rows == null || rows.Length == 0) return false;

            var restored = new List<ActionButtonConfig>();
            foreach (string row in rows)
            {
                string[] f = row.Split('\t');
                if (f.Length < 4) continue;
                if (!int.TryParse(f[0], out int num)) continue;
                if (!Enum.TryParse(f[1], out ActionButtonType type)) continue;
                if (type == ActionButtonType.Unset) continue;
                uint.TryParse(f[2], out uint same);

                // An alias is the one type whose data is NOT the
                // server's to fill in, and the one the game's loader
                // resolves for itself: having read the type and the
                // name, it does `data = aliases->GetItemByKey(name)`,
                // and where that finds nothing it throws the whole
                // button away - type back to Unset, name blanked
                // (`Meridian59.Ogre.Client/OgreClientConfig.cpp:614-627`).
                // It has to, because nothing later will ever bind it:
                // the data controller matches arriving items, spells and
                // skills by name, and an alias arrives from no server at
                // all. A button left dataless would be one BaseClient's
                // dispatch silently ignores forever
                // (`Meridian59/Client/BaseClient.cs:272` guards the whole
                // switch on Data being non-null).
                //
                // Dropping the row rather than keeping an Unset one is
                // this loader's existing habit two lines up, and comes to
                // the same place: Save writes no Unset buttons, and
                // Bind reuses or appends a slot regardless.
                object bound = null;
                if (type == ActionButtonType.Alias)
                {
                    bound = AliasStore.Current?.Aliases?.GetItemByKey(f[3]);
                    if (bound == null) continue;
                }

                // The constructor resolves an Action's data from its own
                // name, and leaves everything else dataless for the
                // server to fill in - which is exactly the split the
                // game's own loader relies on. An alias comes in with the
                // data just resolved, exactly as `OgreClientConfig.cpp:630-636`
                // passes it to the same constructor.
                restored.Add(new ActionButtonConfig(num, type, f[3], bound, null, same));
            }

            if (restored.Count == 0) return false;

            data.ActionButtons.Clear();
            foreach (ActionButtonConfig b in restored) data.ActionButtons.Add(b);
            return true;
        }
        catch (Exception e) { GD.PrintErr($"[HotbarStore] load: {e.Message}"); return false; }
    }

    /// <summary>
    /// One section per character. Lower-cased because the server is not
    /// consistent about the case it hands a name back in, and two
    /// sections for one character would mean losing a set at random.
    /// </summary>
    static string Key(string player) => "char:" + (player ?? "").Trim().ToLowerInvariant();
}
