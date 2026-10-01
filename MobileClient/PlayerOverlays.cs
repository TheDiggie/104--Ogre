using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// Your own hands, the weapon in them, the shield on your arm and the
/// spell you are holding ready - the art the server hangs on the front of
/// a first-person view.
///
/// This client drew none of it, which is a bigger hole than it sounds.
/// The overlays are not decoration: they are the only thing on screen that
/// says what you are wielding, whether a shield is up, and that a spell is
/// primed and waiting for a target. The reference would be drawing them
/// every frame you are in the world, because it only stops when the camera
/// leaves first person and this client's camera never does.
///
/// Where the art comes from
/// ------------------------
/// The server sends message 151, one per overlay, and the library keeps
/// them in Data.PlayerOverlays: `DataController.HandlePlayerOverlay`
/// (DataController.cs:2377-2411) adds an overlay, updates one that is
/// already there by id, or - when the hotspot arrives as HOTSPOT_HIDE -
/// removes it (:2380-2381). So the list is the whole truth about what
/// should be on screen, and this reads it rather than tracking adds and
/// removes itself. The reference does subscribe to the list's
/// ListChanged event (UIPlayerOverlays.cpp:17-18) and keeps one CEGUI
/// window per entry (:64-122), which is the same answer arrived at from
/// the other end; polling suits a client that already walks the data
/// layer once a frame, and it cannot drift out of step with the list the
/// way a pair of event handlers can.
///
/// The model carries what a picture needs and nothing about where it goes
/// except one byte: PlayerOverlay is an ObjectBase - resource, colour
/// translation, animation, sub-overlays - plus RenderPosition, a
/// PlayerOverlayHotspot (PlayerOverlay.cs:97-104). The resource is already
/// loaded by the time it lands, because the message enrichment resolves
/// and decompresses it on the way in (MessageEnrichment.cs:609-613).
///
/// Animation is free. `DataController.Tick` ticks every overlay
/// (DataController.cs:1093-1099) exactly as it ticks room objects, and
/// drops one whose ONCE animation has finished with a final group of zero
/// (:1104-1115) - a spell that has gone off puts itself away. Ticking
/// changes the object's appearance hash, and the hash is what this keys
/// its composed art on, so the art follows the animation without anything
/// here knowing that an animation exists. The reference gets there the
/// same way, through ImageComposer's AppearanceChanged handler
/// (ImageComposer.cs:142-149).
///
/// Where it goes on screen
/// -----------------------
/// `GetScreenPositionForHotspot` (UIPlayerOverlays.cpp:179-235) is a pair
/// of three-way switches: the hotspot's compass letters pick left, centre
/// or right for x and top, middle or bottom for y, and the picture is
/// placed flush against the edges it names. West is x = 0, east is
/// width - w, north is y = 0, south is height - h, and anything without a
/// letter in that axis is centred in it. HOTSPOT_CENTER is centred both
/// ways. That is <see cref="Place"/>, and it is the whole rule.
///
/// The size is the composed art's own pixel size times one scale factor,
/// and the factor is as blunt as it looks: `scale = window width / 800`
/// (:8, recomputed on resize at :254), with the comment "images are so
/// small for big renderwindows... let's scale them all up a bit". So yes,
/// it scales with the viewport, linearly in width alone - a taller window
/// does not grow the hands. A phone held upright is narrow, so the same
/// arithmetic makes the hands small; that is the reference's answer for a
/// narrow window and it is kept rather than second-guessed, with <see
/// cref="ScaleBias"/> left for anyone who wants to argue with it.
///
/// The pixel geometry is mirrored exactly rather than approximated,
/// because the CEGUI layout this depends on is in the tree and can be
/// read: the window type is TaharezLook/StaticImage (Constants.h:313),
/// the code turns its frame and background off (:75-76), and with no
/// frame the image is drawn by the NoFrameImage state through the
/// image_noframe section, which declares no area of its own and so covers
/// the whole widget, with VertFormatting and HorzFormatting at their
/// declared default of Stretched
/// (TaharezLook.looknfeel:2562-2563, :2641-2648, :2728-2738). A window
/// sized to dimension * scale with the image stretched over all of it is
/// a uniform upscale of the composed picture, which is a TextureRect with
/// StretchMode Scale.
///
/// What is left out, and why
/// -------------------------
/// `ControllerInput.cpp:888` hides the overlays when the camera pulls out
/// of first person and :900 shows them again when it snaps back. There is
/// no third-person camera here, so the overlays are always shown - the
/// same branch, with the condition known at compile time. It is the same
/// reason NameTags does not hide your own name.
///
/// Draw order between overlays follows the reference's, which is stranger
/// than a plain z-order: each new window is sent to the back of the whole
/// GUI root as it is created (UIPlayerOverlays.cpp:117-118), so a later
/// overlay ends up behind an earlier one. The list is therefore walked
/// backwards into a pool of children whose order is fixed, which puts
/// entry 0 on top. It rarely shows - a hand and a shield sit in opposite
/// corners - but when two overlays do share a hotspot the reference has
/// an answer and this has the same one.
/// </summary>
public partial class PlayerOverlays : Control
{
    /// <summary>Prints what it composed and where it put it, for the harnesses.</summary>
    [Export] public bool Verbose = false;

