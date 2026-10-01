using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Common;
using Meridian59.Files.ROO;

/// <summary>
/// Top-down room view: real floor textures per BSP leaf, walls on top.
///
/// Meridian rooms are in FINENESS units (1024 per grid square) and one
/// texture tiles across one grid square, so UV = world / 1024. That was
/// derived from barinn.roo spanning exactly 16 x 16.5 squares, not from
/// documentation - if the floors look wrong-sized, TextureScale is the
/// dial to turn.
/// </summary>
public partial class RoomView : Node2D
{
    [Export] public string ResourceDir = "";
    [Export] public string RoomFile = "dvalley1.roo";
    /// <summary>World units per texture tile. FINENESS = 1024.</summary>
    [Export] public float TextureScale = 1024f;
    [Export] public bool ShowTextures = true;
    [Export] public bool ShowWalls = true;

    static readonly Color WallSolid  = new Color(0.10f, 0.10f, 0.10f);
    static readonly Color WallPass   = new Color(0.29f, 0.56f, 0.85f);
    // The reasons a wall blocks, per RooFile.CanMoveInRoomTree3DInternal
    // (RooFile.cs:1636-1672); WallSolid is the first (end outside / one-sided).
    static readonly Color WallNoFlag   = new Color(0.85f, 0.25f, 0.25f); // red: start side lacks WF_PASSABLE (:1643)
    static readonly Color WallNoMove   = new Color(0.90f, 0.60f, 0.15f); // orange: end sector is SF_NOMOVE (:1647)
    static readonly Color WallHeadroom = new Color(0.75f, 0.35f, 0.85f); // purple: end gap < OBJECTHEIGHTROO (:1655)
    static readonly Color Background = new Color(0.06f, 0.06f, 0.07f);
    static readonly Color NoTexture  = new Color(0.20f, 0.20f, 0.22f);

    readonly M59Assets _assets = new M59Assets();
    RooFile _roo;
    Label _status;

    float _fit = 1f;
    Vector2 _origin = Vector2.Zero;
    Vector2 _pan = Vector2.Zero;
    float _zoom = 1f;
    float _minX, _minY, _maxX, _maxY;
    bool _dragging;
    int _drawnLeaves, _texturedLeaves;

    public override void _Ready()
    {
        TextureRepeat = TextureRepeatEnum.Enabled;

        _status = new Label { Position = new Vector2(16, 16) };
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
        AddChild(_status);

        // On Android the game data has to be copied out of the .pck before
        // the library, which reads with System.IO, can see any of it.
        //
        // Offline/harness view, so no UnpackScreen (GameView.cs:472-530 has
        // one for a player who cannot relaunch with a fix): a refusal stops
        // here, loudly, with the report's Problem - which names the failing
        // file or folder (M59Paths.cs:410, 465, 481, 502) - in the
        // in-page label and on stderr. Carrying on would Resolve a
        // part-written folder and draw a half-installed room. Synchronous
        // on purpose: a developer tool has no watchdog to appease.
        M59Paths.UnpackReport report = M59Paths.UnpackIfNeeded(s => GD.Print("[M59] " + s));
        if (!report.Ok)
        {
            Fail(report.Problem ?? "The game data could not be installed.");
            return;
        }
        string dir = M59Paths.Resolve(ResourceDir);
        if (dir == null) { Fail(M59Paths.NotFoundMessage()); return; }

        if (!_assets.Init(dir))
        {
            Fail(_assets.Error + "\n\nSet Resource Dir on the RoomView node.");
            return;
        }

        string path = Path.Combine(dir, RoomFile);
        if (!File.Exists(path)) { Fail($"Room not found:\n{path}"); return; }

        try
        {
            _roo = new RooFile(path);
            _roo.ResolveResources(_assets.Resources);
        }
        catch (Exception e)
        {
            Fail($"Failed to load {RoomFile}:\n{e.GetType().Name}: {e.Message}");
            GD.PrintErr($"[RoomView] {e}");
            return;
        }

        if (_roo.Walls.Count == 0) { Fail($"{RoomFile} has no walls."); return; }

        _minX = float.MaxValue; _minY = float.MaxValue;
        _maxX = float.MinValue; _maxY = float.MinValue;
        foreach (RooWall w in _roo.Walls)
        {
            _minX = Math.Min(_minX, Math.Min(w.X1, w.X2));
            _maxX = Math.Max(_maxX, Math.Max(w.X1, w.X2));
            _minY = Math.Min(_minY, Math.Min(w.Y1, w.Y2));
            _maxY = Math.Max(_maxY, Math.Max(w.Y1, w.Y2));
        }

        GetViewport().SizeChanged += Fit;
        Fit();
    }

    void Fail(string message)
    {
        if (_status != null) _status.Text = message;
        GD.PrintErr("[RoomView] " + message);
    }

    void Fit()
    {
        if (_roo == null) return;
        Vector2 view = GetViewportRect().Size;
        const float pad = 24f;
        float spanX = Math.Max(1f, _maxX - _minX);
        float spanY = Math.Max(1f, _maxY - _minY);
        _fit = Math.Min((view.X - 2 * pad) / spanX, (view.Y - 2 * pad) / spanY);
        _origin = new Vector2((view.X - spanX * _fit) * 0.5f, (view.Y - spanY * _fit) * 0.5f);
        _pan = Vector2.Zero;
        _zoom = 1f;
        QueueRedraw();
    }

