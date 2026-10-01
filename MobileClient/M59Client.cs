using System;
using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meridian59.Client;
using Meridian59.Common;
using Meridian59.Common.Constants;
using Meridian59.Data;
using Meridian59.Data.Models;
using Meridian59.Files;
using Meridian59.Protocol.Enums;
using Meridian59.Protocol.GameMessages;

/// <summary>
/// Concrete game client.
///
/// BaseClient already implements the connection, the message dispatch and
/// the whole data layer - room, objects, inventory, the avatar - so this
/// only has to name the version it reports and drive the login handshake,
/// which is abstract because a real client would put a UI in the middle of
/// it. Here it runs automatically from the configured connection.
///
/// The sequence, once connected:
///   server -&gt; GetLogin        we reply with the username and password
///   server -&gt; LoginOK         accepted
///   server -&gt; Characters      we pick one and send UseCharacter
///   then the server starts sending room and object messages.
/// </summary>
public class M59Client : BaseClient<GameTick, ResourceManager, DataController, Config>
{
    /// <summary>
    /// The client version reported at login. If the server wants a
    /// different one it answers with GetClient and asks us to patch, so
    /// this is settable rather than compiled in - guessing wrong should
    /// cost a config change, not a rebuild.
    /// </summary>
    public byte VersionMajor { get; set; } = 5;
    public byte VersionMinor { get; set; } = 0;

    public override byte AppVersionMajor => VersionMajor;
    public override byte AppVersionMinor => VersionMinor;

    /// <summary>Character to use. Empty means the first non-empty slot.</summary>
    public string PreferredCharacter { get; set; } = "";

    /// <summary>Raised for anything the user should see: errors, server notices.</summary>
    public event Action<string> Notice;

    /// <summary>Raised once a character has been sent and play begins.</summary>
    public event Action<string> EnteredGame;

    /// <summary>
    /// Raised when the account has several characters and nothing said
    /// which one to use. Nothing is sent until <see cref="UseCharacter"/>
    /// is called with one of them, so the UI has as long as it needs.
    /// </summary>
    public event Action<IList<CharSelectItem>> ChooseCharacter;

    /// <summary>
    /// A sound the server wants played, with where and how loud. The data
    /// controller only looks at these to spot an "ouch" and set a health
    /// status; nothing in the library plays them, because playing is the
    /// engine's job. The Ogre client hooks the message stream for the
    /// same reason.
    /// </summary>
    public event Action<PlaySound> Sound;
    /// <summary>A looping sound the server wants stopped. Its own type,
    /// not a PlaySound - it carries only what is needed to find it.</summary>
    public event Action<StopSound> SoundStopped;
    /// <summary>The room's background music changed.</summary>
    public event Action<PlayMusic> Music;

    /// <summary>
    /// The server's answer to a name lookup: one id per name asked, in
    /// the order asked, zero where there is no such player. The base
    /// class deliberately does nothing with this - it is only ever
    /// asked for on the way to sending a mail, and only the view knows
    /// what it asked.
    /// </summary>
    public event Action<ObjectID[]> NamesLookedUp;

    /// <summary>
    /// The server's answer to SendSystemMessageSendCharInfo: every face
    /// part, colour, spell and skill a new character may be made from.
    /// The base class fills Data.CharCreationInfo and builds the
    /// default example model before this is raised, so what arrives
    /// here is ready to show.
    /// </summary>
    public event Action<CharCreationInfo> CharacterPalette;

    /// <summary>
    /// The server has put you in a room - a login, a door, a teleport.
    ///
    /// Raised for every PlayerMessage, including one that puts you back
    /// in a room you have been in before. The room OBJECT is the same
    /// one then (BaseClient.cs:628-635 reuses a loaded room and resets
    /// it), so a view that decides "did the room change?" by comparing
    /// instances misses the second visit entirely - and with it the
    /// stopping of the room's sounds and the rebuilding of the map.
    /// </summary>
    public event Action Arrived;

    void Say(string s) => Notice?.Invoke(s);

