using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// The action buttons: the row of spells, skills, items and commands you
/// keep to hand.
///
/// The game has a grid of these - `UIActionButtons.cpp`, twelve across
/// and four down - filled from the player's own configuration and drawn
/// with the item's composed icon. This client had nothing of the sort;
/// the row it did have is a different thing, the actions you can take on
/// whatever you tapped.
///
/// What a button does is not decided here. Each one is an
/// `ActionButtonConfig` in the client's own list, and calling
/// <c>Activate</c> on it is what fires it: `BaseClient` is subscribed and
/// dispatches by type - a spell casts, a skill performs, an item is used,
/// applied or unused, an action runs, an alias runs as a chat command.
/// Reimplementing that dispatch here would be a second copy of five
/// rules, and the one in the library is the one the game uses.
///
/// Twelve by four does not fit a phone, so this shows however many fit
/// in the thumb's reach, the first buttons that are set. The rest are
/// still there in the configuration.
///
/// THE SHAPE, which is the owner's and was photographed before it:
/// a full-width row of small text buttons along the bottom edge, in the
/// one place both thumbs have to be - the left one on the movement
/// stick, the right one dragging to look - so pressing anything meant
/// lifting the thumb you were steering with, and Attack was one text
/// button in a row of eight, indistinguishable from Wave.
///
/// It is a CLUSTER in the bottom right now, which is where every mobile
/// action game of the last ten years puts combat, for the same reasons:
///
///  - one big round ATTACK under the right thumb's resting point, the
///    biggest control in the HUD, so the thing you press most is the
///    thing you cannot miss. It is not a new control - it is the hotbar
///    slot that holds the game's Attack action, drawn large, so the
///    press, the hold-to-swing and the drag-off clear are the same code
///    they always were;
///  - the other bound slots FAN in an arc up and to the left of it, at
///    one thumb-sweep radius, so the hand does not move to reach them;
///  - four UTILITIES sit on a second, inner arc between the primary and
///    the bindings, fixed - none is paged, cleared or saved. From the
///    top: Next, because acquiring a target is part of attacking and it
///    was a tile in the menu drawer, four taps from the fight; Phase,
///    the spell every character has and, in the owner's words, "very
///    important"; the pager; and Door, the only way out of a room,
///    beside Attack. A padlock in the
///    cluster's bottom-right corner locks every seat against the
///    pull-off clear (default locked), and the primary, Door and Phase
///    can never be pulled off at all;
///  - the primary is whatever the player put there. It is Attack until
///    the HotKeys panel's Primary row says otherwise (PrimaryNum);
///  - the bottom LEFT is untouched. That corner belongs to the movement
///    stick, which is a floating one that appears wherever the thumb
///    lands (TouchControls.cs:8-9), so anything drawn there is a
///    gesture eaten;
///  - the middle and the lower middle of the glass carry nothing. That
///    is where the creature you are fighting is standing.
///
/// The numbers are not eyeballed: 44pt (Apple) / 48dp (Material) is the
/// smallest target a thumb hits, separation matters more than size above
/// about 40pt - 8pt between targets at or over 44, 16pt when one is
/// within 80pt of a screen edge - and reach is a curved arc from the
/// thumb's pivot rather than a rectangle, with primary controls in the
/// bottom 40% of the glass. Every rect this file lays out is printed
/// when M59HUDRECTS is set, so the sizes and the gaps can be read off a
/// run rather than guessed at. See <see cref="Cluster"/>.
///
/// THE LOOK. This is where a hand lives in a fight, and it was a row of
/// default Godot buttons - flat grey rectangles that said nothing about
/// what was in them. They are SLOTS now, in the panels' slot dress: the
/// same sunken dark cell the inventory grid uses, so a bound button
/// reads as a thing sitting in a holder rather than as a label. Three
/// states are worth telling apart and each has its own:
///
///  - a slot that holds something has a lit rim and an opaque cell, so
///    the row reads as bindings in holders rather than as captions;
///  - a slot the thumb is ON gets a gold rim and a lifted fill, which
///    matters more here than anywhere else in the client: a held button
///    is REPEATING, and the player needs to see which one is;
///  - the page button is dressed as what it is - a stepper, not another
///    action - so turning the page never looks like casting.
///
/// An empty seat IS drawn now, and that is a change from "only bound
/// slots are drawn". The old reasoning still stands - the grid in the
/// reference shows all forty-eight cells, and chrome laid across the
/// part of the world a thumb is pointing at costs you the game - so the
/// empty seat is a hollow ring at a third of the rim's strength, with no
/// cell, no caption and, crucially, no INPUT: it is a Panel with the
/// mouse filter on ignore, so a tap aimed through it reaches the world
/// and targets what is behind it, exactly as it did when nothing was
/// drawn there at all. A bound slot has the lit rim and the opaque cell
/// and so reads as holding something; an empty one does not shout.
///
/// None of that touches what a press DOES: the hold-to-repeat rules
/// below, the drag-off clear and the dispatch are all unchanged.
/// </summary>
public partial class ActionButtons : Control
{
    [Export] public int FontSize = 18;
    [Export] public int IconSize = 56;
    /// <summary>An arc seat's diameter. Twice the 48dp floor.</summary>
    [Export] public int ButtonSize = 96;
    /// <summary>
    /// The primary's diameter. It is not "a bit bigger": the research
    /// every action game converges on is ONE control that is obviously
    /// the one, and at 160 it is two thirds again the size of the slots
    /// around it and nothing else in the HUD comes close.
    /// </summary>
    [Export] public int AttackSize = 160;

    /// <summary>
    /// Pixels at the bottom already spoken for. Setting it relays out:
    /// the row is rebuilt from a signature, and a reserve that changed
    /// without touching the signature used to leave the buttons where
    /// they were - which is how they ended up drawn over the menu row.
    /// </summary>
    public float BottomReserve
    {
        get => _reserve;
        set
        {
            if (Mathf.IsEqualApprox(_reserve, value)) return;
            _reserve = value;
            _signature = "";
        }
    }
    float _reserve;

    /// <summary>
    /// Pixels at the left already spoken for - the chat block, when the
    /// screen is wide enough that the chat sits beside the hotbar
    /// rather than under it. Setting it relays out, the same way
    /// BottomReserve does.
    /// </summary>
    public float LeftReserve
    {
        get => _left;
        set
        {
            if (Mathf.IsEqualApprox(_left, value)) return;
            _left = value;
            _signature = "";
        }
    }
    float _left;

    // ---- the cluster's geometry ------------------------------------
    //
    // One place, because three things have to agree about it: the
    // buttons, the empty rings drawn behind them, and the target block
    // in ActionBar, which sits ABOVE the cluster and would otherwise
    // have to guess where the cluster's ceiling is.

    /// <summary>
    /// How far the primary's own edge sits from the right edge of the
    /// glass. Not a margin for its own sake: a thumb pivots at the
    /// corner of the device, not in it, so the control it rests on wants
    /// to be a thumb's width inboard - and the arc's top seat needs room
    /// to lean right of vertical without falling off the glass.
    /// </summary>
    public const float EdgeRight = 80f;
    /// <summary>Over whatever the view has reserved along the bottom.</summary>
    public const float EdgeBottom = 16f;

    /// <summary>
    /// The radius the arc seats sit at, measured from the primary's
    /// centre - one thumb sweep, and the reason there are five seats and
    /// not eight.
    ///
    /// It is bounded from both sides. Below, by separation: at 240 the
    /// gap between the primary's rim and a seat's rim is 112 points,
    /// where 8 is the floor for targets this size, and shrinking it does
    /// not buy a seat - the seat count is set by the ANGLE each one eats,
    /// which grows as the radius falls. Above, by reach: the top seat's
    /// centre lands 238 points up from the primary, which is 22% of a
    /// 1080-tall glass and keeps the whole cluster inside the bottom 40%
    /// where a primary control belongs. Past about 300 the far seats are
    /// a hand movement, not a sweep, and a hand movement is the thing
    /// this layout exists to remove.
    /// </summary>
    public const float ArcR = 300f;

    /// <summary>
    /// The arc runs from 70 degrees - just right of straight up, which
    /// the <see cref="EdgeRight"/> inset is what makes affordable - round
    /// to 180, straight left. It does not continue below 180: that is
    /// over the bottom edge and, further round, over the movement stick.
    /// </summary>
    public const float ArcFrom = 70f, ArcTo = 180f;

    /// <summary>
    /// Seats on the arc: six bindings, and nothing else.
    ///
    /// SIX, and the arithmetic is the whole argument - the same
    /// arithmetic that said five, run again after the radius moved.
    /// Two adjacent seats must be <see cref="ButtonSize"/> apart plus a
    /// gap, 96 + 16 = 112 points of chord. Six seats over 110 degrees
    /// is a 22-degree step, and a 22-degree step subtends 112 points
    /// only at radius 112 / (2*sin(11deg)) = 293.5. At the old 240 it
    /// subtended 92, which is why this said five and why six was
    /// refused.
    ///
    /// So the radius went to 300 rather than the seat count down to
    /// five: 2*300*sin(11deg) = 114.5 points of chord, an 18.5-point
    /// rim gap, which is slightly MORE room than the five-seat layout
    /// had at 18.1.
    ///
    /// The cost is a bigger cluster: the arc reaches 60 points further
    /// from the primary, so Ceiling rises and the target block sits
    /// higher. That is the trade for a fourth binding on every page.
    ///
    /// Door was the sixth seat, at 180 degrees. It is not on the arc
    /// any more (<see cref="DoorSeat"/>), and the seat it held is the
    /// fifth binding: the owner's words were "add one more to the arc
    /// but move the door button over to the right as i did in the
    /// picture", and in the picture Door sits inside the arc, between
    /// its lower end and the primary. So the arc keeps its six seats,
    /// its radius and its chord - every number above still holds - and
    /// gains a binding without gaining a seat.
    ///
    /// Then Target Next left it too (2026-10-03, "add another hotkey to
    /// the hotkey arc. I want an outer hotkey arc of 6 keys and the
    /// inner arc of utilities 'door, page change, phase, target next'"),
    /// so the six seats are six bindings and the arc holds nothing
    /// fixed. The utilities are a second, inner arc - see below.
    /// </summary>
    public const int ArcSeats = 6;

    /// <summary>
    /// Hotbar seats: every seat on the outer arc. Six, from five: Target
    /// Next moved to the inner arc with Door, Phase and the pager, and
    /// the seat it held at 70 degrees is the sixth binding. A page is
    /// six, and a hotbar.cfg saved under five simply re-pages - Nums
    /// are positions in one long row, and the page is a window on it.
    ///
    /// The four utilities stay off the bindings for the reason they
    /// always were: each is pressed when the thing you want is "take me
    /// through" or "aim at the next one", and not whatever the page
    /// happens to be showing. A control you have to turn a page to
    /// reach is a control you do not use. Phase (<see cref="PhaseSeat"/>)
    /// is fixed in the owner's words: "every player has it and it is
    /// very important".
    /// </summary>
    public const int HotSeats = ArcSeats;

    // ---- the inner arc --------------------------------------------------
    //
    // Four fixed controls sit on a second, smaller arc in the annulus
    // between the primary's rim (80 from the pivot) and the outer seats'
    // inner rims (300 - 48 = 252): from the top going round, Target
    // Next, Phase, the pager and Door. Each is placed by angle and 1x
    // distance from the pivot, as the outer arc is, so they scale and
    // move with it, and each is checked against its neighbours rim to
    // rim below (Measure prints every gap).
    //
    // THE RADIUS is 168 for all four: a 96 seat there is 40 from the
    // disc's rim and 36 from the outer seats' inner rims, both over the
    // ~24 asked for, and a radial gap is a floor on the true one - two
    // centres on different radii are never closer than the radii differ.
    //
    // THE ANGLES are 60, 102, 142, 180, chosen for the chords: at 168 a
    // 42-degree step subtends 120.4 points, so Next and Phase (96 each)
    // keep 24.4 rim to rim; the 40-degree step to the pager (72) keeps
    // 30.9; the 38-degree step from the pager to Door keeps 25.4. Next
    // sits at 60 rather than 70 because the owner asked for it "down a
    // bit": on the inner radius it is 145 above the pivot where it was
    // 282, and its right rim is 28 from the glass. Door stays straight
    // left at 180, beside Attack, where he drew it. Nothing changed
    // on the outer arc, so Ceiling and the cluster's box are what they
    // were.

