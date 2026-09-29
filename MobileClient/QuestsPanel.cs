using System;
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
/// quest. The game draws those in bold and takes the hand cursor away,
/// because there is nothing to click. Everything else is a quest, and
/// clicking one sends `SendReqLookMessage(id)` - the same look the
/// client uses for an object, answered with a description.
///
/// Headings are drawn here as headings and are not tappable, which is
/// the same rule stated the way a phone can state it.
/// </summary>
public partial class QuestsPanel : Control
{
    [Export] public int FontSize = 16;
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

    string _signature = "";

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

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Quests", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);

        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _close = new Button { Text = "Close", Visible = false };
        _close.AddThemeFontSizeOverride("font_size", FontSize);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public void Open() { Show(true); Opened?.Invoke(); _signature = ""; }
    public void Close() => Show(false);

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _close.Visible = on; _open.Visible = !on;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(88, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 88, v.Y - ButtonBottom - 40);

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.66f, 620f);
        float top = v.Y - height - side;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _scroll.Position = new Vector2(side, top + FontSize * 2.2f);
        _scroll.Size = new Vector2(v.X - side * 2f, height - FontSize * 2.2f - rowH - 16f);
        _rows.CustomMinimumSize = new Vector2(_scroll.Size.X, 0);

        _close.Position = new Vector2(side, top + height - rowH);
        _close.Size = new Vector2(v.X - side * 2f, rowH);
    }

    public void Sync(SkillList quests)
    {
        if (_rows == null || !IsOpen) return;

        if (quests == null || quests.Count == 0)
        {
            if (_title != null) _title.Text = "Quests (none)";
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (StatList q in quests)
            sb.Append(q?.ResourceName).Append(':').Append(q?.SkillPoints).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        int real = 0;
        foreach (StatList q in quests)
        {
            if (q == null) continue;
            if (q.SkillPoints == 0) _rows.AddChild(Heading(q));
            else { _rows.AddChild(Row(q)); real++; }
        }

        _title.Text = $"Quests ({real})";
    }

    /// <summary>A row with no skill points is a heading, not a quest.</summary>
    Control Heading(StatList q)
    {
        var l = new Label
        {
            Text = string.IsNullOrWhiteSpace(q.ResourceName) ? "-" : q.ResourceName,
            CustomMinimumSize = new Vector2(0, RowHeight * 0.8f),
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize + 2);
        l.AddThemeColorOverride("font_color", new Color(1, 0.86f, 0.45f));
        return l;
    }

    Control Row(StatList q)
    {
        uint id = q.ObjectID;
        var b = new Button
        {
            Text = "   " + (string.IsNullOrWhiteSpace(q.ResourceName) ? "(unnamed)" : q.ResourceName),
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            Flat = true,
        };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.AddThemeColorOverride("font_color", new Color(0.86f, 0.88f, 0.92f));
        b.Pressed += () => Look?.Invoke(id);
        return b;
    }
}
