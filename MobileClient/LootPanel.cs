using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// A list of objects the server has put in front of you: what is lying
/// within reach, or what is inside the thing you just opened.
///
/// The game has two windows of this shape and they are all but the same
/// file. `UILootList.cpp` shows `Data->RoomObjectsLoot` - everything
/// loose around you - with Get and Get All underneath.
/// `UIObjectContents.cpp` shows `Data->ObjectContents` - what is in a
/// container - with only Get, because "all" has no meaning there. Both
/// are a list rather than a grid: one row per item, an icon, the name,
/// and how many. So this is one panel used twice, and `ShowGetAll` is
/// the only difference between the two.
///
/// Three things come straight from the game rather than from taste:
///
///  - the name's colour is `NameColors.GetColorFor(flags)`, which is the
///    library's own function. In vanilla it is white normally, orange for
///    an outlaw, red for a killer, yellow for a creator, green for a
///    super-DM, cyan for a DM, purple for an event character, and black
///    for an object flagged to draw black.
///  - the icons are composed with the same arguments as the inventory's -
///    front frame, no Y offset, centred in the box.
///  - the list appears when the server sends the contents and goes away
///    when it says so: `ObjectContents.IsVisible` is the switch, not
///    anything this client decides.
/// </summary>
public partial class LootPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 40;
    [Export] public int RowHeight = 56;
    /// <summary>What the window is called. The game has "Loot" and "Contents".</summary>
    [Export] public string Heading = "Loot";
    /// <summary>
    /// The loot window has a Get All; the container window does not,
    /// because the server has no "take everything in that box".
    /// </summary>
    [Export] public bool ShowGetAll = true;
    /// <summary>The container window can be put into; the loot pile cannot.</summary>
    [Export] public bool AllowPut = false;

    /// <summary>Take the one that is picked.</summary>
    /// <summary>
    /// Take these. A list, not one thing: both of the game's windows
    /// are multi-select and their Get walks the selection sending one
    /// request each (`UILootList.cpp:270`,
    /// `UIObjectContents.cpp:272`). It matters more here than there -
    /// tapping is the expensive interaction on a phone, and taking six
    /// things one at a time is twelve taps.
    /// </summary>
    public event Action<IList<ObjectBase>> GetItems;
    /// <summary>Take everything in range, which is what the game's Get All does.</summary>
    public event Action GetAll;
    /// <summary>
    /// Put something of yours into this container. The game does it by
    /// dragging out of the inventory window and onto the contents list;
    /// a phone cannot show both at once, so this asks for one to be
    /// picked instead. Only the container window offers it - there is
    /// nothing to put a thing into on the floor.
    /// </summary>
    public event Action PutWanted;

    /// <summary>
    /// Describe this one. The game looks at a row on a right click
    /// (`UIObjectContents.cpp:259`, `UILootList.cpp`), which is worth
    /// having: knowing what a thing is before you fill your pack with
    /// it is the whole point of a loot list. The Inspect button on each
    /// row stands in for the right button; a held press on the row still
    /// does the same, as it does on the hotbar.
    /// </summary>
    public event Action<uint> Look;

    /// <summary>How long a press is held before it describes instead of picking.</summary>
    [Export] public ulong LongPressMs = 600;

    ulong _downAt;
    bool _reverting;

    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title, _empty;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _get, _getAll, _put, _close;

    /// <summary>
    /// What is ticked, by id and nothing else. Never an object: the
    /// reference keeps the selection on the row widget, destroys the
    /// widget when the model drops the item (`UILootList.cpp:148-153`,
    /// `UIObjectContents.cpp:147-152`) and at Get walks the LIVE model
    /// by index (`UILootList.cpp:270-277`, `UIObjectContents.cpp:272-279`).
    /// Holding instances here sent counts as they were when ticked, and
    /// a tick outlived its item. Fill prunes ids that left the list and
    /// <see cref="TickedNow"/> resolves the rest against the model.
    /// </summary>
    readonly HashSet<uint> _ticked = new HashSet<uint>();
    string _signature = "";
    // The model the window is currently showing, so closing it can say
    // so. Only one of the two is ever set - see Dismiss.
    ObjectContents _contents;
    LootInfo _loot;
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim dims the world and eats the touch that would reach
        // it. The CARD is the opaque part, which is the half that
        // matters: LootList/ObjectContents are FrameWindows with no
        // Alpha (Meridian59.layout:2994,2970; UILootList.cpp:8,
        // UIObjectContents.cpp:8), so nothing - the chat log included -
        // draws through the names and counts.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title(Heading);
        _title.Visible = false;
        AddChild(_title);

        // The round close in the title bar does what the footer's Close
        // does - Dismiss - rather than a second, quieter way out.
        _x = M59Skin.CloseX(Dismiss);
        _x.Visible = false;
        AddChild(_x);

        _empty = M59Skin.Empty("There is nothing here to take.");
        _empty.Visible = false;
        AddChild(_empty);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        // Get and Get All close the window in the game as well as
        // sending (`UILootList.cpp:280`, `:295`,
        // `UIObjectContents.cpp:282`), so they go through Dismiss too.
        _get = Action("Get", () =>
        {
            // Nothing ticked: the reference sends nothing and closes
            // anyway (`UILootList.cpp:263-284`, `UIObjectContents.cpp:
            // 265-288`). Not matched, on purpose. On a phone a Get that
            // looks live and silently throws the window away is a
            // mis-tap with a cost, and Close is the button beside it.
            // So Get is DISABLED while nothing is ticked (Caption, run
            // on every open and every tick) and this guard is only the
            // belt to that brace. Nothing ticked is also what is left
            // if every ticked item has left the list.
            List<ObjectBase> picked = TickedNow();
            if (picked.Count == 0) return;
            GetItems?.Invoke(picked);
            Dismiss();
        }, M59Skin.Kind.Primary);
        _put = Action("Put", () => PutWanted?.Invoke());
        _getAll = Action("Get All", () => { GetAll?.Invoke(); Dismiss(); });
        _close = Action("Close", Dismiss);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Action(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// The skin's <see cref="M59Skin.Pick"/>, plus the states a TOGGLE
    /// button draws in. A row here is a CheckBox, so while it is ticked
    /// Godot draws its "pressed" box rather than its "normal" one -
    /// marking only "normal", which is all Pick does, left a ticked row
    /// looking exactly like an unticked one.
    /// </summary>
    static void Mark(Button b, bool on, bool alt)
    {
        M59Skin.Pick(b, on, alt);
        b.AddThemeStyleboxOverride("pressed", b.GetThemeStylebox("normal"));
        b.AddThemeStyleboxOverride("hover_pressed", b.GetThemeStylebox(on ? "normal" : "hover"));
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Sized to the list: three things on the floor is a three-row
        // window, not a tall empty box with the Close at the bottom of
        // it.
        int lines = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        Rect2 card = M59Skin.Frame(v, lines * (RowHeight + 4f));
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        _scroll.Position = body.Position;
        _scroll.Size = body.Size;
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        M59Skin.RowsFit(_rows, body);
        _empty.Position = body.Position;
        _empty.Size = body.Size;

        // Right to left: Close under the thumb that came in with it,
        // and Get - the one thing the window is for - last in the line.
        // Put and Get All are never both up (ShowGetAll is the loot
        // pile, AllowPut the container), and FootRow skips what is
        // hidden, so the one list covers both windows.
        M59Skin.FootRow(foot, _close, _getAll, _put, _get);
    }

    /// <summary>
    /// Closes the window the way the game closes it: by telling the model
    /// it is closed.
    ///
    /// This used to hide the Controls and nothing else, which did not
    /// work at all. The window's visibility is the server's
    /// <c>IsVisible</c> flag, and Sync runs every frame - so the panel
    /// hid itself and the very next frame saw the flag still set and put
    /// itself straight back up. The contents window in particular could
    /// not be closed for the rest of the session, and it blocks movement
    /// and hides the hotbar while it is up.
    ///
    /// The two windows do differ, and the difference is the game's:
    /// the container's contents are cleared as well as hidden
    /// (`UIObjectContents.cpp:282`), because what was in the box is no
    /// longer known once you stop looking; the loot pile is only hidden
    /// (`UILootList.cpp:280`), because what is on the floor is still
    /// there and the library keeps it up to date.
    /// </summary>
    public void Dismiss()
    {
        if (_contents != null) { _contents.IsVisible = false; _contents.Clear(true); }
        if (_loot != null) _loot.IsVisible = false;
        Close();
    }

    public void Close()
    {
        Show(false);
        _ticked.Clear();
        Caption();
        // Or a reopen with the same items would match the signature and
        // short-circuit the rebuild, showing rows that were freed.
        _signature = "";
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) { Panels.ToFront(this); Caption(); }
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _get.Visible = on; _getAll.Visible = on && ShowGetAll;
        _put.Visible = on && AllowPut; _close.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _empty.Visible = on && _rows.GetChildCount() == 0;
        Layout();
    }

    /// <summary>
    /// Follows what is inside a container. The server decides when the
    /// window is up: <c>IsVisible</c> is set when it sends the contents of
    /// something and cleared when it takes them away.
    /// </summary>
    public void Sync(ObjectContents contents)
    {
        _contents = contents; _loot = null;
        // The list is built only for a window that is up. Both Syncs
        // used to copy the model's items into a fresh List every frame,
        // closed or open, and the view runs two of these panels - 88 B
        // a frame at rest for a window nobody could see.
        if (contents == null || !contents.IsVisible) { Fill(null, false); return; }
        _scratch.Clear();
        if (contents.Items != null) foreach (ObjectBase o in contents.Items) _scratch.Add(o);
        Fill(_scratch, true);
    }

    /// <summary>The items handed to Fill, reused across frames; see Sync.</summary>
    readonly List<ObjectBase> _scratch = new List<ObjectBase>();

    /// <summary>
    /// Follows what is loose on the floor around you. Same window, same
    /// switch - `LootInfo.IsVisible` - but the items are room objects the
    /// library filters out of the room for being within reach.
    /// </summary>
    public void Sync(LootInfo loot)
    {
        _loot = loot; _contents = null;
        if (loot == null || !loot.IsVisible) { Fill(null, false); return; }
        _scratch.Clear();
        if (loot.Items != null) foreach (RoomObject o in loot.Items) _scratch.Add(o);
        Fill(_scratch, true);
    }

    void Fill(IList<ObjectBase> items, bool visible)
    {
        if (_rows == null) return;

        // An empty list is an open, empty window, not a closed one. The
        // reference keys the window on the model's IsVisible alone
        // (`UILootList.cpp:53-62`). Treating a count of zero as
        // "invisible" here was worse than cosmetic: Close() does not
        // clear IsVisible - only Dismiss() does - so opening an empty
        // chest left the model visible while this re-closed the panel
        // every frame, and the tap looked like it had done nothing at
        // all.
        if (!visible)
        {
            if (IsOpen) Close();
            return;
        }

        if (!IsOpen) Show(true);

        // A tick belongs to its row, and the row belongs to its item:
        // when the item has left the model its widget is destroyed and
        // the selection goes with it (`UILootList.cpp:148-153`,
        // `UIObjectContents.cpp:147-152`). Done on every pass, before
        // the empty-list and signature exits, so neither can skip it.
        if (_ticked.Count > 0)
        {
            var present = new HashSet<uint>();
            if (items != null) foreach (ObjectBase o in items) if (o != null) present.Add(o.ID);
            if (_ticked.RemoveWhere(id => !present.Contains(id)) > 0) Caption();
        }

        if (items == null || items.Count == 0)
        {
            // Always free the rows, even when `_signature` says there
            // are none: Close() resets the signature without freeing
            // them, so "" does NOT mean "already cleared" - it meant a
            // reopen onto an empty list drew the previous list's rows,
            // live and tickable, under a title saying (0). The
            // reference destroys each widget as the model empties and
            // keys visibility on IsVisible alone
            // (`UILootList.cpp:53-59,148-163`,
            // `UIObjectContents.cpp:147-163`). Both windows alike.
            _signature = "";
            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            _title.Text = $"{Heading} (0)";
            _empty.Visible = true;
            Layout();
            return;
        }

        // The name and the flags are in the signature, not just the id
        // and the count. The contents window is the one window the game
        // rebuilds on ItemChanged as well as on add and delete
        // (`UIObjectContents.cpp:77`, `:165`), and for good reason: the
        // library resolves names out of the string file *after* the list
        // arrives (`DataController.ResolveStrings`), and the flags that
        // pick the name's colour arrive late too. A signature of id and
        // count cannot see either, so rows kept saying "(unnamed)" in
        // the wrong colour for as long as the window was open.
        var sb = Sig.Start();
        foreach (ObjectBase o in items)
            sb.Opt(o?.ID).Append(':').Opt(o?.Count).Append(':')
              .Append(o?.Name).Append(':')
              .Append(o?.Flags != null ? NameColors.GetColorFor(o.Flags) : 0u).Append(':')
              .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        if (!Sig.Changed(sb, ref _signature)) return;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        _title.Text = $"{Heading} ({items.Count})";
        // Alternating tints, so the eye keeps its place down a list of
        // near-identical lines.
        int n2 = 0;
        foreach (ObjectBase o in items)
            if (o != null) _rows.AddChild(Row(o, n2++ % 2 == 1));
        _empty.Visible = false;
        Layout();
    }

    Control Row(ObjectBase o, bool alt)
    {
        ObjectBase captured = o;

        var button = new CheckBox
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            ButtonPressed = _ticked.Contains(o.ID),
            // Named so a scripted run can pick a row: the row's text
            // lives in child labels, so there is nothing to find it by.
            Name = $"loot{o.ID}",
        };
        M59Skin.Dress(button, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
        Mark(button, _ticked.Contains(o.ID), alt);
        TickStyle.Apply(button);
        button.ButtonDown += () => _downAt = Time.GetTicksMsec();
        button.Toggled += on =>
        {
            // A held press describes the row rather than ticking it,
            // and puts the tick back where it was. Setting the property
            // raises this again, hence the guard.
            if (_reverting) return;
            ulong down = _downAt;
            _downAt = 0;
            if (down != 0 && Time.GetTicksMsec() - down >= LongPressMs)
            {
                _reverting = true;
                button.ButtonPressed = !on;
                _reverting = false;
                Look?.Invoke(captured.ID);
                return;
            }
            Mark(button, on, alt);
            Pick(captured.ID, on);
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = RowHeight; line.OffsetTop = 6; line.OffsetRight = -8; line.OffsetBottom = -6;
        button.AddChild(line);

        var icon = new TextureRect
        {
            Texture = Icon(o),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.AddChild(icon);

        // The library's own name colour, not a guess at one.
        uint argb = o.Flags != null ? NameColors.GetColorFor(o.Flags) : NameColors.NORMAL;
        var colour = new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f);

        var name = new Label
        {
            Text = Named(o),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", colour);
        name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        name.AddThemeConstantOverride("outline_size", 3);
        // The name keeps a floor and is cut past it, now that Inspect
        // sits on the row's end - see M59Skin.NameMin.
        M59Skin.NameFits(name);
        line.AddChild(name);

        // Every stack is counted, including a stack of one. The
        // reference sets the box to Count and shows it on IsStackable
        // (`UILootList.cpp:195-196`, `UIObjectContents.cpp:197-198`),
        // and IsStackable is Count > 0 (`ObjectID.cs:202-205`); the
        // two windows agree here. Same fix as the bag
        // (`InventoryPanel.cs:483-491`).
        if (o.Count > 0)
        {
            // Its own right-hand column, so a one-digit count and a
            // three-digit one line up instead of floating wherever the
            // name ended - and "x" in front, so a bare 1 reads as a
            // count rather than as part of the name.
            var amount = new Label
            {
                Text = $"x{o.Count}",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                CustomMinimumSize = new Vector2(80, 0),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            amount.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
            amount.AddThemeColorOverride("font_color", M59Skin.Gold);
            line.AddChild(amount);
        }

        // Inspect: the touch-screen form of the right click that looks
        // at a row in either window (`UILootList.cpp:256-258`,
        // `UIObjectContents.cpp:258-260`). Knowing what a thing is
        // before you fill your pack with it is what the list is for,
        // and the hold above gave no sign it was there. Same Look, same
        // id; the row's tick is untouched because the button takes the
        // press - see M59Skin.Inspect.
        line.AddChild(M59Skin.Inspect($"inspect{button.Name}", () => Look?.Invoke(captured.ID)));

        return button;
    }

    /// <summary>
    /// The row's name, with the game's own suffix on anything the owner
    /// is wearing or wielding: " (in use)", from EN_MISC
    /// (`Language.cpp:118`, appended at `UIObjectContents.cpp:192`).
    /// Without it a list of someone's things gives no hint which of them
    /// are on them - and that is the difference between a sword you can
    /// take and one you cannot.
    /// </summary>
    string Named(ObjectBase o)
    {
        string name = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name;
        // The two windows differ: only the container list appends the
        // suffix (`UIObjectContents.cpp:191-194`); the loot list sets
        // the bare name (`UILootList.cpp:192`). Which one this is, is
        // which model it follows, not a flag somebody has to remember.
        return _contents != null && o.Flags != null && o.Flags.IsEquipped
            ? name + " (in use)" : name;
    }

    /// <summary>
    /// What Get sends: the ticked ids resolved against the model as it is
    /// NOW, in list order, each with the count it has NOW - the
    /// reference's index walk over the live list at click time
    /// (`UILootList.cpp:270-277`, `UIObjectContents.cpp:272-279`).
    /// </summary>
    List<ObjectBase> TickedNow()
    {
        var res = new List<ObjectBase>();
        if (_ticked.Count == 0) return res;
        System.Collections.Generic.IEnumerable<ObjectBase> live = null;
        if (_contents != null) live = _contents.Items;
        else if (_loot != null) live = _loot.Items;
        if (live != null)
            foreach (ObjectBase o in live)
                if (o != null && _ticked.Contains(o.ID)) res.Add(o);
        return res;
    }

    void Pick(uint id, bool on)
    {
        if (on) _ticked.Add(id); else _ticked.Remove(id);
        Caption();
    }

    void Caption()
    {
        if (_get == null) return;
        _get.Text = _ticked.Count == 0 ? "Get" : $"Get ({_ticked.Count})";
        _get.Disabled = _ticked.Count == 0;
    }

    ImageTexture Icon(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        string key = $"{o.Resource.Filename}:{o.ViewerFrameIndex}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[Loot] {o.Name}: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
