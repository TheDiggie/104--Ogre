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
///
/// One row is this client's and not the game's: Go, which the library
/// has no button type for and which the phone binds anyway - see
/// `ActionButtons.Extra`. It needs no fifth field. It writes as the
/// Action row it is, with Go for a name, and the loader rebuilds it
/// rather than letting the constructor read that name; the long note at
/// the rebuild says why.
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

            // Every button, EMPTY ONES INCLUDED, in Num order. The game
            // writes the whole set - the loop at
            // `OgreClientConfig.cpp:1203-1211` runs over set->Count with
            // no test on the type - because its grid is positional and
            // an empty cell is a place the player chose to leave empty.
            // This used to skip Unset rows, which was harmless while the
            // cluster compacted; now that a hole is a hole (see
            // ActionButtons.Sync) a skipped row would move every later
            // binding up a seat on the next login.
            var ordered = new List<ActionButtonConfig>();
            foreach (ActionButtonConfig b in data.ActionButtons) if (b != null) ordered.Add(b);
            ActionButtons.Stable(ordered);

            var rows = new List<string>();
            foreach (ActionButtonConfig b in ordered)
            {
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

            string section = Key(data.ActionButtons.PlayerName);
            file.SetValue(section, "buttons", rows.ToArray());
            // WHICH ROW IS THE BIG BUTTON. The game has no such thing -
            // all forty-eight cells are the same size - so this is the
            // one field here with no `OgreClientConfig.cpp` line behind
            // it. It is a Num, not a row, because the primary is one of
            // the rows above and is written there like any other seat;
            // this only says which. Absent, the loader falls back to the
            // rule the cluster always had - the seat holding Attack is
            // the primary - so a file written before this line still
            // reads, with the same big button it had (ActionButtons.Primary).
            if (ActionButtons.PrimaryNum >= 0) file.SetValue(section, "primary", ActionButtons.PrimaryNum);
            else if (file.HasSectionKey(section, "primary")) file.EraseSectionKey(section, "primary");
            file.Save(Path);
        }
        catch (Exception e) { GD.PrintErr($"[HotbarStore] save: {e.Message}"); }
    }

    /// <summary>
    /// Whether the seats are locked against the clear gesture. GLOBAL,
    /// not per character and not per layout: the player's words were
    /// "so people dont accidentally remove their hotkeys while playing",
    /// which is a statement about the player's thumb, not about one
    /// character's bar. In this file rather than settings.cfg because
    /// OptionsPanel rewrites that file whole on every change (`Keep`),
    /// and a second writer on it is the race ServerList moved out of
    /// the way of; this file is Load-then-Save on every write, so a
    /// section of its own is safe here. Default LOCKED: a new player
    /// has not yet learnt the gesture that would empty a seat, and the
    /// one who wants it finds the padlock on the cluster.
    /// </summary>
    public static bool Locked
    {
        get { if (!_lockRead) ReadLock(); return _locked; }
        set
        {
            _lockRead = true;
            _locked = value;
            try
            {
                var file = new ConfigFile();
                file.Load(Path);
                file.SetValue("client", "locked", value);
                file.Save(Path);
            }
            catch (Exception e) { GD.PrintErr($"[HotbarStore] lock: {e.Message}"); }
        }
    }
    static bool _locked = true, _lockRead;

    static void ReadLock()
    {
        _lockRead = true;
        try
        {
            var file = new ConfigFile();
            if (file.Load(Path) != Error.Ok) return;
            _locked = (bool)file.GetValue("client", "locked", true);
        }
        catch (Exception e) { GD.PrintErr($"[HotbarStore] lock: {e.Message}"); }
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

            // Read before the list is touched: a garbage value here
            // throws out of the Variant cast, and a throw AFTER the list
            // had been cleared and refilled left the restored rows in
            // place with Load reporting false - so Seed then added its
            // eight on top and every Num was there twice.
            // And only an integer counts: a Variant holding "abc" casts
            // to 0 without complaint, which made whatever sat at Num 0
            // the big button.
            int primary = -1;
            if (file.HasSectionKey(section, "primary"))
            {
                Variant pv = file.GetValue(section, "primary", -1);
                if (pv.VariantType == Variant.Type.Int) primary = (int)pv;
            }

            var restored = new List<ActionButtonConfig>();
            // The Nums already taken. The game's grid is positional and a
            // Num names a cell, so two rows with one Num is a corrupt file
            // - and a corrupt one that was let through drew a seat
            // captioned Loot that fired Attack, because the press resolves
            // its config by Num and GetByNum answers the first. The
            // second row is moved to the next free Num rather than
            // dropped: the binding was the player's, the position was not.
            var taken = new HashSet<int>();
            // Past every Num the file names, so a moved row never lands
            // on one a later row is about to claim.
            int free = 0;
            foreach (string row in rows)
            {
                string[] f = row.Split('\t');
                if (f.Length >= 1 && int.TryParse(f[0], out int n) && n >= free) free = n + 1;
            }
            foreach (string row in rows)
            {
                string[] f = row.Split('\t');
                if (f.Length < 4) continue;
                if (!int.TryParse(f[0], out int num) || num < 0) continue;
                // TryParse accepts "999" as a member the enum does not
                // have, and a seat of no known type is a seat that draws
                // and does nothing when pressed.
                if (!Enum.TryParse(f[1], out ActionButtonType type) || !Enum.IsDefined(typeof(ActionButtonType), type)) continue;
                uint.TryParse(f[2], out uint same);
                if (!taken.Add(num))
                {
                    GD.PrintErr($"[HotbarStore] load: two rows at Num {num}; moving '{f[3]}' to {free}");
                    num = free++;
                    taken.Add(num);
                }

                // An empty seat, kept as one. The game's loader builds
                // the config whatever the type (`OgreClientConfig.cpp:630-636`)
                // and so keeps the hole; this one used to drop the row,
                // and a hole that does not survive a login is not a hole
                // the player can rely on.
                if (type == ActionButtonType.Unset)
                {
                    restored.Add(new ActionButtonConfig(num, ActionButtonType.Unset, ""));
                    continue;
                }

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
                // Dropping the row is what the game does with it too.
                // Go, which is an Action-typed row whose data is
                // AvatarAction.None (`ActionButtons.Extra`). It has to
                // be rebuilt rather than constructed, because the
                // constructor resolves an Action's data from its NAME,
                // and `GetAction` does not know "Go": its chain of
                // name tests ends in an else that answers **Wave**
                // (`Meridian59/Data/Models/ActionButtonConfig.cs:344-346`),
                // so the constructor would restore a saved Go as a Wave
                // button, captioned Wave (`:157`). Measured, not
                // reasoned: a row written as `Action 0 Bogus` comes back
                // on screen as "Wave".
                //
                // That same default is what makes None a safe marker in
                // the first place. GetAction can never RETURN None - the
                // unknown case is Wave - and `SetToAction` is only ever
                // called with a real action, so no library path builds
                // an Action button with None for data. The only thing
                // that does is `ActionButtons.SetToGo`, which is why a
                // slot carrying None is unambiguously the Go slot.
                //
                // Everything else keeps the library's own behaviour,
                // Wave for a name it does not know included: that is
                // what the game's loader does with the same row, and a
                // loader that second-guessed it here would differ from
                // the client this one is a port of.
                if (type == ActionButtonType.Action)
                {
                    var action = new ActionButtonConfig(num, ActionButtonType.Unset, "");
                    if (f[3] == ActionButtons.GoName) ActionButtons.SetToGo(action);
                    else action.SetToAction(ActionButtonConfig.GetAction(f[3]));
                    restored.Add(action);
                    continue;
                }

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
            // The primary's Num, where the file has one; -1 where it does
            // not, which is every file written before the big button
            // could hold anything but Attack - see Save, and
            // ActionButtons.Primary for what -1 then means. A Num no row
            // carries (primary=999) is the same as none: Primary would
            // fall back to the Attack seat anyway, and -1 says so in the
            // file instead of carrying the number forward on every Save.
            ActionButtons.PrimaryNum = primary >= 0 && taken.Contains(primary) ? primary : -1;
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
