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
    /// <summary>
    /// Air under the last button row. Without it the button's bottom
    /// edge and the panel's own are the same line, and the only way out
    /// of the window reads as cut off while every row above it has a
    /// gap.
    /// </summary>
    const float Foot = 12f;

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
    /// it is the whole point of a loot list. A hold stands in for the
    /// right button, as it does on the hotbar.
    /// </summary>
    public event Action<uint> Look;

    /// <summary>How long a press is held before it describes instead of picking.</summary>
    [Export] public ulong LongPressMs = 600;

    ulong _downAt;
    bool _reverting;

    ColorRect _panel;
    Label _title;
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

        // Opaque: LootList/ObjectContents are FrameWindows with no Alpha (Meridian59.layout:2994,2970; UILootList.cpp:8, UIObjectContents.cpp:8).
        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = Heading, Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new ScrollContainer { Visible = false };
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
        });
        _put = Action("Put", () => PutWanted?.Invoke());
        _getAll = Action("Get All", () => { GetAll?.Invoke(); Dismiss(); });
        _close = Action("Close", Dismiss);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Action(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.6f, 560f);
        float top = v.Y - height - side;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _scroll.Position = new Vector2(side, top + FontSize * 2.2f);
        _scroll.Size = new Vector2(v.X - side * 2f, height - FontSize * 2.2f - rowH - 16f);

        float y = top + height - rowH - Foot;
        Button[] row = ShowGetAll
            ? new[] { _get, _getAll, _close }
            : (AllowPut ? new[] { _get, _put, _close } : new[] { _get, _close });
        float w = (v.X - side * 2f - 8f * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(side + i * (w + 8f), y);
            row[i].Size = new Vector2(w, rowH);
        }
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
    }

    /// <summary>
    /// Follows what is inside a container. The server decides when the
    /// window is up: <c>IsVisible</c> is set when it sends the contents of
    /// something and cleared when it takes them away.
    /// </summary>
    public void Sync(ObjectContents contents)
    {
        _contents = contents; _loot = null;
        if (contents == null) { Fill(null, false); return; }
        var list = new List<ObjectBase>(contents.Items ?? (System.Collections.Generic.IEnumerable<ObjectBase>)Array.Empty<ObjectBase>());
        Fill(list, contents.IsVisible);
    }

    /// <summary>
    /// Follows what is loose on the floor around you. Same window, same
    /// switch - `LootInfo.IsVisible` - but the items are room objects the
    /// library filters out of the room for being within reach.
    /// </summary>
    public void Sync(LootInfo loot)
    {
        _loot = loot; _contents = null;
        if (loot == null) { Fill(null, false); return; }
        var list = new List<ObjectBase>();
        if (loot.Items != null) foreach (RoomObject o in loot.Items) list.Add(o);
        Fill(list, loot.IsVisible);
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
        var sb = new System.Text.StringBuilder();
        foreach (ObjectBase o in items)
            sb.Append(o?.ID).Append(':').Append(o?.Count).Append(':')
              .Append(o?.Name).Append(':')
              .Append(o?.Flags != null ? NameColors.GetColorFor(o.Flags) : 0u).Append(':')
              .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        _title.Text = $"{Heading} ({items.Count})";
        foreach (ObjectBase o in items)
            if (o != null) _rows.AddChild(Row(o));
    }

    Control Row(ObjectBase o)
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
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", colour);
        name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        name.AddThemeConstantOverride("outline_size", 3);
        line.AddChild(name);

        // Every stack is counted, including a stack of one. The
        // reference sets the box to Count and shows it on IsStackable
        // (`UILootList.cpp:195-196`, `UIObjectContents.cpp:197-198`),
        // and IsStackable is Count > 0 (`ObjectID.cs:202-205`); the
        // two windows agree here. Same fix as the bag
        // (`InventoryPanel.cs:483-491`).
        if (o.Count > 0)
        {
            var amount = new Label
            {
                Text = o.Count.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            amount.AddThemeFontSizeOverride("font_size", FontSize);
            amount.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
            line.AddChild(amount);
        }

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
