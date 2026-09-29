using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Which room to walk around in, chosen in the app rather than by editing
/// the scene.
///
/// This is what makes the offline build worth sideloading: 362 rooms of
/// Meridian on a phone with no server and no account. A search box,
/// because scrolling 362 buttons with a thumb is not a list, it is a
/// punishment.
/// </summary>
public partial class RoomPicker : Control
{
    [Export] public int FontSize = 18;
    [Export] public int MaxShown = 40;

    public event Action<string> Chosen;

    readonly List<string> _all = new List<string>();
    LineEdit _search;
    VBoxContainer _rows;
    ScrollContainer _scroll;
    Label _title;
    Button _open;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Ignore on the root so taps reach the game; the buttons and the
        // backdrop are their own controls and still get theirs.
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Rooms" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);

        _panel = new ColorRect { Color = new Color(0, 0, 0, 0.88f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Rooms", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 6);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _search = new LineEdit { PlaceholderText = "filter", Visible = false };
        _search.AddThemeFontSizeOverride("font_size", FontSize);
        _search.TextChanged += _ => Refill();
        AddChild(_search);

        _scroll = new ScrollContainer { Visible = false };
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        _rows.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    ColorRect _panel;

    /// <summary>Lists the .roo files in a resource folder.</summary>
    public void Load(string resourceDir)
    {
        _all.Clear();
        try
        {
            foreach (string f in Directory.EnumerateFiles(resourceDir, "*.roo"))
                _all.Add(Path.GetFileName(f));
        }
        catch (Exception e) { GD.PrintErr($"[RoomPicker] {e.Message}"); }
        _all.Sort(StringComparer.OrdinalIgnoreCase);
        Refill();
    }

    void Layout()
    {
        Vector2 v = GetViewportRect().Size;
        float pad = 12f;
        float h = FontSize * 2.6f;

        // Sized here rather than anchored: an anchored child of a
        // Control with no rect of its own comes out zero by zero and
        // never draws. See ChatOverlay for the window that spent its
        // whole life invisible for this reason.
        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Bottom right, left of the map toggle. The top right is the map.
        _open.Size = new Vector2(FontSize * 5f, 40f);
        _open.Position = new Vector2(v.X - 70f - pad - _open.Size.X - 8f, v.Y - 40f - pad);

        _title.Position = new Vector2(pad, pad);
        _search.Position = new Vector2(pad, pad + FontSize * 2.6f);
        _search.Size = new Vector2(v.X - pad * 2, h);
        _scroll.Position = new Vector2(pad, pad + FontSize * 5.6f);
        _scroll.Size = new Vector2(v.X - pad * 2, v.Y - _scroll.Position.Y - pad);
    }

    void Open()
    {
        Show(true);
        _search.Text = "";
        Refill();
        _search.GrabFocus();
    }

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on;
        _search.Visible = on; _scroll.Visible = on;
        _open.Visible = !on;
        if (!on) DisplayServer.VirtualKeyboardHide();
    }

    void Refill()
    {
        if (_rows == null) return;
        foreach (Node n in _rows.GetChildren()) n.QueueFree();

        string filter = _search?.Text?.Trim() ?? "";
        IEnumerable<string> shown = _all;
        if (filter.Length > 0)
            shown = _all.Where(s => s.Contains(filter, StringComparison.OrdinalIgnoreCase));

        int n2 = 0;
        foreach (string name in shown)
        {
            if (n2++ >= MaxShown) break;
            string captured = name;
            var b = new Button
            {
                Text = name,
                CustomMinimumSize = new Vector2(0, FontSize * 2.4f),
            };
            b.AddThemeFontSizeOverride("font_size", FontSize);
            b.Pressed += () => { Show(false); Chosen?.Invoke(captured); };
            _rows.AddChild(b);
        }

        _title.Text = filter.Length > 0
            ? $"Rooms  ({Math.Min(n2, MaxShown)} of {_all.Count} matching)"
            : $"Rooms  ({_all.Count})";
    }
}
