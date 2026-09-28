using System;
using System.IO;
using Godot;
using Meridian59.Common;
using Meridian59.Files;
using Meridian59.Files.ROO;

/// <summary>
/// First-person view of a Meridian 59 room.
///
/// Renders on the CPU with <see cref="Renderer"/> - the same code the
/// offline PNG tool uses, so what you see here has been checked frame for
/// frame against a reference image - then blits the result into an
/// ImageTexture stretched over the viewport.
///
/// Internal resolution is deliberately low (see RenderWidth): wall casting
/// is brute force over every wall in the room, which is fine for a few
/// hundred columns and far too slow for a full-res frame. The BSP tree is
/// already loaded and is the obvious fix when this needs to be fast.
///
/// Controls: W/S forward and back, A/D strafe, arrow keys or drag to turn,
/// Escape to release the mouse.
/// </summary>
public partial class FirstPersonView : Node2D
{
    [Export] public string ResourceDir = "";
    [Export] public string RoomFile = "barinn.roo";
    [Export] public int RenderWidth = 480;
    [Export] public float MoveSpeed = 2200f;     // world units per second
    [Export] public float TurnSpeed = 2.2f;      // radians per second
    /// <summary>
    /// Optional: a sprite BGF ("duskrat.bgf") to scatter around the room so
    /// there is something to look at before the server connection exists.
    /// Leave empty for an empty room.
    /// </summary>
    [Export] public string DemoSpriteBgf = "";
    /// <summary>Zero means size each one from its own art.</summary>
    [Export] public float DemoSpriteHeight = 0f;

    readonly M59Assets _assets = new M59Assets();
    readonly TouchControls _touch = new TouchControls();
    RoomPicker _picker;
    MiniMap _map;
    /// <summary>See the note in GameView: Controls need a CanvasLayer.</summary>
    CanvasLayer _ui;
    ResourcePrompt _prompt;
    Renderer _renderer;
    RooFile _roo;

    Image _image;
    ImageTexture _texture;
    uint[] _px;
    byte[] _rgba;
    int _w, _h;

    float _camX, _camY, _camZ, _angle;
    float _clock;
    float _pitch;
    Label _status;
    double _fpsAccum;
    int _frames;
    string _fps = "";

