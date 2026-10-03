using Godot;

/// <summary>
/// A list you scroll by dragging the list, not by finding the bar.
///
/// Godot's ScrollContainer is a mouse control. The bar is a few points
/// wide, it sits hard against the right edge of the body - which is
/// exactly where every row's bind button also is - and the only other
/// way to move the list is a wheel a phone does not have. On a desktop
/// that is fine. Here it meant the pack, the book and the attributes
/// could only be scrolled by landing a thumb on a sliver of bar with a
/// "+" a few points to its left, and missing bound a spell.
///
/// Every mobile list in the world works the other way round: you put a
/// finger anywhere on the content and drag. That is what this adds.
///
/// WHY `_Input` AND NOT `_GuiInput`. The rows are Buttons, and a Button
/// consumes the press that lands on it - so by the time the container
/// would hear about it through the GUI path, the row has already taken
/// it. `_Input` runs for the whole viewport BEFORE the GUI walk, so the
/// drag is seen whatever is under the finger. Nothing is stolen from the
/// row until the finger has actually travelled: under the slop below the
/// event is left completely alone and the tap works as it always did.
/// Past it, the event is marked handled, which is what stops the row
/// firing at the end of a scroll - the failure every list that gets this
/// wrong has, where flicking down casts whatever you started on.
///
/// Flick carries on after the finger leaves, with friction, because a
/// list that stops dead the moment you let go reads as broken.
/// </summary>
public partial class TouchScroll : ScrollContainer
{
    /// <summary>
    /// How far a finger travels before this is a scroll and not a tap.
    ///
    /// Material calls this the touch slop and uses 8dp. A little more
    /// here: a thumb on a phone held in one hand moves further while
    /// pressing than a fingertip on a tablet does, and the cost of
    /// getting it wrong is asymmetric - a scroll misread as a tap casts
    /// a spell, a tap misread as a scroll does nothing.
    /// </summary>
    [Export] public float Slop = 12f;

    /// <summary>Pixels a second, shed per second, after the finger lifts.</summary>
    [Export] public float Friction = 5.5f;

    bool _down, _scrolling;
    float _startY, _lastY, _velocity;
    ulong _lastAt;

    public override void _Ready()
    {
        // Godot's own touch handling would fight this one, and it only
        // works for events the children have not taken - which here is
        // none of them.
        FollowFocus = false;
        SetProcess(true);
        // Dressed here rather than in each panel: every list in the
        // client is one of these, and a bar styled in twenty places is
        // twenty places for the next change to miss one.
        M59Skin.Scroller(this);
        // And kept clear of its own bar, for the same reason. See Fit.
        Resized += Fit;
        ChildEnteredTree += _ => Fit();
        Fit();
    }

    /// <summary>
    /// Keeps the content out from under the bar, for every list, from
    /// here.
    ///
    /// A ScrollContainer lays its child out at its OWN width when the
    /// child expands, bar or no bar, so the last control on a row -
    /// a "+", a checkbox, a slider's end - sat under the grabber. The
    /// first fix was M59Skin.RowsFit, which each panel's Layout had to
    /// remember to call; fourteen did and seven did not, and the
    /// Settings list was one of the seven. Ashton, 2026-10-02: "make
    /// sure no scroll bar in the app covers content." A rule that lives
    /// in each panel is a rule seven panels forget; one that lives in
    /// the container cannot be forgotten, so it moved here. RowsFit is
    /// left in place where it is - it sets the same two things.
    ///
    /// The bar's width is reserved whenever vertical scrolling is
    /// allowed at all, not only when the bar happens to be showing:
    /// a list that reflows its rows the moment it grows past the box
    /// is a list that jumps, and 52 points of margin on a short list
    /// is a cheaper thing to look at than that.
    /// </summary>
    /// <summary>
    /// Off for a list that places its own children and reserves its own
    /// bar - the inventory dock, which draws a slimmer bar and sizes its
    /// box to the slots. Everything else leaves it on.
    /// </summary>
    public bool FitContent = true;

