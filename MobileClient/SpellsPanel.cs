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

    /// <summary>
    /// Row icons, by resource file. Both windows build the icon from the
    /// row's own resource - `UISpells.cpp:131-148` and
    /// `UISkills.cpp:151-183` - and both cache it under the resource's
    /// name, because the same spell scrolls past again and again and the
    /// picture does not change. The dictionary is this client's version of
    /// the reference asking CEGUI's ImageManager whether the image is
    /// already defined (`UISkills.cpp:157`).
    /// </summary>
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();

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

        _tabSpells = Tab("Spells", () => { _showingSpells = true; _signature = ""; _chosen = 0; });
        _tabSkills = Tab("Skills", () => { _showingSpells = false; _signature = ""; _chosen = 0; });
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
        _chosen = 0;
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
        _chosen = 0;

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
    ulong _chosenAt;

    /// <summary>
    /// How long the first tap stays "half a double-click". The reference
    /// casts on `EventMouseDoubleClick` (`UISpells.cpp:73-76`, handler
    /// `:300-309`), and a CEGUI double-click is time-bounded; this used to
    /// stay armed forever, so reading a spell and tapping it a minute
    /// later cast it. It is also disarmed by a tab switch, closing the
    /// panel and a list rebuild, none of which can be part of one
    /// double-click.
    /// </summary>
    [Export] public ulong DoubleTapMs = 600;

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
            if (_chosen == id && Time.GetTicksMsec() - _chosenAt <= DoubleTapMs)
            {
                _chosen = 0;
                if (spell) Cast?.Invoke(id);
                else Perform?.Invoke(id);
            }
            else
            {
                _chosen = id;
                _chosenAt = Time.GetTicksMsec();
                Look?.Invoke(id);
            }
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 8; line.OffsetTop = 4; line.OffsetRight = -8; line.OffsetBottom = -4;
        button.AddChild(line);

        // The row's own icon, which both reference windows draw as the
        // first frame of the row's resource - resolved from
        // ResourceIconName (`Meridian59/Data/Models/StatList.cs:280-296`)
        // and then taken as `obj->Resource->Frames[0]`
        // (`UISkills.cpp:159-164`, `UISpells.cpp:139-144`). Not composed as
        // an object the way an inventory item is: a spell is not a thing
        // standing in a room, so there is no pose to compose, and this is
        // the same treatment QuestsPanel gives its rows. Rows had no icon
        // at all before, so a list of thirty spells was thirty identical
        // lines of text.
        ImageTexture icon = Icon(s);
        if (icon != null)
            line.AddChild(new TextureRect
            {
                Texture = icon,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                // IgnoreSize and ShrinkCenter, for the reason QuestsPanel
                // gives: the frame comes at the art's own size, not
                // something scaled to fit, so without both the row grows
                // as tall as the picture and the picture spills over its
                // neighbours.
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(IconSize, IconSize),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });

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

        // A passive skill gets no bind button at all. The reference
        // decides this when it builds the row: an active skill is made
        // from UI_WINDOWTYPE_AVATARSPELLITEM, whose icon sits in a
        // CEGUI::DragContainer with drag events subscribed, and a
        // non-active one from UI_WINDOWTYPE_AVATARSKILLITEM, which has no
        // drag container - so there is nothing to drag onto an action
        // button (`UISkills.cpp:63-85`, and the matching split when the
        // row updates at `UISkills.cpp:125-137`). The flag is
        // SkillObject.IsActiveSkill
        // (`Meridian59/Data/Models/SkillObject.cs:149-161`, read off the
        // wire at `SkillObject.cs:57`), and the library refuses to perform
        // one anyway (`Meridian59/Client/BaseClient.cs:1841-1853`). This
        // row's "+" is the phone's stand-in for that drag, so a passive
        // skill simply does not get one. Spells are never passive.
        if (!spell && IsPassive(id)) return button;

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

    /// <summary>
    /// Whether this skill is one the reference makes undraggable - see the
    /// comment on the bind button. The stat rows this panel lists carry no
    /// such flag, so the SkillObject behind the row is the one that knows
    /// (`UISkills.cpp:60-68` looks it up the same way, by ObjectID).
    /// An unknown skill is treated as bindable: the reference's lookup can
    /// return null too, and it takes the draggable branch only when the
    /// object is there AND active, but refusing on a missing object would
    /// hide the button for a list that simply has not been detailed yet.
    /// </summary>
    bool IsPassive(uint id)
    {
        SkillObject skill = _data?.SkillObjects?.GetItemByID(id);
        return skill != null && !skill.IsActiveSkill;
    }

    ImageTexture Icon(StatList s)
    {
        if (s?.Resource == null) return null;
        string key = $"{s.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromBgf(s.Resource, 0); }
        catch (Exception e) { GD.PrintErr($"[SpellsPanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
