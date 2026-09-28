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
    readonly List<string> _log = new List<string>();

    M59Client _client;
    Renderer _renderer;
    RooFile _room;
    TexCache _roomTextures;

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

        string dir = ResourceDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            string local = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
            dir = Path.Combine(local, "Meridian-104", "resource");
        }
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
        _client.Notice += s => { _log.Add(s); GD.Print("[M59] " + s); if (_log.Count > 6) _log.RemoveAt(0); };
        _client.EnteredGame += name => _state = $"playing as {name}";

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
        ApplyInput(delta);
        SyncSprites();

        _fpsAccum += delta; _frames++;
        if (_fpsAccum >= 0.5) { _fps = $"{_frames / _fpsAccum:F0} fps"; _fpsAccum = 0; _frames = 0; }

        RenderFrame();
        QueueRedraw();
    }

    public override void _Input(InputEvent e) => _touch.Handle(e, GetViewportRect().Size);

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
        if (avatar == null || _room == null) return;

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

        bool running = Run || Input.IsKeyPressed(Key.Shift);
        float kodSpeed = (float)(running ? MovementSpeed.Run : MovementSpeed.Walk);

        if (fwd == 0f && strafe == 0f)
        {
            // Speed 0 makes SendReqMoveMessage a no-op, which is what we want
            // while standing still.
            avatar.HorizontalSpeed = 0f;
            return;
        }
        avatar.HorizontalSpeed = kodSpeed;

        // kod units per second: speed * MOVEBASECOEFF is per millisecond.
        float step = kodSpeed * GeometryConstants.MOVEBASECOEFF * 1000f * (float)delta;

        float c = MathF.Cos(avatar.Angle), sn = MathF.Sin(avatar.Angle);
        V3 p = avatar.Position3D;
        float nkx = p.X + (c * fwd - sn * strafe) * step;
        float nky = p.Z + (sn * fwd + c * strafe) * step;

        // Collision runs in room units, on the same ROO the renderer draws.
        var from = new V2(M59Geo.KodToWorld(p.X), M59Geo.KodToWorld(p.Z));
        var to = new V2(M59Geo.KodToWorld(nkx), M59Geo.KodToWorld(nky));
        bool clear;
        try { clear = _room.CanMoveInRoom(ref from, ref to, 0f, 0f, out _); }
        catch { clear = _renderer != null && _renderer.SectorAtPoint(to.X, to.Y) != null; }
        if (!clear) return;

        // Follow the floor, the way the library does for moving objects.
        float h = p.Y;
        try
        {
            RooSubSector leaf;
            h = (float)_room.GetHeightAt(to.X, to.Y, out leaf, true, true) * 0.0625f;
        }
        catch { }

        avatar.Position3D = new V3(nkx, h, nky);
        _client.SendReqMoveMessage();
    }

    /// <summary>Rebuilds the renderer when the server moves us to a new room.</summary>
    void SyncRoom()
    {
        RooFile current = _client.Data?.RoomInformation?.ResourceRoom;
        if (current == null || ReferenceEquals(current, _room)) return;

        _room = current;
        _roomTextures = new TexCache(_client.ResourceManager);
        _renderer = new Renderer(_room, _roomTextures);
        _renderer.SpriteFrames.Clear();
        _state = $"in room {_client.Data.RoomInformation.RoomID}";
        GD.Print($"[M59] room -> {_room.Filename} ({_room.Walls.Count} walls)");
    }

    /// <summary>Mirrors the server's object list into the renderer each frame.</summary>
    void SyncSprites()
    {
        if (_renderer == null) return;
        _renderer.Sprites.Clear();

        var objects = _client.Data?.RoomObjects;
        if (objects == null) return;

        RoomObject avatar = _client.Data.AvatarObject;

        foreach (RoomObject o in objects)
        {
            if (o == null || o.Resource == null) continue;
            if (avatar != null && ReferenceEquals(o, avatar)) continue;   // don't draw ourselves

            // Hand over the BGF rather than one frame: the renderer picks
            // the frame per view from the object's facing, so creatures turn
            // as you walk around them.
            _renderer.Sprites.Add(new Renderer.Sprite
            {
                X = M59Geo.KodToWorld(o.Position3D.X),
                Y = M59Geo.KodToWorld(o.Position3D.Z),   // world Y is Position3D.Z
                BaseZ = M59Geo.KodHeightToXY(o.Position3D.Y),
                Height = 640f,
                Bgf = o.Resource,
                AngleUnits = o.AngleUnits,
                Group = o.Animation != null && o.Animation.CurrentGroup > 0 ? o.Animation.CurrentGroup : 1
            });
        }
    }

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
        if (_renderer == null || _px == null) return;

        RoomObject avatar = _client.Data?.AvatarObject;
        float cx, cy, cz, ang;
        if (avatar != null)
        {
            cx = M59Geo.KodToWorld(avatar.Position3D.X);
            cy = M59Geo.KodToWorld(avatar.Position3D.Z);
            cz = M59Geo.KodHeightToXY(avatar.Position3D.Y) + Renderer.EyeHeight;
            ang = avatar.Angle;
        }
        else
        {
            // Not in the world yet - sit still rather than render nothing.
            cx = cy = 0; cz = Renderer.EyeHeight; ang = 0;
        }

        _renderer.Render(_px, _w, _h, cx, cy, cz, ang);

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
                       $"{_renderer.Sprites.Count} objects\n" +
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
