using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Your bag: what you are carrying, and what you can do with it.
///
/// Laid out like the game's own, which is specified in
/// Meridian59.Ogre.Client/UIInventory.cpp rather than guessed at: a grid
/// five slots across, six rows to begin with and more added as the bag
/// fills, every slot drawn whether or not it holds anything, the icon 40
/// pixels square in a slot of 52, and the count printed on the icon. No
/// labels - the name belongs to the selected item, which on a desktop is
/// a tooltip and here is the line above the buttons.
///
/// Reads the client's own InventoryObjects rather than keeping a copy -
/// the library maintains that list from the server, including which items
/// are in use and how many of a name you have.
///
/// Rebuilt only when the list actually changes. An inventory is a few
/// dozen slots with a texture each, and rebuilding that every frame would
/// cost more than the game does.
/// </summary>
public partial class InventoryPanel : Control
{
    [Export] public int FontSize = 16;
    /// <summary>
    /// Five is the game's number (UI_INVENTORY_COLS), for a window 284
    /// pixels wide. A phone held sideways is two thousand pixels wide
    /// and a hundred-item bag showing five of them at a time is not an
    /// inventory, it is a peephole - Ashton's word for it was
    /// "unuseable". So five is the FLOOR and the width decides the
    /// rest, at a slot size a finger can hit.
    /// </summary>
    [Export] public int Columns = 5;

    /// <summary>How many columns actually fit, never fewer than Columns.</summary>
    int Across()
    {
        if (_scroll == null || _scroll.Size.X < 1f) return Columns;
        const float sep = 8f;
        int fit = (int)((_scroll.Size.X + sep) / (SlotSize + sep));
        return Math.Max(Columns, fit);
    }
    /// <summary>UI_INVENTORYICON_WIDTH/HEIGHT - the icon inside the slot.</summary>
    [Export] public int IconSize = 40;
    /// <summary>UI_INVENTORY_MIN_ROWS - the bag is never smaller than this.</summary>
    [Export] public int MinRows = 6;
    /// <summary>
    /// Slots are drawn bigger than the game's 52 pixels because a finger
    /// is not a mouse pointer; the art inside keeps the game's ratio.
    /// </summary>
    [Export] public int SlotSize = 96;

    /// <summary>Use, unuse or apply - the library decides which.</summary>
    public event Action<InventoryObject> UseItem;
    public event Action<InventoryObject> DropItem;
    public event Action<InventoryObject> LookItem;
    /// <summary>
    /// Put this item on the hotbar. The game binds a button by dragging
    /// the item out of this grid onto it (`UIActionButtons.cpp:384`),
    /// which a phone cannot do while this panel is covering the hotbar.
    /// </summary>
    public event Action<InventoryObject> BindItem;
    /// <summary>Raised when the panel is opened, to ask the server for a fresh list.</summary>
    public event Action Opened;
    /// <summary>
    /// One item dragged onto another: move the first to where the second
    /// is. The game reorders its own inventory list and tells the server,
    /// in that order, so the view does not wait for a round trip.
    /// </summary>
    public event Action<InventoryObject, InventoryObject> MoveItem;
    /// <summary>
    /// A single tap while picking. Used by the trade window, which
    /// needs one thing chosen and the bag out of the way again - the
    /// game drags between two windows instead, which a phone cannot
    /// show at once.
    /// </summary>
    public event Action<InventoryObject> Picked;

    /// <summary>
    /// Asked for something and then backed out - the Close button while
    /// PickMode was still set.
    ///
    /// Whoever turned PickMode on also remembers WHY, and that note was
    /// only ever torn up by a tap. Closing the bag instead left both
    /// standing: the next time the bag was opened normally the first tap
    /// on anything closed it again and handed the item to whoever had
    /// asked last - and if that was the trade, the item joined
    /// Trade.ItemsYou with no trade window anywhere and went out in the
    /// next offer.
    /// </summary>
    public event Action PickCancelled;

