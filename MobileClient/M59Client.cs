using System;
#if GODOT
using Godot;
#endif
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
/// The data layer, with one hook the library does not offer.
///
/// InvalidateData (228) reaches `DataController.HandleInvalidateData`
/// (`Meridian59/Data/DataController.cs:2947-2951`), which throws away
/// every list whose contents the server has just declared stale by
/// calling `Invalidate()` (`:1006`). The library raises no event for
/// that, so nothing outside the data layer can hear it happen - and the
/// reference does not try to: it SUBCLASSES the controller and overrides
/// the method (`Meridian59.Ogre.Client/DataControllerOgre.cpp:61-71`),
/// calling the base and then telling the confirmation popup that its
/// world has been swept out from under it.
///
/// This client had no subclass at all, which is why there was nowhere to
/// put that call. The override is the whole of what this class is for -
/// base first, so that anything listening sees the data already cleared
/// rather than half-cleared, exactly as the reference orders it.
///
/// It lives in this file rather than its own because the offline tools
/// link `M59Client.cs` in by path and nothing else, and the client's
/// type parameter names this class; a separate file would break their
/// build without their csproj being touched.
/// </summary>
public class MobileData : DataController
{
    /// <summary>
    /// The server has invalidated its own data and the lists are now
    /// empty. Raised after the sweep, not before.
    /// </summary>
    public event Action Invalidated;

    public override void Invalidate()
    {
        base.Invalidate();

        // A listener that throws must not stop the message pump: an
        // invalidation arrives in the middle of a system save, and
        // losing the connection over a UI mistake is worse than the
        // mistake.
        try { Invalidated?.Invoke(); }
        catch (Exception e) { Complain($"[MobileData] invalidated: {e.Message}"); }
    }

