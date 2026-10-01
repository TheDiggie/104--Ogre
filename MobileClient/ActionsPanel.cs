using System;
using Godot;
using Meridian59.Common.Enums;

/// <summary>
/// The eleven things you can do.
///
/// This is the game's `UIActions.cpp`: a list built once, in that
/// file's own order (`:16-26`), of every `AvatarAction` the client
/// knows. A double click there performs one; a drag puts it on an
/// action button.
///
/// Three of the eleven had no way in on the phone at all. The hotbar
/// is seeded with eight, and nothing could reach Dance, Point or
/// GuildInvite - or restore any of the eight once a long press had
/// cleared it. The target row is not the same thing: those buttons act
/// on what you tapped, and half of these take no target.
///
/// A tap performs, as a double click does there. The "+" puts the
/// action on the hotbar, which is what dragging does there and which a
/// phone cannot do while this panel covers the hotbar.
/// </summary>
public partial class ActionsPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 52;
    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    /// <summary>Do it now.</summary>
    public event Action<AvatarAction> Perform;
    /// <summary>Put it on the hotbar.</summary>
    public event Action<AvatarAction> Assign;

    // The game's own order, from UIActions::Initialize.
    static readonly AvatarAction[] All =
    {
        AvatarAction.Attack, AvatarAction.Rest, AvatarAction.Dance,
        AvatarAction.Wave, AvatarAction.Point, AvatarAction.Loot,
        AvatarAction.Buy, AvatarAction.Inspect, AvatarAction.Trade,
        AvatarAction.Activate, AvatarAction.GuildInvite,
    };

    Button _open, _close;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    bool _built;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Acts" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.94f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Actions", Visible = false };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Or the list is as wide as its longest label and the "+"
        // buttons sit wherever each row's text ends - see SpellsPanel.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

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

    public void Open()
    {
        Build();
        Panels.ToFront(this);
        Show(true);
    }

    public void Close() => Show(false);

    void Show(bool on)
    {
        _panel.Visible = on; _title.Visible = on;
        _scroll.Visible = on; _close.Visible = on;
        _open.Visible = !on;
        Panels.ShowOpeners(!on);
        if (on) Layout();
    }

    /// <summary>
    /// Built once. The list is eleven constants - it has no server
    /// behind it and nothing to keep in step with.
    /// </summary>
    void Build()
    {
        if (_built) return;
        _built = true;
        foreach (AvatarAction a in All) _rows.AddChild(Row(a));
    }

    static string Label(AvatarAction a) =>
        a == AvatarAction.GuildInvite ? "Guild invite" : a.ToString();

    Control Row(AvatarAction a)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = $"act{a}",
        };
        button.Pressed += () => Perform?.Invoke(a);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = 8; line.OffsetTop = 4; line.OffsetRight = -8; line.OffsetBottom = -4;
        button.AddChild(line);

        var name = new Label
        {
            Text = Label(a),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        line.AddChild(name);

        var bind = new Button
        {
            Text = "+",
            TooltipText = "Put on the hotbar",
            CustomMinimumSize = new Vector2(RowHeight, 0),
            Name = $"bind{a}",
        };
        bind.AddThemeFontSizeOverride("font_size", FontSize + 2);
        bind.Pressed += () => Assign?.Invoke(a);
        line.AddChild(bind);

        return button;
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _open.Size = new Vector2(76, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 76, v.Y - ButtonBottom - 40);

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.72f, 700f);
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
}