    /// <summary>The inner arc's radius at 1x. See the section note.</summary>
    const float InnerR = 168f;
    /// <summary>Target Next: the top of the inner arc, "down a bit" from the 70 it had on the outer.</summary>
    const float NextAngle = 60f;
    /// <summary>Phase: over the primary, second from the top.</summary>
    const float PhaseAngle = 102f;
    /// <summary>The pager: the 142-degree seat, between Phase and Door, the smallest thing here.</summary>
    const float TurnAngle = 142f;
    /// <summary>Door: straight left of the primary, the bottom of the inner arc.</summary>
    const float DoorAngle = ArcTo;
    /// <summary>
    /// The padlock's edge. 44 is the tap floor, and that is all it needs
    /// to be: it is pressed once a session, not in a fight.
    /// </summary>
    const float LockSize = 44f;
    /// <summary>
    /// Gap from the primary's bounding box to the padlock's. The lock
    /// sits in the bottom-right corner of the cluster, right of the
    /// primary's disc and bottom-aligned with it: with EdgeRight at 80
    /// and a 44 lock, 20 leaves it 16 from the glass, which is the
    /// edge rule's number. The disc is round, so the real rim gap at
    /// the lock's top edge is 29 and grows below it.
    /// </summary>
    const float LockGap = 20f;

    // ---- the player's size, applied to that geometry ----------------
    //
    // THE ARC STAYS AN ARC because the radius and the buttons scale by
    // the SAME factor. The seat count and the angles above are a
    // statement about a CHORD over a radius - 112 points of chord at 240
    // subtending 26.9 degrees - and multiplying both terms leaves that
    // ratio alone, so five seats still sit where the arithmetic says and
    // the rim-to-rim separation scales with everything else rather than
    // closing up. Scaling the radius alone would crowd the seats; scaling
    // the buttons alone would overlap them.

    /// <summary>The player's size for this cluster, inside the model's band.</summary>
    static float HudScale()
    {
        M59Hud.Piece p = M59Hud.Get("combat");
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>
    /// The smallest anything here may become. Every control in the
    /// cluster is pressed, and the primary is leant on, so the scale's
    /// floor of 0.7 is not allowed to take one under the 44 points a
    /// thumb needs - at the shipped sizes it does not come close (96,
    /// 160 and 72 scale to 67, 112 and 50), so this is a guard rather
    /// than a clamp that fires.
    /// </summary>
    const float TapFloor = 44f;

    /// <summary>The arc's radius at the player's size.</summary>
    float Arc => ArcR * HudScale();
    /// <summary>An arc seat's diameter at the player's size.</summary>
    float Btn => Mathf.Max(TapFloor, ButtonSize * HudScale());
    /// <summary>The primary's diameter at the player's size.</summary>
    float Atk => Mathf.Max(TapFloor, AttackSize * HudScale());
    /// <summary>The page stepper's diameter at the player's size.</summary>
    float Turn => Mathf.Max(TapFloor, TurnSize * HudScale());
    /// <summary>The padlock's edge at the player's size. Never under the tap floor.</summary>
    float Lock => Mathf.Max(TapFloor, LockSize * HudScale());
    /// <summary>The composed icon's edge at the player's size.</summary>
    int IconPx => Mathf.Max(8, Mathf.RoundToInt(IconSize * HudScale()));
    /// <summary>A scaled font size, never small enough to stop being text.</summary>
    static int Pt(int at1, float sc) => Mathf.Max(8, Mathf.RoundToInt(at1 * sc));

    /// <summary>
    /// The player's drag, as the last layout resolved it. Added to the
    /// pivot, so the primary, the arc and the stepper all move together
    /// and the geometry above is untouched by it.
    /// </summary>
    Vector2 _shift;

    /// <summary>The primary's centre, which the whole cluster hangs off.</summary>
    Vector2 Pivot(Vector2 v) => new Vector2(
        v.X - EdgeRight - Atk * 0.5f,
        v.Y - BottomReserve - EdgeBottom - Atk * 0.5f) + _shift;

    /// <summary>
    /// The cluster's own box, before the player has dragged it: the
    /// primary, every arc seat, Door, Phase and the padlock. This is the
    /// natural rect the layout store is handed, and the whole cluster
    /// moves by the difference between it and what comes back. The lock
    /// is in it so the editor's handle and its overlap test cover the
    /// whole thing the player sees as "the buttons".
    /// </summary>
    Rect2 Natural(Vector2 v)
    {
        Vector2 held = _shift;
        _shift = Vector2.Zero;
        Rect2 box = Round(Pivot(v), Atk);
        for (int i = 0; i < ArcSeats; i++) box = box.Merge(Round(Seat(v, i), Btn));
        box = box.Merge(Round(NextSeat(v), Btn)).Merge(Round(DoorSeat(v), Btn))
                 .Merge(Round(PhaseSeat(v), Btn)).Merge(LockRect(v));
        _shift = held;
        return box;
    }

    /// <summary>
    /// The arc seat of binding <paramref name="i"/> on the page. Seat 0
    /// is the top of the outer arc (70 degrees) and seat ArcSeats-1 the
    /// bottom-left (180); bindings fill it from the bottom-left so that
    /// the page reads LEFT TO RIGHT as the HotKeys panel draws it -
    /// Ashton: "i have rest as my first hotkey in the manager but in the
    /// arc it is on the right, make the arc copy the manager left to
    /// right".
    /// </summary>
    Vector2 ArcSeat(Vector2 v, int i) => Seat(v, HotSeats - 1 - i);

    Vector2 Seat(Vector2 v, int i)
    {
        float a = Mathf.DegToRad(ArcFrom + (ArcTo - ArcFrom) * i / (ArcSeats - 1));
        return Pivot(v) + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * Arc;
    }

    /// <summary>A point inside the arc, by angle and 1x distance from the pivot.</summary>
    Vector2 Inside(Vector2 v, float deg, float at)
    {
        float a = Mathf.DegToRad(deg);
        return Pivot(v) + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (at * HudScale());
    }

    /// <summary>Target Next's centre: the top of the inner arc. See NextAngle.</summary>
    Vector2 NextSeat(Vector2 v) => Inside(v, NextAngle, InnerR);

    /// <summary>Door's centre: straight left of the primary, the bottom of the inner arc.</summary>
    Vector2 DoorSeat(Vector2 v) => Inside(v, DoorAngle, InnerR);

    /// <summary>Phase's centre: over the primary, second on the inner arc.</summary>
    Vector2 PhaseSeat(Vector2 v) => Inside(v, PhaseAngle, InnerR);

    /// <summary>
    /// The page stepper's seat: third on the inner arc, between Phase
    /// and Door. It is not ON the outer arc because a seat spent on
    /// turning the page is a binding the player cannot reach, which is
    /// the whole complaint the paging answers.
    /// </summary>
    Vector2 TurnSeat(Vector2 v) => Inside(v, TurnAngle, InnerR);

    /// <summary>
    /// The padlock's rect: bottom-right of the cluster, right of the
    /// primary's disc and bottom-aligned with it. "A padlock icon on the
    /// bottom right of the hotkey ui element" - the owner's words, and
    /// the one corner of the cluster nothing else wanted: the arc leans
    /// up and left, the thumb rests on the disc, and the margin between
    /// the disc and the glass (EdgeRight) was empty.
    /// </summary>
    Rect2 LockRect(Vector2 v)
    {
        Rect2 atk = Round(Pivot(v), Atk);
        float d = Lock;
        return new Rect2(Mathf.Round(atk.End.X + LockGap * HudScale()), Mathf.Round(atk.End.Y - d), d, d);
    }

    /// <summary>A round control's rect, from its centre and diameter.</summary>
    static Rect2 Round(Vector2 centre, float d) => new Rect2(
        Mathf.Round(centre.X - d * 0.5f), Mathf.Round(centre.Y - d * 0.5f), d, d);

    /// <summary>
    /// The top of the cluster in viewport units, as the last layout
    /// actually placed it: nothing else in the HUD may come below it on
    /// the right, and ActionBar's target block sits above it.
    ///
    /// Published rather than computed from the screen, because the
    /// cluster's height depends on <see cref="BottomReserve"/>, which the
    /// view sets and which changes with the chat block - and a target
    /// block that assumed a fraction of the glass would be drawn through
    /// the arc the moment that reserve grew. Static because there is one
    /// hotbar; zero until the first layout, which is the caller's cue to
    /// fall back. ActionBar watches it for changes.
    /// </summary>
    public static float Ceiling { get; private set; }

    DataController _data;
    readonly List<Button> _pool = new List<Button>();
    /// <summary>The hollow rings for seats with nothing in them.</summary>
    readonly List<Panel> _rings = new List<Panel>();
    /// <summary>The button number showing in each screen slot, so a press
    /// can resolve what it fires at the moment it happens.</summary>
    readonly List<int> _nums = new List<int>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _signature = "";
    Meridian59.Data.Lists.ActionButtonList _list;

    /// <summary>
    /// Watches the client's button list, the way `UIActionButtons.cpp:19`
    /// does.
    ///
    /// This is not decoration over the polling below - it is the only
    /// thing that sees most changes. A button is bound to an item, a
    /// spell or a skill by the data controller as those arrive from the
    /// server, and every one of those paths matches the button *by name*
    /// and then calls SetToItem / SetToSpell / SetToSkill. Those set the
    /// type and the name as plain fields and only the data as a property,
    /// so a poll that compares number, type and name sees nothing change
    /// and never redraws - which is exactly the case where the button
    /// finally has an icon to show.
    ///
    /// BaseList re-raises any item's PropertyChanged as an ItemChanged on
    /// the list, so one subscription here covers both the list gaining
    /// and losing buttons and an individual button being rebound.
    /// </summary>
    public void Follow(DataController data)
    {
        if (data?.ActionButtons == null || ReferenceEquals(_list, data.ActionButtons)) return;
        if (_list != null) _list.ListChanged -= OnButtonsChanged;
        _list = data.ActionButtons;
        _list.ListChanged += OnButtonsChanged;
        _signature = "";
    }

    void OnButtonsChanged(object sender, System.ComponentModel.ListChangedEventArgs e) => _signature = "";

    public override void _Ready()
    {
        // ONE piece, and that is the designer's ruling: the arc is
        // computed around the primary and the stepper sits inside it, so
        // the cluster moves and scales as a unit. See M59Hud.
        M59Hud.Register("combat", "Combat buttons", this);
        M59Hud.Changed += OnHudChanged;

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        GetViewport().SizeChanged += () => { _signature = ""; Sync(_data); };
    }

    public override void _ExitTree() => M59Hud.Changed -= OnHudChanged;

    /// <summary>
    /// The player moved something. The cluster is rebuilt from a
    /// signature, so the way to re-lay it is to spend the signature -
    /// exactly as the reserves and the viewport do.
    /// </summary>
    void OnHudChanged() { _signature = ""; Sync(_data); }

    /// <summary>
    /// A kind the hotbar holds that the library has no
    /// <see cref="ActionButtonType"/> for. One member, so far.
    ///
    /// GO IS NOT ONE OF THE FIVE KINDS, and that was worth establishing
    /// before writing a line: it is not an `AvatarAction` - the enum is
    /// eleven members and Go is not among them
    /// (`Meridian59/Common/Enums/AvatarAction.cs:24`), so
    /// `BaseClient.ExecAction` cannot reach it - it is not a user
    /// command, and it is not a chat command either. The one thing in
    /// the library called Go that IS a chat command is the DM teleport,
    /// `ChatCommandType.Go` -> `SendReqDM(DMCommandType.GoRoom, ...)`
    /// (`Meridian59/Client/BaseClient.cs:3116-3118`), which is an
    /// administrator moving himself to a room by number and not a player
    /// walking through a door. Binding Go as an ALIAS would send that.
    ///
    /// What Go actually is: `BaseClient.SendReqGo`
    /// (`Meridian59/Client/BaseClient.cs:1546-1562`), a bare
    /// `ReqGoMessage` with the turn and the move forced out ahead of it,
    /// reached in the reference from one key and nothing else
    /// (`Meridian59.Ogre.Client/ControllerInput.cpp:552-553`). So this
    /// is the second of the two jobs the owner's request could have
    /// been: the hotbar has to learn a new kind.
    ///
    /// It learns it as cheaply as it can. A slot still has to be an
    /// `ActionButtonConfig` in the client's own list, because that list
    /// is what Sync draws, what the pager pages, what the drag-off
    /// clears and what <see cref="HotbarStore"/> writes - a parallel
    /// pseudo-slot would be a second copy of all four. There is no
    /// spare `ActionButtonType`, so a Go slot is the one shape the
    /// library already ignores: type Action with
    /// <c>AvatarAction.None</c> for data. `ExecAction` has no case for
    /// None and falls out of its switch doing nothing
    /// (`BaseClient.cs:3208-3212` and the switch through `:3290`), so
    /// the library's dispatch cannot double-send and
    /// <see cref="Send"/> below is the only thing that fires it.
    ///
    /// None is free, and not merely unused. No library path builds an
    /// Action button holding it: `SetToAction` is only ever called with
    /// a real action, and the one place a name becomes an action -
    /// `ActionButtonConfig.GetAction` - cannot return None, because its
    /// chain of name tests ends in an else that answers Wave
    /// (`ActionButtonConfig.cs:344-346`). So a slot carrying None is
    /// this client's Go and can be nothing else.
    /// </summary>
    public enum Extra
    {
        /// <summary>Through the door. See <see cref="SetToGo"/>.</summary>
        Go,
    }

    /// <summary>The Go slot's caption, and its name on disk.</summary>
    public const string GoName = "Door";

    /// <summary>
    /// Whether a slot is the Go binding.
    ///
    /// On the DATA, not on the name: `Name` is a caption a player never
    /// sets here but which the library would overwrite given the chance
    /// (`ActionButtonConfig.cs:156-157` renames an Action button from
    /// whatever `GetAction` resolved), so the boxed `AvatarAction.None`
    /// is the identity and the name is the label.
    /// </summary>
    public static bool IsGo(ActionButtonConfig cfg) =>
        cfg != null && cfg.ButtonType == ActionButtonType.Action
        && cfg.Data is AvatarAction a && a == AvatarAction.None;

    /// <summary>
    /// Writes the Go marker into a slot.
    ///
    /// Not a library setter, because there is no `SetToGo` to call and
    /// `SetToAction(AvatarAction.None)` would caption the slot "None"
    /// (`ActionButtonConfig.cs:208-215`). `SetToUnset` first for the one
    /// thing only the library can do: drop the PropertyChanged listener
    /// a previously bound item or spell left on the slot
    /// (`ActionButtonConfig.cs:186-199`, called from `:201`). The three
    /// fields after it are the library's own public setters, so the list
    /// still raises ItemChanged and the row still redraws.
    /// </summary>
    public static void SetToGo(ActionButtonConfig slot)
    {
        if (slot == null) return;
        slot.SetToUnset();
        slot.Data = AvatarAction.None;
        slot.ButtonType = ActionButtonType.Action;
        slot.Name = GoName;
    }

    /// <summary>
    /// Puts a starting set of buttons in the client's list if it is empty.
    ///
    /// This part is **not** mirrored, because there is nothing to mirror:
    /// the game ships no default buttons at all. Its list comes from the
    /// player's own configuration file, written by a UI this client does
    /// not have yet, and an empty row on a phone is a row of nothing.
    /// These are the library's own avatar actions, which
    /// `BaseClient.ExecAction` already knows how to run.
    ///
    /// Item buttons bind themselves: the data controller matches a button
    /// by name against the inventory as it arrives, so a button named for
    /// an item you are carrying becomes that item.
    /// </summary>
    public static void Seed(DataController data)
    {
        if (data?.ActionButtons == null || data.ActionButtons.Count > 0) return;

        // What this character had last time wins over the starting set.
        // The game loads its saved buttons at exactly this point, when a
        // character is chosen (`OgreClient.cpp:984`), and falls back to
        // a default set when there are none.
        if (HotbarStore.Load(data)) return;
        // A fresh set has no chosen primary: Attack is, by the old rule.
        PrimaryNum = -1;

        object[] starting =
        {
            AvatarAction.Rest,
            AvatarAction.Attack,
            // GO IS SEEDED, and it goes here - directly after Attack,
            // which puts it on seat 2 of the first page.
            //
            // The argument for seeding it at all is the one the owner
            // made by playing: it is how you leave a room, so it is
            // pressed more often than six of the eight that were
            // already seeded, and a starting set that holds Wave but
            // not Go is a set that cannot get out of the inn without
            // opening a menu. The argument for the POSITION is the
            // pager: HotSeats bindings fit a page, the arc is
            // Attack-less and in list order, so this seed fills page
            // one with Rest, Go, Loot, Activate and pushes Inspect to
            // page two. Inspect is the right thing to push - it reads
            // a description off something already tapped, and nothing
            // in a fight or a corridor waits on it.
            //
            // A character who has played before does NOT gain it: Load
            // above returns first and that character's arrangement is
            // his own. The drawer tile and the Acts panel's "+" are how
            // he gets it, which is the other half of why both stay.
            // Go is NOT seeded any more: it is the Door control at the
            // end of the arc, permanently, so a seeded copy would be the
            // same send twice with one of them paged out of sight.
            AvatarAction.Loot,
            AvatarAction.Activate,
            AvatarAction.Inspect,
            AvatarAction.Buy,
            AvatarAction.Trade,
            AvatarAction.Wave,
        };

        int num = 0;
        foreach (object a in starting)
        {
            // The setters, not the constructor: the constructor leaves
            // Data null, and BaseClient.OnActionButtonActivated does
            // nothing at all for a button whose Data is null - the name
            // is a label, the Data is what the press dispatches on.
            var cfg = new ActionButtonConfig(num++, ActionButtonType.Unset, "");
            if (a is AvatarAction act) cfg.SetToAction(act);
            else SetToGo(cfg);
            data.ActionButtons.Add(cfg);
        }
    }

    /// <summary>
    /// Binds a spell, a skill or an item to a button - the phone's
    /// stand-in for the game's drag and drop.
    ///
    /// In the game every one of the 48 slots is on screen at once, empty
    /// ones included, and you drop a spell from the spell list or an item
    /// from the inventory onto whichever one you want
    /// (`UIActionButtons.cpp:359-438`). None of that survives the port:
    /// the lists you would drag from cover the whole screen on a phone,
    /// so the hotbar you would drop onto is not even visible while you
    /// are holding the thing to drop. The list panels carry a button per
    /// row instead, and this is what it calls.
    ///
    /// The slot chosen is the first unset one, and a new one is appended
    /// when they are all taken. That is the part the game does not have
    /// to decide, because its grid is a fixed 48 and ours is however many
    /// fit across one row.
    ///
    /// The setters are the library's, so what a bound button then does
    /// when pressed is the dispatch in BaseClient - not a second copy of
    /// it here.
    /// </summary>
    /// <summary>
    /// The slot the last successful <see cref="Bind"/> used, so the row
    /// can turn to the page it landed on. The game has room for all 48
    /// at once (`UIActionButtons.cpp:23-26`) and never needs this.
    /// </summary>
    public static int LastBound { get; private set; } = -1;

    /// <summary>
    /// A page the HotKeys panel wants shown when the cluster comes back,
    /// so an edit made on page two is on screen when the panel closes.
    /// Spent by the next Sync, like <see cref="LastBound"/>; -1 is "no
    /// request".
    /// </summary>
    public static int WantPage { get; set; } = -1;

    /// <summary>The page the last rebuild drew, zero-based. For the panel's "showing" mark.</summary>
    public static int CurrentPage { get; private set; }

    /// <summary>
    /// The Num of the config that is the primary - the big disc under
    /// the thumb - or -1 for "the seat holding Attack", which is the
    /// rule the cluster had before the disc could hold anything else.
    ///
    /// THE MODEL. The primary is one of the configs in the client's
    /// list, like every seat: it is drawn at AttackSize instead of on
    /// the arc, and that is the whole of the difference. Which one it
    /// is was decided by TYPE - the first config holding Attack - and
    /// the owner's fifth request ("allow ppl to change their big attack
    /// button to other things") needed it decided by NUM instead: a
    /// spell on the disc has no type to find it by. So the primary is
    /// the config at this Num, the HotKeys panel's Set on the Primary
    /// row writes THAT config in place with the library's own setters
    /// (the Num does not move, so the arc does not either), and
    /// HotbarStore saves the Num as a `primary` line beside the rows.
    ///
    /// -1 keeps every old file working unchanged: no line, no chosen
    /// primary, the Attack seat is the disc, which is what those files
    /// were saved under. It stays -1 until the player changes the disc,
    /// so a player who never opens the panel never writes the line.
    ///
    /// Static for the reason LastBound is: one hotbar, and the store,
    /// the panel and the cluster all read it. Set by HotbarStore.Load,
    /// Seed and the panel; nothing else writes it.
    /// </summary>
    public static int PrimaryNum = -1;

    /// <summary>
    /// The primary's config, or null when there is none: the config at
    /// <see cref="PrimaryNum"/> when that is set and present, else the
    /// first config holding Attack in list order (the old rule, and the
    /// fallback when a chosen Num has gone - a corrupt file, or a list
    /// the server replaced). An Unset config at the chosen Num is still
    /// the primary and draws as the disc's ring; the panel never writes
    /// one, but a file might hold one.
    ///
    /// One function, because three places asked the question with
    /// three copies of the type test - the cluster, the HotKeys panel's
    /// rows and its chooser - and a model that changes in one place and
    /// not the other two is a panel that shows one primary while the
    /// disc shows another.
    /// </summary>
    public static ActionButtonConfig Primary(DataController data)
    {
        if (data?.ActionButtons == null) return null;
        if (PrimaryNum >= 0)
        {
            ActionButtonConfig chosen = data.ActionButtons.GetByNum(PrimaryNum);
            if (chosen != null) return chosen;
        }
        foreach (ActionButtonConfig b in data.ActionButtons)
            if (IsAttack(b)) return b;
        return null;
    }

    /// <summary>
    /// The arc's order: by Num, not by list position. The reference's
    /// grid is positional - button `num` is the cell it sits in
    /// (`UIActionButtons.cpp:23-26`) - and the HotKeys panel moves a
    /// binding by swapping two Nums, which is only a move if this is
    /// what the cluster sorts on. Stable, so two equal Nums (a corrupt
    /// file) keep their list order rather than flickering.
    /// </summary>
    public static int ByNum(ActionButtonConfig a, ActionButtonConfig b) => a.Num.CompareTo(b.Num);

    /// <summary>
    /// Sorts a list by Num in place, stably. List.Sort is not stable
    /// and allocates a comparer; this runs every frame on a dozen
    /// entries, where an insertion sort is both.
    /// </summary>
    public static void Stable(List<ActionButtonConfig> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            ActionButtonConfig k = list[i];
            int j = i - 1;
            while (j >= 0 && list[j].Num > k.Num) { list[j + 1] = list[j]; j--; }
            list[j + 1] = k;
        }
    }

