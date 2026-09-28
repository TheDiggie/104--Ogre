using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// Your own face, in the corner.
///
/// The game has an avatar panel - `UIAvatar.cpp` and the `Avatar` window
/// in the CEGUI layout - holding a head portrait, the condition bars, and
/// a grid of enchantment icons. This is the portrait and the enchantments;
/// the bars live in <see cref="Vitals"/>.
///
/// The enchantments are whatever is in the client's own `AvatarBuffs`,
/// each composed the way the game composes a buff icon: front frame, no Y
/// offset, centred in a small box. Sixteen pixels there; bigger here,
/// because a phone is not a mouse pointer.
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

    [Export] public int BuffSize = 28;
    readonly List<TextureRect> _buffs = new List<TextureRect>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _buffSignature = "";

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

    /// <summary>
    /// The enchantments on you, as icons under the portrait. Rebuilt only
    /// when the list changes.
    /// </summary>
    public void SyncBuffs(DataController data)
    {
        if (data?.AvatarBuffs == null) { HideBuffs(0); return; }

        var sb = new System.Text.StringBuilder();
        foreach (ObjectBase b in data.AvatarBuffs) sb.Append(b?.ID).Append(';');
        string now = sb.ToString();
        if (now == _buffSignature) return;
        _buffSignature = now;

        int used = 0;
        foreach (ObjectBase b in data.AvatarBuffs)
        {
            if (b?.Resource == null) continue;
            TextureRect icon = TakeBuff(used);
            icon.Texture = BuffIcon(b);
            icon.TooltipText = b.Name;
            icon.Position = new Vector2(Margin + used * (BuffSize + 4f), Margin + TopReserve + Size + 6f);
            icon.Size = new Vector2(BuffSize, BuffSize);
            icon.Visible = icon.Texture != null;
            used++;
        }
        HideBuffs(used);
    }

    ImageTexture BuffIcon(ObjectBase o)
    {
        string key = $"{o.Resource.Filename}:{BuffSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, BuffSize)); }
        catch (Exception e) { GD.PrintErr($"[AvatarPanel] buff {o.Name}: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }

    TextureRect TakeBuff(int index)
    {
        while (_buffs.Count <= index)
        {
            var t = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Visible = false,
            };
            AddChild(t);
            _buffs.Add(t);
        }
        return _buffs[index];
    }

    void HideBuffs(int from)
    {
        for (int i = from; i < _buffs.Count; i++) _buffs[i].Visible = false;
    }
}