    Vector2 ToScreen(float x, float y)
    {
        return new Vector2(
            (x - _minX) * _fit * _zoom + _origin.X + _pan.X,
            (y - _minY) * _fit * _zoom + _origin.Y + _pan.Y);
    }

    /// <summary>
    /// Colours a wall by the library's own movement test,
    /// CanMoveInRoomTree3DInternal (RooFile.cs:1636-1672), tried in both
    /// directions across the wall (the caller picks start and end by
    /// which side of the wall each is on, RooFile.cs:1745-1765). Blue
    /// means crossable both ways; anything else is blocked, and the
    /// colour says WHICH of the reasons, in the library's order:
    /// dark = an end is outside the room (no sector or side, :1639);
    /// red = the start side lacks WF_PASSABLE (:1643);
    /// orange = the end sector is SF_NOMOVE (:1647);
    /// purple = the end sector's ceiling-to-floor gap is under
    /// OBJECTHEIGHTROO (:1655, measured at the wall's midpoint).
    /// </summary>
    static Color WallColour(RooWall w)
    {
        float mx = (w.X1 + w.X2) * 0.5f, my = (w.Y1 + w.Y2) * 0.5f;
        Color? a = Block(w.RightSector, w.LeftSector, w.RightSide, w.LeftSide, mx, my);
        Color? b = Block(w.LeftSector, w.RightSector, w.LeftSide, w.RightSide, mx, my);
        return a ?? b ?? WallPass;
    }

    static Color? Block(RooSector s, RooSector e, RooSideDef sideS, RooSideDef sideE, float x, float y)
    {
        if (e == null || sideE == null) return WallSolid;
        if (sideS != null && !sideS.Flags.IsPassable) return WallNoFlag;
        if (e.Flags.IsNoMove) return WallNoMove;
        if (e.CalculateCeilingHeight(x, y) - e.CalculateFloorHeight(x, y, true)
            < Meridian59.Common.Constants.GeometryConstants.OBJECTHEIGHTROO) return WallHeadroom;
        return null;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), Background);
        if (_roo == null) return;

        _drawnLeaves = 0;
        _texturedLeaves = 0;

        if (ShowTextures)
        {
            foreach (RooSubSector leaf in _roo.BSPTreeLeaves)
            {
                Polygon poly = leaf.Vertices;
                if (poly == null || poly.Count < 3) continue;

                var pts = new Vector2[poly.Count];
                var uvs = new Vector2[poly.Count];
                // leaf.Sector is populated by the loader; this resolves
                // from the 1-based SectorNum anyway so the lookup is bounds
                // checked and does not depend on that.
                RooSector sector = (leaf.SectorNum >= 1 && leaf.SectorNum <= _roo.Sectors.Count)
                    ? _roo.Sectors[leaf.SectorNum - 1] : null;
                float offX = sector != null ? sector.TextureX : 0f;
                float offY = sector != null ? sector.TextureY : 0f;

                for (int i = 0; i < poly.Count; i++)
                {
                    V2 v = poly[i];
                    pts[i] = ToScreen(v.X, v.Y);
                    // Axes swapped: Meridian's room textures run Y along
                    // world X. See Renderer.cs for how this was pinned down.
                    uvs[i] = new Vector2((v.Y + offY) / TextureScale,
                                         (v.X + offX) / TextureScale);
                }

                Texture2D tex = sector != null ? _assets.RoomTexture(sector.FloorTexture) : null;
                if (tex != null)
                {
                    DrawColoredPolygon(pts, Colors.White, uvs, tex);
                    _texturedLeaves++;
                }
                else
                {
                    DrawColoredPolygon(pts, NoTexture);
                }
                _drawnLeaves++;
            }
        }

        if (ShowWalls)
        {
            foreach (RooWall w in _roo.Walls)
            {
                DrawLine(ToScreen(w.X1, w.Y1), ToScreen(w.X2, w.Y2),
                         WallColour(w), 1.5f, true);
            }
        }

        _status.Text =
            $"{RoomFile}\n" +
            $"{_roo.Walls.Count} walls, {_roo.Sectors.Count} sectors, {_drawnLeaves} leaves\n" +
            $"{_texturedLeaves} textured, {_assets.CachedTextures} textures loaded\n" +
            $"drag to pan, wheel to zoom, T textures, W walls";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed)
        {
            if (k.Keycode == Key.T) { ShowTextures = !ShowTextures; QueueRedraw(); }
            else if (k.Keycode == Key.W) { ShowWalls = !ShowWalls; QueueRedraw(); }
            else if (k.Keycode == Key.F) Fit();
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { _zoom *= 1.1f; QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { _zoom /= 1.1f; QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
        }
        else if (e is InputEventMouseMotion mm && _dragging) { _pan += mm.Relative; QueueRedraw(); }
        else if (e is InputEventScreenDrag sd) { _pan += sd.Relative; QueueRedraw(); }
        else if (e is InputEventMagnifyGesture mg) { _zoom *= mg.Factor; QueueRedraw(); }
    }
}