    public static bool Bind(DataController data, object what)
    {
        if (data?.ActionButtons == null || what == null) return false;

        ActionButtonConfig slot = null;
        int next = 0;
        foreach (ActionButtonConfig b in data.ActionButtons)
        {
            if (b == null) continue;
            if (b.Num >= next) next = b.Num + 1;
            // The lowest-numbered empty seat, which is the first ring
            // the player sees - the arc runs by Num (see ByNum).
            if (b.ButtonType == ActionButtonType.Unset && (slot == null || b.Num < slot.Num)) slot = b;
        }

        if (slot == null)
        {
            slot = new ActionButtonConfig(next, ActionButtonType.Unset, "");
            data.ActionButtons.Add(slot);
        }

        switch (what)
        {
            case SpellObject spell:    slot.SetToSpell(spell); break;
            // A passive skill cannot go on a button. The reference does
            // not offer the drag in the first place - a non-active skill's
            // row is built from the non-draggable window type, with no
            // CEGUI::DragContainer around its icon
            // (`Meridian59.Ogre.Client/UISkills.cpp:67-86`) - and the
            // library refuses to perform one even if it somehow got there
            // (`Meridian59/Client/BaseClient.cs:1841-1853`). SpellsPanel
            // already withholds the row's bind button for these, so this is
            // the same refusal said once more at the place that actually
            // writes the slot: every other caller of Bind hands it a spell,
            // an item or an action, and a bound passive skill would be a
            // button that silently does nothing forever.
            case SkillObject skill when !skill.IsActiveSkill: return false;
            case SkillObject skill:    slot.SetToSkill(skill); break;
            case InventoryObject item: slot.SetToItem(item);   break;
            // The actions window binds these. SetToAction, not the
            // constructor - see Seed: a config with a null Data is a
            // button BaseClient's dispatch does nothing for.
            case AvatarAction act:     slot.SetToAction(act);   break;
            // Go, which the library has no type for - see Extra. It
            // comes through Bind rather than having a BindGo of its own
            // so that one function stays the only thing that chooses a
            // slot, appends when the page is full and sets LastBound;
            // a second writer would be a second copy of that, and the
            // last time two places wrote the row it shipped a button
            // that fired a different button's action.
            case Extra.Go:             SetToGo(slot);           break;
            // An alias, dragged out of the alias page in the reference:
            // the row carries a CEGUI::DragContainer
            // (`Meridian59.Ogre.Client/UIOptions.cpp:1070`, its icon set
            // at `:1082`) and dropping it on a slot ends in
            // `buttonModels[indexbutton]->SetToAlias(aliasModels[index])`
            // (`UIActionButtons.cpp:427-434`). So the same library
            // setter is called here, with the same object: the
            // KeyValuePairString that lives in Config.Aliases, not a
            // copy of it. That identity is the point - `SetToAlias`
            // hangs the pair itself on Data (`ActionButtonConfig.cs:267-278`)
            // and BaseClient reads `.Value` off it at the press
            // (`BaseClient.cs:292-296`), so editing the alias in the
            // editor changes what the button sends, with no rebinding.
            case KeyValuePairString alias: slot.SetToAlias(alias); break;
            default: return false;
        }

        LastBound = slot.Num;
        HotbarStore.Save(data);
        return true;
    }

