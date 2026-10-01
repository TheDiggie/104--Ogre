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
    Label _title, _head, _selectedDesc, _selected, _passwordDesc, _invalid;
    HBoxContainer _headings;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    LineEdit _password;
    Button _buy, _cancel;

    GuildHallsInfo _info;

    /// <summary>What the rows were last built from.</summary>
    string _signature = "";

    /// <summary>
    /// The picked hall's object id, or zero for none - which is the
    /// reference's "getFirstSelectedItem returned null" and the one
    /// state in which Buy does nothing (`:224-230`).
    /// </summary>
    uint _pick;

    /// <summary>Row buttons by hall id, so the highlight moves without a rebuild.</summary>
    readonly Dictionary<uint, Button> _rowFor = new Dictionary<uint, Button>();

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        // The frame's caption and its headline label, both the layout's
        // own words (`Meridian59.layout:1656`, `:1667`).
        _title = Heading("Rent Guild Hall", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _head = Heading("Select a hall to house your guild:", FontSize, new Color(0.75f, 0.78f, 0.84f));

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
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        // "Guild hall:" and a read-only box showing the pick
        // (`:1676-1686`). The reference fills that box from the selection
        // change (`:151-170`) and blanks it when the selected row is
        // removed (`:129-134`).
        _selectedDesc = Heading("Guild hall:", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _selected = Heading("", FontSize, new Color(1, 0.92f, 0.6f));

        _passwordDesc = Heading("Password:", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _password = new LineEdit { PlaceholderText = "guild password", Visible = false, Name = "hallpassword" };
        _password.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_password);

        // The layout's warning, in the layout's words. It shares its box
        // with the password field there because it replaces it; here it
        // gets its own line, since a phone has the height and hiding the
        // field you are being told to fill in would be unkind.
        _invalid = Heading("You must specify a guild password!", FontSize - 2, new Color(0.95f, 0.55f, 0.5f));

        // Cancel on the left, Buy on the right, as in the layout
        // (`:1703-1712`) - and, as in ConfirmPopup, the one that spends
        // money is not the one under a resting thumb.
        _cancel = Push("Cancel", () => Cancelled?.Invoke(), "hallcancel");
        _buy = Push("Buy", Confirm, "hallbuy");

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
        else l.CustomMinimumSize = new Vector2(FontSize * 5f, 0);
        l.AddThemeFontSizeOverride("font_size", FontSize - 3);
        l.AddThemeColorOverride("font_color", new Color(0.62f, 0.65f, 0.72f));
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

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 780f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.9f;
        _head.Position = new Vector2(side, y); y += FontSize * 1.8f;

        // Below the list: the pick readout, the password line, the
        // warning, and the button row - plus the gap under the last of
        // them, the same accounting GuildPanel.Layout spells out.
        const float foot = 12f;
        float below = rowH * 4f + 24f + foot;

        // The headings sit on the row grid: the rows inset their content
        // by ten pixels either side (see Row), so the headings do too, or
        // "Cost" would not sit over the costs.
        _headings.Position = new Vector2(side + 10f, y);
        _headings.Size = new Vector2(w - 20f, FontSize * 1.4f);
        y += FontSize * 1.6f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, Mathf.Max(rowH, top + height - below - 8f - y));
        _rows.CustomMinimumSize = new Vector2(w, 0);

        float by = top + height - below;
        _selectedDesc.Position = new Vector2(side, by + rowH * 0.25f);
        _selected.Position = new Vector2(side + w * 0.3f, by + rowH * 0.25f);

        by += rowH;
        _passwordDesc.Position = new Vector2(side, by + rowH * 0.25f);
        _password.Position = new Vector2(side + w * 0.3f, by);
        _password.Size = new Vector2(w * 0.7f, rowH);

        by += rowH + 4f;
        _invalid.Position = new Vector2(side, by);

        by += rowH * 0.9f + 8f;
        _cancel.Position = new Vector2(side, by);
        _cancel.Size = new Vector2(w * 0.5f - 6f, rowH);
        _buy.Position = new Vector2(side + w * 0.5f + 6f, by);
        _buy.Size = new Vector2(w * 0.5f - 6f, rowH);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront. The reference
        // does the same with moveToFront right after setting the window
        // visible (`UIGuildHallBuy.cpp:68-69`).
        if (on) Panels.ToFront(this);
        _panel.Visible = on;
        _title.Visible = on; _head.Visible = on;
        _headings.Visible = on; _scroll.Visible = on;
        _selectedDesc.Visible = on; _selected.Visible = on;
        _passwordDesc.Visible = on; _password.Visible = on;
        _cancel.Visible = on; _buy.Visible = on;

        // Never carried into a new showing: the reference clears the
        // warning on every IsVisible change (`:64-65`), so an offer you
        // walked away from does not come back scolding you.
        if (!on) _invalid.Visible = false;

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
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
            if (IsOpen) { _info = info; Show(false); _signature = ""; _pick = 0; }
            return;
        }

        _info = info;

        // Visible, but there is nothing to offer: close it the way the
        // reference does (`UIGuildHallBuy.cpp:139-144`).
        if (info.GuildHalls == null || info.GuildHalls.Count == 0)
        {
            if (IsOpen) { Show(false); _signature = ""; _pick = 0; }
            Cancelled?.Invoke();
            return;
        }

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

        var row = new Button
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            // Named so a scripted run can press one: the text is in
            // child labels, so there is nothing to find it by.
            Name = $"hall{id}",
        };
        row.AddThemeFontSizeOverride("font_size", FontSize);
        row.Pressed += () => Choose(id);

        // Remembered here, as the row is built, so Paint can move the
        // highlight without rebuilding the list.
        _rowFor[id] = row;

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 10; line.OffsetTop = 4;
        line.OffsetRight = -10; line.OffsetBottom = -4;
        row.AddChild(line);

        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(h.Name) ? "(unnamed hall)" : h.Name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", new Color(0.86f, 0.88f, 0.92f));
        line.AddChild(name);

        line.AddChild(Number(h.Cost.ToString(), new Color(1, 0.86f, 0.4f)));
        line.AddChild(Number(h.Rent.ToString(), new Color(0.75f, 0.78f, 0.84f)));

        return row;
    }

    /// <summary>
    /// A money column. Right-aligned and given a fixed share, because two
    /// numbers that slide about as the names beside them change length
    /// are two numbers nobody can compare down the list.
    /// </summary>
    Label Number(string text, Color color)
    {
        var l = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(FontSize * 5f, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize);
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
    /// button has no such state, so the row is tinted instead. Buy dead
    /// with nothing picked is the reference's silent `return` on a null
    /// selection (`:227-230`) said in advance, which is the better place
    /// to say it: a button that does nothing when pressed teaches
    /// nothing.
    /// </summary>
    void Paint()
    {
        foreach (KeyValuePair<uint, Button> pair in _rowFor)
            if (GodotObject.IsInstanceValid(pair.Value))
                pair.Value.Modulate = pair.Key == _pick
                    ? new Color(1.35f, 1.3f, 1f)
                    : Colors.White;

        _buy.Disabled = _pick == 0;
    }
}
