using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Your bag: what you are carrying, and what you can do with it.
///
/// Reads the client's own InventoryObjects rather than keeping a copy -
/// the library maintains that list from the server, including which items
/// are in use and how many of a name you have.
///
/// Rebuilt only when the list actually changes. An inventory is a few
/// dozen buttons with a texture each, and rebuilding that every frame
/// would cost more than the game does.
/// </summary>
public partial class InventoryPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int Columns = 3;
    [Export] public int IconSize = 48;

    /// <summary>Use, unuse or apply - the library decides which.</summary>
    public event Action<InventoryObject> UseItem;
    public event Action<InventoryObject> DropItem;
    public event Action<InventoryObject> LookItem;
    /// <summary>Raised when the panel is opened, to ask the server for a fresh list.</summary>
    public event Action Opened;

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
    /// Widens the item buttons to fill the panel. A GridContainer sizes
    /// its columns to their contents, so without this the grid huddles at
    /// the left in a third of the width and every label is clipped.
    /// </summary>
    void SizeCells()
    {
        if (_grid == null || _scroll == null) return;
        const float sep = 8f;
        float cols = Mathf.Max(1, Columns);
        float w = (_scroll.Size.X - sep * (cols - 1)) / cols;
        if (w <= 0f) return;

        _grid.CustomMinimumSize = new Vector2(_scroll.Size.X, 0);
        foreach (Node n in _grid.GetChildren())
            if (n is Button b) b.CustomMinimumSize = new Vector2(w, IconSize + 12);
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

        // Name, count and in-use state are what the buttons show, so they
        // are what decides whether a rebuild is needed.
        var sb = new System.Text.StringBuilder();
        foreach (InventoryObject o in items)
            sb.Append(o?.ID).Append(':').Append(o?.NumOfSameName).Append(o != null && o.IsInUse ? "u" : "-").Append(';');
        string signature = sb.ToString();
        if (signature == _lastSignature && items.Count == _lastCount) return;
        _lastSignature = signature; _lastCount = items.Count;

        foreach (Node n in _grid.GetChildren()) n.QueueFree();

        foreach (InventoryObject o in items)
        {
            if (o == null) continue;
            InventoryObject captured = o;

            string label = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name;
            if (o.NumOfSameName > 1) label += $"  x{o.NumOfSameName}";
            if (o.IsInUse) label += "  *";

            var b = new Button
            {
                Text = label,
                Icon = Icon(o),
                CustomMinimumSize = new Vector2(0, IconSize + 12),
                Alignment = HorizontalAlignment.Left,
            };
            b.AddThemeFontSizeOverride("font_size", FontSize - 2);
            b.Pressed += () => Pick(captured);
            _grid.AddChild(b);
        }

        SizeCells();

        _title.Text = items.Count == 0 ? "Carrying nothing" : $"Carrying ({items.Count})";
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
    ImageTexture Icon(InventoryObject o)
    {
        if (o?.Resource == null) return null;
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = Shrink(M59Assets.FromBgf(o.Resource, frame)); }
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
