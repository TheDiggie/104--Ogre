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
    [Export] public int RenderWidth = 480;
    [Export] public bool AutoConnect = true;
    /// <summary>Client version reported at login; see M59Client.</summary>
    [Export] public int VersionMajor = 5;
    [Export] public int VersionMinor = 0;
    [Export] public float TurnSpeed = 2.2f;      // radians per second, keyboard
    [Export] public bool Run = false;

    readonly M59Assets _assets = new M59Assets();
    readonly TouchControls _touch = new TouchControls();
    ChatOverlay _chat;
    ActionBar _actions;
    CharacterPicker _picker;
    MiniMap _map;
    Button _loot;
    LootPanel _lootList;
    LootPanel _contents;
    BuyPanel _shop;
    AttributesPanel _sheet;
    AmountPrompt _amount;
    LoginPrompt _login;
    RichTextLabel _crash;
    string _resDir = "";
    NameTags _names;
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
    void Boom(string where, Exception e)
    {
        GD.PrintErr($"[GameView] {where}: {e}");
        if (_crash != null) return;

        _crash = new RichTextLabel
        {
            BbcodeEnabled = false,
            SelectionEnabled = true,
            ScrollFollowing = false,
            Text = $"{where} failed" + "\n\n" + e.ToString(),
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
        if (_loot == null) return;
        Vector2 v = GetViewportRect().Size;
        _loot.Size = new Vector2(76, 40);
        _loot.Position = new Vector2(v.X - 70f - 12f - 76f - 8f, v.Y - 40f - 12f);
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
        if (!_assets.Init(dir)) { Fail(_assets.Error); return; }
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
        _client.EnteredGame += name =>
        {
            _state = $"playing as {name}";
            // In the world - the login screen has done its job.
            if (_login != null) { _login.QueueFree(); _login = null; }
        };

        // Each overlay is built on its own. None of this has run on a
        // device yet, and a widget that throws while being set up should
        // cost you that widget, not the view - a game you can walk around
        // in with no chat box beats a black screen and a stack trace.
        Widget("chat", () =>
        {
            _chat = new ChatOverlay();
            _chat.Submitted += text =>
            {
                try
                {
                    // The game's own command parser, which knows tell,
                    // cast, perform, rest, guild, invite, group, deposit,
                    // appeal, time and twenty more, and keeps the command
                    // history. It is word-based - "tell bob hi" - and
                    // returns nothing for text that is not a command, so
                    // plain text falls through to a say. This used to be
                    // four prefixes I made up.
                    if (ChatCommand.Parse(text, _client.Data, _client.Config) != null)
                        _client.ExecChatCommand(text);
                    else
                        _client.SendSayToMessage(ChatTransmissionType.Normal, text);
                }
                catch (Exception ex) { _chat.Local($"could not send: {ex.Message}"); }
            };
            _ui.AddChild(_chat);
        });

        Widget("actions", () =>
        {
            _actions = new ActionBar();
            _actions.LookAt       += () => Act(() => _client.SendReqLookMessage());
            _actions.PickUp       += () => Act(() => _client.SendReqGetMessage());
            _actions.AttackTarget += () => Act(() => _client.SendReqAttackMessage());
            _actions.UseTarget    += () => Act(() => _client.SendReqUseMessage(_client.Data.TargetID));
            _ui.AddChild(_actions);
            if (_chat != null) _actions.BottomReserve = _chat.BlockHeight;
        });

        Widget("amount", () =>
        {
            _amount = new AmountPrompt();
            _amount.Chosen += (id, many) => Act(() =>
                _client.SendReqDropMessage(new ObjectID(id, (uint)many)));
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
        Widget("names", () => { _names = new NameTags(); _ui.AddChild(_names); });
        Widget("look", () => { _look = new LookPanel(); _ui.AddChild(_look); });
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
            _ui.AddChild(_book);
        });
        Widget("hotbar", () =>
        {
            _hotbar = new ActionButtons();
            ActionButtons.Seed(_client.Data);
            // Above the target row, which is itself above the chat block:
            // the row is one button tall plus the name label over it.
            if (_chat != null) _hotbar.BottomReserve = _chat.BlockHeight + 16f * 2.6f + 16f * 1.8f + 24f;
            _ui.AddChild(_hotbar);
        });
        Widget("face", () =>
        {
            // Under the status lines rather than behind them.
            _face = new AvatarPanel { Size = 72, Margin = 12f };
            _ui.AddChild(_face);
        });

        Widget("inventory", () =>
        {
            _bag = new InventoryPanel();
            _bag.Opened      += () => Act(() => _client.SendReqInventoryMessage());
            _bag.UseItem     += item => Act(() => _client.UseUnuseApply(item));
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

            // The list the game has: what is in the thing, with names in
            // the library's own colours, and a Get for one item as well as
            // the Get All this button does.
            _lootList = new LootPanel { Heading = "Loot", ShowGetAll = true };
            _lootList.GetAll += () => Act(() => _client.LootAll());
            _lootList.GetItem += item => Act(() => _client.SendReqGetMessage(new ObjectID(item.ID)));
            _ui.AddChild(_lootList);

            // The same window again for what is inside a container. The
            // game keeps these apart - UILootList and UIObjectContents -
            // because they follow different lists and only one of them
            // can take everything at once.
            _contents = new LootPanel { Heading = "Contents", ShowGetAll = false };
            _contents.GetItem += item => Act(() =>
                _client.SendReqGetMessage(new ObjectID(item.ID, item.Count)));
            _ui.AddChild(_contents);

            // The shop. One message buys everything ticked, which is what
            // Buy::OnOKClicked sends - not one per item.
            _shop = new BuyPanel();
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
            _ui.AddChild(_picker);
            _client.ChooseCharacter += chars => _picker.Offer(chars);
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

        // The button rows sit over the world, which is fine until a panel
        // covers the world.
        bool covered = PanelUp;
        if (_hotbar != null) _hotbar.Visible = !covered;
        if (_actions != null) _actions.Visible = !covered;
        // Seeded every frame rather than once: the client clears its
        // lists when the world changes under it - a room change or a
        // relogin - and a row that was filled at startup would empty and
        // stay empty.
        ActionButtons.Seed(_client.Data);
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
    void ApplyInput(double delta)
    {
        RoomObject avatar = _client.Data?.AvatarObject;
        if (avatar == null || _world.Room == null) return;
        // Anything covering the screen or owning the keyboard stops
        // movement, so a drag meant for a list does not also walk you.
        if ((_chat != null && (_chat.Capturing || _chat.ShowingHistory)) || PanelUp)
        { avatar.HorizontalSpeed = 0f; return; }

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

        float dAngle = turn * TurnSpeed * (float)delta + _touch.TakeTurn();
        if (dAngle != 0f) _client.TryYaw(dAngle);

        if (fwd == 0f && strafe == 0f)
        {
            avatar.HorizontalSpeed = 0f;
            return;
        }

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

    void Act(Action send)
    {
        try { send(); }
        catch (Exception e) { _chat?.Local($"{e.GetType().Name}: {e.Message}"); }
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

        Renderer.Sprite hit = _world.Renderer.Pick(bx, by, _w, _h, cx, cy, cz, avatar.Angle);
        var obj = hit?.Tag as RoomObject;

        if (obj == null)
        {
            _client.Data.TargetID = uint.MaxValue;      // tapped nothing: clear
            _actions?.SetTarget((RoomObject)null);
            return;
        }

        _client.Data.TargetID = obj.ID;
        _actions?.SetTarget(obj);
    }

    /// <summary>
    /// Plays a sound the server asked for. The object case is resolved
    /// here rather than in the sound player, because this is where the
    /// object list is: a sound with a source id comes from wherever that
    /// object is standing.
    /// </summary>
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

        int lit = Math.Max(room.AmbientLight, room.AvatarLight);
        if (lit <= 0) return 1f;
        return lit / 255f;
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
        || (_amount != null && _amount.IsOpen);

    /// <summary>Rebuilds the renderer when the server moves us to a new room.</summary>
    void SyncRoom()
    {
        if (!_world.SyncRoom(_client.Data?.RoomInformation?.ResourceRoom)) return;
        _map?.Build(_world.Room);
        _state = $"in room {_client.Data.RoomInformation.RoomID}";
        GD.Print($"[M59] room -> {_world.Room.Filename} ({_world.Room.Walls.Count} walls)");
    }

    /// <summary>Mirrors the server's object list into the renderer each frame.</summary>
    void SyncSprites()
        => _world.SyncSprites(_client.Data?.RoomObjects, _client.Data?.AvatarObject);

    void Resize()
    {
        Vector2 view = GetViewportRect().Size;
        if (view.X < 1 || view.Y < 1) return;
        _w = Math.Max(64, RenderWidth);
        _h = Math.Max(48, (int)(_w * view.Y / view.X));
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

        for (int i = 0; i < _px.Length; i++)
        {
            uint c = _px[i];
            _rgba[i * 4] = (byte)(c >> 16);
            _rgba[i * 4 + 1] = (byte)(c >> 8);
            _rgba[i * 4 + 2] = (byte)c;
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
