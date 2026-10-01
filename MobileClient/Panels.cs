using Godot;

/// <summary>
/// Shared behaviour for the full-screen panels.
///
/// There is only one rule here so far, and it is one the game states
/// plainly: a window that becomes visible is brought to the front
/// (`UIBuy.cpp:66`, `UITrade.cpp:80`, `UIObjectContents.cpp:61` all call
/// <c>moveToFront</c>).
///
/// On a desktop that is a nicety, because the windows are small and you
/// can see the one behind. Here it is the difference between working and
/// not. Every panel is a child of the same layer and covers most of the
/// screen, and Godot draws siblings in tree order - which is the order
/// GameView happened to build them in. So opening the spell book while
/// the bag was open put the book *behind* the bag: the button worked,
/// the panel was visible, and nothing appeared to happen. Several panels
/// in a row could pile up unseen.
/// </summary>
public static class Panels
{
    /// <summary>
    /// The buttons that open the panels - WHERE they go as well as
    /// whether they show.
    ///
    /// Each one still belongs to the panel it opens, which is right: a
    /// panel that hides itself should take its own button with it. What
    /// no single panel could do is hide the OTHERS, so they were
    /// registered here and the view hid the row as a row.
    ///
    /// They were also PLACED one by one, and that is what this file now
    /// takes over. Every panel counted its own seat from the right edge
    /// of the screen - `v.X - 70f - pad - (76f + 8f) * 2f` in the bag,
    /// a `ButtonRight` of `12f + 70f + 8f + (76f + 8f) * 7f + 96f + 8f`
    /// handed to the actions panel from the view - so fourteen files
    /// each held one term of the same sum, and adding a button meant
    /// editing all of them. The arithmetic was wrong in the obvious way
    /// at least once: Settings is 96 wide where the rest are 76, and
    /// "Go" was drawn straight through it as "SetGoings".
    ///
    /// Worse than brittle, the answer it computed was the wrong shape:
    /// fourteen small text buttons along the bottom edge, which is a
    /// desktop menu bar on a screen where the bottom edge is where both
    /// thumbs already are. They are a DRAWER now - see
    /// <see cref="MenuDrawer"/> - and a panel says only what its button
    /// is for and roughly where it belongs in the list.
    /// </summary>
    /// <summary>
    /// The margin a panel lays itself out inside.
    ///
    /// This is the plain thing it always was - a share of the width,
    /// never under sixteen - and it is used on BOTH axes by most
    /// panels, which is the whole story of a bad hour: a landscape
    /// version of this added enough margin to centre the content in a
    /// band, and every panel that used `side` for its top edge too
    /// pushed its content four hundred pixels down the screen. On a
    /// sideways phone that is nearly half the height, and the
    /// inventory showed one row of a hundred items.
    ///
    /// Width is banded where it is worth banding, by the panel, with
    /// <see cref="Band"/> - which is horizontal and says so.
    /// </summary>
    public static float Side(Vector2 v, float fraction, float least = 16f)
        => Mathf.Max(least, v.X * fraction);

    /// <summary>
    /// Content width for a panel that would otherwise stretch a row
    /// across a sideways screen - a name at one end and a number at
    /// the other, two thousand pixels apart, is a row nobody reads in
    /// one glance. Portrait gets the full width, as before.
    ///
    /// Horizontal only. Nothing here belongs anywhere near a Y.
    /// </summary>
    public static float Band(Vector2 v, float side)
    {
        float usable = v.X - side * 2f;
        return Mathf.Min(usable, Mathf.Max(900f, v.Y * 1.45f));
    }

    /// <summary>The left edge of that band, centred on the screen.</summary>
    public static float BandLeft(Vector2 v, float side)
        => side + Mathf.Max(0f, (v.X - side * 2f - Band(v, side)) * 0.5f);

    /// <summary>Where an opener lives, now that this file places them.</summary>
    public enum Where
    {
        /// <summary>
        /// A tile in the drawer's grid. Nearly everything: a panel you
        /// open a few times an hour has no claim on the glass.
        /// </summary>
        Drawer,
        /// <summary>
        /// The top band, beside the Menu control. For something used
        /// constantly that is NOT combat and is not movement - the top
        /// quarter of the screen is the one place a control is never in
        /// the way of a thumb, which is also why the menu lives there.
        /// </summary>
        Top,
        /// <summary>
        /// The left edge, by the thumb that steers. Movement only: a
        /// control that changes where you are walking belongs under the
        /// hand that is walking you, not across the screen.
        /// </summary>
        Left,
        /// <summary>
        /// Registered so the row can be hidden as a row, but placed by
        /// whoever owns it.
        ///
        /// One case, and it earns the exception: Say and Log sit in the
        /// chat entry's own rectangle and SWAP with it - pressing Say
        /// replaces both with the text box and the recall arrow, in the
        /// same place and at the same height. Placing them from here
        /// would mean owning the entry's geometry too, and then the
        /// chat's layout would live in two files, which is the thing
        /// this change exists to stop.
        /// </summary>
        Owner,
    }

