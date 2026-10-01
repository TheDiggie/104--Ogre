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
    /// <summary>
    /// Whether the opener row is currently shown. The minimap's zoom
    /// and size buttons are not openers - they belong to the dial - but
    /// they have to come and go with the row, so they ask.
    /// </summary>
    public static bool OpenersShown { get; private set; } = true;

    public static void ShowOpeners(bool on)
    {
        OpenersShown = on;
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
            // The player's own choice, ANDed on after the client's
            // reasons - never instead of them. Which piece decides
            // depends on where the seat is: the pinned ones ARE the
            // "openers" piece, while an Owner seat is Say, which sits in
            // the chat's own row and belongs to that piece. A tile
            // belongs to neither - it lives in the drawer, which is a
            // panel. This is where it has to happen for the Owner seat
            // too: its owner's write would be undone the next frame,
            // because this runs every frame.
            if (Seats[i].Where == Where.Top)
            {
                if (!M59Hud.Shows(TopId)) b.Visible = false;
            }
            else if (Seats[i].Where == Where.Left)
            {
                if (!M59Hud.Shows(SideId)) b.Visible = false;
            }
            else if (Seats[i].Where == Where.Owner && !M59Hud.Shows("chat")) b.Visible = false;
        }

        if (Drawer != null && GodotObject.IsInstanceValid(Drawer)) Drawer.Allowed(on);
        // After Allowed, which sets the Menu control's own Visible every
        // time this runs: the player's hide has to be applied on top of
        // it or it would be undone the next frame.
        if (_menu != null && GodotObject.IsInstanceValid(_menu) && !M59Hud.Shows(TopId))
            _menu.Visible = false;
    }

    /// <summary>
    /// The Menu control, kept from <see cref="PlacePinned"/>. It belongs
    /// to <see cref="MenuDrawer"/>, which is the only thing that builds
    /// one; this file is the only thing that places it, and the player's
    /// piece covers it, so the reference is held here rather than asked
    /// for twice.
    /// </summary>
    static Button _menu;

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

    /// <summary>
    /// The smallest a pinned control may become. Everything here is
    /// pressed, several of them while the other thumb is busy, so the
    /// scale's floor of 0.7 is not allowed to take them under the 44
    /// points a thumb needs.
    /// </summary>
    const float TapFloor = 44f;

    /// <summary>
    /// The two pinned pieces. The top band and the left edge used to be
    /// one, which gave the store an L-shaped bounding box running from the
    /// top of the screen to half way down it - so the piece permanently
    /// "overlapped" the status bar and the editor flagged it on every
    /// launch for a crossing that does not exist. They are also what a
    /// player would separate first: the band is Menu and Map, the edge is
    /// Auto beside the walking thumb.
    /// </summary>
    const string TopId = "openers", SideId = "sidekeys";

    /// <summary>The player's size for a piece, inside the model's band.</summary>
    static float HudScale(string id)
    {
        M59Hud.Piece p = M59Hud.Get(id);
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>The size the pinned controls were last dressed at.</summary>
    static float _dressedAt = 1f, _dressedAtLeft = 1f;

    public static void PlacePinned(Vector2 v, Button menu)
    {
        _menu = menu;
        M59Hud.Register(TopId, "Menu buttons");
        M59Hud.Register(SideId, "Side buttons");

        // Everything a pinned control is made of scales together: its
        // height, its width, the gap between two of them and the font in
        // them. The gaps are what the research is actually about, so a
        // scaled group keeps its separation rather than crowding.
        float sc = HudScale(TopId);
        float tapH = Mathf.Max(TapFloor, TapH * sc);
        float gap = TapGap * sc;
        float menuW = Mathf.Max(TapFloor, MenuW * sc);
        float pinW = Mathf.Max(TapFloor, PinW * sc);

        // The edge column is its own piece, so it carries its own size.
        float lsc = HudScale(SideId);
        float ltapH = Mathf.Max(TapFloor, TapH * lsc);
        float lgap = TapGap * lsc;
        float lpinW = Mathf.Max(TapFloor, PinW * lsc);

        // The top band, centred: Menu, then whatever is pinned beside
        // it. Seats are computed from the FULL group whether or not
        // every member is visible, so hiding one does not slide the
        // others out from under a thumb that was already moving.
        float width = menuW;
        int top = 0;
        foreach (Seat s in Ordered(Where.Top)) { width += gap + pinW; top++; }

        var left = new System.Collections.Generic.List<Seat>(Ordered(Where.Left));
        float leftH = left.Count > 0 ? left.Count * ltapH + (left.Count - 1) * lgap : 0f;
        float leftY = Mathf.Round(v.Y * 0.46f - leftH * 0.5f);

        // THE NATURAL RECT of each piece is just that piece's own box -
        // the band across the top, the column down the edge - so a handle
        // sits on the buttons and nowhere else.
        float bandX = Mathf.Round((v.X - width) * 0.5f);
        Vector2 shift = M59Hud.Place(TopId, new Rect2(bandX, Edge, width, tapH), v).Position
                      - new Vector2(bandX, Edge);
        Vector2 lshift = left.Count > 0
            ? M59Hud.Place(SideId, new Rect2(Edge, leftY, lpinW, leftH), v).Position
              - new Vector2(Edge, leftY)
            : Vector2.Zero;

        float alpha = Alpha(TopId);
        float lalpha = Alpha(SideId);
        float x = bandX + shift.X;
        float y = Edge + shift.Y;
        if (menu != null)
        {
            menu.Position = new Vector2(x, y);
            menu.Size = new Vector2(menuW, tapH);
            menu.Modulate = new Color(1f, 1f, 1f, alpha);
        }
        x += menuW + gap;
        foreach (Seat s in Ordered(Where.Top))
        {
            DressPinned(s.Button);
            s.Button.Position = new Vector2(x, y);
            s.Button.Size = new Vector2(pinW, tapH);
            s.Button.Modulate = new Color(1f, 1f, 1f, alpha);
            x += pinW + gap;
        }

        // The left edge, at the height of the hand rather than at the
        // bottom of it: the stick is a floating one that appears
        // wherever the thumb lands on the left half, so a button down
        // in the corner where the thumb RESTS would eat the gesture the
        // client exists for. Half way up the edge is within reach and
        // is not where anyone plants a thumb to walk.
        if (left.Count > 0)
        {
            float ly = leftY + lshift.Y;
            foreach (Seat s in left)
            {
                DressPinned(s.Button);
                s.Button.Position = new Vector2(Edge + lshift.X, ly);
                s.Button.Size = new Vector2(lpinW, ltapH);
                s.Button.Modulate = new Color(1f, 1f, 1f, lalpha);
                ly += ltapH + lgap;
            }
        }

        // The font last, and only when the size moved: a control keeps
        // whatever size it was given, so a group that scaled its boxes
        // and not its captions is the obvious failure.
        if (!Mathf.IsEqualApprox(_dressedAt, sc) || !Mathf.IsEqualApprox(_dressedAtLeft, lsc))
        {
            _dressedAt = sc;
            _dressedAtLeft = lsc;
            int pt = Mathf.Max(8, Mathf.RoundToInt((M59Skin.BodySize + 2) * sc));
            int lpt = Mathf.Max(8, Mathf.RoundToInt((M59Skin.BodySize + 2) * lsc));
            foreach (Button b in Dressed)
                if (GodotObject.IsInstanceValid(b)) b.AddThemeFontSizeOverride("font_size", pt);
            // The edge column last, over the top of the loop above: its
            // buttons are in Dressed too, and its caption follows its own
            // piece's size rather than the band's.
            foreach (Seat s in left)
                if (GodotObject.IsInstanceValid(s.Button))
                    s.Button.AddThemeFontSizeOverride("font_size", lpt);
            if (menu != null)
                menu.AddThemeFontSizeOverride("font_size",
                    Mathf.Max(8, Mathf.RoundToInt((M59Skin.TitleSize - 2) * sc)));
        }
    }

    /// <summary>
    /// The player's transparency for this piece. Applied here rather than
    /// through <see cref="M59Hud.Dress"/> because these controls have no
    /// common parent: each one belongs to the panel it opens.
    /// </summary>
    static float Alpha(string id)
    {
        M59Hud.Piece p = M59Hud.Get(id);
        if (p == null) return 1f;
        return M59Hud.Editing ? 1f : Mathf.Clamp(p.Alpha, M59Hud.MinAlpha, M59Hud.MaxAlpha);
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

    /// <summary>
    /// What the layout store says about the pinned piece, as one value.
    /// The drawer asks TakeDirty every frame and is the only thing that
    /// can place these, so this is where a moved piece is noticed - an
    /// event alone would miss a layout LOADED after the row was built.
    /// </summary>
    static string HudStamp()
    {
        return One(TopId) + "|" + One(SideId);

        static string One(string id)
        {
            M59Hud.Piece p = M59Hud.Get(id);
            if (p == null) return "";
            return $"{p.Offset.X},{p.Offset.Y},{p.Scale},{p.Alpha},{(p.Hidden ? 1 : 0)},{(M59Hud.Editing ? 1 : 0)}";
        }
    }

    static string _stamp = "";

    /// <summary>True once, after the registry changed - the drawer rebuilds on it.</summary>
    internal static bool TakeDirty()
    {
        string stamp = HudStamp();
        if (stamp != _stamp) { _stamp = stamp; _dirty = true; }
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
