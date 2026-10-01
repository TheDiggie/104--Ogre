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
    // The hand-rolled "Foot" gap that used to live here - air under the
    // last button row, so the only way out of the window did not read as
    // cut off against the panel's own edge - is now the card's footer
    // band (M59Skin.FootH), which keeps that air structurally.

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
    Panel _card, _bar;
    Button _x;
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

        // The scrim eats the touch that would otherwise reach the world
        // behind, and puts the card in front of something rather than
        // being the screen. The CARD is opaque, which is what the
        // reference's FrameWindow with no Alpha asks for
        // (Meridian59.layout:1384, UIActions.cpp:10).
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Actions");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Or the list is as wide as its longest label and the "+"
        // buttons sit wherever each row's text ends - see SpellsPanel.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

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
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
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
        int i = 0;
        foreach (AvatarAction a in All) _rows.AddChild(Row(a, i++));
    }

    static string Label(AvatarAction a) =>
        a == AvatarAction.GuildInvite ? "Guild invite" : a.ToString();

    Control Row(AvatarAction a, int index)
    {
        var button = new Button
        {
            // The skin's row is the floor: RowHeight is the knob, but a
            // thumb needs the 56 the skin settled on.
            CustomMinimumSize = new Vector2(0, Mathf.Max(RowHeight, M59Skin.RowH)),
            Name = $"act{a}",
        };
        // Every other row a shade lighter. Eleven rows of one brown and
        // the eye has nothing to walk down; there is no selected state
        // here because a tap PERFORMS - nothing is ever "the chosen
        // action", so Pick would have nothing to mark.
        M59Skin.Dress(button, index % 2 == 0 ? M59Skin.Kind.Row : M59Skin.Kind.RowAlt);
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
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(name);

        var bind = new Button
        {
            Text = "+",
            TooltipText = "Put on the hotbar",
            CustomMinimumSize = new Vector2(M59Skin.RowH - 12f, 0),
            Name = $"bind{a}",
        };
        // A square stepper rather than a second full-height slab: it is
        // the one thing in the row that is not the row, and it has to
        // look like it takes its own press.
        M59Skin.Dress(bind, M59Skin.Kind.Step);
        bind.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        bind.CustomMinimumSize = new Vector2(M59Skin.RowH - 12f, M59Skin.RowH - 16f);
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

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Sized to the eleven rows it holds, so the window is eleven
        // rows tall instead of a fixed box with three hundred pixels of
        // nothing under the last one. Frame caps it against the screen.
        float want = All.Length * (Mathf.Max(RowHeight, M59Skin.RowH) + 4f);
        Rect2 card = M59Skin.Frame(v, want);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(34, 34);
        _x.Position = new Vector2(card.Position.X + card.Size.X - 34f - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - 34f) * 0.5f);

        _scroll.Position = body.Position;
        _scroll.Size = body.Size;
        _rows.CustomMinimumSize = new Vector2(body.Size.X, 0);

        // Close sits at the right of the footer, where the thumb that
        // dismisses it is, rather than stretched across the bottom edge
        // as the most prominent thing on screen.
        M59Skin.FootRow(foot, _close);
    }
}