    /// <summary>
    /// Rebuilds the row when the configured buttons change. Called every
    /// frame; a signature keeps it from rebuilding for nothing.
    /// </summary>
    readonly List<ActionButtonConfig> _set = new List<ActionButtonConfig>(), _arc = new List<ActionButtonConfig>();

    public void Sync(DataController data)
    {
        _data = data;
        // The player's transparency, every frame: it is the one part of
        // the cluster that no rebuild signature can carry.
        M59Hud.Dress("combat");
        // No list means no world yet: the whole cluster goes, rings and
        // Next included. A ring left on screen over the character picker
        // is the sort of thing that reads as a renderer fault.
        if (data?.ActionButtons == null)
        {
            HideFrom(0);
            for (int i = 0; i < _rings.Count; i++) _rings[i].Visible = false;
            if (_turn != null) _turn.Visible = false;
            if (_nextBtn != null) _nextBtn.Visible = false;
            if (_doorBtn != null) _doorBtn.Visible = false;
            if (_phaseBtn != null) _phaseBtn.Visible = false;
            if (_lockBtn != null) _lockBtn.Visible = false;
            _signature = "";
            return;
        }

        // Reused, not re-made: Sync runs every frame and two fresh lists
        // a frame is a steady drip of garbage for a bar that rarely changes.
        List<ActionButtonConfig> set = _set; set.Clear();
        foreach (ActionButtonConfig b in data.ActionButtons)
            if (b != null && b.ButtonType != ActionButtonType.Unset) set.Add(b);

        Vector2 v = GetViewportRect().Size;
        float sc = HudScale();

        // Where the player put the cluster, resolved before anything in
        // it is placed: the natural box is the designer's, and everything
        // below hangs off a pivot that carries the difference.
        Rect2 nat = Natural(v);
        _shift = M59Hud.Place("combat", nat, v).Position - nat.Position;

        // The primary is one slot out of the list - the same config, the
        // same press, the same hold - drawn at AttackSize in the middle
        // of the cluster instead of as one more seat on the arc. Which
        // slot is Primary's question: the chosen Num, or the seat holding
        // the game's Attack action where none has been chosen (see
        // PrimaryNum). The hold still repeats by what the slot HOLDS
        // (Repeats), so a spell on the disc re-casts while leant on and
        // Rest on the disc does not flap.
        //
        // Nothing synthesises an Attack that is not in the list. A
        // config the client's list does not hold is one BaseClient never
        // subscribed to, so pressing it would do nothing at all
        // (see Seed). An empty disc shows its ring until something is
        // put there from the HotKeys panel's Primary row, or Attack is
        // bound again from the Acts panel, which Primary finds by type.
        ActionButtonConfig chosen = Primary(data);
        // An Unset primary is still kept off the arc; it draws as the
        // disc's ring, not as a seat.
        ActionButtonConfig anchor = chosen != null && chosen.ButtonType != ActionButtonType.Unset ? chosen : null;

        // Phase, which every character has and which the owner wants
        // one press away at all times: the player's own spell of that
        // name, looked up on every Sync because the list fills and its
        // names resolve after login (notes/godot-ui.md, "A polled
        // signature must hold everything the panel draws"). Null is a
        // character without it, or one whose spells have not arrived.
        SpellObject phase = FindPhase(data);

        // THE ARC IS POSITIONAL NOW, which is a change from "the set
        // buttons, compacted". Unset configs stay in their seats and are
        // drawn as rings, and the order is by Num rather than by list
        // position (ByNum). Both halves come from the reference: its
        // grid shows every cell, a cleared one included
        // (`UIActionButtons.cpp:23-26`, the drop-on-root clear at
        // `:471-473` empties the cell and moves nothing), and its config
        // writes every button, unset or not
        // (`OgreClientConfig.cpp:1203-1211`, the loop over set->Count).
        // Compacting meant a cleared seat shuffled every later binding
        // up a place, which on a thumb-memorised arc is three buttons
        // moved for one cleared - and it made the HotKeys panel's "put
        // this HERE" impossible to honour, because a hole could not
        // exist.
        List<ActionButtonConfig> arc = _arc; arc.Clear();
        foreach (ActionButtonConfig b in data.ActionButtons)
            if (b != null && b != chosen) arc.Add(b);
        Stable(arc);

        // The game draws all forty-eight buttons at once, twelve by four
        // (`UIActionButtons.cpp:23-26`). A phone has one row, and what
        // this did was draw the first `across` of them and silently drop
        // the rest - so once the row was full, binding a spell from the
        // book said "it is on the hotbar" and nothing appeared, and the
        // only way to reach it was to turn the device sideways. The
        // bindings were saved the whole time; they were just invisible.
        //
        // So the cluster pages, and paging is also the answer to the arc
        // holding four when the seeded set is eight. The alternatives
        // were weighed and are worse: a smaller button is the defect
        // being fixed, a second outer ring puts its far seats 350 points
        // from the thumb's pivot, which is a hand movement rather than a
        // sweep, and dropping the overflow is what used to happen and
        // reads as a client that lost your binding. The stepper does NOT
        // cost a seat any more - it sits inside the arc (see TurnSeat),
        // so a page is HotSeats bindings - six now - rather than one fewer.
        bool paged = arc.Count > HotSeats;
        int perPage = HotSeats;
        int pages = paged ? (arc.Count + perPage - 1) / perPage : 1;

        // A binding just made is worth more than whatever page you were
        // on: turn to it, so pressing "+" in the spell book shows you
        // where the spell went. Attack is the exception and needs no
        // turn - it is the primary, on every page.
        if (LastBound >= 0)
        {
            int at = arc.FindIndex(b => b.Num == LastBound);
            if (at >= 0) _page = at / perPage;
            LastBound = -1;
        }
        // The HotKeys panel's request, for the same reason: the page
        // that was just edited is the one to come back to.
        if (WantPage >= 0) { _page = WantPage; WantPage = -1; }

        // WRAPS, and the clamp below is only for a page count that
        // SHRANK under us - something unbound while you were on the
        // last page. Incrementing and clamping instead stranded half
        // the hotbar for the session: one tap of the stepper went to
        // page 2 and every further tap did nothing, so Rest, Loot,
        // Activate and Inspect could not be reached again except by
        // binding something that happened to land on page 1, because
        // LastBound above turns the page. The eight-slot bar this
        // replaced showed every slot at once, so it is a regression the
        // layout introduced and a verification pass caught.
        _pages = pages;
        if (_page >= pages) _page = pages - 1;
        if (_page < 0) _page = 0;
        CurrentPage = _page;

        int first = _page * perPage;
        int count = Math.Min(perPage, Math.Max(0, arc.Count - first));

        var sb = Sig.Start();
        if (anchor != null)
            sb.Append('!').Append(anchor.Num).Append(':').Append(anchor.ButtonType).Append(':').Append(anchor.Name)
              .Append(':').Append(anchor.Data is ObjectBase ao ? ao.Resource?.Filename : "").Append(';');
        // Phase: whether there is one, and whether its art has resolved
        // (the seat shows the spell's own icon once it can be composed).
        sb.Append('~').Append(phase == null ? "-" : phase.Resource?.Filename ?? "?").Append(';');
        // The padlock's state, which redraws the glyph.
        sb.Append(HotbarStore.Locked ? 'L' : 'U');
        for (int i = 0; i < count; i++)
            sb.Append(arc[first + i].Num).Append(':').Append(arc[first + i].ButtonType)
              .Append(':').Append(arc[first + i].Name).Append(';');
        // The viewport and the reserve, because the cluster is measured
        // off both corners of the glass.
        sb.Append('@').Append((int)v.X).Append('x').Append((int)v.Y)
          .Append('@').Append((int)BottomReserve).Append('@').Append((int)LeftReserve)
          .Append('@').Append(_page).Append('/').Append(pages);
        // And where the player has put it. Without these a drag, a resize
        // or a hide would leave the cluster exactly where it was: the
        // rebuild is what applies them, as it is for the reserve above.
        M59Hud.Piece piece = M59Hud.Get("combat");
        if (piece != null)
            sb.Append('@').Append((int)piece.Offset.X).Append(',').Append((int)piece.Offset.Y)
              .Append('@').Append(piece.Scale.ToString("0.###"))
              .Append('@').Append(piece.Hidden ? 1 : 0)
              .Append('@').Append(M59Hud.Editing ? 1 : 0);

        if (!Sig.Changed(sb, ref _signature)) return;

        _nums.Clear();
        // Every pooled button gives its name back before any is
        // renamed. A button named `hot{Num}` from the last layout is a
        // sibling-name clash for the button that takes that Num in this
        // one, and Godot renames the newcomer - `hot5` came out as
        // `hot6` - so a scripted run pressed the wrong seat or none
        // (notes/harness.md, "Free a row before you add its
        // replacement" is the same trap on a list).
        for (int i = 0; i < _pool.Count; i++) _pool[i].Name = $"pool{i}";
        // Pool slot 0 is the primary when there is one - OnUp needs to
        // know, because the disc is never cleared by the pull-off.
        _primarySlot = anchor != null ? 0 : -1;

        // Pool index 0 is the primary when there is one, then the arc in
        // seat order. The handlers below capture the POOL index and
        // _nums runs parallel to it, which is what lets a press resolve
        // the button number it is firing at the moment it happens - see
        // the long note where they are wired.
        int at0 = 0;
        for (int i = -1; i < count; i++)
        {
            bool primary = i < 0;
            if (primary && anchor == null) continue;
            ActionButtonConfig cfg = primary ? anchor : arc[first + i];
            // An empty seat on this page: a ring, drawn with the rest of
            // them below. No button, so the tap goes through to the world.
            if (!primary && cfg.ButtonType == ActionButtonType.Unset) continue;
            float d = primary ? Atk : Btn;
            Rect2 cell = Round(primary ? Pivot(v) : ArcSeat(v, i), d);
            Button b = Take(at0);

            // Label is an empty string rather than null when unset, so a
            // null-coalesce picks the blank one and every button reads "?".
            // The disc's picture is composed at the disc's proportion of a
            // seat - a 56-pixel sprite in a 160 circle is a smudge, and
            // the whole reason the primary is big is to be read at a glance.
            Texture2D icon = primary ? Icon(cfg, Mathf.RoundToInt(IconPx * (Atk / Btn))) : Icon(cfg);
            b.Icon = icon;
            // Every alias shares one picture, so the picture alone says
            // "an alias" and never which one. The reference can afford
            // that because the slot carries a tooltip with the name in
            // it (`UIActionButtons.cpp:177-183`) and a mouse to hover
            // with; a thumb has neither. So an alias button shows the
            // icon AND its key, which is the one departure here, and the
            // room for the text is bought by keeping the sprite at its
            // native 24 pixels rather than expanding it.
            bool isAlias = cfg.ButtonType == ActionButtonType.Alias;
            b.ExpandIcon = false;
            // Named by the button's number so a scripted run can press
            // one. A bound spell or item shows a picture and no text, so
            // until now there was nothing to find those by at all - the
            // ones worth testing were exactly the ones unreachable.
            b.Name = $"hot{cfg.Num}";
            // The game never captions a button - its only on-button text
            // is the slot number under _DEBUG (`UIActionButtons.cpp:75`).
            // A label here is not decoration but the last resort of a
            // button that has no picture to show, which is every action
            // button, so it has to be the name: Label holds the key the
            // game binds the slot to, and there are no keys on a phone.
            b.Text = icon != null && !isAlias ? "" : Short(cfg.Name, isAlias ? 6 : 8);
            // CENTRED, which a Godot Button does not do by itself: its
            // IconAlignment defaults to Left, and with no text to push
            // against, a spell's picture sat against the left rim of a
            // round seat instead of in the middle of it. Only the
            // icon-only case: an alias shows a picture AND its key, and
            // there the picture belongs at the left with the word beside
            // it rather than the two stacked on the same spot.
            b.IconAlignment = b.Text.Length == 0
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            // The primary says its name at title size. An action has no
            // picture to show - the seeded set is all actions - so the
            // word IS the icon here, and "Attack" at body size in a
            // 160-point circle is the row of small text buttons again.
            b.AddThemeFontSizeOverride("font_size", Pt(primary ? M59Skin.TitleSize : FontSize, sc));
            // The reference's tooltip is the name over "Key: <label>"
            // (`:177-183`); Label is the keyboard binding, which a phone
            // does not have, so the second line is spent on the thing
            // the player actually wants confirmed - what the alias
            // expands to, which is the Value that `BaseClient.cs:292-296`
            // will send.
            // Go's name is two letters and says nothing about what it
            // does; the drawer tile it duplicates carries "Through the
            // door" as its caption, so the slot carries the same words.
            b.TooltipText = isAlias && cfg.Data is KeyValuePairString kv
                ? cfg.Name + "\n" + kv.Value
                : IsGo(cfg) ? GoName + "\nThrough the door"
                : cfg.Name;
            b.Position = cell.Position;
            b.Size = cell.Size;
            SlotEdge(b, d, primary, false, sc);
            b.Visible = true;

            // The slot, not the button number and not the config object.
            //
            // Not the object, because the client replaces the whole list
            // when it loads the player's saved buttons on login, and the
            // replacements carry the same numbers and names: holding the
            // object would leave every button pressing a config the
            // client has already thrown away, one BaseClient is no
            // longer subscribed to, so the press would do nothing.
            //
            // Not the number either, which is what this used to capture.
            // The handler is attached once per pooled Button and the
            // pool is reused, so it kept the number of whatever config
            // first happened to land in that screen position. Unset
            // buttons are filtered out and the rest compacted, so that
            // position does not hold the same button for long - bind one
            // item and slot 3 would show one button's icon and fire
            // another's. The game has no such bug because it resolves
            // the index at the click (`UIActionButtons.cpp:351`), and
            // that is what the slot gives us.
            int slot = at0++;
            _nums.Add(cfg.Num);
            if (b.HasMeta("wired")) continue;
            b.SetMeta("wired", true);
            // A held press REPEATS; it no longer clears. The reference
            // reads the action-button keys every input tick with
            // isKeyDown(...)->Activate() (`ControllerInput.cpp:995-1030`),
            // so holding the key keeps swinging, and the only throttle is
            // the library's own interval (`GameTick.cs:40-51`, tests at `GameTick.cs:304-313`, enforced in
            // `BaseClient.cs:1522` for attacks and :1753 for casts). The
            // old gesture - hold 600 ms to clear - erased the button the
            // player was leaning on mid-fight, so clearing moved to the
            // reference's own gesture: drag the button off the row and
            // let go (`UIActionButtons.cpp:471-473`, dropped on the root
            // window -> SetToUnset). See OnDown / OnUp / OnGui.
            b.ButtonDown += () => OnDown(slot);
            b.ButtonUp += () => OnUp(slot, b);
            b.GuiInput += ev => OnGui(slot, b, ev);
            b.Pressed += () => Fire(slot);
        }

        // Next, at the top of the INNER arc, where an upward flick of
        // the thumb finds it. It held the outer arc's 70-degree seat
        // until the owner asked for that seat as a sixth binding and
        // Next "down a bit" - so it is the first of the four utilities
        // on the inner arc now (NextSeat), above Phase.
        //
        // It is a TARGETING control and not a binding: it holds no
        // config, it cannot be dragged off, and it does not page. The
        // library does the choosing - nearest guild enemy first, then
        // nearest attackable, skipping the ones already visited
        // (`DataController.NextTarget`) - and this sends it through the
        // same gate the hotbar's own presses go through, which is the
        // same gate the view's Next went through: Run is HotbarAct, and
        // HotbarAct with keepLatch false is WorldAct exactly (the
        // IsWaiting exit, the send, then the self-target latch spent).
        //
        // The view still carries one of these as a tile in the menu
        // drawer (`GameView.cs`, "Next target"), which is where it was
        // before this cluster existed and four taps from a fight. That
        // file is not this change's to edit; the tile is now a duplicate
        // and can go.
        if (_nextBtn == null)
        {
            // Two words in a 96-point circle, so it wraps rather than
            // clipping: "Target Next" on one line inside a round seat
            // is "Targ...", which names nothing.
            _nextBtn = new Button
            {
                // Broken by hand rather than left to autowrap: wrapping
                // happens at the CONTROL's width, and a round seat's
                // width is its diameter, so "Target Next" measured as
                // fitting and then drew past the rim on both sides. The
                // break is where it has to be, so the words sit inside
                // the circle rather than across it.
                Text = "Target\nNext",
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipText = false,
            };
            _nextBtn.Name = "hotnext";
            _nextBtn.TooltipText = "Next target";
            _nextBtn.Pressed += PressNext;
            AddChild(_nextBtn);
        }
        // Dressed on every rebuild rather than once, so the caption
        // follows the player's size.
        // Smaller than a slot's caption, because this one is two lines
        // inside the same circle.
        _nextBtn.AddThemeFontSizeOverride("font_size", Pt(FontSize - 3, sc));
        Rect2 nextCell = Round(NextSeat(v), Btn);
        _nextBtn.Position = nextCell.Position;
        _nextBtn.Size = nextCell.Size;
        SlotEdge(_nextBtn, Btn, false, true, sc);
        _nextBtn.Visible = true;

        // DOOR, fixed, inside the arc.
        //
        // It used to be a hotbar binding - Extra.Go, seeded onto page
        // one - and that was wrong for the same reason Next is not a
        // binding: it is not a thing you choose between, it is the only
        // way out of a room. A phone has no space bar, and a Go that
        // has been paged away, or dragged off by accident, leaves a
        // player able to walk around one room and never leave it.
        //
        // It held the arc's 180-degree seat; that seat is a binding now
        // and Door sits straight left of the primary at the bottom of
        // the inner arc, between the outer arc's lower end and the disc
        // - "move the door button over to the right as i did in the
        // picture".
        if (_doorBtn == null)
        {
            _doorBtn = new Button { Text = "Door" };
            _doorBtn.Name = "hotdoor";
            _doorBtn.TooltipText = "Through the door";
            _doorBtn.Pressed += () => GoSend?.Invoke();
            AddChild(_doorBtn);
        }
        _doorBtn.AddThemeFontSizeOverride("font_size", Pt(FontSize, sc));
        Rect2 doorCell = Round(DoorSeat(v), Btn);
        _doorBtn.Position = doorCell.Position;
        _doorBtn.Size = doorCell.Size;
        SlotEdge(_doorBtn, Btn, false, true, sc);
        _doorBtn.Visible = true;

        // PHASE, fixed, second on the inner arc under Next. "A normal sized button that cast the
        // spell phase (every player has it and it is very important)."
        //
        // Not a config in the list: a config is paged, cleared and
        // saved, and this is none of those - it is a fixed control like
        // Door, whose press is the player's own Phase spell. The send is
        // the spell book's (CastSend, `BaseClient.SendReqCastMessage
        // (SpellObject)`, BaseClient.cs:1717 - the same method a Spell
        // seat's Activate reaches through OnActionButtonActivated at
        // :284-285), through Run, so the IsWaiting gate and the
        // self-target latch apply exactly as they do for a seat.
        //
        // Dimmed, and inert, for a character without the spell: the
        // library would refuse to cast a spell the character does not
        // know (SendReqCastMessage(uint) looks the id up in
        // Data.SpellObjects, :1702-1709), so there is nothing to send,
        // and a dim seat says why. It stays on screen rather than
        // hiding so the cluster does not change shape by character.
        if (_phaseBtn == null)
        {
            _phaseBtn = new Button { Text = "Phase", ClipText = true, ExpandIcon = false,
                                     IconAlignment = HorizontalAlignment.Center };
            _phaseBtn.Name = "hotphase";
            _phaseBtn.TooltipText = "Cast Phase";
            _phaseBtn.Pressed += PressPhase;
            AddChild(_phaseBtn);
            // The name under the picture, inside the ring. A Spell seat
            // on the arc shows its picture alone, because the player put
            // it there and knows it; this seat was put here by the
            // client, so it says what it is.
            _phaseCap = new Label
            {
                Text = "Phase", MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _phaseCap.AddThemeColorOverride("font_color", M59Skin.Gold);
            _phaseBtn.AddChild(_phaseCap);
        }
        _phaseBtn.AddThemeFontSizeOverride("font_size", Pt(FontSize, sc));
        Rect2 phaseCell = Round(PhaseSeat(v), Btn);
        _phaseBtn.Position = phaseCell.Position;
        _phaseBtn.Size = phaseCell.Size;
        // The spell's own picture when the resource has arrived, as a
        // Spell seat shows; the word alone until then, and for ever when
        // the character has no such spell.
        // No picture: "remove the spell icon from the phase button" - the
        // word is the button, as Door's is.
        Texture2D phaseIcon = null;
        _phaseBtn.Icon = phaseIcon;
        _phaseBtn.Text = phaseIcon != null ? "" : "Phase";
        SlotEdge(_phaseBtn, Btn, false, true, sc);
        // With a picture, the caption takes the bottom of the ring and
        // the picture is pushed up to leave it room: the stylebox's
        // content margin is the one lever a Button has over where its
        // icon sits vertically.
        float capH = phaseIcon != null ? Mathf.Round(Btn * 0.22f) : 0f;
        foreach (string state in new[] { "normal", "hover", "focus", "pressed" })
            if (_phaseBtn.GetThemeStylebox(state) is StyleBoxFlat sbf) sbf.ContentMarginBottom = capH;
        _phaseCap.Visible = phaseIcon != null;
        _phaseCap.AddThemeFontSizeOverride("font_size", Pt(FontSize - 4, sc));
        _phaseCap.Position = new Vector2(0f, Btn - capH - Rim(2, sc) * 2f);
        _phaseCap.Size = new Vector2(Btn, capH);
        _phaseBtn.Disabled = phase == null;
        _phaseBtn.Modulate = phase == null ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;
        _phaseBtn.Visible = true;

        // THE PADLOCK. "A padlock icon on the bottom right of the hotkey
        // ui element that unlocks and locks the hotkeys so people dont
        // accidentally remove their hotkeys while playing."
        //
        // Locked, the pull-off clear (OnUp) does nothing on any seat;
        // presses, holds and paging work as before. Unlocked, a seat can
        // be pulled off and emptied as it always could. Default locked,
        // kept across sessions and characters (HotbarStore.Locked). The
        // glyph is drawn, not a font character: the house font has no
        // padlock, and a square with a shackle reads the same in every
        // language the client ships in.
        if (_lockBtn == null)
        {
            _lockBtn = new Button { Name = "hotlock" };
            _lockGlyph = new LockGlyph { MouseFilter = MouseFilterEnum.Ignore };
            _lockBtn.AddChild(_lockGlyph);
            _lockBtn.Pressed += () =>
            {
                HotbarStore.Locked = !HotbarStore.Locked;
                Locked?.Invoke(HotbarStore.Locked);
                _signature = "";
            };
            AddChild(_lockBtn);
        }
        bool locked = HotbarStore.Locked;
        _lockBtn.TooltipText = locked ? "Hotkeys locked: tap to allow clearing seats"
                                      : "Hotkeys unlocked: pull a seat off the cluster to clear it";
        Rect2 lockCell = LockRect(v);
        _lockBtn.Position = lockCell.Position;
        _lockBtn.Size = lockCell.Size;
        // Dressed as a stepper, like the pager: it is chrome, not an
        // action, and it must never read as a seventh thing to press in
        // a fight. Open, the glyph goes gold - the one state worth a
        // glance, because it is the state in which a seat can be lost.
        var lockFace = Cell(locked ? M59Skin.Rule : M59Skin.GoldDim,
                            new Color(0.157f, 0.141f, 0.118f, 0.94f), Rim(2, sc), lockCell.Size.X * 0.5f);
        var lockHit = Cell(M59Skin.Gold, new Color(0.267f, 0.224f, 0.157f, 0.96f), Rim(2, sc), lockCell.Size.X * 0.5f);
        _lockBtn.AddThemeStyleboxOverride("normal", lockFace);
        _lockBtn.AddThemeStyleboxOverride("hover", lockFace);
        _lockBtn.AddThemeStyleboxOverride("focus", lockFace);
        _lockBtn.AddThemeStyleboxOverride("pressed", lockHit);
        _lockGlyph.Position = Vector2.Zero;
        _lockGlyph.Size = lockCell.Size;
        _lockGlyph.Open = !locked;
        _lockGlyph.Tint = locked ? M59Skin.TextDim : M59Skin.Gold;
        _lockGlyph.QueueRedraw();
        _lockBtn.Visible = true;

        // The page button, saying where you are.
        // Its own button, not one out of the pool: a pooled button
        // already carries a Pressed handler that fires whatever action
        // sat in that position, and turning the page would cast a spell.
        if (paged)
        {
            if (_turn == null)
            {
                _turn = new Button();
                // A stepper, not a slot: it moves the cluster rather
                // than doing anything in the world.
                M59Skin.Dress(_turn, M59Skin.Kind.Step);
                _turn.Name = "hotpage";
                _turn.TooltipText = "More buttons";
                _turn.Pressed += () =>
                {
                    // Round, not up: the last page's stepper goes back
                    // to the first. With two pages that is a toggle,
                    // which is what it looks like.
                    _page = _pages > 0 ? (_page + 1) % _pages : 0;
                    _signature = "";
                };
                AddChild(_turn);
            }
            _turn.Text = $"{_page + 1}/{pages}";
            _turn.AddThemeFontSizeOverride("font_size", Pt(18, sc));
            // Round, like everything else in the cluster - the skin's
            // Step is a rounded square, and one square among six circles
            // reads as a thing that failed to load rather than as a
            // stepper. Its COLOURS stay the stepper's, which is what
            // says it is not another action.
            var face = Cell(M59Skin.Rule, new Color(0.157f, 0.141f, 0.118f, 0.94f), Rim(2, sc), Turn);
            var hit = Cell(M59Skin.GoldDim, new Color(0.267f, 0.224f, 0.157f, 0.96f), Rim(2, sc), Turn);
            _turn.AddThemeStyleboxOverride("normal", face);
            _turn.AddThemeStyleboxOverride("hover", face);
            _turn.AddThemeStyleboxOverride("focus", face);
            _turn.AddThemeStyleboxOverride("pressed", hit);
            Rect2 turnCell = Round(TurnSeat(v), Turn);
            _turn.Position = turnCell.Position;
            _turn.Size = turnCell.Size;
            _turn.Visible = true;
        }
        else if (_turn != null) _turn.Visible = false;

        HideFrom(at0);

        // The empty seats, as rings. One for the primary when nothing is
        // bound to Attack, one for each arc seat past the bindings on
        // this page - and none at all while the page is full, which is
        // the usual case with the seeded set.
        int ring = 0;
        if (anchor == null) Ring(ring++, Round(Pivot(v), Atk), sc);
        // Past the end of the arc, and any hole inside it.
        for (int i = 0; i < HotSeats; i++)
            if (i >= count || arc[first + i].ButtonType == ActionButtonType.Unset)
                Ring(ring++, Round(ArcSeat(v, i), Btn), sc);
        for (int i = ring; i < _rings.Count; i++) _rings[i].Visible = false;

        // The highest seat, not seat 0: the arc's top is one step round
        // from the end that leans towards the screen edge.
        //
        // Still published when the player has hidden the cluster: it is
        // where the cluster WOULD be, and the target block stands on it,
        // so hiding this one must not make that one jump down the screen.
        float top = v.Y * 0.58f;
        for (int i = 0; i < ArcSeats; i++)
            top = Mathf.Min(top, Round(Seat(v, i), Btn).Position.Y - M59Skin.Gap);
        Ceiling = top;
        Measure(v, anchor, count, paged);

        // The player's own hide, last and under everything the client
        // decided: the host still hides the whole control when a panel is
        // up or the drawer is open, and this only says whether the player
        // wants to see the cluster while it is allowed to be there.
        if (!M59Hud.Shows("combat"))
        {
            HideFrom(0);
            for (int i = 0; i < _rings.Count; i++) _rings[i].Visible = false;
            if (_turn != null) _turn.Visible = false;
            if (_nextBtn != null) _nextBtn.Visible = false;
            if (_doorBtn != null) _doorBtn.Visible = false;
            if (_phaseBtn != null) _phaseBtn.Visible = false;
            if (_lockBtn != null) _lockBtn.Visible = false;
        }
    }

    /// <summary>
    /// Runs one activation through the GameView's gates. Arguments: the
    /// send, and whether to keep the self-target latch alive afterwards
    /// (a held button keeps it until you let go). Returns whether the
    /// send was let through - false while the server has you waiting
    /// (`ControllerInput.cpp:736`, which the keyboard path sits behind).
    /// Null in a bare harness, which then activates directly.
    /// </summary>
    public Func<Action, bool, bool> Run;

    /// <summary>
    /// Raised when Target Next has taken a new target. The view clears
    /// the self-target mode on it - see GameView.SpendSelfTarget for
    /// why that mode now ends only on a retarget. This used to be
    /// SpendLatch, invoked at the end of a held repeat; a held buff no
    /// longer un-aims you either.
    /// </summary>
    public Action Retargeted;

    /// <summary>Told when a drag-off clears a button, for the chat line.</summary>
    public event Action<string> Cleared;

    /// <summary>
    /// How long a press must be held before it starts repeating. A tap
    /// still fires on release, as before; the delay is what tells a tap
    /// from a hold without firing on the press itself, because a press
    /// that turns into a drag-off must not have cast the spell it is
    /// about to clear.
    /// </summary>
    [Export] public ulong RepeatDelayMs = 250;

    /// <summary>
    /// How far outside the button, in pixels, a finger has to be when it
    /// lifts to clear the button. Big enough that thumb jitter in a
    /// fight is nowhere near it.
    /// </summary>
    [Export] public float PullOffPx = 48f;

    int _heldSlot = -1;
    int _heldNum;
    ulong _heldSince;
    bool _repeating, _outside, _sentInHold, _heldInUse;

    void OnDown(int slot)
    {
        if (slot < 0 || slot >= _nums.Count) return;
        _heldSlot = slot;
        _heldNum = _nums[slot];
        _heldSince = Time.GetTicksMsec();
        _repeating = false; _outside = false; _sentInHold = false;
        _heldInUse = _data?.ActionButtons?.GetByNum(_heldNum)?.Data is InventoryObject io && io.IsInUse;
    }

    void OnGui(int slot, Button b, InputEvent ev)
    {
        if (_heldSlot != slot) return;
        Vector2? at = ev is InputEventMouseMotion m ? m.Position
                    : ev is InputEventScreenDrag d ? d.Position : (Vector2?)null;
        if (at == null) return;
        _outside = !new Rect2(-PullOffPx, -PullOffPx,
            b.Size.X + 2f * PullOffPx, b.Size.Y + 2f * PullOffPx).HasPoint(at.Value);
    }

    void OnUp(int slot, Button b)
    {
        if (_heldSlot != slot) return;
        bool pulled = _outside;
        int num = _heldNum;
        EndHold();
        if (pulled)
        {
            // The reference's clear: drop the button on the root window
            // (`UIActionButtons.cpp:471-473`). Two things stop it here
            // that the reference does not have:
            //
            // THE PADLOCK (HotbarStore.Locked, default on). The owner's
            // words: "so people dont accidentally remove their hotkeys
            // while playing". A thumb that slides off the disc in a
            // fight is exactly the gesture, and the reference never
            // faced it because a mouse does not slip.
            //
            // THE PRIMARY. "do not let people drag off the attack
            // action, phase action or door action" - Phase and Door are
            // not pooled seats and have no pull-off at all; the disc is,
            // and whatever it holds stays. It is changed from the
            // HotKeys panel's Primary row, never lost to a slip.
            if (HotbarStore.Locked || slot == _primarySlot) return;
            ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(num);
            if (cfg == null) return;
            string name = cfg.Name;
            cfg.SetToUnset();
            HotbarStore.Save(_data);
            Cleared?.Invoke(name);
        }
    }

    /// <summary>
    /// Ends a hold. The flags Pressed reads (_repeating, _outside) are
    /// cleared at the end of the frame rather than here, because Godot
    /// may raise Pressed before or after ButtonUp and either order has to
    /// see them.
    /// </summary>
    void EndHold()
    {
        if (_heldSlot < 0) return;
        _heldSlot = -1;
        _sentInHold = false;
        Callable.From(() => { _repeating = false; _outside = false; }).CallDeferred();
    }

    /// <summary>
    /// What a held press repeats. The reference polls every action-button
    /// key with isKeyDown(...)->Activate() and no edge test
    /// (`ControllerInput.cpp:997-1044`), so a held key re-activates
    /// whatever the slot holds, of any type, and the library throttles
    /// each: attack and cast `GameTick.cs:40-51` (`BaseClient.cs:1522`,
    /// `:1753`), a use or apply once per object per 500 ms
    /// (`BaseClient.cs:2041`, `GameTick.cs:349`, INTERVALINTERACT :30),
    /// an alias once per 500 ms (`BaseClient.cs:292`, INTERVALALIAS :35).
    /// Those all repeat here, and the library's gate is the cadence.
    ///
    /// Two deliberate departures, both toggles. Rest: the reference's
    /// held key would alternate Rest and Stand every 500 ms
    /// (`BaseClient.cs:3218-3230`), which no player wants from a thumb
    /// left on the button. An item that is used or unused
    /// (`BaseClient.cs:3018-3024`) - worn gear - would flap the same way,
    /// so an item repeats only while its in-use state is what it was when
    /// the press went down, and only while it is still in the pack (a
    /// drunk potion's config keeps the dead object). One-shot actions
    /// (Dance, Wave...) stay one per press, as the library's 500 ms
    /// action gate (`BaseClient.cs:1471`) makes repeats pointless.
    /// </summary>
    bool Repeats(ActionButtonConfig cfg)
    {
        switch (cfg.ButtonType)
        {
            case ActionButtonType.Spell:
            case ActionButtonType.Skill:
            case ActionButtonType.Alias:
                return true;
            case ActionButtonType.Item:
                return cfg.Data is InventoryObject o && o.IsInUse == _heldInUse && InPack(o);
            // Attack and nothing else, which now has a second reason
            // beyond the action gate: Go is an Action-typed slot (see
            // Extra) and GO MUST NOT REPEAT.
            //
            // The reference settles it. Every action-button key is read
            // by the per-frame input tick with isKeyDown and no edge
            // test (`ControllerInput.cpp:995-1044`), which is why a held
            // slot re-fires here at all - but ReqGo is not read there.
            // It is dispatched from OISKeyboard_KeyPressed
            // (`ControllerInput.cpp:535`, firing at :552-553), the
            // KEY-DOWN EVENT, which OIS raises once per physical press.
            // Leaning on the Open key in the reference sends one ReqGo,
            // so a Go slot that repeated would be a behaviour the game
            // does not have.
            //
            // And the library has no throttle to lean on. Every other
            // repeating kind is gated - attack and cast by
            // `GameTick.cs:300-315` (`BaseClient.cs:1522`, `:1753`), use
            // and apply by INTERVALINTERACT (`GameTick.cs:30,349`), an
            // alias by INTERVALALIAS (`:35,376`) - but there is no
            // CanReqGo anywhere in `GameTick.cs`: SendReqGo records
            // DidReqGo and tests nothing (`BaseClient.cs:1546-1562`,
            // `GameTick.cs:408-411`). A held Go would put one ReqGo per
            // frame on the wire, ungated.
            //
            // Worse than the flood, it would stop the player walking.
            // The one reader of GameTick.ReqGo is SendReqMoveMessage,
            // which refuses to send a move for up to 500 ms after a go
            // so the server does not read the move as a rubber-band
            // (`BaseClient.cs:1627-1634`). A thumb resting on Go would
            // hold that window open for as long as it rested there.
            //
            // Nor is a repeat wanted on its own terms: a door is walked
            // through once, and walking through it twice is walking
            // back out.
            case ActionButtonType.Action:
                return cfg.Data is AvatarAction a && a == AvatarAction.Attack;
        }
        return false;
    }

    bool InPack(InventoryObject o)
    {
        var list = _data?.InventoryObjects;
        if (list == null) return false;
        foreach (InventoryObject i in list) if (i.ID == o.ID) return true;
        return false;
    }

    /// <summary>
    /// The input tick, `ControllerInput.cpp:995-1030`: while the press is
    /// down and past the delay, activate every frame. The library's
    /// interval (`GameTick.CanReqAttack/CanReqCast`) is the rate limit,
    /// exactly as it is for the reference.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_heldSlot < 0 || _outside) return;
        if (Time.GetTicksMsec() - _heldSince < RepeatDelayMs) return;

        ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(_heldNum);
        if (cfg == null || !Repeats(cfg)) return;
        _repeating = true;
        if (Send(cfg, true)) _sentInHold = true;
    }