    /// <summary>
    /// A tap has settled into a target, one double-tap window after it.
    ///
    /// Not the same event as Selected, and that is the whole of finding
    /// two. The reference's click does NOT target: it sets DoClick and
    /// remembers the object (`UIInventory.cpp:332-334`), and Tick writes
    /// Data.TargetID only once CanInventoryClick() is true again -
    /// 250 milliseconds later (`:294-300`, `GameTickOgre.h:39,47`). A
    /// double click inside that window clears DoClick and applies
    /// instead (`:350-352`), so the target never moved and SendReqApply
    /// aims at whatever you had chosen in the world
    /// (`BaseClient.cs:2080-2084`).
    ///
    /// Setting TargetID on the tap itself - which this did - meant an
    /// applyable item could only ever be applied to itself.
    /// </summary>
    public event Action<InventoryObject> Targeted;

    /// <summary>
    /// The selection changed, including to nothing. Raised at the tap.
    ///
    /// The clicked item does become `Data.TargetID` - the library
    /// resolves a target id against the room first and your own
    /// inventory second, so a carried thing is a legitimate target and
    /// the game's target window shows it. But not on the click:
    /// `UIInventory.cpp` sets it from Tick, a double-click window later
    /// (`:294-300`). That half is Targeted; this one is only what the tap
    /// changes on screen at once.
    /// </summary>
    public event Action<InventoryObject> Selected;
    /// <summary>One tap chooses and closes, instead of select-then-use.</summary>
    public bool PickMode;

    Button _open;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    GridContainer _grid;
    Label _selected;
    Button _use, _drop, _look, _bind, _close;

    /// <summary>
    /// UI_INTERVALINVENTORYCLICK: how long a second tap has to arrive
    /// within to count as a double one, and how long a single tap waits
    /// before it becomes the target. 250 milliseconds, which is the
    /// reference's own number (`GameTickOgre.h:39,47`) and the only
    /// thing telling a double click from two clicks there.
    /// </summary>
    const ulong DoubleTapMs = 250;