    internal struct Seat
    {
        public Button Button;
        /// <summary>What it opens, in words, under the tile. May be null.</summary>
        public string What;
        public int Order;
        public Where Where;
    }

    static readonly System.Collections.Generic.List<Seat> Seats =
        new System.Collections.Generic.List<Seat>();

    /// <summary>Set when the list changes, so the drawer relays itself out.</summary>
    static bool _dirty;

    /// <summary>The drawer, once the view has mounted it. Null before that.</summary>
    public static MenuDrawer Drawer { get; private set; }

    /// <summary>
    /// Called by a panel once, for the button that opens it.
    /// </summary>
    /// <param name="what">
    /// What the button opens, in plain words, for the caption under its
    /// tile. "Book" and "Me" and "Acts" are each a word you have to
    /// translate before you can press them, which is a reading task in
    /// the middle of a fight; the caption is what makes the grid
    /// glanceable. The button's own Text is left exactly as it was -
    /// the harness presses by text and every note in `notes/` names
    /// these buttons by it.
    /// </param>
    /// <param name="order">
    /// Where it sits in the grid, low first. Grouped by what you are
    /// doing rather than alphabetically, and spaced so a new entry can
    /// land between two without renumbering.
    /// </param>
    public static void Opener(Button b, string what = null, int order = 500,
                              Where where = Where.Drawer)
    {
        if (b == null) return;
        for (int i = 0; i < Seats.Count; i++) if (Seats[i].Button == b) return;
        Seats.Add(new Seat { Button = b, What = what, Order = order, Where = where });
        _dirty = true;
    }

    /// <summary>
    /// Builds the drawer, under <paramref name="parent"/> - the view's
    /// UI layer, which is also what every opener and the ConfirmPopup
    /// are under, so tree order means the same thing for all of them.
    /// Called once; calling it again returns what is already there.
    /// </summary>
    public static MenuDrawer Mount(Node parent)
    {
        if (Drawer != null && GodotObject.IsInstanceValid(Drawer)) return Drawer;
        if (parent == null) return null;
        Drawer = new MenuDrawer { Name = "menuDrawer" };
        parent.AddChild(Drawer);
        _dirty = true;
        return Drawer;
    }

    /// <summary>Whether the drawer is up. False when there is no drawer.</summary>
    public static bool DrawerOpen =>
        Drawer != null && GodotObject.IsInstanceValid(Drawer) && Drawer.IsOpen;

    /// <summary>
    /// Shows or hides the openers. Safe to call every frame: a panel
    /// that is open makes the caller pass false anyway, so this never
    /// fights a panel over its own button.
    ///
    /// The drawer goes with them - a panel opened from a tile has to
    /// take the grid down behind it, and so does the chat box.
    /// </summary>
    public static void ShowOpeners(bool on)
    {
        if (!on && DrawerOpen) Drawer.Close();
        bool drawerUp = DrawerOpen;

        for (int i = Seats.Count - 1; i >= 0; i--)
        {
            Button b = Seats[i].Button;
            if (!GodotObject.IsInstanceValid(b)) { Seats.RemoveAt(i); _dirty = true; continue; }
            // A tile is gated twice: by this flag and by the grid's own
            // host, which is hidden while the drawer is shut. The
            // pinned ones have nothing above them, so they are told to
            // stand down while the grid is over them.
            b.Visible = Seats[i].Where == Where.Drawer ? on : on && !drawerUp;
        }

        if (Drawer != null && GodotObject.IsInstanceValid(Drawer)) Drawer.Allowed(on);
    }

    // ---- placement -------------------------------------------------

    /// <summary>
    /// How far a control stays off the edge of the glass. The research
    /// the owner handed over: 8pt between targets, and 16pt when a
    /// target is within 80pt of a screen edge, which everything pinned
    /// here is.
    /// </summary>
    public const float Edge = 16f;

    /// <summary>
    /// How tall a pinned control is. Apple asks 44pt and Material 48dp;
    /// these are things pressed while the other thumb is busy, so they
    /// get more than either.
    /// </summary>
    public const float TapH = 60f;

    /// <summary>Between two pinned controls, which are all near an edge.</summary>
    public const float TapGap = 16f;

    /// <summary>The Menu control's width, and a pinned opener's.</summary>
    public const float MenuW = 150f, PinW = 118f;

    /// <summary>
    /// Lays out everything that is NOT a tile: the Menu control and the
    /// handful pinned to the glass. Called by the drawer, which is the
    /// only thing with a node and therefore the only thing that can
    /// hear the viewport change size.
    /// </summary>
    /// <param name="menu">The drawer's own control, placed first in the top band.</param>
    /// <summary>Dressed once, the first time it is placed.</summary>
    static readonly System.Collections.Generic.HashSet<Button> Dressed =
        new System.Collections.Generic.HashSet<Button>();

