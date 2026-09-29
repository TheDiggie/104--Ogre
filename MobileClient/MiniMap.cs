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
///    you: enemy red, guildmate green, other players blue, anything else
///    attackable red. Nothing else is drawn - an ordinary item on the
///    floor does not appear.
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
    /// Server units per pixel. The library's default is 4; its own limits
    /// are 0.05 to 20, and the Ogre client's wheel moves between 1 and 32.
    /// </summary>
    [Export] public float Zoom = 4f;

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
    // The drawsurface sits at alpha 0.9 over the background image.
    static readonly Color Ink    = new Color(1f, 1f, 1f, 0.9f);
    // Fallback face, for when the dial cannot be loaded.
    static readonly Color Back   = new Color(0.82f, 0.82f, 0.80f, 0.92f);

    Button _toggle;
    bool _shown = true;
    Texture2D _dial;

    RooFile _room;
    // Walls worth drawing, in server units, filtered once per room.
    readonly List<Vector2> _walls = new List<Vector2>();

    IEnumerable<RoomObject> _objects;
    float _px, _py, _angle;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _toggle = new Button { Text = "Map" };
        _toggle.Pressed += () => { _shown = !_shown; QueueRedraw(); };
        AddChild(_toggle);

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

    void Layout()
    {
        if (_toggle == null) return;
        Vector2 v = GetViewportRect().Size;
        _toggle.Size = new Vector2(70, 40);
        _toggle.Position = new Vector2(v.X - 70 - Margin, v.Y - 40 - Margin);
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
        QueueRedraw();
    }

    /// <summary>Objects to show. The avatar among them is skipped.</summary>
    public void SetObjects(IEnumerable<RoomObject> objects) => _objects = objects;

    /// <summary>
    /// Where you are, in server units, and which way you face. Cheap: the
    /// map redraws from the wall list rather than rebuilding anything.
    /// </summary>
    public void SetPlayer(float kodX, float kodY, float angle)
    {
        _px = kodX; _py = kodY; _angle = angle;
        if (_shown) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_shown || _walls.Count == 0) return;

        Vector2 v = GetViewportRect().Size;
        var origin = new Vector2(v.X - MapSize - Margin, Margin);
        float half = MapSize * 0.5f;
        Vector2 centre = origin + new Vector2(half, half);
        // The dial's rim is about a twelfth of its width, so the map is
        // cut inside it. The game does not bother - its map texture fills
        // the square window and walls run under the rim - but it hit-tests
        // the map against a circle, and a map that stops at the frame
        // looks like a map rather than a leak.
        float radius = half * 0.88f;

        if (_dial != null)
            DrawTextureRect(_dial, new Rect2(origin, new Vector2(MapSize, MapSize)), false);
        else
            DrawCircle(centre, radius, Back);

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
        if (clipped.Count > 0)
            DrawMultiline(clipped.ToArray(), new Color(Wall, Ink.A), 1f);

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
                if (p.DistanceTo(centre) > radius) continue;

                // Ten pixels across for the ring, six for the dot, as the
                // game draws them.
                if (ring != null) DrawCircle(p, 5f, ring.Value);
                if (dot != null) DrawCircle(p, 3f, dot.Value);
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
        DrawColoredPolygon(new[] { me + dir, me + left, me + right }, Player);
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
