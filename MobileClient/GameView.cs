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
    [Export] public float TurnSpeed = 2.2f;      // radians per second, keyboard
    [Export] public bool Run = false;

    readonly M59Assets _assets = new M59Assets();
    readonly TouchControls _touch = new TouchControls();
    ChatOverlay _chat;
    ActionBar _actions;
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

    public override void _Ready()
    {
        _status = new Label { Position = new Vector2(12, 8) };
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _status.AddThemeConstantOverride("outline_size", 4);
        AddChild(_status);

        // On Android the game data has to be copied out of the .pck before
        // the library, which reads with System.IO, can see any of it.
        M59Paths.UnpackIfNeeded(s => GD.Print("[M59] " + s));
        string dir = M59Paths.Resolve(ResourceDir);
        if (dir == null) { Fail(M59Paths.NotFoundMessage()); return; }
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

        _client = new M59Client { PreferredCharacter = Character };
        _world = new WorldSync(_client.ResourceManager);
        _client.Notice += s =>
        {
            _log.Add(s); GD.Print("[M59] " + s);
            if (_log.Count > 6) _log.RemoveAt(0);
            _chat?.Local(s);
        };
        _client.EnteredGame += name => _state = $"playing as {name}";

        _chat = new ChatOverlay();
        _chat.Submitted += (type, text) =>
        {
            try { _client.SendSayToMessage(type, text); }
            catch (Exception ex) { _chat.Local($"could not send: {ex.Message}"); }
        };
        AddChild(_chat);

        _actions = new ActionBar();
        _actions.LookAt       += () => Act(() => _client.SendReqLookMessage());
        _actions.PickUp       += () => Act(() => _client.SendReqGetMessage());
        _actions.AttackTarget += () => Act(() => _client.SendReqAttackMessage());
        _actions.UseTarget    += () => Act(() => _client.SendReqUseMessage(_client.Data.TargetID));
        AddChild(_actions);

        _client.Init();
        _client.Config.ResourcesPath = dir;
        _client.Config.Connections.Add(new ConnectionInfo(
            "server", Host, (ushort)Port, "rsc0000.rsb", user, pass, Character, null));
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

        // Pumps the socket and applies every message that arrived.
        try { _client.Update(); }
        catch (Exception e) { Fail($"Update: {e.GetType().Name}: {e.Message}"); return; }

        SyncRoom();
        _chat?.Sync(_client.Data?.ChatMessages);
        ApplyInput(delta);
        SyncSprites();
        ApplyTap();

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
        if (_chat != null && _chat.Capturing) { avatar.HorizontalSpeed = 0f; return; }

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
        if (dAngle != 0f)
        {
            avatar.Angle += dAngle;
            _client.SendReqTurnMessage();
        }

        if (fwd == 0f && strafe == 0f)
        {
            // Speed 0 makes SendReqMoveMessage a no-op, which is what we
            // want while standing still.
            avatar.HorizontalSpeed = 0f;
            return;
        }

        bool running = Run || Input.IsKeyPressed(Key.Shift);
        float kodSpeed = (float)(running ? MovementSpeed.Run : MovementSpeed.Walk);
        avatar.HorizontalSpeed = kodSpeed;

        if (!_world.TryStep(avatar, fwd, strafe, kodSpeed, delta, out V3 next)) return;
        avatar.Position3D = next;
        _client.SendReqMoveMessage();
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
