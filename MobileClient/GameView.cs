using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Common;
using Meridian59.Common.Constants;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Files.BGF;
using Meridian59.Files.ROO;

/// <summary>
/// Live view of a Meridian 59 server: connects, logs in, and renders the
/// room the avatar is standing in with the objects the server reports.
///
/// The renderer is the same one FirstPersonView uses. The difference is
/// where the world comes from - a file on disk there, the server here.
///
/// Credentials are read from the environment (M59USER / M59PASS) unless
/// set in the inspector. They are deliberately not stored in the scene.
/// </summary>
public partial class GameView : Node2D
{
    [Export] public string Host = "3.141.65.36";
    [Export] public int Port = 5959;
    [Export] public string Username = "";
    [Export] public string Password = "";
    [Export] public string Character = "";
    [Export] public string ResourceDir = "";
    /// <summary>
    /// Internal buffer height. The HEIGHT is the fixed side, and the
    /// width follows the screen: a phone held sideways is 16:9 or 21:9,
    /// and sizing by width there left 221 rows to draw the world in.
    /// 432 rows at 21:9 is 936x432, which is the same number of pixels
    /// a shot cost when this client was portrait.
    /// </summary>
    [Export] public int RenderHeight = 432;

    /// <summary>Widest buffer to allow, whatever the screen's aspect.</summary>
    [Export] public int RenderWidthCap = 1280;
    [Export] public bool AutoConnect = true;
    /// <summary>Client version reported at login; see M59Client.</summary>
    [Export] public int VersionMajor = 5;
    [Export] public int VersionMinor = 0;
    /// <summary>
    /// Keyboard turn rate, radians a second. The reference's own:
    /// KEYROTATESPEED 0.00012 (ControllerInput.h:49) times the default
    /// KeyRotateSpeed of 25 (OgreClientConfig.h:61) times milliseconds,
    /// which is 3 radians a second. It was 2.2 here, which is a guess.
    /// </summary>
    [Export] public float TurnSpeed = 3.0f;
    /// <summary>
    /// Hold to WALK. The reference runs by default and slows you with
    /// the walk key (ControllerInput.cpp:950, OISKeyBinding.cpp:44);
    /// this used to be a run flag, which had it backwards.
    /// </summary>
    [Export] public bool Walk = false;

    /// <summary>
    /// Autorun: the reference's AutoMove, which is a key there
    /// (`OISKeyBinding`, dispatched at `ControllerInput.cpp:566`) and
    /// a button here. Walking forward on a phone means holding a thumb
    /// on a stick for as long as the walk lasts, which is the one
    /// thing a keyboard never asked of anybody.
    /// </summary>
    bool _autoMove;

    /// <summary>
    /// Whether autorun was switched on while already walking. The
    /// reference keeps the same flag (isAutoMoveOnMove): turning it on
    /// mid-walk means the release that follows is the one you were
    /// already making, so it clears this rather than cancelling the
    /// autorun you just asked for.
    /// </summary>
    bool _autoMoveOnMove;

    readonly M59Assets _assets = new M59Assets();
    readonly TouchControls _touch = new TouchControls();
    ChatOverlay _chat;

    /// <summary>
    /// Every word the library's chat parser treats as a command, read
    /// off the ChatCommand classes themselves rather than listed here -
    /// there are over thirty, in three languages, and a list copied by
    /// hand would be wrong the first time one was added.
    /// </summary>
    static readonly System.Collections.Generic.HashSet<string> ChatWords = BuildChatWords();

    /// <summary>
    /// Classifies one submitted line, by parsing it EXACTLY ONCE.
    ///
    /// The old shape asked a bool question - `IsCommand(line)` - and
    /// then let `ExecChatCommand` parse the same line all over again.
    /// Two things were wrong with that, and both of them shipped:
    ///
    /// 1. `ChatCommand.Parse` is NOT pure, whatever the comment that
    ///    used to sit here claimed. `ParseTell`, `ParseCast`,
    ///    `ParseGoPlayer` and `ParseGetPlayer` push a ServerString into
    ///    `Data.ChatMessages` on every failure path
    ///    (`ChatCommand.cs:400-405`, `:411-412`, `:432-433`, `:470-474`,
    ///    `:631-633`, `:640-641`, `:645-647`, `:700-701`, `:729-731`,
    ///    `:735-737`). Parsing twice therefore printed every such error
    ///    twice: "No player or group with prefix: Alice" on two
    ///    consecutive rows, photographed. The reference parses once
    ///    (`UIChat.cpp:274-281` hands the raw line to ExecChatCommand
    ///    and that is the only parse in the path).
    ///
    /// 2. A null parse was read as "this is speech". Every MALFORMED
    ///    command parses to null - `tell Alice` with the message
    ///    missing, a tell to someone who just logged off, `deposit all`,
    ///    a typo'd command word - so the whole line went out as SayTo to
    ///    everyone in the room. `tell Zorak my guild password is
    ///    hunter2` was captured on the wire doing exactly that. The
    ///    reference sends NOTHING for a null parse: it falls off the end
    ///    of `BaseClient.cs:3033-3045` at `:3200-3202`.
    ///
    /// So the single parse is done here and its result is what the
    /// caller acts on. <paramref name="said"/> reports whether the
    /// library itself put something in the chat log while parsing, which
    /// is how the caller knows whether a failed command needs a word of
    /// its own or has already been explained.
    /// </summary>
    Meridian59.Data.Models.ChatCommand ParseChat(string text, out bool said)
    {
        said = false;
        if (string.IsNullOrEmpty(text)) return null;

        var log = _client?.Data?.ChatMessages;
        int before = log?.Count ?? 0;
        try
        {
            var parsed = Meridian59.Data.Models.ChatCommand.Parse(
                text, _client.Data, _client.Config);
            said = (log?.Count ?? 0) > before;
            return parsed;
        }
        catch (Exception e)
        {
            // A parser that throws is not a licence to broadcast the
            // line instead. The caller treats a throw as a command that
            // failed, which is silent, rather than as speech.
            said = (log?.Count ?? 0) > before;
            GD.PrintErr($"[GameView] chat parse: {e.Message}");
            throw new ChatCommandFailed();
        }
    }

    /// <summary>A parse that threw. Carries no message; see ParseChat.</summary>
    class ChatCommandFailed : Exception { }

    /// <summary>
    /// The first word of a line, after the one alias expansion the
    /// parser itself would do (`ChatCommand.cs:66-86`).
    /// </summary>
    string FirstWord(string line)
    {
        string expanded = AliasStore.Expand(_client?.Config, line)?.Trim() ?? "";
        int cut = expanded.IndexOf(' ');
        return (cut < 0 ? expanded : expanded.Substring(0, cut)).ToLowerInvariant();
    }

    /// <summary>
    /// Whether the line opens with a word the library's parser treats as
    /// a command. A line that does is never said aloud, however badly it
    /// is formed: that is the whole of finding 1.
    /// </summary>
    bool OpensWithCommandWord(string line)
        => ChatWords.Contains(FirstWord(line));

    /// <summary>
    /// The six commands that take no argument and, unlike every other
    /// no-argument command in the same switch, are not length-checked.
    ///
    /// `ChatCommandBalance` (`ChatCommand.cs:199-203`),
    /// `ChatCommandSuicide` (`:206-211`) and `ChatCommandTime`
    /// (`:293-298`) all guard on `splitted.Length == 1`. Rest, Stand,
    /// Quit, Dance, Point and Wave (`:239-266`) do not, so the parser
    /// accepts "wave goodbye everyone" and "quit messing about" as the
    /// bare command with the rest of the sentence discarded.
    ///
    /// On the desktop that parser sits behind a command line and the
    /// cost is a shrug. Here it sits behind a button labelled Say, in a
    /// box whose placeholder reads "say something", so the same line
    /// waves at the room and says nothing - and "quit ..." logs you out
    /// mid-sentence. The documented departure above licenses saying
    /// plain text and NOTHING more, so the tie is broken the way the
    /// library itself breaks it everywhere it remembered to: a
    /// no-argument command is a one-word line. With anything after it,
    /// the line is a sentence.
    /// </summary>
    static readonly System.Collections.Generic.HashSet<Meridian59.Common.Enums.ChatCommandType>
        WordlessCommands = new System.Collections.Generic.HashSet<Meridian59.Common.Enums.ChatCommandType>
        {
            Meridian59.Common.Enums.ChatCommandType.Rest,
            Meridian59.Common.Enums.ChatCommandType.Stand,
            Meridian59.Common.Enums.ChatCommandType.Quit,
            Meridian59.Common.Enums.ChatCommandType.Dance,
            Meridian59.Common.Enums.ChatCommandType.Point,
            Meridian59.Common.Enums.ChatCommandType.Wave,
        };

    /// <summary>
    /// A parse that succeeded but that this client declines to treat as
    /// a command: a no-argument command word with a sentence after it.
    /// See <see cref="WordlessCommands"/>.
    /// </summary>
    bool SentenceNotCommand(Meridian59.Data.Models.ChatCommand cmd, string line)
    {
        if (cmd == null || !WordlessCommands.Contains(cmd.CommandType)) return false;
        string expanded = AliasStore.Expand(_client?.Config, line)?.Trim() ?? "";
        return expanded.IndexOf(' ') >= 0;
    }

    /// <summary>
    /// The text as the library's parser needs to see it.
    ///
    /// `ChatCommand.Parse` switches on the word exactly as typed
    /// (ChatCommand.cs:60, :97) and looks an alias up with `==`
    /// (`KeyValuePairStringList.cs:47-53`), and an Android keyboard
    /// sentence-cases the first word by default. So "Tell Alice hi"
    /// matched nothing and "Chuckle" matched no alias. Both went out as
    /// speech, which for a tell is the leak described above.
    ///
    /// Only the first word is touched, and only when lowering it turns a
    /// miss into a hit - either a command word or, via AliasStore, the
    /// single alias key that matches case-insensitively. The rest of the
    /// line may be a player's name and is left exactly as typed. There
    /// is no minimum length any more: "T alice hi" is the tell it looks
    /// like.
    /// </summary>
    string Commandable(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string trimmed = text.TrimStart();
        int cut = trimmed.IndexOf(' ');
        string first = cut < 0 ? trimmed : trimmed.Substring(0, cut);
        if (first.Length == 0) return text;

        string fixedFirst = null;
        string lower = first.ToLowerInvariant();
        if (ChatWords.Contains(lower)) fixedFirst = lower;
        else fixedFirst = AliasStore.CanonicalKey(_client?.Config, first);

        if (fixedFirst == null || fixedFirst == first) return text;
        return fixedFirst + (cut < 0 ? "" : trimmed.Substring(cut));
    }

    static System.Collections.Generic.HashSet<string> BuildChatWords()
    {
        var words = new System.Collections.Generic.HashSet<string>();
        try
        {
            Type baseType = typeof(Meridian59.Data.Models.ChatCommand);
            foreach (Type t in baseType.Assembly.GetTypes())
            {
                if (!baseType.IsAssignableFrom(t)) continue;
                foreach (System.Reflection.FieldInfo f in t.GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                    if (!f.Name.StartsWith("KEY")) continue;
                    if (f.GetRawConstantValue() is string w && w.Length > 0)
                        words.Add(w.ToLowerInvariant());
                }
            }
        }
        catch (Exception e) { GD.PrintErr($"[GameView] chat words: {e.Message}"); }
        return words;
    }
    ActionBar _actions;
    CharacterPicker _picker;

    /// <summary>
    /// The id of the first empty character slot the server offered, for
    /// the creation request to name. See the NewWanted handler.
    /// </summary>
    uint _emptySlot;
    MiniMap _map;
    SplashNotifier _splash;
    LostConnection _lost;
    bool _wasInGame;
    bool _bagWasOpen;
    /// <summary>The shop line the amount prompt was opened for, if any.</summary>
    // The shop line the amount prompt is for, by ID: the merchant re-sends
    // its whole stock as new objects, so the object would be orphaned.
    uint? _amountFor;
    /// <summary>The trade line the amount prompt was opened for, if any.</summary>
    ObjectBase _amountForTrade;
    uint _targetBeforeBag = uint.MaxValue;
    RoomBuffsPanel _roomBuffs;
    Button _loot, _go, _auto;
    LootPanel _lootList;
    ActionsPanel _acts;
    LootPanel _contents;
    BuyPanel _shop;
    AttributesPanel _sheet;
    AmountPrompt _amount;
    PlayersPanel _players;
    QuestsPanel _quests;
    MailPanel _mail;
    NewsPanel _news;
    OptionsPanel _options;
    AliasEditor _aliases;
    HudEditor _hudEditor;
    float _bright = 0f;
    GuildPanel _guild;
    GuildShieldPanel _shieldDesigner;
    GuildHallBuyPanel _hallBuy;
    GuildCreatePanel _guildCreate;
    ConfirmPopup _ask;
    StatsWizard _wizard;
    CreateCharacter _newChar;
    NpcQuestsPanel _npcQuests;
    TradePanel _trade;
    /// <summary>Who asked the bag for something: the trade, or a container.</summary>
    enum PickFor { Nobody, Trade, Container }
    PickFor _pickFor = PickFor.Nobody;
    LoginPrompt _login;
    RichTextLabel _crash;
    string _resDir = "";

    /// <summary>
    /// The servers there are to log into, and which one is showing.
    ///
    /// The reference keeps this list in Config->Connections and the
    /// choice in Config->SelectedConnectionIndex, which is what
    /// BaseClient.Connect reads (`BaseClient.cs:114-141`). Held here
    /// rather than there because the choice has to be made BEFORE the
    /// client exists - Config.Load runs inside Begin, and Begin is what
    /// login starts. See ServerList.
    /// </summary>
    System.Collections.Generic.List<ServerList.Entry> _servers;
    int _serverPick;
    /// <summary>The string dictionary found on disk; see M59Client.FindStringDictionary.</summary>
    string _strings = "";

    /// <summary>The server about to be connected to, or a last resort.</summary>
    ServerList.Entry Chosen =>
        _servers != null && _serverPick >= 0 && _serverPick < _servers.Count
            ? _servers[_serverPick]
            : new ServerList.Entry(Host, Host, Port, _strings);
    NameTags _names;
    QuestMarkers _questMarks;
    ScreenEffects _fx;
    CanvasLayer _fxLayer;
    PlayerOverlays _overlays;
    ActionButtons _hotbar;
    LookPanel _look;
    SpellsPanel _book;
    M59Sound _sound;
    Vitals _vitals;
    AvatarPanel _face;
    InventoryPanel _bag;

    /// <summary>
    /// Controls live under a CanvasLayer, not directly under this Node2D.
    /// A Control resolves its anchors against its parent's rect, and a
    /// Node2D has none, so a full-rect backdrop or a bottom-anchored log
    /// collapses to nothing. A CanvasLayer gives them the viewport, and
    /// draws above the rendered frame whatever order things were added in.
    /// </summary>
    CanvasLayer _ui;
    ResourcePrompt _prompt;
    UnpackScreen _unpack;
    /// <summary>
    /// The on-screen scratch log - the corner readout, and the one place
    /// a failure shows up when nothing else on screen will say it.
    /// </summary>
    readonly List<string> _log = new List<string>();

    /// <summary>
    /// How many lines of _log are kept. Six is what fits above the HUD
    /// without covering the world.
    /// </summary>
    const int LogLines = 6;

    /// <summary>
    /// Adds a line to the on-screen log, oldest out.
    ///
    /// Everything that appends goes through here now. Two of the three
    /// callers trimmed and one did not: Fail appended without a bound,
    /// and Fail is what the per-message catch in Pump calls - which runs
    /// at frame rate. A resource that throws every time it is touched
    /// therefore grew this list by about sixty strings a second, for as
    /// long as the app was left up, and RenderFrame joined all of them
    /// into the status label every one of those frames. A leak and a
    /// slowdown out of an error path, which is the worst place for one.
    /// </summary>
    void Note(string line)
    {
        _log.Add(line);
        while (_log.Count > LogLines) _log.RemoveAt(0);
    }
    string _passwordBefore, _passwordAfter;
    /// <summary>
    /// What to do when the server answers a password change, held rather
    /// than subscribed because Settings runs before there is a client to
    /// subscribe to. See Settings.
    /// </summary>
    Action<bool> _passwordAnswer;

    M59Client _client;
    WorldSync _world;

    Image _image;
    ImageTexture _texture;
    uint[] _px;
    byte[] _rgba;
    int _w, _h;

    Label _status;

    /// <summary>
    /// Whether the diagnostics go on screen. M59DEBUG=1 turns them on;
    /// the harnesses set it, and so can anyone chasing a frame rate.
    /// </summary>
    /// <remarks>
    /// Read in _Ready rather than in a field initialiser: the harness
    /// sets the variable from its own scene, and a static field would
    /// have been computed first or last depending on which type the
    /// runtime happened to touch first.
    /// </remarks>
    bool Debugging;
    StatusBar _bar;
    string _state = "starting";
    double _fpsAccum; int _frames; string _fps = "";
    float _clock;
    float _pitch;

    public override void _Ready()
    {
        // Anything that escapes here reaches Godot's own crash overlay,
        // which shows one line and no stack. On a phone that is all you
        // get - there is no console to read and no log to open - so the
        // exception is caught and put on the screen instead.
        try { Boot(); }
        catch (Exception e) { Boom("startup", e); }
    }

    /// <summary>
    /// Puts the whole exception on the screen, because on a phone there
    /// is nowhere else for it to go.
    /// </summary>
    void Boom(string where, Exception e) => Boom(where, e.ToString());

    /// <summary>
    /// The same full-screen report for a failure that is already a
    /// string - the resource manager hands back its trace that way.
    /// A stack trace in the one-line status label is a stack trace
    /// nobody can read, which on a phone means nobody can report it.
    /// </summary>
    void Boom(string where, string detail)
    {
        GD.PrintErr($"[GameView] {where}: {detail}");
        if (_crash != null) return;

        _crash = new RichTextLabel
        {
            BbcodeEnabled = false,
            SelectionEnabled = true,
            ScrollFollowing = false,
            Text = $"{where} failed" + "\n\n" + detail,
        };
        _crash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _crash.OffsetLeft = 12; _crash.OffsetTop = 40;
        _crash.OffsetRight = -12; _crash.OffsetBottom = -12;
        _crash.AddThemeFontSizeOverride("normal_font_size", 15);
        _crash.AddThemeColorOverride("default_color", new Color(1, 0.75f, 0.7f));

        var bg = new ColorRect { Color = new Color(0.05f, 0.02f, 0.02f, 0.97f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var layer = new CanvasLayer { Layer = 100 };
        layer.AddChild(bg);
        layer.AddChild(_crash);
        AddChild(layer);
        // A stack trace that runs off the curve is a stack trace with
        // the interesting line missing.
        SafeArea.Apply(layer, GetViewportRect().Size);
    }

    void Boot()
    {
        // Threads and deferred calls do not come back through Boot's
        // catch, and on a phone an exception that escapes is a grey
        // screen with one line on it.
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is Exception ex)
                Callable.From(() => Boom("something", ex)).CallDeferred();
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Exception ex = a.Exception;
            Callable.From(() => Boom("a background task", ex)).CallDeferred();
            a.SetObserved();
        };

        _ui = new CanvasLayer();
        AddChild(_ui);

        // Where the player put their HUD. Read before any piece lays
        // itself out, so the first frame is already the layout they
        // saved rather than the designer's with a jump a frame later.
        // Registering late is still safe - see M59Hud.Register - this is
        // only about the first frame.
        M59Hud.Load();

        // Everything on this layer is kept off the glass's edge - the
        // system's own cutouts, plus a margin for the curve, which
        // nothing reports. The world underneath keeps the whole
        // screen. See SafeArea.
        SafeArea.Apply(_ui, GetViewportRect().Size);
        GetViewport().SizeChanged += () => SafeArea.Apply(_ui, GetViewportRect().Size);

        // Below the avatar block, which owns the corner.
        Debugging = System.Environment.GetEnvironmentVariable("M59DEBUG") == "1";

        _status = new Label { Position = new Vector2(12, 132) };
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _status.AddThemeConstantOverride("outline_size", 4);
        _ui.AddChild(_status);

        // On Android the game data has to be copied out of the .pck before
        // the library, which reads with System.IO, can see any of it. That
        // is hundreds of megabytes on first run, so it happens off the main
        // thread - doing it here would freeze the app long enough for
        // Android to decide it had hung.
        if (M59Paths.NeedsUnpack())
        {
            Unpack();
            return;
        }

        FindResources();
    }

    /// <summary>
    /// Copies the bundled game data out, on a worker thread, behind the
    /// client's own progress screen - and stops here if it cannot.
    ///
    /// That last clause is the whole change. This used to call
    /// UnpackIfNeeded for its side effects and go straight on to
    /// FindResources whatever came back, so a -1 from a failed
    /// CreateDirectory and a disk that filled up on file two both
    /// arrived at the login screen looking healthy. Resolve then found
    /// the part-written folder - one .roo in it was enough for the old
    /// HasContent test (M59Paths.cs:57) - and the player logged in to a
    /// blank world with a live HUD, because RenderFrame draws nothing
    /// without a room (see :3274 below) and nothing anywhere said why.
    /// Clearing app data was the only cure and no screen mentioned it.
    ///
    /// Now: a refusal is a screen with the reason on it and a button, the
    /// marker means an interrupted copy resumes from where it stopped,
    /// and the only path onward to FindResources is a finished unpack.
    /// </summary>
    // True from the moment the worker is handed the job until its
    // verdict comes back. Try again is a button, and two taps in quick
    // succession would otherwise put two threads on the same files.
    bool _unpacking;

    void Unpack()
    {
        if (_unpacking) return;
        _unpacking = true;

        if (_unpack == null)
        {
            _unpack = new UnpackScreen();
            _unpack.Retry += Unpack;
            _ui.AddChild(_unpack);
        }
        _status.Text = "";
        _unpack.Working("Starting...");

        System.Threading.Tasks.Task.Run(() =>
        {
            // Callable.From rather than a method name: these are
            // private methods, so the engine has no name for them.
            try
            {
                M59Paths.UnpackReport r = M59Paths.UnpackIfNeeded(
                    msg => Callable.From(() => _unpack?.Working(msg)).CallDeferred());
                Callable.From(() => Unpacked(r)).CallDeferred();
            }
            catch (Exception e)
            {
                // A task's exception is unobserved, so it would
                // otherwise be a silent hang on the unpack screen.
                Callable.From(() => { _unpacking = false; Boom("unpacking", e); })
                    .CallDeferred();
            }
        });
    }

