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

    /// <summary>
    /// The lowest the enchantment row may start, in viewport units.
    ///
    /// The row hangs under the portrait, and the portrait is 72 points
    /// wide while the row is fourteen icons wide - so from the third
    /// icon on it runs out from under the portrait and straight under
    /// the condition bars, which are drawn over it. The view sets this
    /// from Vitals.Bottom, so the row starts below the whole block
    /// rather than below the head alone.
    ///
    /// Zero means "under the head", which is what it was. And it is a
    /// DEFAULT only: once the player has dragged the row somewhere in
    /// the HUD editor this floor is not consulted again - see
    /// BuffNatural.
    /// </summary>
    public float BuffTop
    {
        get => _buffTop;
        set
        {
            if (Mathf.IsEqualApprox(_buffTop, value)) return;
            _buffTop = value;
            // Relaid HERE rather than waiting for the next SyncBuffs.
            // SyncBuffs rebuilds only when the LIST changes, which is
            // what makes it cheap - so the first layout happened before
            // the view had a number to give (Vitals publishes its
            // bottom when it draws, which is after this), and the row
            // then sat at its old place for ever. The bug was invisible
            // with two enchantments and obvious with eight.
            RelayBuffs();
        }
    }
    float _buffTop;

    DataController _data;
    Button _head;
    uint _shown;

    /// <summary>
    /// Two hosts, one per piece. The layout store fades and hides a
    /// piece through the one node it was given (M59Hud.Dress sets
    /// Modulate on it), and Modulate is inherited - so with the buff
    /// buttons under the same root as the head, fading the portrait
    /// faded the enchantments with it, and the row could not be its own
    /// piece however it was registered. The head and its halo live in
    /// one host, the enchantment buttons in the other, and each piece
    /// dresses its own.
    /// </summary>
    Control _faceHost, _buffHost;

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

        _faceHost = new Control { MouseFilter = MouseFilterEnum.Ignore, Name = "faceHost" };
        _faceHost.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_faceHost);
        _buffHost = new Control { MouseFilter = MouseFilterEnum.Ignore, Name = "buffHost" };
        _buffHost.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_buffHost);

        _head = new Button
        {
            Flat = true,
            IconAlignment = HorizontalAlignment.Center,
            Visible = false,
            Name = "selfPortrait",
        };
        _head.Pressed += () => SelfTarget?.Invoke();
        _faceHost.AddChild(_head);
        Tight(_head);
        // The ring that says the latch is on, behind the portrait so the
        // face still reads. See SelfTargeting.
        _aimed = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _faceHost.AddChild(_aimed);
        _faceHost.MoveChild(_aimed, 0);

        GetViewport().SizeChanged += Layout;

        // The portrait is its own piece rather than part of "vitals"
        // because the game keeps them in one panel but they are two
        // controls here, and a player who wants the bars somewhere else
        // usually wants the face where it was. The enchantments used to
        // travel with it as one piece, and the day the row was pushed
        // down to clear the bars and the status row it stopped being
        // movable at all - the floor won every time. So the row is a
        // piece of its own: by default it sits where that floor puts it,
        // and once the player has dragged it their place wins. See
        // M59Hud and BuffNatural.
        M59Hud.Register("portrait", "Portrait", _faceHost);
        M59Hud.Register("buffs", "Enchantments", _buffHost);
        M59Hud.Changed += Layout;
        Layout();
    }

    public override void _ExitTree() => M59Hud.Changed -= Layout;

    /// <summary>
    /// Everything the layout store says about this piece, as one value.
    /// Compared on the per-frame entry point, because a layout LOADED
    /// after _Ready leaves the piece drawn where it used to be and says
    /// nothing about it.
    /// </summary>
    static (M59Hud.Stamp, M59Hud.Stamp) HudStamp()
        // Both pieces in one value: a drag of either has to re-lay the
        // row, because the unmoved row follows the portrait.
        => (M59Hud.StampOf("portrait"), M59Hud.StampOf("buffs"));

    (M59Hud.Stamp, M59Hud.Stamp) _stamp;

    /// <summary>The player's size for a piece, inside the model's band.</summary>
    static float HudScale(string id = "portrait")
    {
        M59Hud.Piece p = M59Hud.Get(id);
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>
    /// Where the portrait actually sits this frame, scale included. The
    /// enchantment row is laid out from here too, so the whole piece
    /// travels together.
    /// </summary>
    Vector2 _at;
    float _head1 = 72f;

    /// <summary>
    /// Takes the theme's padding off an icon-only button, so the control
    /// is exactly as big as the picture it holds.
    ///
    /// A Button's minimum size is its icon PLUS its stylebox's content
    /// margins (4 a side in the default theme), and a control cannot be
    /// laid out smaller than its minimum. So the portrait, composed at
    /// HeadSize square (`M59Compose.Icon` -> `RenderInfo.ScaleToBox`,
    /// `RenderInfo.cs:490-495,583-651`, which pins Dimension to the box
    /// however tall or wide the art is) and laid out at HeadSize, was
    /// quietly grown to HeadSize + 8. Everything below it - the first
    /// enchantment sits at HeadSize + 6 - was positioned for the smaller
    /// number, so the portrait's button ran 2 px over buff0 and its tap
    /// area took the first enchantment's top edge. The picture itself
    /// never left the 72 px it was composed in; the CONTROL did. Each
    /// state's style is copied rather than replaced, so hover and press
    /// still draw as the theme says.
    /// </summary>
    /// <summary>
    /// Whether the next spell is aimed at you - drawn, not just said.
    ///
    /// Self-target here is a MODE and not a target: tapping the
    /// portrait sets DataController.SelfTarget, which SendReqCastMessage
    /// reads to aim at your avatar without touching TargetID
    /// (`BaseClient.cs:1735-1741`), and GameView.SpendSelfTarget clears
    /// it after whatever you send next. The reference needs no indicator
    /// because its version is a key you are physically holding down
    /// (`ControllerInput.cpp:776-778`) - let go and it is over, and your
    /// hand knows.
    ///
    /// A latch has no hand. All this mode had to show for itself was one
    /// line in the chat log, which scrolls, so the honest answer to "am I
    /// still aimed at myself?" was to cast something and find out. That
    /// is the whole of the complaint: the latch DOES clear when you press
    /// Target Next - it goes through HotbarAct with keepLatch false,
    /// which spends it, and a run confirms the "Self-target off." line -
    /// but nothing on screen ever said so, so clearing it and failing to
    /// clear it looked identical.
    ///
    /// So the portrait wears it. Gold ring on, nothing off.
    /// </summary>
    /// <summary>
    /// The halo itself and the bloom around it.
    ///
    /// Warmer and lighter than the skin's Gold, which is a UI colour
    /// meant to sit under text: behind a face it reads as a brown panel
    /// rather than as light. This is the classic client's yellow, pulled
    /// back far enough that the portrait still reads against it.
    /// </summary>
    static readonly Color Halo  = new Color(1.00f, 0.84f, 0.32f, 0.90f);
    static readonly Color Bloom = new Color(1.00f, 0.80f, 0.25f, 0.55f);

    public bool SelfTargeting
    {
        set
        {
            if (_selfAimed == value) return;
            _selfAimed = value;
            if (_aimed != null) _aimed.Visible = value && _head != null && _head.Visible;
        }
    }
    bool _selfAimed;
    Panel _aimed;

    static void Tight(Button b)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            StyleBox src = b.GetThemeStylebox(state);
            if (src == null) continue;
            var s = (StyleBox)src.Duplicate();
            s.ContentMarginLeft = s.ContentMarginRight = 0f;
            s.ContentMarginTop = s.ContentMarginBottom = 0f;
            b.AddThemeStyleboxOverride(state, s);
        }
    }

    void Layout()
    {
        if (_head == null) return;

        // THE NATURAL RECT - where the designer put the portrait - handed
        // to the store, which answers with where the player dragged it,
        // clamped onto the screen. The square is scaled first, so the
        // rect the editor draws a handle over is the picture's own.
        float sc = HudScale();
        _head1 = HeadSize * sc;
        Rect2 at = M59Hud.Place("portrait",
            new Rect2(Margin, Margin + TopReserve, _head1, _head1),
            GetViewportRect().Size);
        _at = at.Position;
        _head.Position = _at;
        _head.Size = at.Size;
        if (_aimed != null)
        {
            // Sized to the face and sat BEHIND it, which is what makes
            // this a glow rather than a border. The portrait art is a
            // composed head on transparency, so a lit panel underneath
            // shows through everywhere the head is not - the gold ends
            // up around the silhouette, following the hair and the
            // shoulders, exactly as the classic client's halo does.
            // A border drawn on top would instead trace the rectangle,
            // which is the version this replaced.
            float bleed = 5f * HudScale();
            _aimed.Position = _at - new Vector2(bleed, bleed);
            _aimed.Size = at.Size + new Vector2(bleed * 2f, bleed * 2f);
            var glow = new StyleBoxFlat
            {
                BgColor = Halo,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                // The bloom. StyleBoxFlat's shadow is drawn OUTSIDE the
                // box and fades, which is the cheapest honest glow Godot
                // has - no shader, no extra texture, and it scales with
                // the piece because the size is figured from HudScale.
                ShadowColor = Bloom,
                ShadowSize = Mathf.RoundToInt(10f * HudScale()),
                AntiAliasing = true,
            };
            _aimed.AddThemeStyleboxOverride("panel", glow);
        }
        // The player's own hide is ANDed under the client's: the view
        // sets Visible on this root every frame out of the world, so the
        // hide lands on the host under it. Shown again here before Dress
        // takes it away, because Dress only ever hides.
        bool show = M59Hud.Shows("portrait");
        _faceHost.Visible = true;
        M59Hud.Dress("portrait");
        if (_head.Icon != null) _head.Visible = show;
        if (_aimed != null) _aimed.Visible = _selfAimed && _head.Visible;
        RelayBuffs();
    }

    /// <summary>
    /// Where the enchantment row would like to be - the natural rect
    /// handed to the store, which answers with where the player put it.
    ///
    /// TWO ANSWERS, by design. While the player has never dragged the
    /// row (no offset in the store) it sits where the stack says: under
    /// the portrait's actual place, or under the floor the view computed
    /// from the condition bars and the status row, whichever is lower -
    /// which is exactly the frame the client drew before the row was a
    /// piece. The moment it carries an offset, the natural rect is the
    /// designer's fixed base under the DEFAULT portrait position, and
    /// neither the portrait's offset nor the floor is asked again. The
    /// base has to be fixed: an offset is saved to disk and read back
    /// next launch, and a base that moved with the bars would move the
    /// row the player had placed every time the bars changed, which is
    /// the complaint this answers. The editor handles the hand-over
    /// between the two on the first drag - see HudEditor._GuiInput.
    ///
    /// Width and height come from the icons the row actually shows,
    /// wrapped as they will be laid out, so the editor's handle covers
    /// the row and nothing else. With nothing on you there is no row;
    /// in the editor it is given one slot's worth so there is still a
    /// handle to place it by.
    /// </summary>
    Rect2 BuffNatural(float sc, int count)
    {
        float cell = BuffSize * sc + 8f * sc;
        float slot = BuffSize * sc + 6f * sc;
        int cols = Columns();
        int n = count > 0 ? count : (M59Hud.Editing ? 1 : 0);
        int rows = (n + cols - 1) / cols;
        int wide = Mathf.Min(n, cols);
        var size = n > 0
            ? new Vector2((wide - 1) * cell + slot, (rows - 1) * cell + slot)
            : Vector2.Zero;

        M59Hud.Piece p = M59Hud.Get("buffs");
        bool moved = p != null && p.Offset != Vector2.Zero;
        if (moved)
            return new Rect2(Margin, Margin + TopReserve + HeadSize * HudScale() + 6f * HudScale(), size);
        return new Rect2(_at.X, Mathf.Max(_at.Y + _head1 + 6f * HudScale(), BuffTop), size);
    }

    /// <summary>
    /// Puts the enchantments at the piece's current place and size,
    /// without recomposing any art. Called when the layout moves;
    /// SyncBuffs calls it when the list moves.
    /// </summary>
    void RelayBuffs()
    {
        if (_buffHost == null) return;
        int count = 0;
        foreach (Button b in _buffs) if (b.Visible) count++;

        bool show = M59Hud.Shows("buffs");
        _buffHost.Visible = true;
        M59Hud.Dress("buffs");
        if (!show) { HideBuffs(0); BuffBottom = 0f; return; }

        float sc = HudScale("buffs");
        float size = BuffSize * sc;
        int cols = Columns();
        Rect2 at = M59Hud.Place("buffs", BuffNatural(sc, count), GetViewportRect().Size);
        float low = 0f;
        for (int i = 0; i < _buffs.Count; i++)
        {
            if (!_buffs[i].Visible) continue;
            _buffs[i].Position = new Vector2(
                at.Position.X + (i % cols) * (size + 8f * sc),
                at.Position.Y + (i / cols) * (size + 8f * sc));
            _buffs[i].Size = new Vector2(size + 6f * sc, size + 6f * sc);
            low = Mathf.Max(low, _buffs[i].Position.Y + _buffs[i].Size.Y);
        }
        // Only a row still in the top-left stack is published. One the
        // player has carried off somewhere else is not above the log any
        // more, and a log that chased it would be pushed to wherever the
        // row landed - off the bottom of the screen, for a row parked
        // over the hotbar.
        M59Hud.Piece p = M59Hud.Get("buffs");
        BuffBottom = p != null && p.Offset != Vector2.Zero ? 0f : low;
    }

    /// <summary>
    /// The lowest point the enchantment icons reach, or zero when there
    /// are none showing - or when the player has moved the row out of
    /// the stack, see RelayBuffs.
    ///
    /// Published for the same reason Vitals.Bottom and StatusBar.Bottom
    /// are, and found the same way - by looking at a frame. Moving this
    /// row down to clear the condition bars and the status row put it
    /// straight through the connection log, which is the next thing down
    /// the left edge; the log now asks where the row ended rather than
    /// assuming the corner is empty below the status bar. The row wraps
    /// to a second line when there are more enchantments than fit, so
    /// this is measured from the icons themselves and not worked out
    /// from a count.
    /// </summary>
    public float BuffBottom { get; private set; }

    /// <summary>
    /// Rebuilds the portrait when the avatar's appearance changes. The
    /// hash is the library's own - it moves when the frame, the parts or
    /// their colours do - so a walking player does not recompose a face
    /// every frame.
    /// </summary>
    public void Follow(DataController data)
    {
        _data = data;
        // The player's layout, every frame: a drag does not change the
        // appearance hash, so nothing below would move the portrait.
        var stamp = HudStamp();
        if (stamp != _stamp) { _stamp = stamp; Layout(); }

        RoomObject me = data?.AvatarObject;
        if (me == null) { _head.Visible = false; if (_aimed != null) _aimed.Visible = false; return; }
        if (!M59Hud.Shows("portrait")) { _head.Visible = false; if (_aimed != null) _aimed.Visible = false; return; }

        // The composed size is part of what is on screen, so a scale
        // change has to recompose - the hash alone would keep the old,
        // smaller picture stretched into the bigger button.
        uint want = me.AppearanceHash ^ (uint)Mathf.RoundToInt(_head1 * 16f);
        // Visible, not merely unchanged. The two guards above HIDE the
        // portrait without forgetting what is in it, so coming back from
        // either of them lands here with the right picture already
        // composed and the button still switched off - and this used to
        // return on that, leaving a permanent hole beside the vitals
        // bars. Both guards are reachable in an ordinary session: the
        // player can turn the portrait off and on again in the HUD
        // editor, and leaving the world nulls AvatarObject
        // (`Meridian59/Data/DataController.cs:1053`) and the same
        // character walks back in with the same AppearanceHash, so the
        // cache hits. Found by logging out and back in and stacking the
        // two frames: every other piece of the HUD returned and this one
        // did not.
        if (want == _shown && _head.Icon != null) { _head.Visible = true; return; }
        _shown = want;

        try
        {
            Tex t = M59Compose.Icon(me, Mathf.RoundToInt(_head1), (byte)KnownHotspot.HEAD);
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
        if (data?.AvatarBuffs == null || !M59Hud.Shows("buffs")) { HideBuffs(0); BuffBottom = 0f; return; }
        // The icons are composed at the player's size for THIS piece, so
        // a bigger row is bigger pictures and not the same pictures
        // further apart. The pixel size is in the signature so a change
        // of size rebuilds.
        int px = Mathf.Max(8, Mathf.RoundToInt(BuffSize * HudScale("buffs")));

        var sb = Sig.Start();
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
            sb.Opt(b?.ID).Append(b?.Resource != null ? "+" : "-").Append(';');
        }
        sb.Append('@').Append(Columns()).Append('/').Append(px);
        if (_buffMissed && Time.GetTicksMsec() >= _buffRetryAt) _buffSignature = "";
        if (!Sig.Changed(sb, ref _buffSignature)) return;
        _buffMissed = false;
        _buffRetryAt = Time.GetTicksMsec() + 500;

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
            ImageTexture tex = BuffIcon(b, px);
            if (tex == null) { _buffMissed = true; continue; }

            Button icon = TakeBuff(used);
            icon.Icon = tex;
            icon.TooltipText = b.Name;
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
        // One place lays the row out and publishes its bottom. This used
        // to place the icons itself as well, and the copy that forgot
        // the bottom was the one that left the connection log sitting on
        // top of the row.
        RelayBuffs();
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

    ImageTexture BuffIcon(ObjectBase o, int px)
    {
        string key = $"{o.Resource.Filename}:{px}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, px)); }
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
            _buffHost.AddChild(b);
            // Same padding trap as the portrait (see Tight): without
            // this a BuffSize + 6 slot is grown to BuffSize + 8.
            Tight(b);
            _buffs.Add(b);
        }
        return _buffs[index];
    }

    void HideBuffs(int from)
    {
        for (int i = from; i < _buffs.Count; i++) _buffs[i].Visible = false;
    }
}
