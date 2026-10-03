using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// The exclamation mark that floats over an NPC with a quest for you.
///
/// `RemoteNode::CreateQuestMarker` and `UpdateQuestMarker`
/// (RemoteNode.cpp:315, :337) hang one billboard over the object's head
/// and show it when any of three server flags is set:
/// <c>OF_NPCHASQUESTS</c>, <c>OF_NPCACTIVEQUEST</c> or
/// <c>OF_MOBKILLQUEST</c> (ObjectFlags.cs:70-72) - "an NPC with
/// available quests", "an NPC that is the destination of an active
/// quest", "a monster that is the target of a kill quest". They live
/// inside the library's <c>#if !VANILLA</c>, so a vanilla server never
/// sets them and nothing here ever draws; Server 104 does set them.
///
/// The picture is not an asset. `QuestMarkerBitmap`
/// (ImageComposerGDI.cs:358-424) draws the single character "!" in a
/// 42-pixel sans-serif, coloured by <c>QuestMarkerColors.GetColorFor</c>
/// (QuestMarkerColors.cs:36-44): green for an active quest, purple for a
/// kill target, yellow for an NPC with quests going spare, and that
/// function decides the priority as well as the colour, so it is called
/// rather than reimplemented.
///
/// Placement follows the name tag - the top of the drawn picture from
/// the renderer - but three scene units higher rather than one
/// (RemoteNode.cpp:454 against :490), which at the room's scale of
/// 0.0625 (ControllerRoom.h:73) is 48 world units against 16.
///
/// Two things in the reference are deliberately not copied. Its shader
/// lifts the marker further the farther away you stand
/// (general.hlsl:76), which is a readability trick for a 3D billboard
/// and means nothing to a label placed in screen space. And its
/// material is keyed on the colour alone (RemoteNode.cpp:353-358), so
/// every marker of one colour in the room shares a height - the last
/// object to move decides it for all of them. That is a defect, not a
/// rule; here each marker gets its own.
/// </summary>
public partial class QuestMarkers : Control
{
    [Export] public int FontSize = 28;

    /// <summary>
    /// How far above the top of the picture the mark floats, in world
    /// units: the reference's +3 scene units at SCALE 0.0625.
    /// </summary>
    [Export] public float Lift = 48f;

    /// <summary>Fallback when the renderer does not know how tall the thing is.</summary>
    [Export] public float Height = 700f;

    /// <summary>
    /// The height each marker is actually drawn at, held across
    /// animation frames exactly as the name is - see NameTags._offset
    /// for the argument. `UpdateQuestMarkerPosition` is the same routine
    /// with the same threshold, `if (abs(diff) > 16.0f)` commented
    /// "ignore small changes (due to animations)"
    /// (RemoteNode.cpp:457-464), over its own `lastQuestMarkerOffset`
    /// (RemoteNode.h:57). Only the lift differs, +3 scene units against
    /// +1, which is <see cref="Lift"/>.
    /// </summary>
    readonly Dictionary<uint, float> _offset = new Dictionary<uint, float>();
    readonly HashSet<uint> _seen = new HashSet<uint>();

    /// <summary>The reference's threshold, in world units: 16 scene units.</summary>
    const float Deadband = 256f;

    readonly List<NameTags.Tag> _pool = new List<NameTags.Tag>();
    readonly List<uint> _gone = new List<uint>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Sync(Renderer renderer, List<RoomObject> objects, Vector2 scale)
    {
        if (renderer == null || objects == null) { Hide(0); return; }

        int used = 0;
        _seen.Clear();
        foreach (RoomObject o in objects)
        {
            if (!Shows(o)) continue;

            uint argb = QuestMarkerColors.GetColorFor(o.Flags);
            if (argb == 0xFFFFFFFF) continue;   // the function's own "none"

            float wx = M59Geo.KodToWorld(o.Position3D.X);
            float wy = M59Geo.KodToWorld(o.Position3D.Z);
            float tall = renderer.WorldHeight(renderer.SpriteFor(o));
            float h = (tall > 0f ? tall : Height) + Lift;
            _seen.Add(o.ID);
            if (!_offset.TryGetValue(o.ID, out float held)) held = 0f;
            if (MathF.Abs(h - held) > Deadband) held = h;
            _offset[o.ID] = held;
            float wz = M59Geo.KodHeightToXY(o.Position3D.Y) + held;

            if (!renderer.Project(wx, wy, wz, out float sx, out float sy)) continue;

            NameTags.Tag l = Take(used++);
            // The same centring the names use. The mark is always "!",
            // so the stale-minimum trap cannot bite here the way it does
            // there - but two copies of a placement is two places for
            // the next change to miss one. See NameTags.Tag.
            l.Set("!", argb);
            l.CentreAt(new Vector2(sx * scale.X, sy * scale.Y));
        }

        Hide(used);
        Forget();
    }

    /// <summary>Drops the held heights of objects that have gone.</summary>
    void Forget()
    {
        if (_offset.Count == 0) return;
        _gone.Clear();
        foreach (uint id in _offset.Keys) if (!_seen.Contains(id)) _gone.Add(id);
        foreach (uint id in _gone) _offset.Remove(id);
    }

    /// <summary>
    /// `UpdateQuestMarker` (RemoteNode.cpp:339-343): any of the three
    /// quest flags, not flagged invisible, and not your own avatar while
    /// the camera is first person - which this client always is. Unlike
    /// a name, a marker does not need OF_DISPLAY_NAME and does not need
    /// the object to have a name at all.
    /// </summary>
    static bool Shows(RoomObject o)
    {
        return o != null
            && o.Flags != null
            && (o.Flags.IsNPCHasQuests || o.Flags.IsNPCActiveQuest || o.Flags.IsMobKillQuest)
            && o.Flags.Drawing != ObjectFlags.DrawingType.Invisible
            && !o.IsAvatar;
    }

    NameTags.Tag Take(int index)
    {
        while (_pool.Count <= index)
        {
            var l = new Label { Text = "!", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            l.AddThemeFontSizeOverride("font_size", FontSize);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            l.AddThemeConstantOverride("outline_size", 5);
            AddChild(l);
            _pool.Add(new NameTags.Tag(l));
        }
        return _pool[index];
    }

    void Hide(int from)
    {
        for (int i = from; i < _pool.Count; i++)
            if (_pool[i].Label.Visible) _pool[i].Label.Visible = false;
    }
}
