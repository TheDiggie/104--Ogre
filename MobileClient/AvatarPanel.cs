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
        // The reference repaints a slot from the composer's own
        // NewImageAvailable (`UIAvatar.cpp:172-186`); there is no such
        // callback here, so the signature - and the retry timer below for
        // a sprite that is resolved but not yet composable - is the
        // equivalent.
        int n = 0;
        foreach (ObjectBase b in data.AvatarBuffs)
        {
            if (n++ >= MaxSlots) break;
            sb.Append(b?.ID).Append(b?.Resource != null ? "+" : "-").Append(';');
        }
        sb.Append('@').Append(Columns());
        string now = sb.ToString();
        if (_buffMissed && Time.GetTicksMsec() >= _buffRetryAt) _buffSignature = "";
        if (now == _buffSignature) return;
        _buffSignature = now;
        _buffMissed = false;
        _buffRetryAt = Time.GetTicksMsec() + 500;

        int cols = Columns();
        int used = 0, seen = 0;
        foreach (ObjectBase b in data.AvatarBuffs)
        {
            // Slots past the cap are dropped, as the reference drops them
            // (`UIAvatar.cpp:226-228`, see MaxSlots).
            if (seen++ >= MaxSlots) break;
            if (b?.Resource == null) continue;
            uint id = b.ID;

            // A buff that cannot be drawn yet takes no slot: leaving a
            // hole in the row for an invisible button looked like a gap
            // and, once the art arrived, shifted every icon after it.
            ImageTexture tex = BuffIcon(b);
            if (tex == null) { _buffMissed = true; continue; }

            Button icon = TakeBuff(used);
            icon.Icon = tex;
            icon.TooltipText = b.Name;
            icon.Position = new Vector2(
                Margin + (used % cols) * (BuffSize + 8f),
                Margin + TopReserve + HeadSize + 6f + (used / cols) * (BuffSize + 8f));
            icon.Size = new Vector2(BuffSize + 6f, BuffSize + 6f);
            icon.Visible = true;

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

    /// <summary>
    /// How many enchantments the panel will show at all: the reference's
    /// grid, UI_AVATAR_ENCHANTMENTS_COLS * UI_AVATAR_ENCHANTMENTS_ROWS =
    /// 14 x 2 (`Constants.h:872-873`). Like the room panel's 14 x 1
    /// (RoomBuffsPanel.MaxSlots) the COUNT is the reference's: BuffAdd only
    /// touches a slot when `Enchantments->getChildCount() > Index`
    /// (`UIAvatar.cpp:226-228`), so the 29th enchantment is silently
    /// dropped there too.
    ///
    /// The SHAPE is not. Fourteen 34-pixel icons are about 500 px, fine on
    /// a landscape phone, but the right half of the screen belongs to the
    /// minimap and the room's enchantments, so the row wraps at whatever
    /// fits in the left half (at most the reference's 14) and grows
    /// downward instead. 28 slots then always fit on screen and can never
    /// run off the right edge, which an unbounded single row did past
    /// about 53 enchantments at 1920 wide.
    /// </summary>
    const int MaxSlots = 14 * 2;
    const int MaxColumns = 14;

    int Columns()
    {
        float avail = GetViewportRect().Size.X * 0.5f - Margin;
        int cols = (int)(avail / (BuffSize + 8f));
        return Mathf.Clamp(cols, 1, MaxColumns);
    }

    bool _buffMissed;
    ulong _buffRetryAt;

    ImageTexture BuffIcon(ObjectBase o)
    {
        string key = $"{o.Resource.Filename}:{BuffSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, BuffSize)); }
        catch (Exception e) { GD.PrintErr($"[AvatarPanel] buff {o.Name}: {e.Message}"); }
        // Only a picture is worth keeping, for the reason given at
        // RoomBuffsPanel.Icon:176-182: `M59Compose.Icon` returns null while
        // the bitmap behind a present resource is unreadable, and a cached
        // null was answered for every buff sharing that art for the whole
        // session. The reference repaints from NewImageAvailable
        // (`UIAvatar.cpp:172-186`), so a first miss is always recoverable.
        if (tex != null) _icons[key] = tex;
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
