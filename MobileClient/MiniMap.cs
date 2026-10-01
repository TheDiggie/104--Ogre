using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Files.ROO;

/// <summary>
/// The corner map, drawn the way the game draws it.
///
/// The rules come from the library's own <c>Drawing2D/MiniMap</c> and the
/// Ogre client's <c>MiniMapCEGUI</c>, not from taste:
///
///  - it is a window onto the room centred on you, at a zoom, rather than
///    the whole room squeezed into a box. Half the width and height of the
///    view, times the zoom, is how far it reaches - so zooming out shows
///    more room at the same size.
///  - a wall with no sides at all, or whose only sides are flagged
///    <c>IsMapNever</c>, is not drawn. Map-never is how the game hides the
///    scenery it does not want you navigating by.
///  - objects are dots, ten pixels across, coloured by what they are to
///    you. The colours are `MiniMapCEGUI.cpp`'s non-vanilla branch,
///    which is the one that ships here, not the short vanilla list: a
///    ring for who someone is to you - guild, ally, enemy, a player at
///    all - and a filled dot for what a thing is, monsters and quest
///    mobs included. An object with none of those flags is not drawn.
///  - you are a triangle pointing where you face, not a dot.
///  - the whole thing is round, on the game's own dial: the CEGUI layout
///    puts "TaharezLook/MiniMapBackground" behind it, which is the wooden
///    rim and pale face in Resources/ui/imagesets, and draws the map over
///    it at nine tenths alpha. That picture is cut out of the repo's own
///    imageset into art/minimap-bg.png rather than invented.
///
/// Coordinates are the server's own - the same units the objects arrive in
/// - because that is what the library's minimap works in: a wall vertex
/// becomes <c>X * 0.0625 + 64</c>, which is exactly room units over 16,
/// offset by 64.
/// </summary>
public partial class MiniMap : Control
{
    // MapSize, not Size: Control already has one.
    [Export] public int MapSize = 220;
    [Export] public float Margin = 12f;

    /// <summary>
    /// How big the dial itself may get, and in what increments.
    ///
    /// The reference lets the player resize the minimap WINDOW as well
    /// as zoom it, and the two are separate gestures: the wheel alone
    /// changes zoom, the wheel with the self-target key held changes the
    /// window's size (`UIMiniMap.cpp:62-101`). Its band is 256 to 512
    /// pixels and it insists on 32-pixel steps - "MUST step in 32
    /// pixel-steps (tex requirement for DYN DISCARD)"
    /// (`UIMiniMap.cpp:67-77`) - because the map is redrawn into a
    /// dynamic-discard texture whose dimensions have to stay on that
    /// grid. The size is then written back into the client's own
    /// configuration with the rest of the window's layout
    /// (`ControllerUI.cpp:643-644`, `OgreClientConfig.cpp:781-784`) and
    /// read again on startup (`UIMiniMap.cpp:12-17`), so a player's
    /// chosen size survives the session.
    ///
    /// Two deliberate divergences, both forced by the screen rather than
    /// chosen:
    ///
    ///  - the floor is 160 rather than the reference's 256. The
    ///    reference is sizing a floating window inside a desktop
    ///    viewport that is at least 1024 wide; a phone held upright is
    ///    often under 400 points across, where a 256-point dial is most
    ///    of the width and cannot be made smaller - which is the one
    ///    thing a player on a small screen actually wants. The existing
    ///    default of 220 sits inside this band, so the shipped map does
    ///    not jump the first time the button is pressed.
    ///  - the ceiling is additionally capped to what fits the viewport
    ///    (see <see cref="FittingMax"/>). The reference's 512 is fine on
    ///    a monitor and would swallow a phone screen whole.
    ///
    /// The 32-pixel step is kept as the reference has it, and so is the
    /// clamping; what is dropped is the reference's habit of moving the
    /// window by half the size change so it grows about its centre
    /// (`UIMiniMap.cpp:90-95`). Here the dial is pinned to the top-right
    /// corner by <see cref="Layout"/> rather than dragged anywhere by the
    /// player, so there is no free position to compensate: it grows down
    /// and to the left, away from the corner it is anchored to.
    /// </summary>
    public const int MinMapSize = 160;
    public const int MaxMapSize = 512;
    public const int MapSizeStep = 32;