    public override void _Ready()
    {
        _ui = new CanvasLayer();
        AddChild(_ui);

        _status = new Label { Position = new Vector2(12, 8) };
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

    /// <summary>Status text from a worker thread.</summary>
    void SetStatus(string text) { if (_status != null) _status.Text = text; }

    /// <summary>Finds the resource folder, or asks. Main thread only.</summary>
    void FindResources()
    {
        _status.Text = "";
        string dir = M59Paths.Resolve(ResourceDir);

        if (dir == null)
        {
            // Asking beats stopping. The places we look are guesses -
            // installs move, and people keep them on other drives.
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

        try { _map = new MiniMap(); _ui.AddChild(_map); }
        catch (Exception e) { GD.PrintErr($"[FirstPersonView] map unavailable: {e.Message}"); }

        // The room list is a convenience; if it throws you should still get
        // the room the scene names, not a black screen.
        try
        {
            _picker = new RoomPicker();
            _picker.Chosen += f => LoadRoom(f);
            _ui.AddChild(_picker);
            _picker.Load(dir);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FirstPersonView] room list unavailable: {e.GetType().Name}: {e.Message}");
        }

        if (!LoadRoom(RoomFile)) return;

        Resize();
        GetViewport().SizeChanged += Resize;
        SetProcess(true);
    }

    /// <summary>
    /// Swaps in a room by file name. Everything derived from the old one -
    /// renderer, textures, camera, demo sprites - is rebuilt, so this is
    /// also what the room picker calls.
    /// </summary>
    public bool LoadRoom(string file)
    {
        string path = Path.Combine(_assets.ResourceDir, file);
        if (!File.Exists(path)) { Fail($"Room not found:\n{path}"); return false; }

        try
        {
            var roo = new RooFile(path);
            roo.ResolveResources(_assets.Resources);
            _roo = roo;
        }
        catch (Exception e) { Fail($"{file}: {e.GetType().Name}: {e.Message}"); return false; }

        RoomFile = file;
        _renderer = new Renderer(_roo, new TexCache(_assets.Resources));
        _map?.Build(_roo);
        PlaceCameraInLargestLeaf();
        AddDemoSprites();
        SetProcess(true);
        if (_status != null) _status.Text = "";
        return true;
    }

    void Fail(string msg)
    {
        if (_status != null) _status.Text = msg;
        GD.PrintErr("[FirstPersonView] " + msg);
        SetProcess(false);
    }

    void PlaceCameraInLargestLeaf()
    {
        RooSubSector best = null;
        double bestArea = -1;
        foreach (RooSubSector l in _roo.BSPTreeLeaves)
        {
            if (l.Vertices == null || l.Vertices.Count < 3) continue;
            double s = 0;
            for (int i = 0, j = l.Vertices.Count - 1; i < l.Vertices.Count; j = i++)
                s += (double)l.Vertices[j].X * l.Vertices[i].Y - (double)l.Vertices[i].X * l.Vertices[j].Y;
            s = Math.Abs(s * 0.5);
            if (s > bestArea) { bestArea = s; best = l; }
        }
        if (best == null) return;

        float sx = 0, sy = 0;
        foreach (var v in best.Vertices) { sx += v.X; sy += v.Y; }
        _camX = sx / best.Vertices.Count;
        _camY = sy / best.Vertices.Count;
        _camZ = M59Geo.FloorXY(_renderer.SectorAtPoint(_camX, _camY)) + Renderer.EyeHeight;
        _angle = 0f;
    }

    /// <summary>
    /// Scatters a few billboards on the floor so the room is not empty.
    /// Real objects will come from the server; this is scaffolding.
    /// </summary>
    void AddDemoSprites()
    {
        if (string.IsNullOrWhiteSpace(DemoSpriteBgf)) return;
        var bgf = _assets.Resources.GetObject(DemoSpriteBgf);
        if (bgf == null) { GD.PrintErr($"[FirstPersonView] could not load {DemoSpriteBgf}"); return; }

        var rng = new Random(1);
        int placed = 0, attempts = 0;
        while (placed < 8 && attempts++ < 400)
        {
            float ang = (float)(rng.NextDouble() * Math.PI * 2);
            float dist = 800f + (float)rng.NextDouble() * 5000f;
            float x = _camX + MathF.Cos(ang) * dist;
            float y = _camY + MathF.Sin(ang) * dist;
            RooSector sec = _renderer.SectorAtPoint(x, y);
            if (sec == null) continue;
            _renderer.Sprites.Add(new Renderer.Sprite {
                X = x, Y = y, BaseZ = M59Geo.FloorXY(sec),
                Height = DemoSpriteHeight, Bgf = bgf,
                AngleUnits = (ushort)rng.Next(0, 4096), Group = 1 });
            placed++;
        }
        GD.Print($"[FirstPersonView] placed {placed} demo sprites");
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
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_renderer == null) return;

        float turn = 0f, fwd = 0f, strafe = 0f;
        if (Input.IsKeyPressed(Key.Left)) turn -= 1f;
        if (Input.IsKeyPressed(Key.Right)) turn += 1f;
        if (Input.IsKeyPressed(Key.W)) fwd += 1f;
        if (Input.IsKeyPressed(Key.S)) fwd -= 1f;
        if (Input.IsKeyPressed(Key.A)) strafe -= 1f;
        if (Input.IsKeyPressed(Key.D)) strafe += 1f;

        // Touch: left-hand stick moves, right-hand drag turns.
        Vector2 stick = _touch.Move;
        strafe += stick.X;
        fwd -= stick.Y;                       // screen Y grows downward

        _angle += turn * TurnSpeed * (float)delta + _touch.TakeTurn();

        if (fwd != 0f || strafe != 0f)
        {
            float c = MathF.Cos(_angle), s = MathF.Sin(_angle);
            float nx = _camX + (c * fwd - s * strafe) * MoveSpeed * (float)delta;
            float ny = _camY + (s * fwd + c * strafe) * MoveSpeed * (float)delta;

            // The same movement the live view uses: the library's own
            // collision, which walks the BSP tree and accounts for step
            // heights, and a slide along the blocking wall so walking into
            // one at an angle does not stop you dead.
            // The third argument is where we are, not how tall we are -
            // see WorldSync.TryMove.
            if (WorldSync.TryMove(_roo, new V2(_camX, _camY), new V2(nx, ny),
                                  true, _camZ - Renderer.EyeHeight, out V2 landed))
            {
                _camX = landed.X; _camY = landed.Y;
                RooSector dest = _renderer.SectorAtPoint(_camX, _camY);
                if (dest != null) _camZ = M59Geo.FloorXY(dest) + Renderer.EyeHeight;
            }
        }

        // Scrolling water, lava and moving walls run off this.
        // Looking up and down, clamped: the horizon shear exaggerates the
        // further you push it.
        _pitch = Math.Clamp(_pitch + _touch.TakePitch(), -Renderer.MaxPitch, Renderer.MaxPitch);

        _clock += (float)delta;
        _renderer.Time = _clock;
        _renderer.Pitch = _pitch;
        // No server here, so this view drives the room's own animations -
        // the flicker of an animated wall texture, and any lift. The live
        // view gets this from BaseClient.Update, which ticks the room.
        try { _roo.Tick(_clock * 1000.0, delta * 1000.0); } catch { }
        _map?.SetPlayer(_camX, _camY, _angle);

        _fpsAccum += delta; _frames++;
        if (_fpsAccum >= 0.5)
        {
            _fps = $"{_frames / _fpsAccum:F0} fps";
            _fpsAccum = 0; _frames = 0;
        }

        Render();
        QueueRedraw();
    }

    void Render()
    {
        if (_px == null) return;
        int closed = _renderer.Render(_px, _w, _h, _camX, _camY, _camZ, _angle);

        for (int i = 0; i < _px.Length; i++)
        {
            uint c = _px[i];
            _rgba[i * 4]     = (byte)(c >> 16);
            _rgba[i * 4 + 1] = (byte)(c >> 8);
            _rgba[i * 4 + 2] = (byte)c;
            _rgba[i * 4 + 3] = 255;
        }
        _image.SetData(_w, _h, false, Image.Format.Rgba8, _rgba);
        _texture.Update(_image);

        _status.Text = $"{RoomFile}  {_w}x{_h}  {_fps}\n" +
                       $"({_camX:F0}, {_camY:F0})  {_angle * 180f / MathF.PI:F0}deg  {closed}/{_w} closed\n" +
                       "WASD move, arrows turn";
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, GetViewportRect().Size), false);
        _touch.Draw(this);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        _touch.Handle(e, GetViewportRect().Size);
    }
}
