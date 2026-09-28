using System;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// Your own face, in the corner.
///
/// The game has an avatar panel - `UIAvatar.cpp` and the `Avatar` window
/// in the CEGUI layout - holding a head portrait, the condition bars, and
/// a row of enchantment icons. This is the portrait half of it; the bars
/// live in <see cref="Vitals"/>.
///
/// The portrait is not a separate picture. It is your own object composed
/// from its HEAD hotspot downwards, with the front frame rather than the
/// viewer's, no Y offset, centred in the box - exactly the arguments
/// `UIAvatar` gives its composer. That is why it turns into a face rather
/// than a whole body: the hotspot picks out which part of the composed
/// object to build from.
/// </summary>
public partial class AvatarPanel : Control
{
    [Export] public int Size = 72;
    [Export] public float Margin = 12f;
    /// <summary>Leaves room for whatever owns the top-left corner.</summary>
    [Export] public float TopReserve = 0f;

    DataController _data;
    TextureRect _head;
    uint _shown;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _head = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_head);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_head == null) return;
        _head.Position = new Vector2(Margin, Margin + TopReserve);
        _head.Size = new Vector2(Size, Size);
    }

    /// <summary>
    /// Rebuilds the portrait when the avatar's appearance changes. The
    /// hash is the library's own - it moves when the frame, the parts or
    /// their colours do - so a walking player does not recompose a face
    /// every frame.
    /// </summary>
    public void Follow(DataController data)
    {
        _data = data;
        RoomObject me = data?.AvatarObject;
        if (me == null) { _head.Visible = false; return; }

        if (me.AppearanceHash == _shown && _head.Texture != null) return;
        _shown = me.AppearanceHash;

        try
        {
            Tex t = M59Compose.Icon(me, Size, (byte)KnownHotspot.HEAD);
            _head.Texture = M59Assets.FromTex(t);
            _head.Visible = _head.Texture != null;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AvatarPanel] portrait: {e.Message}");
            _head.Visible = false;
        }
    }
}