    /// <summary>
    /// Server units per pixel, and how much of the room you can see: the
    /// map covers MapSize * Zoom units across.
    ///
    /// Eight, not the library's DEFAULTZOOM of 4. The shipping client
    /// overrides that with 8 (`ControllerUI.h:559`), and it is looking
    /// at a window of 256 to 512 pixels rather than this one's 220 - so
    /// at 4 this map showed under half the area the game's does, which
    /// on a small map is the difference between seeing the room and
    /// seeing a corner of it.
    /// </summary>
    [Export] public float Zoom = 8f;

    public const float MinZoom = 1f;
    public const float MaxZoom = 32f;

    // The game's own colours, from MiniMapCEGUI, on the game's own dial.
    static readonly Color Wall   = new Color(0f, 0f, 0f);                 // COLOR_MAP_WALL
    static readonly Color Player      = new Color(0f, 0f, 1f);                       // 0,0,255
    static readonly Color Enemy       = new Color(1f, 0f, 0f);                       // 255,0,0
    static readonly Color Friend      = new Color(0f, 1f, 120f / 255f);              // 0,255,120
    static readonly Color GuildMate   = new Color(1f, 1f, 0f);                       // 255,255,0
    static readonly Color Minion      = new Color(0f, 200f / 255f, 0f);              // 0,200,0
    static readonly Color MinionOther = new Color(70f / 255f, 5f / 255f, 130f / 255f);
    static readonly Color BuildGroup  = new Color(0f, 1f, 0f);                       // 0,255,0
    static readonly Color Npc         = new Color(0f, 0f, 0f);                       // black
    static readonly Color TempSafe    = new Color(0f, 170f / 255f, 1f);              // 0,170,255
    static readonly Color MiniBoss    = new Color(160f / 255f, 66f / 255f, 194f / 255f);
    static readonly Color Boss        = new Color(127f / 255f, 0f, 0f);              // 127,0,0
    static readonly Color RareItem    = new Color(237f / 255f, 1f, 9f / 255f);
    static readonly Color NoPvP       = new Color(1f, 1f, 1f);
    static readonly Color AggroSelf   = new Color(0f, 0f, 0f);
    static readonly Color AggroOther  = new Color(1f, 1f, 1f);
    static readonly Color Mercenary   = new Color(1f, 169f / 255f, 27f / 255f);
    static readonly Color MobQuest    = new Color(179f / 255f, 0f, 179f / 255f);
    // The 0.9 of the drawsurface is applied once, by the CanvasGroup
    // (see <see cref="_group"/>), not carried in these colours.
    // Fallback face, for when the dial cannot be loaded.
    static readonly Color Back   = new Color(0.82f, 0.82f, 0.80f, 0.92f);

    Button _toggle, _in, _out, _bigger;
    bool _shown = true;
    Texture2D _dial;

    RooFile _room;
    // Walls worth drawing, in server units, filtered once per room.
    readonly List<Vector2> _walls = new List<Vector2>();

    IEnumerable<RoomObject> _objects;
    float _px, _py, _angle;

    /// <summary>
    /// The clip radius as a fraction of half the dial's width.
    ///
    /// The reference does not clip at the window's edge but inside it:
    /// `UI_MINIMAP_CLIPPADDING = 13.0f/256.0f` (`Constants.h:932`) is
    /// the inset on every side, and `MiniMapCEGUI.h:173-184` adds a pie
    /// from (padding * width, padding * height) across
    /// (width - 2 * padding * width) by the same in height, 0 to 360
    /// degrees, as the graphics clip. Half of that pie's width over half
    /// the window's is 1 - 2 * 13/256 = 0.898. This used 0.88, a guess at
    /// "about the rim", which showed 4% less of the room than the game
    /// does at the same zoom - the ratio of the areas is (0.88/0.898)^2.
    /// </summary>
    const float ClipPadding = 13f / 256f;
    const float ClipFraction = 1f - 2f * ClipPadding;