    /// <summary>
    /// Points the resource manager at the game files.
    ///
    /// BaseClient.Init assumes this project's own layout, with strings,
    /// rooms, bgftextures, bgfobjects, sounds, music and mails as
    /// subfolders of the resource path. An installed Meridian client is
    /// flat: everything sits in 'resource' together. Handing the library
    /// the subfolder layout against a flat install finds nothing, and
    /// ResourceManager.Init creates the missing folders on the way past,
    /// so it fails quietly with empty directories rather than an error.
    ///
    /// So look before assuming - and look for FILES, not just for the
    /// folder. ResourceManager.Init creates whatever is missing, so one
    /// run with the wrong layout leaves empty subfolders behind, and a
    /// check for the folder alone would then pick the wrong layout for
    /// ever afterwards. That is not hypothetical: it is what happened
    /// while testing this.
    /// </summary>
    public override void Init()
    {
        string root = Config.ResourcesPath;
        if (HasFiles(Path.Combine(root ?? "", Meridian59.Files.ResourceManager.SUBPATHOBJECTS), "*.bgf"))
        {
            base.Init();
            return;
        }

        ResourceManager.Init(root, root, root, root, root, root, root);
    }

    /// <summary>
    /// The string file to use for a connection.
    ///
    /// This was hardcoded to rsc0000.rsb, which is the usual name and not
    /// a guarantee. Connect() selects the dictionary by that name and gets
    /// nothing back if it is wrong, after which the server's room and
    /// object names resolve to nothing - a silent failure of the same
    /// shape as the resource layout one. Looking in the folder costs
    /// nothing and is right more often than a guess.
    /// </summary>
    public static string FindStringDictionary(string resourceDir)
    {
        foreach (string dir in new[]
        {
            Path.Combine(resourceDir ?? "", Meridian59.Files.ResourceManager.SUBPATHSTRINGS),
            resourceDir ?? "",
        })
        {
            try
            {
                string found = Directory.EnumerateFiles(dir, "*" + FileExtensions.RSB)
                                        .OrderBy(x => x).FirstOrDefault();
                if (found != null) return Path.GetFileName(found);
            }
            catch { }
        }
        return "rsc0000.rsb";
    }