    /// <summary>
    /// What a Go slot sends - the view's `SendReqGo(true)`, which is the
    /// same call its drawer tile makes.
    ///
    /// Supplied rather than called, for the reason the whole file is
    /// built this way: the client lives in <see cref="GameView"/> and
    /// this control does not hold one. The press still goes through
    /// <see cref="Run"/>, so a Go in the arc is gated and latched
    /// exactly as the tile is - HotbarAct with keepLatch false is
    /// WorldAct exactly. Null in a bare harness, where the slot then
    /// draws and does nothing, as an unwired Attack would.
    /// </summary>
    public Action GoSend;

    /// <summary>
    /// What the Phase seat sends: the view's `_client.SendReqCastMessage
    /// (SpellObject)` (`BaseClient.cs:1717`), which is the method the
    /// spell book's cast reaches (`GameView` wires `_book.Cast` to the
    /// uint overload at `:1702`, which looks the object up and calls
    /// this one at `:1709`) and the method a bound Spell seat's
    /// Activate reaches (`BaseClient.OnActionButtonActivated`,
    /// `:284-285`). One send, three ways in. Supplied, not called, for
    /// the reason GoSend is; null in a bare harness.
    /// </summary>
    public Action<SpellObject> CastSend;

    /// <summary>Raised when the padlock is tapped, with the new state, for the chat line.</summary>
    public event Action<bool> Locked;

