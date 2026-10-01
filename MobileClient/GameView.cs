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
    [Export] public bool Run = false;

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
    MiniMap _map;
    SplashNotifier _splash;
    LostConnection _lost;
    bool _wasInGame;
    bool _bagWasOpen;
    /// <summary>The shop line the amount prompt was opened for, if any.</summary>
    TradeOfferObject _amountFor;
    /// <summary>The trade line the amount prompt was opened for, if any.</summary>
    ObjectBase _amountForTrade;
    uint _targetBeforeBag = uint.MaxValue;
    RoomBuffsPanel _roomBuffs;
    Button _loot, _go, _auto, _next;
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
    float _bright = 0f;
    GuildPanel _guild;
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
    NameTags _names;
    QuestMarkers _questMarks;
    ScreenEffects _fx;
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
    readonly List<string> _log = new List<string>();

    M59Client _client;
    WorldSync _world;

    Image _image;
    ImageTexture _texture;
    uint[] _px;
    byte[] _rgba;
    int _w, _h;

    Label _status;
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

        // Everything on this layer is kept off the glass's edge - the
        // system's own cutouts, plus a margin for the curve, which
        // nothing reports. The world underneath keeps the whole
        // screen. See SafeArea.
        SafeArea.Apply(_ui, GetViewportRect().Size);
        GetViewport().SizeChanged += () => SafeArea.Apply(_ui, GetViewportRect().Size);

        // Below the avatar block, which owns the corner.
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
            _status.Text = "Unpacking game data...";
            System.Threading.Tasks.Task.Run(() =>
            {
                // Callable.From rather than a method name: these are
                // private methods, so the engine has no name for them.
                try
                {
                    M59Paths.UnpackIfNeeded(msg => Callable.From(() => SetStatus(msg)).CallDeferred());
                    Callable.From(FindResources).CallDeferred();
                }
                catch (Exception e)
                {
                    // A task's exception is unobserved, so it would
                    // otherwise be a silent hang on the unpack screen.
                    Callable.From(() => Boom("unpacking", e)).CallDeferred();
                }
            });
            return;
        }

        FindResources();
    }

    /// <summary>Bottom right, left of the map toggle.</summary>
    void LayoutLoot()
    {
        Vector2 v = GetViewportRect().Size;
        if (_loot != null)
        {
            _loot.Size = new Vector2(76, 40);
            _loot.Position = new Vector2(v.X - 70f - 12f - 76f - 8f, v.Y - 40f - 12f);
        }

        // The far end of the same row, past Settings. Slot arithmetic
        // alone puts it underneath Settings, which is 96 wide rather
        // than 76 - the first attempt drew "Go" straight through the
        // word and the shot showed "SetGoings". So: the seven 76-wide
        // slots from Map to Guild, then Settings' own 96, then this.
        const float edge = 12f, map = 70f, gap = 8f, slot = 76f, settings = 96f;
        float past = edge + map + gap + (slot + gap) * 7f + settings + gap;
        if (_go != null)
        {
            _go.Size = new Vector2(slot, 40);
            _go.Position = new Vector2(v.X - (past + slot), v.Y - 40f - 12f);
        }

        if (_auto != null)
        {
            _auto.Size = new Vector2(slot, 40);
            _auto.Position = new Vector2(v.X - (past + (slot + gap) + slot), v.Y - 40f - 12f);
        }

        if (_next != null)
        {
            _next.Size = new Vector2(slot, 40);
            _next.Position = new Vector2(
                v.X - (past + (slot + gap) * 3f + slot), v.Y - 40f - 12f);
        }
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
        _login = new LoginPrompt();
        _login.Server(Host, Port);
        _login.Submitted += (u, p) => Begin(u, p);
        _ui.AddChild(_login);
    }

    /// <summary>Builds the client and connects, once we know who we are.</summary>
    void Begin(string user, string pass)
    {
        _client = new M59Client
        {
            PreferredCharacter = Character,
            VersionMajor = (byte)Math.Clamp(VersionMajor, 0, 255),
            VersionMinor = (byte)Math.Clamp(VersionMinor, 0, 255),
        };
        _world = new WorldSync(_client.ResourceManager);
        _client.Notice += s =>
        {
            _log.Add(s); GD.Print("[M59] " + s);
            // Anything that means "you are not getting in" belongs on the
            // login screen, not only in a log nobody can see yet.
            if (_login != null &&
                (s.StartsWith("Connection error") || s.Contains("ailed") || s.Contains("efused")))
                _login.Trouble(s);
            if (_log.Count > 6) _log.RemoveAt(0);
            _chat?.Local(s);
        };
        // A connection that has gone away, said plainly. The socket
        // going down produced one line of .NET exception text in the
        // log and nothing else: the last frame of the world stayed up
        // with every button still looking live. On a phone that is not
        // an error case, it is Tuesday - a lift, a tunnel, the app put
        // in the background - so it needs a sentence and a way back in.
        _client.ConnectionLost += why =>
        {
            if (!_wasInGame) return;
            // Disconnect first. Nothing else marks the connection
            // offline after a broken pipe, and Connect refuses to run
            // while it thinks it is still up - so without this the
            // Reconnect button would do nothing at all.
            try { _client.Disconnect(); } catch { }
            _lost?.Show(why);
        };
        _client.EnteredGame += name =>
        {
            _state = $"playing as {name}";
            _wasInGame = true;
            // In the world - the login screen has done its job.
            if (_login != null) { _login.QueueFree(); _login = null; }
        };

        // Each overlay is built on its own. None of this has run on a
        // device yet, and a widget that throws while being set up should
        // cost you that widget, not the view - a game you can walk around
        // in with no chat box beats a black screen and a stack trace.
        Widget("chat", () =>
        {
            // Clear of the minimap's own button in the bottom corner.
            _chat = new ChatOverlay { RightReserve = 70f + 12f + 8f };
            _chat.Submitted += text =>
            {
                try
                {
                    // The game's own command parser, which knows tell,
                    // cast, perform, rest, guild, invite, group, deposit,
                    // appeal, time and twenty more, and keeps the command
                    // history. Everything goes through it, because it is
                    // what adds the line to that history - sending a say
                    // directly kept ordinary talk out of it entirely.
                    _client.ExecChatCommand(text);

                    // And then, if it was not a command, say it.
                    //
                    // This is a deliberate departure. The parser only
                    // makes a say out of text that begins with the word
                    // "say" (ChatCommand.cs:103), and the game's own chat
                    // bar does no more than this call (`UIChat.cpp:279`),
                    // so on a desktop typing "hello" and pressing enter
                    // does nothing at all. My earlier comment here
                    // claimed plain text fell through to a say. It does
                    // not, and nothing was being sent.
                    //
                    // Keeping that would be faithful and wrong. This bar
                    // is opened by pressing a button labelled Say, so the
                    // intent is already stated, and typing three more
                    // letters before every sentence costs a great deal
                    // more on a soft keyboard than on a real one. A
                    // command still runs as a command: the say only
                    // happens when the parser found none.
                    // Only when the first word is not a command at all.
                    //
                    // "Parse found nothing" is not the same question. A
                    // command with its arguments missing - "tell Alice"
                    // with no message, a half-typed guild command -
                    // parses to nothing as well, and the game's answer
                    // is to do nothing. Saying it instead would put
                    // "tell Alice" in front of the whole room, and the
                    // next attempt would put the message there too. A
                    // failed private word must not become a public one.
                    string first = text.TrimStart().Split(' ')[0].ToLowerInvariant();
                    if (!ChatWords.Contains(first) &&
                        Meridian59.Data.Models.ChatCommand.Parse(text, _client.Data, _client.Config) == null)
                        _client.SendSayToMessage(
                            Meridian59.Common.Enums.ChatTransmissionType.Normal, text);
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
            _actions.LookAt         += () => Act(() => _client.SendReqLookMessage());
            _actions.AttackTarget   += () => Act(() => _client.SendReqAttackMessage());
            _actions.ActivateTarget += () => Act(() => _client.ExecAction(AvatarAction.Activate));
            _actions.BuyFrom        += () => Act(() => _client.SendReqBuyMessage());
            _actions.TradeWith      += () => Act(() => _client.ExecAction(AvatarAction.Trade));
            _actions.LootTarget     += () => Act(() => _client.SendReqGetMessage());
            _actions.AskQuests      += () => Act(() => _client.SendReqNPCQuestsMessage());
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
                _amountForTrade = o;
                _amount.Ask(o.ID, (int)o.Count, o.Name);
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
            _quests = new QuestsPanel { ButtonRight = 12f + (70f + 8f) + (76f + 8f) * 4f };
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
            _mail = new MailPanel { ButtonRight = 12f + (70f + 8f) + (76f + 8f) * 5f };
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
        Widget("ask", () => { _ask = new ConfirmPopup(); _ui.AddChild(_ask); });
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
        Widget("options", () =>
        {
            // Left of the mail button.
            _options = new OptionsPanel { ButtonRight = 12f + (70f + 8f) + (76f + 8f) * 7f };
            _options.SoundVolume += v => { if (_sound != null) _sound.Volume = v; };
            _options.MusicVolume += v => { if (_sound != null) _sound.MusicLevel = v; };
            _options.LoopSounds  += on => { if (_sound != null) _sound.Loops = on; };
            // A factor on the room's own ambient light, not on the
            // finished picture - AdjustAmbientLight, not a gamma ramp.
            _options.Brightness  += v => _bright = v;
            _options.LookSpeed   += v => _touch.LookSensitivity = 0.006f * v;
            _options.InvertLook  += on => _touch.InvertLook = on;
            _options.Preferences += () => Act(() => _client.SendUserCommandSendPreferences());
            _ui.AddChild(_options);
        });
        Widget("guild", () =>
        {
            // No button opens this: UserCommandGuildInfo raises it.
            _guild = new GuildPanel { ButtonRight = 12f + (70f + 8f) + (76f + 8f) * 6f };
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
            _guild.Exile += (id, who) => _ask?.Choice(
                $"Are you sure you want to exile {who}?", id,
                confirmed => Act(() =>
                {
                    _client.SendUserCommandGuildExile(confirmed);
                    Reask();
                }));
            _guild.SetRank += (id, rank) => Act(() => _client.SendUserCommandGuildSetRank(id, rank));
            _guild.Abdicate += (id, who) => _ask?.Choice(
                $"Are you sure you want to abdicate to {who}?", id,
                confirmed => Act(() =>
                {
                    _client.SendUserCommandGuildAbdicate(confirmed);
                    Reask();
                }));
            _guild.Password += pw => Act(() => _client.SendUserCommandGuildSetPassword(pw));
            _guild.AbandonHall += () => _ask?.Choice(
                "Are you sure you want to abandon your hall?", 0,
                _ => Act(() => _client.SendUserCommandGuildAbandonHall()));
            _guild.Renounce += disband => _ask?.Choice(
                disband ? "Are you sure you want to disband your guild?"
                        : "Are you sure you want to leave your guild?", 0,
                _ => Act(() =>
                {
                    if (disband) _client.SendUserCommandGuildDisband();
                    else _client.SendUserCommandGuildRenounce();
                    _client.Data?.GuildInfo?.Clear(true);
                    _client.Data?.GuildShieldInfo?.Clear(true);
                }));
            // None of the guild commands is echoed, so the file clears
            // and re-asks after each one rather than guessing.
            _guild.Reload += () => Act(Reask);
            _ui.AddChild(_guild);
        });
        Widget("players", () =>
        {
            // Left of the character sheet button.
            _players = new PlayersPanel { ButtonRight = 12f + (70f + 8f) + (76f + 8f) * 3f };
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
            _players.Tell += who => { _players.Close(); _chat?.Compose($"tell {who} "); };
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

                TradeOfferObject line = _amountFor;
                _amountFor = null;
                if (line != null)
                {
                    // Buying part of a stack: the count goes back into
                    // the shop's own line, which is what the game does
                    // (`UIBuy.cpp:255`), so the row and the total follow
                    // it and the buy sends what you chose.
                    line.Count = (uint)Math.Max(1, many);
                    _shop?.Refresh();
                    return;
                }
                _client.SendReqDropMessage(new ObjectID(id, (uint)many));
            });
            _ui.AddChild(_amount);
        });
        Widget("sheet", () =>
        {
            // Right of the Book button, left of the Bag.
            _sheet = new AttributesPanel { ButtonRight = 12f + 70f + 8f + 76f + 8f + 76f + 8f };
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
        Widget("statusbar", () =>
        {
            _bar = new StatusBar();
            _bar.Mood   += a => Act(() => _client.SendActionMessage(a));
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
        Widget("effects", () => { _fx = new ScreenEffects(); _ui.AddChild(_fx); });
        Widget("look", () =>
        {
            _look = new LookPanel();
            // Writing on a book, a tombstone or a deed. The reference
            // sends the object's own id rather than the avatar's
            // (`UIObjectDetails.cpp:239-266`), which is what the
            // two-argument overload is for.
            _look.Inscribe += (id, text) => Act(() => _client.SendChangeDescription(id, text));
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
            _book = new SpellsPanel { RightReserve = 330f };
            _book.Opened += () => Act(() => { _client.SendSendSpellsMessage(); _client.SendSendSkillsMessage(); });
            _book.Cast += id => Act(() => _client.SendReqCastMessage(id));
            _book.Perform += id => Act(() => _client.SendReqPerformMessage(id));
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
                    _chat?.Local($"{(what as Meridian59.Data.Models.ObjectBase)?.Name} is on the hotbar. Hold the button to clear it.");
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
            _acts = new ActionsPanel { ButtonRight = 12f + 70f + 8f + (76f + 8f) * 7f + 96f + 8f + (76f + 8f) * 2f };
            _acts.Perform += a => Act(() => _client.ExecAction(a));
            _acts.Assign += a => Act(() =>
            {
                if (ActionButtons.Bind(_client.Data, a))
                    _chat?.Local($"{a} is on the hotbar. Hold the button to clear it.");
            });
            _ui.AddChild(_acts);
        });
        Widget("hotbar", () =>
        {
            _hotbar = new ActionButtons();
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
            _face = new AvatarPanel { Size = 72, Margin = 12f };
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
            _bag.UseItem     += item => Act(() => _client.UseUnuseApply(item));
            // One tap puts it on the trade table, rather than the
            // select-then-use a tap normally means.
            _bag.Selected    += item => Act(() =>
            {
                // The library resolves the id against the room and then
                // the inventory, so this targets the carried thing -
                // which is what the game does on a click
                // (`UIInventory.cpp`).
                //
                // Only when something was actually picked. Setting it to
                // nothing on a null selection threw away whatever you
                // had targeted in the world, and the panel clears its
                // selection when it opens - so opening the bag lost your
                // target. What you had before is put back when the bag
                // closes; see the restore in Pump.
                if (item != null) _client.Data.TargetID = item.ID;
            });
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
            _bag.DropItem    += item => Act(() =>
            {
                if (item.IsStackable && _amount != null)
                    _amount.Ask(item.ID, (int)item.Count, item.Name);
                else
                    _client.SendReqDropMessage(new ObjectID(item.ID));
            });
            _bag.LookItem    += item => Act(() => _client.SendReqLookMessage(item.ID));
            _bag.BindItem    += item => Act(() =>
            {
                if (ActionButtons.Bind(_client.Data, item))
                    _chat?.Local($"{item.Name} is on the hotbar. Hold the button to clear it.");
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
            _loot.Pressed += () => Act(() => _client.ExecAction(AvatarAction.Loot));
            _ui.AddChild(_loot);
            Panels.Opener(_loot);

            // Go, which is the game's Open key - space, by default
            // (`OISKeyBinding.cpp:52`), dispatched at
            // `ControllerInput.cpp:552` as SendReqGo(true).
            //
            // It is not AvatarAction.Activate and not the target row's
            // Open: those act on a thing you have tapped. ReqGo takes no
            // argument at all - it is "take me through", the doors and
            // the passages between rooms - and there is no AvatarAction
            // for it, so it cannot be a hotbar button and has to be its
            // own control.
            //
            // Without it this client could walk around one room and
            // never leave it. A phone has no space bar; the reference
            // never had to think about that.
            _go = new Button { Text = "Go" };
            // true, so the server is told where we are standing and
            // which way we face before it is asked to move us - the
            // reference passes the same, and SendReqGo forces both the
            // turn and the move out ahead of the request.
            _go.Pressed += () => Act(() => _client.SendReqGo(true));
            _ui.AddChild(_go);
            Panels.Opener(_go);

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
            Panels.Opener(_auto);

            // The game's NextTarget key (`ControllerInput.cpp:564`).
            // The library does the choosing - nearest guild enemy
            // first, then nearest attackable, skipping the ones
            // already visited (`DataController.NextTarget`) - and a
            // phone needs it more than a mouse does: a rat across a
            // dark room is a few pixels of tap target.
            _next = new Button { Text = "Next" };
            _next.Pressed += () => Act(() => _client.Data?.NextTarget());
            _ui.AddChild(_next);
            Panels.Opener(_next);

            // The list the game has: what is in the thing, with names in
            // the library's own colours, and a Get for one item as well as
            // the Get All this button does.
            _lootList = new LootPanel { Heading = "Loot", ShowGetAll = true };
            _lootList.GetAll += () => Act(() => _client.LootAll());
            // One request per thing ticked, each with its count, which
            // is what the game's own Get loop sends
            // (`UILootList.cpp:270-275`). The count had been left off
            // here, so taking a pile of coins took one coin.
            _lootList.Look += id => Act(() => _client.SendReqLookMessage(id));
            _lootList.GetItems += items => Act(() =>
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
            _contents.GetItems += items => Act(() =>
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
                _amountFor = line;
                _amount.Ask(line.ID, (int)line.Count, line.Name);
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

            LayoutLoot();
            GetViewport().SizeChanged += LayoutLoot;
        });

        Widget("character picker", () =>
        {
            _picker = new CharacterPicker();
            _picker.Chosen += c => _client.UseCharacter(c);
            // Asking for the palette is a separate round trip: the
            // server sends back every face part, colour, spell and
            // skill on offer, and the data layer builds the example
            // model out of it before the wizard has anything to show.
            _picker.NewWanted += () => Act(() => _client.SendSystemMessageSendCharInfo());
            _ui.AddChild(_picker);
            _client.ChooseCharacter += chars => _picker.Offer(chars);

            _newChar = new CreateCharacter();
            _newChar.Create += (name, description) => Act(() =>
            {
                _client.Data.CharCreationInfo.AvatarName = name;
                _client.Data.CharCreationInfo.AvatarDescription = description;
                _client.SendSystemMessageNewCharInfo();
            });
            _newChar.Complain += text => _ask?.Tell(text);
            // The wizard has no resource manager of its own, and the
            // example model arrives as resource ids rather than files.
            _newChar.Resolve = o => o.ResolveResources(_client.ResourceManager, false);
            // Backing out puts the list back rather than leaving the
            // screen empty.
            _newChar.Cancelled += () => _client.SendSendCharactersMessage();
            _ui.AddChild(_newChar);
            _client.CharacterPalette += info => _newChar.Open(info);
        });

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
        string strings = M59Client.FindStringDictionary(_resDir);
        GD.Print($"[M59] string file: {strings}");
        _client.Config.Connections.Add(new ConnectionInfo(
            "server", Host, (ushort)Port, strings, user, pass, Character, null));
        _client.Config.SelectedConnectionIndex = _client.Config.Connections.Count - 1;

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
        _log.Add(msg);
        if (_status != null) _status.Text = msg;
        // While the login screen is up it is the only thing on screen,
        // so a failure that only reached the status line was invisible.
        _login?.Trouble(msg);
        GD.PrintErr("[GameView] " + msg);
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
        catch (Exception e) { Fail($"Update: {e.GetType().Name}: {e.Message}"); return; }

        SyncRoom();
        _chat?.Sync(_client.Data?.ChatMessages);
        ApplyInput(delta);
        SyncSprites();
        ApplyTap();

        // Looking up and down, clamped: the horizon shear exaggerates the
        // further you push it.
        _pitch = Math.Clamp(_pitch + _touch.TakePitch(), -Renderer.MaxPitch, Renderer.MaxPitch);

        _clock += (float)delta;
        if (_world.Renderer != null)
        {
            _world.Renderer.Time = _clock;     // scrolling water and lava
            _world.Renderer.Pitch = _pitch;
            _world.Renderer.Brightness = Ambient();
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
        Wading();
        _guild?.Sync(_client.Data?.GuildInfo, _client.Data?.DiplomacyInfo,
                     _client.Data != null ? _client.Data.AvatarID : 0u);
        _wizard?.Sync(_client.Data?.StatChangeInfo);
        _newChar?.Sync();

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
        foreach (Control c in new Control[] { _map, _bar, _roomBuffs, _names, _questMarks, _face, _vitals, _chat })
            if (c != null) c.Visible = inWorld;
        if (_loot != null) _loot.Visible = inWorld;
        if (_go != null) _go.Visible = inWorld;
        if (_next != null) _next.Visible = inWorld;
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

        bool covered = PanelUp;
        if (_hotbar != null) _hotbar.Visible = inWorld && !covered;
        // The row of buttons that open the panels goes with it - and
        // also while the chat box is up, because the row shares a line
        // with the entry. Each button belongs to the panel it opens, so
        // no panel could hide the others; Panels keeps the list.
        Panels.ShowOpeners(inWorld && !covered && !(_chat != null && _chat.Capturing));
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

        RoomObject me = _client.Data?.AvatarObject;
        if (me != null && _map != null)
        {
            // The map works in the server's own units, as the game's does -
            // it is drawing the same numbers the objects arrive in.
            _map.SetObjects(_client.Data?.RoomObjects);
            _map.SetPlayer(me.Position3D.X, me.Position3D.Z, me.Angle);
        }

        _fpsAccum += delta; _frames++;
        if (_fpsAccum >= 0.5) { _fps = $"{_frames / _fpsAccum:F0} fps"; _fpsAccum = 0; _frames = 0; }

        RenderFrame();
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
    /// Setting HorizontalSpeed to zero is not enough on its own:
    /// `SendReqMoveMessage` returns early when the speed is zero
    /// (BaseClient.cs:1625), so the last stride - up to a whole 100ms
    /// throttle window of it - is never sent, and the server keeps you
    /// where you were when it last heard. That is the rubberband on
    /// every stop. The reference forces a send on the movement-key
    /// release (`ControllerInput.cpp:604`); this does it on the same
    /// edge, before the speed is cleared, because clearing it first
    /// would suppress the very message being forced.
    /// </summary>
    void Settle(RoomObject avatar)
    {
        if (_wasMoving)
        {
            try { _client.SendReqMoveMessage(true); }
            catch (Exception e) { _chat?.Local($"stop: {e.GetType().Name}: {e.Message}"); }
            _wasMoving = false;
        }
        avatar.HorizontalSpeed = 0f;
    }

    void ApplyInput(double delta)
    {
        RoomObject avatar = _client.Data?.AvatarObject;
        if (avatar == null || _world.Room == null) return;
        // Anything covering the screen or owning the keyboard stops
        // movement, so a drag meant for a list does not also walk you.
        if ((_chat != null && (_chat.Capturing || _chat.ShowingHistory)) || PanelUp)
        { Settle(avatar); return; }

        // While the server has you waiting - a save, a teleport - the
        // reference abandons input entirely (`ControllerInput.cpp:736`).
        // It has to: `SendReqMoveMessage` refuses to send anything while
        // `IsWaiting` (BaseClient.cs:1624), so walking on regardless
        // moves you locally, tells the server nothing, and snaps you
        // back the moment the wait ends.
        if (_client.Data != null && _client.Data.IsWaiting)
        { Settle(avatar); return; }

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

        // The reference halves the keyboard turn rate while you are
        // walking, so you can steer without spinning
        // (`ControllerInput.cpp:963-971`). Its own rate is
        // KEYROTATESPEED * KeyRotateSpeed * milliseconds
        // (ControllerInput.h:49, OgreClientConfig.h:61), which works out
        // at 3 radians a second.
        bool moving = fwd != 0f || strafe != 0f;
        float rate = TurnSpeed * (moving ? 0.5f : 1f);
        float dAngle = turn * rate * (float)delta + _touch.TakeTurn();
        if (dAngle != 0f) _client.TryYaw(dAngle);

        // A turn that has just ended has to be sent whether or not the
        // throttle is ready, or the last fraction of it is lost and you
        // swing, attack or cast at a facing the server does not have.
        // The reference forces one on every rotate-key release and when
        // mouse aiming stops (`ControllerInput.cpp:599`, :286).
        bool turning = dAngle != 0f;
        if (_wasTurning && !turning) _client.SendReqTurnMessage(true);
        _wasTurning = turning;

        if (!moving) { Settle(avatar); return; }
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

        bool running = Run || Input.IsKeyPressed(Key.Shift) || stick.Length() > 0.75f;
        try { _client.TryMove(dir, running, 0f); }
        catch (Exception e) { _chat?.Local($"move: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>
    /// Builds one overlay, reporting rather than propagating if it throws.
    /// A missing widget is written into the status text so it is visible
    /// on the device, where there is no console to read.
    /// </summary>
    void Widget(string name, Action build)
    {
        try { build(); }
        catch (Exception e)
        {
            string msg = $"{name} unavailable: {e.GetType().Name}: {e.Message}";
            _log.Add(msg);
            GD.PrintErr("[GameView] " + msg);
        }
    }

    /// <summary>
    /// Sends something, and says so rather than throwing if it fails.
    ///
    /// Nothing is sent while the server has you waiting. The reference
    /// swallows input at every level during a wait - key down
    /// (`ControllerInput.cpp:541`), key up (:568), mouse press (:333)
    /// and mouse release before CEGUI sees it at all (:268), so the
    /// Attack button and the spell list are dead too. Requests thrown
    /// into that window are ignored by the server and still spend the
    /// local attack and cast throttles, so the first thing you do after
    /// a teleport does nothing.
    /// </summary>
    /// <summary>
    /// Throws away what the client thinks about the guild and asks
    /// again. None of the guild commands is echoed, so the reference
    /// clears and re-requests after each one rather than guessing
    /// (`UIGuild.cpp:604-620`) - and it clears the shield model too,
    /// and asks for the guild list as well as the roster, which is
    /// what makes the diplomacy view come back current.
    /// </summary>
    void Reask()
    {
        _client.Data?.GuildInfo?.Clear(true);
        _client.Data?.GuildShieldInfo?.Clear(true);
        _client.SendUserCommandGuildInfoReq();
        _client.SendUserCommandGuildGuildListReq();
    }

    /// <summary>Takes a guild out of one of the declaration lists.</summary>
    static void Drop(Meridian59.Data.Lists.ObjectIDList<ObjectID> list, uint id)
    {
        if (list == null) return;
        ObjectID had = list.GetItemByID(id);
        if (had != null) list.Remove(had);
    }

    void Act(Action send)
    {
        if (_client?.Data != null && _client.Data.IsWaiting) return;
        try { send(); }
        catch (Exception e) { _chat?.Local($"{e.GetType().Name}: {e.Message}"); }

        // The latch spends itself on whatever was just sent, so you do
        // not heal yourself for the rest of the fight by accident. The
        // reference gets this free by holding a key down.
        if (_client?.Data != null && _client.Data.SelfTarget)
        {
            _client.Data.SelfTarget = false;
            _chat?.Local("Self-target off.");
        }
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
        WorldSync.Camera(avatar, out float cx, out float cy, out float cz);

        // Everything under the thumb, nearest first, so a second tap in
        // the same place reaches what is standing behind the first
        // thing. That is the library's own rule -
        // `DataController.ClickTarget` takes the first id you have not
        // already picked and starts over when the list runs out
        // (DataController.cs:1403-1441) - and the reference feeds it the
        // whole distance-sorted ray (`ControllerInput.cpp:153-215`).
        // Handing it only the nearest made the creature at the back
        // unselectable.
        var ids = new System.Collections.Generic.List<uint>();
        RoomObject obj = null;
        foreach (Renderer.Sprite sp in
                 _world.Renderer.PickAll(bx, by, _w, _h, cx, cy, cz, avatar.Angle))
        {
            if (!(sp.Tag is RoomObject ro) || ro.IsAvatar) continue;
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
        _client.Data.ClickTarget(ids);
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

        WorldSync.Camera(avatar, out float cx, out float cy, out float cz);

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

                screen = new Vector2(bx / (float)_w * view.X, by / (float)_h * view.Y);
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
        RooSubSector leaf = me?.SubSector;
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

    /// <summary>When the last splash was, so the next one waits its turn.</summary>
    double _splashedAt;

    void PlaySound(PlaySound info)
    {
        if (_sound == null || info == null) return;

        // Sounds can arrive before the avatar does - the server plays one
        // on entering a room - and a sound with nowhere to stand is still
        // a sound, so it plays flat rather than being dropped.
        RoomObject me = _client.Data?.AvatarObject;
        float lx = 0f, ly = 0f, facing = 0f;
        if (me != null)
        { lx = me.Position3D.X; ly = me.Position3D.Z; facing = me.Angle; }

        if (info.ID > 0)
        {
            RoomObject source = _client.Data?.RoomObjects?.GetItemByID(info.ID);
            if (source != null)
            {
                // Played at the object, by pretending the listener is
                // where they are relative to it - the player takes a
                // place and works out the rest.
                _sound.PlayAt(info, source.Position3D.X, source.Position3D.Z, lx, ly, facing);
                return;
            }
        }

        _sound.Play(info, _world.Room, lx, ly, facing);
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
        || (_guild != null && _guild.IsOpen)
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

        Act(() =>
        {
            _client.SendReqPut(new ObjectID(item.ID, item.Count), new ObjectID(box.ID, 0));
            _client.SendSendObjectContents(box.ID);
        });
    }

    /// <summary>Rebuilds the renderer when the server moves us to a new room.</summary>
    void SyncRoom()
    {
        if (!_world.SyncRoom(_client.Data?.RoomInformation?.ResourceRoom)) return;

        // The room's sounds belong to the room. The reference stops and
        // drops every one of them when a Player message arrives
        // (`ControllerSound.cpp:341-357`); without it a fountain from
        // three rooms back keeps running, and another joins it at every
        // doorway until the session is a swamp.
        _sound?.StopAll();
        _map?.Build(_world.Room);
        _state = $"in room {_client.Data.RoomInformation.RoomID}";
        GD.Print($"[M59] room -> {_world.Room.Filename} " +
                 $"({_world.Room.Walls.Count} walls, {_map?.MappedWalls ?? 0} on the map)");
    }

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

    void RenderFrame()
    {
        if (_world.Renderer == null || _px == null) return;

        RoomObject avatar = _client.Data?.AvatarObject;
        float cx, cy, cz, ang;
        if (avatar != null)
        {
            WorldSync.Camera(avatar, out cx, out cy, out cz);
            ang = avatar.Angle;
        }
        else
        {
            // Not in the world yet - sit still rather than render nothing.
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
        _image.SetData(_w, _h, false, Image.Format.Rgba8, _rgba);
        _texture.Update(_image);

        _status.Text = $"{_state}   {_w}x{_h}  {_fps}\n" +
                       $"{_world.Renderer.Sprites.Count} objects\n" +
                       string.Join("\n", _log);
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
