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
    /// <summary>Five, as the game has. UI_INVENTORY_COLS.</summary>
    [Export] public int Columns = 5;
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
    /// <summary>Raised when the panel is opened, to ask the server for a fresh list.</summary>
    public event Action Opened;
    /// <summary>
    /// One item dragged onto another: move the first to where the second
    /// is. The game reorders its own inventory list and tells the server,
    /// in that order, so the view does not wait for a round trip.
    /// </summary>
    public event Action<InventoryObject, InventoryObject> MoveItem;

    Button _open;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    GridContainer _grid;
    Label _selected;
    Button _use, _drop, _look, _close;

    InventoryObject _picked;
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

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.94f), Visible = false };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
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

        // Third slot along the bottom right, after the map and the loot
        // button. They agree on the sizes rather than each guessing.
        _open.Size = new Vector2(76, 40);
        _open.Position = new Vector2(v.X - 70f - pad - (76f + 8f) * 2f, v.Y - 40f - pad);

        float side = Mathf.Max(16f, v.X * 0.05f);
        float rowH = FontSize * 2.6f;

        _title.Position = new Vector2(side, side);
        _scroll.Position = new Vector2(side, side + FontSize * 2.4f);
        _scroll.Size = new Vector2(v.X - side * 2f, v.Y - _scroll.Position.Y - rowH * 2f - side);
        SizeCells();

        float y = v.Y - rowH - side * 0.5f;
        _selected.Position = new Vector2(side, y - FontSize * 1.6f);

        Button[] row = { _use, _drop, _look, _close };
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
        float w = (_scroll.Size.X - sep * (Columns - 1)) / Math.Max(1, Columns);
        return Mathf.Clamp(w, SlotSize * 0.5f, SlotSize * 2f);
    }

    public void Open()
    {
        Show(true);
        Opened?.Invoke();
        _lastSignature = "";     // force a rebuild on the next sync
    }

    public void Close() { Show(false); Pick(null); }

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _open.Visible = !on;
        _close.Visible = on;
        if (!on) { _use.Visible = false; _drop.Visible = false; _look.Visible = false; _selected.Visible = false; }
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

        // Count and in-use state are what a slot shows, so they are what
        // decides whether a rebuild is needed.
        var sb = new System.Text.StringBuilder();
        foreach (InventoryObject o in items)
            sb.Append(o?.ID).Append(':').Append(o?.Count).Append(o != null && o.IsInUse ? "u" : "-").Append(';');
        string signature = sb.ToString();
        if (signature == _lastSignature && items.Count == _lastCount) return;
        _lastSignature = signature; _lastCount = items.Count;

        foreach (Node n in _grid.GetChildren()) { _grid.RemoveChild(n); n.QueueFree(); }

        // Rows grow with the bag and never drop below the minimum, so a
        // near-empty inventory still looks like an inventory rather than
        // one lonely icon. AddInventoryRow/RemoveInventoryRow do the same.
        int rows = Math.Max(MinRows, (items.Count + Columns - 1) / Columns);
        int slots = rows * Columns;

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

        if (o != null && o.IsInUse)
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

        if (o.Count > 1)
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
            if (ReferenceEquals(_picked, item)) UseItem?.Invoke(item);
            else Pick(item);
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
        _picked = item;
        bool on = item != null && IsOpen;
        _use.Visible = on; _drop.Visible = on; _look.Visible = on;
        _selected.Visible = on;
        if (!on) return;

        // The library decides between use, unuse and apply; the label
        // should say which of those it is about to do.
        _use.Text = item.Flags.IsApplyable ? "Apply" : (item.IsInUse ? "Unuse" : "Use");
        _selected.Text = string.IsNullOrWhiteSpace(item.Name) ? "(unnamed)" : item.Name;
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
        string key = $"{o.Resource.Filename}:{frame}:{size}";
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