    /// <remarks>
    /// Also run from _Process, and that is not belt-and-braces. A
    /// panel's Layout sets `_scroll.Size` - which raises Resized and
    /// this fit - and on its NEXT line sets the row box's own minimum
    /// width, undoing it; the Settings list did exactly that and the
    /// first photograph after this was written showed a 3-point gap.
    /// Every write below is guarded by a compare, so a frame where
    /// nothing changed costs a few comparisons and no re-sort, and a
    /// frame after a panel's Layout corrects it once and stops.
    /// </remarks>
    void Fit()
    {
        if (!FitContent || VerticalScrollMode == ScrollMode.Disabled) return;
        float w = Size.X - M59Skin.ScrollBarW;
        if (w < 120f) return;
        foreach (Node n in GetChildren())
        {
            if (n is not Control c || c == GetVScrollBar() || c == GetHScrollBar()) continue;
            if (!Mathf.IsEqualApprox(c.CustomMinimumSize.X, w))
                c.CustomMinimumSize = new Vector2(w, c.CustomMinimumSize.Y);
            if (c.SizeFlagsHorizontal != Control.SizeFlags.ShrinkBegin)
                c.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree()) return;

        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Only a press that starts on this list arms it. A press
                // elsewhere that happens to drag across the list must not
                // move it, or dragging a window would scroll whatever it
                // passed over.
                if (!GetGlobalRect().HasPoint(mb.GlobalPosition)) return;
                // A press on the bar itself is the bar's. Armed here, the
                // bar's own drag never arrives - the motion is marked
                // handled below before the GUI walk reaches the grabber -
                // and what the finger gets instead is the content drag,
                // which runs the OTHER way: pulling the grabber down
                // scrolled the list up. Measured on the inventory dock's
                // slim bar, where dragging the grabber is one of the two
                // ways the player was promised; it was true of every
                // panel's bar before that, unnoticed because the list is
                // the way you scroll.
                VScrollBar vbar = GetVScrollBar();
                if (vbar != null && vbar.Visible && vbar.GetGlobalRect().HasPoint(mb.GlobalPosition)) return;
                _down = true;
                _scrolling = false;
                _startY = _lastY = mb.GlobalPosition.Y;
                _velocity = 0f;
                _lastAt = Time.GetTicksMsec();
                return;
            }

            if (!_down) return;
            _down = false;
            // A release that ended a scroll is swallowed, so the row
            // under the finger does not fire. A release that never
            // became a scroll is left alone and the tap lands.
            if (_scrolling) GetViewport().SetInputAsHandled();
            _scrolling = false;
            return;
        }

        if (e is InputEventMouseMotion mm && _down)
        {
            float y = mm.GlobalPosition.Y;
            if (!_scrolling)
            {
                if (Mathf.Abs(y - _startY) < Slop) return;
                _scrolling = true;
                // From where the slop was crossed, not from where the
                // finger went down: otherwise the list jumps by the slop
                // the moment scrolling starts.
                _lastY = y;
            }

            float dy = y - _lastY;
            _lastY = y;
            Move(-dy);

            ulong now = Time.GetTicksMsec();
            float dt = Mathf.Max(0.001f, (now - _lastAt) / 1000f);
            _lastAt = now;
            // Smoothed, so one stuttering frame at the end of a drag does
            // not decide the whole flick.
            _velocity = Mathf.Lerp(_velocity, -dy / dt, 0.5f);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        Fit();
        if (_down || Mathf.Abs(_velocity) < 8f) { if (!_down) _velocity = 0f; return; }
        Move((float)(_velocity * delta));
        _velocity *= Mathf.Exp((float)(-Friction * delta));
    }

    /// <summary>
    /// Scrolls by <paramref name="dy"/>, and gives up the flick at either
    /// end rather than grinding against the stop.
    /// </summary>
    void Move(float dy)
    {
        VScrollBar bar = GetVScrollBar();
        if (bar == null) return;
        float before = ScrollVertical;
        ScrollVertical = (int)Mathf.Round(before + dy);
        if (Mathf.IsEqualApprox(ScrollVertical, before) && !Mathf.IsZeroApprox(dy)) _velocity = 0f;
    }
}