    /// <summary>
    /// Everything the map draws that the reference draws onto its
    /// drawsurface - walls, dots, the arrow - goes in here, and only here.
    ///
    /// The reference's alpha of 0.9 is a property of ONE window: the
    /// layout gives `MiniMap.DrawSurface` `Alpha 0.9` with
    /// `InheritsAlpha False` (`Resources/ui/layouts/Meridian59.layout:1270-1277`),
    /// while the dial behind it stays at 1 (`:1265`). The map is
    /// painted into a bitmap by GDI+ at full opacity (`MiniMapCEGUI.h`),
    /// the bitmap is uploaded as one image, and CEGUI fades that image
    /// as a whole. So where a dot lies over a wall, or two walls cross,
    /// the picture underneath is one flat colour and the fade is applied
    /// to it once - the overlap is exactly as dense as a lone stroke.
    ///
    /// This client used to give each primitive its own 0.9. Two strokes
    /// at 0.9 over each other come out at 0.99, so every crossing and
    /// every dot on a wall was visibly heavier than the game's. A
    /// CanvasGroup renders its children into an offscreen buffer first
    /// and applies its own Modulate once to the result, which is the same
    /// order of operations: opaque strokes, then one fade. The group is
    /// sized to the dial and not the screen so the offscreen buffer is a
    /// couple of hundred pixels square.
    /// </summary>
    CanvasGroup _group;
    Control _surface;

    /// <summary>The reference's drawsurface alpha (`Meridian59.layout:1276`).</summary>
    const float SurfaceAlpha = 0.9f;

