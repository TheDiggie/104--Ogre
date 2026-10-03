using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// The hotbar, laid out flat: every page as a row of seats, so the
/// player can put a binding WHERE he wants it rather than where the
/// next empty seat happens to be.
///
/// The reference needs no such window. Its forty-eight buttons are all
/// on screen at once (`UIActionButtons.cpp:23-26`), you drag a spell
/// from the book onto the cell you want (`:359-438`) and drag a cell
/// onto the root window to empty it (`:471-473`). On a phone the list
/// you drag from covers the hotbar you drop on, so the list panels
/// grew a "+" that takes the first empty seat (ActionButtons.Bind) and
/// the arc grew pages. That left two things nobody could do: choose the
/// seat, and move a binding once it was in one. This window is those
/// two things - and only those. What a seat DOES when pressed is still
/// the library's dispatch; this writes the slot with the library's own
/// setters (`ActionButtonConfig.SetToSpell/SetToSkill/SetToItem/
/// SetToAction`), the same calls Bind makes, and nothing else.
///
/// THE MODEL is the cluster's (ActionButtons.Sync): every config that
/// is not the Attack primary, in Num order, cut into pages of
/// <see cref="ActionButtons.HotSeats"/>. A seat on the last page past
/// the end of the list is drawn empty too and is a real seat here -
/// setting it appends configs up to it. A move is a swap of two Nums,
/// which is a move because the arc sorts on Num and the store writes
/// in Num order; the configs themselves, their listeners and the
/// client's subscriptions to them, are never touched.
///
/// The primary is shown once at the top and is not a seat in the rows,
/// because it is not in them: it is the big disc under the thumb on
/// every page. It CAN be Set - the owner asked to "change their big
/// attack button to other things" - and the disc is then whatever was
/// chosen, by Num (ActionButtons.PrimaryNum); it cannot be cleared or
/// moved. Empty, the row offers to put Attack back.
/// </summary>
public partial class HotKeysPanel : Control
{
    /// <summary>A seat in the rows. The cluster's seat is 96; the rows are read, not fought with.</summary>
    const float SlotSize = 88f;
    /// <summary>The composed picture inside a seat, and inside a chooser row.</summary>
    const int SlotIcon = 52, RowIcon = 36;
    /// <summary>The page caption's column, so the seats line up page to page.</summary>
    const float PageW = 150f;
    /// <summary>Height of the chooser's tab strip at the top of the body.</summary>
    const float TabH = 44f;

    /// <summary>Raised when the window opens, so the view can ask for spells, skills and the pack.</summary>
    public event Action Opened;

    Button _open;
    ColorRect _scrim;
    M59Skin.Chrome _chrome;
    TouchScroll _scroll;
    VBoxContainer _rows;
    Button _close, _back, _set, _clear, _left, _right;
    Button[] _tabs;
    Label _empty;
    DataController _data;

    /// <summary>Position in the arc of the chosen seat; -1 for none.</summary>
    int _picked = -1;
    /// <summary>Whether the body is the chooser rather than the pages.</summary>
    bool _choosing;
    /// <summary>Which of the chooser's lists is up.</summary>
    enum Tab { Actions, Spells, Skills, Items }
    Tab _tab = Tab.Actions;
    string _signature = "";
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    bool _iconMissed;
    ulong _iconRetryAt;
    readonly List<ActionButtonConfig> _arc = new List<ActionButtonConfig>();