    static bool HasFiles(string dir, string pattern)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            foreach (string _ in Directory.EnumerateFiles(dir, pattern)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Passes the sound messages out before the base class handles them.
    /// Base first would work too - it does not consume them - but the
    /// order is worth being deliberate about: the view should hear about
    /// a sound at the same tick the data model does.
    /// </summary>
    protected override void HandlePlayerMessage(PlayerMessage Message)
    {
        base.HandlePlayerMessage(Message);
        try { Arrived?.Invoke(); }
        catch (Exception e) { Say($"arrived: {e.Message}"); }
    }

    protected override void HandleGameModeMessage(GameModeMessage Message)
    {
        switch ((MessageTypeGameMode)Message.PI)
        {
            case MessageTypeGameMode.PlayWave:
                Raise(Sound, ((PlayWaveMessage)Message).PlayInfo);
                break;

            case MessageTypeGameMode.StopWave:
                try
                {
                    StopSound quiet = ((StopWaveMessage)Message).PlayInfo;
                    quiet?.ResolveResources(ResourceManager, false);
                    if (quiet != null) SoundStopped?.Invoke(quiet);
                }
                catch (Exception e) { Say($"stop sound: {e.Message}"); }
                break;

            case MessageTypeGameMode.CharInfo:
                // Raised after the base call below, not here: the data
                // layer is what turns this into a CharCreationInfo with
                // an example model on it, and the view has nothing to
                // show until it has.
                _palette = true;
                break;

            // A MIDI track and an Ogg track arrive as two different
            // messages carrying the same PlayMusic, and the reference
            // sends both to the same StartMusic
            // (`ControllerSound.cpp:248-253`). Only one of them was
            // handled here, so a room whose music comes as PlayMidi was
            // silent.
            case MessageTypeGameMode.PlayMidi:
            case MessageTypeGameMode.PlayMusic:
                try
                {
                    PlayMusic tune = Message is PlayMidiMessage midi
                        ? midi.PlayInfo
                        : ((PlayMusicMessage)Message).PlayInfo;
                    // Like a wave, this arrives as a string-resource id and
                    // has to be turned into a filename before anything can
                    // open it.
                    tune?.ResolveResources(ResourceManager, false);
                    if (tune != null) Music?.Invoke(tune);
                }
                catch (Exception e) { Say($"music: {e.Message}"); }
                break;
        }

        base.HandleGameModeMessage(Message);

        if (_palette)
        {
            _palette = false;
            try { CharacterPalette?.Invoke(Data?.CharCreationInfo); }
            catch (Exception e) { Say($"char info: {e.GetType().Name}: {e.Message}"); }
        }
    }

    bool _palette;

    void Raise(Action<PlaySound> handler, PlaySound info)
    {
        // A sound that cannot be resolved is not worth an exception, and
        // a view that throws while handling one must not take the
        // connection down with it.
        try
        {
            if (info == null || handler == null) return;
            info.ResolveResources(ResourceManager, false);
            handler(info);
        }
        catch (Exception e) { Say($"sound: {e.GetType().Name}: {e.Message}"); }
    }

    protected override void HandleLookupNamesMessage(LookupNamesMessage Message)
    {
        try { NamesLookedUp?.Invoke(Message?.ResolvedIDs); }
        catch (Exception e) { Say($"lookup: {e.GetType().Name}: {e.Message}"); }
    }

    protected override void HandleGetLoginMessage(GetLoginMessage Message)
    {
        ConnectionInfo info = Config.SelectedConnectionInfo;
        if (info == null) { Say("No connection selected."); return; }
        if (string.IsNullOrEmpty(info.Username)) { Say("No username configured."); return; }
        Say($"Logging in as {info.Username}...");
        SendLoginMessage(info.Username, info.Password);
    }

    protected override void HandleLoginOKMessage(LoginOKMessage Message)
    {
        Say("Login accepted.");
    }

    protected override void HandleCharactersMessage(CharactersMessage Message)
    {
        var chars = Message.WelcomeInfo?.Characters;
        if (chars == null || chars.Count == 0) { Say("No characters on this account."); return; }

        CharSelectItem pick = null;
        if (!string.IsNullOrWhiteSpace(PreferredCharacter))
            pick = chars.FirstOrDefault(c =>
                !c.IsEmptySlot &&
                string.Equals(c.Name, PreferredCharacter, StringComparison.OrdinalIgnoreCase));

        if (pick == null)
        {
            var real = chars.Where(c => !c.IsEmptySlot).ToList();
            bool room = chars.Any(c => c.IsEmptySlot);

            // A new account is nothing but empty slots, and this used to
            // stop dead on it - which made the client unusable for
            // anyone who had not already made a character elsewhere.
            if (real.Count == 0)
            {
                if (!room) { Say("No characters on this account."); return; }
                Say("No characters yet - making one.");
                SendSystemMessageSendCharInfo();
                return;
            }

            // One character is not a choice; several is, and picking the
            // first silently would log you in as the wrong one. An empty
            // slot is a choice too, because it is the only way to reach
            // the creation wizard.
            if ((real.Count > 1 || room) && ChooseCharacter != null)
            {
                Say($"{real.Count} character{(real.Count == 1 ? "" : "s")} on this account.");
                ChooseCharacter(real);
                return;
            }
            pick = real[0];
        }

        UseCharacter(pick);
    }

    /// <summary>Enters the world as this character.</summary>
    public void UseCharacter(CharSelectItem pick)
    {
        if (pick == null) return;
        Say($"Entering the world as {pick.Name}...");
        SendUseCharacterMessage(pick, true, pick.Name);
        EnteredGame?.Invoke(pick.Name);
    }

    protected override void HandleGetClientMessage(GetClientMessage Message)
    {
        // The server wants a different client build than we claim to be.
        Say($"Server refused version {VersionMajor}.{VersionMinor} and wants a patch. " +
            "Either log in once with the classic client to update, or set " +
            "Version Major / Version Minor on the node to match what the " +
            "server expects.");
    }

    protected override void HandleLoginModeMessageMessage(LoginModeMessageMessage Message)
    {
        Say(Message.Message);
    }

    /// <summary>
    /// The connection failed. Raised as well as logged, because a
    /// dropped socket has to reach the screen.
    ///
    /// Watching ConnectionState instead does not work: only an explicit
    /// Disconnect sets it to Offline
    /// (`ServerConnection.cs:292`), so after a broken pipe the client
    /// still reports itself as playing and nothing can tell that the
    /// world on screen is a photograph. The error event is the only
    /// honest signal there is.
    /// </summary>
    public event Action<string> ConnectionLost;

    protected override void OnServerConnectionException(Exception Error)
    {
        Say($"Connection error: {Error.GetType().Name}: {Error.Message}");
        try { ConnectionLost?.Invoke($"{Error.GetType().Name}: {Error.Message}"); }
        catch (Exception e) { GD.PrintErr($"[M59Client] lost: {e.Message}"); }
    }
}
