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
    Panel _card, _bar;
    Button _x;
    Label _title;
    Label _empty;
    Button _tabSpells, _tabSkills, _close;
    ScrollContainer _scroll;
    VBoxContainer _rows;

    /// <summary>
    /// The row buttons as built, in list order, so the chosen one can be
    /// re-marked without rebuilding the list. The stripe is remembered
    /// with them: Pick has to be told which stripe a row goes back to
    /// when it stops being the chosen one.
    /// </summary>
    readonly List<(uint id, Button button, bool alt)> _built = new List<(uint, Button, bool)>();

    bool _showingSpells = true;
    string _signature = "";
    bool _iconMissed;
    ulong _iconRetryAt;
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
        Panels.Opener(_open, "Spells & skills", 20);

        // The scrim eats the touch that would reach the world behind.
        // The card is opaque, which the old 0.94 panel was not - the
        // chat log read straight through the spell names.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Spells");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        _tabSpells = Tab("Spells", M59Skin.Kind.Tab, () => { _showingSpells = true; _signature = ""; _chosen = 0; });
        _tabSkills = Tab("Skills", M59Skin.Kind.Tab, () => { _showingSpells = false; _signature = ""; _chosen = 0; });
        _close = Tab("Close", M59Skin.Kind.Secondary, () => Close());

        _empty = M59Skin.Empty("");
        _empty.Visible = false;
        AddChild(_empty);

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
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Tab(string text, M59Skin.Kind kind, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// Pixels at the top already spoken for by the avatar block. Kept
    /// because the view may set it, but the card is centred and bounded
    /// now, so it no longer has to dodge that corner.
    /// </summary>
    public float TopReserve { get; set; } = 120f;

    /// <summary>Height of the tab strip at the top of the body.</summary>
    const float TabH = 44f;

    void Layout()
    {
        if (_open == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Sized to the list it holds, within the screen: a book with two
        // spells in it is a two-row window, not nine hundred pixels of
        // black with two lines at the top. Frame caps both axes.
        float rowH = Mathf.Max(RowHeight, M59Skin.RowH);
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        float want = TabH + M59Skin.Gap + shown * (rowH + 4f);
        // Narrow as well as short. A row here is a name and a percentage;
        // across the whole screen the two ends of it stop reading as one
        // line. See M59Skin.ListW.
        Rect2 card = M59Skin.Frame(v, want, true, M59Skin.ListW);
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

        // The tabs sit at the top of the BODY, inside the card, rather
        // than floating in the panel's dead space: they choose what the
        // list below them shows, so they belong to it.
        Button[] tabs = { _tabSpells, _tabSkills };
        float tw = (body.Size.X - M59Skin.Gap) / 2f;
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].Position = new Vector2(body.Position.X + i * (tw + M59Skin.Gap), body.Position.Y);
            tabs[i].Size = new Vector2(tw, TabH);
        }

        float listY = body.Position.Y + TabH + M59Skin.Gap;
        float listH = Mathf.Max(rowH, body.Position.Y + body.Size.Y - listY);
        _scroll.Position = new Vector2(body.Position.X, listY);
        _scroll.Size = new Vector2(body.Size.X, listH);
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        _rows.CustomMinimumSize = new Vector2(M59Skin.RowsW(body), 0);

        // Over the list, where the rows would have been.
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        // Close at the right of the footer, not stretched across the
        // bottom as the loudest thing on the panel.
        M59Skin.FootRow(foot, _close);
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
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _tabSpells.Visible = on; _tabSkills.Visible = on; _close.Visible = on;
        _open.Visible = !on;
        if (!on) _empty.Visible = false;
        Layout();
    }

    /// <summary>Rebuilds when the list changes. Cheap to call every frame.</summary>
    public void Sync(DataController data)
    {
        _data = data;
        if (_rows == null || !IsOpen || data == null) return;

        SkillList list = _showingSpells ? data.AvatarSpells : data.AvatarSkills;
        if (list == null) return;

        // Every field Row() draws has to be in here, or a row keeps the
        // picture it was built with. The name and the icon are resolved
        // later than the id and the percentage can arrive: ResourceName
        // by StatList's ResolveStrings and Resource from ResourceIconName
        // by ResolveResources (`Meridian59/Data/Models/StatList.cs:259-294`),
        // and an in-place StatMessage rewrites name, percent and icon name
        // on the existing row (`StatList.cs:302-318`). The reference does
        // not poll: it reacts to ListChangedType.ItemChanged, which BaseList
        // raises for any PropertyChanged on a row, and its SpellChange /
        // SkillChange rewrite all three (`UISpells.cpp:109-164`,
        // `UISkills.cpp:38-54`). This signature is the port's substitute
        // for that subscription (see notes/godot-ui.md, "Subscribe, do not
        // poll"), the same shape as Vitals.Follow, AvatarPanel.SyncBuffs
        // and RoomBuffsPanel.Sync: a row that arrived as "(unnamed)" or
        // with no icon was otherwise stuck that way until the panel was
        // reopened. The resource's file name stands for "has art, and
        // which", since the icon is cached by it.
        var sb = new System.Text.StringBuilder();
        sb.Append(_showingSpells ? 's' : 'k').Append(':');
        foreach (StatList s in list)
            sb.Append(s.ObjectID).Append('/').Append(s.SkillPoints).Append('/')
              .Append(s.ResourceName).Append('/').Append(s.ResourceIconName).Append('/')
              .Append(s.Resource?.Filename).Append(';');

        // A row whose art exists but could not be turned into a picture
        // yet (see Icon) gets another try on a timer: nothing in the data
        // changes when the bitmap behind a resource becomes readable, so
        // the signature alone would never notice. Half a second, not every
        // frame - a permanently bad bitmap would otherwise be re-read 60
        // times a second.
        string now = sb.ToString();
        if (_iconMissed && Time.GetTicksMsec() >= _iconRetryAt) _signature = "";
        if (now == _signature) return;
        _signature = now;
        _iconMissed = false;
        _iconRetryAt = Time.GetTicksMsec() + 500;
        _chosen = 0;

        // Freed before their replacements arrive, or the old child still
        // holds the name and Godot renames the new one - see
        // notes/godot-ui.md, "Free a row before you add its replacement".
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _built.Clear();

        _title.Text = _showingSpells ? $"Spells ({list.Count})" : $"Skills ({list.Count})";

        // Which tab you are on, said by the tab rather than only by the
        // title. The two buttons were drawn identically whichever list
        // was showing. Pick is what marks a chosen thing in this skin -
        // the gold edge and the lit fill - and a tab is the one thing on
        // the panel that is chosen whether or not anything was tapped.
        M59Skin.Pick(_tabSpells, _showingSpells);
        M59Skin.Pick(_tabSkills, !_showingSpells);

        int index = 0;
        foreach (StatList s in list)
        {
            bool alt = index++ % 2 == 1;
            Button b = Row(s, alt);
            _rows.AddChild(b);
            _built.Add((s.ObjectID, b, alt));
        }

        // Thirty identical lines of nothing is worse than a sentence.
        _empty.Text = _showingSpells ? "You know no spells." : "You have no skills yet.";
        _empty.Visible = list.Count == 0;

        // The card is sized to the row count, so a list that just
        // changed length needs the frame measured again.
        Layout();
        Mark();
    }

    /// <summary>
    /// Re-marks the row that has been tapped once, so the half of a
    /// double tap you are in the middle of is visible. Pure appearance:
    /// _chosen is set by the row handler either way.
    /// </summary>
    void Mark()
    {
        foreach ((uint id, Button button, bool alt) r in _built)
            if (GodotObject.IsInstanceValid(r.button))
                M59Skin.Pick(r.button, r.id == _chosen && _chosen != 0, r.alt);
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

    Button Row(StatList s, bool alt)
    {
        uint id = s.ObjectID;
        bool spell = _showingSpells;

        // The skin's row is the floor: RowHeight is the knob, but a
        // thumb needs the 56 the skin settled on.
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, Mathf.Max(RowHeight, M59Skin.RowH)),
        };
        // Alternating stripes, because thirty rows of one brown give the
        // eye nothing to count down.
        M59Skin.Dress(button, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
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
            Mark();
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
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(name);

        // How far along you are with it, which is what the game's rows show.
        var percent = new Label
        {
            Text = $"{s.SkillPoints}%",
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        percent.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        percent.AddThemeColorOverride("font_color", M59Skin.Gold);
        // A fixed column, so the numbers line up down the list instead
        // of each landing wherever its spell's name ended.
        percent.CustomMinimumSize = new Vector2(64, 0);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
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
            Name = $"bind{id}",
        };
        // A square stepper rather than a second row-high slab: it is the
        // one thing in the row that takes its own press.
        M59Skin.Dress(bind, M59Skin.Kind.Step);
        bind.CustomMinimumSize = new Vector2(M59Skin.RowH - 12f, M59Skin.TapMin);
        bind.SizeFlagsVertical = SizeFlags.ShrinkCenter;
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
        // Only a picture is worth keeping. Caching the failure answered
        // null for every spell sharing this art for the whole session,
        // though the bitmap behind a resource can be unread at first and
        // fine a moment later (RoomBuffsPanel.Icon, AvatarPanel.BuffIcon).
        if (tex != null) _icons[key] = tex;
        else _iconMissed = true;
        return tex;
    }
}
