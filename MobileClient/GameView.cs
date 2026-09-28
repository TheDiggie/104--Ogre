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
    NameTags _names;
    ActionButtons _hotbar;
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
        _ui = new CanvasLayer();
        AddChild(_ui);

        // Below the avatar block, which owns the corner.
        _status = new Label { Position = new Vector2(12, 100) };
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
                M59Paths.UnpackIfNeeded(msg => Callable.From(() => SetStatus(msg)).CallDeferred());
                Callable.From(FindResources).CallDeferred();
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

        string user = !string.IsNullOrWhiteSpace(Username)
            ? Username : System.Environment.GetEnvironmentVariable("M59USER");
        string pass = !string.IsNullOrWhiteSpace(Password)
            ? Password : System.Environment.GetEnvironmentVariable("M59PASS");

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Fail("No credentials. Set M59USER and M59PASS in the environment,\n" +
                 "or Username/Password on this node.");
            return;
        }

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
            if (_log.Count > 6) _log.RemoveAt(0);
            _chat?.Local(s);
        };
        _client.EnteredGame += name => _state = $"playing as {name}";

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

        Widget("map", () => { _map = new MiniMap(); _ui.AddChild(_map); });
        Widget("names", () => { _names = new NameTags(); _ui.AddChild(_names); });
        Widget("hotbar", () =>
        {
            _hotbar = new ActionButtons();
            ActionButtons.Seed(_client.Data);
            if (_chat != null) _hotbar.BottomReserve = _chat.BlockHeight + 56f;
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
            _bag.DropItem    += item => Act(() => _client.SendReqDropMessage(new ObjectID(item.ID)));
            _bag.LookItem    += item => Act(() => _client.SendReqLookMessage(item.ID));
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
            _loot.Pressed += () => Act(() => _client.LootAll());
            _ui.AddChild(_loot);

            // The list the game has: what is in the thing, with names in
            // the library's own colours, and a Get for one item as well as
            // the Get All this button does.
            _lootList = new LootPanel();
            _lootList.GetAll += () => Act(() => _client.LootAll());
            _lootList.GetItem += item => Act(() => _client.SendReqGetMessage(new ObjectID(item.ID)));
            _ui.AddChild(_lootList);

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
        _client.Config.ResourcesPath = dir;
        _client.Init();
        string strings = M59Client.FindStringDictionary(dir);
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
        GD.PrintErr("[GameView] " + msg);
    }

    public override void _Process(double delta)
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
        }

        _vitals?.Follow(_client.Data);
        _face?.Follow(_client.Data);
        _bag?.Sync(_client.Data?.InventoryObjects);
        _lootList?.Sync(_client.Data?.ObjectContents);
        // Seeded every frame rather than once: the client clears its
        // lists when the world changes under it - a room change or a
        // relogin - and a row that was filled at startup would empty and
        // stay empty.
        ActionButtons.Seed(_client.Data);
        _hotbar?.Sync(_client.Data);

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
        if ((_chat != null && (_chat.Capturing || _chat.ShowingHistory))
            || (_bag != null && _bag.IsOpen)
            || (_lootList != null && _lootList.IsOpen))
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
            _actions?.SetTarget(null);
            return;
        }

        _client.Data.TargetID = obj.ID;
        _actions?.SetTarget(string.IsNullOrWhiteSpace(obj.Name) ? "something" : obj.Name);
    }

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