    /// <summary>
    /// The player's Phase spell, by name, case-insensitively: the spell
    /// list's names are resources that resolve after the ids arrive, so
    /// a character's "phase" is compared as text once it is there. Null
    /// until then, and for a character who has not learnt it.
    /// </summary>
    static SpellObject FindPhase(DataController data)
    {
        if (data?.SpellObjects == null) return null;
        foreach (SpellObject s in data.SpellObjects)
            if (s != null && string.Equals(s.Name, PhaseName, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    /// <summary>The spell's name in the game's string table, lower case as the server has it.</summary>
    public const string PhaseName = "phase";

    /// <summary>
    /// The Phase seat's press: the spell, through Run, as every seat's
    /// send goes. Looked up at the press rather than held, for the
    /// reason Fire resolves a Num at the press: the list is replaced
    /// on a relogin and a held object would be one the server has
    /// forgotten. Nothing for a character without it - the seat is
    /// Disabled, which a finger honours, and this is the guard for the
    /// harness's emitted Pressed, which Disabled does not stop.
    /// </summary>
    void PressPhase()
    {
        SpellObject spell = FindPhase(_data);
        if (spell == null) return;
        Action go = () =>
        {
            try { CastSend?.Invoke(spell); }
            catch (Exception e) { GD.PrintErr($"[ActionButtons] phase: {e.Message}"); }
        };
        if (Run != null) Run(go, false);
        else go();
    }

    /// <summary>
    /// The one press path, for a second control that shows the same
    /// seats (HotkeyDock). A tap is <c>Perform(cfg, false)</c>; a frame
    /// of a held Attack is <c>Perform(cfg, true)</c>, which keeps the
    /// self-target latch alive exactly as the cluster's own hold does.
    /// Nothing else is exposed: what a seat DOES is decided here and in
    /// BaseClient, once, and a box that drew the seats with a dispatch
    /// of its own would be a second answer to "what does Rest send".
    /// </summary>
    public bool Perform(ActionButtonConfig cfg, bool repeat) => cfg != null && Send(cfg, repeat);

    /// <summary>The game's Attack action, the one binding a held press repeats everywhere.</summary>
    public static bool IsAttack(ActionButtonConfig cfg) =>
        cfg != null && cfg.ButtonType == ActionButtonType.Action
        && cfg.Data is AvatarAction a && a == AvatarAction.Attack;

    bool Send(ActionButtonConfig cfg, bool keepLatch)
    {
        bool sent = true;
        // The one slot whose press is not the library's dispatch. The
        // config is in the client's list and BaseClient IS subscribed to
        // it, so Activate would be raised and handled - ExecAction with
        // AvatarAction.None, which does nothing (see Extra). Calling the
        // view's send instead of Activate is what makes it do something,
        // and is also why there is no double-send to worry about.
        bool isGo = IsGo(cfg);
        Action go = () =>
        {
            try { if (isGo) GoSend?.Invoke(); else cfg.Activate(); }
            catch (Exception e) { GD.PrintErr($"[ActionButtons] {cfg?.Name}: {e.Message}"); }
        };
        if (Run != null) sent = Run(go, keepLatch);
        else go();
        return sent;
    }

    /// <summary>
    /// A tap, or the release of a press that did not repeat. The screen
    /// slot says which button number is showing there right now, and that
    /// number is looked up in the client's own list; the dispatch lives
    /// in BaseClient, which is subscribed to every button in the list.
    /// </summary>
    void Fire(int slot)
    {
        // A repeat already fired for this press, or it was pulled off:
        // the release is not another tap. (A scripted press arrives with
        // no press-down and passes straight through.)
        bool held = _heldSlot == slot;
        if (_repeating || _outside) { EndHold(); return; }
        if (held) EndHold();

        if (slot < 0 || slot >= _nums.Count) return;
        ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(_nums[slot]);
        if (cfg == null) return;
        Send(cfg, false);
    }

    /// <summary>
    /// The button's picture.
    ///
    /// Spells and skills get one as well as items. The game composes all
    /// three (`UIActionButtons.cpp:244-332`) - it takes a different route
    /// for spells and skills, blitting a frame straight out of the
    /// resource rather than going through the image composer, but its own
    /// comment calls that a hack for resolution, not a different picture.
    /// Composing them the same way items are composed is the same answer
    /// through one path instead of two.
    ///
    /// A button with no picture keeps its short label; that is every
    /// action button, which is what the seeded row is made of.
    ///
    /// Keyed on everything the composed picture depends on rather than on
    /// the file alone. The cache used to be keyed on the filename, so an
    /// item that was dyed, took an effect or simply animated kept the
    /// first picture it was ever drawn with for the rest of the session -
    /// where the game re-pushes the texture every time the object changes.
    /// </summary>
    Texture2D Icon(ActionButtonConfig cfg) => Icon(cfg, IconPx);

    Texture2D Icon(ActionButtonConfig cfg, int px)
    {
        // An alias has no game object behind it and so nothing to
        // compose, which is why the reference gives it a fixed picture
        // instead: the one branch of ActionButtonChange that does not
        // touch an image composer just sets UI_IMAGE_ALIAS_ICON on the
        // button (`UIActionButtons.cpp:334-338`). See AliasIcon for
        // where that picture comes from.
        if (cfg.ButtonType == ActionButtonType.Alias) return AliasIcon();

        if (cfg.ButtonType == ActionButtonType.Action ||
            cfg.ButtonType == ActionButtonType.Unset) return null;
        if (cfg.Data is not ObjectBase o) return null;
        return Icon(o, px);
    }

    /// <summary>The composed picture of a game object - a seat's data, or the Phase spell itself - at a pixel edge.</summary>
    Texture2D Icon(ObjectBase o, int px)
    {
        if (o?.Resource == null) return null;

        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        // The composed size is in the key, so a scaled cluster composes
        // its pictures again rather than stretching the small ones.
        string key = $"{o.Resource.Filename}:{frame}:{px}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, px)); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }

    /// <summary>
    /// The reference's alias picture, shared by the hotbar button and by
    /// the alias editor's own bind button - both places the reference
    /// draws it (`UIActionButtons.cpp:337`, `UIOptions.cpp:1082`, both
    /// naming UI_IMAGE_ALIAS_ICON, which `Constants.h:331` defines as
    /// "TaharezLook/AliasIcon").
    ///
    /// That name is a CEGUI imageset entry, not a game resource: a
    /// 24x24 rectangle at (133,304) of the UI sheet
    /// (`Resources/ui/imagesets/TaharezLook.imageset:314`), and what is
    /// in that rectangle is a letter A. There is no CEGUI here to ask
    /// for it, so it is cut out of the repo's own sheet into
    /// art/alias.png rather than invented - the same thing MiniMap does
    /// with the dial it draws on.
    ///
    /// Loaded once and kept, because it never changes and every alias
    /// button shows the same one. A null is survivable and deliberately
    /// not treated as an error: the caller falls back to the key as
    /// text, which is the better half of the label anyway.
    /// </summary>
    static Texture2D _aliasIcon;
    static bool _aliasIconTried;
    public static Texture2D AliasIcon()
    {
        if (_aliasIconTried) return _aliasIcon;
        _aliasIconTried = true;
        try { _aliasIcon = GD.Load<Texture2D>("res://art/alias.png"); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] alias icon: {e.Message}"); }
        return _aliasIcon;
    }

    static string Short(string s, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(s)) return "?";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max);
    }

    Button Take(int index)
    {
        while (_pool.Count <= index)
        {
            var b = new Button { Visible = false, ClipText = true };
            M59Skin.Dress(b, M59Skin.Kind.Slot);
            // A placeholder: the rebuild sets the real size, scaled.
            b.AddThemeFontSizeOverride("font_size", FontSize);
            // An alias shows its picture AND its key, and a 24px
            // sprite with a word beside it does not fit a 96 circle.
            // Godot has no vertical-icon flag on Button, so the gap
            // between them is pulled to nothing and the caption is
            // clipped rather than pushing the icon off the cell.
            b.AddThemeConstantOverride("h_separation", 2);
            AddChild(b);
            _pool.Add(b);
        }
        return _pool[index];
    }

    /// <summary>The page stepper's diameter. Over 44, under a seat's.</summary>
    const float TurnSize = 72f;

    /// <summary>
    /// The slot's own frame, over the skin's Slot dress.
    ///
    /// Done here rather than in the skin because the skin's Slot is the
    /// inventory's, where a cell is pressed to CHOOSE and the pressed
    /// state is a selection. Here a press is a cast or a swing and a
    /// held press repeats, so the pressed state has to be the loudest
    /// thing on the cluster: gold rim, lifted fill. The cell is
    /// near-opaque for the same reason the plates above are - this sits
    /// over the floor, not over a panel.
    ///
    /// ROUND, now, and round is not decoration. A circle is what tells
    /// the thumb that this is the combat cluster and not a panel or a
    /// list, every action game on a phone draws it that way, and a ring
    /// at the primary's size is also the only honest way to show a seat
    /// with nothing in it (see <see cref="Ring"/>).
    ///
    /// Three dresses, one shape. The primary carries the skin's Primary
    /// colours with a gold rim at every size, because it IS the one thing
    /// the cluster is for; a bound seat keeps the lit rim and the opaque
    /// cell, so it reads as holding something; Next is dressed apart, dim
    /// gold on a darker cell, because it acquires rather than acts.
    /// </summary>
    /// <summary>
    /// A rim width at the player's size. A two-pixel rim on a cell half
    /// again as wide is a hairline, so the frame scales with the cell.
    /// </summary>
    static int Rim(int at1, float sc) => Mathf.Max(1, Mathf.RoundToInt(at1 * sc));

    static void SlotEdge(Button b, float d, bool primary, bool target = false, float sc = 1f)
    {
        StyleBoxFlat normal, down;
        if (primary)
        {
            normal = Cell(M59Skin.Gold, new Color(0.286f, 0.231f, 0.129f, 0.96f), Rim(3, sc), d);
            down = Cell(M59Skin.GoldBright, new Color(0.420f, 0.329f, 0.169f, 0.98f), Rim(4, sc), d);
            b.AddThemeColorOverride("font_color", M59Skin.GoldBright);
            b.AddThemeColorOverride("font_pressed_color", M59Skin.GoldBright);
            b.AddThemeColorOverride("font_hover_color", M59Skin.GoldBright);
            b.AddThemeColorOverride("font_disabled_color", M59Skin.TextOff);
        }
        else if (target)
        {
            normal = Cell(M59Skin.GoldDim, new Color(0.110f, 0.100f, 0.086f, 0.94f), Rim(2, sc), d);
            down = Cell(M59Skin.Gold, M59Skin.RowPick, Rim(3, sc), d);
            b.AddThemeColorOverride("font_color", M59Skin.Gold);
            b.AddThemeColorOverride("font_pressed_color", M59Skin.GoldBright);
            b.AddThemeColorOverride("font_hover_color", M59Skin.Gold);
        }
        else
        {
            normal = Cell(M59Skin.EdgeLit, new Color(0.078f, 0.071f, 0.063f, 0.94f), Rim(2, sc), d);
            down = Cell(M59Skin.Gold, M59Skin.RowPick, Rim(3, sc), d);
            b.AddThemeColorOverride("font_color", M59Skin.Text);
            b.AddThemeColorOverride("font_pressed_color", M59Skin.GoldBright);
            b.AddThemeColorOverride("font_hover_color", M59Skin.Text);
        }
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal);
        b.AddThemeStyleboxOverride("focus", normal);
        b.AddThemeStyleboxOverride("pressed", down);
    }

    /// <summary>
    /// A round cell. <paramref name="d"/> is the diameter: a corner
    /// radius of half the box is what makes a StyleBoxFlat a circle, and
    /// passing it in rather than hard-coding 6 is the whole difference
    /// between a rounded square and the cluster.
    /// </summary>
    static StyleBoxFlat Cell(Color edge, Color fill, int width, float d = 12f)
    {
        var s = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)(d * 0.5f);
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = width;
        s.BorderColor = edge;
        return s;
    }