    /// <summary>What to do with the unpack's verdict. Main thread only.</summary>
    void Unpacked(M59Paths.UnpackReport r)
    {
        _unpacking = false;
        if (!r.Ok)
        {
            _unpack?.Problem(r.Problem ?? "The game data could not be installed.");
            return;
        }

        // A file that shipped only as Godot's own converted copy cannot
        // be unpacked, and on this client that means silence: the
        // library reads sound and music as .ogg off the disk
        // (ResourceManager.cs:583,595). It is a packaging fault rather
        // than a device one, so it does not stop the game - but it is
        // said out loud, because "the game has no sound" is otherwise a
        // bug report with nothing in it. See M59Paths.Scan.
        System.Collections.Generic.List<string> lost = r.Lost;
        if (lost != null && lost.Count > 0)
            Note($"{lost.Count} data file(s) missing from this build " +
                 $"(e.g. {lost[0]}) - sound and music may be silent.");

        _unpack?.Done();
        FindResources();
    }

    /// <summary>Status text from a worker thread.</summary>
    void SetStatus(string text) { if (_status != null) _status.Text = text; }

    /// <summary>Finds the resource folder, or asks. Main thread only.</summary>
    void FindResources()
    {
        try { Look(); }
        catch (Exception e) { Boom("finding the game files", e); }
    }

    void Look()
    {
        _status.Text = "";
        string dir = M59Paths.Resolve(ResourceDir);

        if (dir == null)
        {
            // Asking beats stopping - see the note in FirstPersonView.
            _prompt = new ResourcePrompt();
            _prompt.Accepted += Start;
            _ui.AddChild(_prompt);
            _prompt.Ask(M59Paths.NotFoundMessage());
            return;
        }

        Start(dir);
    }

    /// <summary>Everything that needs the resource folder, once it is known.</summary>
    void Start(string dir)
    {
        if (!_assets.Init(dir))
        {
            // A folder that is simply not there is the player's to fix,
            // and putting the crash screen over it would bury the
            // prompt that lets them fix it. A throw is a different
            // thing and wants its whole stack, on a screen you can
            // scroll and copy from.
            if (_assets.Threw) Boom("loading the game files", _assets.Error);
            else Fail(_assets.Error);
            return;
        }
        _resDir = dir;

        // Which servers there are, before anything asks who you are: the
        // picker is part of the login screen, so the list has to exist by
        // the time that screen is built. The reference has the same
        // ordering - Config->Connections is loaded by RootClient.Start
        // long before UILogin::Initialize fills its combobox from it
        // (`UILogin.cpp:22-26`).
        _strings = M59Client.FindStringDictionary(_resDir);
        _servers = ServerList.Load(Host, Port, _strings, out _serverPick);

        string user = !string.IsNullOrWhiteSpace(Username)
            ? Username : System.Environment.GetEnvironmentVariable("M59USER");
        string pass = !string.IsNullOrWhiteSpace(Password)
            ? Password : System.Environment.GetEnvironmentVariable("M59PASS");

        // Nobody double-clicks a game and then goes to set environment
        // variables, so an exported build asks instead of refusing to
        // start. The environment still wins when it is set, which is
        // what the screenshot harnesses rely on.
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Ask();
            return;
        }

