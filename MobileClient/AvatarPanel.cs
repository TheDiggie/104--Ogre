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
    /// <summary>
    /// How big the portrait is drawn, in pixels, square.
    ///
    /// Named HeadSize rather than Size because Control already has a
    /// Size, of type Vector2, and an int called Size on a Control hides
    /// it (CS0108). It worked only by luck of reference type: every use
    /// inside this class meant the int, and every use outside went
    /// through a variable typed Control, which meant the Vector2. A
    /// Vector2 assigned through an AvatarPanel-typed variable would have
    /// silently set this number instead of the control's rectangle, and
    /// nothing would have said so.
    /// </summary>
    [Export] public int HeadSize = 72;
    [Export] public float Margin = 12f;
    /// <summary>Leaves room for whatever owns the top-left corner.</summary>
    [Export] public float TopReserve = 0f;

    DataController _data;
    Button _head;
    uint _shown;

    [Export] public int BuffSize = 28;
    /// <summary>
    /// An enchantment on you was tapped: look at it.
    ///
    /// `UIAvatar.cpp` subscribes a plain left click on every buff slot
    /// and sends `SendReqLookMessage(buff.ID)` - not a right click, so
    /// it maps straight onto a tap. Without it a phone player has no
    /// way at all to find out what has been cast on them: the name is a
    /// tooltip, and there is nothing to hover with.
    /// </summary>
    public event Action<uint> LookBuff;

    /// <summary>
    /// Your own portrait was tapped: target yourself.
    ///
    /// `OnHeadMouseClick` sets `Data.TargetID` to your own id on a left
    /// click and looks at you on a right one, both guarded by
    /// `ObjectID.IsValid`. Self-targeting is otherwise unreachable
    /// here - you cannot tap yourself in first person - so every
    /// self-cast through the target row had nothing to aim at.
    /// </summary>
    public event Action SelfTarget;

    readonly List<Button> _buffs = new List<Button>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _buffSignature = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _head = new Button
        {
            Flat = true,
            IconAlignment = HorizontalAlignment.Center,
            Visible = false,
            Name = "selfPortrait",
        };
        _head.Pressed += () => SelfTarget?.Invoke();
        AddChild(_head);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_head == null) return;
        _head.Position = new Vector2(Margin, Margin + TopReserve);
        _head.Size = new Vector2(HeadSize, HeadSize);
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

        if (me.AppearanceHash == _shown && _head.Icon != null) return;
        _shown = me.AppearanceHash;

        try
        {
            Tex t = M59Compose.Icon(me, HeadSize, (byte)KnownHotspot.HEAD);
            _head.Icon = M59Assets.FromTex(t);
            _head.Visible = _head.Icon != null;
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
        // Resolution state is in the signature as well as the id: a
        // buff whose sprite has not been resolved yet is skipped below,
        // and on an id-only signature it would stay skipped for ever.
        foreach (ObjectBase b in data.AvatarBuffs)
            sb.Append(b?.ID).Append(b?.Resource != null ? "+" : "-").Append(';');
        string now = sb.ToString();
        if (now == _buffSignature) return;
        _buffSignature = now;

        int used = 0;
        foreach (ObjectBase b in data.AvatarBuffs)
        {
            if (b?.Resource == null) continue;
            uint id = b.ID;

            Button icon = TakeBuff(used);
            icon.Icon = BuffIcon(b);
            icon.TooltipText = b.Name;
            icon.Position = new Vector2(Margin + used * (BuffSize + 8f), Margin + TopReserve + HeadSize + 6f);
            icon.Size = new Vector2(BuffSize + 6f, BuffSize + 6f);
            icon.Visible = icon.Icon != null;

            // Slots are reused as the list changes, so the old handler
            // has to go or a tap looks at whatever was in that position
            // before.
            foreach (Godot.Collections.Dictionary c in icon.GetSignalConnectionList(BaseButton.SignalName.Pressed))
                icon.Disconnect(BaseButton.SignalName.Pressed, (Callable)c["callable"]);
            icon.Pressed += () => LookBuff?.Invoke(id);

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

    Button TakeBuff(int index)
    {
        while (_buffs.Count <= index)
        {
            var b = new Button
            {
                Flat = true,
                IconAlignment = HorizontalAlignment.Center,
                Visible = false,
                Name = $"buff{_buffs.Count}",
            };
            AddChild(b);
            _buffs.Add(b);
        }
        return _buffs[index];
    }

    void HideBuffs(int from)
    {
        for (int i = from; i < _buffs.Count; i++) _buffs[i].Visible = false;
    }
}