    /// <summary>
    /// An empty seat: a hollow ring where a binding would sit.
    ///
    /// A Panel and not a Button, and the mouse filter is the point. An
    /// empty Button would eat the tap aimed at whatever is standing
    /// behind it, which is the reason this file drew nothing at all here
    /// for so long; a Panel set to ignore is seen and not felt. At a
    /// third of the rim's strength and with no caption it says "a seat"
    /// without saying it louder than the bindings beside it.
    /// </summary>
    void Ring(int index, Rect2 cell, float sc = 1f)
    {
        while (_rings.Count <= index)
        {
            var p = new Panel { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            // Behind the buttons: added later than the pool would draw
            // it over them, and a ring over a bound slot is a smudge.
            AddChild(p);
            MoveChild(p, 0);
            _rings.Add(p);
        }
        Panel ring = _rings[index];
        var s = Cell(new Color(M59Skin.Rule.R, M59Skin.Rule.G, M59Skin.Rule.B, 0.35f),
                     new Color(0f, 0f, 0f, 0.18f), Rim(2, sc), cell.Size.X);
        ring.AddThemeStyleboxOverride("panel", s);
        ring.Position = cell.Position;
        ring.Size = cell.Size;
        ring.Visible = true;
    }

    /// <summary>
    /// Asks the library for the next target, through the same gate every
    /// other press here goes through. See where it is placed for why it
    /// is in this file at all.
    /// </summary>
    void PressNext()
    {
        Action go = () =>
        {
            try { _data?.NextTarget(); Retargeted?.Invoke(); }
            catch (Exception e) { GD.PrintErr($"[ActionButtons] next: {e.Message}"); }
        };
        if (Run != null) Run(go, false);
        else go();
    }

    /// <summary>
    /// Prints every control's rect, with M59HUDRECTS set.
    ///
    /// Not decoration and not a debug leftover: the rules this layout is
    /// built to - 44pt targets, 8pt between them, 16 near an edge, the
    /// primary inside the bottom 40% - are claims about numbers, and
    /// numbers are not a thing to eyeball off a screenshot. A layout that
    /// cannot be measured gets measured by the player, in a fight.
    /// </summary>
    void Measure(Vector2 v, ActionButtonConfig anchor, int count, bool paged)
    {
        if (OS.GetEnvironment("M59HUDRECTS") == "") return;

        void Say(string what, Rect2 r) => GD.Print(
            $"[hud] {what,-10} {r.Position.X,6:0},{r.Position.Y,6:0} {r.Size.X,4:0}x{r.Size.Y,4:0}" +
            $"  edge r={v.X - r.Position.X - r.Size.X,4:0} b={v.Y - r.Position.Y - r.Size.Y,4:0}" +
            $"  up={(v.Y - r.Position.Y - r.Size.Y * 0.5f) / v.Y * 100f,4:0}%");

        GD.Print($"[hud] viewport {v.X}x{v.Y} reserve b={BottomReserve} l={LeftReserve}" +
                 $" ceiling={Ceiling:0} page={_page + 1} paged={paged}" +
                 $" scale={HudScale():0.00} shift={_shift.X:0},{_shift.Y:0}");
        // Every round control as (centre, diameter), so the gaps below
        // can be rim to rim - which is what a thumb feels, not centre to
        // centre - and so the overlap count is over circles, not boxes.
        var discs = new List<(string name, Vector2 c, float d)>();
        discs.Add((anchor != null ? "ATTACK" : "attack(-)", Pivot(v), Atk));
        // The outer arc, in binding order (bottom-left to top).
        for (int i = 0; i < HotSeats; i++)
            discs.Add((i < count ? $"seat{i + 1}" : $"seat{i + 1}(-)", ArcSeat(v, i), Btn));
        // The inner arc, top to bottom.
        discs.Add(("next", NextSeat(v), Btn));
        discs.Add(("phase", PhaseSeat(v), Btn));
        if (paged) discs.Add(("page", TurnSeat(v), Turn));
        discs.Add(("door", DoorSeat(v), Btn));
        foreach (var (name, c, d) in discs) Say(name, Round(c, d));
        Rect2 pad = LockRect(v);
        Say("lock", pad);

        // The gaps the rules are about. The inner arc's gap to the outer
        // is the radial one, which is a floor on the true gap to any seat.
        float rim = Arc - Atk * 0.5f - Btn * 0.5f;
        float step = (Seat(v, 1) - Seat(v, 0)).Length() - Btn;
        float inner = InnerR * HudScale();
        float innerDisc = inner - Atk * 0.5f - Btn * 0.5f;
        float innerArc = Arc - Btn * 0.5f - inner - Btn * 0.5f;
        float nextPhase = (NextSeat(v) - PhaseSeat(v)).Length() - Btn;
        float phaseTurn = (PhaseSeat(v) - TurnSeat(v)).Length() - Btn * 0.5f - Turn * 0.5f;
        float turnDoor = (TurnSeat(v) - DoorSeat(v)).Length() - Turn * 0.5f - Btn * 0.5f;
        float phaseDoor = (PhaseSeat(v) - DoorSeat(v)).Length() - Btn;
        // The nearest outer seat to each inner control, rim to rim.
        float Nearest(Vector2 c, float d)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ArcSeats; i++)
                best = Mathf.Min(best, (Seat(v, i) - c).Length() - Btn * 0.5f - d * 0.5f);
            return best;
        }
        // The lock is a square beside a circle: its nearest corner to the
        // disc's centre, less the disc's radius.
        Vector2 lockNear = new Vector2(pad.Position.X, Mathf.Clamp(Pivot(v).Y, pad.Position.Y, pad.End.Y));
        float lockGap = (lockNear - Pivot(v)).Length() - Atk * 0.5f;
        GD.Print($"[hud] gaps primary-to-arc={rim:0} arc-to-arc={step:0}" +
                 $" primary-to-inner={innerDisc:0} inner-to-arc={innerArc:0}" +
                 $" next-to-phase={nextPhase:0} phase-to-page={phaseTurn:0} page-to-door={turnDoor:0} phase-to-door={phaseDoor:0}" +
                 $" next-to-seat={Nearest(NextSeat(v), Btn):0} phase-to-seat={Nearest(PhaseSeat(v), Btn):0}" +
                 $" page-to-seat={Nearest(TurnSeat(v), Turn):0} door-to-seat={Nearest(DoorSeat(v), Btn):0}" +
                 $" primary-to-lock={lockGap:0} lock-to-edge={v.X - pad.End.X:0} next-to-edge={v.X - Round(NextSeat(v), Btn).End.X:0}");