        Begin(user, pass);
    }

    /// <summary>Puts the login screen up and waits for it.</summary>
    void Ask()
    {
        if (_login != null) return;

        // Settings and the popup it talks through, built before the login
        // screen rather than with the rest of the widgets in Begin.
        //
        // Every opener in this client is gated on being in the world, and
        // Settings was no exception, so a player who could not get past
        // login had no settings at all - could not turn the music down,
        // could not pick a language, could not even see what the client
        // thought the server was. The reference puts an Options button on
        // its login window for exactly this (`Meridian59.layout:3075`,
        // `UILogin.cpp:16`, :37, :163-171), and the window it opens is the
        // same window the game opens later.
        //
        // The same instances, deliberately: Begin adopts whatever is here
        // rather than building a second panel, so a volume set on the
        // login screen is the volume in the world.
        Widget("ask", MakeAsk);
        Widget("options", () => Settings());

        _login = new LoginPrompt();
        // Added to the tree before anything is put into it: _Ready is
        // what builds the widgets, and it does not run until the node is
        // in the tree. Setting the address first was a silent no-op -
        // Server() guards on a null label, so the line that told the
        // player where they were connecting never appeared at all.
        _ui.AddChild(_login);

        _login.Choices(_servers, _serverPick);
        _login.Server(Chosen.Host, Chosen.Port);
        _login.Account(Chosen.Account, Chosen.Secret);

        // Which server, remembered as it is picked. The reference writes
        // the choice to SelectedConnectionIndex there and then
        // (`UILogin.cpp:88-92`) and refills the account boxes from the
        // new entry (:94-101), because an account name means nothing on a
        // server it was not made on.
        _login.ServerChanged += index =>
        {
            if (_servers == null || index < 0 || index >= _servers.Count) return;
            _serverPick = index;
            ServerList.Remember(_servers[index]);
            _login.Server(Chosen.Host, Chosen.Port);
            _login.Account(Chosen.Account, Chosen.Secret);
        };
        _login.Options += () => _options?.Open();
        _login.Submitted += (u, p) =>
        {
            // Remembered on the way in as well as on the pick, so the
            // server actually logged into is the one that comes back next
            // time even if it was the default all along.
            if (_servers != null && _serverPick >= 0 && _serverPick < _servers.Count)
                ServerList.Remember(_servers[_serverPick]);
            Begin(u, p);
        };
    }

    /// <summary>
    /// Builds the Settings panel, once, and wires everything about it
    /// that does not need a client.
    ///
    /// Its own method because it is now built from two places. The login
    /// screen needs it (`Meridian59.layout:3075`, `UILogin.cpp:163-171`),
    /// and the world needs the same instance and not a second one - the
    /// settings are a per-player thing, not a per-screen thing, and a
    /// duplicate panel would mean the volume you set before logging in
    /// being replaced by the volume the second panel restored.
    ///
    /// Everything wired here reads its target at invoke time rather than
    /// capturing it, so the handlers are safe to attach before the sound
    /// player, the touch controls and the client itself exist.
    /// </summary>
    void Settings()
    {
        if (_options != null) return;

        // Left of the mail button.
        _options = new OptionsPanel();
        _options.SoundVolume += v => { if (_sound != null) _sound.Volume = v; };
        _options.MusicVolume += v => { if (_sound != null) _sound.MusicLevel = v; };
        _options.LoopSounds  += on => { if (_sound != null) _sound.Loops = on; };
        // A factor on the room's own ambient light, not on the
        // finished picture - AdjustAmbientLight, not a gamma ramp.
        _options.Brightness  += v => _bright = v;
        _options.LookSpeed   += v => { if (_touch != null) _touch.LookSensitivity = 0.006f * v; };
        _options.InvertLook  += on => { if (_touch != null) _touch.InvertLook = on; };
        _options.Preferences += () => Act(() => _client.SendUserCommandSendPreferences());
        // The aliases are a page of the Options window in the
        // reference (`UIOptions.cpp:1241`); here they are their own
        // panel, so Settings closes and it takes its place.
        _options.EditAliases += () => { _options.Close(); _aliases?.Open(); };
        // The HUD editor draws over the live HUD, so Settings has to
        // get out of the way first - otherwise the thing being
        // arranged is underneath the window it was reached from.
        _options.EditHud += () => { _options.Close(); _hudEditor?.Open(); };
        // Whatever the panel has to say goes through the client's own one
        // popup, which is what the reference does with all four of its
        // password refusals (`ConfirmPopup::ShowOK` at
        // `UIOptions.cpp:2785`, :2792, :2799, :2806). Passed false for
        // closeOnInvalidate for the reason ConfirmPopup spells out: a
        // popup about the account has no game data behind it to go stale
        // (`OgreClient.cpp:895`, :908).
        _options.Complain += text => _ask?.Tell(text, null, false);

        // The password this client believes the account has, which is
        // what the "old password incorrect" check compares against - the
        // reference reads the same field
        // (`UIOptions.cpp:2790` reads Config->SelectedConnectionInfo->Password).
        _options.KnownPassword = () => _client?.Config?.SelectedConnectionInfo?.Password;

        // The send, and the write-back. Both halves of what
        // `OnChangePasswordClicked` does once its checks pass
        // (`UIOptions.cpp:2810-2812`): the request goes up, and the
        // connection entry is updated so a second change in the same
        // session compares against the new password rather than the old
        // one. The library call has been sitting unused in
        // `BaseClient.cs:797` all along.
        _options.ChangePassword += (before, after) => Act(() =>
        {
            _client.SendReqChangePassword(before, after);
            // What to put back if the server says no. The reference
            // overwrites unconditionally and never restores
            // (`UIOptions.cpp:2812`), so after a refusal it too holds a
            // password the account does not have; that is the reference's
            // fault, not its specification, and is not copied.
            _passwordBefore = before;
            _passwordAfter = after;
            if (_client.Config?.SelectedConnectionInfo != null)
                _client.Config.SelectedConnectionInfo.Password = after;
            // No "sent" line: the server answers, and the answer is what
            // the reference shows (`OgreClient.cpp:1073-1083`). The
            // library's handlers for it are empty (`BaseClient.cs:708-719`)
            // and M59Client overrides both.
        });

        // The server's answer to that change. This is the one line in
        // Settings that does NOT read its target at invoke time - a `+=`
        // has to have the client in hand now - and the summary above
        // says everything here is safe to wire before the client exists,
        // so the two were in contradiction and the contradiction won:
        // `Ask` calls Settings before Begin has built a client
        // (`Widget("options", () => Settings())`, and Begin's own
        // Widget("options") returns early on `_options != null`), so this
        // threw a NullReferenceException on EVERY session, the Widget
        // swallowed it as designed, and the throw is before
        // `_ui.AddChild(_options)` - so the Options panel was built,
        // never added to the tree, and never rebuilt. There were no
        // settings at all, on the login screen or in the world: no
        // volume, no brightness, no look speed, no language, no
        // password box. Caught by a census of the node tree, which
        // reported OptionsPanel=0 with the log line "[GameView] options
        // unavailable: NullReferenceException" above it.
        //
        // Held as a delegate and attached wherever the client turns up
        // first, which restores the invariant the summary claims.
        _passwordAnswer = ok =>
        {
            // Only undo what this change did: if the stored password is
            // no longer the one we wrote, something else has replaced it.
            var info = _client?.Config?.SelectedConnectionInfo;
            if (!ok && info != null && _passwordAfter != null && info.Password == _passwordAfter)
                info.Password = _passwordBefore;
            _passwordBefore = _passwordAfter = null;
            _ask?.Tell(ok ? "Password changed successfully."
                          : "The server did not accept your new password.", null, false);
        };
        if (_client != null) _client.PasswordAnswered += _passwordAnswer;

        _options.LanguageChanged += UseLanguage;

        _ui.AddChild(_options);
    }

    /// <summary>
    /// Puts a language into force, the three places
    /// `OnLanguageChanged` puts it (`UIOptions.cpp:2681-2691`).
    ///
    /// Config->Language, because that is what `BaseClient.Connect`
    /// passes to `SelectStringDictionary` on the next connect
    /// (`BaseClient.cs:119-121`). StringResources->Language, because the
    /// RSB already holds every language at once and that property is
    /// which bracket of the id space a lookup reads
    /// (`StringDictionary.cs:66`, :101-117) - so this is the one that
    /// changes anything for a session already running. And then
    /// ResolveStrings over the whole data model, because every name
    /// already resolved is still the old language's name
    /// (`DataController.cs:1161+`).
    ///
    /// The reference also calls ControllerUI::ApplyLanguage here, which
    /// relabels its own buttons from Language.cpp. There is no equivalent
    /// to port: this client's labels are English literals in C#, and
    /// inventing a translation table for them would be writing content,
    /// not porting it. What the setting does buy is the server's own
    /// strings - object names, room names, what people say - which is the
    /// larger half and the half that was missing.
    /// </summary>
    void UseLanguage(Meridian59.Common.Enums.LanguageCode code)
    {
        if (_client == null) return;
        try
        {
            _client.Config.Language = code;

            Meridian59.Common.StringDictionary strings =
                _client.ResourceManager?.StringResources;
            if (strings == null) return;
            if (strings.Language == code) return;

            strings.Language = code;
            _client.Data?.ResolveStrings(strings, true);
        }
        catch (Exception e) { GD.PrintErr($"[GameView] language: {e.Message}"); }
    }

    /// <summary>
    /// Shows or hides the login screen, if there is one.
    ///
    /// There often is not: an account out of the environment never
    /// builds one (`Ask` is skipped), which is exactly why the bug this
    /// exists to fix was invisible to every scripted run.
    /// </summary>
    void LoginPromptShown(bool on)
    {
        if (_login != null) _login.Visible = on;
    }

    /// <summary>
    /// Back to the login screen, with the character picker and the
    /// creation wizard put away.
    ///
    /// The three are mutually exclusive in the reference by
    /// construction - `Login::Window->setVisible(mode == UIMode::Login)`
    /// (`ControllerUI.cpp:747`), the welcome window only in
    /// AvatarSelection (`:710`), the wizard only in AvatarCreation
    /// (`:742`) - and here they were not exclusive at all. Nothing ever
    /// hid LoginPrompt after `Ask` added it to `_ui` (`:663-669`), and
    /// the picker's backdrop is 0.82 alpha, so "Meridian 59", the server
    /// field, the account box, the MASKED PASSWORD box and Connect all
    /// stayed legible through the picker and through the wizard, and
    /// stayed on screen for the rest of the session. Photographed.
    ///
    /// `BaseClient.Disconnect` sets `Data.UIMode = UIMode.Login`
    /// (`BaseClient.cs:142-147`); this is that line's visible half.
    /// </summary>
    void LoginMode()
    {
        if (_picker != null) _picker.Visible = false;
        _newChar?.Close();
        LoginPromptShown(true);
    }

    /// <summary>
    /// Where the last character played on each server is kept.
    ///
    /// The reference keeps it on the connection entry -
    /// `Config->SelectedConnectionInfo->Character`, written on all three
    /// of its selection paths (`UIWelcome.cpp:170-172`, `:214-216`,
    /// `:300-302`) - and Config writes that field out with the rest of
    /// the entry (`Meridian59/Common/Config.cs:672`) and reads it back
    /// (`:504-505`) so `CharacterAdd` can preselect the row whose name
    /// matches (`UIWelcome.cpp:124-128`).
    ///
    /// The entry half is done here too, because that is where the
    /// reference reads it from and it costs one line. The FILE half
    /// cannot be: nothing in this client ever calls `Config.Save`, and a
    /// phone has no writable folder beside the executable to put a
    /// configuration.xml in - the same reason the server pick, the
    /// aliases and the settings all live in `user://`. So this follows
    /// what those already do rather than inventing a shape: a small
    /// ConfigFile of its own, keyed by server address exactly as
    /// `ServerList` keys its remembered pick, and its own file rather
    /// than a shared one because OptionsPanel rewrites settings.cfg
    /// whole on every change.
    ///
    /// By address, not by account: an account name is per server
    /// (`UILogin.cpp:94-101`) and a character name is per account, so
    /// the address is the coarsest key that is never wrong about which
    /// server's characters it is naming. Two accounts on one server
    /// share the row, which costs one failed name match on the picker
    /// and nothing else.
    /// </summary>
    const string CharacterStorePath = "user://character.cfg";
    const string CharacterStoreSection = "login";

    /// <summary>The character last played on the server now chosen, or null.</summary>
    string RememberedCharacter()
    {
        try
        {
            var file = new ConfigFile();
            if (file.Load(CharacterStorePath) != Error.Ok) return null;
            string got = (string)file.GetValue(CharacterStoreSection, Chosen.Address, "");
            return string.IsNullOrWhiteSpace(got) ? null : got;
        }
        catch (Exception e) { GD.PrintErr($"[GameView] character store: {e.Message}"); return null; }
    }

    /// <summary>
    /// Remembers a character, on the connection entry and on disk.
    ///
    /// Written as it is chosen rather than on the way out, which is what
    /// the reference does too (the assignment sits immediately before
    /// `SendUseCharacterMessage`, `UIWelcome.cpp:170-175`) and what every
    /// other store in this client does, for the reason HotbarStore
    /// spells out: a phone client is not closed, it is swiped away, and
    /// there is no shutdown to hang a save on.
    /// </summary>
    void RememberCharacter(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        // The reference's own line (`UIWelcome.cpp:171-172`).
        var info = _client?.Config?.SelectedConnectionInfo;
        if (info != null) info.Character = name;

        try
        {
            var file = new ConfigFile();
            // Loaded first: the file holds one row per server and a
            // write that did not read would drop the others.
            file.Load(CharacterStorePath);
            file.SetValue(CharacterStoreSection, Chosen.Address, name);
            file.Save(CharacterStorePath);
        }
        catch (Exception e) { GD.PrintErr($"[GameView] character store: {e.Message}"); }
    }

    /// <summary>
    /// Marks the row of the character played last, which is the whole of
    /// what `CharacterAdd` does with the remembered name
    /// (`UIWelcome.cpp:124-128`: `widget->setSelected(true)` on the row
    /// whose Name equals `coninfo->Character`).
    ///
    /// The reference's rows are items in a CEGUI ListBox with a separate
    /// button to act on the selection, so "selected" there is a state
    /// that can be preselected. Here each row IS the button and tapping
    /// it logs straight in (`CharacterPicker.cs:235`), so there is no
    /// selection state to set - which leaves focus, the only per-row
    /// state a Godot Button has that means "this is the one you would
    /// act on", plus the picker's own highlight colour so that it is
    /// legible on a phone rather than only to a focus ring.
    ///
    /// Deliberately NOT done by setting `PreferredCharacter`: that is a
    /// different feature. `M59Client.HandleCharactersMessage` logs the
    /// preferred character in without showing the picker at all
    /// (`M59Client.cs:383-387`, `:417`), which is right for the
    /// harness's `--char` and wrong here - the reference preselects a
    /// row and still waits for the player to press it, and silently
    /// entering the world as whoever you played last is not the same
    /// offer.
    /// </summary>
    void PreselectCharacter(string name)
    {
        if (_picker == null || string.IsNullOrWhiteSpace(name)) return;
        Button row = RowNamed(_picker, name);
        if (row == null) return;
        // All three states, not just `font_color`: a Godot Button draws
        // its label through `font_focus_color` while it holds focus and
        // `font_hover_color` under a finger, so overriding the normal
        // colour alone left the marked row looking exactly like the
        // others - photographed, white text inside the focus ring.
        Color mark = new Color(1, 0.92f, 0.6f);
        row.AddThemeColorOverride("font_color", mark);
        row.AddThemeColorOverride("font_focus_color", mark);
        row.AddThemeColorOverride("font_hover_color", mark);
        row.GrabFocus();
    }

    /// <summary>
    /// The room's soundscape, ended, because the room has been left -
    /// dropped, kicked or logged out.
    ///
    /// Nothing did this. The ConnectionLost handler showed its screen and
    /// disconnected and never touched `_sound`, so the last room's
    /// looping ambient - and its music - played on underneath "connection
    /// lost" for as long as the app stayed open. The room itself is gone:
    /// `BaseClient.Disconnect` resets the data layer
    /// (`BaseClient.cs:142-147`), so the sound was coming from a room
    /// that no longer existed in any model.
    ///
    /// The reference cannot reach that state. Disconnect goes to
    /// `DemoSceneLoadBrax` (`OgreClient.cpp:641`), which is
    /// `DemoSceneDestroy` (`:1183`) -> `ControllerRoom::UnloadRoom` plus
    /// `Data->RoomObjects->Clear()` (`:1171-1172`); clearing the object
    /// list destroys every RemoteNode, and a RemoteNode's destructor
    /// stops and drops every sound attached to it
    /// (`RemoteNode.cpp:112-125`). `StopAll` is this client's equivalent
    /// of that sweep and is already what a room CHANGE uses
    /// (`M59Sound.cs:619-624`).
    ///
    /// Then the music. `DemoSceneLoadBrax` finishes by starting
    /// "nec02.ogg" (`OgreClient.cpp:1199-1203`), which is a deliberate
    /// switch and not a side effect - the menu track is named there and
    /// nowhere else. It is also the only way the room's track can be
    /// stopped from here: M59Sound exposes no StopMusic, and replacing
    /// the track is what the reference does anyway.
    ///
    /// So: does menu music belong on a phone? At LAUNCH, no, and that is
    /// left as it is. The reference's login screen is a rendered tavern -
    /// `DemoSceneLoadBrax` loads tosinn.roo and points a camera at it
    /// (`:1181-1198`) - and nec02 is that scene's score; this client's
    /// login screen is a dark panel with three boxes on it, so there is
    /// nothing for the music to be the music OF. A game that starts
    /// playing before you have typed anything is also the wrong thing to
    /// hand someone who opened it on a bus, and `_sound` does not exist
    /// until a client is built, so it would mean moving the audio setup
    /// to get a result nobody asked for.
    ///
    /// Leaving the WORLD is the other case, and there it belongs: the
    /// player has had the room's music in their ears for an hour, the
    /// room has just been taken away, and the choice is between the
    /// track of a room that is gone and the track the game itself plays
    /// when you are not in one. The second is the reference's answer and
    /// the better one.
    ///
    /// It obeys the music slider because `PlayMusic` is where that is
    /// enforced: it refuses to start at all at zero, as `StartMusic`
    /// does (`ControllerSound.cpp:488`), and sets the level from
    /// MusicLevel, which the Settings panel keeps live
    /// (`M59Sound.cs:630-655`, `:63-72`).
    ///
    /// The lower-case name is the reference's own spelling. The file on
    /// disk is `Nec02.ogg`; the case-insensitive resolve in `M59Sound`
    /// (`OnDisk`, used by `Load`) is what makes the mismatch harmless,
    /// and asking the way the reference asks is the point of citing it.
    /// </summary>
    void LeftTheWorldAudio()
    {
        if (_sound == null) return;
        try
        {
            _sound.StopAll();

            Meridian59.Files.ResourceManager res = _client?.ResourceManager;
            if (res == null) return;
            var menu = new Meridian59.Data.Models.PlayMusic { ResourceName = "nec02.ogg" };
            menu.ResolveResources(res, false);
            _sound.PlayMusic(menu);
        }
        catch (Exception e) { GD.PrintErr($"[GameView] leaving audio: {e.Message}"); }
    }

    /// <summary>The picker row carrying this character's name, if it has one.</summary>
    static Button RowNamed(Node under, string name)
    {
        foreach (Node n in under.GetChildren())
        {
            if (n is Button b && string.Equals(b.Text, name, StringComparison.OrdinalIgnoreCase))
                return b;
            Button deeper = RowNamed(n, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    /// <summary>Builds the client and connects, once we know who we are.</summary>
    void Begin(string user, string pass)
    {
        // Once. The Connect button comes back after a refusal (Fail puts
        // the reason on the login screen and leaves the button live), so
        // a second press ran all of this a second time: a second
        // M59Client, a second WorldSync, a second copy of every one of
        // the sixteen `Widget(...)` panels below - none of which guard
        // on their field already being set, unlike Widget("ask") and
        // Widget("options") in `Ask`, which do - a second
        // `GetViewport().SizeChanged += Resize` subscription, and a
        // first client left orphaned, never pumped and never
        // disconnected. The reference cannot reach this state at all:
        // it disables its login window while a connection is in flight
        // (`OgreClient.cpp:642` re-enables it) and there is only ever
        // the one client singleton.
        //
        // A retry after a refusal is Connect(), which reuses the client
        // that is already built - that is what LostConnection's
        // Reconnect button calls, and what the login screen's second
        // press now falls through to.
        if (_client != null)
        {
            // The credentials may have changed between the two presses -
            // a refused password retyped is the likeliest second press
            // there is - so the entry this client logs in with is
            // updated before the retry rather than left at the first
            // attempt's values.
            var already = _client.Config?.SelectedConnectionInfo;
            if (already != null) { already.Username = user; already.Password = pass; }
            // Marked offline first, or Connect refuses to run while the
            // client still thinks the refused socket is up - the same
            // reason the ConnectionLost handler disconnects before
            // offering Reconnect.
            try { _client.Disconnect(); } catch { }
            LoginMode();
            Connect();
            return;
        }

        _client = new M59Client
        {
            PreferredCharacter = Character,
            VersionMajor = (byte)Math.Clamp(VersionMajor, 0, 255),
            VersionMinor = (byte)Math.Clamp(VersionMinor, 0, 255),
        };
        // The one handler Settings could not attach itself, because
        // Settings runs from Ask and this is where its target is born.
        if (_passwordAnswer != null) _client.PasswordAnswered += _passwordAnswer;
        _world = new WorldSync(_client.ResourceManager);
        _world.RootPath = _resDir;
        _world.SkyDir = M59Paths.SkyDir();
        // Every arrival in a room, the same one included - see the note
        // on _arrived.
        //
        // The room's sounds are stopped HERE, in the handler for the
        // Player message, and not in SyncRoom. The reference dispatches
        // one message at a time in arrival order
        // (`ControllerSound.cpp:229-256`) and clears its sound list when
        // it REACHES the Player message (`:341-357`), so a PlayWave that
        // arrives after the Player message belongs to the new room and
        // survives. `_client.Update()` drains the ENTIRE socket before
        // returning (`BaseClient.cs:333-349` loops the enrichment queue
        // dry), firing Sound for every message in the batch, so stopping
        // afterwards in SyncRoom inverted that order: the new room's
        // looping ambient - the loudest and most continuous thing in the
        // soundscape - started and was killed in the same frame whenever
        // the Player message and the room's PlayWave landed in one batch,
        // which is most of the time. Photographed: "f5 Player message",
        // "Rat_awr.ogg gain 0.77 loop True", "room sounds stopped", all
        // in frame 5.
        //
        // Unconditional, as HandlePlayerMessage is: nothing about the new
        // room's file gets in the way, so a .roo that fails to load still
        // silences the room you have left instead of playing it for the
        // rest of the session over a room that is not there.
        _client.Arrived += () =>
        {
            if (_sound != null && _sound.Verbose)
                GD.Print($"[GameView] f{Engine.GetProcessFrames()} Player message: room sounds stopped");
            _sound?.StopAll();
            _arrived = true;
        };

        // `/suicide`. The library parses the word and calls a virtual
        // Suicide() that its own base class leaves empty on purpose
        // (`BaseClient.cs:3103-3108` dispatching to :3347-3349), so
        // until M59Client overrode it the command did nothing whatever -
        // no death, no error, nothing. The override is in M59Client and
        // deliberately refuses to send on its own; it asks here first.
        //
        // The reference does the identical two-step: OgreClient::Suicide
        // raises a yes/no popup reading "Are you sure?"
        // (`OgreClient.cpp:1063-1069`) and only the confirm listener
        // sends the command (`:1109-1112`). Irreversible and one word
        // long is exactly the combination that earns a confirmation.
        //
        // The popup is this client's own in-game ConfirmPopup, as it is
        // the reference's own ConfirmPopup there - never an engine or OS
        // dialog. _ask is looked up when the player types, not now,
        // because the popup is built by a Widget() that may run after
        // this line; if it is genuinely missing, nothing is sent and the
        // chat says so, because an unconfirmed suicide is a worse
        // outcome than a refused one.
        _client.ConfirmSuicide = go =>
        {
            if (_ask == null)
            {
                _chat?.Local("Cannot confirm a suicide right now - nothing sent.");
                return;
            }

            _ask.Choice("Are you sure?", 0, _ => go());
        };

        // A refusal or a result the player has to be told: the chat log
        // gets these. The reference's chat holds server text and its own
        // short player-facing lines only, never exception text.
        _client.Notice += s =>
        {
            Note(s); GD.Print("[M59] " + s);
            // Anything that means "you are not getting in" belongs on the
            // login screen, not only in a log nobody can see yet.
            if (_login != null &&
                (s.StartsWith("Connection error") || s.Contains("ailed") || s.Contains("efused")))
                _login.Trouble(s);
            _chat?.Local(s);
        };
        // The client's own diagnostics (M59Client.Diagnostic): console
        // always, the on-screen readout only under M59DEBUG, never the
        // chat log. The socket error still reaches the login screen.
        _client.Diagnostic += s =>
        {
            GD.Print("[M59] " + s);
            if (Debugging) Note(s);
            if (_login != null && s.StartsWith("Connection error"))
                _login.Trouble(s);
        };
        // A connection that has gone away, said plainly. The socket
        // going down produced one line of .NET exception text in the
        // log and nothing else: the last frame of the world stayed up
        // with every button still looking live. On a phone that is not
        // an error case, it is Tuesday - a lift, a tunnel, the app put
        // in the background - so it needs a sentence and a way back in.
        _client.ConnectionLost += why =>
        {
            // Shown ALWAYS, with no gate on how far into the session
            // this is. It used to bail out whenever the player had not
            // entered the world and a login screen existed, on the
            // theory that the login screen says its own piece - but the
            // login screen is UNDERNEATH the character picker, and the
            // window between "Login accepted" and "Entering the world"
            // is exactly where a phone drops its connection. What the
            // player got was a line of small red text they could not
            // reach, a live-looking picker, and a dead socket behind it;
            // reproduced by cutting the connection four seconds after
            // Connect. The reference shows its failure popup on EVERY
            // connection exception with no gate on UI mode at all
            // (`OgreClient.cpp:644-653`).
            //
            // In-page, as everything in this client is: LostConnection
            // is a Control, never a system dialog.
            //
            // Disconnect first. Nothing else marks the connection
            // offline after a broken pipe, and Connect refuses to run
            // while it thinks it is still up - so without this the
            // Reconnect button would do nothing at all. It is also what
            // the reference does on the way out of its popup
            // (`OgreClient.cpp:941-944`), and `BaseClient.Disconnect`
            // resets the data layer and puts UIMode back to Login
            // (`BaseClient.cs:142-147`) - which is what the line below
            // makes visible here.
            try { _client.Disconnect(); } catch { }
            LoginMode();
            LeftTheWorldAudio();
            _lost?.Show(why);
        };
        _client.EnteredGame += name =>
        {
            _state = $"playing as {name}";
            _wasInGame = true;
            // And here too, because this client reaches the world by
            // routes the reference does not have. The three paths
            // UIWelcome writes the name on are all picker rows; here the
            // picker is skipped entirely when the account holds one
            // character or `PreferredCharacter` matches one
            // (`M59Client.cs:383-387`, `:404-414`), and a character
            // played that way is still the character you played last.
            // Empty on the creation path (`M59Client.cs:534` has no name
            // to give), which the wizard covers itself.
            RememberCharacter(name);
            // In the world - the login screen has done its job, and so
            // has the creation wizard if that is how we got here. The
            // wizard no longer closes when Create is pressed, because
            // the server may refuse; this is the yes.
            if (_login != null) { _login.QueueFree(); _login = null; }
            _newChar?.Close();
        };

        // Each overlay is built on its own. None of this has run on a
        // device yet, and a widget that throws while being set up should
        // cost you that widget, not the view - a game you can walk around
        // in with no chat box beats a black screen and a stack trace.
        Widget("chat", () =>
        {
            // Clear of the minimap's own button in the bottom corner.
            // Nothing is reserved at the bottom right any more. That
            // 70 was the minimap's toggle, which used to sit in the
            // corner and had the recall arrow drawn through it; the
            // toggle is in the top band now and the corner belongs to
            // the combat cluster, which the chat block does not reach.
            _chat = new ChatOverlay();
            _chat.Submitted += text =>
            {
                try
                {
                    // Is this a command, or is it something to say?
                    //
                    // The library's parser answers for itself on a
                    // desktop: you type "broadcast x" or you type
                    // nothing it understands, and plain text does
                    // nothing at all - `UIChat.cpp:274-281` calls
                    // ExecChatCommand and stops there, and the parser
                    // only makes a say out of text beginning with the
                    // word "say" (ChatCommand.cs:103).
                    //
                    // This bar is opened by pressing a button labelled
                    // Say, so plain text is said. That is a departure
                    // and it is the right one: three more letters
                    // before every sentence costs far more on a soft
                    // keyboard than on a real one.
                    //
                    // The departure has exactly one rule: a line is
                    // EITHER executed OR said, never both - and the
                    // corollary that took a wire capture to notice, a
                    // line that was MEANT as a command is never said,
                    // not even when it fails to parse. Deciding by
                    // "did Parse return something?" got the corollary
                    // backwards, because every malformed command parses
                    // to null: `tell Zorak my guild password is
                    // hunter2` went out as SayTo to the whole room and
                    // the room echoed it back. So the decision is made
                    // in two parts - what the parser made of the line,
                    // and whether the line opens with a command word at
                    // all - and only a line that fails both is spoken.
                    //
                    // ExecChatCommand gets the line as typed, because it
                    // is the parser's job to expand a leading alias
                    // (`ChatCommand.cs:66-86`) and doing it here as well
                    // expanded twice. Commandable only fixes the case of
                    // the first word, which is what lets the parser see
                    // a phone keyboard's "Tell" and "Chuckle" at all.
                    string line = Commandable(text);

                    // A leading slash is not the library's syntax - it
                    // has none - but it is every other game's, and a
                    // player who types one has unambiguously said "this
                    // is a command, not a sentence". Honouring it costs
                    // one character and stops `/tell bob the plan` from
                    // being read out to the room, which is what it did.
                    bool sigil = line != null && line.TrimStart().StartsWith("/");
                    if (sigil) line = Commandable(line.TrimStart().Substring(1));

                    bool said;
                    Meridian59.Data.Models.ChatCommand cmd = null;
                    bool threw = false;
                    try { cmd = ParseChat(line, out said); }
                    catch (ChatCommandFailed) { said = false; threw = true; }

                    // Parsed, and this client is content to run it.
                    if (cmd != null && !SentenceNotCommand(cmd, line))
                    {
                        // ExecChatCommand parses a second time. That is
                        // harmless HERE and only here: the failure paths
                        // are the only ones that touch Data.ChatMessages,
                        // and this parse has just succeeded, so the
                        // second one succeeds identically and says
                        // nothing. It is also what logs the line to the
                        // command history (`BaseClient.cs:3039`).
                        _client.ExecChatCommand(line);
                    }
                    // Meant as a command and not one: silent. The line
                    // is logged to the history so arrow-up gives it back
                    // to be corrected (`BaseClient.cs:3039` does the
                    // same for a malformed line), and nothing whatever
                    // reaches the wire.
                    else if (cmd == null && (threw || sigil || OpensWithCommandWord(line)))
                    {
                        _client.Data?.ChatCommandHistoryAdd(line);
                        // Silent, unless the library was silent too. A
                        // failed command that says nothing at all is a
                        // dead button on a phone, where there is no
                        // console to check.
                        if (!said)
                            _chat?.Local($"\"{FirstWord(line)}\" needs different words - nothing was sent.");
                    }
                    // Speech.
                    else
                    {
                        // Through the history the way ExecChatCommand
                        // would have logged it, but NOT through the
                        // parser again: the one parse above is the only
                        // one this line gets.
                        _client.Data?.ChatCommandHistoryAdd(line);

                        // Said with a leading alias expanded, because an
                        // alias whose value is not a command ("brb" ->
                        // "be right back") is still the player asking
                        // for the long form.
                        _client.SendSayToMessage(
                            Meridian59.Common.Enums.ChatTransmissionType.Normal,
                            AliasStore.Expand(_client.Config, line));
                    }
                }
                catch (Exception ex) { _chat.Local($"could not send: {ex.Message}"); }
            };
            // The chat command history, which on a desktop is ArrowUp
            // in the box. The library keeps the last twenty and walks
            // them itself; the index resets when the box closes, the
            // way Enter and Escape reset it in the game.
            _chat.History += back =>
            {
                var d = _client?.Data;
                if (d == null) return null;
                return back ? d.ChatCommandHistoryGetNext() : d.ChatCommandHistoryGetPrevious();
            };
            _chat.HistoryReset += () =>
            {
                if (_client?.Data != null) _client.Data.ChatCommandHistoryIndex = -1;
            };
            _ui.AddChild(_chat);
        });

        Widget("actions", () =>
        {
            _actions = new ActionBar();
            // Each of these is what the game's target window sends,
            // and each acts on the target the library is holding, not
            // on an id this view kept for itself.
            _actions.LookAt         += () => WorldAct(() => _client.SendReqLookMessage());
            _actions.AttackTarget   += () => WorldAct(() => _client.SendReqAttackMessage());
            _actions.ActivateTarget += () => WorldAct(() => _client.ExecAction(AvatarAction.Activate));
            _actions.BuyFrom        += () => WorldAct(() => _client.SendReqBuyMessage());
            _actions.TradeWith      += () => WorldAct(() => _client.ExecAction(AvatarAction.Trade));
            _actions.LootTarget     += () => WorldAct(() => _client.SendReqGetMessage());
            _actions.AskQuests      += () => WorldAct(() => _client.SendReqNPCQuestsMessage());
            // The Close key, `ControllerInput.cpp:555-562`: TargetID =
            // 0xFFFFFFFF, and only while nothing has focus. It sits
            // behind the same key-down exits as ReqGo (:541-550,
            // including IsWaiting), hence WorldAct.
            _actions.Deselect       += () => WorldAct(() => _client.Data.TargetID = 0xFFFFFFFFU);
            _ui.AddChild(_actions);
            if (_chat != null) _actions.BottomReserve = _chat.BlockHeight;


            // The hotbar's own reserve is not set here. It used to be,
            // and it never took: this widget is built before the hotbar
            // widget, so _hotbar was still null every time and the row
            // kept a reserve of zero - which is why the buttons sat on
            // top of the menu row along the bottom edge. It is set from
            // _Process instead, where both blocks certainly exist and
            // where it also follows the chat block growing and shrinking.
        });

        Widget("trade", () =>
        {
            _trade = new TradePanel();
            _trade.Offer += ids => Act(() =>
            {
                ObjectBase who = _client.Data?.Trade?.TradePartner;
                // Same button, two messages: a counter-offer when they
                // opened the trade, a fresh offer when you did.
                if (_client.Data != null && _client.Data.Trade.IsBackgroundOffer)
                    _client.SendReqCounterOffer(ids.ToArray());
                else if (who != null)
                    _client.SendReqOffer(who, ids.ToArray());
            });
            _trade.Accept += () => Act(() => _client.SendAcceptOffer());
            _trade.Cancel += () => Act(() => _client.SendCancelOffer());
            // Not the game's: it drags out of the inventory window, and
            // a phone cannot show both at once.
            _trade.Look += id => Act(() => _client.SendReqLookMessage(id));
            _trade.AmountWanted += o =>
            {
                if (_amount == null || o == null) return;
                _amountFor = null;
                _amountForTrade = o;
                // Prefilled with what you chose last time, capped at the
                // live stack. Passing o.Count as both put the whole
                // stack back every time the prompt was re-opened, and OK
                // writes the box back - see TradePanel.Chosen.
                _amount.Ask(o.ID, (int)_trade.Chosen(o), (int)o.Count, o.Name);
            };
            _trade.AddWanted += () =>
            {
                if (_bag == null) return;
                _pickFor = PickFor.Trade;
                _bag.PickMode = true;
                _bag.Open();
            };
            _ui.AddChild(_trade);
        });
        Widget("quests", () =>
        {
            _quests = new QuestsPanel();
            _quests.Opened += () => Act(() =>
                _client.SendSendStatsMessage(Meridian59.Common.Enums.StatGroup.Quests));
            _quests.Look += id => Act(() => _client.SendReqLookMessage(id));
            _ui.AddChild(_quests);

            // What an NPC offers. No button opens this: the target row's
            // Quest button asks, and the server's answer raises it.
            _npcQuests = new NpcQuestsPanel();
            _npcQuests.Accept += (giver, quest) => Act(() =>
                _client.SendReqTriggerQuestMessage(new ObjectID(giver, 0), new ObjectID(quest, 0)));
            // UINPCQuestList closes the window by clearing the data
            // layer, not by hiding the widget - so the next Sync agrees
            // with it instead of reopening it.
            _npcQuests.Dismissed += () => _client.Data?.QuestUIInfo?.Clear(true);
            // The reference puts the help in its own OK popup
            // (`UINPCQuestList.cpp:353-360`); this client already has
            // the in-page equivalent.
            _npcQuests.Helped += text => _ask?.Tell(text);
            _ui.AddChild(_npcQuests);
        });
        Widget("mail", () =>
        {
            // Left of the quest log button.
            _mail = new MailPanel();
            _mail.Refresh += () => Act(() => _client.SendReqGetMail());
            _mail.Lookup += names => Act(() => _client.SendReqLookupNames(names));
            // The second half of the send: the server has said who these
            // names are, and the panel decides whether that is everyone.
            _client.NamesLookedUp += ids => Act(() =>
            {
                if (_mail == null) return;
                if (_mail.Answer(ids, out ObjectID[] to, out string subject, out string text))
                    _client.SendSendMail(to, subject, text);
            });
            _ui.AddChild(_mail);
        });
        // Ask() may already have built this, so the login screen had
        // something for Settings to complain through.
        Widget("ask", MakeAsk);
        Widget("statwizard", () =>
        {
            // No button opens this either: the server offers a stat
            // change and the data layer raises it.
            _wizard = new StatsWizard();
            _wizard.Apply += () => Act(() => _client.SendChangedStatsMessage());
            _wizard.Confirm += (text, yes) => _ask?.Choice(text, 0, _ => yes());
            _wizard.Complain += text => _ask?.Tell(text);
            _ui.AddChild(_wizard);
        });
        Widget("news", () =>
        {
            // No button: looking at a news globe in the world is what
            // raises it.
            _news = new NewsPanel();
            // HandleArticles adds to the list rather than replacing it,
            // so asking again without clearing appends the board to
            // itself.
            _news.Refresh += () => Act(() =>
            {
                _client.Data?.NewsGroup?.Articles?.Clear();
                _client.SendReqArticles();
            });
            _news.Read += number => Act(() => _client.SendReqArticle(number));
            _news.Post += (title, text) => Act(() =>
            {
                ushort globe = _client.Data != null ? _client.Data.NewsGroup.NewsGlobeID : (ushort)0;
                _client.SendPostArticle(globe, title, text);
                _client.Data?.NewsGroup?.Articles?.Clear();
                _client.SendReqArticles();
            });
            _news.Remove += number => Act(() =>
            {
                ushort globe = _client.Data != null ? _client.Data.NewsGroup.NewsGlobeID : (ushort)0;
                _client.SendDeleteNews(globe, number);
                _client.Data?.NewsGroup?.Articles?.Clear();
                _client.SendReqArticles();
            });
            _ui.AddChild(_news);
        });
        // Adopted rather than built: Ask() has already made this so the
        // login screen has settings to open. All the wiring lives in
        // Settings(); the only thing that changes here is that the row of
        // openers along the bottom is now a thing that exists.
        Widget("options", () => { Settings(); _options.OpenerAllowed = true; });
        Widget("aliases", () =>
        {
            // No button of its own: Settings is the way in, as it is in
            // the reference. Closing it puts Settings back, so the trip
            // is reversible with the button the player already found.
            _aliases = new AliasEditor();
            _aliases.Closed += () => _options?.Open();
            // The alias row's stand-in for dragging it onto a hotbar
            // slot (`UIOptions.cpp:1070` drags, `UIActionButtons.cpp:427-434`
            // drops and calls SetToAlias). Not wrapped in Act: nothing is
            // sent, so a pending server reply is no reason to refuse it.
            _aliases.Assign += a =>
            {
                if (ActionButtons.Bind(_client.Data, a))
                    _chat?.Local($"\"{a.Key}\" is on the hotbar. Drag the button off the row to clear it.");
            };
            _ui.AddChild(_aliases);
        });
        Widget("hudEditor", () =>
        {
            // No button of its own, for the same reason the alias editor
            // has none: Settings is the door, and coming back out of the
            // editor puts Settings back, so the trip is reversible with
            // the control the player already found.
            _hudEditor = new HudEditor();
            _hudEditor.Closed += () => _options?.Open();
            _ui.AddChild(_hudEditor);
        });
        Widget("guild", () =>
        {
            // No button opens this: UserCommandGuildInfo raises it.
            _guild = new GuildPanel();
            // The window wants the roster AND the list of other guilds;
            // the reference asks for both, along with the shield lists
            // it uses and this does not (`UIGuild.cpp:604-620`).
            _guild.Opened += () => Act(() =>
            {
                _client.SendUserCommandGuildInfoReq();
                _client.SendUserCommandGuildGuildListReq();
            });

            // Changing where you stand with another guild. The server
            // has no "switch sides", so half of these are two commands:
            // ally to enemy is end the alliance THEN declare, and enemy
            // to ally is end the enmity THEN ally
            // (`UIGuild.cpp:762-847`). Each one is gated on its own
            // right, and a transition you do not hold the rights for
            // simply does not happen - the reference's final else is
            // empty, and so is this.
            //
            // The lists are updated here as well as sent, because none
            // of these is echoed: the reference does the same
            // (:791, :800-801), and without it the row springs back to
            // where it was on the next rebuild.
            _guild.Diplomacy += (id, was, now) => Act(() =>
            {
                GuildFlags f = _client.Data?.GuildInfo?.Flags;
                DiplomacyInfo d = _client.Data?.DiplomacyInfo;
                if (f == null || d == null || was == now) return;

                if (was == 1 && now == 2 && f.IsDeclareEnemy)
                {
                    _client.SendUserCommandGuildMakeEnemy(id);
                    d.YouDeclaredEnemyList.Add(new ObjectID(id, 0));
                }
                else if (was == 0 && now == 2 && f.IsEndAlliance && f.IsDeclareEnemy)
                {
                    _client.SendUserCommandGuildEndAlliance(id);
                    _client.SendUserCommandGuildMakeEnemy(id);
                    Drop(d.YouDeclaredAllyList, id);
                    d.YouDeclaredEnemyList.Add(new ObjectID(id, 0));
                }
                else if (was == 1 && now == 0 && f.IsMakeAlliance)
                {
                    _client.SendUserCommandGuildMakeAlliance(id);
                    d.YouDeclaredAllyList.Add(new ObjectID(id, 0));
                }
                else if (was == 2 && now == 0 && f.IsEndEnemy && f.IsMakeAlliance)
                {
                    _client.SendUserCommandGuildEndEnemy(id);
                    _client.SendUserCommandGuildMakeAlliance(id);
                    Drop(d.YouDeclaredEnemyList, id);
                    d.YouDeclaredAllyList.Add(new ObjectID(id, 0));
                }
                else if (was == 2 && now == 1 && f.IsEndEnemy)
                {
                    _client.SendUserCommandGuildEndEnemy(id);
                    Drop(d.YouDeclaredEnemyList, id);
                }
                else if (was == 0 && now == 1 && f.IsEndAlliance)
                {
                    _client.SendUserCommandGuildEndAlliance(id);
                    Drop(d.YouDeclaredAllyList, id);
                }
            });
            _guild.Support += id => Act(() => _client.SendUserCommandGuildVote(id));
            // The three irreversible ones go through the popup, with
            // the file's own wording. They were going straight off the
            // button press, which on a phone is worse than on a desktop
            // rather than better.
            //
            // The question stays open while the world moves, so every Yes
            // tests the right AGAIN against the live guild model before it
            // sends: the reference does so at the confirm and not only at
            // the button (`UIGuild.cpp:604-611` exile, `:578-598` renounce
            // and disband, both on the current `Flags`). A master demoted
            // while "exile Boris?" was up, a member who left meanwhile, or a
            // model cleared under the question all read as "no" here, and
            // the command that the server would refuse is never sent. The
            // per-row exile rule is GuildPanel's `CanExile`
            // (`UIGuild.cpp:451`); that is private to it and not reachable
            // from here, so it is restated in `GuildMayExile`.
            _guild.Exile += (id, who) => _ask?.Choice(
                $"Are you sure you want to exile {who}?", id,
                confirmed => Act(() =>
                {
                    if (!GuildMayExile(confirmed)) { GuildNoLonger(); return; }
                    _client.SendUserCommandGuildExile(confirmed);
                    Reask();
                }));
            _guild.SetRank += (id, rank) => Act(() => _client.SendUserCommandGuildSetRank(id, rank));
            _guild.Abdicate += (id, who) => _ask?.Choice(
                $"Are you sure you want to abdicate to {who}?", id,
                confirmed => Act(() =>
                {
                    if (!GuildMayAbdicate(confirmed)) { GuildNoLonger(); return; }
                    _client.SendUserCommandGuildAbdicate(confirmed);
                    Reask();
                }));
            _guild.Password += pw => Act(() => _client.SendUserCommandGuildSetPassword(pw));
            _guild.AbandonHall += () => _ask?.Choice(
                "Are you sure you want to abandon your hall?", 0,
                _ => Act(() =>
                {
                    if (!GuildIsMaster() || _client.Data.GuildInfo.PasswordSetFlag == 0)
                    {
                        GuildNoLonger();
                        return;
                    }
                    _client.SendUserCommandGuildAbandonHall();
                }));
            _guild.Renounce += disband => _ask?.Choice(
                disband ? "Are you sure you want to disband your guild?"
                        : "Are you sure you want to leave your guild?", 0,
                _ => Act(() =>
                {
                    // The reference picks renounce or disband from the flags
                    // as they are NOW (`UIGuild.cpp:582,591`). Here the
                    // answer must also still be the question that was
                    // asked: Yes to "leave?" never disbands.
                    GuildFlags f = _client.Data?.GuildInfo?.Flags;
                    bool renounceNow = f != null && f.IsRenounce;
                    bool disbandNow = f != null && !f.IsRenounce && f.IsDisband;
                    if (disband ? !disbandNow : !renounceNow) { GuildNoLonger(); return; }
                    if (disband) _client.SendUserCommandGuildDisband();
                    else _client.SendUserCommandGuildRenounce();
                    _client.Data?.GuildInfo?.Clear(true);
                    _client.Data?.GuildShieldInfo?.Clear(true);
                }));
            // None of the guild commands is echoed, so the file clears
            // and re-asks after each one rather than guessing.
            _guild.Reload += () => Act(Reask);
            _ui.AddChild(_guild);

            // The shield designer. In the game it is the guild window's
            // fourth tab (`UIGuild.cpp:15`); here it is its own panel, so
            // the guild window has a button that opens it.
            //
            // Opening it asks for both shield things the reference asks
            // for before showing that window (`UIGuild.cpp:618-619`,
            // `UIMainButtonsRight.cpp:73-74`): the list of shield art,
            // which is what the design stepper walks, and our own
            // shield's colours and design.
            _shieldDesigner = new GuildShieldPanel();
            _guild.ShieldDesigner += () => _shieldDesigner.Open();
            // The designer is a TAB of the guild window in the game
            // (`UIGuild.cpp:15`), so only one of the two is ever on
            // screen. Told about the roster, it takes the roster's place
            // when it opens and gives it back when it closes - the trip
            // Settings and the alias editor already make.
            _shieldDesigner.Roster = _guild;
            _shieldDesigner.Requested += () => Act(() =>
            {
                _client.SendUserCommandGuildShieldListReq();
                _client.SendUserCommandGuildShieldInfoReq();
            });
            // Every change of colour or design asks the server about that
            // design rather than taking it - ClaimShield with ReallyClaim
            // false (`UIGuild.cpp:927-934`). It is the only way to learn
            // whether a design is free, and the three bytes it carries
            // are read off Data.GuildShieldInfo, which the panel has
            // already written to (`BaseClient.cs:1312-1325`).
            _shieldDesigner.Preview += () => Act(() => _client.SendUserCommandClaimShield(false));
            // And the same command with the flag set takes it (:943).
            //
            // The reference sends this straight off the button with no
            // question asked. This asks first, the way the exile and
            // disband buttons here already do: claiming a shield is not
            // undoable, and a stray tap on a phone is cheaper to make
            // than a stray click on a desktop.
            _shieldDesigner.Claim += () => _ask?.Choice(
                "Claim this shield for your guild?", 0,
                _ => Act(() => _client.SendUserCommandClaimShield(true)));
            _ui.AddChild(_shieldDesigner);
        });
        Widget("guild hall buy", () =>
        {
            // No button opens this either: a GuildHalls UserCommand does.
            // `DataController` fills GuildHallsInfo and raises IsVisible
            // (`Meridian59/Data/DataController.cs:2820-2823`), the panel
            // follows the flag, and the reference does the same through
            // the model's PropertyChanged (`UIGuildHallBuy.cpp:25-29`,
            // `:60-70`).
            _hallBuy = new GuildHallBuyPanel();

            // Renting spends the guild's money, and the reference sends
            // it straight off the button (`UIGuildHallBuy.cpp:249`). The
            // question in between is this client's own, the way the exile,
            // disband and shield-claim buttons already ask - and it names
            // the hall and the price, because the list scrolls and the
            // row you meant is not always the row you hit.
            _hallBuy.Buy += (id, password) =>
            {
                GuildHall hall = null;
                var halls = _client.Data?.GuildHallsInfo?.GuildHalls;
                if (halls != null)
                    foreach (GuildHall h in halls)
                        if (h != null && h.ID == id) { hall = h; break; }

                string what = hall == null || string.IsNullOrWhiteSpace(hall.Name)
                    ? "this hall" : hall.Name;
                string price = hall == null ? "" : $" for {hall.Cost}, {hall.Rent} a day";

                _ask?.Choice($"Rent {what}{price}?", id, confirmed => Act(() =>
                {
                    _client.SendUserCommandGuildRent(confirmed, password);
                    // The reference's own tear-down after a successful
                    // send: throw the offer away and lower the flag
                    // (`UIGuildHallBuy.cpp:251-252`). Clear alone would
                    // not close the window - GuildHallsInfo.Clear does
                    // not touch IsVisible (`GuildHallsInfo.cs:171-181`).
                    _client.Data?.GuildHallsInfo?.Clear(true);
                    if (_client.Data != null) _client.Data.GuildHallsInfo.IsVisible = false;
                }));
            };

            // Cancel, and the list running dry after a server save. Both
            // are the same two lines in the reference (`:260-262`, and
            // `:139-144` for the empty list), and neither sends anything.
            _hallBuy.Cancelled += () =>
            {
                _client.Data?.GuildHallsInfo?.Clear(true);
                if (_client.Data != null) _client.Data.GuildHallsInfo.IsVisible = false;
            };
            _ui.AddChild(_hallBuy);
        });
        Widget("guild create", () =>
        {
            // A GuildAsk UserCommand raises this one
            // (`DataController.cs:2802-2804`); the reference listens for
            // the same flag (`UIGuildCreate.cpp:42-43`, `:74-82`).
            _guildCreate = new GuildCreatePanel();

            // Twelve strings and a flag, in the order
            // SendUserCommandGuildCreate takes them - all five male ranks,
            // then all five female ones (`BaseClient.cs:1342-1360`), which
            // is not the interleaved order they go on the wire in
            // (`UserCommandGuildCreate.cs:59-120`). The reference reads
            // them out of its edit boxes in exactly this order
            // (`UIGuildCreate.cpp:123-135`).
            //
            // Asked first, with the price the panel quoted: the reference
            // sends on the press with no question (`:118-143`), and
            // founding a guild is not undoable.
            _guildCreate.Found += f => _ask?.Choice(
                $"Found {f.Name}{(f.Secret ? " as a secret guild" : "")} for {f.Cost}?", 0,
                _ => Act(() =>
                {
                    _client.SendUserCommandGuildCreate(
                        f.Name,
                        f.Male[0], f.Male[1], f.Male[2], f.Male[3], f.Male[4],
                        f.Female[0], f.Female[1], f.Female[2], f.Female[3], f.Female[4],
                        f.Secret);
                    // Hidden, and the costs left alone - which is all the
                    // reference does after sending (`:138-140`).
                    if (_client.Data != null) _client.Data.GuildAskData.IsVisible = false;
                }));
            _guildCreate.Closed += () =>
            {
                if (_client.Data != null) _client.Data.GuildAskData.IsVisible = false;
            };
            _ui.AddChild(_guildCreate);
        });
        Widget("players", () =>
        {
            // WHY THE SEAT ARITHMETIC IS GONE, kept because it is the
            // case that proves the point: this button's old position,
            // 12 + 78 + 84*3, landed it exactly on top of the spell
            // book's opener, which reserved 330 and was 76 wide. The
            // two were drawn one over the other, Who was unreachable,
            // and once the Who panel had been opened once it came to
            // the front and took every later tap meant for Book. The
            // row mixed 64, 70, 76, 78 and 96-wide buttons, so no file
            // counting slots could get it right on its own. Panels
            // lays the openers out now - see Panels.Opener.
            _players = new PlayersPanel();
            _players.Opened += () => Act(() => _client.SendSendPlayers());
            // Nothing goes to the server: HandleSaid consults this list
            // and drops the message before it reaches the log.
            _players.Ignore += (name, on) => Act(() =>
            {
                var list = _client.Data?.IgnoreList;
                if (list == null || string.IsNullOrWhiteSpace(name)) return;
                if (on) { if (!list.Contains(name)) list.Add(name); }
                else list.Remove(name);
            });
            // Not the game's: its row has an ignore checkbox its own
            // source never implements. A tell has to start somewhere on
            // a phone, and the list of names is the obvious place.
            _players.Tell += who =>
            {
                _players.Close();
                // QUOTED, always. `ParseTell`'s unquoted branch
                // (`ChatCommand.cs:441-472`) extends the name one word
                // at a time only while the prefix is ambiguous and
                // stops at the SHORTEST prefix that matches one player,
                // treating everything after it as the message - so a
                // pre-filled "tell Cordelia the DM " sent the body
                // "the DM" to Cordelia, proved on the wire. The quoted
                // branch (`:344-347`, `:350-352`) matches the name
                // exactly with `GetItemByName`, which is what a name
                // taken from the server's own Who list deserves, and it
                // exists for precisely this case.
                //
                // Quotes in the name itself would break `GetQuote`, so
                // they come out; no Meridian name contains one.
                string name = (who ?? "").Replace("\"", "");
                _chat?.Compose($"tell \"{name}\" ");
            };
            _ui.AddChild(_players);
        });
        Widget("amount", () =>
        {
            _amount = new AmountPrompt();
            _amount.Chosen += (id, many) => Act(() =>
            {
                // The prompt is asked for by more than one thing now, so
                // what it means has to be remembered when it is opened.
                // Which of the things that can ask is asking.
                ObjectBase offering = _amountForTrade;
                _amountForTrade = null;
                if (offering != null) { _trade?.SetAmount(id, (uint)Math.Max(1, many)); return; }

                uint? lineID = _amountFor;
                _amountFor = null;
                if (lineID != null)
                {
                    // Buying part of a stack: the count goes back into
                    // the shop's own line, which is what the game does
                    // (`UIBuy.cpp:255`), so the row and the total follow
                    // it and the buy sends what you chose.
                    //
                    // The line is looked up NOW, by id, as the reference
                    // does by index when the box closes (`UIBuy.cpp:
                    // 240-258`): a stock list that arrived while the
                    // prompt was up replaced every line object, and a
                    // write into the one we were asked about would land
                    // nowhere. Capped at what the merchant has now.
                    TradeOfferObject line = _shop?.Line(lineID.Value);
                    if (line == null)
                    {
                        _chat?.Local("That item is no longer for sale, so nothing was changed.");
                        return;
                    }
                    uint most = _shop.Most(line);
                    line.Count = Math.Min((uint)Math.Max(1, many), Math.Max(1u, most));
                    _shop.Refresh();
                    return;
                }
                _client.SendReqDropMessage(new ObjectID(id, (uint)many));
            });
            // Backed out of: forget who asked, or the next stack dropped
            // is answered into that window instead and the drop is never
            // sent. No message - a cancel that does nothing is a cancel
            // doing its job.
            _amount.Cancelled += () => { _amountForTrade = null; _amountFor = null; };
            _ui.AddChild(_amount);
        });
        Widget("sheet", () =>
        {
            // Right of the Book button, left of the Bag.
            _sheet = new AttributesPanel();
            _sheet.Opened += () => Act(() =>
                _client.SendSendStatsMessage(Meridian59.Common.Enums.StatGroup.Attributes));
            _ui.AddChild(_sheet);
        });
        Widget("map", () => { _map = new MiniMap(); _ui.AddChild(_map); });

        // The word across the middle when the avatar cannot move.
        Widget("splash", () => { _splash = new SplashNotifier(); _ui.AddChild(_splash); });
        Widget("lost", () =>
        {
            _lost = new LostConnection();
            _lost.Retry += () =>
            {
                _chat?.Local("Reconnecting...");
                Connect();
            };
            _ui.AddChild(_lost);
        });
        // Quit (149): the server has ended the session - a logout, a
        // kick, a shutdown. The reference does two things the library's
        // own handler does not: it writes the played action button set to
        // config before the data is reset, and it drops back to the
        // scene characters are selected from
        // (`OgreClient.cpp:952-962`; `:946-949` is where that scene's
        // avatar-selection mode is switched to). Neither happened here,
        // so a quit left the HUD drawn over an emptied room until the
        // socket happened to close, and then blamed the connection.
        //
        // The panel is a local rather than a field on purpose: nothing
        // else in this view needs to ask about it, and a Widget block
        // that owns everything it builds cannot be half-built.
        Widget("quit", () =>
        {
            var left = new LeftWorld();
            // Back the way this client already goes back after a
            // connection drops: reconnect, which runs the login
            // handshake again and puts the character list up.
            left.Back += () => { _chat?.Local("Logging in again..."); Connect(); };
            _ui.AddChild(left);

            // Before Data.Reset() empties the button list and the player
            // name it is filed under - the same order the reference
            // keeps, writing the set and only then calling the base
            // handler (`OgreClient.cpp:955-958`). HotbarStore.Save is how
            // this client already persists the hotbar (ActionButtons.cs:261).
            _client.Quitting += () => HotbarStore.Save(_client.Data);

            _client.Quitted += () =>
            {
                // The world is gone, so the furniture over it goes with
                // it: inWorld is read from this flag or a live avatar,
                // and Reset has already taken the avatar.
                _wasInGame = false;
                // And the room's soundscape with it. The reference
                // reaches the same call from its Quit handler as from
                // its Disconnect - `DemoSceneLoadBrax()` at
                // `OgreClient.cpp:961` and `:641` - so a logout and a
                // dropped socket sound identical there, and the bug
                // fixed for the drop was the same bug here.
                LeftTheWorldAudio();
                left.Open();
            };
        });
        // InvalidateData (228). The server has thrown away the lists it
        // sent and will send them again
        // (`DataController.cs:2947-2951` -> `:1006`), which means an id
        // captured before the sweep is not about the same thing after
        // it. The reference overrides Invalidate for exactly this and
        // tells the confirmation popup (`DataControllerOgre.cpp:61-71`),
        // which hides itself and drops its handlers and its id when the
        // caller asked it to (`UIConfirmPopup.cpp:199-214`). There was no
        // subclass here to hang it on until MobileData; this is the wire.
        Widget("invalidate", () =>
        {
            _client.Data.Invalidated += () => _ask?.DataInvalidated();
        });
        Widget("statusbar", () =>
        {
            _bar = new StatusBar();
            _bar.Mood   += a => WorldAct(() => _client.SendActionMessage(a));
            // Non-vanilla, like Server 104: the whole preferences word
            // goes up rather than a dedicated safety command.
            _bar.Safety += _ => Act(() => _client.SendUserCommandSendPreferences());
            _bar.Players += () => { _players?.Open(); };
            _ui.AddChild(_bar);

            // The debug line moves down to clear it.
            if (_status != null)
                _status.Position = new Vector2(12, _bar.TopReserve + _bar.BlockHeight);
        });
        Widget("roombuffs", () =>
        {
            // Under the minimap, which owns the top-right corner.
            _roomBuffs = new RoomBuffsPanel { TopReserve = 220f + 8f };
            _roomBuffs.Look += id => Act(() => _client.SendReqLookMessage(id));
            _ui.AddChild(_roomBuffs);
        });
        Widget("names", () => { _names = new NameTags(); _ui.AddChild(_names); });
        Widget("questmarks", () => { _questMarks = new QuestMarkers(); _ui.AddChild(_questMarks); });
        // Added before the panels so blindness darkens the world and not
        // the buttons - the reference's compositors run on the 3D
        // viewport, and CEGUI is drawn over the top of them.
        // Its own layer, under the interface and over the world.
        //
        // Added to the UI layer it landed wherever it happened to be
        // built in the widget list, which is to say on top of the nine
        // widgets made before it and under the seven made after -
        // blindness blacked out the chat, the status line and six of
        // the thirteen bottom buttons while leaving Book, Bag and Loot
        // lit. The reference has no such question to answer: its
        // compositors run on the 3D viewport and CEGUI is drawn over
        // all of them. A CanvasLayer numbered below the interface says
        // the same thing once, for every widget, whatever order they
        // are built in.
        Widget("effects", () =>
        {
            _fxLayer = new CanvasLayer { Layer = 0 };
            AddChild(_fxLayer);
            _fx = new ScreenEffects();
            _fxLayer.AddChild(_fx);
        });
        // Your own hands, weapon, shield and held spell, over the world
        // and under the interface. See PlayerOverlays.
        //
        // On the effects layer rather than one of its own, and added
        // after the effect rectangle rather than before it, because both
        // of those are decisions the reference has already made. Under
        // the interface: the reference's overlay windows are sent to the
        // back of the GUI root (`UIPlayerOverlays.cpp:117-118`) with the
        // comment "set z-ordering so overlays are behind UI elements",
        // and this layer is numbered below the interface's, which says
        // the same thing once for every widget however the widget list is
        // ordered. Over the effects: the reference's effects are Ogre
        // compositors on the 3D viewport and CEGUI draws over all of
        // them, so a blinded player still sees their own hands. Being a
        // sibling after _fx settles that exactly - two CanvasLayers
        // sharing a layer number would leave it to the order they
        // happened to be constructed in.
        Widget("overlays", () =>
        {
            _overlays = new PlayerOverlays
            { Verbose = System.Environment.GetEnvironmentVariable("M59OVERLAYLOG") == "1" };
            if (_fxLayer != null) _fxLayer.AddChild(_overlays);
            else { var l = new CanvasLayer { Layer = 0 }; AddChild(l); l.AddChild(_overlays); }
        });
        Widget("look", () =>
        {
            _look = new LookPanel();
            // Writing on a book, a tombstone or a deed. The reference
            // sends the object's own id rather than the avatar's
            // (`UIObjectDetails.cpp:239-266`), which is what the
            // two-argument overload is for.
            _look.Inscribe += (id, text) => Act(() => _client.SendChangeDescription(id, text));
            // Editing your own player details, which the same window now
            // shows. Two separate commands, as the reference sends them:
            // the one-argument ChangeDescription, which the library aims
            // at your own avatar (`BaseClient.cs:2604-2606`), and a
            // ChangeURL user command (`UIPlayerDetails.cpp:225`, `:240`).
            _look.Describe += text => Act(() => _client.SendChangeDescription(text));
            _look.Homepage += url => Act(() => _client.SendUserCommandChangeURL(url));
            _ui.AddChild(_look);
        });
        Widget("sound", () =>
        {
            _sound = new M59Sound { Verbose = System.Environment.GetEnvironmentVariable("M59SOUNDLOG") == "1" };
            AddChild(_sound);

            _client.Sound += PlaySound;
            _client.SoundStopped += info => _sound.Stop(info);
            _client.Music += info => _sound.PlayMusic(info);
        });
        Widget("book", () =>
        {
            _book = new SpellsPanel();
            _book.Opened += () => Act(() => { _client.SendSendSpellsMessage(); _client.SendSendSkillsMessage(); });
            _book.Cast += id => WorldAct(() => _client.SendReqCastMessage(id));
            _book.Perform += id => WorldAct(() => _client.SendReqPerformMessage(id));
            _book.Look += id => Act(() => _client.SendReqLookMessage(id));
            // The row's list and the objects the hotbar needs are two
            // different lists: the panel shows AvatarSpells / AvatarSkills,
            // which are stat rows, while a button holds the SpellObject or
            // SkillObject. The game pairs them by position
            // (`UIActionButtons.cpp:394-411`); pairing by id is the same
            // answer whenever the two are in step and the right one when
            // they are not.
            _book.Assign += (id, spell) => Act(() =>
            {
                object what = spell
                    ? (object)_client.Data?.SpellObjects?.GetItemByID(id)
                    : _client.Data?.SkillObjects?.GetItemByID(id);
                if (what == null) { _chat?.Local("Nothing to put on the hotbar yet."); return; }
                if (ActionButtons.Bind(_client.Data, what))
                    _chat?.Local($"{(what as Meridian59.Data.Models.ObjectBase)?.Name} is on the hotbar. Drag the button off the row to clear it.");
            });
            _ui.AddChild(_book);
        });

        // The game's actions window. Three of its eleven had no way in
        // on a phone at all, and a long press on the hotbar could
        // clear one of the other eight for good.
        Widget("actions list", () =>
        {
            // Left of Auto, which is left of Go, which is left of the
            // panels' own row.
            _acts = new ActionsPanel();
            _acts.Perform += a => WorldAct(() => _client.ExecAction(a));
            _acts.Assign += a => Act(() =>
            {
                if (ActionButtons.Bind(_client.Data, a))
                    _chat?.Local($"{a} is on the hotbar. Drag the button off the row to clear it.");
            });
            // The twelfth row. Not ExecAction - there is no AvatarAction
            // for Go (see the Go tile below) - but the same send the
            // tile and the bound slot make, through the same gate.
            _acts.PerformGo += () => WorldAct(() => _client.SendReqGo(true));
            _acts.AssignGo += () => Act(() =>
            {
                if (ActionButtons.Bind(_client.Data, ActionButtons.Extra.Go))
                    _chat?.Local("Go is on the hotbar. Drag the button off the row to clear it.");
            });
            _ui.AddChild(_acts);
        });
        Widget("hotbar", () =>
        {
            _hotbar = new ActionButtons();
            // The hotbar goes through the same gates as everything else
            // the avatar does (see HotbarAct), where it used to call
            // Activate() straight from the button.
            _hotbar.Run = HotbarAct;
            _hotbar.SpendLatch = SpendSelfTarget;
            // The one slot the library cannot dispatch for itself, so
            // the view hands the hotbar the send. The same call the Go
            // tile makes, through the same gate: the hotbar wraps this
            // in Run, which is HotbarAct, which is WorldAct for a tap.
            _hotbar.GoSend = () => _client.SendReqGo(true);
            _hotbar.Cleared += name => _chat?.Local($"{name} cleared from the hotbar.");
            ActionButtons.Seed(_client.Data);
            // Above the target row, which is itself above the chat block:
            // the row is one button tall plus the name label over it.
            // The reserve is set once both rows exist - see below. The
            // hotbar is built before the target row, so measuring it
            // here reads a null and puts the hotbar through the
            // portrait.
            _ui.AddChild(_hotbar);
        });
        Widget("face", () =>
        {
            // Under the status lines rather than behind them.
            _face = new AvatarPanel { HeadSize = 72, Margin = 12f };
            _face.LookBuff += id => Act(() => _client.SendReqLookMessage(id));
            // Self-target, guarded the way the file guards it. Nothing
            // else here can select you: you cannot tap yourself in
            // first person, so every self-cast through the target row
            // had nothing to aim at.
            // Self-target is a MODE, not a target. SendReqCastMessage
            // checks Data.SelfTarget and aims the spell at your avatar
            // without touching TargetID (BaseClient.cs:1735-1741), which
            // is the whole point: the reference holds a modifier key
            // (`ControllerInput.cpp:776-778`) and lets go, and what you
            // were fighting is still what you were fighting. Setting
            // TargetID to your own id instead - which this did - meant a
            // self-heal in the middle of a fight threw the monster away
            // and you had to tap it again.
            //
            // There is no key to hold on a phone, so it latches: tap the
            // portrait, the next thing is aimed at you, and it clears
            // itself.
            _face.SelfTarget += () =>
            {
                if (_client.Data == null) return;
                _client.Data.SelfTarget = !_client.Data.SelfTarget;
                _chat?.Local(_client.Data.SelfTarget
                    ? "Self-target on: the next spell is aimed at you."
                    : "Self-target off.");
            };
            _ui.AddChild(_face);
        });

        Widget("inventory", () =>
        {
            _bag = new InventoryPanel();
            _bag.Opened      += () => Act(() => _client.SendReqInventoryMessage());
            _bag.UseItem     += item => WorldAct(() =>
            {
                // An Apply is aimed at the world, and the reference never
                // has to say so: its double click lands inside the
                // 250ms window, before the single click has written
                // Data.TargetID (`UIInventory.cpp:294-300`, `:350-352`),
                // and SendReqApply reads that same TargetID
                // (`BaseClient.cs:2080-2084`).
                //
                // The Use button has no such window - it is pressed long
                // after the tap that picked the item, by which time the
                // target IS the item. So when the only thing the apply
                // could aim at is the item itself, the target from before
                // the bag was opened is put back first; see the pair of
                // lines in Pump that save and restore it. A target on
                // something ELSE is left alone, which is how applying one
                // carried item to another still works.
                if (item != null && item.Flags != null && item.Flags.IsApplyable
                    && _client.Data != null && _client.Data.TargetID == item.ID
                    && _targetBeforeBag != uint.MaxValue)
                    _client.Data.TargetID = _targetBeforeBag;

                _client.UseUnuseApply(item);
            });
            // The tap has settled into a target, a double-tap window
            // after it - the reference's Inventory::Tick
            // (`UIInventory.cpp:292-301`). Selected is the same tap
            // reaching the buttons and the border immediately; only this
            // one moves the target, and that split is what leaves an
            // Apply something to aim at.
            _bag.Targeted    += item => Act(() =>
            {
                if (item != null && _client.Data != null) _client.Data.TargetID = item.ID;
            });
            // Backed out of "pick an item": forget who had asked. Without
            // this the next ordinary tap in the bag handed the item to
            // the trade or the container, with neither window open.
            _bag.PickCancelled += () => _pickFor = PickFor.Nobody;
            // Nothing subscribes to Selected here any more, and that is
            // the point of the split above: the tap's effect on the
            // target belongs to Targeted, one double-tap window later,
            // because the reference's single click does not set the
            // target either - Tick does (`UIInventory.cpp:294-300`). The
            // library resolves a target id against the room and then the
            // inventory, so a carried thing is still a legitimate target;
            // it just is not one the instant you touch it. What you had
            // targeted before the bag opened is put back when it closes;
            // see the pair of lines in Pump.
            _bag.Picked      += item =>
            {
                PickFor who = _pickFor;
                _pickFor = PickFor.Nobody;
                if (who == PickFor.Container) PutInContainer(item);
                else _trade?.Put(item);
            };
            // UIInventory.cpp: something that is not a stack drops
            // straight away with a count of zero, and a stack asks how
            // many first, prefilled with the lot.
            _bag.DropItem    += item => WorldAct(() =>
            {
                if (item.IsStackable && _amount != null)
                {
                    // Whose question this is, said at every open rather
                    // than only at the ones that route somewhere. A
                    // cancelled prompt used to leave the last answer's
                    // routing standing, so the next drop of a stack was
                    // filed as a trade amount or a shop amount and never
                    // left the client. Cancelled clears these too - both
                    // belts, because this is a drop going missing in
                    // silence.
                    _amountForTrade = null;
                    _amountFor = null;
                    _amount.Ask(item.ID, (int)item.Count, item.Name);
                }
                else
                    _client.SendReqDropMessage(new ObjectID(item.ID));
            });
            _bag.LookItem    += item => Act(() => _client.SendReqLookMessage(item.ID));
            _bag.BindItem    += item => Act(() =>
            {
                if (ActionButtons.Bind(_client.Data, item))
                    _chat?.Local($"{item.Name} is on the hotbar. Drag the button off the row to clear it.");
            });
            _bag.MoveItem    += (from, to) => Act(() => MoveInBag(from, to));
            _ui.AddChild(_bag);
        });

        Widget("vitals", () =>
        {
            // Top left, beside the portrait, as the game's avatar panel
            // has them.
            _vitals = new Vitals { Left = 94f, Top = 14f };
            _ui.AddChild(_vitals);
        });

        // Picking things up one at a time by tapping each is exactly the
        // sort of thing a phone is bad at, and the library already has
        // LootAll: everything gettable within close distance, in one go.
        Widget("loot", () =>
        {
            _loot = new Button { Text = "Loot" };
            // The game's loot key does not take anything: it brings the
            // loot window up, and brings it down again if it is already
            // up (ExecAction, AvatarAction.Loot). Taking is what the Get
            // and Get All buttons in that window are for.
            _loot.Pressed += () => WorldAct(() => _client.ExecAction(AvatarAction.Loot));
            _ui.AddChild(_loot);
            Panels.Opener(_loot, "Pick things up", 110);

            // Go, which is the game's Open key - space, by default
            // (`OISKeyBinding.cpp:52`), dispatched at
            // `ControllerInput.cpp:552` as SendReqGo(true).
            //
            // It is not AvatarAction.Activate and not the target row's
            // Open: those act on a thing you have tapped. ReqGo takes no
            // argument at all - it is "take me through", the doors and
            // the passages between rooms.
            //
            // Without it this client could walk around one room and
            // never leave it. A phone has no space bar; the reference
            // never had to think about that.
            //
            // IT IS A HOTBAR BINDING AS WELL, now, which this comment
            // used to say was impossible - "there is no AvatarAction for
            // it, so it cannot be a hotbar button and has to be its own
            // control". The first half is true and the second did not
            // follow: the hotbar learned the kind instead
            // (`ActionButtons.Extra`), so Go can sit in the combat arc
            // beside Attack, where it belongs - it is pressed while
            // moving, and the drawer that now holds this tile is modal
            // and stops the world.
            //
            // THE TILE STAYS ANYWAY, and deliberately. A tile costs one
            // cell of a grid that is only opened a few times an hour,
            // and it is the only Go a player has if he cleared the slot
            // by dragging it off, or if he is a character who played
            // before this change and whose saved hotbar the seed does
            // not touch (`HotbarStore.Load` wins over `Seed`). Taking
            // it away would be the same class of mistake as putting Go
            // in here in the first place: removing the last way to
            // reach something. It is the same send either way -
            // HotbarAct with keepLatch false is WorldAct exactly - so
            // there is one behaviour and two doors to it.
            // Named, because the caption is no longer unique: a bound Go
            // slot in the arc says "Go" too, and a bare-name press takes
            // whichever comes first in tree order - the trap
            // notes/harness.md calls a caption that exists twice.
            _go = new Button { Text = "Go", Name = "goButton" };
            // true, so the server is told where we are standing and
            // which way we face before it is asked to move us - the
            // reference passes the same, and SendReqGo forces both the
            // turn and the move out ahead of the request.
            _go.Pressed += () => WorldAct(() => _client.SendReqGo(true));
            _ui.AddChild(_go);
            Panels.Opener(_go, "Through the door", 120);

            // Autorun. A toggle rather than a hold, as the reference's
            // key is: press once and walk until something stops you.
            _auto = new Button { Text = "Auto", ToggleMode = true };
            _auto.Toggled += on =>
            {
                _autoMove = on;
                // Switched on with the stick already pushed? Then the
                // release you are about to make is the one you were
                // already making, and it clears this instead of the
                // autorun - `ControllerInput.cpp:568`.
                _autoMoveOnMove = on && _touch.Move.LengthSquared() > 0.0001f;
            };
            _ui.AddChild(_auto);
            // Movement, so it is pinned to the LEFT edge by the thumb
            // that steers, not buried in a drawer and not over on the
            // right where the look drag and the combat cluster are. It
            // is also a toggle, and a toggle whose state you cannot see
            // without opening a menu is a toggle you cannot trust - the
            // button follows _autoMove every frame and has to be on
            // screen for that to be worth anything.
            Panels.Opener(_auto, "Walk by itself", 10, Panels.Where.Left);

            // NEXT TARGET IS NOT HERE ANY MORE. It was the game's
            // NextTarget key (`ControllerInput.cpp:564`), and a phone
            // needs it more than a mouse does: a rat across a dark room
            // is a few pixels of tap target. But it is COMBAT, and the
            // one rule the drawer keeps is that combat does not live
            // behind a menu - picking your next enemy four taps deep is
            // worse than the bottom-edge row it replaced. It is in the
            // combat cluster now, beside the attack control, as
            // `ActionButtons._nextBtn` (`hotnext`), which sends it
            // through the same gate this did. Two buttons reading
            // "Next" is also one too many for a harness that presses by
            // text.

            // The list the game has: what is in the thing, with names in
            // the library's own colours, and a Get for one item as well as
            // the Get All this button does.
            _lootList = new LootPanel { Heading = "Loot", ShowGetAll = true };
            _lootList.GetAll += () => WorldAct(() => _client.LootAll());
            // One request per thing ticked, each with its count, which
            // is what the game's own Get loop sends
            // (`UILootList.cpp:270-275`). The count had been left off
            // here, so taking a pile of coins took one coin.
            _lootList.Look += id => Act(() => _client.SendReqLookMessage(id));
            _lootList.GetItems += items => WorldAct(() =>
            {
                foreach (ObjectBase o in items)
                    _client.SendReqGetMessage(new ObjectID(o.ID, o.Count));
            });
            _ui.AddChild(_lootList);

            // The same window again for what is inside a container. The
            // game keeps these apart - UILootList and UIObjectContents -
            // because they follow different lists and only one of them
            // can take everything at once.
            _contents = new LootPanel { Heading = "Contents", ShowGetAll = false, AllowPut = true };
            _contents.Look += id => Act(() => _client.SendReqLookMessage(id));
            _contents.GetItems += items => WorldAct(() =>
            {
                foreach (ObjectBase o in items)
                    _client.SendReqGetMessage(new ObjectID(o.ID, o.Count));
            });
            _contents.PutWanted += () =>
            {
                if (_bag == null) return;
                _pickFor = PickFor.Container;
                _bag.PickMode = true;
                _bag.Open();
            };
            _ui.AddChild(_contents);

            // The shop. One message buys everything ticked, which is what
            // Buy::OnOKClicked sends - not one per item.
            _shop = new BuyPanel();
            _shop.Look += id => Act(() => _client.SendReqLookMessage(id));
            _shop.AmountWanted += line =>
            {
                if (_amount == null || line == null) return;
                _amountForTrade = null;
                _amountFor = line.ID;
                // Prefilled with what you chose last time, capped at what
                // the merchant has. Those stopped being the same number
                // the moment choosing fewer wrote the choice into the
                // line (`UIBuy.cpp:255`), and passing the line's count as
                // both made the amount a ratchet - see BuyPanel.Most.
                _amount.Ask(line.ID, (int)line.Count, (int)_shop.Most(line), line.Name);
            };
            _shop.Buy += want => Act(() =>
            {
                ObjectBase who = _client.Data?.Buy?.TradePartner;
                if (who == null) return;
                var ids = new ObjectID[want.Count];
                for (int i = 0; i < want.Count; i++)
                    ids[i] = new ObjectID(want[i].ID, want[i].Count);
                _client.SendReqBuyItemsMessage(who.ID, ids);
            });
            _ui.AddChild(_shop);
        });

        Widget("character picker", () =>
        {
            _picker = new CharacterPicker();
            // Remembered on the way in, which is where the reference
            // remembers it: the write to Config->...->Character sits
            // immediately before the SendUseCharacterMessage on every one
            // of its three selection paths (`UIWelcome.cpp:170-175`,
            // `:214-219`, `:300-305`). See RememberCharacter.
            _picker.Chosen += c => { RememberCharacter(c?.Name); _client.UseCharacter(c); };
            // Asking for the palette is a separate round trip: the
            // server sends back every face part, colour, spell and
            // skill on offer, and the data layer builds the example
            // model out of it before the wizard has anything to show.
            // The empty slot's own id, not 0.
            //
            // `SendSystemMessageSendCharInfo()` defaults SlotID to 0
            // (`BaseClient.cs:2634-2645`) and that value is written as
            // the FIRST field of the NewCharInfo that follows
            // (`:2651-2670`). Server-104 reads that field as the user
            // object - `oUser = Nth(client_msg,2)` then
            // `Send(oUser, @IsFirstTime)` (`kod/util/system.kod:4570`,
            // `:4572`, `:4579`) - so a 0 is refused and creating a
            // character fails every time on the live server. The
            // reference passes the selected slot's own ID
            // (`UIWelcome.cpp:159-161`, `:203-205`, `:289-291`).
            //
            // The picker offers ONE "New character" button rather than
            // a row per slot (`CharacterPicker.cs:196-204`), so the slot
            // it means is the first empty one in the list the server
            // sent - which is remembered here, since the picker's event
            // carries no argument and that file is not ours to change.
            // The fixture's empty slot happens to BE id 0
            // (`Tools/Meridian59.Net8FakeServer/Program.cs:1855`), which
            // is why no harness run could ever have caught this.
            _picker.NewWanted += () => Act(() =>
                _client.SendSystemMessageSendCharInfo(_emptySlot));
            _ui.AddChild(_picker);
            _client.ChooseCharacter += chars =>
            {
                _emptySlot = 0;
                if (chars != null)
                    foreach (Meridian59.Data.Models.CharSelectItem c in chars)
                        if (c != null && c.IsEmptySlot) { _emptySlot = c.ID; break; }
                LoginPromptShown(false);
                _picker.Offer(chars);
                // After Offer, because Offer is what builds the rows -
                // which is the same order the reference works in, its
                // preselect being the tail of CharacterAdd
                // (`UIWelcome.cpp:118-128`).
                PreselectCharacter(RememberedCharacter());
            };

            _newChar = new CreateCharacter();
            _newChar.Create += (name, description) => Act(() =>
            {
                _client.Data.CharCreationInfo.AvatarName = name;
                _client.Data.CharCreationInfo.AvatarDescription = description;
                _client.SendSystemMessageNewCharInfo();
                // The character you are about to play, remembered here
                // because the creation path raises EnteredGame with no
                // name at all (`M59Client.cs:534`). The server may still
                // refuse the name, in which case the stored one matches
                // no row next time and the picker simply preselects
                // nothing - the cheapest possible failure.
                RememberCharacter(name);
            });
            _newChar.Complain += text => _ask?.Tell(text);
            // The wizard has no resource manager of its own, and the
            // example model arrives as resource ids rather than files.
            _newChar.Resolve = o => o.ResolveResources(_client.ResourceManager, false);
            // Backing out puts the list back rather than leaving the
            // screen empty.
            _newChar.Cancelled += () => _client.SendSendCharactersMessage();
            _ui.AddChild(_newChar);
            _client.CharacterPalette += info =>
            {
                LoginPromptShown(false);
                _newChar.Open(info);
                // The server's refusal arrives as a property change on
                // the same object the wizard is showing
                // (DataController.cs:2741-2744). The reference watches
                // it (`UIAvatarCreateWizard.cpp:411-490`); nothing here
                // did, so a refused name was silence and a closed
                // window.
                info.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != CharCreationInfo.PROPNAME_CHARINFONOTOKERROR) return;
                    _newChar.Refused(info.CharInfoNotOkError);
                    info.CharInfoNotOkError = CharInfoNotOkError.NoError;
                };
            };
        });

        // Now that the sound player and the touch controls exist, the
        // settings the panel restored can actually reach them. The panel
        // is built before they are, so it cannot do this itself - see
        // OptionsPanel.Apply.
        Widget("settings", () => _options?.Apply());

        // Last, so the drawer starts as the top child of the layer -
        // and after every panel, because this is what collects their
        // opener buttons into the grid. Mounting it earlier is not
        // wrong (it raises itself on open, and its Layout adopts
        // whatever has registered since), only pointlessly busier.
        Widget("menu", () => Panels.Mount(_ui));

        // RootClient.Start loads the config before calling Init, and this
        // did not load it at all. It is where the player's aliases, ignore
        // list and language come from, it falls back to defaults when
        // there is no file, and it writes nothing. It also CLEARS the
        // connection list, so it has to happen before the connection is
        // added rather than after - and before ResourcesPath, which a
        // configuration.xml is allowed to set.
        try
        {
            _client.Config.Load(Meridian59.Common.Config.CONFIGFILE,
                                Meridian59.Common.Config.CONFIGFILE_ALT);
        }
        catch (Exception e) { GD.Print($"[M59] no configuration loaded: {e.Message}"); }

        // The player's own aliases, on top of whatever that file had.
        // Nothing here ever calls Config.Save and a phone has no
        // writable file beside the executable anyway, so they live in
        // user:// - see AliasStore for the whole of why.
        AliasStore.Load(_client.Config);
        _aliases?.Follow(_client.Config);

        // ResourcesPath before Init, not after: Init is what reads it.
        _client.Config.ResourcesPath = _resDir;
        _client.Init();

        // After Init, because Init is what creates Data - subscribing
        // in the widget setup attached to nothing at all, and the
        // target row simply never appeared again.
        //
        // The row follows the library's target rather than the tap.
        // `UITarget` subscribes to exactly this and hides itself when
        // the target goes null, which happens on its own more often
        // than by tapping: the object leaves the room, you change room,
        // you log out, or the library re-binds the target when the
        // object model is rebuilt.
        // The guild window clears the shield model when it closes
        // (`UIGuild.cpp:951`); hand it the model rather than let it
        // reflect on the designer's private field. Data is created by
        // Init and its GuildShieldInfo is a readonly field of it, so this
        // is the one instance for the life of the client.
        if (_guild != null) _guild.ShieldInfo = _client.Data.GuildShieldInfo;

        // A shield refusal is shown the moment it arrives, as the reference
        // does (it hangs the popup off this property, `UIGuild.cpp:265-267`),
        // and not only on the per-frame poll in ShieldError: the model holds
        // ONE ServerString, so two refusals landing in the same frame left
        // the poll to see the second and lose the first. Messages are
        // handled inside _client.Update() on this thread, so the handler may
        // touch the popup. ConfirmPopup.Tell queues them behind whatever is
        // being read; the poll stays as the net for one already there.
        _client.Data.GuildShieldInfo.PropertyChanged += (_, e) =>
        {
            if (e?.PropertyName == Meridian59.Data.Models.GuildShieldInfo.PROPNAME_GUILDSHIELDERROR)
                ShieldError();
        };

        _client.Data.PropertyChanged += (_, e) =>
        {
            if (e?.PropertyName != Meridian59.Data.DataController.PROPNAME_TARGETOBJECT) return;
            _actions?.SetTarget(_client.Data.TargetObject, _client.Data.AvatarID);
        };

        // The chat log is followed rather than polled - see
        // ChatOverlay.Follow for why a count is not enough.
        _hotbar?.Follow(_client.Data);
        _chat?.Follow(_client.Data.ChatMessages);
        // Also after Init, for the same reason: Data is what holds it.
        _players?.Follow(_client.Data.IgnoreList);
        // The server the player picked on the login screen, not the one
        // baked into the scene. Still exactly one entry on the client's
        // own Config - the reference keeps the whole list there and
        // indexes into it, but that list was cleared by Config.Load a few
        // lines up and rebuilding it here would leave two sources of truth
        // for a choice that has already been made.
        ServerList.Entry where = Chosen;
        GD.Print($"[M59] server: {where.Name} {where.Address}  string file: {where.Strings}");
        _client.Config.Connections.Add(new ConnectionInfo(
            where.Name, where.Host, (ushort)where.Port, where.Strings,
            // The Character field is the reference's own store for the
            // character played last (`UIWelcome.cpp:170-172` writes it,
            // `:124-128` reads it back), so the entry starts out holding
            // it: the scene's own `Character` when one was set, and
            // otherwise whatever this server's row in character.cfg
            // says. See RememberCharacter for why the file exists as
            // well as the field.
            user, pass,
            string.IsNullOrWhiteSpace(Character) ? (RememberedCharacter() ?? "") : Character,
            null));
        _client.Config.SelectedConnectionIndex = _client.Config.Connections.Count - 1;

        // The saved language, now that there is both a Config to put it
        // on and a loaded string dictionary to point at it. After
        // Config.Load, because Load sets Language from configuration.xml
        // (`Config.cs:581-594`) and would overwrite anything set earlier;
        // before Connect, because Connect is what passes it to
        // SelectStringDictionary (`BaseClient.cs:119-121`).
        if (_options != null) UseLanguage(_options.ChosenLanguage);

        Resize();
        GetViewport().SizeChanged += Resize;

        if (AutoConnect) Connect();
        SetProcess(true);
    }

    public void Connect()
    {
        try { _state = "connecting"; _client.Connect(); }
        catch (Exception e) { Fail($"Connect failed: {e.GetType().Name}: {e.Message}"); }
    }

    void Fail(string msg)
    {
        _state = "error";
        Note(msg);
        if (_status != null) _status.Text = msg;
        // While the login screen is up it is the only thing on screen,
        // so a failure that only reached the status line was invisible.
        _login?.Trouble(msg);
        GD.PrintErr("[GameView] " + msg);
    }

    // The last distinct fault out of Update, and how many frames running
    // it has been the same one. See PumpFailed.
    string _lastFault;
    int _faults;

    /// <summary>
    /// Something threw while the client was applying server messages.
    ///
    /// Update runs every message handler there is - room parsing and BGF
    /// parsing among them - so this is exactly where a corrupt .roo comes
    /// out, and it used to come out as `Fail($"Update: {type}:
    /// {message}")`: one small line in the corner, no stack, no file
    /// name, the world frozen on its last frame and no way for the
    /// player to say anything useful about it. mobile-client.md carries
    /// the lesson the rest of this file follows - "a message without a
    /// stack is a riddle", M59Assets.cs:66 - and this path did not.
    ///
    /// So: the whole trace goes to the console every time, and the room
    /// file we were in goes on the screen, because that is the nearest
    /// thing to a filename the library gives us at this point and a
    /// corrupt room is the likeliest cause.
    ///
    /// The escalation is deliberate. One throw may be one bad object in
    /// one message, and killing the session over it would be a
    /// regression - the old code's `return` and carry on was right about
    /// that much. The same throw three frames running is not transient;
    /// it is a client that will never advance again, and then the player
    /// gets the full-screen report they can scroll, select and send.
    /// Pumping stops with it, which is also what keeps a repeating
    /// exception from churning at frame rate.
    /// </summary>
    void PumpFailed(Exception e)
    {
        string room = _world?.Room?.Filename;
        string what = $"{e.GetType().Name}: {e.Message}";
        string key = what + "|" + room;

        // Always, whole trace, whether or not it is the repeat that
        // earns a screen - a logcat with the first one in it is worth
        // having when the third never comes.
        GD.PrintErr($"[GameView] applying a server message (room {room ?? "none"}): {e}");

        if (key != _lastFault)
        {
            _lastFault = key;
            _faults = 1;
            Fail($"Update failed in {room ?? "no room"}: {what}");
            return;
        }

        // Said once already; saying it again per frame is what filled
        // the log up.
        if (++_faults < 3) return;

        Boom("applying a server message",
             $"room: {room ?? "none"}\n" +
             $"resources: {_resDir ?? "unknown"}\n" +
             $"state: {_state}\n\n" +
             $"The same failure has repeated every frame, so the client cannot " +
             $"go on. If the room file above is named, that is the file to " +
             $"suspect.\n\n{e}");
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        try { Pump(delta); }
        catch (Exception e) { Boom("running", e); }
    }

    void Pump(double delta)
    {
        if (_client == null) return;

        // Advance the client's clock, then pump the socket and apply
        // every message that arrived.
        //
        // RootClient.Tick does exactly these two in this order, and its own
        // loop is the only thing that normally calls it. Calling Update
        // alone leaves GameTick.Span at zero for ever, and Span is what
        // every timed thing in the library multiplies by: TryMove computes
        // its step as direction * speed * Span, so the avatar would not
        // move at all, objects would not interpolate, animations would not
        // advance and the request rate limiters would never come round.
        // RootClient.Tick itself is no use here because it sleeps - Godot
        // owns the frame timing.
        try { _client.GameTick.Tick(); _client.Update(); }
        catch (Exception e) { PumpFailed(e); return; }

        SyncRoom();
        _chat?.Sync(_client.Data?.ChatMessages);
        ApplyInput(delta);
        SyncSprites();
        ApplyTap();

        // Looking up and down, clamped: the horizon shear exaggerates the
        // further you push it.
        _pitch = Math.Clamp(_pitch + _touch.TakePitch(delta), -Renderer.MaxPitch, Renderer.MaxPitch);

        _clock += (float)delta;
        if (_world.Renderer != null)
        {
            _world.Renderer.Time = _clock;     // scrolling water and lava
            _world.Renderer.Pitch = _pitch;
            _world.Renderer.Brightness = Ambient();
            Sun(_world.Renderer);
        }

        _vitals?.Follow(_client.Data);
        _face?.Follow(_client.Data);
        _face?.SyncBuffs(_client.Data);
        _bag?.Sync(_client.Data?.InventoryObjects);
        _lootList?.Sync(_client.Data?.RoomObjectsLoot);
        _contents?.Sync(_client.Data?.ObjectContents);
        _shop?.Sync(_client.Data?.Buy);
        _sheet?.Sync(_client.Data?.AvatarAttributes);
        _players?.Sync(_client.Data?.OnlinePlayers);
        _quests?.Sync(_client.Data?.AvatarQuests);
        _trade?.Sync(_client.Data?.Trade);
        _npcQuests?.Sync(_client.Data?.QuestUIInfo);
        _roomBuffs?.Sync(_client.Data?.RoomBuffs);
        _bar?.Sync(_client.Data);
        _mail?.Sync(_client.ResourceManager?.Mails);
        _news?.Sync(_client.Data?.NewsGroup);
        _options?.Follow(_client.Data?.ClientPreferences);
        _fx?.Sync(_client.Data);
        _overlays?.Sync(_client.Data);
        TradeOffered();
        ShieldError();
        Wading();
        FollowSounds();
        _guild?.Sync(_client.Data?.GuildInfo, _client.Data?.DiplomacyInfo,
                     _client.Data != null ? _client.Data.AvatarID : 0u);
        _shieldDesigner?.Sync(_client.Data?.GuildShieldInfo, _client.Data?.GuildInfo);
        _hallBuy?.Sync(_client.Data?.GuildHallsInfo);
        _guildCreate?.Sync(_client.Data?.GuildAskData);
        _wizard?.Sync(_client.Data?.StatChangeInfo);
        _newChar?.Sync();
        // The message of the day, on the screen that chooses a
        // character. The reference sets it while building that window and
        // again on every change to the model's MOTD
        // (`UIWelcome.cpp:36-38`, `:55-63`); polling the string here does
        // both, and the selection screen is reachable more than once in a
        // session so both are needed.
        _picker?.Sync(_client.Data?.WelcomeInfo);

        // The button rows sit over the world, which is fine until a panel
        // covers the world.
        // None of the furniture until you are actually in the world.
        //
        // The character picker is a full-screen panel, and everything
        // the game draws over the world was up behind and around it -
        // the minimap dial over one corner, the vitals bars over
        // another, the hotbar across the middle, the menu row along the
        // bottom. It looked like a broken game rather than a choice of
        // character. None of it means anything before a character has
        // been picked, so none of it is drawn.
        // "Has an avatar in a room", not "did we press a character
        // button". Gating on the latter was wrong and I caught it by
        // creating a character: the server can put you in the world by
        // routes that do not go through UseCharacter, and when it did,
        // every piece of the interface stayed hidden - a playable room
        // with no bars, no hotbar and no menu, which is a far worse
        // failure than the cosmetic one the gate was added for.
        bool inWorld = _wasInGame || _client.Data?.AvatarObject != null;
        // The reference's UIMode::Playing, which is what decides whether
        // the Options window's Game tab shows its switches and password
        // boxes or the two "you must be logged in" lines instead
        // (`UIOptions.cpp:971`, :979-987). Set every frame rather than on
        // an event because the panel reads it when it opens, and there is
        // no one moment that is "logged in" - see the note above on why
        // this is an avatar in a room and not a button press.
        if (_options != null) _options.Playing = inWorld;
        foreach (Control c in new Control[] { _map, _bar, _roomBuffs, _names, _questMarks, _face, _vitals, _chat, _overlays })
            if (c != null) c.Visible = inWorld;
        if (_loot != null) _loot.Visible = inWorld;
        if (_go != null) _go.Visible = inWorld;
        if (_auto != null)
        {
            _auto.Visible = inWorld;
            // The button follows the state rather than owning it:
            // walking manually turns autorun off, and the button has
            // to say so.
            if (_auto.ButtonPressed != _autoMove) _auto.SetPressedNoSignal(_autoMove);
        }
        if (_splash != null)
        {
            _splash.Visible = inWorld;
            _splash.Sync(inWorld ? _client.Data : null);
        }

        // Your target survives a look in the bag. Picking a carried
        // thing targets it, which the game does too, but that is a
        // detour: coming back out you should still be facing whatever
        // you were facing.
        bool bagOpen = _bag != null && _bag.IsOpen;
        if (_client.Data != null)
        {
            if (bagOpen && !_bagWasOpen) _targetBeforeBag = _client.Data.TargetID;
            else if (!bagOpen && _bagWasOpen) _client.Data.TargetID = _targetBeforeBag;
        }
        _bagWasOpen = bagOpen;

        // The drawer is not in PanelUp and must not be: PanelUp is
        // what makes ShowOpeners(false) happen, and ShowOpeners(false)
        // is what SHUTS the drawer - it would close itself the frame
        // it opened. It still covers the screen, though, so the two
        // rows that live over the world go with it; a hotbar left
        // showing under the grid's scrim is a row of buttons that
        // cannot be pressed.
        bool covered = PanelUp || Panels.DrawerOpen;
        if (_hotbar != null) _hotbar.Visible = inWorld && !covered;
        // The row of buttons that open the panels goes with it - and
        // also while the chat box is up, because the row shares a line
        // with the entry. Each button belongs to the panel it opens, so
        // no panel could hide the others; Panels keeps the list.
        Panels.ShowOpeners(inWorld && !PanelUp && !(_chat != null && _chat.Capturing));
        // Not simply !covered: the row hides itself when there is
        // nothing targeted, and this runs every frame.
        if (_actions != null) _actions.Visible = inWorld && !covered && _actions.HasTarget;
        // Seeded every frame rather than once: the client clears its
        // lists when the world changes under it - a room change or a
        // relogin - and a row that was filled at startup would empty and
        // stay empty.
        ActionButtons.Seed(_client.Data);
        // Above the target block, which is above the chat. Measured
        // rather than guessed, and re-measured because the chat block
        // changes height with the number of lines it is showing. The
        // target block's height counts even while nothing is targeted
        // and the row is hidden, so the buttons do not hop down the
        // screen and back the moment you tap something.
        if (_hotbar != null && _chat != null)
        {
            Vector2 screen = GetViewportRect().Size;
            if (screen.X > screen.Y)
            {
                // Sideways: the chat keeps the left, the hotbar takes
                // the right and drops to just above the menu row. Left
                // stacked, the row landed across the middle of the
                // screen - which on a phone held sideways is exactly
                // where both thumbs drag.
                _hotbar.LeftReserve = _chat.BlockWidth + 24f;
                _hotbar.BottomReserve = 40f + 12f + 8f;
            }
            else
            {
                _hotbar.LeftReserve = 0f;
                _hotbar.BottomReserve = _chat.BlockHeight + (_actions?.BlockHeight ?? 0f) + 12f;
            }
        }
        _hotbar?.Sync(_client.Data);
        _look?.Sync(_client.Data);
        _book?.Sync(_client.Data);

        RoomObject me = _client.Data?.AvatarObject;
        if (me != null && _map != null)
        {
            // The walls first, and only now: SyncRoom leaves the rebuild
            // owed so that the new room's walls and the player's place in
            // them go up in the same frame. Rebuilt before SetPlayer,
            // because Build redraws from the pose it already has.
            if (!ReferenceEquals(_mapRoom, _world.Room))
            {
                _map.Build(_world.Room);
                _mapRoom = _world.Room;
                GD.Print($"[M59] map -> {_world.Room?.Filename ?? "nothing"} " +
                         $"({_map.MappedWalls} walls on the map)");
            }
            // The map works in the server's own units, as the game's does -
            // it is drawing the same numbers the objects arrive in.
            _map.SetObjects(_client.Data?.RoomObjects);
            _map.SetPlayer(me.Position3D.X, me.Position3D.Z, me.Angle);
        }

        _fpsAccum += delta; _frames++;
        if (_fpsAccum >= 0.5) { _fps = $"{_frames / _fpsAccum:F0} fps"; _fpsAccum = 0; _frames = 0; }

        RenderFrame();

        // AFTER the render, and that is the whole of the fix.
        //
        // Renderer.Project places a point against the camera of the
        // last frame Render was called with, because that is the only
        // camera it has - `_lastCamX` and the rest are written by
        // Render and nowhere else. These two Syncs used to run up here
        // with SyncSprites, BEFORE RenderFrame, so every label was
        // placed for camera(N-1) while its owner's sprite was drawn for
        // camera(N): the name slid off its owner for as long as you
        // kept turning and snapped back the moment you stopped. In the
        // reference a name is a BillboardSet attached to the object's
        // own SceneNode (RemoteNode.cpp:238, :334) and goes through the
        // same view matrix in the same pass, so it cannot lag by
        // construction.
        //
        // Nothing moves between RenderFrame and here - the object
        // positions are the library's and the pump is over - so the
        // labels now carry camera(N) with position(N), which is what
        // the sprites were drawn with. Project also reads the two depth
        // buffers Render fills, so this is the only order in which the
        // occlusion tests mean anything either.
        if (_names != null && _world.Renderer != null && _w > 0 && _h > 0)
        {
            // The world is drawn into a smaller buffer and stretched up,
            // so a name's place on screen is its place in that buffer
            // times the stretch.
            Vector2 v = GetViewportRect().Size;
            _names.Sync(_world.Renderer, _client.Data?.RoomObjects,
                        new Vector2(v.X / _w, v.Y / _h));
            _questMarks?.Sync(_world.Renderer, _client.Data?.RoomObjects,
                              new Vector2(v.X / _w, v.Y / _h));
        }

        QueueRedraw();
    }

    // Unhandled, not _Input: the chat box is a Control and has to see keys
    // and taps first, or typing walks you into a wall.
    public override void _UnhandledInput(InputEvent e)
        => _touch.Handle(e, GetViewportRect().Size);

    /// <summary>
    /// Turns touch and keyboard into avatar movement, then tells the server.
    ///
    /// The library does not move the avatar for us: for our own avatar
    /// RoomObject.UpdatePosition only snaps to a destination we already set.
    /// So the client owns the step - predict locally, then send - and the
    /// server corrects us if it disagrees.
    /// </summary>
    /// <summary>Was the avatar moving, or turning, last frame.</summary>
    bool _wasMoving, _wasTurning;

    /// <summary>
    /// Comes to a stop, and tells the server where.
    ///
    /// All this does is force the last stride out. `SendReqMoveMessage`
    /// is throttled, so without a forced send on the release edge the
    /// last part of a stride - up to a whole 100ms window of it - never
    /// reaches the server and it keeps you where it last heard. That is
    /// the rubberband on every stop, and the reference forces the same
    /// send on the movement-key release (`ControllerInput.cpp:601-604`).
    ///
    /// It does NOT touch the avatar's HorizontalSpeed, and must not.
    /// Zero is `MovementSpeed.Teleport` (SPEED_NONE,
    /// MovementSpeed.cs:26,38), and `RoomObject.UpdatePosition` gates
    /// its whole gravity and step-up block on
    /// `horizontalSpeed != MovementSpeed.Teleport`
    /// (RoomObject.cs:1102): with the speed zeroed, hDiff comes out 0,
    /// neither the falling branch nor the stepping-up branch runs, and
    /// the else branch snaps Position3D.Y straight onto the new floor.
    /// So letting go of the stick in mid-air used to TELEPORT you to the
    /// ground in one tick instead of falling - a 64-unit drop measured
    /// off the library itself is 41 ticks of 16ms at speed 55 and 1 tick
    /// at speed 0 - and the eased step-up was lost with it. Nothing in the reference ever writes the
    /// avatar's speed; only StartMoveTo does, with 25 or 55
    /// (ControllerInput.cpp:934-951).
    ///
    /// The write was not buying the throttle anything either:
    /// `SendReqMoveMessage` already returns early when the kod position
    /// has not changed (BaseClient.cs:1620-1626), so a standing avatar
    /// puts nothing on the wire whatever its speed says.
    /// </summary>
    void Settle()
    {
        if (!_wasMoving) return;
        try { _client.SendReqMoveMessage(true); }
        catch (Exception e) { _chat?.Local($"stop: {e.GetType().Name}: {e.Message}"); }
        _wasMoving = false;
    }

    /// <summary>
    /// Whether the walk modifier is held: the reference's KC_LSHIFT
    /// (OISKeyBinding.cpp:44), read as `IsWalkKeyDown` at
    /// ControllerInput.cpp:950 and :968. Running is what you do without
    /// it. A phone has no shift key, so the on-screen walk toggle -
    /// which is what the Run export now is, inverted - stands in.
    /// </summary>
    bool Walking() => Walk || Input.IsKeyPressed(Key.Shift);

    void ApplyInput(double delta)
    {
        RoomObject avatar = _client.Data?.AvatarObject;
        if (avatar == null || _world.Room == null) return;

        // Rearranging the HUD is not playing.
        //
        // The editor is a full-screen Control with MouseFilter.Stop, so
        // a drag on the glass is eaten by it and never reaches
        // _UnhandledInput - which means TouchControls has nothing to
        // report and the stick and the look drag are already silent.
        // This is the other half, for the two inputs that do NOT come
        // through the touch layer: the keyboard, and autorun, which
        // keeps walking with nothing touching the screen at all. A
        // player who stops to move their health bar should come back to
        // where they stopped, not to a wall.
        //
        // Settle() is on the entering edge only and is the right thing:
        // it is the stop message for a stride already taken, and
        // SendReqMoveMessage says nothing at all when the position has
        // not changed (BaseClient.cs:1620-1626).
        if (M59Hud.Editing) { Settle(); return; }
        // Anything covering the screen or owning the keyboard stops
        // movement, so a drag meant for a list does not also walk you.
        //
        // TURNING is not stopped with it. The reference applies the
        // avatar's yaw at ControllerInput.cpp:809-932, BEFORE the
        // `if (ControllerUI::ProcessingInput) return;` at :957, so
        // looking around keeps working with a window open - and its
        // movement gate is only on the keyboard anyway, both-mouse-
        // button movement carrying on through a window (:938-939).
        //
        // Here it was worse than merely stopped: the pitch was still
        // being taken each frame while the yaw was not, so a drag made
        // with a panel open piled up unspent and swung the view
        // sideways in one lump the moment the panel closed. Only look
        // input reaches this at all - _UnhandledInput means a drag a
        // panel wanted never got here - so what is applied is a drag on
        // the visible world, which is what the reference turns on too.
        if ((_chat != null && (_chat.Capturing || _chat.ShowingHistory)) || PanelUp
            || Panels.DrawerOpen)
        {
            float look = _touch.TakeTurn(delta);
            if (look != 0f) _client.TryYaw(look);
            if (_wasTurning && look == 0f) _client.SendReqTurnMessage(true);
            _wasTurning = look != 0f;

            // Autorun is exempt, as in the reference: `if (isAutoMove ||
            // isMoveByMouseOrKeysNotUsedByUI)` (ControllerInput.cpp:934-951)
            // drives TryMove on isAutoMove alone, and the
            // ProcessingInput early-out only comes after it (:957). The
            // stop above is for STICK movement (a drag meant for a
            // list must not walk you); autorun has nothing touching the
            // screen. The stick is deliberately not read here, so a
            // drag on a panel cannot cancel it either. The waiting gate
            // (:736) still applies.
            if (_autoMove && !(_client.Data != null && _client.Data.IsWaiting))
            {
                _wasMoving = true;
                float c0 = MathF.Cos(avatar.Angle), s0 = MathF.Sin(avatar.Angle);
                try { _client.TryMove(new V2(c0, s0), !Walking(), 0f); }
                catch (Exception e) { _chat?.Local($"move: {e.GetType().Name}: {e.Message}"); }
                return;
            }
            Settle();
            return;
        }

        // While the server has you waiting - a save, a teleport - the
        // reference abandons input entirely (`ControllerInput.cpp:736`).
        // It has to: `SendReqMoveMessage` refuses to send anything while
        // `IsWaiting` (BaseClient.cs:1624), so walking on regardless
        // moves you locally, tells the server nothing, and snaps you
        // back the moment the wait ends.
        if (_client.Data != null && _client.Data.IsWaiting)
        { Settle(); return; }

        float turn = 0f, fwd = 0f, strafe = 0f;
        if (Input.IsKeyPressed(Key.Left)) turn -= 1f;
        if (Input.IsKeyPressed(Key.Right)) turn += 1f;
        if (Input.IsKeyPressed(Key.W)) fwd += 1f;
        if (Input.IsKeyPressed(Key.S)) fwd -= 1f;
        if (Input.IsKeyPressed(Key.A)) strafe -= 1f;
        if (Input.IsKeyPressed(Key.D)) strafe += 1f;

        Vector2 stick = _touch.Move;
        strafe += stick.X;
        fwd -= stick.Y;                       // screen Y grows downward

        // Autorun. The reference cancels it on a manual forward or
        // back (`ControllerInput.cpp:607`), and the stick IS forward
        // and back here - so pushing it cancels, unless the autorun
        // was switched on mid-walk, in which case the first release
        // spends that grace instead. Turning with the look drag does
        // not cancel, which is the point: you run and steer.
        if (_autoMove)
        {
            bool pushed = MathF.Abs(stick.Y) > 0.35f;
            if (pushed)
            {
                if (_autoMoveOnMove) { /* the walk it was turned on during */ }
                else _autoMove = false;
            }
            else if (_autoMoveOnMove) _autoMoveOnMove = false;

            if (_autoMove && fwd == 0f) fwd += 1f;
        }

        // The reference halves the keyboard turn rate while the WALK
        // MODIFIER is held - `if (IsWalkKeyDown) diff *= 0.5f`,
        // ControllerInput.cpp:968-972 - not while you happen to be
        // moving, which is what this used to test. Its own rate is
        // KEYROTATESPEED * KeyRotateSpeed * milliseconds
        // (ControllerInput.h:49, OgreClientConfig.h:61), which works out
        // at 3 radians a second, and running forward while turning keeps
        // all of it. Halving it whenever you moved meant you could not
        // swing a corner at speed, and doubled it for turning on the
        // spot.
        bool moving = fwd != 0f || strafe != 0f;
        float rate = TurnSpeed * (Walking() ? 0.5f : 1f);
        float dAngle = turn * rate * (float)delta + _touch.TakeTurn(delta);
        if (dAngle != 0f) _client.TryYaw(dAngle);

        // A turn that has just ended has to be sent whether or not the
        // throttle is ready, or the last fraction of it is lost and you
        // swing, attack or cast at a facing the server does not have.
        // The reference forces one on every rotate-key release and when
        // mouse aiming stops (`ControllerInput.cpp:599`, :286).
        bool turning = dAngle != 0f;
        if (_wasTurning && !turning) _client.SendReqTurnMessage(true);
        _wasTurning = turning;

        if (!moving) { Settle(); return; }
        _wasMoving = true;

        // The library's own avatar movement, not a hand-rolled one. It
        // denies movement while resting or paralyzed, refuses to run on
        // low vigor, knows about the wolfpack buff and the movement-speed
        // percent, slows you in deep water, collides with objects flagged
        // no-move-on as well as with walls, slides using the room's own
        // VerifyMove, and starts the move properly so BaseClient.Update
        // sends it. None of which the version this replaced did.
        float c = MathF.Cos(avatar.Angle), sn = MathF.Sin(avatar.Angle);
        var dir = new V2(c * fwd - sn * strafe, sn * fwd + c * strafe);

        // Running is the DEFAULT, and the modifier slows you down. The
        // reference passes `!IsWalkKeyDown` (ControllerInput.cpp:950)
        // and binds walk to left shift (OISKeyBinding.cpp:44), so plain
        // WASD runs at 55 and holding shift walks at 25
        // (MovementSpeed.cs:35, picked at BaseClient.cs:2846-2848).
        //
        // This had it inverted, and worse: autorun, which is how you
        // cross a map on a phone, took its thumb off the stick and so
        // could never reach the deflection threshold - so the one
        // control built for travelling moved you at under half the
        // speed the reference travels at, and told the server so.
        //
        // The deflection test is gone with it. The reference's speed is
        // binary and changes only on a key edge; it normalises the
        // direction (ControllerInput.cpp:712) so a partial push never
        // scales anything. A speed that flipped at an invisible radius
        // under your thumb was this client's invention.
        bool running = !Walking();
        try { _client.TryMove(dir, running, 0f); }
        catch (Exception e) { _chat?.Local($"move: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>
    /// Builds one overlay, reporting rather than propagating if it throws.
    /// A missing widget is written into the status text so it is visible
    /// on the device, where there is no console to read.
    /// </summary>
    /// <summary>
    /// The one confirmation popup, built once. A question turned away
    /// because another is on screen (ConfirmPopup.Choice) is said in the
    /// chat, so the player is told rather than left with a dead tap.
    /// </summary>
    void MakeAsk()
    {
        if (_ask != null) return;
        _ask = new ConfirmPopup();
        _ask.Refused += what => _chat?.Local("Answer the question on screen first, then try again.");
        _ui.AddChild(_ask);
    }

    void Widget(string name, Action build)
    {
        try { build(); }
        catch (Exception e)
        {
            string msg = $"{name} unavailable: {e.GetType().Name}: {e.Message}";
            Note(msg);
            GD.PrintErr("[GameView] " + msg);
        }
    }

    /// <summary>
    /// The guildmaster test GuildPanel draws its hall controls by
    /// (`GuildPanel.cs:673`; the reference removes that tab whenever
    /// IsRenounce is set, `UIGuild.cpp:176-212`), read from the live model.
    /// </summary>
    bool GuildIsMaster()
    {
        GuildFlags f = _client.Data?.GuildInfo?.Flags;
        return f != null && !f.IsRenounce && f.IsDisband;
    }

    /// <summary>
    /// Whether exiling this member is still allowed, tested on the live
    /// roster at the moment Yes is pressed: the flag, a member who is still
    /// on it, and the per-row rule of `UIGuild.cpp:451` with THEIR rank now.
    /// </summary>
    bool GuildMayExile(uint id)
    {
        GuildInfo info = _client.Data?.GuildInfo;
        GuildFlags f = info?.Flags;
        if (f == null || !f.IsExile || id == 0) return false;
        GuildMemberEntry m = info.GuildMembers?.GetItemByID(id);
        if (m == null) return false;
        return !(id == _client.Data.AvatarID || m.Rank == 5 || (m.Rank == 4 && !f.IsDisband));
    }

    /// <summary>
    /// Abdication is the rank-5 picking rank 5 for someone else
    /// (`UIGuild.cpp:733`); both halves are tested again on the live model,
    /// and the heir has to still be on the roster.
    /// </summary>
    bool GuildMayAbdicate(uint id)
    {
        GuildInfo info = _client.Data?.GuildInfo;
        GuildFlags f = info?.Flags;
        if (f == null || !f.IsAbdicate || id == 0 || id == _client.Data.AvatarID) return false;
        GuildMemberEntry me = info.GuildMembers?.GetItemByID(_client.Data.AvatarID);
        return me != null && me.Rank == 5 && info.GuildMembers.GetItemByID(id) != null;
    }

    /// <summary>Said in-page when a confirmed guild command is dropped.</summary>
    void GuildNoLonger() => _chat?.Local("You can no longer do that, so nothing was sent.");

    /// <summary>
    /// Throws away what the client thinks about the guild and asks
    /// again. None of the guild commands is echoed, so the reference
    /// clears and re-requests after each one rather than guessing
    /// (`UIGuild.cpp:604-620`) - and it clears the shield model too,
    /// and asks for the guild list as well as the roster, which is
    /// what makes the diplomacy view come back current.
    ///
    /// The shield had been half of that: cleared and never asked for
    /// again. Clearing is the easy half and on its own it is strictly
    /// worse than doing nothing, because the shield information the
    /// window had was correct until this threw it away. So after an
    /// exile, an abdication or a rank change - the three callers - the
    /// guild's shield went blank and stayed blank for the rest of the
    /// session, since nothing else in the client ever re-asks.
    ///
    /// The reference asks for the shield in the same breath as the
    /// roster, every time. Exile sends four requests, shield list and
    /// shield info among them (`UIGuild.cpp:613-619`); abdicate sends
    /// info and shield info (`:631-634`); set-rank the same two
    /// (`:726-729`). The shield LIST is what exile asks for on top,
    /// because a change of membership can change which shields are
    /// available; it is harmless on the other two paths and asking
    /// uniformly is better than three near-identical variants of this
    /// function.
    /// </summary>
    void Reask()
    {
        _client.Data?.GuildInfo?.Clear(true);
        _client.Data?.GuildShieldInfo?.Clear(true);
        _client.SendUserCommandGuildInfoReq();
        _client.SendUserCommandGuildGuildListReq();
        _client.SendUserCommandGuildShieldListReq();
        _client.SendUserCommandGuildShieldInfoReq();
    }

    /// <summary>Takes a guild out of one of the declaration lists.</summary>
    static void Drop(Meridian59.Data.Lists.ObjectIDList<ObjectID> list, uint id)
    {
        if (list == null) return;
        ObjectID had = list.GetItemByID(id);
        if (had != null) list.Remove(had);
    }

    /// <summary>
    /// Sends a UI request, and says so rather than throwing if it fails.
    ///
    /// This used to refuse to send anything at all while
    /// `Data.IsWaiting`, and the guard was an invention: the reference
    /// puts no such test anywhere near a window. Search the UI sources
    /// for IsWaiting and there is exactly one hit in the whole set, in
    /// the splash notifier that DRAWS the wait (`UISplashNotifier.cpp:94`).
    /// Every sender is unconditional - `UIGuild.cpp` throughout,
    /// `UIMail.cpp:284`, `UITrade.cpp:492`, `UIOptions.cpp:2728` and
    /// :2811. IsWaiting gates the WORLD, in the input controller and
    /// nowhere else: key down (`ControllerInput.cpp:541-550`), key up
    /// (:582-592, :596-601) and the whole per-frame input tick, which is
    /// where held action-button keys are read (:736, reading buttons at
    /// :995-1022). See <see cref="WorldAct"/>, which keeps that half.
    ///
    /// The difference is not cosmetic, because a suppressed send is
    /// invisible: the caller returns as though it had sent. Trade is the
    /// clear case. Cancel sends SendCancelOffer and then clears the local
    /// trade either way, which is the reference's own shape
    /// (`UITrade.cpp:490-495` on close, :507-513 on ESC - cancel if
    /// IsPending, clear regardless). With the guard in front of it, a
    /// Cancel during a wait cleared this side and sent nothing: the
    /// window closed, the client believed the trade was over, and the
    /// partner sat holding a pending offer against an avatar that would
    /// never answer it. A wait is a second or two; a stranded offer is
    /// not.
    ///
    /// Honest footnote on the reference, since it is the specification:
    /// its MOUSE path does drop clicks during a wait, because the
    /// IsWaiting test at `ControllerInput.cpp:336` (press) and :269
    /// (release) sits BEFORE the injection into CEGUI. Its keyboard path
    /// does not - :538-540 injects and only then tests. So the reference
    /// is not uniformly one thing at the widget level; what it is
    /// uniformly is this: no window ever asks about IsWaiting before
    /// sending, and the one thing a wait is there to stop is the avatar
    /// acting in the world. That is the line drawn here, and it is the
    /// line that would have kept the trade consistent.
    /// </summary>
    void Act(Action send)
    {
        try { send(); }
        catch (Exception e) { _chat?.Local($"{e.GetType().Name}: {e.Message}"); }

        // The latch spends itself on whatever was just sent, so you do
        // not heal yourself for the rest of the fight by accident. The
        // reference gets this free by holding a key down.
        SpendSelfTarget();
    }

    /// <summary>
    /// Clears the self-target latch. The reference has no latch: it sets
    /// `Data->SelfTarget = IsSelfTargetDown` every input tick
    /// (`ControllerInput.cpp:776-778`), so the aim ends the moment the
    /// modifier does. This is the phone's version of letting go.
    /// </summary>
    void SpendSelfTarget()
    {
        if (_client?.Data != null && _client.Data.SelfTarget)
        {
            _client.Data.SelfTarget = false;
            _chat?.Local("Self-target off.");
        }
    }

    /// <summary>
    /// A hotbar activation. The reference reads the action-button keys
    /// inside the input tick behind its IsWaiting exit
    /// (`ControllerInput.cpp:736`, keys at :995-1030), so a press during
    /// a wait is dropped, and the aim it was fired with is the modifier
    /// held at that instant (:776-778). This is both: the wait gate, and
    /// the latch spent by the press. A button being HELD passes
    /// <paramref name="keepLatch"/> so a repeating self-heal stays aimed
    /// at you until the finger lifts, which is how a held modifier
    /// behaves. Returns whether the send went out.
    /// </summary>
    bool HotbarAct(Action send, bool keepLatch)
    {
        if (_client?.Data != null && _client.Data.IsWaiting) return false;
        try { send(); }
        catch (Exception e) { _chat?.Local($"{e.GetType().Name}: {e.Message}"); }
        if (!keepLatch) SpendSelfTarget();
        return true;
    }

    /// <summary>
    /// Sends an action the AVATAR takes in the world - attack, cast,
    /// perform, activate, loot, get, buy, trade, use, drop, go, retarget
    /// - and drops it while the server has you waiting.
    ///
    /// This is the half of the old blanket guard that belongs. In the
    /// reference these are keys and world clicks, not windows: the
    /// action buttons are read inside the input tick
    /// (`ControllerInput.cpp:995-1022`) behind its IsWaiting exit
    /// (:736), ReqGo, target-clear and NextTarget behind the key-down
    /// exit (`:541-550` then :553-566), and the right-click action
    /// button behind the mouse-press exit (:336, firing at :322). The
    /// reason is the same reason the reference has it: these requests
    /// are ignored by a server that has you waiting while still spending
    /// the client's local attack and cast throttles, so pressing Attack
    /// through a teleport buys nothing and costs you the swing on the
    /// other side.
    ///
    /// Everything that merely asks a window's question - a roster, a
    /// mailbox, a look, a form, a cancel - goes through
    /// <see cref="Act"/> and is not gated.
    /// </summary>
    void WorldAct(Action send)
    {
        if (_client?.Data != null && _client.Data.IsWaiting) return;
        Act(send);
    }

    /// <summary>
    /// Turns a tap into a target. The renderer picks the sprite under the
    /// pixel - opaque texels only, and never through a wall - and the
    /// object it stands for becomes the library's target, which is what
    /// every no-argument Send* overload acts on.
    /// </summary>
    void ApplyTap()
    {
        if (_world.Renderer == null || _client?.Data == null) return;
        // Retargeting during a wait is the same mistake as acting during
        // one, and a swapped target mid-teleport is the worst version of
        // it. The reference drops the press outright
        // (`ControllerInput.cpp:333-339`).
        if (_client.Data.IsWaiting) { _touch.TakeTap(out _); return; }
        if (!_touch.TakeTap(out Vector2 screen)) return;

        // Screen is the stretched viewport; the renderer works in its own
        // smaller buffer.
        Vector2 view = GetViewportRect().Size;
        if (view.X < 1f || view.Y < 1f) return;
        int bx = (int)(screen.X / view.X * _w);
        int by = (int)(screen.Y / view.Y * _h);

        RoomObject avatar = _client.Data.AvatarObject;
        if (avatar == null) return;
        _world.Camera(avatar, out float cx, out float cy, out float cz);

        // Everything under the thumb, nearest first, so a second tap in
        // the same place reaches what is standing behind the first
        // thing. That is the library's own rule -
        // `DataController.ClickTarget` takes the first id you have not
        // already picked and starts over when the list runs out
        // (DataController.cs:1403-1441) - and the reference feeds it the
        // whole distance-sorted ray (`ControllerInput.cpp:153-215`),
        // minus the current target (see below), so the cycle is the
        // exclusion. Handing it only the nearest made the creature at
        // the back unselectable.
        var ids = new System.Collections.Generic.List<uint>();
        RoomObject obj = null;
        foreach (Renderer.Sprite sp in
                 _world.Renderer.PickAll(bx, by, _w, _h, cx, cy, cz, avatar.Angle))
        {
            if (!(sp.Tag is RoomObject ro) || ro.IsAvatar) continue;
            // "don't select own avatar or already selected target": the
            // reference leaves the current target out of the list
            // (`ControllerInput.cpp:205-206`, `!obj->IsAvatar &&
            // !obj->IsTarget`). That exclusion IS the cycle: tap where A
            // and B overlap with A selected and the list is [B]; tap
            // again and it is [A]. Keeping it in the list, as this did,
            // made the first tap on a stack re-pick what you already had.
            if (ro.IsTarget) continue;
            if (obj == null) obj = ro;
            ids.Add(ro.ID);
        }

        // A tap that hits nothing is not a command to forget what you
        // were fighting. The reference changes only the cursor when a
        // click misses (`ControllerInput.cpp:243-246`); the one
        // deliberate clear is the Close key (:554-560). Clearing on a
        // miss is bad enough on a mouse and worse under a thumb: drop
        // the target mid-melee and the next Attack quietly picks the
        // nearest attackable in front of you instead, which may be a
        // different monster or somebody's pet.
        if (obj == null) return;

        // Only the id is set, and by the library rather than here. It
        // resolves it to an object and raises TargetObject, and the row
        // follows that - which is how the game does it, and the only way
        // the row hears about the targets the library sets by itself:
        // the thing you killed leaving the room, a room change, a
        // tab-target, a click-target.
        // UseFirst = true, as the reference passes it
        // (`ControllerInput.cpp:238`, `ClickTarget(objectIDs,
        // !IsSelfTargetDown)`; a phone has no held modifier here, so it
        // is always true). With the current target already excluded
        // above, UseFirst takes the nearest of what is left and RESETS
        // the cycle memory (`DataController.cs:1409-1418`). With false,
        // ClickedTargets persisted across different screen positions:
        // tap A, tap C elsewhere, then tap where A and B overlap and
        // A was still on the "already picked" list, so B won.
        _client.Data.ClickTarget(ids, true);
    }

    /// <summary>
    /// The screen point where a named object can be tapped, or false if
    /// nothing by that name is visible from here.
    ///
    /// Harness support, and deliberately not a shortcut: it finds the
    /// point by asking the renderer the same question a finger asks -
    /// Pick, at a grid of screen points, opaque texels only and never
    /// through a wall - so a test that taps the answer exercises the
    /// whole path rather than setting TargetID behind its back. Written
    /// after a scripted run spent three tries guessing where a duskrat
    /// was standing.
    /// </summary>
    public bool ScreenPointOf(string name, out Vector2 screen)
    {
        screen = Vector2.Zero;
        if (string.IsNullOrWhiteSpace(name) || _world.Renderer == null) return false;

        RoomObject avatar = _client?.Data?.AvatarObject;
        if (avatar == null) return false;

        Vector2 view = GetViewportRect().Size;
        if (view.X < 1f || view.Y < 1f) return false;

        _world.Camera(avatar, out float cx, out float cy, out float cz);

        // Coarse: a phone sprite a grid step wide is not worth missing,
        // and the whole sweep is a few thousand picks on a buffer that
        // is 480 across.
        const int step = 4;
        for (int by = 0; by < _h; by += step)
            for (int bx = 0; bx < _w; bx += step)
            {
                Renderer.Sprite hit = _world.Renderer.Pick(bx, by, _w, _h, cx, cy, cz, avatar.Angle);
                if (hit?.Tag is not RoomObject o || o.ID == avatar.ID) continue;
                if (o.Name == null ||
                    !o.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;

                // In WINDOW pixels, not the viewport's.
                //
                // The project stretches a 1920x1080 viewport over
                // whatever window it gets, so GetViewportRect is 1920
                // wide in a 1280-wide window and a point taken from it
                // is half again too far right and too far down. Fed
                // back as a touch - which is measured in window pixels
                // - a duskrat's own position landed on the Inspect
                // button, and a coin's landed off the bottom of the
                // screen entirely. Every scripted tap on a named object
                // was quietly aimed at the wrong thing, and the ones
                // that hit something looked like the client doing
                // something odd.
                Vector2 window = DisplayServer.WindowGetSize();
                float sx = view.X > 0f ? window.X / view.X : 1f;
                float sy = view.Y > 0f ? window.Y / view.Y : 1f;
                screen = new Vector2(bx / (float)_w * view.X * sx,
                                     by / (float)_h * view.Y * sy);
                return true;
            }

        return false;
    }

    /// <summary>
    /// Plays a sound the server asked for. The object case is resolved
    /// here rather than in the sound player, because this is where the
    /// object list is: a sound with a source id comes from wherever that
    /// object is standing.
    /// </summary>
    /// <summary>
    /// The splash you make walking through water, which nothing here
    /// was playing.
    ///
    /// `ControllerSound::UpdateListener` (:155-192) checks it every
    /// time the listener moves: you are below the sector's own floor
    /// height - which is what standing in a depth sector means, since
    /// the depth is subtracted from where your feet go and not from the
    /// drawn surface - the sector has some depth, and enough time has
    /// passed since the last splash. That interval is 500ms times the
    /// depth level, so deeper water splashes less often, which sounds
    /// backwards until you picture wading rather than paddling.
    ///
    /// The sound is the room's, not the sector's: RoomInfo carries the
    /// file for the whole room.
    /// </summary>
    void Wading()
    {
        if (_sound == null || _client?.Data == null) return;
        RoomObject me = _client.Data.AvatarObject;
        if (me == null) return;

        // Only when you have actually MOVED. In the reference this block
        // is inside UpdateListener, which is reached from nothing but the
        // avatar's own PropertyChanged for Angle and Position3D
        // (`ControllerSound.cpp:119-127`) - and it then insists on
        // `lastListenerPosition != pos` on top of that (:155), storing
        // the position afterwards whatever the wading checks decide
        // (:195). Called once a frame with no such test, this splashed
        // away every half-second while you stood in a puddle reading the
        // chat, which is a thing a person does and not a thing wading
        // sounds like. The store is unconditional here for the same
        // reason it is there: a turn on the spot is not a step.
        V3 stood = me.Position3D;
        bool moved = stood != _heardFrom;
        _heardFrom = stood;
        if (!moved) return;

        RooSubSector leaf = me.SubSector;
        if (leaf?.Sector == null) return;

        var depth = leaf.Sector.Flags.SectorDepth;
        if (depth == RooSectorFlags.DepthType.Depth0) return;

        // The floor WITHOUT the depth taken off, which is the surface
        // of the water; the avatar's own height already has it off.
        float hFloor = leaf.Sector.CalculateFloorHeight(
            M59Geo.KodToWorld(me.Position3D.X), M59Geo.KodToWorld(me.Position3D.Z), false)
            * (1f / M59Geo.KodToRoom);   // back to kod, where Position3D lives
        if (me.Position3D.Y >= hFloor) return;

        double now = Time.GetTicksMsec();
        if (now - _splashedAt <= 500.0 * (uint)depth) return;
        _splashedAt = now;

        RoomInfo room = _client.Data.RoomInformation;
        if (room == null || string.IsNullOrEmpty(room.ResourceWadingSound)) return;

        var splash = new PlaySound
        {
            ID = 0, Row = 0, Column = 0,
            ResourceName = room.WadingSoundFile,
            Resource = room.ResourceWadingSound,
        };
        PlaySound(splash);
    }

    /// <summary>
    /// Somebody has offered you a trade.
    ///
    /// Neither client opens the window for this - the reference waits
    /// for you to press Trade, and `HandleOffer` deliberately leaves
    /// IsVisible false (DataController.cs:3007). On a desktop that is
    /// fine: the offer arrives with a line of server text and the
    /// window is one of a handful. On a phone the Trade button is one
    /// of thirteen along the bottom edge and the only way to find out
    /// was to guess. So a line in the chat, once, when the offer
    /// arrives. A departure, and a small one.
    /// </summary>
    void TradeOffered()
    {
        TradeInfo t = _client?.Data?.Trade;
        bool offered = t != null && t.IsBackgroundOffer && t.IsPending;
        if (offered && !_wasOffered)
        {
            string who = t.TradePartner?.Name;
            _chat?.Local(string.IsNullOrWhiteSpace(who)
                ? "Someone offers you a trade. Press Trade to see it."
                : $"{who} offers you a trade. Press Trade to see it.");
        }
        _wasOffered = offered;
    }

    /// <summary>Whether an offer was already waiting last frame.</summary>
    bool _wasOffered;

    /// <summary>
    /// The server's answer when a shield claim goes wrong.
    ///
    /// GuildShieldInfo carries it as a ServerString and the reference
    /// puts it in an OK popup and clears it (`UIGuild.cpp:265-267`,
    /// :643). GuildShieldPanel is where a claim comes from now, and this
    /// is where its refusal lands - but it stays outside that panel on
    /// purpose, because a claim can fail after the panel has been closed
    /// and dropping the server's reason silently is the one thing that
    /// should not happen to it.
    /// </summary>
    void ShieldError()
    {
        ServerString err = _client?.Data?.GuildShieldInfo?.GuildShieldError;
        string text = err?.FullString;
        if (string.IsNullOrWhiteSpace(text)) return;
        err.Clear(true);
        _ask?.Tell(text);
    }

    /// <summary>
    /// Tells the looping sounds where their objects have got to, and
    /// which of them have left. See M59Sound.Follow.
    /// </summary>
    void FollowSounds()
    {
        if (_sound == null || _client?.Data == null) return;
        RoomObject me = _client.Data.AvatarObject;
        if (me == null) return;

        // The height goes with the other two. The reference gives
        // irrklang all three components of both the source
        // (`ControllerSound.cpp:405-408`, :420-428) and the listener
        // (:140-142); measuring the distance flat made a sound one floor
        // down as loud as one in the room.
        _sound.Follow(id =>
        {
            RoomObject o = _client.Data.RoomObjects?.GetItemByID(id);
            return o == null ? (false, 0f, 0f, 0f)
                             : (true, o.Position3D.X, o.Position3D.Z, o.Position3D.Y);
        }, me.Position3D.X, me.Position3D.Z, me.Position3D.Y, me.Angle);
    }

    /// <summary>When the last splash was, so the next one waits its turn.</summary>
    double _splashedAt;

    /// <summary>
    /// Where you were standing when the wading check last ran - the
    /// reference's `lastListenerPosition` (`ControllerSound.cpp:12`,
    /// stored at :195).
    /// </summary>
    V3 _heardFrom = V3.ZERO;

    void PlaySound(PlaySound info)
    {
        if (_sound == null || info == null) return;

        // Sounds can arrive before the avatar does - the server plays one
        // on entering a room - and a sound with nowhere to stand is still
        // a sound, so it plays flat rather than being dropped.
        RoomObject me = _client.Data?.AvatarObject;
        float lx = 0f, ly = 0f, lh = 0f, facing = 0f;
        if (me != null)
        { lx = me.Position3D.X; ly = me.Position3D.Z; lh = me.Position3D.Y; facing = me.Angle; }

        if (info.ID > 0)
        {
            RoomObject source = _client.Data?.RoomObjects?.GetItemByID(info.ID);
            if (source != null)
            {
                // Played at the object, by pretending the listener is
                // where they are relative to it - the player takes a
                // place and works out the rest. All three components of
                // it, as the reference reads all three off the source's
                // scene node (`ControllerSound.cpp:405-408`).
                _sound.PlayAt(info, source.Position3D.X, source.Position3D.Z,
                              source.Position3D.Y, lx, ly, lh, facing);
                return;
            }
        }

        _sound.Play(info, _world.Room, lx, ly, lh, facing);
    }

    /// <summary>
    /// How bright the room is, 0 to 1.
    ///
    /// `ControllerRoom::AdjustAmbientLight` takes the larger of the
    /// room's ambient light and the avatar's own - night vision, a lamp -
    /// and uses it as a plain ratio of 255. It is read every frame
    /// because the server changes both of them as you play: dusk falls,
    /// a spell wears off. A room that has not said anything yet is left
    /// at full brightness rather than being rendered black.
    /// </summary>
    /// <summary>
    /// The one directional light, which the renderer had the arithmetic
    /// for and nothing to feed it.
    ///
    /// Its strength is LightShading.LightIntensity through
    /// AdjustAmbientLight, which multiplies it by three and by the
    /// player's brightness slider before handing it to the sun and the
    /// moon (ControllerRoom.cpp:1434-1446). With no LightShading message
    /// yet - which is what an indoor room leaves you with - the
    /// intensity is zero and the term vanishes, leaving 0.6 of the
    /// ambient, as the shader says (general.hlsl:114-115).
    ///
    /// DIVERGENCE: the DIRECTION. The server sends one as a sphere
    /// position, an angle round the horizon and a height, and the
    /// reference throws it away and lets Caelum's clock place the sun
    /// instead. Porting an astronomical model to get a shading direction
    /// is not worth it, so the server's own direction is used - it is
    /// the only one anybody sends. Height is the same 0..4095 turn as
    /// the angle, so it is an elevation above the horizon.
    /// </summary>
    void Sun(Renderer r)
    {
        LightShading ls = _client?.Data?.LightShading;
        if (ls == null || ls.LightIntensity == 0) { r.SunLight = 0f; return; }

        float extra = Math.Clamp(1f + _bright, 1f, 1.8f);
        r.SunLight = extra * 3f * (ls.LightIntensity / 255f);

        float a = ls.SpherePosition.Angle * (2f * MathF.PI / GeometryConstants.MAXANGLE);
        float h = ls.SpherePosition.Height * (2f * MathF.PI / GeometryConstants.MAXANGLE);
        float ch = MathF.Cos(h);
        r.SunX = MathF.Cos(a) * ch;
        r.SunY = MathF.Sin(a) * ch;
        r.SunZ = MathF.Sin(h);
    }

    float Ambient()
    {
        RoomInfo room = _client?.Data?.RoomInformation;
        if (room == null) return 1f;

        // Before the server has put us in a room there is no light to
        // read and a black screen would look like a broken client, so
        // full brightness stands in. Once there IS a room, its own
        // number is the answer even when that number is zero: a room
        // the server says is pitch dark is pitch dark.
        if (room.RoomID == 0) return 1f;

        int lit = Math.Max(room.AmbientLight, room.AvatarLight);
        // The settings brightness is a factor on top, the way
        // AdjustAmbientLight applies Config->BrightnessFactor
        // (`Util.h:218-222`), clamped the same way - and NOT capped at
        // one afterwards, because the reference's scene ambient is not
        // either. Capping it is what made the slider do nothing in a
        // room that was already bright.
        float extra = Math.Clamp(1f + _bright, 1f, 1.8f);
        return lit / 255f * extra;
    }

    /// <summary>
    /// Moves one carried item to where another sits.
    ///
    /// `Inventory::OnItemDropped` takes the item out of the client's own
    /// inventory list and puts it back at the other one's position, and
    /// only then tells the server - so the bag settles under the finger
    /// rather than after a round trip. The message carries the two object
    /// ids, because each slot's window id is the object's id.
    /// </summary>
    void MoveInBag(InventoryObject from, InventoryObject to)
    {
        var bag = _client?.Data?.InventoryObjects;
        if (bag == null || from == null || to == null || ReferenceEquals(from, to)) return;

        int at = bag.IndexOf(from), onto = bag.IndexOf(to);
        if (at < 0 || onto < 0) return;

        bag.RemoveAt(at);
        bag.Insert(onto, from);

        _client.SendReqInventoryMoveMessage(from.ID, to.ID);
    }

    /// <summary>
    /// Whether something is covering the screen. Movement stops while one
    /// is up - a drag meant for a list should not also walk you - and the
    /// rows of buttons that live over the world get out of the way, since
    /// they were drawing straight through the middle of the panel.
    /// </summary>
    bool PanelUp =>
           (_bag != null && _bag.IsOpen)
        || (_lootList != null && _lootList.IsOpen)
        || (_contents != null && _contents.IsOpen)
        || (_shop != null && _shop.IsOpen)
        || (_sheet != null && _sheet.IsOpen)
        || (_look != null && _look.IsOpen)
        || (_book != null && _book.IsOpen)
        || (_amount != null && _amount.IsOpen)
        || (_players != null && _players.IsOpen)
        || (_quests != null && _quests.IsOpen)
        || (_trade != null && _trade.IsOpen)
        || (_npcQuests != null && _npcQuests.IsOpen)
        || (_mail != null && _mail.IsOpen)
        || (_news != null && _news.IsOpen)
        || (_options != null && _options.IsOpen)
        || (_aliases != null && _aliases.IsOpen)
        || (_guild != null && _guild.IsOpen)
        || (_shieldDesigner != null && _shieldDesigner.IsOpen)
        || (_hallBuy != null && _hallBuy.IsOpen)
        || (_guildCreate != null && _guildCreate.IsOpen)
        || (_wizard != null && _wizard.IsOpen)
        || (_acts != null && _acts.IsOpen)
        || (_ask != null && _ask.IsOpen);

    /// <summary>
    /// Puts one of yours into the container whose contents are open.
    ///
    /// `UIInventory.cpp` does this when you drag an inventory item onto
    /// the contents list: it looks the container up as a **room object**
    /// by `ObjectContents.ObjectID`, checks it really is a container,
    /// sends `ReqPut(item, container)` - the item with its count, the
    /// container with a count of zero - and then asks for the contents
    /// again, because the server does not push the new list.
    /// </summary>
    void PutInContainer(InventoryObject item)
    {
        if (item == null || _client?.Data == null) return;

        uint boxId = _client.Data.ObjectContents?.ObjectID?.ID ?? 0;
        RoomObject box = _client.Data.RoomObjects?.GetItemByID(boxId);
        if (box == null || box.Flags == null || !box.Flags.IsContainer) return;

        WorldAct(() =>
        {
            _client.SendReqPut(new ObjectID(item.ID, item.Count), new ObjectID(box.ID, 0));
            _client.SendSendObjectContents(box.ID);
        });
    }

    /// <summary>Rebuilds the renderer when the server moves us to a new room.</summary>
    /// <summary>
    /// Set by the arrival event and spent by the next SyncRoom.
    ///
    /// Comparing room OBJECTS is not enough to notice a room change:
    /// the library hands back the same RooFile for a room you have been
    /// in before and resets it in place (BaseClient.cs:628-635). So
    /// walking back through the door you just came out of looked to
    /// this like nothing had happened - the room's sounds were never
    /// stopped, and they piled up at every doorway. The reference
    /// unloads and reloads on every PlayerMessage without asking
    /// whether the room is the same (`ControllerRoom.cpp:1605-1612`).
    /// </summary>
    bool _arrived;

    void SyncRoom()
    {
        // Before the room, so a rebuild starts with the right sky: the
        // background can change without the room changing, and the room
        // can change without the background changing.
        _world.SetBackground(_client.Data?.RoomInformation?.BackgroundFile);
        bool fresh = _world.SyncRoom(_client.Data?.RoomInformation?.ResourceRoom);
        if (!fresh && !_arrived) return;
        _arrived = false;

        // The room's sounds are NOT stopped here any more. They are
        // stopped where the reference stops them, at the Player message
        // itself - see the Arrived handler in Begin. Doing it here meant
        // doing it after `_client.Update()` had already drained the whole
        // socket and fired Sound for every message in the batch, so the
        // new room's looping ambient started and was destroyed in the
        // same frame, every time the two landed in one batch.

        // The room the server sent has no .roo behind it: the resource
        // id resolved to a filename and nothing on disk matched, so
        // `RoomInfo.ResourceRoom` is null. WorldSync has already
        // unloaded, so there is no ghost of the last room left to walk
        // around in - but there is also nothing to play, and the player
        // has to be told, because `BaseClient.CurrentRoom` is that same
        // null and `TryMove` refuses every step against it
        // (BaseClient.cs:2830). Frozen and unwarned is the worst of the
        // two, and it is what this did before.
        //
        // The full-screen report, not the status line: it is the same
        // report a corrupt room gets (see PumpFailed), it can be
        // scrolled and selected on a phone that has no console, and the
        // .roo is named because the likeliest cause by far is a
        // half-installed resource folder - one missing file out of the
        // pack. In-page, as everything in this client is.
        if (_world.Room == null)
        {
            string want = _client.Data?.RoomInformation?.RoomFile;
            uint id = _client.Data?.RoomInformation?.RoomID ?? 0;
            _state = $"room {id} unavailable";
            _map?.Build(null);
            _mapRoom = null;
            GD.PrintErr($"[M59] room {id} ({want ?? "unnamed"}) has no .roo - " +
                        $"nothing loaded, movement will be refused");
            Boom("entering the room",
                 $"room: {id}\n" +
                 $"room file: {want ?? "not named by the server"}\n" +
                 $"resources: {_resDir ?? "unknown"}\n\n" +
                 $"The server moved you into this room and the client has no " +
                 $"room file for it, so there is nothing to draw and nothing " +
                 $"to walk in - the server will refuse every step.\n\n" +
                 $"The likeliest cause is an incomplete copy of the game " +
                 $"files: one room missing out of the resource folder above. " +
                 $"Clearing the app's data and letting it unpack again " +
                 $"replaces them.");
            return;
        }

        // The minimap is NOT rebuilt here if the new avatar has not
        // arrived yet. Player and RoomContents are two messages, and in
        // between them the library has AvatarObject null with
        // ResourceRoom already the new room (DataController.cs:2328-2368),
        // so a rebuild now would put the new room's walls under the
        // previous room's coordinates - the dial drawing one room centred
        // on another. Held instead until there is somebody to centre it
        // on, which is the same thing the view does across that gap; see
        // _camHeld in RenderFrame. Rebuilt even on a second visit:
        // RooFile.Reset puts every sidedef's flags back to FlagsOrig
        // (RooSideDef.cs:768-774), so which walls belong on the map can
        // have changed since the first time.
        _mapRoom = null;
        _state = $"in room {_client.Data.RoomInformation.RoomID}";
        GD.Print($"[M59] room -> {_world.Room.Filename} " +
                 $"({_world.Room.Walls.Count} walls)");
    }

    /// <summary>
    /// The room the minimap's walls were built from, so the rebuild can
    /// wait for the avatar. Null means "owed a rebuild".
    /// </summary>
    RooFile _mapRoom;

    /// <summary>Mirrors the server's object list into the renderer each frame.</summary>
    void SyncSprites()
    {
        // The flashing material runs off the clock, so the clock has to
        // reach it; everything else here is a constant.
        _world.Seconds = Time.GetTicksMsec() / 1000.0;

        // Where the eye is, told to the data layer. The reference writes
        // it on every camera move (`OgreListeners.cpp:113`) and the
        // library uses it in ProcessViewerAngle (DataController.cs:1717)
        // to work out which way round each object is being seen and how
        // far away it is. Nothing here was setting it, so it stayed at
        // the origin. Room objects escaped the consequences because the
        // compose cache overrides the viewer angle per frame, but the
        // avatar does not go through that path, so its own facing was
        // measured from the corner of the room - which is what the
        // portrait in the avatar panel is composed from.
        RoomObject self = _client.Data?.AvatarObject;
        if (self != null && _client.Data != null)
            _client.Data.ViewerPosition = self.Position3D;

        _world.SyncSprites(_client.Data?.RoomObjects, _client.Data?.AvatarObject,
                           _client.Data?.Projectiles);
    }

    void Resize()
    {
        Vector2 view = GetViewportRect().Size;
        if (view.X < 1 || view.Y < 1) return;
        _h = Math.Max(48, RenderHeight);
        _w = Math.Clamp((int)(_h * view.X / view.Y), 64, Math.Max(64, RenderWidthCap));
        _px = new uint[_w * _h];
        _rgba = new byte[_w * _h * 4];
        _image = Image.CreateEmpty(_w, _h, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>
    /// The last camera pose there was an avatar to take one from, held
    /// across the gap between a Player message and its RoomContents.
    /// See RenderFrame.
    /// </summary>
    float _camX, _camY, _camZ, _camAng;
    bool _camHeld;

    void RenderFrame()
    {
        if (_px == null || _image == null || _texture == null) return;

        // No room loaded - we were moved somewhere whose .roo is missing,
        // and WorldSync has unloaded. Leaving the buffer alone would hold
        // the last frame of the room we are no longer in on screen, which
        // is the ghost this client used to live in. An empty scene is
        // what UnloadRoom leaves behind (ControllerRoom.cpp:497-535), and
        // it is the honest picture.
        if (_world.Renderer == null)
        {
            Array.Clear(_rgba, 0, _rgba.Length);
            _image.SetData(_w, _h, false, Image.Format.Rgba8, _rgba);
            _texture.Update(_image);
            if (_status != null)
                _status.Text = Debugging ? $"{_state}\n" + string.Join("\n", _log)
                                         : string.Join("\n", _log);
            return;
        }

        RoomObject avatar = _client.Data?.AvatarObject;
        float cx, cy, cz, ang;
        if (avatar != null)
        {
            _world.Camera(avatar, out cx, out cy, out cz);
            ang = avatar.Angle;
            _camX = cx; _camY = cy; _camZ = cz; _camAng = ang; _camHeld = true;
        }
        else if (_camHeld)
        {
            // Between Player and RoomContents there is no avatar: the
            // library clears AvatarObject and the object list on Player
            // (DataController.cs:2328-2368) and only RoomContents puts
            // them back, while ResourceRoom - and so the renderer - is
            // already the new room. This used to render it from
            // `cx = cy = 0, ang = 0`, and (0,0) is outside every room in
            // the game, so a doorway threw the view out into the void for
            // as long as the second message took.
            //
            // The reference does not have a pose of its own to reset: the
            // camera node is a CHILD of the avatar's scene node and is
            // merely detached when that node dies, keeping its last
            // transform until the new avatar re-attaches it
            // (RemoteNode.cpp:36-45, :73-80). Holding the last pose here
            // is that, in the one place this renderer keeps a camera.
            cx = _camX; cy = _camY; cz = _camZ; ang = _camAng;
        }
        else
        {
            // Never had an avatar at all - nothing to hold. Only
            // reachable before the first RoomContents of the session,
            // where there is no room either and Render is not called.
            cx = cy = 0; cz = Renderer.EyeHeight; ang = 0;
        }

        _world.Renderer.Render(_px, _w, _h, cx, cy, cz, ang);

        // A bonk on the head turns the world inside out:
        // `Invert_ps` is one minus the picture with the alpha left
        // alone (compositors.hlsl:39-45). It costs one subtraction per
        // channel here, where the pixels already are, and it lands on
        // the view rather than over the buttons - which is where the
        // reference's compositor lands too.
        bool flip = _fx != null && _fx.Inverted;
        for (int i = 0; i < _px.Length; i++)
        {
            uint c = _px[i];
            byte r = (byte)(c >> 16), g = (byte)(c >> 8), b = (byte)c;
            if (flip) { r = (byte)(255 - r); g = (byte)(255 - g); b = (byte)(255 - b); }
            _rgba[i * 4] = r;
            _rgba[i * 4 + 1] = g;
            _rgba[i * 4 + 2] = b;
            _rgba[i * 4 + 3] = 255;
        }
        // Drink swims the edges of your vision: the reference turns on
        // COMPOSITOR_BLUR for the viewport while the effect lasts
        // (ControllerEffects.cpp:222-231), which is a ten-tap radial blur
        // over the finished picture (compositors.hlsl:48-91). This
        // renderer owns its pixels, so it is done to them, here, after
        // the invert above and before the buffer is handed to the
        // texture - which also lands it on the 3D view and not over the
        // buttons, where the reference's compositor lands. It costs
        // nothing when the player is sober. See ScreenEffects.Blur.
        _fx?.Blur(_rgba, _w, _h);

        _image.SetData(_w, _h, false, Image.Format.Rgba8, _rgba);
        _texture.Update(_image);

        // The room id, the buffer size, the frame rate, the object
        // count and a second copy of everything that is already in the
        // chat log, printed over the top-left corner of the world.
        //
        // All of it was useful while there was nothing else to look at
        // and none of it belongs in front of somebody playing. It is
        // kept behind M59DEBUG=1, with one exception: a line in _log is
        // a widget that failed to build, and that has to be visible
        // wherever it happens - a game missing its Bag button and
        // saying nothing about it is worse than an untidy corner.
        _status.Text = Debugging
            ? $"{_state}   {_w}x{_h}  {_fps}\n" +
              $"{_world.Renderer.Sprites.Count} objects\n" +
              string.Join("\n", _log)
            : string.Join("\n", _log);
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, GetViewportRect().Size), false);
        _touch.Draw(this);
    }

    public override void _ExitTree()
    {
        try { _client?.Disconnect(); } catch { }
    }
}
