using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// Names over people's heads.
///
/// The rule is `RemoteNode::UpdateName` in the Ogre client: a name is
/// drawn when the object is flagged to display one, is not flagged
/// invisible, actually has a name, and is not your own avatar while the
/// camera is in first person - which this client always is.
///
/// Which flag decides it depends on the flavour, and Server 104 is the
/// non-vanilla one: its proto.h has `OF_DISPLAY_NAME 0x00000001`, and the
/// server sets it on whatever should be labelled. The vanilla branch uses
/// <c>IsPlayer</c> instead, which on this server would label the wrong
/// things and miss signs entirely.
///
/// The colour likewise: vanilla works it out from the player type, while
/// this server sends the colour itself as hex RGB and
/// <c>NameColors.GetColorFor</c> returns it - falling back to white when
/// the server sends nothing, and to orange for a magic item. That
/// function is the library's own and is called rather than reimplemented.
///
/// Placement comes from the renderer rather than from a second projection
/// written here, and the renderer refuses a point that is behind a wall,
/// so a name does not hang in front of the wall its owner is standing
/// behind.
/// </summary>
public partial class NameTags : Control
{
    [Export] public int FontSize = 14;

    /// <summary>
    /// How far above the object's base the name floats when the
    /// drawn height is not known, in world units. The game puts the
    /// name at the top of the picture - `RemoteNode::UpdateNamePosition`
    /// asks the scene node how tall it is and adds one - so a fixed
    /// height hangs names in the air over a rat and through the chest
    /// of a cyclops. This is only the fallback.
    /// </summary>
    [Export] public float Height = 700f;

    /// <summary>
    /// How far above the top of the drawn picture the name sits, in
    /// world units: the reference's +1 scene unit
    /// (`RemoteNode::UpdateNamePosition`, RemoteNode.cpp:491) at the
    /// room's SCALE of 0.0625 (ControllerRoom.h:73), which is 16. This
    /// client placed the name at the picture's top exactly, so every
    /// name sat one notch lower than the game puts it.
    /// </summary>
    [Export] public float Lift = 16f;

    /// <summary>
    /// The height each object's name is actually drawn at, which is not
    /// the height its picture happens to be this frame.
    ///
    /// `UpdateNamePosition` works out `h` every time it is called and
    /// then applies it only when it has moved by more than sixteen
    /// scene units - `if (abs(diff) > 16.0f)`, commented "ignore small
    /// changes (due to animations)" (RemoteNode.cpp:494-501), with
    /// `lastNameOffset` starting at zero (RemoteNode.h:52). Sixteen
    /// scene units is 256 world units here. Without it the name is
    /// re-placed from `WorldHeight` on every frame and bobs with the
    /// walk cycle: measured over the art, the top-of-picture height
    /// across a multi-group BGF's groups has a median spread of 203
    /// world units, and most of the game's creatures never leave the
    /// dead band at all - for those the reference holds the name
    /// perfectly still and this moved it every frame.
    ///
    /// Keyed on the object id, because the reference keeps it on the
    /// node, which is per object. Entries for objects that have left
    /// the room are dropped at the end of each pass rather than kept
    /// for a session - ids are reused across rooms.
    /// </summary>
    readonly Dictionary<uint, float> _offset = new Dictionary<uint, float>();
    readonly HashSet<uint> _seen = new HashSet<uint>();

    /// <summary>The reference's threshold, in world units: 16 scene units.</summary>
    const float Deadband = 256f;

    readonly List<Label> _pool = new List<Label>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Places a name over every object that should have one.
    /// <paramref name="scale"/> converts the renderer's own buffer to
    /// screen pixels, because the world is drawn small and stretched up.
    /// </summary>
    public void Sync(Renderer renderer, IEnumerable<RoomObject> objects, Vector2 scale)
    {
        if (renderer == null || objects == null) { Hide(0); return; }

        int used = 0;
        _seen.Clear();
        foreach (RoomObject o in objects)
        {
            if (!Shows(o)) continue;

            float wx = M59Geo.KodToWorld(o.Position3D.X);
            float wy = M59Geo.KodToWorld(o.Position3D.Z);
            // The top of the drawn picture plus the reference's lift,
            // held steady across animation frames - see _offset.
            float tall = renderer.WorldHeight(renderer.SpriteFor(o));
            float h = (tall > 0f ? tall : Height) + Lift;
            _seen.Add(o.ID);
            if (!_offset.TryGetValue(o.ID, out float held)) held = 0f;
            if (MathF.Abs(h - held) > Deadband) held = h;
            _offset[o.ID] = held;
            float wz = M59Geo.KodHeightToXY(o.Position3D.Y) + held;

            if (!renderer.Project(wx, wy, wz, out float sx, out float sy)) continue;

            Label l = Take(used++);
            l.Text = o.Name;

            uint argb = NameColors.GetColorFor(o.Flags);
            l.AddThemeColorOverride("font_color", new Color(
                ((argb >> 16) & 0xFF) / 255f,
                ((argb >> 8) & 0xFF) / 255f,
                (argb & 0xFF) / 255f));

            // Centred over the head rather than starting there.
            l.Size = Vector2.Zero;
            Vector2 at = new Vector2(sx * scale.X, sy * scale.Y);
            l.Position = at - new Vector2(l.GetMinimumSize().X * 0.5f, l.GetMinimumSize().Y);
            l.Visible = true;
        }

        Hide(used);
        Forget();
    }

    /// <summary>
    /// Drops the held heights of objects that are no longer here. The
    /// reference has nothing to do because the state lives on the scene
    /// node and dies with it; a dictionary has to be told.
    /// </summary>
    void Forget()
    {
        if (_offset.Count == 0) return;
        var gone = new List<uint>();
        foreach (uint id in _offset.Keys) if (!_seen.Contains(id)) gone.Add(id);
        foreach (uint id in gone) _offset.Remove(id);
    }

    /// <summary>
    /// The rule, whole. The avatar is excluded because the camera is
    /// always first person here - the game only hides your own name in
    /// that case.
    /// </summary>
    static bool Shows(RoomObject o)
    {
        return o != null
            && o.Flags != null
            && o.Flags.IsDisplayName
            && o.Flags.Drawing != ObjectFlags.DrawingType.Invisible
            && !o.IsAvatar
            && !string.IsNullOrWhiteSpace(o.Name);
    }

    Label Take(int index)
    {
        while (_pool.Count <= index)
        {
            var l = new Label { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            l.AddThemeFontSizeOverride("font_size", FontSize);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            l.AddThemeConstantOverride("outline_size", 4);
            AddChild(l);
            _pool.Add(l);
        }
        return _pool[index];
    }

    void Hide(int from)
    {
        for (int i = from; i < _pool.Count; i++) _pool[i].Visible = false;
    }
}