        // The two claims the whole layout rests on, counted rather than
        // eyeballed: nothing under the tap floor, nothing touching.
        int small = 0, overlaps = 0;
        foreach (var (_, _, d) in discs) if (d < TapFloor) small++;
        if (pad.Size.X < TapFloor) small++;
        for (int i = 0; i < discs.Count; i++)
            for (int j = i + 1; j < discs.Count; j++)
                if ((discs[i].c - discs[j].c).Length() < (discs[i].d + discs[j].d) * 0.5f)
                { overlaps++; GD.Print($"[hud] OVERLAP {discs[i].name} x {discs[j].name}"); }
        foreach (var (name, c, d) in discs)
        {
            // Circle against the lock's square: the nearest point of the
            // square to the centre, inside the radius.
            Vector2 q = new Vector2(Mathf.Clamp(c.X, pad.Position.X, pad.End.X), Mathf.Clamp(c.Y, pad.Position.Y, pad.End.Y));
            if ((q - c).Length() < d * 0.5f) { overlaps++; GD.Print($"[hud] OVERLAP {name} x lock"); }
        }
        Rect2 all = pad;
        foreach (var (_, c, d) in discs) all = all.Merge(Round(c, d));
        bool off = all.Position.X < 0 || all.Position.Y < 0 || all.End.X > v.X || all.End.Y > v.Y;
        GD.Print($"[hud] controls={discs.Count + 1} under44={small} overlaps={overlaps} offglass={(off ? 1 : 0)}" +
                 $" box={all.Position.X:0},{all.Position.Y:0} {all.Size.X:0}x{all.Size.Y:0}");
    }

    /// <summary>Which page of bindings the row is showing.</summary>
    int _page;
    /// <summary>
    /// How many pages the last rebuild found. The stepper is built once
    /// and its handler outlives any one layout, so it cannot close over
    /// a local.
    /// </summary>
    int _pages = 1;

    /// <summary>The page button, when there is more than one page.</summary>
    Button _turn;

    /// <summary>The target control. Not a binding - see where it is placed.</summary>
    Button _nextBtn;
    Button _doorBtn;
    /// <summary>The Phase seat. Not a binding either - see where it is placed.</summary>
    Button _phaseBtn;
    Label _phaseCap;
    /// <summary>The padlock, and the glyph drawn on it.</summary>
    Button _lockBtn;
    LockGlyph _lockGlyph;
    /// <summary>The pool slot the primary is in this layout, or -1. OnUp spares it.</summary>
    int _primarySlot = -1;

    /// <summary>
    /// A padlock, drawn. A square body with a shackle over it; open, the
    /// shackle lifts and swings its free leg clear of the body, which is
    /// the shape every lock-screen icon uses and the one a glance reads
    /// without a caption. Drawn in the button's own rect, scaled to it,
    /// so it follows the player's size with the rest of the cluster.
    /// </summary>
    sealed partial class LockGlyph : Control
    {
        public bool Open;
        public Color Tint = Colors.White;

        public override void _Draw()
        {
            float s = Mathf.Min(Size.X, Size.Y);
            float w = Mathf.Max(1.5f, s * 0.08f);
            Vector2 c = Size * 0.5f;
            // The body: a square a little wider than tall, in the lower
            // half, so the shackle has the upper half to stand in.
            float bw = s * 0.46f, bh = s * 0.34f;
            var body = new Rect2(c.X - bw * 0.5f, c.Y + s * 0.02f, bw, bh);
            DrawRect(body, Tint, true);
            // The keyhole: a dark dot in the body's face.
            DrawCircle(new Vector2(c.X, body.Position.Y + bh * 0.45f), w * 1.1f, new Color(0.157f, 0.141f, 0.118f));
            // The shackle: a half ring a third of the body's width,
            // standing on the body. Closed, both legs reach the body.
            // Open, the whole shackle is lifted by most of its radius and
            // the right leg stops short of the body - the gap is the sign.
            float r = bw * 0.34f;
            float lift = Open ? r * 0.9f : 0f;
            float topY = body.Position.Y - r - lift;
            DrawArc(new Vector2(c.X, topY), r, Mathf.Pi, Mathf.Tau, 16, Tint, w);
            DrawLine(new Vector2(c.X - r, topY), new Vector2(c.X - r, body.Position.Y), Tint, w);
            float legEnd = Open ? topY + r * 0.6f : body.Position.Y;
            DrawLine(new Vector2(c.X + r, topY), new Vector2(c.X + r, legEnd), Tint, w);
        }
    }

    void HideFrom(int from)
    {
        for (int i = from; i < _pool.Count; i++) _pool[i].Visible = false;
    }
}
