using System.Collections.Generic;
using Godot;

/// <summary>
/// The menu, as one control and a grid, instead of a row along the
/// bottom edge.
///
/// WHAT WAS THERE. Fourteen small text buttons - Who, Next, Acts, Auto,
/// Go, Settings, Guild, Mail, Quests, Book, Me, Bag, Loot, Map - spread
/// across the whole bottom edge, with Say and Log at the left end. Three
/// things wrong with that, and the third is the one that matters:
///
/// - They are 40pt tall where a touch target is 44pt (Apple) or 48dp
///   (Material), with 8pt between them, and they are TEXT, so every
///   press is a reading task.
/// - They are a menu bar, which is a desktop idiom. Reach on a phone is
///   a curved arc from where the thumb is anchored, not a rectangle;
///   primary actions belong in the bottom 40% and the top quarter
///   should hold only the infrequent - which a menu is, exactly.
/// - The bottom edge is where both thumbs already ARE. The left thumb
///   drives the movement stick over the left half, the right thumb
///   drags to look over the right half, and the combat cluster takes
///   the bottom-right corner. A row of fourteen along that edge is
///   both unreachable and in the way of the two gestures the client is
///   for.
///
/// WHAT IT IS NOW. One control at the top centre, labelled, 150x60, and
/// a grid of large labelled tiles over the world when you press it. The
/// grid is an <see cref="M59Skin"/> card like every other window in the
/// client, because it IS one - a scrim that eats the tap behind it, a
/// title bar with a round close, and a body.
///
/// WHAT STAYED OUT, and why - the set is deliberately tiny, because
/// everything kept out is glass taken away from the game:
///
/// - Say and Log, which are the chat, which is what an MMO is for. They
///   keep the chat entry's own rectangle at the bottom left and swap
///   with the text box, as they always have.
/// - Map, a toggle glanced at constantly while finding your way, and
///   not combat. It moves from the bottom-right corner - which the
///   combat cluster now owns - up beside the Menu control, next to the
///   dial it shows.
/// - Auto, which is movement, so it is on the LEFT edge by the thumb
///   that steers rather than anywhere near the right.
///
/// Everything else is a tile - eleven of them. Loot and Go are not
/// openers at all but they were in the same row and behave the same
/// way, so they are tiles too.
///
/// Go is the one tile that is ALSO somewhere else, and that is the
/// correction to this change. Walking through a door is done constantly
/// while moving, and a drawer is modal - a scrim that eats the world
/// behind it - so Go two taps inside here was a regression the owner
/// found by playing. It is bindable to the combat arc now
/// (`ActionButtons.Extra`) and seeded there for a new character. The
/// tile stays as the floor: a player who dragged the slot off, or whose
/// saved hotbar predates the seed, still has to be able to leave the
/// room. Both doors make the same send.
///
/// Next is NOT a tile, though it was for about an hour: it picks the
/// next target and is pressed in a fight, and a fight is no time to
/// open a drawer. It now sits in the combat cluster beside the attack
/// control, where it is seat zero of the arc.
///
/// TREE ORDER. The drawer is a child of the view's UI layer, beside the
/// panels and the ConfirmPopup, and it raises itself with
/// <see cref="Panels.ToFront"/> exactly as a panel does - which puts an
/// armed popup back above it in the same call. The invariant is that
/// nothing is ever drawn over a question that is waiting; a new window
/// that forgot it would be the fourth time that bug shipped.
/// </summary>
public partial class MenuDrawer : Control
{
    /// <summary>Four across: three rows, the last one short.</summary>
    const int Columns = 4;
    /// <summary>
    /// A tile. Well over the 44pt minimum in both directions, because
    /// the point of collapsing the row was to stop asking a thumb to
    /// hit a 40pt slab.
    /// </summary>
    const float TileH = 104f, TileGap = 16f;
    /// <summary>The caption's strip at the bottom of a tile.</summary>
    const float CapH = 26f;

    Button _menu;
    ColorRect _scrim;
    M59Skin.Chrome _chrome;
    Control _host;
    readonly List<Label> _caps = new List<Label>();
    readonly HashSet<Button> _dressed = new HashSet<Button>();

    bool _open;
    bool _allowed = true;

    public bool IsOpen => _open;

    public override void _Ready()
    {
        // Sized by hand, never anchored: this lives under a CanvasLayer
        // whose Control parent has no rect, and an anchored child of
        // one of those comes out 0x0 and never draws. See notes/godot-ui.md.
        MouseFilter = MouseFilterEnum.Ignore;

        _menu = new Button { Text = "Menu", Name = "menuButton" };
        M59Skin.Dress(_menu, M59Skin.Kind.Primary);
        _menu.AddThemeFontSizeOverride("font_size", M59Skin.TitleSize - 2);
        _menu.Pressed += Toggle;
        AddChild(_menu);

        // Clear, like every other scrim in this client - the room is the
        // game and you are standing in it. It is here to eat the tap
        // that would otherwise reach the world behind the grid.
        _scrim = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _scrim.MouseFilter = MouseFilterEnum.Stop;
        // Tapping beside the grid shuts it, which is what every drawer
        // on a phone does and is faster than aiming at the close.
        _scrim.GuiInput += ev =>
        {
            if (ev is InputEventScreenTouch t && t.Pressed) Close();
            else if (ev is InputEventMouseButton m && m.Pressed) Close();
        };
        AddChild(_scrim);

        _chrome = new M59Skin.Chrome(Close);
        _chrome.Name.Text = "Menu";
        if (_chrome.X != null) _chrome.X.Name = "menuClose";
        _chrome.AddTo(this);
        _chrome.Show(false);

        // The tiles hang here so they draw over the card: they are the
        // panels' own buttons, moved under the drawer rather than
        // copied, so the handler a tile fires is the one the panel
        // wired up and there is no second button to keep in step.
        _host = new Control { Name = "menuGrid", Visible = false };
        _host.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_host);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>Whether the openers are allowed up at all - see Panels.ShowOpeners.</summary>
    public void Allowed(bool on)
    {
        _allowed = on;
        _menu.Visible = on;
        if (!on && _open) Close();
    }

