using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Where the player rearranges their HUD.
///
/// WHY IT LOOKS LIKE THIS. PUBG Mobile, Call of Duty Mobile and Mobile
/// Legends all ship the same editor, and they ship the same one because
/// it is the one that works on a phone: FOUR VERBS and nothing else.
/// Drag a thing to move it, a slider for its size, a slider for its
/// transparency, and a switch to hide the ones you never press. More
/// than one saved layout, because the HUD you want in a fight is not the
/// one you want walking to the shop. And a reset, because the quickest
/// way to ruin a HUD is to edit it.
///
/// The three guides all warn about the same three mistakes rather than
/// about the verbs: an overcrowded screen, controls OVERLAPPING each
/// other, and spacing so tight that a fast swipe hits the neighbour.
/// Overlap is the one an editor can actually police, so this one does -
/// any two pieces that intersect are outlined in red with the shared
/// ground filled in, and the bar counts them. It does not refuse the
/// layout: a player who wants the map over the chat may have a reason,
/// and a tool that argues is a tool that gets turned off. It just never
/// lets it happen silently.
///
/// THE UNIT IS A PIECE, not a button - see M59Hud, which owns the model
/// and the file. This file is the finger end of it and holds no state
/// the layout needs; everything it changes it changes on
/// <see cref="M59Hud.Piece"/>, and Done is a call to
/// <see cref="M59Hud.Save"/>.
///
/// HOW THE TOUCHES ARE KEPT APART, which is the hard part of the
/// feature. While this is open it IS the full screen, with
/// MouseFilterEnum.Stop, so:
///
/// - Nothing reaches <c>GameView._UnhandledInput</c>, which is where
///   TouchControls lives. A drag across the glass in edit mode is a
///   handle being moved, never the movement stick and never the look
///   drag, so no ReqMove and no ReqTurn can come out of it. That is
///   the same mechanism that already stops a drag on an open panel
///   from walking the avatar ("A drag that starts on a button or a
///   panel is eaten by that control", notes/harness.md).
/// - Nor do the HUD's own buttons see it: the attack control is UNDER
///   this, so dragging its handle cannot also swing at something.
/// - And the editor's own controls are not draggable pieces, because
///   they are CHILDREN of this node. Godot offers an event to the
///   topmost control first, so the card, the bar and the two slider
///   tracks eat their own presses and <see cref="_GuiInput"/> - the
///   glass - only ever sees what none of them wanted.
///
/// GameView closes the loop on the keyboard and on autorun, neither of
/// which goes through the touch layer: ApplyInput returns early while
/// <see cref="M59Hud.Editing"/>.
///
/// WHAT IT DELIBERATELY IS NOT is a mock-up. The editor draws OVER the
/// live HUD - the real bars with the real numbers, the real hotbar with
/// whatever is bound to it - because a layout judged against grey
/// placeholder boxes is a layout judged against the wrong thing. That
/// is also why the scrim is faint.
/// </summary>
public partial class HudEditor : Control
{
    /// <summary>Raised on Done and on Cancel, so Settings can come back.</summary>
    public event Action Closed;

    public bool IsOpen => Visible;

    // ---- the card ---------------------------------------------------

    Panel _cardBg;
    Label _cardName, _cardWhere;
    Label _sizeCap, _fadeCap, _sizeVal, _fadeVal;
    ProgressBar _sizeBar, _fadeBar;
    Control _sizeHot, _fadeHot;
    Button _sizeLess, _sizeMore, _fadeLess, _fadeMore, _hide, _reset;

    // ---- the bar ----------------------------------------------------

    Panel _barBg, _barBg2;
    Button _fold;
    bool _folded;
    Label _barHint;
    readonly List<Button> _slots = new List<Button>();
    Button _resetAll, _cancel, _done;
    Label _empty;

    // ---- state ------------------------------------------------------

    string _snapshot;
    M59Hud.Piece _picked;

    /// <summary>The piece a finger is currently moving, and the grab.</summary>
    M59Hud.Piece _drag;
    Vector2 _grab;
    bool _dragged;

    /// <summary>Which slider track a finger is on: 0 none, 1 size, 2 fade.</summary>
    int _track;

    /// <summary>
    /// Whether the last drag of the selected piece was refused on each
    /// axis by <see cref="M59Hud.Place"/>'s clamp. Kept so the handle
    /// can SAY it hit the edge; a piece that silently stops following
    /// the finger reads as a bug in the editor.
    /// </summary>
    bool _pinX, _pinY;

    /// <summary>Pairs that intersect, and the ground they share.</summary>
    readonly HashSet<string> _clash = new HashSet<string>();
    readonly List<Rect2> _shared = new List<Rect2>();