    /// <summary>Redraws the dial and the drawsurface together.</summary>
    void Redraw()
    {
        QueueRedraw();
        _surface?.QueueRedraw();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // First child, so it is drawn before - under - the zoom and size
        // buttons, which sit on the dial's face. A child added after them
        // would paint the map over its own controls.
        _group = new CanvasGroup { Modulate = new Color(1f, 1f, 1f, SurfaceAlpha) };
        _surface = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _surface.Draw += DrawSurface;
        _group.AddChild(_surface);
        AddChild(_group);

        _toggle = new Button { Text = "Map" };
        _toggle.Pressed += () => { _shown = !_shown; Save(); Layout(); Redraw(); };
        AddChild(_toggle);
        Panels.Opener(_toggle, "The map", 10, Panels.Where.Top);

        // The game zooms with the mouse wheel, between 1 and 32
        // (`UIMiniMap.cpp:105-114`). A wheel is desktop input but zoom
        // is not a desktop feature: it is how you get from "where is
        // that door" to "where am I in the level", and without it this
        // map showed one fixed slice of the room for ever. Two buttons
        // and a pinch stand in for the wheel.
        // A tap is a coarse multiplicative step and a hold is the
        // reference's own fine additive one - see <see cref="Step"/>.
        _in = Step("+", 1f / 1.25f, -FineStep);
        _out = Step("-", 1.25f, FineStep);

        // The resize control. The reference's gesture for this is the
        // self-target key held down while the wheel turns
        // (`UIMiniMap.cpp:65`), which is two hands and a wheel: a
        // chorded modifier has no touch equivalent at all, and a second
        // pinch cannot be told apart from the zoom pinch this map
        // already reads. So the discrete 32-pixel steps the reference
        // insists on become one button that walks up through them and
        // wraps round to the smallest at the top. A single control
        // reaches every size the reference offers, which two arrows
        // would also do but at twice the clutter on a dial this small.
        _bigger = new Button { Text = "□", Name = "mapSize" };
        _bigger.AddThemeFontSizeOverride("font_size", 18);
        _bigger.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.1f));
        _bigger.AddThemeColorOverride("font_hover_color", new Color(0.1f, 0.1f, 0.1f));
        _bigger.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f));
        _bigger.AddThemeConstantOverride("outline_size", 4);
        _bigger.TooltipText = "Map size";
        _bigger.Pressed += NextSize;
        AddChild(_bigger);

        Load();

        // The game's own minimap face. Loaded as a resource rather than
        // read off disk: Image.LoadFromFile works from the editor and
        // quietly does nothing in an exported build, where res:// is an
        // entry in the pck and not a file. Missing art is not worth a
        // crash either - the map falls back to a plain disc.
        try
        {
            _dial = GD.Load<Texture2D>("res://art/minimap-bg.png");
        }
        catch (Exception e) { GD.PrintErr($"[MiniMap] no dial: {e.Message}"); }

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>
    /// The reference's zoom step: `zoom += wheelChange * -0.2f`, clamped
    /// to 1..32 (`UIMiniMap.cpp:105-114`). It is additive, so at the
    /// default zoom of 8 one notch is 2.5% and every value in between is
    /// reachable; a player can settle on exactly the view they want.
    /// Positive here means zoom OUT (a larger number is more room per
    /// pixel), which is the wheel turned towards you, wheelChange -1.
    /// </summary>
    const float FineStep = 0.2f;

    /// <summary>How long a button is held before it starts repeating.</summary>
    const double HoldDelay = 0.35;

    /// <summary>
    /// Seconds between fine steps while held. The reference's wheel has no
    /// rate - a notch is whatever the player's finger makes it - so this
    /// is chosen: 0.04s is 5 units a second, which sweeps the whole 1..32
    /// band in about six seconds, slow enough to stop on a value and fast
    /// enough that walking it from one end to the other is not a chore.
    /// </summary>
    const double HoldRepeat = 0.04;

    // The button being held, the direction it steps, and its clock.
    Button _held;
    float _heldStep;
    double _heldFor;
    // A hold that has already repeated must not ALSO fire the coarse tap
    // when the finger lifts: Button raises Pressed on release, so without
    // this every long-press ended with one extra x1.25 lurch.
    bool _heldRepeated;

    /// <summary>
    /// A zoom button. Two gestures, two granularities.
    ///
    /// A tap is multiplicative rather than the game's flat 0.2 a notch: a
    /// notch is cheap on a wheel and a tap is not, so each press has to be
    /// worth making - but that gives only ~17 reachable zooms between 1
    /// and 32, and no way to tune. Holding the button therefore repeats
    /// the reference's own additive step (<see cref="FineStep"/>) after a
    /// short delay, which lands on the reference's granularity. The pinch
    /// stays, and is continuous already.
    /// </summary>
    Button Step(string text, float factor, float fine)
    {
        var b = new Button { Text = text, Name = text == "+" ? "mapIn" : "mapOut" };
        b.AddThemeFontSizeOverride("font_size", 24);
        // Black on the dial's pale face, with a light outline so it
        // still reads where a wall shows through the face.
        b.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.1f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.1f, 0.1f, 0.1f));
        b.AddThemeColorOverride("font_outline_color", new Color(1f, 1f, 1f));
        b.AddThemeConstantOverride("outline_size", 4);
        b.ButtonDown += () => { _held = b; _heldStep = fine; _heldFor = 0; _heldRepeated = false; };
        // Saved once on release, not on every repeat: a config write at
        // 25 Hz for the length of a hold is a lot of disk for one value.
        b.ButtonUp += () => { if (_held == b) { _held = null; if (_heldRepeated) Save(); } };
        b.Pressed += () =>
        {
            if (_heldRepeated) { _heldRepeated = false; return; }
            Zoom = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
            Save();
            Redraw();
        };
        AddChild(b);
        return b;
    }

    /// <summary>
    /// The clock for a held zoom button. Reads frame time rather than the
    /// wall clock so it runs at whatever rate the screen does.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_held == null) return;
        // A button that lost its press without a release - hidden under a
        // panel, say - must not keep zooming for ever.
        if (!_held.IsVisibleInTree()) { _held = null; return; }

        // The first fine step lands when the delay ends, then one per
        // HoldRepeat; a slow frame catches up instead of making the zoom
        // rate frame-bound.
        static int Steps(double t) => t < HoldDelay ? 0 : (int)((t - HoldDelay) / HoldRepeat) + 1;
        int n = Steps(_heldFor + delta) - Steps(_heldFor);
        _heldFor += delta;
        if (n <= 0) return;
        _heldRepeated = true;
        Zoom = Mathf.Clamp(Zoom + _heldStep * n, MinZoom, MaxZoom);
        Redraw();
    }

    /// <summary>
    /// Pinch, where the device sends one. Factor above one is fingers
    /// spreading, which means zoom in, which means fewer server units
    /// per pixel - so it divides.
    /// </summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!_shown || e is not InputEventMagnifyGesture pinch) return;
        if (pinch.Factor <= 0f) return;
        Zoom = Mathf.Clamp(Zoom / pinch.Factor, MinZoom, MaxZoom);
        Save();
        Redraw();
    }

    /// <summary>
    /// The largest dial this screen can carry, on the reference's own
    /// 32-pixel grid.
    ///
    /// The reference has no equivalent because it does not need one: its
    /// hard 512 ceiling (`UIMiniMap.cpp:69`) is comfortably inside any
    /// desktop viewport. Here the shorter side of the screen is the
    /// constraint - held upright that is the width, and a dial wider than
    /// the screen is not a map - so the ceiling is whichever of the
    /// reference's 512 and four fifths of the shorter side comes first,
    /// rounded DOWN onto the grid so every reachable size is still a
    /// multiple of the step.
    /// </summary>
    int FittingMax()
    {
        Vector2 v = GetViewportRect().Size;
        float room = Mathf.Min(v.X, v.Y) * 0.8f - Margin * 2f;
        int fits = (int)(Mathf.Min(MaxMapSize, room) / MapSizeStep) * MapSizeStep;
        // Never below the floor: a screen too small for even 160 points
        // of dial gets the floor anyway, because a map that has been
        // clamped out of existence is worse than one that overhangs.
        return Mathf.Max(fits, MinMapSize);
    }

    /// <summary>
    /// One press of the size button: up a step, wrapping to the smallest
    /// once the largest size this screen allows has been passed. The step
    /// and the clamp are the reference's (`UIMiniMap.cpp:74-80`); the wrap
    /// is what makes one button do the work of a modifier plus a wheel.
    /// </summary>
    void NextSize()
    {
        int max = FittingMax();
        // Snap onto the grid first. The shipped default of 220 is not a
        // multiple of 32 - it predates this control - and stepping from
        // it would carry that offset for ever, so the first press lands
        // on the grid and every press after that stays on it.
        int snapped = (int)Mathf.Round(MapSize / (float)MapSizeStep) * MapSizeStep;
        int next = (snapped <= MapSize ? snapped + MapSizeStep : snapped);
        MapSize = next > max ? MinMapSize : Mathf.Clamp(next, MinMapSize, max);
        Save();
        Layout();
        Redraw();
    }

    void Layout()
    {
        if (_toggle == null) return;
        Vector2 v = GetViewportRect().Size;
        // A size saved on one orientation can be too big for another -
        // the reference never has to think about this because its
        // viewport does not turn ninety degrees under it - so the dial
        // is pulled back inside what fits whenever the screen changes.
        MapSize = Mathf.Clamp(MapSize, MinMapSize, FittingMax());
        // On the dial's own face rather than under it. Below the dial
        // they sat over the world, where a dark button on a dark wall is
        // barely a shape; the face is pale and holds them.
        float w = 40f, h = 34f;
        float left = v.X - MapSize - Margin;
        float y = Margin + MapSize * 0.72f;
        _out.Size = _in.Size = new Vector2(w, h);
        _out.Position = new Vector2(left + MapSize * 0.5f - w - 6f, y);
        _in.Position = new Vector2(left + MapSize * 0.5f + 6f, y);
        _in.Visible = _out.Visible = _shown;

        // The drawsurface fills the dial's square, as the layout's
        // {{0,0},{0,0},{1,0},{1,0}} does (`Meridian59.layout:1272`).
        _group.Position = new Vector2(left, Margin);
        _surface.Size = new Vector2(MapSize, MapSize);
        _group.Visible = _shown;

        // The size button sits a row below the zoom pair, still on the
        // dial's pale face for the same reason they are: a dark glyph on
        // a dark wall is not a button.
        _bigger.Size = new Vector2(w, h);
        _bigger.Position = new Vector2(left + MapSize * 0.5f - w * 0.5f, y + h + 4f);
        _bigger.Visible = _shown;
    }

    const string PrefsPath = "user://view.cfg";

    /// <summary>
    /// Keeps the map's zoom, its size, and whether it is up at all.
    ///
    /// Only the last two are the reference's behaviour. It writes the
    /// minimap window's position, size and visibility into its
    /// configuration on the way out (`ControllerUI.cpp:643-645`) and reads
    /// the visibility back on a mode change (`:722`); the zoom is NOT
    /// among them. `MiniMap::Zoom` is a plain static initialised to 8
    /// (`ControllerUI.h:559`) that nothing saves, so in the game every
    /// launch starts at 8 again and the player winds the wheel from there.
    ///
    /// Persisting the zoom is therefore a deliberate improvement on the
    /// reference, not a mirror of it: re-zooming with a thumb on every
    /// launch is a much bigger tax than re-zooming with a wheel, and
    /// nothing in the game depends on the value starting at 8.
    /// </summary>
    void Save()
    {
        try
        {
            var f = new ConfigFile();
            f.Load(PrefsPath);
            f.SetValue("map", "zoom", Zoom);
            f.SetValue("map", "shown", _shown);
            // Alongside the zoom, in the same file and the same section
            // the rest of this view's settings live in. The reference
            // persists the minimap's size the same way - as part of the
            // window's layout in its own config
            // (`ControllerUI.cpp:644`, `OgreClientConfig.cpp:781-784`) -
            // so a chosen size is not a per-session thing there either.
            f.SetValue("map", "size", MapSize);
            f.Save(PrefsPath);
        }
        catch (Exception e) { GD.PrintErr($"[MiniMap] save: {e.Message}"); }
    }

    void Load()
    {
        try
        {
            var f = new ConfigFile();
            if (f.Load(PrefsPath) != Error.Ok) return;
            Zoom = Mathf.Clamp((float)f.GetValue("map", "zoom", Zoom), MinZoom, MaxZoom);
            _shown = (bool)f.GetValue("map", "shown", _shown);
            // Clamped on the way in as the reference clamps on the way
            // round (`UIMiniMap.cpp:79-80`): a config edited by hand, or
            // written on a larger screen, must not be able to hand this
            // a dial bigger than the device can draw.
            MapSize = Mathf.Clamp((int)f.GetValue("map", "size", MapSize), MinMapSize, MaxMapSize);
        }
        catch (Exception e) { GD.PrintErr($"[MiniMap] load: {e.Message}"); }
    }

    /// <summary>
    /// Takes the room's walls. Called on a room change: the filtering and
    /// the unit conversion are per room, and what moves per frame is only
    /// where the window onto them sits.
    /// </summary>
    public void Build(RooFile roo)
    {
        _room = roo;
        _walls.Clear();
        if (roo == null) return;

        foreach (RooWall w in roo.Walls)
        {
            RooSideDef left = w.LeftSide, right = w.RightSide;

            // The library's own four cases: no sides at all, or every side
            // there is flagged map-never.
            if ((left == null && right == null) ||
                (left != null && right == null && left.Flags.IsMapNever) ||
                (left == null && right != null && right.Flags.IsMapNever) ||
                (left != null && right != null && left.Flags.IsMapNever && right.Flags.IsMapNever))
                continue;

            _walls.Add(new Vector2(w.X1 * 0.0625f + 64f, w.Y1 * 0.0625f + 64f));
            _walls.Add(new Vector2(w.X2 * 0.0625f + 64f, w.Y2 * 0.0625f + 64f));
        }
        Redraw();
    }

    /// <summary>
    /// How many wall segments survived the map-never filter. The room's
    /// own wall count is not the same number and does not tell you
    /// whether the map has anything to draw.
    /// </summary>
    public int MappedWalls => _walls.Count / 2;

    /// <summary>Objects to show. The avatar among them is skipped.</summary>
    public void SetObjects(IEnumerable<RoomObject> objects) => _objects = objects;

    /// <summary>
    /// Where you are, in server units, and which way you face. Cheap: the
    /// map redraws from the wall list rather than rebuilding anything.
    /// </summary>
    public void SetPlayer(float kodX, float kodY, float angle)
    {
        _px = kodX; _py = kodY; _angle = angle;
        if (_shown) Redraw();
    }

    public override void _Draw()
    {
        // Only the dial's face lives here; the map is on the drawsurface
        // (see <see cref="_group"/>), which is what carries the 0.9.
        if (!_shown) return;

        Vector2 v = GetViewportRect().Size;
        var origin = new Vector2(v.X - MapSize - Margin, Margin);
        float half = MapSize * 0.5f;

        if (_dial != null)
            DrawTextureRect(_dial, new Rect2(origin, new Vector2(MapSize, MapSize)), false);
        else
            DrawCircle(origin + new Vector2(half, half), half * ClipFraction, Back);
    }

    void DrawSurface()
    {
        // Not "and there are walls". A room whose every wall is
        // map-never still has you in it, and the game always draws the
        // dial and the arrow (`MiniMapCEGUI.cpp:274`, `:337`). Bailing
        // on an empty wall list turned that into a blank corner.
        if (!_shown) return;

        // Local to the drawsurface: it is positioned at the dial's corner.
        var origin = Vector2.Zero;
        float half = MapSize * 0.5f;
        Vector2 centre = origin + new Vector2(half, half);
        // Cut to the game's own pie, `UI_MINIMAP_CLIPPADDING` in from the
        // window edge (`Constants.h:932`, `MiniMapCEGUI.h:173-184`) - see
        // <see cref="ClipFraction"/>. The game's map texture fills the
        // square window and the pie is the only thing that stops walls at
        // the rim; Godot has no clip to hand inside a draw, so the same
        // cut is done geometrically below.
        float radius = half * ClipFraction;

        // The window onto the room, in server units, centred on you.
        float zoom = Mathf.Clamp(Zoom, MinZoom, MaxZoom);
        float reach = half * zoom;
        float minX = _px - reach, minY = _py - reach;
        float inv = 1f / zoom;

        Vector2 Place(float kx, float ky)
            => origin + new Vector2((kx - minX) * inv, (ky - minY) * inv);

        // Round, so each wall is cut to the circle rather than merely
        // tested against it. Without the cut, walls run out across the
        // room behind the map, which is not a map, it is a scribble.
        var clipped = new List<Vector2>();
        for (int i = 0; i + 1 < _walls.Count; i += 2)
        {
            Vector2 a = Place(_walls[i].X, _walls[i].Y);
            Vector2 b = Place(_walls[i + 1].X, _walls[i + 1].Y);
            if (!Clip(ref a, ref b, centre, radius)) continue;
            clipped.Add(a); clipped.Add(b);
        }
        // Two pixels, as the game's pen is (`MiniMapCEGUI.h:265`). One
        // was a fair reading of a map drawn at desktop scale, but a
        // single black hairline on a phone screen is close to invisible.
        // Opaque: the 0.9 is the group's, applied once to the lot.
        if (clipped.Count > 0)
            _surface.DrawMultiline(clipped.ToArray(), Wall, 2f);

        if (_objects != null)
        {
            foreach (RoomObject o in _objects)
            {
                if (o == null || o.IsAvatar || o.Flags == null) continue;
                if (o.Flags.Drawing == ObjectFlags.DrawingType.Invisible) continue;

                Color? dot = DotColour(o.Flags);
                Color? ring = RingColour(o.Flags);
                if (dot == null && ring == null) continue;

                Vector2 p = Place(o.Position3D.X, o.Position3D.Z);

                // Ten pixels across for the ring, six for the dot, as the
                // game draws them - and cut to the rim, as the game cuts
                // them. Testing the CENTRE against the circle, which is
                // what this did, let a dot sitting on the rim be drawn
                // whole: a thing half a room outside the map still showed
                // as a complete dot hanging off the dial's edge.
                if (ring != null) Blob(p, 5f, ring.Value, centre, radius);
                if (dot != null) Blob(p, 3f, dot.Value, centre, radius);
            }
        }

        // You, as a triangle, in the player colour. The library builds it
        // from the facing direction and two copies of it rotated by half a
        // turn less a half radian, which is a wide arrowhead rather than a
        // needle.
        Vector2 me = Place(_px, _py);
        var dir = new Vector2(MathF.Cos(_angle), MathF.Sin(_angle)) * 8f;
        Vector2 left = dir.Rotated(MathF.PI - 0.5f);
        Vector2 right = dir.Rotated(-MathF.PI + 0.5f);
        // The game's alpha comes from the surface the whole map is drawn
        // on, so it covers the dots and the arrow as well as the walls -
        // and here too, through the group, not per primitive.
        _surface.DrawColoredPolygon(new[] { me + dir, me + left, me + right }, Player);
    }

    /// <summary>
    /// The inner dot's colour, in `MiniMapCEGUI::DrawObject`'s own order.
    /// Null means the game draws nothing, which is the common case.
    ///
    /// This is the non-vanilla branch on purpose. Server 104 is that
    /// flavour - its proto.h carries the separate MM_* minimap bitfield
    /// and sends name colours as hex RGB - and so is the library as it is
    /// built here, with VANILLA undefined. Mirroring the vanilla branch
    /// instead reads flags that this server does not set.
    /// </summary>
    static Color? DotColour(ObjectFlags f)
    {
        if (f.IsMinimapPlayer) return Player;
        if (f.IsMinimapTempSafe) return TempSafe;
        if (f.IsMinimapMinionSelf) return Minion;
        if (f.IsMinimapMinionOther) return MinionOther;
        if (f.IsMinimapMercenary) return Mercenary;
        if (f.IsMinimapMobKillQuest) return MobQuest;
        if (f.IsMinimapMonster) return Enemy;
        if (f.IsMinimapNPC) return Npc;
        if (f.IsRareItem) return RareItem;
        if (f.IsMinimapMiniBoss) return MiniBoss;
        if (f.IsMinimapBoss) return Boss;
        if (f.IsNonPvP) return NoPvP;
        return null;
    }

    /// <summary>
    /// The outer ring, which says what a player is to you - or, for
    /// anything else, whether it has aggro or a quest. Drawn under the dot
    /// and slightly larger, as `DrawObjectOutter` is.
    /// </summary>
    static Color? RingColour(ObjectFlags f)
    {
        if (f.IsPlayer)
        {
            if (f.IsMinimapBuilderGroup) return BuildGroup;
            if (f.IsMinimapFriend) return Friend;
            if (f.IsMinimapEnemy) return Enemy;
            if (f.IsMinimapGuildMate) return GuildMate;
            return null;
        }

        if (f.IsMinimapAggroSelf) return AggroSelf;
        if (f.IsMinimapAggroOther) return AggroOther;
        if (f.IsMinimapNPCCurrentQuest) return Friend;
        if (f.IsMinimapNPCHasQuest) return GuildMate;
        return null;
    }

    /// <summary>
    /// A dot, cut to the map's circle.
    ///
    /// The game draws its whole map through one circular clip region -
    /// `gdi->Clip = Region(pie 0..360)`, `MiniMapCEGUI.h:172-185` - so
    /// every wall and every dot is cut by the same edge and a dot on the
    /// rim comes out as a half dot. Godot has no clip to hand inside a
    /// _Draw, so the cut is done here: a dot well inside is one circle,
    /// a dot well outside is nothing, and only the few straddling the
    /// edge pay for a polygon intersection.
    /// </summary>
    void Blob(Vector2 p, float r, Color colour, Vector2 centre, float radius)
    {
        float d = p.DistanceTo(centre);
        if (d + r <= radius) { _surface.DrawCircle(p, r, colour); return; }
        if (d - r >= radius) return;

        Vector2[] disc = Ring(centre, radius, 64);
        Vector2[] blob = Ring(p, r, 16);
        foreach (Vector2[] piece in Geometry2D.IntersectPolygons(blob, disc))
            if (piece.Length >= 3) _surface.DrawColoredPolygon(piece, colour);
    }

    /// <summary>A closed regular polygon approximating a circle.</summary>
    static Vector2[] Ring(Vector2 centre, float radius, int sides)
    {
        var pts = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.Tau * i / sides;
            pts[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return pts;
    }

    /// <summary>
    /// Cuts a segment to the circle, returning false if it misses
    /// entirely. Solves |a + t(b-a) - centre| = radius for the two
    /// crossings and keeps the part of 0..1 between them.
    /// </summary>
    static bool Clip(ref Vector2 a, ref Vector2 b, Vector2 centre, float radius)
    {
        Vector2 d = b - a;
        Vector2 f = a - centre;

        float A = d.LengthSquared();
        if (A < 1e-6f) return f.LengthSquared() <= radius * radius;

        float B = 2f * f.Dot(d);
        float C = f.LengthSquared() - radius * radius;

        float disc = B * B - 4f * A * C;
        if (disc < 0f) return false;                 // the line misses the circle

        disc = MathF.Sqrt(disc);
        float t0 = (-B - disc) / (2f * A);
        float t1 = (-B + disc) / (2f * A);

        // the overlap between the segment and the chord
        float lo = MathF.Max(0f, t0), hi = MathF.Min(1f, t1);
        if (hi <= lo) return false;

        Vector2 a0 = a;
        a = a0 + d * lo;
        b = a0 + d * hi;
        return true;
    }
}
