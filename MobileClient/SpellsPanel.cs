using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// Your spells and your skills.
///
/// The game has two windows of the same shape - `UISpells.cpp` and
/// `UISkills.cpp` - each a list whose rows are an icon, the name, and how
/// far along you are with it as a percentage. A double click casts the
/// spell or performs the skill.
///
/// The rows come from the client's own `AvatarSpells` and `AvatarSkills`,
/// which the library keeps sorted by name, and each row carries the id
/// that `SendReqCastMessage` and `SendReqPerformMessage` take - those
/// look the object up in the client's own list, so nothing here needs to
/// hold on to a spell.
///
/// The one departure: two windows are one panel with a pair of tabs,
/// because a phone has room for one list and the rows are identical.
/// </summary>
public partial class SpellsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 36;
    [Export] public int RowHeight = 52;

    /// <summary>Cast this spell - the library resolves the id.</summary>
    public event Action<uint> Cast;
    /// <summary>Perform this skill.</summary>
    public event Action<uint> Perform;
    /// <summary>
    /// Describe this one. `UISpells.cpp` sends `ReqLook` on a single
    /// click and casts only on a double - a tap that casts is both
    /// backwards from the game and a good way to throw a spell you
    /// meant to read about.
    /// </summary>
    public event Action<uint> Look;
    /// <summary>Raised on opening, to ask the server for a fresh list.</summary>
    public event Action Opened;
    /// <summary>
    /// Put this on the hotbar: the id, and whether it is a spell. The
    /// game does this by dragging the row onto a button, which a phone
    /// cannot do while this panel is covering the hotbar - so the row
    /// carries a button instead.
    /// </summary>
    public event Action<uint, bool> Assign;

    Button _open;
    ColorRect _panel;
    Label _title;
    Button _tabSpells, _tabSkills, _close;
    ScrollContainer _scroll;
    VBoxContainer _rows;

    bool _showingSpells = true;
    string _signature = "";
    DataController _data;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Book" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.94f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Spells", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _tabSpells = Tab("Spells", () => { _showingSpells = true; _signature = ""; });
        _tabSkills = Tab("Skills", () => { _showingSpells = false; _signature = ""; });
        _close = Tab("Close", () => Close());

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // A ScrollContainer sizes its child to that child's MINIMUM
        // width unless the child asks to expand, and the rows' minimum
        // is whatever their text happens to need. Without this the list
        // was as wide as its longest spell name, so the percentage and
        // the bind button sat wherever each row's text ended - a ragged
        // column of tap targets a thumb has to hunt for, on a panel with
        // most of the screen going spare beside it.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Tab(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>Where the opening button sits, left of whatever else owns the corner.</summary>
    public float RightReserve { get; set; } = 250f;
    /// <summary>Pixels at the top already spoken for by the avatar block.</summary>
    public float TopReserve { get; set; } = 120f;

    void Layout()
    {
        if (_open == null) return;
        Vector2 v = GetViewportRect().Size;
        const float pad = 12f;

        _open.Size = new Vector2(76, 40);
        _open.Position = new Vector2(v.X - RightReserve - 76f, v.Y - 40f - pad);

        float side = Panels.Side(v, 0.05f);
        float rowH = FontSize * 2.6f;

        // Below the corner the avatar block owns - the bars and the
        // portrait are drawn over this panel otherwise.
        float top = Mathf.Max(side, TopReserve);

        // Down to the bottom edge, not to `side` above it. The close
        // button sits in the last row of the panel, and a panel that
        // stopped short of the screen left that row - and the button -
        // floating over the world with the room visible around it.
        _panel.Position = new Vector2(side * 0.5f, top);
        _panel.Size = new Vector2(v.X - side, v.Y - top);

        _title.Position = new Vector2(side, top + 8f);
        _scroll.Position = new Vector2(side, top + FontSize * 4.6f);
        _scroll.Size = new Vector2(v.X - side * 2f, v.Y - top - side - FontSize * 4.6f - rowH - 16f);

        float tabY = top + FontSize * 2.2f;
        Button[] tabs = { _tabSpells, _tabSkills };
        float tw = (v.X - side * 2f - 8f) / 2f;
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].Position = new Vector2(side + i * (tw + 8f), tabY);
            tabs[i].Size = new Vector2(tw, FontSize * 2.2f);
        }

        _close.Position = new Vector2(side, v.Y - side - rowH);
        _close.Size = new Vector2(v.X - side * 2f, rowH);
    }

    public void Open()
    {
        Show(true);
        _signature = "";
        Opened?.Invoke();
    }

    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _tabSpells.Visible = on; _tabSkills.Visible = on; _close.Visible = on;
        _open.Visible = !on;
    }

    /// <summary>Rebuilds when the list changes. Cheap to call every frame.</summary>
    public void Sync(DataController data)
    {
        _data = data;
        if (_rows == null || !IsOpen || data == null) return;

        SkillList list = _showingSpells ? data.AvatarSpells : data.AvatarSkills;
        if (list == null) return;

        var sb = new System.Text.StringBuilder();
        sb.Append(_showingSpells ? 's' : 'k').Append(':');
        foreach (StatList s in list)
            sb.Append(s.ObjectID).Append('/').Append(s.SkillPoints).Append(';');

        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        _title.Text = _showingSpells ? $"Spells ({list.Count})" : $"Skills ({list.Count})";

        // Which tab you are on, said by the tab rather than only by the
        // title. The two buttons were drawn identically whichever list
        // was showing.
        _tabSpells.Flat = !_showingSpells;
        _tabSkills.Flat = _showingSpells;
        _tabSpells.AddThemeColorOverride("font_color",
            _showingSpells ? new Color(1f, 0.92f, 0.6f) : new Color(0.72f, 0.74f, 0.8f));
        _tabSkills.AddThemeColorOverride("font_color",
            _showingSpells ? new Color(0.72f, 0.74f, 0.8f) : new Color(1f, 0.92f, 0.6f));
        foreach (StatList s in list) _rows.AddChild(Row(s));
    }

    /// <summary>The row tapped once, waiting to see if it is tapped again.</summary>
    uint _chosen;

    Control Row(StatList s)
    {
        uint id = s.ObjectID;
        bool spell = _showingSpells;

        var button = new Button { CustomMinimumSize = new Vector2(0, RowHeight) };
        // Named so a test can press a row: the row's text lives in a
        // child label, so there is nothing to find it by otherwise.
        button.Name = $"row{id}";
        button.Pressed += () =>
        {
            // First tap describes, second casts - the phone's version of
            // the game's click and double click.
            if (_chosen == id)
            {
                _chosen = 0;
                if (spell) Cast?.Invoke(id);
                else Perform?.Invoke(id);
            }
            else
            {
                _chosen = id;
                Look?.Invoke(id);
            }
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 8; line.OffsetTop = 4; line.OffsetRight = -8; line.OffsetBottom = -4;
        button.AddChild(line);

        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(s.ResourceName) ? "(unnamed)" : s.ResourceName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        line.AddChild(name);

        // How far along you are with it, which is what the game's rows show.
        var percent = new Label
        {
            Text = $"{s.SkillPoints}%",
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        percent.AddThemeFontSizeOverride("font_size", FontSize);
        percent.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.95f));
        line.AddChild(percent);

        // Sits inside the row but takes its own presses, so tapping it
        // binds without also describing or casting.
        var bind = new Button
        {
            Text = "+",
            TooltipText = "Put on the hotbar",
            CustomMinimumSize = new Vector2(RowHeight, 0),
            Name = $"bind{id}",
        };
        bind.AddThemeFontSizeOverride("font_size", FontSize + 2);
        bind.Pressed += () => Assign?.Invoke(id, spell);
        line.AddChild(bind);

        return button;
    }
}