    Rect2 _barRect, _cardRect;
    Vector2 _was;

    const float CardW = 440f, CardH = 232f;
    const float BarH = 66f, BarW = 1180f;

    public override void _Ready()
    {
        // The whole point: while this is open it owns every touch on the
        // screen. See the class comment.
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        BuildBar();
        BuildCard();

        _empty = M59Skin.Empty("Nothing on screen to arrange yet.");
        AddChild(_empty);
    }

    // ---- building ---------------------------------------------------

    void BuildBar()
    {
        // TWO plates, not one strip across the top. A single full-width
        // bar sat exactly on the Menu/Map band - a piece that lives at
        // the top centre by design - so the one group a player is most
        // likely to want moved was the one group they could not grab.
        // The slots go left, the two decisions go right, and the centre
        // of the strip is left clear for whatever lives up there.
        _barBg = M59Skin.Window();
        AddChild(_barBg);
        _barBg2 = M59Skin.Window();
        AddChild(_barBg2);

        for (int i = 0; i < M59Hud.Slots; i++)
        {
            int which = i;
            var b = new Button { Text = $"Layout {i + 1}", Name = $"hudSlot{i + 1}" };
            M59Skin.Dress(b, M59Skin.Kind.Secondary);
            b.Pressed += () => Pick(which);
            AddChild(b);
            _slots.Add(b);
        }

        _resetAll = Make("Reset all", "hudResetAll", M59Skin.Kind.Danger, () =>
        {
            M59Hud.ResetAll();
            Follow();
        });

        _barHint = M59Skin.Caption("");
        _barHint.HorizontalAlignment = HorizontalAlignment.Center;
        _barHint.VerticalAlignment = VerticalAlignment.Center;
        _barHint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_barHint);

        _cancel = Make("Cancel", "hudCancel", M59Skin.Kind.Secondary, Cancel);
        _done = Make("Done", "hudDone", M59Skin.Kind.Primary, Done);

