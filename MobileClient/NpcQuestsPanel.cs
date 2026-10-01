using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// What an NPC has for you: the quest offer window.
///
/// `UINPCQuestList.cpp`. The Quest button on the target row sends
/// `SendReqNPCQuestsMessage`, the server answers with a QuestUIList, and
/// `DataController.HandleQuestUIList` fills `Data.QuestUIInfo` and sets
/// `IsVisible`. So this window, like the shop, is the server's decision
/// and not the view's - it appears when the data says so and the view
/// never opens it by itself.
///
/// Four things in that file are worth copying rather than inventing:
///
///  - the list order is already decided. The data layer adds active
///    quests first, then valid ones, then the rest, so the list is
///    walked in order and nothing is sorted here.
///  - a row's colour is `QuestTypeColors.GetColorFor(Flags)`: green for
///    a quest you are on, yellow for one you could take, white for one
///    you cannot.
///  - the Accept button changes with the selected row. On an active
///    quest it reads Continue and the second field is headed
///    Instructions rather than Requirements; on an invalid one it is
///    disabled, because there is nothing to accept.
///  - accepting sends `SendReqTriggerQuestMessage(questGiver, quest)` -
///    both ids, not the quest alone - and then hides and clears the
///    window locally rather than waiting for the server to.
///
/// The one thing left out is the right-click toggle between the styled
/// text and the plain copyable text. That exists so you can select and
/// copy a quest description with a mouse; there is no right button on a
/// phone and no selection to make, so the styled text is all there is.
/// </summary>
public partial class NpcQuestsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 32;
    [Export] public int RowHeight = 44;

    /// <summary>Take (or continue) this quest, from this giver.</summary>
    public event Action<uint, uint> Accept;
    /// <summary>The window was dismissed; clear the data layer.</summary>
    public event Action Dismissed;

    ColorRect _panel;
    Label _title, _who, _descLabel, _reqLabel;
    RichTextLabel _desc, _req;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _accept, _close, _help;
    TextureRect _portrait;

    /// <summary>Show this text to the player. Raised by Help.</summary>
    public event System.Action<string> Helped;

    /// <summary>
    /// The quest window's help, EN_NPCQUESTUI[7] (Language.cpp:109-115),
    /// less its last paragraph: that one explains right-clicking a box
    /// to copy out of it, which this client has no equivalent of.
    /// </summary>
    const string HelpText =
        "Click on a quest in the quest list to view its description.\n\n" +
        "If you meet the requirements to start a quest, it will be shown in " +
        "yellow in the quest list. Currently active Quests are shown in green " +
        "if this NPC is the destination for the quest. Quests are shown in " +
        "white if you do not meet all the requirements.\n\n" +
        "Requirements are shown under the description, with met requirements " +
        "shown in green and unmet ones in red.\n\n" +
        "Tap 'Accept'/'Continue' to start a new quest or progress an existing " +
        "one. Completing a Quest where the NPC requires an item will give that " +
        "item to the NPC. If you have multiple copies of an item, the last one " +
        "in your inventory will be given.\n\n" +
        "Check your own Quest Log for information on current and completed quests.";

    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    readonly List<QuestObjectInfo> _quests = new List<QuestObjectInfo>();
    readonly List<Button> _buttons = new List<Button>();
    string _signature = "";
    int _picked = -1;
    // The quest being read, by its object id: the list is rebuilt from
    // scratch whenever the server re-sends it, and a row index would
    // land on a different quest (or none) after that.
    long _pickedId = -1;
    // The portrait is composed once per giver, not once per rebuild.
    uint _portraitFor = uint.MaxValue;
    uint _giver;

    /// <summary>Side of the quest giver's picture.</summary>
    [Export] public int PortraitSize = 96;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Quests", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _who = Heading("", FontSize, new Color(0.75f, 0.78f, 0.84f));
        // The quest giver's picture. The reference composes one and sets
        // it as the window image (`UINPCQuestList.cpp:33-42`, `:98-104`,
        // `:115-118`); it is the only thing that window shows of the NPC.
        _portrait = new TextureRect
        {
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Name = "giverPortrait",
        };
        AddChild(_portrait);
        _descLabel = Heading("Description", FontSize, new Color(1, 0.86f, 0.45f));
        _reqLabel = Heading("Requirements", FontSize, new Color(1, 0.86f, 0.45f));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _desc = Body();
        _req = Body();

        _accept = Push("Accept", () =>
        {
            if (_picked < 0 || _picked >= _quests.Count) return;
            uint id = _quests[_picked].ObjectBase.ID;
            Accept?.Invoke(_giver, id);
            Dismiss();
        });
        // The game has a Help button on this window and it is the only
        // place a new player is told how quests read
        // (`UINPCQuestList.cpp:353-360`). The text is the client's own,
        // not the server's - EN_NPCQUESTUI[7] in Language.cpp:109-115 -
        // so it is carried here word for word rather than paraphrased,
        // less the last paragraph about right-clicking a text box to
        // copy from it, which is a thing this client does not do.
        _help = Push("Help", () => Helped?.Invoke(HelpText));
        _close = Push("Close", Dismiss);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    RichTextLabel Body()
    {
        var r = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        r.AddThemeFontSizeOverride("normal_font_size", FontSize);
        AddChild(r);
        return r;
    }

    Button Push(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 760f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        // The picture on the left, the title and the giver's name beside it.
        float pic = PortraitSize;
        _portrait.Position = new Vector2(side, y);
        _portrait.Size = new Vector2(pic, pic);
        float tx = side + pic + 12f;
        float headTop = y;
        _title.Position = new Vector2(tx, y); y += FontSize * 1.6f;
        _who.Position = new Vector2(tx, y); y += FontSize * 1.8f;
        y = Mathf.Max(y, headTop + pic + 6f);

        // The list takes a third of what is left; the two text blocks
        // share the rest. A phone cannot show the game's side-by-side
        // layout, so it is stacked.
        float rest = top + height - rowH - 8f - y;
        float listH = Mathf.Max(rowH * 2f, rest * 0.34f);
        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, listH);
        _rows.CustomMinimumSize = new Vector2(w, 0);
        y += listH + 8f;

        float textH = (rest - listH - 8f - FontSize * 3.2f) * 0.5f;
        _descLabel.Position = new Vector2(side, y); y += FontSize * 1.6f;
        _desc.Position = new Vector2(side, y);
        _desc.Size = new Vector2(w, textH); y += textH + 4f;
        _reqLabel.Position = new Vector2(side, y); y += FontSize * 1.6f;
        _req.Position = new Vector2(side, y);
        _req.Size = new Vector2(w, textH);

        float by = top + height - rowH;
        Button[] row = { _accept, _help, _close };
        float bw = (w - 8f) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(side + i * (bw + 8f), by);
            row[i].Size = new Vector2(bw, rowH);
        }
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _who.Visible = on; _portrait.Visible = on;
        _scroll.Visible = on; _descLabel.Visible = on; _desc.Visible = on;
        _reqLabel.Visible = on; _req.Visible = on;
        _accept.Visible = on; _help.Visible = on; _close.Visible = on;
        if (on) GetParent()?.MoveChild(this, -1);
    }

    void Dismiss()
    {
        Show(false);
        _signature = "";
        _picked = -1; _pickedId = -1;
        Dismissed?.Invoke();
    }

    /// <summary>
    /// Follows the NPC's offer. `IsVisible` is the server's switch.
    /// </summary>
    public void Sync(QuestUIInfo info)
    {
        if (_rows == null) return;

        // An empty list is NOT "no window". The reference shows the window
        // on IsVisible alone (`UINPCQuestList.cpp:107-112`), the data layer
        // sets IsVisible after the sort with no count test
        // (`DataController.cs:3034-3056`), and the wire admits a zero-length
        // list (`QuestUIListMessage.cs:71-79`). So an NPC with nothing to
        // offer opens an empty window with his picture and name - and
        // hiding it here instead left IsVisible true in the data layer
        // (only Dismissed clears it) while the Quest button did nothing.
        if (info == null || !info.IsVisible || info.QuestList == null)
        {
            if (IsOpen) { Show(false); _signature = ""; _picked = -1; _pickedId = -1; }
            return;
        }

        if (!IsOpen) Show(true);

        var sb = new System.Text.StringBuilder();
        sb.Append(info.QuestGiver?.ID).Append('|');
        foreach (QuestObjectInfo q in info.QuestList)
            sb.Append(q?.ObjectBase?.ID).Append(':')
              .Append((int)(q?.ObjectBase?.Flags?.Player ?? 0)).Append(':')
              // The text too: a quest offered again with the same id and
              // flags but new words would otherwise keep the old ones on
              // screen. The reference clears and re-adds the whole list
              // on every QuestUIList (`DataController.cs:3040-3052`).
              .Append(q?.Description?.FullString).Append('\u0001')
              .Append(q?.Requirements?.FullString).Append('\u0002').Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _giver = info.QuestGiver != null ? info.QuestGiver.ID : 0u;
        _who.Text = info.QuestGiver != null && !string.IsNullOrWhiteSpace(info.QuestGiver.Name)
            ? info.QuestGiver.Name : "";

        if (_portraitFor != _giver)
        {
            _portraitFor = _giver;
            _portrait.Texture = Portrait(info.QuestGiver);
        }

        _quests.Clear();
        _buttons.Clear();
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        foreach (QuestObjectInfo q in info.QuestList)
        {
            if (q?.ObjectBase == null) continue;
            _quests.Add(q);
            Button b = Row(q, _quests.Count - 1);
            _buttons.Add(b);
            _rows.AddChild(b);
        }

        _title.Text = $"Quests ({_quests.Count})";

        // The game selects row 0 only when the list goes from empty to
        // one item (`UINPCQuestList.cpp:165-166`). Here a rebuild is
        // not an empty list, it is the same list with something
        // changed, so the quest being read stays selected when it is
        // still offered, and the first row is the fallback - which is
        // also the first open, when nothing was selected.
        int keep = 0;
        for (int i = 0; i < _quests.Count; i++)
            if (_quests[i].ObjectBase.ID == _pickedId) { keep = i; break; }
        Pick(_quests.Count > 0 ? keep : -1);
        // Nothing selected, nothing to read: say so, rather than leave two
        // blank boxes that look like a window that failed to load. (The
        // reference leaves the boxes as they were; this wording is the
        // client's own.)
        if (_quests.Count == 0) _desc.Text = "This person has no quests to offer right now.";
    }

    Button Row(QuestObjectInfo q, int index)
    {
        uint argb = QuestTypeColors.GetColorFor(q.ObjectBase.Flags);

        var b = new Button
        {
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Flat = true,
            Icon = Icon(q.ObjectBase),
            Text = "  " + (string.IsNullOrWhiteSpace(q.ObjectBase.Name) ? "(unnamed)" : q.ObjectBase.Name),
            // Named by position so the screenshot harness can pick a row
            // it cannot find by text.
            Name = $"row{index}",
        };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
        b.Pressed += () => Pick(index);
        return b;
    }

    /// <summary>
    /// `SetQuestText`: the selected row fills the two text blocks and
    /// decides what the Accept button says and whether it works.
    /// </summary>
    void Pick(int index)
    {
        _picked = index;
        _pickedId = index >= 0 && index < _quests.Count ? _quests[index].ObjectBase.ID : -1;

        for (int i = 0; i < _buttons.Count; i++)
            _buttons[i].Flat = i != index;

        if (index < 0 || index >= _quests.Count)
        {
            _desc.Text = ""; _req.Text = "";
            _title.Text = $"Quests ({_quests.Count})";
            _accept.Disabled = true;
            return;
        }

        QuestObjectInfo q = _quests[index];
        // The window is titled with the quest you are reading, as the
        // reference titles it (`UINPCQuestList.cpp:251`). "Quests (n)"
        // is what it says while nothing is picked.
        _title.Text = string.IsNullOrWhiteSpace(q.ObjectBase?.Name)
            ? $"Quests ({_quests.Count})" : q.ObjectBase.Name;
        // The two fields are not drawn alike, and that is the file's
        // choice rather than an oversight: `SetQuestText` sets the
        // description from `FullString` - plain, styling stripped,
        // "shouldn't be any in a description" - and the requirements
        // through `Util::GetChatString`, because the server colours the
        // met requirements green and the unmet ones red, and that
        // colouring is the whole information.
        // Plain, but into a markup box: a stray bracket in the server's
        // own text would otherwise be read as a tag and swallow the rest
        // of the line.
        string plain = q.Description != null ? q.Description.FullString : "";
        _desc.Text = plain != null ? plain.Replace("[", "[lb]") : "";
        _req.Text = q.Requirements != null ? (ChatOverlay.Markup(q.Requirements) ?? "") : "";

        ObjectFlags.PlayerType kind = q.ObjectBase.Flags.Player;
        if (kind == ObjectFlags.PlayerType.QuestActive)
        {
            _reqLabel.Text = "Instructions";
            _accept.Text = "Continue";
            _accept.Disabled = false;
        }
        else
        {
            _reqLabel.Text = "Requirements";
            _accept.Text = "Accept";
            _accept.Disabled = kind == ObjectFlags.PlayerType.QuestInvalid;
        }
    }

    /// <summary>
    /// The giver's picture, composed as `UINPCQuestList.cpp:33-42` does:
    /// viewer frame, no Y offset, no power-of-two padding, centred both
    /// ways in a box. The giver arrives as a plain ObjectBase (`new
    /// ObjectBase(true, ...)`, QuestUIListMessage.cs:68), and the
    /// ObjectBase route of <see cref="M59Compose.Icon"/> composes with
    /// the viewer frame unconditionally (RenderInfo.cs:217), which
    /// is exactly that. Nothing is cached across givers: a giver's art
    /// is one picture, composed once per change of giver.
    /// </summary>
    ImageTexture Portrait(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        try { return M59Assets.FromTex(M59Compose.Icon(o, PortraitSize)); }
        catch (Exception e) { GD.PrintErr($"[NpcQuestsPanel] portrait: {e.Message}"); return null; }
    }

    ImageTexture Icon(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        string key = $"{o.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[NpcQuestsPanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