    /// <summary>
    /// The window width the reference's scale factor is relative to
    /// (UIPlayerOverlays.cpp:8). Not a tuning knob - changing it makes
    /// this client's hands a different size from every other client's.
    /// </summary>
    const float ReferenceWidth = 800f;

    /// <summary>
    /// A multiplier on top of the reference's scale, for a phone. One is
    /// the reference exactly and is the default; it is here because the
    /// reference's rule is width-only and a phone in portrait is narrow
    /// enough that somebody may well want to argue with the result.
    /// </summary>
    [Export] public float ScaleBias = 1.0f;

    readonly List<TextureRect> _pool = new List<TextureRect>();

    /// <summary>
    /// Composed art, keyed on the overlay's appearance hash - the same
    /// key ImageComposer's own cache uses (ImageComposer.cs:180). The
    /// hash changes when anything that affects the picture does, which
    /// includes the animation frame, so an animating overlay cycles
    /// through a handful of entries rather than recomposing a picture a
    /// frame.
    /// </summary>
    readonly Dictionary<uint, ImageTexture> _art = new Dictionary<uint, ImageTexture>();

    /// <summary>Hashes already known to compose to nothing, so they are not retried each frame.</summary>
    readonly HashSet<uint> _failed = new HashSet<uint>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Your own hand is not a button. Every rect below is Ignore too:
        // the reference goes out of its way to say so, setting mouse pass
        // through on each overlay window and disabling it
        // (UIPlayerOverlays.cpp:77-81), because a weapon across the
        // bottom of the screen would otherwise eat every tap that landed
        // on it.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Reads Data.PlayerOverlays and places every one of them. Called
    /// once a frame, after the library's own tick has advanced their
    /// animations.
    /// </summary>
    public void Sync(DataController data)
    {
        // A Control hung off a CanvasLayer is laid out by nothing, so its
        // anchors resolve against no rect and it reports a size of zero -
        // and a child positioned inside zero is a child nobody sees. The
        // rect is taken from the viewport each frame instead, which also
        // makes it follow a rotation or a resize the way the reference
        // does in WindowResized (UIPlayerOverlays.cpp:249-272). Same
        // reasoning, and the same shape, as ScreenEffects.Sync.
        Vector2 view = GetViewportRect().Size;

        // The interface's layer is scaled and shifted to keep controls
        // off the curve of the glass (see SafeArea), and the overlays do
        // not belong to the interface: the reference measures them
        // against the GUI root, which is the whole render window, and its
        // compositors and its 3D viewport cover all of it. This layer is
        // its own and carries no such transform, but undoing one that is
        // there costs nothing and means the overlays still cover the
        // screen if this is ever moved onto the interface's layer.
        Vector2 scale = Vector2.One, offset = Vector2.Zero;
        if (GetParent() is CanvasLayer layer) { scale = layer.Scale; offset = layer.Offset; }
        if (scale.X <= 0f) scale.X = 1f;
        if (scale.Y <= 0f) scale.Y = 1f;

        Position = new Vector2(-offset.X / scale.X, -offset.Y / scale.Y);
        Size = new Vector2(view.X / scale.X, view.Y / scale.Y);

        if (data?.PlayerOverlays == null) { Park(0); return; }

        // scale = renderwindow width / 800 (UIPlayerOverlays.cpp:8, :254).
        float k = Size.X / ReferenceWidth * (ScaleBias > 0f ? ScaleBias : 1f);

        int used = 0;
        // Backwards: see the note on draw order above. Indexed rather
        // than foreach because the order is the point.
        for (int i = data.PlayerOverlays.Count - 1; i >= 0; i--)
        {
            PlayerOverlay ov = data.PlayerOverlays[i];
            if (ov == null) continue;

            // A HIDE hotspot should never reach the list - the data
            // controller takes it as the remove instruction it is
            // (DataController.cs:2380-2381) - but the byte comes off the
            // wire and the switches in Place have no case for it, so a
            // stray one would land in the top-left corner rather than
            // being skipped. Cheaper to say so than to wonder.
            if (ov.RenderPosition == PlayerOverlayHotspot.HOTSPOT_HIDE) continue;

            ImageTexture tex = Art(ov);
            if (tex == null) continue;

            float w = k * tex.GetWidth();
            float h = k * tex.GetHeight();
            if (w < 1f || h < 1f) continue;

            TextureRect r = Take(used++);
            r.Texture = tex;
            r.Size = new Vector2(w, h);
            r.Position = Place(ov.RenderPosition, Size, w, h);
            r.Visible = true;

            if (Verbose)
                GD.Print($"[PlayerOverlays] {ov.ID} {ov.RenderPosition} " +
                         $"{tex.GetWidth()}x{tex.GetHeight()} -> {r.Size} at {r.Position}");
        }

        Park(used);
    }

