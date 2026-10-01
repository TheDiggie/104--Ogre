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

    // The shared chrome: a card over a scrim, a title bar with a round
    // close, and a footer. See M59Skin.
    Panel _card, _bar;
    Button _x;
    /// <summary>The frame around the giver's picture.</summary>
    Panel _portraitFrame;
    /// <summary>The surface the description and the requirements sit on.</summary>
    Panel _page;
    /// <summary>What an NPC with nothing to offer says, where the list would be.</summary>
    Label _empty;
    /// <summary>
    /// Each row's colour, kept because M59Skin.Pick sets a font colour
    /// of its own and the quest colours are the information here.
    /// </summary>
    readonly List<Color> _rowColors = new List<Color>();

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

        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window(); _card.Visible = false; AddChild(_card);
        _bar = M59Skin.TitleBar(); _bar.Visible = false; AddChild(_bar);

        _title = M59Skin.Title("Quests"); _title.Visible = false; AddChild(_title);
        _x = M59Skin.CloseX(Dismiss); _x.Visible = false; AddChild(_x);
        _who = Heading("", M59Skin.BodySize, M59Skin.Text);

        // A frame behind the picture, so a sprite with a lot of
        // transparency around it still reads as a portrait and not as
        // art that has come loose.
        _portraitFrame = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _portraitFrame.AddThemeStyleboxOverride("panel", Sunken());
        AddChild(_portraitFrame);
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
        // Built before the captions and the text that sit on it:
        // siblings draw in tree order, so a page added later covers
        // them (it did, and the captions vanished).
        _page = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _page.AddThemeStyleboxOverride("panel", Sunken());
        AddChild(_page);

        _descLabel = Heading("Description", M59Skin.SmallSize, M59Skin.GoldDim);
        _reqLabel = Heading("Requirements", M59Skin.SmallSize, M59Skin.GoldDim);

        _empty = M59Skin.Empty("Nothing to offer just now.");
        _empty.Visible = false;
        AddChild(_empty);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        _rows.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;
        _scroll = new TouchScroll { Visible = false };
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
        }, M59Skin.Kind.Primary);
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

    /// <summary>
    /// An inset surface - the portrait's frame and the page the quest
    /// text is read off. M59Skin's chrome is all raised; a thing you
    /// read out of wants to look punched into the card.
    /// </summary>
    static StyleBoxFlat Sunken()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.051f, 0.043f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
        };
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
        r.AddThemeFontSizeOverride("normal_font_size", M59Skin.BodySize);
        r.AddThemeColorOverride("default_color", M59Skin.Text);
        // A quest description is a paragraph someone wrote, and the
        // requirements are a list read line by line: both want air
        // between the lines. The server's own colours are untouched.
        r.AddThemeConstantOverride("line_separation", 7);
        AddChild(r);
        return r;
    }

    Button Push(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// How wide a line of a quest description may run before the eye
    /// loses the next one.
    /// </summary>
    const float Measure = 760f;

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        Rect2 card = M59Skin.Frame(v);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position; _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        // Who is offering, then what they have: the picture and the
        // name head the LIST rather than the window, because the title
        // bar is already carrying the quest being read.
        bool wide = body.Size.X >= 980f;
        float listW = wide ? Mathf.Clamp(body.Size.X * 0.38f, 340f, 520f) : body.Size.X;
        float pic = PortraitSize;

        _portraitFrame.Position = body.Position;
        _portraitFrame.Size = new Vector2(pic, pic);
        _portrait.Position = body.Position + new Vector2(4f, 4f);
        _portrait.Size = new Vector2(pic - 8f, pic - 8f);
        _who.Position = new Vector2(body.Position.X + pic + M59Skin.Gap,
                                    body.Position.Y + (pic - M59Skin.BodySize * 1.6f) * 0.5f);
        _who.Size = new Vector2(listW - pic - M59Skin.Gap, M59Skin.BodySize * 1.6f);

        float top = body.Position.Y + pic + M59Skin.Pad;
        float listH = wide ? body.Position.Y + body.Size.Y - top
                           : Mathf.Max(M59Skin.RowH * 2f, (body.Position.Y + body.Size.Y - top) * 0.34f);

        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(listW, listH);
        _rows.CustomMinimumSize = new Vector2(listW - 14f, 0);
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        // The reading half: beside the list when there is room, under
        // it when there is not. The two blocks are one page with two
        // captions on it, rather than two boxes floating on the card.
        float rx = wide ? body.Position.X + listW + M59Skin.Pad : body.Position.X;
        float ry = wide ? body.Position.Y : top + listH + M59Skin.Pad;
        float rw = wide ? body.Size.X - listW - M59Skin.Pad : body.Size.X;
        float rh = body.Position.Y + body.Size.Y - ry;

        _page.Position = new Vector2(rx, ry);
        _page.Size = new Vector2(rw, Mathf.Max(0f, rh));

        float inner = Mathf.Min(rw - M59Skin.Pad * 2f, Measure);
        float ix = rx + Mathf.Round((rw - inner) * 0.5f);
        float capH = M59Skin.SmallSize + 10f;
        // The description gets the larger share: the requirements are
        // short lines and the description is prose.
        float textH = Mathf.Max(0f, rh - M59Skin.Pad * 2f - capH * 2f - M59Skin.Pad);
        // The description takes what it needs and the requirements
        // follow it, instead of each taking half the page and the
        // instructions sitting four hundred pixels below a two-line
        // description. GetContentHeight is a frame behind a change of
        // text, which is why Pick asks for another pass.
        float wants = _desc.GetContentHeight();
        float descH = wants > 1f
            ? Mathf.Clamp(wants + 6f, M59Skin.RowH, Mathf.Max(M59Skin.RowH, textH - M59Skin.RowH))
            : Mathf.Round(textH * 0.58f);

        float y = ry + M59Skin.Pad;
        _descLabel.Position = new Vector2(ix, y);
        _descLabel.Size = new Vector2(inner, capH); y += capH;
        _desc.Position = new Vector2(ix, y);
        _desc.Size = new Vector2(inner, descH); y += descH + M59Skin.Pad;
        _reqLabel.Position = new Vector2(ix, y);
        _reqLabel.Size = new Vector2(inner, capH); y += capH;
        _req.Position = new Vector2(ix, y);
        _req.Size = new Vector2(inner, Mathf.Max(0f, textH - descH));

        // Right to left: Close under the dismissing thumb, then Help,
        // and Accept - the one thing this window is for - furthest in.
        M59Skin.FootRow(foot, _close, _help, _accept);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _who.Visible = on; _portrait.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on; _portraitFrame.Visible = on;
        _page.Visible = on;
        _scroll.Visible = on; _descLabel.Visible = on; _desc.Visible = on;
        _reqLabel.Visible = on; _req.Visible = on;
        _accept.Visible = on; _help.Visible = on; _close.Visible = on;
        _empty.Visible = on && _quests.Count == 0;
        if (on) GetParent()?.MoveChild(this, -1);
        // The card is laid out against the viewport, and nothing else
        // calls this on the way in.
        if (on) Layout();
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
        _rowColors.Clear();
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
        // ... and the list says so where the list would be.
        _empty.Visible = IsOpen && _quests.Count == 0;
        Layout();
    }

    Button Row(QuestObjectInfo q, int index)
    {
        uint argb = QuestTypeColors.GetColorFor(q.ObjectBase.Flags);

        var b = new Button
        {
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, M59Skin.RowH),
            Icon = Icon(q.ObjectBase),
            Text = "  " + (string.IsNullOrWhiteSpace(q.ObjectBase.Name) ? "(unnamed)" : q.ObjectBase.Name),
            // Named by position so the screenshot harness can pick a row
            // it cannot find by text.
            Name = $"row{index}",
        };
        // Striped like every other list, then the quest's own colour
        // put back on top: green, yellow and white are the library's
        // (QuestTypeColors) and say what the row IS, so nothing here
        // may override them - Dress and Pick both set a font colour,
        // and both are followed by this.
        M59Skin.Dress(b, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
        b.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        var tone = new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f);
        b.AddThemeColorOverride("font_color", tone);
        b.AddThemeColorOverride("font_hover_color", tone);
        b.AddThemeColorOverride("font_pressed_color", tone);
        b.AddThemeColorOverride("font_focus_color", tone);
        _rowColors.Add(tone);
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

        // The chosen row is marked by fill and a gold edge, and keeps
        // its own quest colour - which is why Pick's font colour is
        // put back rather than left.
        for (int i = 0; i < _buttons.Count; i++)
        {
            M59Skin.Pick(_buttons[i], i == index, i % 2 != 0);
            if (i < _rowColors.Count) _buttons[i].AddThemeColorOverride("font_color", _rowColors[i]);
        }

        if (index < 0 || index >= _quests.Count)
        {
            _desc.Text = ""; _req.Text = "";
            _title.Text = $"Quests ({_quests.Count})";
            _accept.Disabled = true;
            return;
        }

        // The two blocks are sized to the text that is about to go in
        // them, which the engine has not measured yet.
        CallDeferred(MethodName.Layout);

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
