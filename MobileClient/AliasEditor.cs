using System;
using Godot;
using Meridian59.Common;
using Meridian59.Data.Models;

/// <summary>
/// The alias editor.
///
/// WHAT THE REFERENCE OFFERS, AND WHAT IT DOES NOT
///
/// In the Ogre client this is a page of the Options window, reached by a
/// category button next to the others (`UIOptions.cpp:18`, :623, :1241)
/// and drawn on the TabAliases page (`:27`, :167-171). The page holds
/// exactly four things: a list of the aliases, a key box, a value box
/// and an Add button.
///
///   ADD - `OnAliasAddClicked` (`UIOptions.cpp:2392-2418`). Both boxes
///   are trimmed; if either is empty nothing happens, silently. If the
///   key already exists nothing happens, silently. Otherwise the pair
///   is added to Config.Aliases.
///
///   EDIT - each row IS two edit boxes (`:1072-1073`, :1085-1086), and
///   the model is written on EventTextAccepted and again on
///   EventDeactivated (`:1095-1099`) - so committing means pressing
///   Enter or simply leaving the box. Key and value are both editable
///   in place, with no validation whatsoever on the way in.
///
///   DELETE - a button per row, `OnAliasDeleteClicked`
///   (`:2420-2432`). It removes the entry at that index straight away.
///   There is no confirmation, and none is added here: a prompt over a
///   two-word row is heavier than the thing it guards, and the row can
///   be typed back in ten seconds.
///
///   REORDER - there is none, and there cannot be. The list sorts
///   itself by key (`Config.cs:345-346`), so an alias sits where its
///   name puts it. The row does carry a DragContainer
///   (`:1070`, Constants.h:923), but that is not for ordering - it
///   drags the alias onto a hotbar slot (`UIActionButtons.cpp:427-434`).
///
///   HOTBAR - that drag, which is the reason the DragContainer is there.
///   Its handle is an icon showing UI_IMAGE_ALIAS_ICON (`:1076-1082`)
///   with the tooltip "Drag&amp;Drop me on the Button Grid!", and letting
///   go over a slot calls `SetToAlias` on that slot's model
///   (`UIActionButtons.cpp:427-434`). A phone cannot do the gesture -
///   this panel covers the hotbar it would be dropped onto - so the
///   handle becomes a button that binds, which is the same stand-in
///   SpellsPanel and ActionsPanel already make for the same drag. It
///   keeps the reference's icon and the reference's place at the head of
///   the row.
///
/// WHAT THIS ONE DOES DIFFERENTLY
///
/// Two silent refusals are two too many on a phone, where the boxes are
/// small and a mistyped key is normal. The reference's rules are kept
/// exactly - empty is refused, a duplicate key is refused - but each one
/// says so in a line under the boxes. Nothing is a system dialog; this
/// is the client's own UI, as everything here is.
///
/// The one rule added is that a key or value may not be emptied by
/// editing, where the reference would happily store a blank. A blank
/// cannot be matched or persisted, so it is silently put back rather
/// than written.
/// </summary>
public partial class AliasEditor : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 46;

    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title;
    Label _note;
    Label _formCaption;
    ColorRect _rule;
    Label _empty;
    LineEdit _newKey, _newValue;
    Button _add;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    Config _config;

    /// <summary>
    /// Every row's key box, so Layout can hold them all to the same
    /// column as the form's. A row is built before the panel has been
    /// laid out, so it cannot read the width itself - and a row whose
    /// key box is its own width puts the command box somewhere new on
    /// every line.
    /// </summary>
    readonly System.Collections.Generic.List<LineEdit> _keyBoxes =
        new System.Collections.Generic.List<LineEdit>();

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>
    /// Raised when the panel closes, so the view can put the Settings
    /// window back where the player left it - this page is reached from
    /// there, the way the reference's category button is.
    /// </summary>
    public event Action Closed;

    /// <summary>
    /// Put this alias on the hotbar - the row's stand-in for dragging it
    /// onto a slot (`UIOptions.cpp:1070`, dropped at
    /// `UIActionButtons.cpp:427-434`). The pair itself is handed over,
    /// not its key: `SetToAlias` keeps the object, so the button follows
    /// later edits to the alias rather than a snapshot of it.
    /// </summary>
    public event Action<KeyValuePairString> Assign;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim eats the touch that would reach whatever is
        // behind, and is what makes the card read as a window rather
        // than as the screen.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Aliases");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        // The form at the top of the body says what it is, because two
        // empty boxes over a list of filled ones is a row of the list
        // that happens to be blank.
        _formCaption = M59Skin.Caption("New alias");
        _formCaption.Visible = false;
        AddChild(_formCaption);

        // The two boxes and the Add button, which are the reference's
        // AddKey, AddValue and Add (`UIOptions.cpp:169-171`).
        _newKey = new LineEdit { PlaceholderText = "alias", Visible = false, Name = "aliasNewKey" };
        M59Skin.Field(_newKey);
        AddChild(_newKey);

        _newValue = new LineEdit { PlaceholderText = "command it stands for", Visible = false, Name = "aliasNewValue" };
        M59Skin.Field(_newValue);
        // Enter in either box adds, because reaching a button with a
        // thumb while a keyboard is up is the slow way round.
        _newValue.TextSubmitted += _ => Add();
        _newKey.TextSubmitted += _ => Add();
        AddChild(_newValue);

        // The one thing this panel is for, so it is the one Primary on
        // it; Close is a footer button like any other.
        _add = new Button { Text = "Add", Visible = false, Name = "aliasAdd" };
        M59Skin.Dress(_add, M59Skin.Kind.Primary);
        _add.Pressed += Add;
        AddChild(_add);

        _note = new Label { Visible = false };
        _note.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        _note.AddThemeColorOverride("font_color", new Color(0.95f, 0.65f, 0.55f));
        AddChild(_note);

        // A hairline between the form and the list: they are two
        // things, and the gap alone was not saying so.
        _rule = M59Skin.Hairline();
        _rule.Visible = false;
        AddChild(_rule);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Or the list is only as wide as its longest command and the
        // delete buttons land in a ragged column - see notes/godot-ui.md.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _empty = M59Skin.Empty("No aliases yet. A word above, the command it stands for beside it.");
        _empty.Visible = false;
        AddChild(_empty);

        _close = new Button { Text = "Close", Visible = false, Name = "aliasClose" };
        M59Skin.Dress(_close, M59Skin.Kind.Secondary);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>
    /// The list this edits is the library's own, on the config object
    /// (`Config.cs:307`) - the same one `ChatCommand.Parse` reads when a
    /// line is typed. Nothing is copied.
    /// </summary>
    public void Follow(Config config) => _config = config;

    public void Open() { Build(); Show(true); }

    public void Close()
    {
        Show(false);
        Closed?.Invoke();
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _close.Visible = on; _note.Visible = on;
        _newKey.Visible = on; _newValue.Visible = on; _add.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _formCaption.Visible = on; _rule.Visible = on;
        _empty.Visible = on && (_config?.Aliases == null || _config.Aliases.Count == 0);
        Layout();
    }

    /// <summary>Height of a form box and of a list row.</summary>
    const float FieldH = 46f;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        float rowH = Mathf.Max(RowHeight, M59Skin.RowH);
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        // The form, the note, the rule and the list: the card is as
        // tall as those come to, within the screen.
        float formH = M59Skin.SmallSize + 6f + FieldH + M59Skin.Gap
                    + M59Skin.SmallSize + 6f + M59Skin.Gap + 1f + M59Skin.Gap;
        // Capped: a two-word alias and the command it stands for do not
        // want the whole width of a sideways phone between them.
        Rect2 card = M59Skin.Frame(v, formH + shown * (rowH + 4f), true, 1100f);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position; _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(34, 34);
        _x.Position = new Vector2(card.Position.X + card.Size.X - 34f - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - 34f) * 0.5f);

        float w = body.Size.X, x = body.Position.X, y = body.Position.Y;

        _formCaption.Position = new Vector2(x, y);
        _formCaption.Size = new Vector2(w, M59Skin.SmallSize + 6f);
        y += M59Skin.SmallSize + 6f;

        // Key gets the same fixed column the rows use, so the form sits
        // directly over the list it adds to; Add takes a thumb's width
        // at the right, where the row's delete is.
        float addW = Mathf.Min(120f, w * 0.22f);
        float keyW = KeyW(w);
        foreach (LineEdit k in _keyBoxes)
            if (GodotObject.IsInstanceValid(k)) k.CustomMinimumSize = new Vector2(keyW, 0);
        // Indented by the bind column the rows carry, so the two boxes
        // of the form sit directly over the two boxes of every row.
        float fx = x + M59Skin.Gap + BindW + M59Skin.Gap;
        _newKey.Position = new Vector2(fx, y);
        _newKey.Size = new Vector2(keyW, FieldH);
        _newValue.Position = new Vector2(fx + keyW + M59Skin.Gap, y);
        _newValue.Size = new Vector2(x + w - addW - M59Skin.Gap - (fx + keyW + M59Skin.Gap), FieldH);
        _add.Position = new Vector2(x + w - addW, y);
        _add.Size = new Vector2(addW, FieldH);
        y += FieldH + M59Skin.Gap;

        _note.Position = new Vector2(x, y);
        _note.Size = new Vector2(w, M59Skin.SmallSize + 6f);
        y += M59Skin.SmallSize + 6f + M59Skin.Gap;

        _rule.Position = new Vector2(x, y);
        _rule.Size = new Vector2(w, 1f);
        y += 1f + M59Skin.Gap;

        _scroll.Position = new Vector2(x, y);
        _scroll.Size = new Vector2(w, Mathf.Max(rowH, body.Position.Y + body.Size.Y - y));
        _rows.CustomMinimumSize = new Vector2(w, 0);

        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        M59Skin.FootRow(foot, _close);
    }

    /// <summary>
    /// The key column, shared by the form and every row so the two
    /// boxes of a row sit under the two boxes that add one.
    /// </summary>
    static float KeyW(float w) => Mathf.Clamp(w * 0.26f, 120f, 280f);

    /// <summary>The bind column at the head of every row.</summary>
    const float BindW = 48f;

    void Build()
    {
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _keyBoxes.Clear();
        _note.Text = "";
        if (_config?.Aliases == null) return;

        // Index order is alphabetical by key, because the list keeps
        // itself that way (`Config.cs:345-346`) - so this walk IS the
        // sorted display the reference's list box shows.
        for (int i = 0; i < _config.Aliases.Count; i++)
            _rows.AddChild(Row(i));

        _title.Text = $"Aliases ({_config.Aliases.Count})";
        _empty.Visible = IsOpen && _config.Aliases.Count == 0;

        // The card is sized to the list, so a list that just changed
        // length needs the frame measured again.
        Layout();
    }

    /// <summary>
    /// One row: the key, the value, and a delete. The reference's row is
    /// the same three things (`UIOptions.cpp:1071-1073`).
    ///
    /// The index is captured rather than looked up, which is safe for
    /// exactly as long as the row lives: every add and every delete
    /// rebuilds the list, so no row outlives its own index.
    /// </summary>
    Control Row(int index)
    {
        KeyValuePairString alias = _config.Aliases[index];
        bool alt = index % 2 == 1;

        // The row is a striped panel with the controls laid on it,
        // rather than three bare widgets in a line: alternating stripes
        // are what let the eye follow one alias across to its delete.
        var back = new Panel
        {
            CustomMinimumSize = new Vector2(0, Mathf.Max(RowHeight, M59Skin.RowH)),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        back.AddThemeStyleboxOverride("panel", M59Skin.Stripe(alt));

        var line = new HBoxContainer();
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        line.OffsetLeft = M59Skin.Gap; line.OffsetRight = -M59Skin.Gap;
        line.OffsetTop = 5; line.OffsetBottom = -5;
        back.AddChild(line);

        // First in the row, where the reference puts its drag handle
        // (`UI_OPTIONS_CHILDINDEX_ALIAS_DRAG` is child 0, `:1070`), and
        // carrying the same picture it does (`:1076-1082`). The tooltip
        // says what the reference's says, minus the gesture: there is
        // nothing to drag.
        var bind = new Button
        {
            Icon = ActionButtons.AliasIcon(),
            // Only when the icon failed to load - see AliasIcon. A blank
            // square would be a button nobody presses.
            Text = ActionButtons.AliasIcon() == null ? "+" : "",
            ExpandIcon = false,
            TooltipText = "Put on the hotbar",
            CustomMinimumSize = new Vector2(BindW, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Name = $"aliasBind{index}",
        };
        // A small square stepper, like the spell book's bind: it is one
        // of three things in the row that takes its own press, and the
        // row itself is not pressable.
        M59Skin.Dress(bind, M59Skin.Kind.Step);
        bind.CustomMinimumSize = new Vector2(BindW, M59Skin.RowH - 16f);
        bind.Pressed += () => Hotbar(index);
        line.AddChild(bind);

        var key = new LineEdit
        {
            Text = alias.Key ?? "",
            // The same column the form's key box takes, so the list
            // reads as a table rather than as rows of their own widths.
            Name = $"aliasKey{index}",
        };
        M59Skin.Field(key);
        _keyBoxes.Add(key);
        // Enter or leaving the box commits, which is EventTextAccepted
        // and EventDeactivated on the reference's boxes (`:1095-1099`).
        key.TextSubmitted += _ => CommitKey(index, key);
        key.FocusExited += () => CommitKey(index, key);
        line.AddChild(key);

        var value = new LineEdit
        {
            Text = alias.Value ?? "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = $"aliasValue{index}",
        };
        M59Skin.Field(value);
        value.TextSubmitted += _ => CommitValue(index, value);
        value.FocusExited += () => CommitValue(index, value);
        line.AddChild(value);

        var del = new Button
        {
            Text = "✕",
            TooltipText = "Remove this alias",
            CustomMinimumSize = new Vector2(48, M59Skin.RowH - 16f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Name = $"aliasDelete{index}",
        };
        // It destroys something, and there is no confirmation - see the
        // class note - so it says so in its colour.
        M59Skin.Dress(del, M59Skin.Kind.Danger);
        del.Pressed += () => Delete(index);
        line.AddChild(del);

        return back;
    }

    /// <summary>
    /// The reference's Add, rule for rule (`UIOptions.cpp:2397-2417`):
    /// both sides trimmed, either side empty refuses, an existing key
    /// refuses. The only difference is that the refusal is said out loud.
    /// </summary>
    void Add()
    {
        if (_config?.Aliases == null) return;

        string key = (_newKey.Text ?? "").Trim();
        string val = (_newValue.Text ?? "").Trim();

        if (key.Length == 0 || val.Length == 0)
        {
            _note.Text = "An alias needs both a word and a command.";
            return;
        }

        // `GetIndexByKey` is the reference's own check, and it compares
        // with == (`KeyValuePairStringList.cs:38-44`) - so a key that
        // differs only in case is a different key, here as there.
        if (_config.Aliases.GetIndexByKey(key) != -1)
        {
            _note.Text = $"\"{key}\" is already an alias.";
            return;
        }

        _config.Aliases.Add(new KeyValuePairString(key, val));
        AliasStore.Save(_config);

        _newKey.Text = "";
        _newValue.Text = "";
        Build();
        // Build cleared the line; say what happened rather than nothing.
        _note.Text = $"\"{key}\" added.";
    }

    /// <summary>
    /// Writes an edited key back (`UIOptions.cpp:2434-2448`). Emptying
    /// it is refused rather than stored - see the class note.
    /// </summary>
    void CommitKey(int index, LineEdit box)
    {
        if (_config?.Aliases == null || index >= _config.Aliases.Count) return;
        KeyValuePairString alias = _config.Aliases[index];

        string key = (box.Text ?? "").Trim();
        if (key.Length == 0) { box.Text = alias.Key ?? ""; return; }
        if (key == alias.Key) return;

        // A rename onto a name that exists would give two entries the
        // same key, and `GetItemByKey` returns the first (`:47-53`) -
        // so the second would be unreachable and impossible to tell
        // apart on screen. The reference does not check; this does,
        // because there it is one keystroke away from being noticed and
        // here the row is half a thumb wide.
        if (_config.Aliases.GetIndexByKey(key) != -1)
        {
            box.Text = alias.Key ?? "";
            _note.Text = $"\"{key}\" is already an alias.";
            return;
        }

        alias.Key = key;
        AliasStore.Save(_config);
        _note.Text = "";
    }

    /// <summary>
    /// Writes an edited value back (`UIOptions.cpp:2450-2463`).
    /// </summary>
    void CommitValue(int index, LineEdit box)
    {
        if (_config?.Aliases == null || index >= _config.Aliases.Count) return;
        KeyValuePairString alias = _config.Aliases[index];

        string val = (box.Text ?? "").Trim();
        if (val.Length == 0) { box.Text = alias.Value ?? ""; return; }
        if (val == alias.Value) return;

        alias.Value = val;
        AliasStore.Save(_config);
        _note.Text = "";
    }

    /// <summary>
    /// Hands the row's alias to whoever is listening, which in practice
    /// is the view calling `ActionButtons.Bind` - the drop handler's job
    /// in the reference (`UIActionButtons.cpp:427-434`).
    ///
    /// The index is looked up against the list at the moment of the
    /// press rather than trusted, for the same reason the commits above
    /// guard it: a row lives exactly as long as its index, and a stale
    /// one must bind nothing rather than bind the wrong alias.
    ///
    /// Nothing is saved here. The binding belongs to the hotbar, and
    /// HotbarStore writes it; the alias itself has not changed.
    /// </summary>
    void Hotbar(int index)
    {
        if (_config?.Aliases == null || index < 0 || index >= _config.Aliases.Count) return;

        KeyValuePairString alias = _config.Aliases[index];
        if (alias == null) return;

        Assign?.Invoke(alias);
        // Said here rather than left to the chat line, because the chat
        // is behind this panel while it is open.
        _note.Text = $"\"{alias.Key}\" put on the hotbar.";
    }

    /// <summary>
    /// Removes the row's entry, which is all the reference's delete does
    /// (`UIOptions.cpp:2429`).
    /// </summary>
    void Delete(int index)
    {
        if (_config?.Aliases == null || index >= _config.Aliases.Count) return;

        string key = _config.Aliases[index].Key;
        _config.Aliases.RemoveAt(index);
        AliasStore.Save(_config);
        Build();
        _note.Text = $"\"{key}\" removed.";
    }
}