    /// <summary>
    /// Prints where it can be seen - the engine's console when there is
    /// an engine, the terminal when this file is compiled into one of
    /// the offline tools.
    /// </summary>
    static void Complain(string text)
    {
#if GODOT
        GD.PrintErr(text);
#else
        Console.Error.WriteLine(text);
#endif
    }
}

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
public class M59Client : BaseClient<GameTick, ResourceManager, MobileData, Config>
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
    /// Raised for the client's own diagnostics: exception text from the
    /// sound, music and lookup handlers, login progress, the socket
    /// error. None of it belongs in the chat log - the reference never
    /// writes any of it to Data->ChatMessages (its exceptions go to the
    /// Ogre log and its login progress to nothing at all,
    /// `OgreClient.cpp:646-654`). The view prints it to the console and
    /// shows it only under M59DEBUG. <see cref="Notice"/> is left for
    /// what a player has to be told.
    /// </summary>
    public event Action<string> Diagnostic;

    void Diag(string s) => Diagnostic?.Invoke(s);

    /// <summary>
    /// The server's answer to a password change. The library's handlers
    /// are empty (`BaseClient.cs:708-719`); the reference overrides both
    /// to raise a popup (`OgreClient.cpp:1073-1083`). True means accepted.
    /// </summary>
    public event Action<bool> PasswordAnswered;

    protected override void HandlePasswordOKMessage(PasswordOKMessage Message)
    {
        try { PasswordAnswered?.Invoke(true); }
        catch (Exception e) { Diag($"password ok: {e.Message}"); }
    }

    protected override void HandlePasswordNotOKMessage(PasswordNotOKMessage Message)
    {
        try { PasswordAnswered?.Invoke(false); }
        catch (Exception e) { Diag($"password not ok: {e.Message}"); }
    }

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
        catch (Exception e) { Diag($"arrived: {e.Message}"); }
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
                catch (Exception e) { Diag($"stop sound: {e.Message}"); }
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
                catch (Exception e) { Diag($"music: {e.Message}"); }
                break;
        }

        base.HandleGameModeMessage(Message);

        if (_palette)
        {
            _palette = false;
            try { CharacterPalette?.Invoke(Data?.CharCreationInfo); }
            catch (Exception e) { Diag($"char info: {e.GetType().Name}: {e.Message}"); }
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
        catch (Exception e) { Diag($"sound: {e.GetType().Name}: {e.Message}"); }
    }

    protected override void HandleLookupNamesMessage(LookupNamesMessage Message)
    {
        try { NamesLookedUp?.Invoke(Message?.ResolvedIDs); }
        catch (Exception e) { Diag($"lookup: {e.GetType().Name}: {e.Message}"); }
    }

    protected override void HandleGetLoginMessage(GetLoginMessage Message)
    {
        ConnectionInfo info = Config.SelectedConnectionInfo;
        if (info == null) { Say("No connection selected."); return; }
        if (string.IsNullOrEmpty(info.Username)) { Say("No username configured."); return; }
        Diag($"Logging in as {info.Username}...");
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
                Diag("No characters yet - making one.");
                SendSystemMessageSendCharInfo();
                return;
            }

            // The selection screen, always. It used to be skipped when
            // there was exactly one character and the server reported no
            // empty slot, on the reasoning that one character is not a
            // choice - and that is wrong twice. The reference has no such
            // shortcut at all: `UIWelcome` goes up on every
            // CharactersMessage however many characters came with it
            // (`UIWelcome.cpp:80-130`). And the screen is not only a
            // chooser - it carries the message of the day, which a player
            // who owns one character would then never see, and it is the
            // one place an account's shape is visible, so "why can I not
            // make another?" had no screen that could answer it.
            //
            // PreferredCharacter above is still a bypass, because that is
            // the harness asking for a named character on purpose.
            if (ChooseCharacter != null)
            {
                Diag($"{real.Count} character{(real.Count == 1 ? "" : "s")} on this account" +
                     $"{(room ? "" : ", no free slot")}.");
                ChooseCharacter(real);
                return;
            }
            pick = real[0];
        }

        UseCharacter(pick);
    }

    /// <summary>
    /// Prints where it can be seen. The engine's own console when
    /// there is an engine; the terminal when this file is compiled
    /// into one of the offline tools, which is the only reason the
    /// Godot import above is conditional.
    /// </summary>
    static void Complain(string text)
    {
#if GODOT
        GD.PrintErr(text);
#else
        Console.Error.WriteLine(text);
#endif
    }

    /// <summary>
    /// Enters the world as this character.
    ///
    /// The id is copied into a BARE ObjectID rather than handed over as
    /// the CharSelectItem itself, and that is the whole point of the
    /// line. UseCharacterMessage writes its CharacterID through the
    /// virtual serializer - `cursor += CharacterID.WriteTo(Buffer,
    /// cursor)` (`UseCharacterMessage.cs:42`) over a virtual ByteLength
    /// (:33) - and CharSelectItem derives from ObjectID and overrides
    /// both to append NameLEN, the name and a flags byte after the id
    /// (`CharSelectItem.cs:65-80` for the array form, :96-112 for the
    /// pointer form). Passing the item therefore put a name and a flag
    /// on the wire inside a message whose body the protocol defines as
    /// an id and nothing else, so the very first packet of a session was
    /// malformed - a login the server is entitled to reject or
    /// mis-parse, and the worst possible place to be lax.
    ///
    /// Both paths in the reference pass a bare id: the welcome window
    /// goes through the by-index overload
    /// (`UIWelcome.cpp:175` -> `BaseClient.cs:869-879`, which does
    /// `new ObjectID(item.ID)`), and the library's own post-creation
    /// login does the same (`BaseClient.cs:567`). The Name argument is
    /// kept because it is a local bookkeeping string - it never reaches
    /// the buffer.
    /// </summary>
    public void UseCharacter(CharSelectItem pick)
    {
        if (pick == null) return;
        Diag($"Entering the world as {pick.Name}...");
        SendUseCharacterMessage(new ObjectID(pick.ID), true, pick.Name);
        EnteredGame?.Invoke(pick.Name);
    }

    /// <summary>
    /// Asks the view to confirm a suicide, and is handed the thing to do
    /// if the player says yes.
    ///
    /// `Suicide()` below is the only caller, and the shape is a
    /// callback rather than an event because there must be exactly one
    /// answerer: two subscribers would mean two popups over one
    /// keystroke, and a "yes" on each would send the command twice.
    /// </summary>
    public Action<Action> ConfirmSuicide;

    /// <summary>
    /// `/suicide` - the one chat command the library deliberately leaves
    /// unfinished.
    ///
    /// `ExecChatCommand` parses the word, reaches
    /// `case ChatCommandType.Suicide` and calls the virtual `Suicide()`
    /// (`BaseClient.cs:3103-3108`), which the base class defines as an
    /// empty body with a comment saying a subclass "must be overwritten
    /// with code calling SendUserCommandSuicide()"
    /// (`BaseClient.cs:3343-3349`). Nothing here overrode it, so the
    /// word parsed, dispatched and evaporated: a player could not kill
    /// their character at all, and got no error either, because as far
    /// as the parser was concerned the command had succeeded.
    ///
    /// The reference supplies exactly the missing half. `OgreClient`
    /// overrides Suicide to attach a listener and raise a yes/no popup
    /// reading "Are you sure?" (`OgreClient.cpp:1063-1069`), and the
    /// listener - and only the listener - sends the command
    /// (`OgreClient.cpp:1109-1112`, `SendUserCommandSuicide`). The
    /// confirmation is not decoration on an irreversible act typed as
    /// one word.
    ///
    /// That popup is the client's own in-game ConfirmPopup here, as it
    /// is there (`ControllerUI::ConfirmPopup::ShowChoice`) - never an
    /// engine or OS dialog. If no view has claimed
    /// <see cref="ConfirmSuicide"/> the command does nothing except say
    /// so: an unconfirmed suicide is a worse failure than a refused one.
    /// </summary>
    protected override void Suicide()
    {
        Action<Action> ask = ConfirmSuicide;
        if (ask == null)
        {
            Say("Cannot confirm a suicide right now - nothing sent.");
            return;
        }

        ask(() => SendUserCommandSuicide());
    }

    /// <summary>
    /// A character made in the wizard enters the world too.
    ///
    /// The library does not go back through UseCharacter for this - it
    /// calls SendUseCharacterMessage itself (BaseClient.cs:565-573) -
    /// so the view was never told, and the consequences were both
    /// invisible and serious: the login screen's own opaque background
    /// stayed drawn over the world for the whole session, and the
    /// "were we ever in the game?" flag stayed false, which meant every
    /// later disconnect was swallowed without a word. A player's first
    /// session on a new account got both.
    /// </summary>
    protected override void HandleCharInfoOKMessage(CharInfoOkMessage Message)
    {
        base.HandleCharInfoOKMessage(Message);
        try { EnteredGame?.Invoke(""); }
        catch (Exception e) { Diag($"entering: {e.Message}"); }
    }

    /// <summary>
    /// The world is about to be let go of, and anything worth keeping
    /// per character has to be kept NOW.
    ///
    /// Raised before `Data.Reset()` runs, because Reset is what empties
    /// `Data.ActionButtons` - including the player name the saved set is
    /// filed under. The reference does exactly this and in exactly this
    /// order: `OgreClient::HandleQuitMessage` writes the played action
    /// button set to its config and only then calls the base handler
    /// (`Meridian59.Ogre.Client/OgreClient.cpp:952-958`), and the base
    /// handler is the one that resets
    /// (`Meridian59/Client/BaseClient.cs:648-651`).
    /// </summary>
    public event Action Quitting;

    /// <summary>
    /// The world is gone. Raised after the data has been reset and the
    /// socket let go of, so the view has nothing left to read and its
    /// only job is to put the player somewhere they can start again.
    /// </summary>
    public event Action Quitted;

    /// <summary>
    /// Quit (149): the server is ending the session.
    ///
    /// It arrives for an ordinary logout, for a kick, and for a server
    /// shutdown, and the library's own handler does one thing with it -
    /// `Data.Reset()` (`Meridian59/Client/BaseClient.cs:648-651`). That
    /// is the data half. The reference adds the other two halves:
    /// persisting the played action buttons before the reset, and going
    /// back to the avatar-selection interface afterwards
    /// (`Meridian59.Ogre.Client/OgreClient.cpp:952-962`, whose
    /// DemoSceneLoadBrax leaves the client sitting in its login scene,
    /// the same place `HandleCharactersMessage` at `:946-949` switches to
    /// `UIMode::AvatarSelection` from).
    ///
    /// Nothing here handled it at all, and the consequence was not
    /// cosmetic: after a quit or a kick the HUD stayed up over a room
    /// that had just been emptied, every button still looking live,
    /// until the server got round to closing the socket - at which point
    /// the player was told they had lost their connection, which is not
    /// what happened and not something they can fix by reconnecting
    /// blindly.
    ///
    /// The socket goes here rather than being waited on. An explicit
    /// `Disconnect` is the only thing that marks the connection Offline
    /// (`Meridian59/Client/ServerConnection.cs:292`) - and, just as
    /// usefully, it means the close that follows a quit is an expected
    /// one rather than a read error arriving through
    /// `OnServerConnectionException` and raising `ConnectionLost` on top
    /// of a message the view has already shown.
    /// </summary>
    protected override void HandleQuitMessage(QuitMessage Message)
    {
        // Before the reset: the hotbar is filed under the character's
        // name and Reset takes the name with it.
        try { Quitting?.Invoke(); }
        catch (Exception e) { Complain($"[M59Client] quitting: {e.Message}"); }

        base.HandleQuitMessage(Message);

        try { Disconnect(); } catch { }

        try { Quitted?.Invoke(); }
        catch (Exception e) { Complain($"[M59Client] quit: {e.Message}"); }
    }

    /// <summary>
    /// The password was wrong, or the account has no room for a
    /// character.
    ///
    /// The library's own handlers only close the socket
    /// (BaseClient.cs:527-542) - the reference puts a sentence in front
    /// of the player and disconnects on OK (OgreClient.cpp:885-909),
    /// and disconnecting is what re-enables its login window. Neither
    /// was overridden here, so a mistyped password left the player
    /// looking at a login screen with a dead Connect button and no
    /// message at all. Nothing in the client said anything.
    /// </summary>
    protected override void HandleLoginFailedMessage(LoginFailedMessage Message)
    {
        base.HandleLoginFailedMessage(Message);
        Trouble("Login failed: that account name or password is not right.");
    }

    protected override void HandleNoCharactersMessage(NoCharactersMessage Message)
    {
        base.HandleNoCharactersMessage(Message);
        Trouble("Login failed: this account has no character slots.");
    }

    protected override void HandleGetClientMessage(GetClientMessage Message)
    {
        // The server wants a different client build than we claim to be.
        // It is a dead end, so the socket goes with the message: the
        // reference disconnects when its popup is dismissed
        // (OgreClient.cpp:921-929), and that is what lets you try again.
        Trouble($"Login failed: the server refused version {VersionMajor}.{VersionMinor} " +
                "and wants a patch. Either log in once with the classic client to " +
                "update, or set Version Major / Version Minor to match what the " +
                "server expects.");
    }

    protected override void HandleLoginModeMessageMessage(LoginModeMessageMessage Message)
    {
        // Whatever the server wants to say at login - maintenance, an
        // account held, a full server. The reference shows it and then
        // disconnects (OgreClient.cpp:911-919). Said as a failure so it
        // reaches the login screen rather than a chat log nobody is
        // looking at yet.
        Trouble("Login failed: " + Message.Message);
    }

    /// <summary>
    /// The resources on disk are not the ones the server expects.
    /// Handled here only so it does not pass in silence; there is no
    /// patcher on a phone.
    /// </summary>
    protected override void HandleDownloadMessage(DownloadMessage Message)
    {
        Trouble("Login failed: the server wants a different set of game files " +
                "than this device has.");
    }

    /// <summary>
    /// Says why the login did not work, and lets go of the socket.
    ///
    /// The wording begins with "Login failed" on purpose: that is what
    /// the view watches for to put the message on the login screen and
    /// give the Connect button back. Without the disconnect the client
    /// sits on a half-open socket with nothing to press.
    /// </summary>
    void Trouble(string why)
    {
        Say(why);
        try { Disconnect(); } catch { }
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
        Diag($"Connection error: {Error.GetType().Name}: {Error.Message}");
        try { ConnectionLost?.Invoke($"{Error.GetType().Name}: {Error.Message}"); }
        catch (Exception e) { Complain($"[M59Client] lost: {e.Message}"); }
    }
}