    /// <summary>
    /// The house look for something pinned to the glass. These were
    /// bare Godot buttons at 16pt, which over a torchlit stone wall is
    /// a word with no edge: at a glance there is nothing there to
    /// press. Everything else in this client is an M59Skin control and
    /// these are the two the player sees most.
    /// </summary>
    static void DressPinned(Button b)
    {
        if (b == null || !Dressed.Add(b)) return;
        M59Skin.Dress(b, M59Skin.Kind.Secondary);
        if (b.ToggleMode) M59Skin.Latch(b);
        b.AddThemeFontSizeOverride("font_size", M59Skin.BodySize + 2);
        b.ClipText = true;
    }

    public static void PlacePinned(Vector2 v, Button menu)
    {
        // The top band, centred: Menu, then whatever is pinned beside
        // it. Seats are computed from the FULL group whether or not
        // every member is visible, so hiding one does not slide the
        // others out from under a thumb that was already moving.
        float width = MenuW;
        int top = 0;
        foreach (Seat s in Ordered(Where.Top)) { width += TapGap + PinW; top++; }

        float x = Mathf.Round((v.X - width) * 0.5f);
        if (menu != null)
        {
            menu.Position = new Vector2(x, Edge);
            menu.Size = new Vector2(MenuW, TapH);
        }
        x += MenuW + TapGap;
        foreach (Seat s in Ordered(Where.Top))
        {
            DressPinned(s.Button);
            s.Button.Position = new Vector2(x, Edge);
            s.Button.Size = new Vector2(PinW, TapH);
            x += PinW + TapGap;
        }

        // The left edge, at the height of the hand rather than at the
        // bottom of it: the stick is a floating one that appears
        // wherever the thumb lands on the left half, so a button down
        // in the corner where the thumb RESTS would eat the gesture the
        // client exists for. Half way up the edge is within reach and
        // is not where anyone plants a thumb to walk.
        var left = new System.Collections.Generic.List<Seat>(Ordered(Where.Left));
        if (left.Count > 0)
        {
            float h = left.Count * TapH + (left.Count - 1) * TapGap;
            float y = Mathf.Round(v.Y * 0.46f - h * 0.5f);
            foreach (Seat s in left)
            {
                DressPinned(s.Button);
                s.Button.Position = new Vector2(Edge, y);
                s.Button.Size = new Vector2(PinW, TapH);
                y += TapH + TapGap;
            }
        }
    }

    /// <summary>The seats in one place, in the order the panels asked for.</summary>
    internal static System.Collections.Generic.IEnumerable<Seat> Ordered(Where where)
    {
        var list = new System.Collections.Generic.List<Seat>();
        foreach (Seat s in Seats)
            if (s.Where == where && GodotObject.IsInstanceValid(s.Button)) list.Add(s);
        list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order)
                                               : string.CompareOrdinal(a.Button.Text, b.Button.Text));
        return list;
    }

    /// <summary>True once, after the registry changed - the drawer rebuilds on it.</summary>
    internal static bool TakeDirty()
    {
        if (!_dirty) return false;
        _dirty = false;
        return true;
    }

    /// <summary>
    /// Puts this panel above its siblings. Called when it opens - and
    /// puts an open <see cref="ConfirmPopup"/> back above the panel.
    ///
    /// The reference's popup is AlwaysOnTop, both the root window and the
    /// framed one (`Meridian59.layout:2859`, `:2873`), and it is not modal
    /// (`UIConfirmPopup.cpp`), so it moves to front once, when shown, and
    /// nothing else is ever drawn over a question that is waiting. Here
    /// "on top" is "last child", so the same guarantee is two things:
    /// this method, which is where almost every panel raises itself, and
    /// <see cref="ConfirmPopup"/>'s own watch on its parent's child order,
    /// which catches everything that does NOT come through here - a
    /// panel's own `MoveChild(this, -1)` (AmountPrompt, ChatOverlay,
    /// LookPanel, ... still do), and every `AddChild` of a panel built
    /// after the popup, which lands after it in the tree and so on top of it.
    /// </summary>
    public static void ToFront(Control panel)
    {
        Node parent = panel?.GetParent();
        if (parent == null) return;
        // -1 is last child, which is drawn last, which is on top.
        if (parent.GetChildCount() > 0) parent.MoveChild(panel, -1);
        KeepPopupOnTop(parent);
    }

    /// <summary>
    /// Makes an open ConfirmPopup the last child of <paramref name="parent"/>
    /// if it is not already. Immediate, so a panel raised through
    /// <see cref="ToFront"/> is never drawn over the popup even for a frame.
    /// </summary>
    public static void KeepPopupOnTop(Node parent)
    {
        if (parent == null) return;
        int n = parent.GetChildCount();
        if (n < 2) return;
        // The popup is the last child when it is where it belongs, so the
        // common case is one comparison. Otherwise look for it.
        if (parent.GetChild(n - 1) is ConfirmPopup) return;
        for (int i = n - 2; i >= 0; i--)
            if (parent.GetChild(i) is ConfirmPopup p)
            {
                if (p.IsOpen) parent.MoveChild(p, -1);
                return;
            }
    }
}
