using System;
using System.IO;
using Godot;
using Meridian59.Files.ROO;

/// <summary>
/// First milestone for the mobile client: load a ROO through the ported
/// core library and draw its walls. Deliberately minimal - the output
/// should match the PNG that Tools/Meridian59.Net8Room produces for the
/// same room, which was itself checked against the wiki map. If it does
/// not match, the bug is here and not in the library.
///
/// Set ResourceDir in the inspector (or edit the default below) to the
/// installed client's asset folder, e.g.
///   C:\Users\<you>\AppData\Local\Meridian-104\resource
/// </summary>
public partial class RoomView : Node2D
{
    [Export] public string ResourceDir = "";
    [Export] public string RoomFile = "dvalley1.roo";

    static readonly Color WallSolid = new Color("1a1a1a");
    static readonly Color WallPass  = new Color("4a90d9");
    static readonly Color Background = new Color("f5f2ea");

    RooFile _roo;
    Label _status;

    // world -> screen
    float _scale = 1f;
    Vector2 _offset = Vector2.Zero;
    Vector2 _pan = Vector2.Zero;
    float _zoom = 1f;

    int _minX, _minY, _maxX, _maxY;
    bool _dragging;

    public override void _Ready()
    {
        _status = new Label { Position = new Vector2(16, 16) };
        _status.AddThemeColorOverride("font_color", new Color("1a1a1a"));
        AddChild(_status);

        string dir = ResourceDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            // System.Environment, not Godot.Environment (the 3D world one)
            string local = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
            dir = Path.Combine(local, "Meridian-104", "resource");
        }

        string path = Path.Combine(dir, RoomFile);
        if (!File.Exists(path))
        {
            _status.Text = $"Room not found:\n{path}\n\nSet ResourceDir on the RoomView node.";
            GD.PrintErr($"[RoomView] missing {path}");
            return;
        }

        try
        {
            _roo = new RooFile(path);
        }
        catch (Exception e)
        {
            _status.Text = $"Failed to load {RoomFile}:\n{e.GetType().Name}: {e.Message}";
            GD.PrintErr($"[RoomView] {e}");
            return;
        }

        if (_roo.Walls.Count == 0)
        {
            _status.Text = $"{RoomFile} has no walls.";
            return;
        }

        _minX = int.MaxValue; _minY = int.MaxValue;
        _maxX = int.MinValue; _maxY = int.MinValue;
        foreach (RooWall w in _roo.Walls)
        {
            _minX = Math.Min(_minX, Math.Min(w.X1, w.X2));
            _maxX = Math.Max(_maxX, Math.Max(w.X1, w.X2));
            _minY = Math.Min(_minY, Math.Min(w.Y1, w.Y2));
            _maxY = Math.Max(_maxY, Math.Max(w.Y1, w.Y2));
        }

        _status.Text = $"{RoomFile}\n{_roo.Walls.Count} walls, {_roo.Sectors.Count} sectors\ndrag to pan, wheel or pinch to zoom";

        GetViewport().SizeChanged += Fit;
        Fit();
    }

    void Fit()
    {
        if (_roo == null) return;
        Vector2 view = GetViewportRect().Size;
        const float pad = 32f;
        float spanX = Math.Max(1, _maxX - _minX);
        float spanY = Math.Max(1, _maxY - _minY);
        _scale = Math.Min((view.X - 2 * pad) / spanX, (view.Y - 2 * pad) / spanY);
        _offset = new Vector2(
            (view.X - spanX * _scale) * 0.5f,
            (view.Y - spanY * _scale) * 0.5f);
        _pan = Vector2.Zero;
        _zoom = 1f;
        QueueRedraw();
    }

    Vector2 ToScreen(int x, int y)
    {
        // ROO x/y map straight onto screen x/y - no flip. Verified by
        // comparing Net8Room's output for dvalley1 against the wiki map.
        return new Vector2(
            (x - _minX) * _scale * _zoom + _offset.X + _pan.X,
            (y - _minY) * _scale * _zoom + _offset.Y + _pan.Y);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), Background);
        if (_roo == null) return;

        foreach (RooWall w in _roo.Walls)
        {
            bool passable = w.RightSectorNum != 0 && w.LeftSectorNum != 0;
            DrawLine(ToScreen(w.X1, w.Y1), ToScreen(w.X2, w.Y2),
                     passable ? WallPass : WallSolid, 1.5f, true);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { _zoom *= 1.1f; QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { _zoom /= 1.1f; QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            _pan += mm.Relative; QueueRedraw();
        }
        else if (e is InputEventScreenDrag sd)
        {
            _pan += sd.Relative; QueueRedraw();
        }
        else if (e is InputEventMagnifyGesture mg)
        {
            _zoom *= mg.Factor; QueueRedraw();
        }
    }
}