    public bool IsOpen => _scrim != null && _scrim.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "HotKeys" };
        _open.AddThemeFontSizeOverride("font_size", 16);
        _open.Pressed += Open;
        AddChild(_open);
        // Beside Actions, which is the other window that writes the hotbar.
        Panels.Opener(_open, "Hotbar pages", 35);

        _scrim = new ColorRect { Color = M59Skin.Scrim, Visible = false, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_scrim);

        _chrome = new M59Skin.Chrome(Close);
        _chrome.Name.Text = "Hotbar pages";
        _chrome.AddTo(this);
        _chrome.Show(false);

        _tabs = new Button[4];
        string[] names = { "Actions", "Spells", "Skills", "Items" };
        for (int i = 0; i < 4; i++)
        {
            Tab t = (Tab)i;
            _tabs[i] = new Button { Text = names[i], Visible = false, Name = "hkTab" + names[i] };
            M59Skin.Dress(_tabs[i], M59Skin.Kind.Tab);
            _tabs[i].Pressed += () => { _tab = t; _signature = ""; };
            AddChild(_tabs[i]);
        }

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _empty = M59Skin.Empty("");
        _empty.Visible = false;
        AddChild(_empty);

        // The strip. It acts on the chosen seat, so it lives in the
        // footer where it is the same four targets whichever page the
        // seat is on, rather than a strip per row that would put
        // sixteen small buttons under the pages.
        _set = Foot("Set…", "hkSet", M59Skin.Kind.Primary, () => { if (_picked >= 0 || _picked == PickPrimary) { _choosing = true; _signature = ""; Layout(); } });
        _clear = Foot("Clear", "hkClear", M59Skin.Kind.Secondary, () => Clear(_picked));
        _left = Foot("◀", "hkLeft", M59Skin.Kind.Secondary, () => Swap(_picked, -1));
        _right = Foot("▶", "hkRight", M59Skin.Kind.Secondary, () => Swap(_picked, +1));
        _close = Foot("Close", "hkClose", M59Skin.Kind.Secondary, Close);
        _back = Foot("Back", "hkBack", M59Skin.Kind.Secondary, () => { _choosing = false; _signature = ""; Layout(); });

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Foot(string text, string name, M59Skin.Kind kind, Action pressed)
    {
        var b = new Button { Text = text, Visible = false, Name = name };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    public void Open()
    {
        _choosing = false;
        _picked = -1;
        _signature = "";
        Panels.ToFront(this);
        Show(true);
        Opened?.Invoke();
    }

    public void Close() => Show(false);

    void Show(bool on)
    {
        _scrim.Visible = on;
        _chrome.Show(on);
        _scroll.Visible = on;
        _open.Visible = !on;
        if (!on) { _empty.Visible = false; foreach (Button t in _tabs) t.Visible = false; }
        Panels.ShowOpeners(!on);
        if (on) Layout();
        else foreach (Button b in new[] { _set, _clear, _left, _right, _close, _back }) b.Visible = false;
    }

    void Layout()
    {
        if (_scrim == null) return;
        Vector2 v = GetViewportRect().Size;
        _scrim.Position = Vector2.Zero;
        _scrim.Size = v;

        // As tall as its pages and as wide as a row: a row here is a
        // word and HotSeats seats, which across the whole of a sideways
        // screen would be a caption at one end and the seats a long way
        // off. The width is READ off HotSeats rather than assumed: at
        // five the list measure (ListW) held a row; at six it did not,
        // and the sixth seat ran under the scrollbar. The chooser takes
        // all the height it can get, since a spell list is as long as
        // the character is old. Frame caps both against the screen, and
        // the list scrolls past the cap.
        float want = 0f;
        if (!_choosing && _data?.ActionButtons != null)
            want = (Pages(Arc(_data).Count) + 1) * (SlotSize + 12f + 6f) + M59Skin.RowH + 6f;
        float rowW = PageW + ActionButtons.HotSeats * (SlotSize + M59Skin.Gap)
                   + M59Skin.ScrollBarW + M59Skin.Pad * 2f;
        Rect2 card = M59Skin.Frame(v, want, true, Mathf.Max(M59Skin.ListW, rowW));
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);
        _chrome.Place(card);

        float listY = body.Position.Y;
        if (_choosing)
        {
            float tw = (body.Size.X - M59Skin.Gap * 3f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                _tabs[i].Position = new Vector2(body.Position.X + i * (tw + M59Skin.Gap), body.Position.Y);
                _tabs[i].Size = new Vector2(tw, TabH);
                _tabs[i].Visible = IsOpen;
            }
            listY += TabH + M59Skin.Gap;
        }
        else foreach (Button t in _tabs) t.Visible = false;

        _scroll.Position = new Vector2(body.Position.X, listY);
        _scroll.Size = new Vector2(body.Size.X, Mathf.Max(M59Skin.RowH, body.Position.Y + body.Size.Y - listY));
        // Clear of the scrollbar - see M59Skin.RowsFit.
        M59Skin.RowsFit(_rows, body);
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        bool on = IsOpen;
        _back.Visible = on && _choosing;
        _close.Visible = on && !_choosing;
        _set.Visible = _clear.Visible = _left.Visible = _right.Visible = on && !_choosing;
        // The Primary row takes Set and nothing else: "do not let people
        // drag off the attack action" is a rule about losing the disc,
        // and Clear here would be the same loss by another door; and a
        // move is a swap of arc Nums, which the disc is not among. Set
        // REPLACES, which is how the disc changes.
        bool primary = _picked == PickPrimary;
        _clear.Disabled = _left.Disabled = _right.Disabled = primary;
        if (_choosing) M59Skin.FootRow(foot, _back);
        else
        {
            // Close at the right, the strip to its left, and a gap
            // between the two: Clear next to Close is a miss away from
            // losing a binding.
            float x = M59Skin.FootRow(foot, _close) - M59Skin.Gap * 2f;
            var left = new Rect2(foot.Position, new Vector2(Mathf.Max(0f, x - foot.Position.X), foot.Size.Y));
            M59Skin.FootRow(left, _right, _left, _clear, _set);
        }
    }

    // ---- the model -------------------------------------------------

    /// <summary>
    /// The primary, as the cluster finds it (ActionButtons.Primary): the
    /// chosen Num, or the seat holding Attack. One answer for the disc
    /// and these rows, or the panel would show one primary while the
    /// cluster drew another.
    /// </summary>
    static ActionButtonConfig Primary(DataController data) => ActionButtons.Primary(data);

    /// <summary>The chosen seat is the Primary row, not a position in the arc.</summary>
    const int PickPrimary = -2;

    /// <summary>Everything but the primary, in Num order - the cluster's arc.</summary>
    List<ActionButtonConfig> Arc(DataController data)
    {
        _arc.Clear();
        if (data?.ActionButtons == null) return _arc;
        ActionButtonConfig anchor = Primary(data);
        foreach (ActionButtonConfig b in data.ActionButtons)
            if (b != null && b != anchor) _arc.Add(b);
        ActionButtons.Stable(_arc);
        return _arc;
    }

    static int Pages(int count) => Math.Max(1, (count + ActionButtons.HotSeats - 1) / ActionButtons.HotSeats);

    /// <summary>
    /// The config at an arc position, appending empty seats up to it
    /// when it lies past the end. A seat drawn on the last page is a
    /// seat, whether or not a config stands behind it yet.
    /// </summary>
    ActionButtonConfig Ensure(int pos)
    {
        if (_data?.ActionButtons == null || pos < 0) return null;
        List<ActionButtonConfig> arc = Arc(_data);
        while (arc.Count <= pos)
        {
            int next = 0;
            foreach (ActionButtonConfig b in _data.ActionButtons) if (b != null && b.Num >= next) next = b.Num + 1;
            _data.ActionButtons.Add(new ActionButtonConfig(next, ActionButtonType.Unset, ""));
            arc = Arc(_data);
        }
        return arc[pos];
    }

    /// <summary>Every change ends here: saved, the cluster turned to the page, the rows redrawn.</summary>
    void Changed(int pos)
    {
        HotbarStore.Save(_data);
        if (pos >= 0) ActionButtons.WantPage = pos / ActionButtons.HotSeats;
        _signature = "";
        // The card is sized to its pages, so a page added or removed
        // moves its edges.
        Layout();
    }

    void Clear(int pos)
    {
        if (pos < 0) return; // includes PickPrimary: the disc is never cleared
        List<ActionButtonConfig> arc = Arc(_data);
        if (pos >= arc.Count || arc[pos].ButtonType == ActionButtonType.Unset) return;
        // The reference's clear (`UIActionButtons.cpp:471-473`): the
        // cell is emptied and nothing moves.
        arc[pos].SetToUnset();
        Changed(pos);
    }

    /// <summary>
    /// Moves the chosen seat one place, across a page boundary too. Two
    /// Nums change hands and nothing else: not the configs, which the
    /// client is subscribed to, and not the primary, which is not in
    /// the arc and so can never be a party to the swap.
    /// </summary>
    void Swap(int pos, int dir)
    {
        if (pos < 0) return;
        int other = pos + dir;
        if (other < 0) return;
        List<ActionButtonConfig> arc = Arc(_data);
        // Past the last page's last seat there is nowhere to go.
        if (other >= Pages(arc.Count) * ActionButtons.HotSeats) return;
        ActionButtonConfig a = Ensure(Math.Max(pos, other));
        if (a == null) return;
        arc = Arc(_data);
        a = arc[pos]; ActionButtonConfig b = arc[other];
        int n = a.Num; a.Num = b.Num; b.Num = n;
        _picked = other;
        Changed(other);
    }

    /// <summary>Appends empty seats up to one whole page beyond the last.</summary>
    void AddPage()
    {
        List<ActionButtonConfig> arc = Arc(_data);
        int target = (Pages(arc.Count) + 1) * ActionButtons.HotSeats;
        Ensure(target - 1);
        // No turn: a page of rings is not something to come back to.
        Changed(-1);
    }

    /// <summary>Whether the last page holds nothing, so it can go.</summary>
    static bool LastPageEmpty(List<ActionButtonConfig> arc)
    {
        if (Pages(arc.Count) < 2) return false;
        int first = (Pages(arc.Count) - 1) * ActionButtons.HotSeats;
        for (int i = first; i < arc.Count; i++)
            if (arc[i].ButtonType != ActionButtonType.Unset) return false;
        return true;
    }

    void RemovePage()
    {
        List<ActionButtonConfig> arc = Arc(_data);
        if (!LastPageEmpty(arc)) return;
        int first = (Pages(arc.Count) - 1) * ActionButtons.HotSeats;
        var gone = new List<ActionButtonConfig>();
        for (int i = first; i < arc.Count; i++) gone.Add(arc[i]);
        foreach (ActionButtonConfig b in gone) _data.ActionButtons.Remove(b);
        if (_picked >= first) _picked = -1;
        // No turn here either: the cluster clamps itself off a page
        // that has gone (ActionButtons.Sync) and otherwise stays put.
        Changed(-1);
    }

    /// <summary>Writes the chosen seat with the library's setter for what was picked, and comes back to the pages.</summary>
    void Put(object what)
    {
        if (what == null) return;
        bool primary = _picked == PickPrimary;
        ActionButtonConfig slot = primary ? EnsurePrimary() : Ensure(_picked);
        if (slot == null) return;
        // A second copy of what the disc already holds, on the arc, is
        // the same swing twice; the chooser hides it, and this is the
        // same refusal at the writer.
        if (!primary && Same(Primary(_data), what)) return;
        switch (what)
        {
            case SpellObject spell:    slot.SetToSpell(spell); break;
            // The chooser never lists a passive skill, for the reason
            // Bind refuses one; this is the same refusal at the writer.
            case SkillObject skill when !skill.IsActiveSkill: return;
            case SkillObject skill:    slot.SetToSkill(skill); break;
            case InventoryObject item: slot.SetToItem(item);   break;
            case AvatarAction act:     slot.SetToAction(act);  break;
            // Go is not offered for the disc (Door is fixed beside it).
            case ActionButtons.Extra.Go when primary: return;
            case ActionButtons.Extra.Go: ActionButtons.SetToGo(slot); break;
            default: return;
        }
        if (primary)
        {
            // The disc is THIS config from now on, by Num - see
            // ActionButtons.PrimaryNum. Writing the Num even when it is
            // the Attack seat's own costs nothing and means a later
            // Attack bound to the arc does not become a second disc.
            ActionButtons.PrimaryNum = slot.Num;
        }
        _choosing = false;
        Changed(primary ? -1 : _picked);
        Layout();
    }

    /// <summary>
    /// The config the disc is to be written into: the primary where
    /// there is one - rewritten in place, so its Num and the arc stay
    /// put - or a new config at the next free Num where the list holds
    /// no primary at all (Attack dragged off before the lock existed,
    /// or a seeded set a file never held). The new one is kept off the
    /// arc by PrimaryNum, which Put sets right after.
    /// </summary>
    ActionButtonConfig EnsurePrimary()
    {
        if (_data?.ActionButtons == null) return null;
        ActionButtonConfig have = Primary(_data);
        if (have != null) return have;
        int next = 0;
        foreach (ActionButtonConfig b in _data.ActionButtons) if (b != null && b.Num >= next) next = b.Num + 1;
        var made = new ActionButtonConfig(next, ActionButtonType.Unset, "");
        _data.ActionButtons.Add(made);
        return made;
    }

    /// <summary>Whether a chooser pick is the thing a config already holds.</summary>
    static bool Same(ActionButtonConfig cfg, object what)
    {
        if (cfg == null || what == null) return false;
        return what switch
        {
            AvatarAction a => cfg.ButtonType == ActionButtonType.Action && cfg.Data is AvatarAction b && a == b && a != AvatarAction.None,
            SpellObject s => cfg.ButtonType == ActionButtonType.Spell && cfg.Data is SpellObject t && s.ID == t.ID,
            SkillObject s => cfg.ButtonType == ActionButtonType.Skill && cfg.Data is SkillObject t && s.ID == t.ID,
            _ => false,
        };
    }

    // ---- the rows --------------------------------------------------

    /// <summary>Rebuilds when what it shows changes. Cheap to call every frame.</summary>
    public void Sync(DataController data)
    {
        _data = data;
        if (_rows == null || !IsOpen || data?.ActionButtons == null) return;

        var sb = Sig.Start();
        if (_choosing) ChooserSig(sb, data);
        else PagesSig(sb, data);
        // Art that was not readable yet gets another try on a timer -
        // see notes/godot-ui.md, "A polled signature must hold
        // everything the panel draws".
        if (_iconMissed && Time.GetTicksMsec() >= _iconRetryAt) _signature = "";
        if (!Sig.Changed(sb, ref _signature)) return;
        _iconMissed = false;
        _iconRetryAt = Time.GetTicksMsec() + 500;

        // Freed before their replacements arrive - see notes/godot-ui.md,
        // "Free a row before you add its replacement".
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _empty.Visible = false;

        if (_choosing) BuildChooser(data);
        else BuildPages(data);
    }

    void PagesSig(System.Text.StringBuilder sb, DataController data)
    {
        ActionButtonConfig anchor = Primary(data);
        sb.Append("p:").Append(_picked).Append(':').Append(ActionButtons.CurrentPage).Append(';');
        // The primary row draws what the disc holds, so its entry is in
        // full - type, name and whether its art has resolved.
        if (anchor != null) Entry(sb, anchor); else sb.Append("-;");
        foreach (ActionButtonConfig b in Arc(data)) Entry(sb, b);
    }

    /// <summary>One config, with everything its seat draws - including whether its art has resolved.</summary>
    static void Entry(System.Text.StringBuilder sb, ActionButtonConfig b)
    {
        sb.Append(b.Num).Append(':').Append(b.ButtonType).Append(':').Append(b.Name);
        if (b.Data is ObjectBase o)
            sb.Append(':').Append(o.Resource?.Filename).Append(':').Append(o.ColorTranslation).Append(':').Append(o.Effect);
        sb.Append(';');
    }

    void ChooserSig(System.Text.StringBuilder sb, DataController data)
    {
        ActionButtonConfig anchor = Primary(data);
        sb.Append("c:").Append((int)_tab).Append(':').Append(_picked == PickPrimary ? 1 : 0)
          .Append(':').Append(anchor?.Num ?? -1).Append(':').Append(anchor?.ButtonType).Append(':').Append(anchor?.Name).Append(';');
        switch (_tab)
        {
            case Tab.Spells:
                if (data.SpellObjects != null) foreach (SpellObject s in data.SpellObjects) Obj(sb, s);
                break;
            case Tab.Skills:
                if (data.SkillObjects != null) foreach (SkillObject s in data.SkillObjects)
                    if (s.IsActiveSkill) Obj(sb, s);
                break;
            case Tab.Items:
                if (data.InventoryObjects != null) foreach (InventoryObject i in data.InventoryObjects) Obj(sb, i);
                break;
        }
    }

    static void Obj(System.Text.StringBuilder sb, ObjectBase o)
        => sb.Append(o.ID).Append(':').Append(o.Name).Append(':').Append(o.Resource?.Filename)
             .Append(':').Append(o.Count).Append(';');

    void BuildPages(DataController data)
    {
        List<ActionButtonConfig> arc = Arc(data);
        int pages = Pages(arc.Count);

        _rows.AddChild(PrimaryRow(Primary(data)));

        for (int p = 0; p < pages; p++)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", M59Skin.GapI);
            line.CustomMinimumSize = new Vector2(0, SlotSize + 12f);

            // The caption says which page the cluster is on right now,
            // because the arc shows one page at a time and the rows
            // show all of them.
            bool showing = p == ActionButtons.CurrentPage;
            var cap = new Label
            {
                Text = showing ? $"Page {p + 1}\nshowing" : $"Page {p + 1}",
                CustomMinimumSize = new Vector2(PageW, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            cap.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
            cap.AddThemeColorOverride("font_color", showing ? M59Skin.Gold : M59Skin.Text);
            line.AddChild(cap);

            for (int s = 0; s < ActionButtons.HotSeats; s++)
            {
                int pos = p * ActionButtons.HotSeats + s;
                ActionButtonConfig cfg = pos < arc.Count ? arc[pos] : null;
                line.AddChild(Seat(pos, cfg));
            }
            _rows.AddChild(line);
        }

        // The page controls, after the pages they act on.
        var tail = new HBoxContainer();
        tail.AddThemeConstantOverride("separation", M59Skin.GapI);
        tail.CustomMinimumSize = new Vector2(0, M59Skin.RowH);
        var add = new Button { Text = "Add page", Name = "hkAddPage", CustomMinimumSize = new Vector2(PageW, M59Skin.TapMin) };
        M59Skin.Dress(add, M59Skin.Kind.Secondary);
        add.Pressed += AddPage;
        tail.AddChild(add);
        var drop = new Button
        {
            Text = "Remove last page", Name = "hkRemovePage",
            CustomMinimumSize = new Vector2(PageW, M59Skin.TapMin),
            Disabled = !LastPageEmpty(arc),
            TooltipText = "Only an empty last page can go",
        };
        M59Skin.Dress(drop, M59Skin.Kind.Secondary);
        drop.Pressed += RemovePage;
        tail.AddChild(drop);
        var hint = M59Skin.Caption(_picked == -1 ? "Tap a seat, then Set, Clear or move it." : "");
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.ClipText = true;
        tail.AddChild(hint);
        _rows.AddChild(tail);
    }

    /// <summary>
    /// The primary's row: the disc, as a seat that can be picked and
    /// Set. "in the hotkey customizer allow ppl to change their big
    /// attack button to other things" - so the seat is a button like the
    /// arc's, a tap picks it (`hkPrimary`), and Set… opens the chooser
    /// with Attack among the actions. It cannot be cleared or moved -
    /// see Layout - because the disc is never emptied by anything
    /// (ActionButtons.OnUp) and is not a position in the arc. Set
    /// replaces what it holds, in place, by Num (ActionButtons.PrimaryNum).
    ///
    /// Empty - a list with no primary at all - the row still offers
    /// "Set Attack" as the one-tap way back to the game's default,
    /// beside the general Set.
    /// </summary>
    Control PrimaryRow(ActionButtonConfig anchor)
    {
        PanelContainer plate = M59Skin.Plate(false, SlotSize + 12f);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        plate.AddChild(line);

        var cap = new Label
        {
            Text = "Primary",
            CustomMinimumSize = new Vector2(PageW - 14f, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        cap.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(cap);

        bool empty = anchor == null || anchor.ButtonType == ActionButtonType.Unset;
        var seat = new Button
        {
            Name = "hkPrimary",
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ClipText = true,
            ExpandIcon = false,
            IconAlignment = HorizontalAlignment.Center,
        };
        if (!empty)
        {
            // Captioned as the disc captions itself: the picture when
            // there is one, the name when there is not.
            Texture2D icon = Icon(anchor);
            seat.Icon = icon;
            bool alias = anchor.ButtonType == ActionButtonType.Alias;
            seat.Text = icon != null && !alias ? "" : Short(anchor.Name, alias ? 6 : 8);
            if (!string.IsNullOrEmpty(seat.Text) && icon != null) seat.IconAlignment = HorizontalAlignment.Left;
            seat.TooltipText = anchor.Name;
        }
        Dress(seat, _picked == PickPrimary ? SeatLook.Picked : empty ? SeatLook.Empty : SeatLook.Primary);
        seat.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        seat.AddThemeConstantOverride("h_separation", 2);
        seat.Pressed += () => { _picked = _picked == PickPrimary ? -1 : PickPrimary; _signature = ""; Layout(); };
        line.AddChild(seat);

        if (!empty)
        {
            var note = M59Skin.Caption(_picked == PickPrimary
                ? "Set… puts something else on the big button."
                : "The big button, on every page. Tap it, then Set…");
            note.VerticalAlignment = VerticalAlignment.Center;
            note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            note.ClipText = true;
            line.AddChild(note);
        }
        else
        {
            var set = new Button { Text = "Set Attack", Name = "hkSetAttack", CustomMinimumSize = new Vector2(PageW, M59Skin.TapMin) };
            M59Skin.Dress(set, M59Skin.Kind.Primary);
            set.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            set.Pressed += () =>
            {
                _picked = PickPrimary;
                Put(AvatarAction.Attack);
            };
            line.AddChild(set);
        }
        return plate;
    }

    enum SeatLook { Empty, Bound, Picked, Primary }

    /// <summary>
    /// A seat in a row. Named `hk{pos}` by its position in the arc, so a
    /// scripted run reaches the first seat of page two as `hk4` without
    /// knowing what is in it.
    /// </summary>
    Control Seat(int pos, ActionButtonConfig cfg)
    {
        bool empty = cfg == null || cfg.ButtonType == ActionButtonType.Unset;
        var b = new Button
        {
            Name = $"hk{pos}",
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ClipText = true,
            ExpandIcon = false,
            IconAlignment = HorizontalAlignment.Center,
        };
        if (!empty)
        {
            Texture2D icon = Icon(cfg);
            b.Icon = icon;
            // As the cluster captions: the picture when there is one,
            // the name when there is not (every action), the key for an
            // alias beside its shared picture.
            bool alias = cfg.ButtonType == ActionButtonType.Alias;
            b.Text = icon != null && !alias ? "" : Short(cfg.Name, alias ? 6 : 8);
            if (!string.IsNullOrEmpty(b.Text)) b.IconAlignment = HorizontalAlignment.Left;
            b.TooltipText = cfg.Name;
        }
        Dress(b, _picked == pos ? SeatLook.Picked : empty ? SeatLook.Empty : SeatLook.Bound);
        b.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        b.AddThemeConstantOverride("h_separation", 2);
        b.Pressed += () => { _picked = _picked == pos ? -1 : pos; _signature = ""; };
        return b;
    }

    /// <summary>
    /// The seat dress, square: the cluster's cell colours - lit rim and
    /// opaque cell for a binding, a hollow ring at a third of the rim's
    /// strength for an empty seat, gold for the chosen one - on the
    /// panels' rounded square, because a row of circles in a list reads
    /// as a row of radio buttons.
    /// </summary>
    static void Dress(Button b, SeatLook look)
    {
        StyleBoxFlat normal, down;
        switch (look)
        {
            case SeatLook.Primary:
                normal = Box(M59Skin.Gold, new Color(0.286f, 0.231f, 0.129f), 3);
                down = normal;
                b.AddThemeColorOverride("font_color", M59Skin.GoldBright);
                b.AddThemeColorOverride("font_disabled_color", M59Skin.GoldBright);
                break;
            case SeatLook.Picked:
                normal = Box(M59Skin.GoldBright, M59Skin.RowPick, 3);
                down = normal;
                b.AddThemeColorOverride("font_color", M59Skin.GoldBright);
                break;
            case SeatLook.Bound:
                normal = Box(M59Skin.EdgeLit, new Color(0.078f, 0.071f, 0.063f), 2);
                down = Box(M59Skin.Gold, M59Skin.RowPick, 3);
                b.AddThemeColorOverride("font_color", M59Skin.Text);
                break;
            default:
                normal = Box(new Color(M59Skin.Rule.R, M59Skin.Rule.G, M59Skin.Rule.B, 0.45f), new Color(0f, 0f, 0f, 0.25f), 2);
                down = Box(M59Skin.GoldDim, new Color(0f, 0f, 0f, 0.35f), 2);
                b.AddThemeColorOverride("font_color", M59Skin.TextDim);
                b.AddThemeColorOverride("font_disabled_color", M59Skin.TextOff);
                break;
        }
        b.AddThemeColorOverride("font_pressed_color", M59Skin.GoldBright);
        b.AddThemeColorOverride("font_hover_color", M59Skin.Text);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal);
        b.AddThemeStyleboxOverride("focus", normal);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("pressed", down);
    }

    static StyleBoxFlat Box(Color edge, Color fill, int width)
    {
        var s = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)M59Skin.Radius;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = width;
        s.BorderColor = edge;
        return s;
    }

    // ---- the chooser -----------------------------------------------

    /// <summary>The game's own order, from UIActions::Initialize - as ActionsPanel lists them.</summary>
    static readonly AvatarAction[] Actions =
    {
        AvatarAction.Attack, AvatarAction.Rest, AvatarAction.Dance,
        AvatarAction.Wave, AvatarAction.Point, AvatarAction.Loot,
        AvatarAction.Buy, AvatarAction.Inspect, AvatarAction.Trade,
        AvatarAction.Activate, AvatarAction.GuildInvite,
    };

    void BuildChooser(DataController data)
    {
        for (int i = 0; i < 4; i++) M59Skin.Tab(_tabs[i], (int)_tab == i);
        int n = 0;
        switch (_tab)
        {
            case Tab.Actions:
                ActionButtonConfig disc = Primary(data);
                bool forPrimary = _picked == PickPrimary;
                foreach (AvatarAction a in Actions)
                {
                    // Whatever the disc holds is not offered to an arc
                    // seat: a second copy on the arc would be the same
                    // swing twice. For the disc itself everything is
                    // offered, Attack first - it is the game's default
                    // and the way back to it.
                    if (!forPrimary && Same(disc, a)) continue;
                    string label = a == AvatarAction.GuildInvite ? "Guild invite" : a.ToString();
                    _rows.AddChild(Row($"pick{a}", label, null, n++, () => Put(a)));
                }
                // Door is a fixed seat beside the disc, so Go on the disc
                // would be Door twice; it stays an arc option for the
                // player who wants it under a thumb elsewhere.
                if (!forPrimary)
                    _rows.AddChild(Row("pickGo", "Go (through the door)", null, n++, () => Put(ActionButtons.Extra.Go)));
                break;
            case Tab.Spells:
                if (data.SpellObjects != null)
                    foreach (SpellObject s in data.SpellObjects)
                        if (_picked == PickPrimary || !Same(Primary(data), s))
                            _rows.AddChild(Row($"pick{s.ID}", s.Name, s, n++, () => Put(s)));
                if (n == 0) Nothing("No spells known yet.");
                break;
            case Tab.Skills:
                if (data.SkillObjects != null)
                    foreach (SkillObject s in data.SkillObjects)
                        if (s.IsActiveSkill && (_picked == PickPrimary || !Same(Primary(data), s)))
                            _rows.AddChild(Row($"pick{s.ID}", s.Name, s, n++, () => Put(s)));
                if (n == 0) Nothing("No skills you can perform.");
                break;
            case Tab.Items:
                if (data.InventoryObjects != null)
                    foreach (InventoryObject i in data.InventoryObjects)
                        _rows.AddChild(Row($"pick{i.ID}", i.Count > 1 ? $"{i.Name} x{i.Count}" : i.Name, i, n++, () => Put(i)));
                if (n == 0) Nothing("Nothing in your pack.");
                break;
        }
    }

    void Nothing(string text)
    {
        _empty.Text = text;
        _empty.Visible = true;
    }

    /// <summary>A chooser row: a picture when the thing has one, its name, and a tap that writes the seat.</summary>
    Control Row(string name, string text, ObjectBase o, int index, Action pick)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, M59Skin.RowH),
            Name = name,
        };
        M59Skin.Dress(button, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
        button.Pressed += pick;

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 8; line.OffsetTop = 4; line.OffsetRight = -8; line.OffsetBottom = -4;
        button.AddChild(line);

        ImageTexture icon = o != null ? Compose(o, RowIcon) : null;
        // A fixed-width cell whether or not there is a picture, so the
        // names line up down the list.
        var pic = new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(RowIcon, RowIcon),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.AddChild(pic);

        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        label.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(label);
        return button;
    }

    // ---- pictures --------------------------------------------------

    Texture2D Icon(ActionButtonConfig cfg)
    {
        if (cfg.ButtonType == ActionButtonType.Alias) return ActionButtons.AliasIcon();
        if (cfg.Data is not ObjectBase o) return null;
        return Compose(o, SlotIcon);
    }

    /// <summary>
    /// The composed picture, as the cluster composes it. A miss is not
    /// cached and sets the retry flag - see notes/godot-ui.md.
    /// </summary>
    ImageTexture Compose(ObjectBase o, int px)
    {
        if (o?.Resource == null) { _iconMissed = true; return null; }
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}:{px}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;
        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, px)); }
        catch (Exception e) { GD.PrintErr($"[HotKeysPanel] icon: {e.Message}"); }
        if (tex != null) _icons[key] = tex;
        else _iconMissed = true;
        return tex;
    }

    static string Short(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "?";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
