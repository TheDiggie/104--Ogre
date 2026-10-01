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
///   drags the alias onto a hotbar slot (`UIActionButtons.cpp:427-434`),
///   which is a separate feature this client does not have yet.
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
    Label _title;
    Label _note;
    LineEdit _newKey, _newValue;
    Button _add;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    Config _config;

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>
    /// Raised when the panel closes, so the view can put the Settings
    /// window back where the player left it - this page is reached from
    /// there, the way the reference's category button is.
    /// </summary>
    public event Action Closed;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Aliases", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        // The two boxes and the Add button, which are the reference's
        // AddKey, AddValue and Add (`UIOptions.cpp:169-171`).
        _newKey = new LineEdit { PlaceholderText = "alias", Visible = false, Name = "aliasNewKey" };
        _newKey.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_newKey);

        _newValue = new LineEdit { PlaceholderText = "command it stands for", Visible = false, Name = "aliasNewValue" };
        _newValue.AddThemeFontSizeOverride("font_size", FontSize);
        // Enter in either box adds, because reaching a button with a
        // thumb while a keyboard is up is the slow way round.
        _newValue.TextSubmitted += _ => Add();
        _newKey.TextSubmitted += _ => Add();
        AddChild(_newValue);

        _add = new Button { Text = "Add", Visible = false, Name = "aliasAdd" };
        _add.AddThemeFontSizeOverride("font_size", FontSize);
        _add.Pressed += Add;
        AddChild(_add);

        _note = new Label { Visible = false };
        _note.AddThemeFontSizeOverride("font_size", FontSize - 3);
        _note.AddThemeColorOverride("font_color", new Color(0.95f, 0.65f, 0.55f));
        AddChild(_note);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _close = new Button { Text = "Close", Visible = false, Name = "aliasClose" };
        _close.AddThemeFontSizeOverride("font_size", FontSize);
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
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 800f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 2f;

        // Key gets a third, value the rest, Add a thumb's width. On a
        // narrow screen a key box any smaller stops showing the word
        // that is in it.
        float addW = Mathf.Min(96f, w * 0.22f);
        float keyW = Mathf.Max(90f, (w - addW - 16f) * 0.34f);
        _newKey.Position = new Vector2(side, y);
        _newKey.Size = new Vector2(keyW, rowH);
        _newValue.Position = new Vector2(side + keyW + 8f, y);
        _newValue.Size = new Vector2(w - keyW - addW - 16f, rowH);
        _add.Position = new Vector2(side + w - addW, y);
        _add.Size = new Vector2(addW, rowH);
        y += rowH + 4f;

        _note.Position = new Vector2(side, y);
        _note.Size = new Vector2(w, FontSize * 1.4f);
        y += FontSize * 1.6f;

        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, Mathf.Max(rowH, top + height - rowH - 24f - y));
        _rows.CustomMinimumSize = new Vector2(w, 0);

        _close.Position = new Vector2(side, top + height - rowH - 12f);
        _close.Size = new Vector2(w, rowH);
    }

    void Build()
    {
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _note.Text = "";
        if (_config?.Aliases == null) return;

        // Index order is alphabetical by key, because the list keeps
        // itself that way (`Config.cs:345-346`) - so this walk IS the
        // sorted display the reference's list box shows.
        for (int i = 0; i < _config.Aliases.Count; i++)
            _rows.AddChild(Row(i));

        _title.Text = $"Aliases ({_config.Aliases.Count})";
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

        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        line.AddThemeConstantOverride("separation", 6);

        var key = new LineEdit
        {
            Text = alias.Key ?? "",
            CustomMinimumSize = new Vector2(Mathf.Max(90f, _rows.Size.X * 0.3f), 0),
            Name = $"aliasKey{index}",
        };
        key.AddThemeFontSizeOverride("font_size", FontSize);
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
        value.AddThemeFontSizeOverride("font_size", FontSize);
        value.TextSubmitted += _ => CommitValue(index, value);
        value.FocusExited += () => CommitValue(index, value);
        line.AddChild(value);

        var del = new Button
        {
            Text = "x",
            CustomMinimumSize = new Vector2(52, 0),
            Name = $"aliasDelete{index}",
        };
        del.AddThemeFontSizeOverride("font_size", FontSize);
        del.Pressed += () => Delete(index);
        line.AddChild(del);

        return line;
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
