using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Renting a guild hall: the list of halls the server is offering, what
/// each costs to take and to keep, and the one you pick.
///
/// `UIGuildHallBuy.cpp`. This is the other half of something the mobile
/// client already had one end of: GuildPanel can give a hall up
/// (AbandonHall, `UIGuild.cpp` / `BaseClient.cs:1226-1234`) but there was
/// nowhere to get one. The offer is the server's, not the player's - you
/// ask a hall registrar in the game and a UserCommand (155) comes back as
/// GuildHalls, which `DataController` merges into `GuildHallsInfo` and
/// then marks visible (`Meridian59/Data/DataController.cs:2820-2823`).
/// The reference listens for exactly that: it hooks the model's
/// PropertyChanged in Initialize (`UIGuildHallBuy.cpp:25-29`) and shows
/// or hides the window on IsVisible (`:60-70`), the same server-decides
/// shape as the shop, the quest offer and the stat-change wizard. With no
/// screen for it the message arrived and nothing happened, which on a
/// phone is indistinguishable from the registrar being broken.
///
/// What the player actually chooses is two things, and only two
/// (`:221-249`):
///
///  - which hall, out of a single-selection list. The reference builds
///    that list in three columns - "Guild hall name", "Cost", "Daily
///    rent" (`:20-22`) - one row per `GuildHall` as it is added to the
///    model's BaseList (`:91-122`). Cost and rent are the model's own
///    uints (`GuildHall.cs:131-158`); the name is not sent as text at
///    all, it is a string-resource id resolved on the way in
///    (`GuildHall.cs:232-248`, called from
///    `MessageController.cs:1233-1237`), so by the time the row is built
///    `Name` is a real string.
///  - the guild password, typed. This is not a new password: it is the
///    guild's existing one, sent for verification -
///    `SendUserCommandGuildRent(HallID, Password)`
///    (`BaseClient.cs:1236-1249`) puts the hall's object id and that
///    string on the wire as UserCommandGuildRent
///    (`UserCommandGuildRent.cs:16-75`).
///
/// The reference's validation is exactly two tests, in this order, and
/// both are kept:
///
///  - nothing selected: return, silently, sending nothing (`:224-230`).
///    Here the Buy button is simply dead until a row is picked, which
///    says the same thing without a dead press.
///  - empty password: make the PasswordInvalid label visible and send
///    nothing (`:233-241`). The layout's own wording for that label is
///    "You must specify a guild password!"
///    (`Resources/ui/layouts/Meridian59.layout:1715-1719`), and it is
///    reset to hidden every time the window is shown (`:64-65`). Both
///    are copied - the message and the reset - because a warning left
///    over from a previous visit is worse than none.
///
/// What the reference does NOT do is ask. Buy sends the rent command
/// straight off the press (`:249`), and renting a hall spends the guild's
/// money. That is a mouse click on a desktop; on a phone it is a thumb
/// brushing a list, so the press raises <see cref="Buy"/> and GameView
/// puts the client's own ConfirmPopup in front of it - the same
/// deliberate departure the shield claim already makes, and for the same
/// reason. The window stays exactly as it was if the answer is No, which
/// is why the tear-down below is the caller's job and not this panel's.
///
/// Closing, in the reference, is four paths that do the same three
/// things: Buy, Cancel, the frame's close button and Escape all
/// `Clear(true)` the model, set IsVisible false and return control to the
/// root (`:251-289`). GuildHallsInfo.Clear only empties the list
/// (`GuildHallsInfo.cs:171-181`) - it does not touch IsVisible - so both
/// halves are needed, and <see cref="Cancelled"/> carries that pair out
/// to the caller rather than doing it here, so the two exits stay one
/// piece of code.
///
/// One more path matters and is easy to miss. The reference hides the
/// window when the list becomes empty (`:139-144`), which is not just
/// about a row being removed: a server save calls
/// `DataController.Invalidate`, which clears GuildHallsInfo
/// (`DataController.cs:1049`) and leaves IsVisible set. Without that
/// test the buy window would sit there, visible, offering nothing, with
/// a Buy button pointed at a hall id the server has already forgotten.
/// See Sync below.
/// </summary>
public partial class GuildHallBuyPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 52;

    /// <summary>
    /// Rent this hall, with this password. The caller asks first and
    /// clears up after; see the class note.
    /// </summary>
    public event Action<uint, string> Buy;

    /// <summary>
    /// Cancel, close or the list running dry: throw the offer away and
    /// mark it not visible, which is the three-line tear-down the
    /// reference repeats at `UIGuildHallBuy.cpp:251-253`, `:260-262`,
    /// `:269-271` and `:283-285`.
    /// </summary>
    public event Action Cancelled;

    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title, _head, _selectedDesc, _selected, _passwordDesc, _invalid;
    HBoxContainer _headings;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    LineEdit _password;
    Button _buy, _cancel;
    /// <summary>The rule between the list and the two things below it.</summary>
    ColorRect _rule;

    GuildHallsInfo _info;

    /// <summary>
    /// Rows have been on screen since the window opened, so an empty list
    /// now means they were removed rather than that none ever arrived.
    /// </summary>
    bool _hadRows;

    /// <summary>What the rows were last built from.</summary>
    string _signature = "";

    /// <summary>
    /// The picked hall's object id, or zero for none - which is the
    /// reference's "getFirstSelectedItem returned null" and the one
    /// state in which Buy does nothing (`:224-230`).
    /// </summary>
    uint _pick;

    /// <summary>
    /// Row buttons by hall id, so the highlight moves without a rebuild.
    /// The stripe is remembered with them: Pick has to be told which
    /// stripe a row goes back to when it stops being the chosen one.
    /// </summary>
    readonly Dictionary<uint, (Button button, bool alt)> _rowFor =
        new Dictionary<uint, (Button, bool)>();

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim eats the touch that would reach the world behind;
        // the card over it is opaque, which the old 0.97 panel was not.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        // The frame's caption and its headline label, both the layout's
        // own words (`Meridian59.layout:1656`, `:1667`).
        _title = M59Skin.Title("Rent Guild Hall");
        _title.Visible = false;
        AddChild(_title);

        // The cross is the frame's close button, which in the reference
        // tears the offer down exactly as Cancel does (`:269-271`).
        _x = M59Skin.CloseX(() => Cancelled?.Invoke());
        _x.Visible = false;
        AddChild(_x);

        _head = Heading("Select a hall to house your guild:", M59Skin.BodySize, M59Skin.TextDim);

        // The list's column headings: "Guild hall name", "Cost", "Daily
        // rent", in the reference's order and its words
        // (`UIGuildHallBuy.cpp:20-22`). CEGUI's MultiColumnList draws its
        // own header; a VBox of rows has none, and without one the two
        // numbers on a row are a cost and a rent only to somebody who
        // already knew. They are outside the scroll because a heading
        // that scrolls away stops being a heading.
        _headings = new HBoxContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _headings.AddThemeConstantOverride("separation", 10);
        _headings.AddChild(Column("Guild hall name", HorizontalAlignment.Left, true));
        _headings.AddChild(Column("Cost", HorizontalAlignment.Right, false));
        _headings.AddChild(Column("Daily rent", HorizontalAlignment.Right, false));
        AddChild(_headings);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Or the list is only as wide as its longest hall name and the
        // two money columns land wherever that row's text ended - see
        // notes/godot-ui.md, "A ScrollContainer sizes its child to that
        // child's minimum".
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _rule = M59Skin.Hairline();
        _rule.Visible = false;
        AddChild(_rule);

        // "Guild hall:" and a read-only box showing the pick
        // (`:1676-1686`). The reference fills that box from the selection
        // change (`:151-170`) and blanks it when the selected row is
        // removed (`:129-134`).
        _selectedDesc = Cap("Guild hall");
        _selected = Heading("", M59Skin.BodySize + 2, M59Skin.GoldBright);

        _passwordDesc = Cap("Password");
        _password = M59Skin.Field(new LineEdit
        {
            PlaceholderText = "guild password",
            Visible = false,
            Name = "hallpassword",
        });
        AddChild(_password);

        // The layout's warning, in the layout's words. It shares its box
        // with the password field there because it replaces it; here it
        // gets its own line, since a phone has the height and hiding the
        // field you are being told to fill in would be unkind.
        _invalid = Heading("You must specify a guild password!", M59Skin.SmallSize + 2, M59Skin.Danger);

        // Cancel on the left, Buy on the right, as in the layout
        // (`:1703-1712`) - and, as in ConfirmPopup, the one that spends
        // money is not the one under a resting thumb. FootRow lays them
        // out from the right, so Cancel is the rightmost argument.
        _cancel = Push("Cancel", () => Cancelled?.Invoke(), "hallcancel");
        _buy = Push("Buy", Confirm, "hallbuy");
        M59Skin.Dress(_cancel, M59Skin.Kind.Secondary);
        // Renting the hall is the one thing this window is for.
        M59Skin.Dress(_buy, M59Skin.Kind.Primary);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    /// <summary>
    /// One heading, sized to match the row column under it: the name
    /// stretches, the two money columns keep the fixed width
    /// <see cref="Number"/> gives them.
    /// </summary>
    Label Column(string text, HorizontalAlignment align, bool stretch)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (stretch) l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        else l.CustomMinimumSize = new Vector2(ColMoney, 0);
        l.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        l.AddThemeColorOverride("font_color", M59Skin.GoldDim);
        return l;
    }

    /// <summary>The small gold line over a field.</summary>
    Label Cap(string text)
    {
        Label l = M59Skin.Caption(text);
        l.Visible = false;
        AddChild(l);
        return l;
    }

    Button Push(string text, Action pressed, string name)
    {
        var b = new Button { Text = text, Visible = false, Name = name };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// The Buy press, which is `UICallbacks::GuildHallBuy::OnBuyClicked`
    /// (`UIGuildHallBuy.cpp:221-256`) with the send replaced by an ask.
    ///
    /// The two guards are the reference's, in its order: no selection is
    /// a silent return (`:227-230`), and an empty password shows the
    /// warning and returns (`:236-241`). Only past both does anything
    /// leave this panel.
    /// </summary>
    void Confirm()
    {
        if (_pick == 0) return;

        string password = _password.Text ?? "";
        if (password.Length == 0)
        {
            _invalid.Visible = true;
            return;
        }

        _invalid.Visible = false;
        Buy?.Invoke(_pick, password);
    }

    /// <summary>
    /// The two money columns, fixed so the figures line up down the
    /// list and the headings sit over them. Wide enough for the cost,
    /// which is the long number and the one being compared.
    /// </summary>
    const float ColMoney = 150f;
    /// <summary>The gutter a row insets its contents by - the headings too.</summary>
    const float RowInset = 12f;
    /// <summary>Room kept for the list's scrollbar, so the headings stay put.</summary>
    const float BarW = 14f;
    const float CapH = 20f, FieldH = 44f;

    float RowTall => Mathf.Max(RowHeight, M59Skin.RowH);

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Below the list, inside the card: a rule, the pick readout and
        // the password line side by side, and the warning under them.
        float below = 1f + M59Skin.Gap + CapH + FieldH + M59Skin.SmallSize + 12f;
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        float want = M59Skin.BodySize + 8f + CapH + shown * (RowTall + 4f) + below;

        Rect2 card = M59Skin.Frame(v, want);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position; _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(34, 34);
        _x.Position = new Vector2(card.Position.X + card.Size.X - 34f - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - 34f) * 0.5f);

        float x = body.Position.X, w = body.Size.X, y = body.Position.Y;

        _head.Position = new Vector2(x, y);
        _head.Size = new Vector2(w, M59Skin.BodySize + 6f);
        y += M59Skin.BodySize + 8f;

        // The headings sit on the row grid: the rows inset their content
        // by RowInset either side, so the headings do too, or "Cost"
        // would not sit over the costs.
        _headings.Position = new Vector2(x + RowInset, y);
        // The rows take the scroll's full width until a scrollbar
        // appears, so the headings match that and not the reserved
        // width: "Cost" fourteen pixels off the costs is the common
        // case, an overflowing list the rare one.
        _headings.Size = new Vector2(w - RowInset * 2f, CapH);
        y += CapH;

        float listH = Mathf.Max(RowTall, body.Position.Y + body.Size.Y - below - y);
        _scroll.Position = new Vector2(x, y);
        _scroll.Size = new Vector2(w, listH);
        _rows.CustomMinimumSize = new Vector2(w - BarW, 0);

        float by = body.Position.Y + body.Size.Y - below;
        _rule.Position = new Vector2(x, by);
        _rule.Size = new Vector2(w, 1f);
        by += 1f + M59Skin.Gap;

        // The chosen hall and the password are one line of two columns:
        // what you picked, and the word that proves you may rent it.
        float colW = (w - M59Skin.Gap) * 0.5f;
        float rightX = x + colW + M59Skin.Gap;
        _selectedDesc.Position = new Vector2(x, by);
        _selectedDesc.Size = new Vector2(colW, CapH);
        _passwordDesc.Position = new Vector2(rightX, by);
        _passwordDesc.Size = new Vector2(colW, CapH);
        by += CapH;
        _selected.Position = new Vector2(x, by);
        _selected.Size = new Vector2(colW, FieldH);
        _selected.VerticalAlignment = VerticalAlignment.Center;
        _password.Position = new Vector2(rightX, by);
        _password.Size = new Vector2(colW, FieldH);
        by += FieldH + 4f;

        _invalid.Position = new Vector2(rightX, by);
        _invalid.Size = new Vector2(colW, M59Skin.SmallSize + 6f);

        // Cancel at the right, where the dismissing thumb is; Buy, which
        // spends, beside it rather than under it.
        M59Skin.FootRow(foot, _cancel, _buy);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront. The reference
        // does the same with moveToFront right after setting the window
        // visible (`UIGuildHallBuy.cpp:68-69`).
        if (on) Panels.ToFront(this);
        _panel.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _title.Visible = on; _head.Visible = on;
        _headings.Visible = on; _scroll.Visible = on; _rule.Visible = on;
        _selectedDesc.Visible = on; _selected.Visible = on;
        _passwordDesc.Visible = on; _password.Visible = on;
        _cancel.Visible = on; _buy.Visible = on;

        // Never carried into a new showing: the reference clears the
        // warning on every IsVisible change (`:64-65`), so an offer you
        // walked away from does not come back scolding you.
        if (!on) _invalid.Visible = false;

        if (on) { GetParent()?.MoveChild(this, -1); KeepPopupOnTop(); }
        Layout();
    }

    /// <summary>
    /// Puts an open ConfirmPopup back above everything. The reference's
    /// popup is AlwaysOnTop (`Meridian59.layout:2859,2873`), so a window
    /// that is shown and moved to front never ends up over a question
    /// that is waiting for an answer; in this tree "on top" is just the
    /// last child, so whoever moves itself last has to put the popup back.
    /// </summary>
    void KeepPopupOnTop()
    {
        Node parent = GetParent();
        if (parent == null) return;
        foreach (Node n in parent.GetChildren())
            if (n is ConfirmPopup p && p.IsOpen) { parent.MoveChild(p, -1); break; }
    }

    /// <summary>
    /// Follows the model, every frame. The reference gets the same three
    /// edges from events - IsVisible changing (`:60-70`), a hall being
    /// added (`:77-79`, `:91-122`) and the list emptying (`:139-144`) -
    /// and polling a signature covers all three without a subscription
    /// that would have to be torn down.
    ///
    /// The empty-list case is the one with teeth. It is not only a
    /// removed row: `DataController.Invalidate` clears GuildHallsInfo
    /// after a server save (`DataController.cs:1049`) and leaves
    /// IsVisible alone, so "visible with no halls" is a state that
    /// really happens. The reference's answer is to close, and it does
    /// it by pushing IsVisible false back into the model - which is what
    /// <see cref="Cancelled"/> makes the caller do here, so the window
    /// does not reopen on the very next frame off a flag nobody cleared.
    /// </summary>
    public void Sync(GuildHallsInfo info)
    {
        if (_rows == null) return;

        if (info == null || !info.IsVisible)
        {
            _hadRows = false;
            if (IsOpen) { _info = info; Show(false); _signature = ""; _pick = 0; }
            return;
        }

        _info = info;

        if (info.GuildHalls == null || info.GuildHalls.Count == 0)
        {
            // The reference closes only when rows LEAVE the list and the
            // last one goes (`UIGuildHallBuy.cpp:139-144`, reached through
            // GuildHallRemove alone). A GuildHalls message that arrives with
            // count 0 - which the server can send (SendBuyGuildHall) - adds
            // no rows and removes none, so the window simply opens empty.
            // Closing it here gave the player no sign the registrar had
            // answered at all.
            if (_hadRows)
            {
                _hadRows = false;
                if (IsOpen) { Show(false); _signature = ""; _pick = 0; }
                Cancelled?.Invoke();
                return;
            }

            if (!IsOpen) Show(true);
            if (_signature != "empty")
            {
                _signature = "empty";
                foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
                _rowFor.Clear();
                Choose(0);
                _head.Text = "No guild halls are on offer.";
            }
            return;
        }

        _hadRows = true;
        _head.Text = "Select a hall to house your guild:";

        if (!IsOpen) Show(true);

        var sb = new System.Text.StringBuilder();
        foreach (GuildHall h in info.GuildHalls)
            if (h != null)
                sb.Append(h.ID).Append(':').Append(h.Name).Append(':')
                  .Append(h.Cost).Append(':').Append(h.Rent).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _rowFor.Clear();

        foreach (GuildHall h in info.GuildHalls)
            if (h != null) _rows.AddChild(Row(h));

        // A hall that was picked and is no longer offered stops being
        // the pick, and the readout empties with it - the reference
        // blanks its SelectedHall box for the same case (`:129-134`).
        // Leaving a stale id there would aim Buy at whatever the server
        // next called it.
        if (_pick != 0 && !_rowFor.ContainsKey(_pick)) Choose(0);
        else Paint();

        Show(true);
    }

    /// <summary>
    /// One hall: its name, what it costs and what it costs per day -
    /// the reference's three columns, in its order (`:20-22`, `:93-121`).
    ///
    /// A tap on the row is the reference's selection change (`:151-170`).
    /// The whole row is the target rather than a corner of it, and there
    /// is no keyboard equivalent to its arrow-key walk (`:175-219`)
    /// because there are no arrow keys to walk with.
    /// </summary>
    Control Row(GuildHall h)
    {
        uint id = h.ID;
        bool alt = _rowFor.Count % 2 == 1;

        var row = new Button
        {
            CustomMinimumSize = new Vector2(0, RowTall),
            // Named so a scripted run can press one: the text is in
            // child labels, so there is nothing to find it by.
            Name = $"hall{id}",
        };
        // Alternating stripes, so a long list of halls keeps its place.
        M59Skin.Dress(row, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
        row.Pressed += () => Choose(id);

        // Remembered here, as the row is built, so Paint can move the
        // highlight without rebuilding the list.
        _rowFor[id] = (row, alt);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        line.OffsetLeft = RowInset; line.OffsetTop = 4;
        line.OffsetRight = -RowInset; line.OffsetBottom = -4;
        row.AddChild(line);

        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(h.Name) ? "(unnamed hall)" : h.Name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(name);

        // The cost is what the decision turns on, so it is the loud one:
        // bright gold, two sizes up. The daily rent is the small print
        // beside it, which is how the two differ in the choosing.
        line.AddChild(Number(h.Cost.ToString(), M59Skin.GoldBright, M59Skin.BodySize + 4));
        line.AddChild(Number(h.Rent.ToString(), M59Skin.TextDim, M59Skin.SmallSize));

        return row;
    }

    /// <summary>
    /// A money column. Right-aligned and given a fixed share, because two
    /// numbers that slide about as the names beside them change length
    /// are two numbers nobody can compare down the list.
    /// </summary>
    Label Number(string text, Color color, int size)
    {
        var l = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(ColMoney, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    /// <summary>
    /// Picks a hall, or nothing when <paramref name="id"/> is zero. The
    /// reference's selection callback writes the hall's name into its
    /// read-only box (`:161-166`); this does that and repaints, and it is
    /// the only place <c>_pick</c> moves.
    /// </summary>
    void Choose(uint id)
    {
        _pick = id;

        string text = "";
        if (id != 0 && _info?.GuildHalls != null)
            foreach (GuildHall h in _info.GuildHalls)
                if (h != null && h.ID == id)
                {
                    text = string.IsNullOrWhiteSpace(h.Name) ? "(unnamed hall)" : h.Name;
                    break;
                }
        _selected.Text = text;

        Paint();
    }

    /// <summary>
    /// The highlight, and whether Buy can be pressed.
    ///
    /// CEGUI gives the picked row a selection brush (`:104-110`); Godot's
    /// button has no such state, so the skin's Pick marks it - the lit
    /// fill and a gold edge down the left, which is what tells you which
    /// row you are looking at when the fill alone is a shade of brown. A
    /// brightening Modulate did the job before and could not be told
    /// apart from the hover. Buy dead with nothing picked is the
    /// reference's silent `return` on a null selection (`:227-230`) said
    /// in advance, which is the better place to say it: a button that
    /// does nothing when pressed teaches nothing.
    /// </summary>
    void Paint()
    {
        foreach (KeyValuePair<uint, (Button button, bool alt)> pair in _rowFor)
            if (GodotObject.IsInstanceValid(pair.Value.button))
                M59Skin.Pick(pair.Value.button, pair.Key == _pick, pair.Value.alt);

        _buy.Disabled = _pick == 0;
    }
}