    void Toggle() { if (_open) Close(); else Open(); }

    public void Open()
    {
        if (!_allowed) return;
        _open = true;
        // Above whatever else is on this layer - and this call is also
        // what puts an armed ConfirmPopup straight back above US.
        Panels.ToFront(this);
        Apply();
        Layout();
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        Apply();
    }

    void Apply()
    {
        _scrim.Visible = _open;
        _chrome.Show(_open);
        _host.Visible = _open;
        foreach (Label l in _caps) l.Visible = _open;
        // The pinned controls stand down while the grid is over them,
        // and come back when it goes.
        Panels.ShowOpeners(_allowed);
    }

    public override void _Process(double delta)
    {
        if (Panels.TakeDirty()) Layout();
        // Cheap - one comparison in the common case - and it is the
        // belt to Open()'s braces: a question armed WHILE the grid is
        // up must still be the thing on top.
        if (_open) Panels.KeepPopupOnTop(GetParent());
    }

    void Layout()
    {
        if (_menu == null) return;
        Vector2 v = GetViewportRect().Size;

        Position = Vector2.Zero;
        Size = v;
        _host.Position = Vector2.Zero;
        _host.Size = v;

        Panels.PlacePinned(v, _menu);

        _scrim.Position = Vector2.Zero;
        _scrim.Size = v;

        var tiles = new List<Panels.Seat>(Panels.Ordered(Panels.Where.Drawer));

        // Adopt anything new. A tile is one of the panels' own buttons,
        // so moving it here is the whole of "Panels owns placement":
        // there is no second button and no handler to mirror.
        foreach (Panels.Seat s in tiles)
        {
            Button b = s.Button;
            if (b.GetParent() != _host)
            {
                b.GetParent()?.RemoveChild(b);
                _host.AddChild(b);
            }
            if (_dressed.Add(b)) Tile(b);
        }

        int rows = Mathf.Max(1, (tiles.Count + Columns - 1) / Columns);
        float tileW = 200f;
        float wantW = Columns * tileW + (Columns - 1) * TileGap + M59Skin.Pad * 2f;
        float wantH = rows * TileH + (rows - 1) * TileGap;

        // No footer: the close is the round one in the title bar and
        // the scrim, and a footer band here would be an empty strip
        // under a grid that is already all buttons.
        Rect2 card = M59Skin.Frame(v, wantH, false, wantW);
        _chrome.Place(card);
        Rect2 body = M59Skin.Body(card, false);

        tileW = (body.Size.X - (Columns - 1) * TileGap) / Columns;

        while (_caps.Count < tiles.Count)
        {
            var l = M59Skin.Caption("");
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            // Not autowrapped, and clipped: a wrapping Label sized
            // before it has ever been shaped reports a minimum height
            // in the thousands. M59Skin.Empty carries the whole story.
            l.ClipText = true;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            l.MouseFilter = MouseFilterEnum.Ignore;
            l.Visible = _open;
            AddChild(l);
            _caps.Add(l);
        }
        for (int i = tiles.Count; i < _caps.Count; i++) _caps[i].Visible = false;

        for (int i = 0; i < tiles.Count; i++)
        {
            int col = i % Columns, row = i / Columns;
            var at = new Vector2(body.Position.X + col * (tileW + TileGap),
                                 body.Position.Y + row * (TileH + TileGap));
            tiles[i].Button.Position = at;
            tiles[i].Button.Size = new Vector2(tileW, TileH);

            Label cap = _caps[i];
            cap.Text = tiles[i].What ?? "";
            cap.Visible = _open && cap.Text.Length > 0;
            cap.Position = new Vector2(at.X + 6f, at.Y + TileH - CapH - 10f);
            cap.Size = new Vector2(tileW - 12f, CapH);
        }

        // A tile press opens a panel, which hides the openers, which
        // shuts this - so nothing here has to chase the close.
        foreach (Panels.Seat s in tiles)
            if (!_hooked.Contains(s.Button)) { _hooked.Add(s.Button); s.Button.Pressed += Close; }
    }

    readonly HashSet<Button> _hooked = new HashSet<Button>();

    /// <summary>
    /// A tile: the house secondary button, at title size, with its word
    /// lifted off the bottom so the caption has a strip to live in.
    ///
    /// The word itself is NOT changed - the harness presses by text and
    /// every note in `notes/` names these buttons by it - so the
    /// caption is how "Me" becomes legible as your character sheet.
    /// </summary>
    void Tile(Button b)
    {
        M59Skin.Dress(b, M59Skin.Kind.Secondary);
        b.AddThemeFontSizeOverride("font_size", M59Skin.TitleSize);
        b.ClipText = true;
        // A Button draws its text inside the stylebox's content box, so
        // a bottom margin is what moves the word up off the caption.
        foreach (string which in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            if (b.GetThemeStylebox(which) is StyleBoxFlat flat &&
                flat.Duplicate() is StyleBoxFlat copy)
            {
                copy.ContentMarginBottom = CapH;
                b.AddThemeStyleboxOverride(which, copy);
            }
        }
    }
}