    InventoryObject _picked;
    /// <summary>A tap waiting out the double-tap window to become the target.</summary>
    InventoryObject _arming;
    ulong _armedAt;
    ulong _lastTapAt;
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    int _lastCount = -1;
    string _lastSignature = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Bag" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        // Opaque: the reference window is a TaharezLook FrameWindow with no Alpha (Meridian59.layout:1396, UIInventory.cpp:8).
        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Carrying", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 6);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        _grid.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;

        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_grid);
        AddChild(_scroll);

        _selected = new Label { Visible = false };
        _selected.AddThemeFontSizeOverride("font_size", FontSize);
        _selected.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        AddChild(_selected);

        _use   = MakeAction("Use",   () => { if (_picked != null) UseItem?.Invoke(_picked); });
        _drop  = MakeAction("Drop",  () => { if (_picked != null) DropItem?.Invoke(_picked); });
        _look  = MakeAction("Look",  () => { if (_picked != null) LookItem?.Invoke(_picked); });
        _bind  = MakeAction("Hotbar",() => { if (_picked != null) BindItem?.Invoke(_picked); });
        _close = MakeAction("Close", Close);

        GetViewport().SizeChanged += Layout;
        Layout();
        Pick(null);
    }

    Button MakeAction(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_open == null) return;
        Vector2 v = GetViewportRect().Size;
        const float pad = 12f;

        // Sized here rather than anchored: an anchored child of a
        // Control with no rect of its own comes out zero by zero and
        // never draws. See ChatOverlay for the window that spent its
        // whole life invisible for this reason.
        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Third slot along the bottom right, after the map and the loot
        // button. They agree on the sizes rather than each guessing.
        _open.Size = new Vector2(76, 40);
        _open.Position = new Vector2(v.X - 70f - pad - (76f + 8f) * 2f, v.Y - 40f - pad);

        float side = Panels.Side(v, 0.05f);
        float rowH = FontSize * 2.6f;

        _title.Position = new Vector2(side, side);
        _scroll.Position = new Vector2(side, side + FontSize * 2.4f);
        _scroll.Size = new Vector2(v.X - side * 2f, v.Y - _scroll.Position.Y - rowH * 2f - side);
        SizeCells();

        float y = v.Y - rowH - side * 0.5f;
        _selected.Position = new Vector2(side, y - FontSize * 1.6f);

        Button[] row = { _use, _drop, _look, _bind, _close };
        float w = (v.X - side * 2f - 8f * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(side + i * (w + 8f), y);
            row[i].Size = new Vector2(w, rowH);
        }
    }

    /// <summary>
    /// Holds the grid to the slot size. A GridContainer sizes its columns
    /// to their contents, so without this the grid huddles at the left
    /// and the slots come out whatever size their icon happens to be.
    /// </summary>
    void SizeCells()
    {
        if (_grid == null || _scroll == null) return;

        // The game's slots are a fixed 52 pixels in a window 284 wide,
        // which is five of them and no more. A phone is wider than that
        // and holds no more columns, so the slots grow to fill it rather
        // than huddling in a corner - five across either way.
        float side = Cell();
        if (side <= 0f) return;

        foreach (Node n in _grid.GetChildren())
            if (n is Control c) c.CustomMinimumSize = new Vector2(side, side);
    }

    /// <summary>How big one slot is drawn, in screen pixels.</summary>
    float Cell()
    {
        if (_scroll == null) return SlotSize;
        const float sep = 8f;
        int cols = Across();
        float w = (_scroll.Size.X - sep * (cols - 1)) / Math.Max(1, cols);
        return Mathf.Clamp(w, SlotSize * 0.5f, SlotSize * 2f);
    }

    public void Open()
    {
        Show(true);
        Opened?.Invoke();
        _lastSignature = "";     // force a rebuild on the next sync
    }

    /// <summary>
    /// Shuts the bag, and drops the picking mode with it.
    ///
    /// PickMode was cleared only by a tap, so backing out of "pick an
    /// item" with Close left it set - and the note saying who had asked
    /// stayed with it. See PickCancelled. The tap path clears PickMode
    /// itself before calling this, so a pick that succeeded raises
    /// nothing here.
    /// </summary>
    public void Close()
    {
        bool wasPicking = PickMode;
        PickMode = false;
        Show(false);
        Pick(null);
        if (wasPicking) PickCancelled?.Invoke();
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _open.Visible = !on;
        _close.Visible = on;
        if (!on) { _use.Visible = false; _drop.Visible = false; _look.Visible = false; _bind.Visible = false; _selected.Visible = false; }
        Layout();
    }

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>
    /// Rebuilds the grid if the inventory has changed. Cheap to call every
    /// frame: it compares a signature and does nothing when nothing moved.
    /// </summary>
    public void Sync(IList<InventoryObject> items)
    {
        if (_grid == null || !IsOpen || items == null) return;

        // What a slot shows is what decides whether it is rebuilt, and
        // that is more than the count and the in-use mark. An object's
        // picture changes with its colour translation, its effect and
        // its frame - an item dyed, enchanted, lit or drawn - and its
        // name changes outright when the server identifies a magic
        // item. The reference's per-slot composer re-pushes a texture
        // on any of those; a signature of id, count and use alone
        // leaves the old bitmap and the old name there for the rest of
        // the session.
        var sb = new System.Text.StringBuilder();
        foreach (InventoryObject o in items)
        {
            sb.Append(o?.ID).Append(':').Append(o?.Count)
              .Append(o != null && o.IsInUse ? "u" : "-").Append(':')
              .Append(o?.Name).Append(':').Append(o?.ColorTranslation).Append(':')
              .Append(o?.Effect).Append(':').Append(o?.ViewerFrameIndex).Append(';');
        }
        // The column count is part of it: turn the phone and the grid
        // has to be rebuilt, and a signature that ignored the width
        // left five columns on a screen with room for fifteen.
        sb.Append('@').Append(Across());
        string signature = sb.ToString();
        if (signature == _lastSignature && items.Count == _lastCount) return;

        // The selection is dropped when what it pointed at has left the
        // bag, and re-pointed at the fresh instance when it has not.
        //
        // Nothing did either. _picked held the object the slot was built
        // with, so dropping the last of a stack, selling it or handing it
        // over left the buttons live over a dead id - Use, Drop and Look
        // all went out naming an object the server no longer had. And a
        // surviving item is a NEW instance after a rebuild, so the count
        // the Drop prompt opened with was the count that item had at the
        // last tap, not the count it has now.
        if (_picked != null)
        {
            InventoryObject still = null;
            foreach (InventoryObject o in items)
                if (o != null && o.ID == _picked.ID) { still = o; break; }

            if (still == null) Pick(null);
            else if (!ReferenceEquals(still, _picked))
            {
                // Not through Pick: the item was not tapped, so this must
                // not re-arm the target or move the selection.
                _picked = still;
                Relabel(still);
            }
        }

        _lastSignature = signature; _lastCount = items.Count;

        foreach (Node n in _grid.GetChildren()) { _grid.RemoveChild(n); n.QueueFree(); }

        // Rows grow with the bag and never drop below the minimum, so a
        // near-empty inventory still looks like an inventory rather than
        // one lonely icon. AddInventoryRow/RemoveInventoryRow do the same.
        int cols = Across();
        if (_grid.Columns != cols) _grid.Columns = cols;
        // The floor is a number of SLOTS, not a number of rows.
        //
        // The game is 5 columns by at least 6 rows (Constants.h:867-878)
        // - thirty slots. This grows the columns with the screen, which
        // is right in landscape, and then kept six rows of them: on a
        // 1920-wide screen that is sixteen columns and ninety-six
        // cells, so three items were shown in a grid covering the
        // entire display, ninety-three of it empty. The same thirty
        // slots, laid out however wide the screen is, is what the game
        // means.
        int floorRows = Math.Max(1, (MinRows * Columns + cols - 1) / cols);
        int rows = Math.Max(floorRows, (items.Count + cols - 1) / cols);
        int slots = rows * cols;

        for (int i = 0; i < slots; i++)
        {
            InventoryObject o = i < items.Count ? items[i] : null;
            _grid.AddChild(Slot(o));
        }

        SizeCells();
        _title.Text = items.Count == 0 ? "Carrying nothing" : $"Carrying ({items.Count})";
    }

    /// <summary>
    /// One slot: a framed square, the icon centred in it, the count in the
    /// corner if there is more than one, and a mark if the thing is in
    /// use. An empty slot is the same square with nothing in it.
    /// </summary>
    Control Slot(InventoryObject o)
    {
        var slot = new InventorySlot
        {
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            Item = o,
            MouseFilter = MouseFilterEnum.Stop,
        };

        var box = new StyleBoxFlat
        {
            BgColor = o != null ? new Color(0.13f, 0.13f, 0.16f) : new Color(0.07f, 0.07f, 0.09f),
            BorderColor = new Color(0.3f, 0.3f, 0.36f),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);

        // The one you have chosen. The buttons and the name label at
        // the bottom said which item was picked, and the grid said
        // nothing - with a hundred slots of the same three icons, the
        // name was the only way to know what was selected and there was
        // no way at all to see WHERE. The reference has the row
        // highlighted by the list widget itself (`UIInventory.cpp` puts
        // the selection on the ItemListbox), so this is the same thing
        // said in a grid.
        // By id, not by instance: the slot keeps the object it was
        // built with, and a rebuild can hand out a different one for
        // the same item.
        if (o != null && _picked != null && o.ID == _picked.ID)
        {
            box.BgColor = new Color(0.22f, 0.24f, 0.32f);
            // The two marks are not exclusive. Picking an item used to
            // overwrite its in-use border, which hid the one thing the
            // Use button is about to change - the selected slot said
            // "Unuse" underneath and looked exactly like a slot holding
            // something idle. The lit background says picked; the colour
            // of the border still says worn.
            box.BorderColor = o.IsInUse
                ? new Color(1f, 0.8f, 0.35f)
                : new Color(0.75f, 0.85f, 1f);
            box.SetBorderWidthAll(3);
        }
        else if (o != null && o.IsInUse)
        {
            // The game glows the background of an item in use - the
            // composer turns its background on for exactly that. A warm
            // border says the same thing without a second texture.
            box.BorderColor = new Color(1f, 0.8f, 0.35f);
            box.SetBorderWidthAll(2);
        }
        slot.AddThemeStyleboxOverride("panel", box);

        if (o == null) return slot;

        InventoryObject captured = o;

        var icon = new TextureRect
        {
            Texture = Icon(o, IconPixels()),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = 6; icon.OffsetTop = 6; icon.OffsetRight = -6; icon.OffsetBottom = -6;
        slot.AddChild(icon);

        // Every stack is badged, including one holding a single item.
        //
        // The reference prints the count whenever Count > 0
        // (`UIInventory.cpp:223-224` on add, `:287-288` on change), and
        // Count > 0 is exactly what IsStackable means
        // (`ObjectID.cs:202-205`). Hiding the badge at 1 made a stack of
        // one look like a plain object, so Drop opened an amount prompt
        // for something that by every visible sign was not a stack - and
        // the same stack becoming two items grew a number out of nowhere.
        if (o.Count > 0)
        {
            var count = new Label
            {
                Text = o.Count.ToString(),
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            count.AddThemeFontSizeOverride("font_size", FontSize - 2);
            count.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            count.AddThemeConstantOverride("outline_size", 4);
            count.SetAnchorsPreset(LayoutPreset.FullRect);
            count.OffsetRight = -4; count.OffsetBottom = -2;
            slot.AddChild(count);
        }

        // A tap picks the item - the game targets it on a left click -
        // and a second tap uses it, as a double click does there. The
        // slot handles it itself, because a Button laid over the top
        // would swallow the press a drag has to start from.
        slot.TooltipText = captured.Name;
        slot.Preview = icon.Texture;
        slot.Tapped += item =>
        {
            if (PickMode) { PickMode = false; Close(); Picked?.Invoke(item); return; }

            ulong now = Time.GetTicksMsec();
            ulong since = now - _lastTapAt;
            _lastTapAt = now;

            // By id. The slot holds the object it was built with and a
            // rebuild can hand out another instance for the same item,
            // so an identity test made the second tap on a slot select
            // it again instead of using it - and the selection border
            // never appeared either, which is how this was found.
            //
            // And only inside the double-tap window. The reference uses
            // or applies on a DOUBLE click (`UIInventory.cpp:344-353`,
            // reached only while CanInventoryClick() is false); a slow
            // second click falls into the single-click branch and just
            // re-targets (`:327-335`). Without the window, a tap to see
            // what something is and a tap a minute later to read the
            // name again wielded it, unwielded it, or drank it.
            if (_picked != null && _picked.ID == item.ID && since < DoubleTapMs)
            {
                // The double tap cancels the target the first one armed,
                // exactly as the reference's `DoClick = false` does
                // (`UIInventory.cpp:351`). That is what leaves the world
                // target standing for an Apply.
                _arming = null;
                UseItem?.Invoke(item);
                return;
            }
            Pick(item);
        };
        slot.Moved += (from, to) => MoveItem?.Invoke(from, to);

        return slot;
    }

    /// <summary>
    /// Selects an item from outside, as a tap on it would. Only the
    /// screenshot harness uses this; the panel picks its own otherwise.
    /// </summary>
    public void Choose(InventoryObject item) => Pick(item);

    void Pick(InventoryObject item)
    {
        bool moved = !ReferenceEquals(_picked, item);
        _picked = item;
        // Arm the target rather than set it. Tick does the setting, one
        // window later - see Targeted.
        _arming = item;
        _armedAt = Time.GetTicksMsec();
        // The grid draws the selection, so it has to be rebuilt when
        // the selection moves.
        if (moved) _lastSignature = "";
        Selected?.Invoke(item);
        bool on = item != null && IsOpen;
        _use.Visible = on; _drop.Visible = on; _look.Visible = on; _bind.Visible = on;
        _selected.Visible = on;
        if (!on) return;

        Relabel(item);
    }

    /// <summary>
    /// What the buttons say about the item that is picked. Split out so a
    /// rebuild can re-point the selection at a fresh instance of the same
    /// object without pretending it was tapped again.
    /// </summary>
    void Relabel(InventoryObject item)
    {
        if (item == null) return;
        // The library decides between use, unuse and apply; the label
        // should say which of those it is about to do.
        _use.Text = item.Flags.IsApplyable ? "Apply" : (item.IsInUse ? "Unuse" : "Use");
        _selected.Text = string.IsNullOrWhiteSpace(item.Name) ? "(unnamed)" : item.Name;
    }

    /// <summary>
    /// The reference's Inventory::Tick: a tap that was not answered by a
    /// second one inside the window becomes the target
    /// (`UIInventory.cpp:292-301`).
    /// </summary>
    public override void _Process(double delta)
    {
        if (_arming == null) return;
        if (Time.GetTicksMsec() - _armedAt < DoubleTapMs) return;

        InventoryObject landing = _arming;
        _arming = null;
        Targeted?.Invoke(landing);
    }

    /// <summary>The frame the library says to show for this object.</summary>
    /// <summary>
    /// The size to compose an icon at. The game's is 40 pixels in a
    /// 52-pixel slot; keeping that ratio at whatever size the slot is
    /// actually drawn means the art is composed sharp rather than
    /// composed small and stretched.
    /// </summary>
    int IconPixels() => Mathf.Max(IconSize, Mathf.RoundToInt(Cell() * IconSize / 52f));

    ImageTexture Icon(InventoryObject o, int size)
    {
        if (o?.Resource == null) return null;
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        // Keyed on everything the composed picture depends on, not the
        // file alone: two of the same item dyed differently are two
        // pictures, and one that gains an effect is a third.
        string key = $"{o.Resource.Filename}:{frame}:{size}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, size)); }
        catch (Exception e) { GD.PrintErr($"[Inventory] {o.Name}: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }

    /// <summary>
    /// Scales an icon to fit the row, keeping its aspect. Letting the
    /// button do it with ExpandIcon does not work: the icon gets whatever
    /// width the label leaves it, so a wide picture - an ear of corn is
    /// 860 pixels across - ends up a sliver a few pixels wide, and a
    /// 17-pixel ankh towers over it.
    /// </summary>
    ImageTexture Shrink(ImageTexture tex)
    {
        if (tex == null) return null;
        Image img = tex.GetImage();
        if (img == null) return tex;

        float k = Mathf.Min(IconSize / (float)img.GetWidth(), IconSize / (float)img.GetHeight());
        int w = Mathf.Max(1, Mathf.RoundToInt(img.GetWidth() * k));
        int h = Mathf.Max(1, Mathf.RoundToInt(img.GetHeight() * k));
        if (w == img.GetWidth() && h == img.GetHeight()) return tex;

        // Nearest keeps the pixel art crisp; the game's own art is not
        // drawn to be smoothed.
        img.Resize(w, h, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(img);
    }
}