    /// <summary>
    /// The placement rule, whole:
    /// `ControllerUI::PlayerOverlays::GetScreenPositionForHotspot`
    /// (UIPlayerOverlays.cpp:179-235).
    ///
    /// The reference converts its absolute offset into a fraction of the
    /// root and then zeroes the offset (:227-232), which is the same
    /// pixel position expressed so that it survives a resize on its own.
    /// This is recomputed from the live viewport every frame instead, so
    /// the conversion has nothing to do and is left out - the pixels land
    /// in the same place.
    /// </summary>
    static Vector2 Place(PlayerOverlayHotspot at, Vector2 screen, float w, float h)
    {
        float x;
        switch (at)
        {
            // West edge: flush left (:186-190).
            case PlayerOverlayHotspot.HOTSPOT_NW:
            case PlayerOverlayHotspot.HOTSPOT_W:
            case PlayerOverlayHotspot.HOTSPOT_SW:
                x = 0f;
                break;

            // East edge: flush right (:192-196).
            case PlayerOverlayHotspot.HOTSPOT_NE:
            case PlayerOverlayHotspot.HOTSPOT_E:
            case PlayerOverlayHotspot.HOTSPOT_SE:
                x = screen.X - w;
                break;

            // No east or west in the name: centred horizontally (:198-202).
            default:
                x = 0.5f * (screen.X - w);
                break;
        }

        float y;
        switch (at)
        {
            // North: flush to the top (:208-212).
            case PlayerOverlayHotspot.HOTSPOT_NW:
            case PlayerOverlayHotspot.HOTSPOT_N:
            case PlayerOverlayHotspot.HOTSPOT_NE:
                y = 0f;
                break;

            // South: flush to the bottom (:214-218).
            case PlayerOverlayHotspot.HOTSPOT_SW:
            case PlayerOverlayHotspot.HOTSPOT_S:
            case PlayerOverlayHotspot.HOTSPOT_SE:
                y = screen.Y - h;
                break;

            // No north or south in the name: centred vertically (:220-224).
            default:
                y = 0.5f * (screen.Y - h);
                break;
        }

        return new Vector2(x, y);
    }

    /// <summary>
    /// The composed picture for an overlay, from the cache if it is
    /// there. See <see cref="M59Compose.Overlay"/> for what settings the
    /// reference composes these with.
    /// </summary>
    ImageTexture Art(PlayerOverlay ov)
    {
        uint key = ov.AppearanceHash;
        if (_art.TryGetValue(key, out ImageTexture cached)) return cached;
        if (_failed.Contains(key)) return null;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Overlay(ov)); }
        catch (Exception e) { GD.PrintErr($"[PlayerOverlays] {ov.ID}: {e.Message}"); }

        // A hash that composes to nothing is remembered as nothing rather
        // than retried: an overlay whose art will not decode is still in
        // the list every frame, and composing it sixty times a second to
        // fail sixty times is a frame rate problem with no upside.
        if (tex == null) { _failed.Add(key); return null; }

        // One overlay's animation is a handful of frames and a player
        // carries a handful of overlays, so this stays small in normal
        // play. It is still bounded: a long session of swapping weapons
        // would otherwise accumulate every picture ever held.
        if (_art.Count > 64) { _art.Clear(); _failed.Clear(); }

        _art[key] = tex;
        return tex;
    }

    /// <summary>
    /// The pool of rects, in a fixed child order so that draw order is
    /// decided by which slot an overlay is put in rather than by when its
    /// texture last changed.
    /// </summary>
    TextureRect Take(int index)
    {
        while (_pool.Count <= index)
        {
            var r = new TextureRect
            {
                // Sized and placed explicitly every frame, so the rect
                // must not resize itself around its texture.
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                // The stretch the looknfeel asks for: the image fills the
                // widget, both axes, aspect not preserved. See the class
                // note - it is only ever a uniform upscale in practice,
                // because the rect is sized from the picture.
                StretchMode = TextureRect.StretchModeEnum.Scale,
                // The art is drawn to be pixels. Smoothing an upscaled
                // hand makes it a smear, and the reference's CEGUI
                // texture is point-filtered.
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            AddChild(r);
            _pool.Add(r);
        }
        return _pool[index];
    }

    /// <summary>Hides the slots nothing was put in this frame.</summary>
    void Park(int from)
    {
        for (int i = from; i < _pool.Count; i++) _pool[i].Visible = false;
    }
}
