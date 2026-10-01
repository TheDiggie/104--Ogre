using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;

/// <summary>
/// The quest log.
///
/// `UIQuests.cpp` is a list over `Data->AvatarQuests`, which arrives as
/// the Quests stat group - the client asks for it at login, alongside
/// the condition and attribute groups. Each row is an icon and the
/// quest's name.
///
/// One rule in that file is easy to miss and changes how the list
/// reads: **`SkillPoints == 0` means the row is a heading**, not a
/// quest. The game draws those in bold with the plain arrow cursor
/// instead of the hand (`UIQuests.cpp:113-123`). Everything else is a
/// quest, and clicking one sends `SendReqLookMessage(id)` - the same look
/// the client uses for an object, answered with a description.
///
/// There is no collapsing or grouping behind a heading: the list is flat,
/// items are only added, removed and changed in place
/// (`UIQuests.cpp:37-53`), and nothing toggles a section. A heading is a
/// label. (The reference does still wire its click handler to every row,
/// headings included - `:68-70` - so a click on one asks the server to
/// look at the heading's id; that is an oversight the cursor contradicts,
/// and it is not copied here.)
/// </summary>
public partial class QuestsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 32;
    [Export] public int RowHeight = 44;

    /// <summary>Raised when opened, to ask the server for the list.</summary>
    public event Action Opened;
    /// <summary>A quest was tapped: look at it.</summary>
    public event Action<uint> Look;

    Button _open;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    // The shared chrome: a card over a scrim, a title bar with a round
    // close, and a footer. See M59Skin.
    Panel _card, _bar;
    Button _x;
    /// <summary>What an empty log says.</summary>
    Label _empty;

    string _signature = "";
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();

    public bool IsOpen => _panel != null && _panel.Visible;

    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Quests" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window(); _card.Visible = false; AddChild(_card);
        _bar = M59Skin.TitleBar(); _bar.Visible = false; AddChild(_bar);

        _title = M59Skin.Title("Quests"); _title.Visible = false; AddChild(_title);
        _x = M59Skin.CloseX(Close); _x.Visible = false; AddChild(_x);

        _empty = M59Skin.Empty("No quests yet. Ask around - anyone with something to be done will say so.");
        _empty.Visible = false;
        AddChild(_empty);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Without the expand flag the list is only as wide as its
        // longest row and every icon after it lands wherever that row's
        // text ended - see notes/godot-ui.md.
        _rows.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;

        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _close = new Button { Text = "Close", Visible = false };
        M59Skin.Dress(_close, M59Skin.Kind.Secondary);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public void Open() { Show(true); Opened?.Invoke(); _signature = ""; }
    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _close.Visible = on; _open.Visible = !on;
        _empty.Visible = on && _rows != null && _rows.GetChildCount() == 0;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(88, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 88, v.Y - ButtonBottom - 40);

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // The card is as tall as the log, not as tall as it is allowed
        // to be: five quests used to be shown in a box with four
        // hundred pixels of nothing under them.
        float want = 0f;
        if (_rows != null)
            foreach (Node n in _rows.GetChildren())
                if (n is Control c) want += c.CustomMinimumSize.Y + 4f;
        if (_rows == null || _rows.GetChildCount() == 0) want = M59Skin.RowH * 3f;

        Rect2 card = M59Skin.Frame(v, want);
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

        // A quest name is a line of text, not a table: held to a
        // column rather than stretched across a sideways phone, where
        // the icon is at one edge and nothing is near the other.
        float listW = Mathf.Min(body.Size.X, 820f);
        _scroll.Position = new Vector2(body.Position.X + Mathf.Round((body.Size.X - listW) * 0.5f),
                                       body.Position.Y);
        _scroll.Size = new Vector2(listW, body.Size.Y);
        _rows.CustomMinimumSize = new Vector2(listW - 14f, 0);
        _empty.Position = body.Position;
        _empty.Size = body.Size;

        M59Skin.FootRow(foot, _close);
    }

    public void Sync(SkillList quests)
    {
        if (_rows == null || !IsOpen) return;

        if (quests == null || quests.Count == 0)
        {
            if (_title != null) _title.Text = "Quests (none)";
            // Clear the rows, and the signature with them. The list
            // really does empty and refill on a live server - the
            // library clears it on every save cycle
            // (`DataController.Invalidate`) - and this used to leave
            // the old rows on screen under a "(none)" heading. Worse,
            // the signature was left holding the pre-clear string, so
            // when the same quests came back it compared equal and the
            // rows were never rebuilt: what you were tapping was a row
            // built over StatList objects the library had thrown away.
            //
            // The game has the same flaw - `UIQuests.cpp:37` ignores
            // the list's Reset - so this is a fix rather than a mirror.
            if (_rows != null)
                foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            _signature = "";
            // An empty log says so, rather than showing an empty box.
            _empty.Visible = IsOpen;
            Layout();
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (StatList q in quests)
            sb.Append(q?.ObjectID).Append(':').Append(q?.ResourceName).Append(':').Append(q?.SkillPoints).Append(':')
              .Append(q?.Resource?.Filename).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        int real = 0;
        foreach (StatList q in quests)
        {
            if (q == null) continue;
            if (q.SkillPoints == 0) _rows.AddChild(Heading(q));
            else { _rows.AddChild(Row(q, real)); real++; }
        }

        _title.Text = $"Quests ({real})";
        _empty.Visible = false;
        // The card is sized to the rows it holds, so it has to be laid
        // out again once they exist.
        Layout();
    }

    /// <summary>
    /// A row with no skill points is a heading, not a quest.
    ///
    /// It used to be a bare label the same width as the rows, which on
    /// a list of flat slabs read as a row that had been disabled. A
    /// heading now looks like one: gold, spaced away from what is above
    /// it, and underlined by a hairline that runs the width of the
    /// list. Still not a button, and still not clickable.
    /// </summary>
    Control Heading(StatList q)
    {
        var box = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0, RowHeight * 1.1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 4);

        var l = new Label
        {
            Text = string.IsNullOrWhiteSpace(q.ResourceName) ? "-" : q.ResourceName,
            VerticalAlignment = VerticalAlignment.Bottom,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        l.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        box.AddChild(l);

        var rule = M59Skin.Hairline();
        rule.CustomMinimumSize = new Vector2(0, 1);
        rule.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(rule);
        return box;
    }

    Control Row(StatList q, int index)
    {
        uint id = q.ObjectID;
        var b = new Button
        {
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, M59Skin.RowH),
            // Named by position among the quests (headings not counted), as
            // NpcQuestsPanel names its rows: with an icon the text lives in
            // a child Label and Button.Text is empty, so neither the
            // harness's text match nor anything else can find a row.
            Name = $"row{index}",
        };
        // Striped by position among the quests, so a long log keeps its
        // place; the headings break the stripe, which is what a heading
        // is for.
        M59Skin.Dress(b, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
        b.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        b.AddThemeColorOverride("font_color", M59Skin.Text);
        b.Pressed += () => Look?.Invoke(id);

        string name = string.IsNullOrWhiteSpace(q.ResourceName) ? "(unnamed)" : q.ResourceName;

        // The game puts the quest's own icon on the row, taken as the
        // first frame of its resource rather than composed as an object
        // (`UIQuests.cpp:126-142`) - a quest is not a thing standing in
        // a room, so there is no pose to compose. It is also the only
        // thing that tells two rows apart at a glance.
        ImageTexture icon = Icon(q);
        if (icon == null) { b.Text = "   " + name; return b; }

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 8; line.OffsetTop = 4; line.OffsetRight = -8; line.OffsetBottom = -4;
        b.AddChild(line);

        // IgnoreSize, and shrink rather than fill: this icon is the
        // resource's own frame at its own size rather than something
        // composed to fit, so without both the row is as tall as the
        // art and the picture spills over its neighbours.
        line.AddChild(new TextureRect
        {
            Texture = icon,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var label = new Label
        {
            Text = name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        label.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(label);

        return b;
    }

    ImageTexture Icon(StatList q)
    {
        if (q?.Resource == null) return null;
        string key = $"{q.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromBgf(q.Resource, 0); }
        catch (Exception e) { GD.PrintErr($"[QuestsPanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