        // The bar has to sit somewhere, and on a full HUD every strip of
        // the screen already belongs to a piece - so wherever it goes it
        // covers something the player may want to drag. Rather than pick
        // a victim, it folds: one tap takes the whole bar down to a pill
        // and gives the ground back, and another brings it up. The pill
        // goes to the bottom centre, which is the one place no piece is
        // pinned to and the thumb can reach while the other hand is free.
        _fold = Make("Hide bar", "hudFold", M59Skin.Kind.Secondary, () =>
        {
            _folded = !_folded;
            _fold.Text = _folded ? "Edit bar" : "Hide bar";
            // true: the bar moving is exactly when the card has to look
            // for somewhere else to be.
            Layout(true);
            Follow();
        });
    }

    Button Make(string text, string node, M59Skin.Kind kind, Action pressed)
    {
        var b = new Button { Text = text, Name = node };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void BuildCard()
    {
        _cardBg = M59Skin.Window();
        AddChild(_cardBg);

        _cardName = M59Skin.Title();
        _cardName.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_cardName);

        // Which way the card dodged, said out loud. Without it a card
        // that jumps from one corner to another when the selection
        // changes reads as a glitch rather than as the card getting out
        // of the way.
        _cardWhere = M59Skin.Caption("");
        _cardWhere.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_cardWhere);

        Row("Size", out _sizeCap, out _sizeBar, out _sizeVal, out _sizeHot,
            out _sizeLess, out _sizeMore, "hudSize",
            () => Nudge(-0.05f, true), () => Nudge(0.05f, true), 1);

        Row("Fade", out _fadeCap, out _fadeBar, out _fadeVal, out _fadeHot,
            out _fadeLess, out _fadeMore, "hudFade",
            () => Nudge(-0.05f, false), () => Nudge(0.05f, false), 2);

        _hide = Make("Hide", "hudHide", M59Skin.Kind.Secondary, () =>
        {
            if (_picked == null) return;
            // The one piece that may not be hidden. The menu band carries
            // the Menu control (`Panels.cs:238`, which applies the
            // player's hide to it), and Menu -> Settings -> Interface ->
            // Arrange is the ONLY way into this editor
            // (`GameView.cs:791`; the Settings tile itself lives in the
            // drawer Menu opens). Hiding it takes the door with it, and
            // the choice is saved - so the next launch comes up with no
            // way to undo it and no way back to Settings at all. This is
            // the same rule the store's clamp already keeps for dragging
            // ("a control a player cannot see is a control they cannot
            // drag back", M59Hud.Place): everything else about the band
            // still moves, scales and fades.
            //
            // Guarded here and not only on `Disabled`, because a Godot
            // Button honours an emitted Pressed while disabled
            // (notes/harness.md, "A press proves the node was FOUND").
            if (_picked.Id == Panels.TopId && !_picked.Hidden) { Follow(); return; }
            _picked.Hidden = !_picked.Hidden;
            M59Hud.Touch();
            Follow();
        });

        _reset = Make("Reset", "hudReset", M59Skin.Kind.Danger, () =>
        {
            M59Hud.Reset(_picked);
            Follow();
        });
    }

    /// <summary>
    /// One slider: a caption, a bar, the value, and a minus and a plus.
    ///
    /// The bar is a real slider - press it anywhere and the value goes
    /// there, drag along it and it follows - but it has steppers too, for
    /// the reason <c>OptionsPanel.Slider</c> gives: a track thin enough
    /// to fit a row is not something a thumb can place accurately, and
    /// the last five percent of a size is exactly what a player is
    /// fiddling with by the time they care. The bar itself is
    /// MouseFilter.Ignore and a transparent Control sits over it, so the
    /// press lands on something that is neither the card behind it nor
    /// the glass underneath.
    /// </summary>
    void Row(string cap, out Label caption, out ProgressBar bar, out Label value,
             out Control hot, out Button less, out Button more, string node,
             Action down, Action up, int track)
    {
        caption = M59Skin.Caption(cap);
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(caption);

        bar = M59Skin.Bar();
        bar.MinValue = 0; bar.MaxValue = 1; bar.Value = 1;
        bar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bar);

        value = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        value.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        value.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        AddChild(value);

        int which = track;
        var zone = new Control { MouseFilter = MouseFilterEnum.Stop, Name = node + "Track" };
        zone.GuiInput += e => Track(e, which, zone);
        AddChild(zone);
        hot = zone;

        less = Make("-", node + "Less", M59Skin.Kind.Step, down);
        more = Make("+", node + "More", M59Skin.Kind.Step, up);
    }

    // ---- opening and closing ---------------------------------------

    /// <summary>
    /// Enters edit mode. Takes the snapshot Cancel puts back, and tells
    /// the HUD it is being edited - which is what makes a piece the
    /// client would normally have hidden show itself, so it can be
    /// found and brought back.
    /// </summary>
    public void Open()
    {
        M59Hud.Load();
        _snapshot = M59Hud.Snapshot();
        M59Hud.Editing = true;
        _picked = null;
        _track = 0; _drag = null;
        // ToFront already makes this the last child AND puts an open
        // ConfirmPopup back above it. The MoveChild that used to follow
        // undid the second half: a question waiting for an answer would
        // have ended up under a full-screen editor that eats every
        // touch, which is the one invariant notes/godot-ui.md names -
        // "nothing may end up above an armed ConfirmPopup". A trade
        // offer arriving while the editor is open is enough to reach it.
        Panels.ToFront(this);
        Visible = true;
        M59Hud.Touch();
        Follow();
        Layout(true);
    }

    void Done()
    {
        M59Hud.Save();
        Leave();
    }

    void Cancel()
    {
        M59Hud.Restore(_snapshot);
        Leave();
    }

    void Leave()
    {
        M59Hud.Editing = false;
        _drag = null; _track = 0; _picked = null;
        Visible = false;
        // The pieces were drawn at full opacity while editing (see
        // M59Hud.Dress); this is what puts the player's fades back and
        // takes the hidden ones away again.
        M59Hud.Touch();
        Closed?.Invoke();
    }

    /// <summary>
    /// Switches saved layout.
    ///
    /// <see cref="M59Hud.Use"/> writes the layout being left, which is
    /// the behaviour the reference games have - the slot you were on is
    /// kept whether or not you say Done. So the snapshot is retaken
    /// here: Cancel after a slot switch backs out of what was done
    /// SINCE the switch, which is the only thing it can honestly
    /// promise.
    /// </summary>
    void Pick(int slot)
    {
        if (slot == M59Hud.Slot) return;
        M59Hud.Use(slot);
        _snapshot = M59Hud.Snapshot();
        Follow();
        Layout(true);
    }

    // ---- the four verbs --------------------------------------------

    void Nudge(float by, bool size)
    {
        if (_picked == null) return;
        if (size) _picked.Scale = Mathf.Clamp(_picked.Scale + by, M59Hud.MinScale, M59Hud.MaxScale);
        else _picked.Alpha = Mathf.Clamp(_picked.Alpha + by, M59Hud.MinAlpha, M59Hud.MaxAlpha);
        M59Hud.Touch();
        Follow();
    }

    /// <summary>A press or a drag on one of the two slider tracks.</summary>
    void Track(InputEvent e, int which, Control hot)
    {
        if (_picked == null || hot == null) return;
        if (!Reach(e, out Vector2 at, out int phase)) return;
        if (phase == 2) { _track = 0; return; }
        if (phase == 0) _track = which;
        if (_track != which) return;

        float f = hot.Size.X > 1f ? Mathf.Clamp(at.X / hot.Size.X, 0f, 1f) : 0f;
        if (which == 1) _picked.Scale = M59Hud.MinScale + f * (M59Hud.MaxScale - M59Hud.MinScale);
        else _picked.Alpha = M59Hud.MinAlpha + f * (M59Hud.MaxAlpha - M59Hud.MinAlpha);
        M59Hud.Touch();
        Follow();
        AcceptEvent();
    }

    // ---- the glass -------------------------------------------------

    /// <summary>
    /// Every touch the editor's own controls did not want.
    ///
    /// Absolute, not incremental: the piece is put where the finger is
    /// plus the grab it was picked up by, rather than moved by a delta.
    /// Godot can deliver the same gesture twice - a screen touch and the
    /// mouse event emulated from it - and a delta applied twice moves at
    /// double speed, while an absolute position applied twice is simply
    /// the same position.
    /// </summary>
    public override void _GuiInput(InputEvent e)
    {
        if (!Reach(e, out Vector2 at, out int phase)) return;

        if (phase == 0)
        {
            M59Hud.Piece hit = Under(at);
            if (hit != null)
            {
                if (hit != _picked) { _picked = hit; Follow(); Layout(true); }
                _drag = hit;
                _grab = hit.Rect.Position - at;
                _dragged = false;
                _pinX = _pinY = false;
            }
            else
            {
                _drag = null;
                // A tap on bare glass drops the selection, which is how
                // a player puts the card away without a close button.
                if (_picked != null) { _picked = null; Follow(); Layout(true); }
            }
            AcceptEvent();
            return;
        }

        if (phase == 1 && _drag != null)
        {
            Vector2 want = at + _grab;
            _drag.Offset = want - _drag.Natural.Position;
            _dragged = true;
            // Place() clamps and WRITES THE CLAMP BACK, so comparing
            // what was asked for against what the piece ends up with is
            // how the editor knows the drag hit the edge. Read after the
            // relayout, not before.
            M59Hud.Touch();
            Vector2 got = _drag.Rect.Position;
            _pinX = Mathf.Abs(got.X - want.X) > 0.5f;
            _pinY = Mathf.Abs(got.Y - want.Y) > 0.5f;
            Follow();
            AcceptEvent();
            return;
        }

        if (phase == 2)
        {
            if (_drag != null && _dragged) Layout(true);
            _drag = null;
            AcceptEvent();
        }
    }

    /// <summary>
    /// Which piece a finger landed on: the SMALLEST that contains the
    /// point. A big piece whose rectangle happens to swallow a small one
    /// must not be the one that gets picked up, or the small one is
    /// unreachable wherever it sits.
    /// </summary>
    static M59Hud.Piece Under(Vector2 at)
    {
        M59Hud.Piece best = null;
        float area = float.MaxValue;
        foreach (M59Hud.Piece p in M59Hud.All)
        {
            if (!Drawn(p)) continue;
            Rect2 r = p.Rect;
            if (!r.HasPoint(at)) continue;
            float a = r.Size.X * r.Size.Y;
            if (a < area) { area = a; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Whether a piece has a handle. A piece that has never been through
    /// <see cref="M59Hud.Place"/> has no rectangle to draw one on, and a
    /// zero-by-zero outline somewhere near the origin is worse than
    /// nothing - it is a handle that cannot be hit, over a piece that is
    /// not there.
    /// </summary>
    static bool Drawn(M59Hud.Piece p)
        => p != null && p.Natural.Size.X > 2f && p.Natural.Size.Y > 2f;

    /// <summary>
    /// One shape for the four event types a phone and a harness between
    /// them produce. <paramref name="phase"/> is 0 press, 1 move, 2
    /// release.
    /// </summary>
    static bool Reach(InputEvent e, out Vector2 at, out int phase)
    {
        at = Vector2.Zero; phase = -1;
        switch (e)
        {
            // One finger only. A second finger landing mid-drag must not
            // take the piece somewhere else.
            case InputEventScreenTouch t when t.Index == 0:
                at = t.Position; phase = t.Pressed ? 0 : 2; return true;
            case InputEventScreenDrag d when d.Index == 0:
                at = d.Position; phase = 1; return true;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                at = mb.Position; phase = mb.Pressed ? 0 : 2; return true;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Left) != 0:
                at = mm.Position; phase = 1; return true;
        }
        return false;
    }

    // ---- following the model ---------------------------------------

    /// <summary>Puts the card and the bar where the model is.</summary>
    void Follow()
    {
        bool any = false;
        foreach (M59Hud.Piece p in M59Hud.All) if (Drawn(p)) { any = true; break; }
        if (_picked != null && !Drawn(_picked)) _picked = null;

        bool card = _picked != null;
        _cardBg.Visible = card;
        _cardName.Visible = card; _cardWhere.Visible = card;
        _sizeCap.Visible = card; _sizeBar.Visible = card; _sizeVal.Visible = card;
        _sizeHot.Visible = card; _sizeLess.Visible = card; _sizeMore.Visible = card;
        _fadeCap.Visible = card; _fadeBar.Visible = card; _fadeVal.Visible = card;
        _fadeHot.Visible = card; _fadeLess.Visible = card; _fadeMore.Visible = card;
        _hide.Visible = card; _reset.Visible = card;
        _empty.Visible = !any;

        if (card)
        {
            _cardName.Text = _picked.Name;
            _sizeBar.Value = (_picked.Scale - M59Hud.MinScale) / (M59Hud.MaxScale - M59Hud.MinScale);
            _fadeBar.Value = (_picked.Alpha - M59Hud.MinAlpha) / (M59Hud.MaxAlpha - M59Hud.MinAlpha);
            _sizeVal.Text = $"{_picked.Scale:0.00}x";
            _fadeVal.Text = $"{_picked.Alpha * 100f:0}%";
            // NOT a ToggleMode button, deliberately. A Godot toggle
            // changes state through ButtonPressed and raises `toggled`,
            // not `pressed` - so a toggle here would be a control that a
            // finger works and a scripted run cannot, which is the
            // harness trap in notes/harness.md ("Emitting Pressed does
            // not press a toggle"). It says which way it goes instead.
            _hide.Text = _picked.Hidden ? "Show" : "Hide";
            M59Skin.Dress(_hide, _picked.Hidden ? M59Skin.Kind.Primary : M59Skin.Kind.Secondary);
            // Greyed rather than missing: a verb that is simply absent on
            // one piece reads as a bug in the editor, and the bar says why.
            _hide.Disabled = _picked.Id == Panels.TopId;
        }

        for (int i = 0; i < _slots.Count; i++)
            M59Skin.Pick(_slots[i], i == M59Hud.Slot);

        Clashes();
        _barHint.Text = _clash.Count > 0
            ? $"{_clash.Count / 2} overlap{(_clash.Count / 2 == 1 ? "" : "s")} - the commonest HUD mistake"
            : (_picked != null && _picked.Id == Panels.TopId
                   ? "Menu is the way back here, so this piece cannot be hidden - it still moves and scales."
               : _picked != null ? "Drag to move. Tap the background to deselect."
                                 : "Tap a piece to edit it. Drag it to move it.");
        _barHint.AddThemeColorOverride("font_color",
            _clash.Count > 0 ? M59Skin.Danger : M59Skin.GoldDim);

        QueueRedraw();
    }

    /// <summary>
    /// Which pieces are sitting on each other.
    ///
    /// A one-pixel kiss is not an overlap and flagging it would make the
    /// warning noise, so a shared rectangle has to be at least a thumb
    /// wide on both axes before it counts - and that is also the number
    /// the guides actually complain about: not the geometry, but a swipe
    /// that lands on the neighbour.
    /// </summary>
    void Clashes()
    {
        _clash.Clear();
        _shared.Clear();
        var live = new List<M59Hud.Piece>();
        foreach (M59Hud.Piece p in M59Hud.All)
            if (Drawn(p) && !p.Hidden) live.Add(p);

        for (int i = 0; i < live.Count; i++)
            for (int j = i + 1; j < live.Count; j++)
            {
                Rect2 a = live[i].Rect, b = live[j].Rect;
                Rect2 over = a.Intersection(b);
                if (over.Size.X < Thumb || over.Size.Y < Thumb) continue;
                _clash.Add(live[i].Id);
                _clash.Add(live[j].Id);
                _shared.Add(over);
            }
    }

    /// <summary>What a thumb needs, and the width an overlap has to reach.</summary>
    const float Thumb = 44f;

    // ---- layout -----------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Visible) return;
        // The pieces re-lay themselves out constantly - a bar whose text
        // got longer, a hotbar that gained a button - so the handles are
        // redrawn every frame off the live rectangles rather than cached.
        Vector2 v = GetViewportRect().Size;
        Layout(v != _was);
        _was = v;
        Clashes();
        QueueRedraw();
    }

    void Layout(bool replace)
    {
        Vector2 v = GetViewportRect().Size;
        // BY HAND, not by anchors. This hangs off a CanvasLayer, whose
        // Control children get no rect of their own - the first trap in
        // notes/godot-ui.md - and here it is not merely a drawing bug:
        // a Control with a 0x0 rect fails its own hit test, so the
        // editor came up looking perfect and swallowed nothing, and
        // every tap went through to the HUD underneath.
        Position = Vector2.Zero;
        Size = v;
        float margin = Mathf.Max(16f, Mathf.Min(v.X, v.Y) * 0.04f);

        // The left plate: the three layouts and Reset all.
        float slotW = 118f, actW = 126f;
        float leftW = M59Skin.Pad * 2f + slotW * _slots.Count + 6f * (_slots.Count - 1)
                    + M59Skin.Gap + 132f;
        float rightW = M59Skin.Pad * 2f + actW * 2f + M59Skin.Gap;
        float top = Mathf.Round(margin);

        // Narrow screens have no centre to spare, so the two plates meet
        // rather than overlap: the band is then unreachable from under
        // the bar, which is why the pieces are also draggable by any part
        // of themselves the plates do not cover.
        float lx = Mathf.Round(Mathf.Min(margin, v.X - margin - leftW - rightW));
        float rx = Mathf.Round(Mathf.Max(lx + leftW, v.X - margin - rightW));

        _barBg.Position = new Vector2(lx, top);
        _barBg.Size = new Vector2(leftW, BarH);
        _barBg2.Position = new Vector2(rx, top);
        _barBg2.Size = new Vector2(rightW, BarH);

        // The strip as a whole, for the card's "under the bar" candidates
        // and for the clamp banner. The gap in the middle is not a place
        // to put a card either: a card there would cover the band.
        _barRect = new Rect2(lx, top, rx + rightW - lx, BarH);

        float x = lx + M59Skin.Pad;
        float y = top + (BarH - 44f) * 0.5f;
        foreach (Button b in _slots)
        {
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(slotW, 44f);
            x += slotW + 6f;
        }
        x += M59Skin.Gap - 6f;
        _resetAll.Position = new Vector2(x, y);
        _resetAll.Size = new Vector2(132f, 44f);

        _cancel.Size = new Vector2(actW, 44f);
        _cancel.Position = new Vector2(rx + M59Skin.Pad, y);
        _done.Size = new Vector2(actW, 44f);
        _done.Position = new Vector2(rx + M59Skin.Pad + actW + M59Skin.Gap, y);

        // The hint, and the overlap count it turns into, below both
        // plates rather than between them - the space between them is
        // what the top-centre piece needs.
        _barHint.Position = new Vector2(0f, top + BarH + 6f);
        _barHint.Size = new Vector2(v.X, 30f);

        // Folded, nothing of the bar is on the glass but the pill, so a
        // piece anywhere under it can be grabbed.
        _fold.Size = new Vector2(150f, 44f);
        _fold.Position = _folded
            ? new Vector2(Mathf.Round((v.X - 150f) * 0.5f), Mathf.Round(v.Y - margin - 44f))
            : new Vector2(Mathf.Round((v.X - 150f) * 0.5f), Mathf.Round(top + BarH + 40f));
        _barBg.Visible = _barBg2.Visible = !_folded;
        foreach (Button b in _slots) b.Visible = !_folded;
        _resetAll.Visible = _cancel.Visible = _done.Visible = !_folded;
        _barHint.Visible = !_folded;
        if (_folded) _barRect = new Rect2(_fold.Position, _fold.Size);

        _empty.Position = new Vector2(0f, v.Y * 0.5f - 20f);
        _empty.Size = new Vector2(v.X, 40f);

        if (_picked != null && (replace || _cardRect.Size.X < 1f))
            _cardRect = Dodge(v, margin);
        if (_picked != null) PlaceCard(_cardRect);
    }

    /// <summary>
    /// Where the control card goes: anywhere the piece being edited is
    /// not.
    ///
    /// This is the one piece of arithmetic in the editor that matters,
    /// because the obvious answer - a card beside the thing, or a card
    /// at the bottom - is wrong for the case that actually happens. A
    /// piece dragged to the MIDDLE of a landscape screen has nothing
    /// beside it on the short axis and the bottom is where the hotbar
    /// lives; a card pinned anywhere fixed will sooner or later cover
    /// the very thing whose size the player is trying to judge.
    ///
    /// So six places are tried - the three heights down each side,
    /// under the bar - and the one that covers the least of the selected
    /// piece wins, with the least of EVERY other piece as the
    /// tie-break, and distance from the piece breaking that. Six is
    /// enough that on a 1920x1080 screen a centred piece half the
    /// screen wide still leaves a clear one.
    /// </summary>
    Rect2 Dodge(Vector2 v, float margin)
    {
        Rect2 keep = _picked.Rect;
        // Under the bar - but the bar is only up there while it is up.
        // Folded, _barRect is the pill at the BOTTOM of the screen, and
        // taking a floor from it put every candidate off the bottom edge
        // and left the card half off the glass.
        float top = _folded ? margin : _barRect.Position.Y + _barRect.Size.Y + M59Skin.Gap;
        float low = v.Y - margin - CardH;
        float[] xs = { margin, v.X - margin - CardW };
        float[] ys = { top, Mathf.Round((v.Y - CardH) * 0.5f), low };

        Rect2 best = new Rect2(xs[0], ys[0], CardW, CardH);
        float bestScore = float.MaxValue;
        foreach (float cx in xs)
            foreach (float cy in ys)
            {
                // Clamped rather than skipped: a short screen can leave
                // no candidate at all above the floor, and no candidate
                // means the card falls back to a default that may be off
                // the glass - which is worse than a card slightly high.
                var r = new Rect2(cx, Mathf.Min(Mathf.Max(cy, top), Mathf.Max(top, low)), CardW, CardH);
                Rect2 on = r.Intersection(keep);
                float score = on.Size.X * on.Size.Y * 8f;
                foreach (M59Hud.Piece p in M59Hud.All)
                {
                    if (!Drawn(p) || p == _picked) continue;
                    Rect2 o = r.Intersection(p.Rect);
                    score += o.Size.X * o.Size.Y;
                }
                // Far from the piece, all else equal: a card that hugs
                // what it is editing still reads as being in the way.
                score -= (r.GetCenter() - keep.GetCenter()).Length();
                if (score < bestScore) { bestScore = score; best = r; }
            }

        _cardWhere.Text = best.Position.X < v.X * 0.5f ? "moved left, out of the way"
                                                       : "moved right, out of the way";
        return best;
    }

    void PlaceCard(Rect2 c)
    {
        _cardBg.Position = c.Position;
        _cardBg.Size = c.Size;

        float x = c.Position.X + M59Skin.Pad;
        float w = c.Size.X - M59Skin.Pad * 2f;
        float y = c.Position.Y + 8f;

        _cardName.Position = new Vector2(x, y);
        _cardName.Size = new Vector2(w, 34f);
        y += 32f;
        _cardWhere.Position = new Vector2(x, y);
        _cardWhere.Size = new Vector2(w, 20f);
        y += 24f;

        y = SliderRow(x, y, w, _sizeCap, _sizeBar, _sizeVal, _sizeHot, _sizeLess, _sizeMore);
        y = SliderRow(x, y, w, _fadeCap, _fadeBar, _fadeVal, _fadeHot, _fadeLess, _fadeMore);

        y += 4f;
        float half = (w - M59Skin.Gap) * 0.5f;
        _hide.Position = new Vector2(x, y);
        _hide.Size = new Vector2(half, 46f);
        _reset.Position = new Vector2(x + half + M59Skin.Gap, y);
        _reset.Size = new Vector2(half, 46f);
    }

    float SliderRow(float x, float y, float w, Label cap, ProgressBar bar, Label val,
                    Control hot, Button less, Button more)
    {
        const float capW = 52f, stepW = 46f, valW = 62f, h = 44f;
        cap.Position = new Vector2(x, y);
        cap.Size = new Vector2(capW, h);

        float bx = x + capW + M59Skin.Gap;
        float bw = w - capW - valW - stepW * 2f - M59Skin.Gap * 4f;
        bar.Position = new Vector2(bx, y + (h - 12f) * 0.5f);
        bar.Size = new Vector2(bw, 12f);
        // The track a finger gets is the full row height, not the twelve
        // points the bar is drawn at - the drawn thickness of a slider
        // and the size of its target are different numbers.
        hot.Position = new Vector2(bx, y);
        hot.Size = new Vector2(bw, h);

        val.Position = new Vector2(bx + bw + M59Skin.Gap, y);
        val.Size = new Vector2(valW, h);

        float sx = bx + bw + M59Skin.Gap + valW + M59Skin.Gap;
        less.Position = new Vector2(sx, y);
        less.Size = new Vector2(stepW, h);
        more.Position = new Vector2(sx + stepW + M59Skin.Gap, y);
        more.Size = new Vector2(stepW, h);

        return y + h + M59Skin.Gap;
    }

    // ---- drawing ----------------------------------------------------

    public override void _Draw()
    {
        Vector2 v = GetViewportRect().Size;

        // Faint, on purpose: the HUD underneath is the real one and the
        // player is judging it, not the editor.
        DrawRect(new Rect2(Vector2.Zero, v), new Color(0f, 0f, 0f, 0.3f), true);

        Font f = GetThemeDefaultFont();

        // The shared ground first, under the outlines, so a red edge
        // still reads as an edge.
        foreach (Rect2 r in _shared)
            DrawRect(r, new Color(M59Skin.Danger.R, M59Skin.Danger.G, M59Skin.Danger.B, 0.22f), true);

        foreach (M59Hud.Piece p in M59Hud.All)
        {
            if (!Drawn(p)) continue;
            Rect2 r = p.Rect;
            bool on = p == _picked;
            bool bad = _clash.Contains(p.Id);

            Color line = p.Hidden ? M59Skin.TextOff
                       : bad ? M59Skin.Danger
                       : on ? M59Skin.GoldBright
                       : M59Skin.Rule;

            DrawRect(r, new Color(line.R, line.G, line.B, on ? 0.10f : 0.05f), true);
            DrawRect(r, line, false, on ? 3f : 2f);

            if (on)
            {
                // Corner ticks, so the selected piece reads as picked up
                // rather than merely brighter.
                float t = Mathf.Min(22f, Mathf.Min(r.Size.X, r.Size.Y) * 0.4f);
                Vector2 a = r.Position, b = r.Position + r.Size;
                DrawLine(a, a + new Vector2(t, 0), M59Skin.GoldBright, 5f);
                DrawLine(a, a + new Vector2(0, t), M59Skin.GoldBright, 5f);
                DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X - t, a.Y), M59Skin.GoldBright, 5f);
                DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X, a.Y + t), M59Skin.GoldBright, 5f);
                DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X + t, b.Y), M59Skin.GoldBright, 5f);
                DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X, b.Y - t), M59Skin.GoldBright, 5f);
                DrawLine(b, b - new Vector2(t, 0), M59Skin.GoldBright, 5f);
                DrawLine(b, b - new Vector2(0, t), M59Skin.GoldBright, 5f);
            }

            string tag = p.Name;
            if (p.Hidden) tag += "  (hidden)";
            if (bad) tag += "  overlapping";
            if (p.Scale != 1f) tag += $"  {p.Scale:0.00}x";
            if (p.Alpha != 1f) tag += $"  {p.Alpha * 100f:0}%";

            float tw = f.GetStringSize(tag, HorizontalAlignment.Left, -1, 16).X;
            var chip = new Rect2(r.Position.X + 2f, r.Position.Y + 2f, tw + 14f, 22f);
            DrawRect(chip, new Color(0.04f, 0.036f, 0.031f, 0.88f), true);
            DrawRect(chip, line, false, 1f);
            DrawString(f, new Vector2(chip.Position.X + 7f, chip.Position.Y + 16f), tag,
                       HorizontalAlignment.Left, -1, 16,
                       p.Hidden ? M59Skin.TextDim : (bad ? M59Skin.Danger : M59Skin.Text));
        }

        // The clamp, made legible. M59Hud.Place keeps a quarter of a
        // piece on the glass whatever the finger does; without this the
        // piece simply stops following and the editor looks broken.
        if (_drag != null && (_pinX || _pinY))
        {
            Rect2 r = _drag.Rect;
            var edge = new Color(M59Skin.Gold.R, M59Skin.Gold.G, M59Skin.Gold.B, 0.9f);
            if (_pinX)
            {
                float ex = r.GetCenter().X < v.X * 0.5f ? 0f : v.X - 4f;
                DrawRect(new Rect2(ex, 0f, 4f, v.Y), edge, true);
            }
            if (_pinY)
            {
                float ey = r.GetCenter().Y < v.Y * 0.5f ? 0f : v.Y - 4f;
                DrawRect(new Rect2(0f, ey, v.X, 4f), edge, true);
            }
            // Said in a FIXED place under the bar, not beside the piece:
            // the moment this message is true the piece is half off the
            // screen, which is exactly where a caption hung off it would
            // be unreadable or gone.
            const string why = "Edge of the screen - a piece is always kept where you can reach it.";
            float ww = f.GetStringSize(why, HorizontalAlignment.Left, -1, 18).X;
            float wx = Mathf.Round((v.X - ww) * 0.5f);
            float wy = _barRect.Position.Y + _barRect.Size.Y + 14f;
            DrawRect(new Rect2(wx - 12f, wy - 2f, ww + 24f, 30f),
                     new Color(0.04f, 0.036f, 0.031f, 0.9f), true);
            DrawRect(new Rect2(wx - 12f, wy - 2f, ww + 24f, 30f), M59Skin.Gold, false, 1f);
            DrawString(f, new Vector2(wx, wy + 20f), why,
                       HorizontalAlignment.Left, -1, 18, M59Skin.Gold);
        }
    }
}
