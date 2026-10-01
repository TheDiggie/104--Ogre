using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// Who else is logged in.
///
/// `UIOnlinePlayers.cpp` is a list over `Data->OnlinePlayers`, one row
/// per player, the name drawn in `NameColors::GetColorFor(player->Flags)`
/// - the library's own function, the same one the loot list and the
/// name labels use. The row also carries a tooltip naming what the
/// player is: moderator, admin, GM, murderer, outlaw or lawful, decided
/// by the same flags.
///
/// The list arrives from the server: `SendSendPlayers` asks and a
/// `Players` message answers, which the data layer sorts by name.
///
/// Each row has a checkbox for the ignore list, as the game's does.
/// This file used to claim the game never implemented it, on the
/// strength of a "todo: set ignorestate" comment in
/// `UIOnlinePlayers.cpp` - that comment is about refreshing the
/// checkbox when a row changes, and the rest is fully wired and
/// load-bearing: `OnIgnoreSelectStateChanged` adds or removes the
/// player's name in `Data.IgnoreList`, `HandleSaid` drops an incoming
/// message outright when its speaker is on that list, and the list is
/// saved per connection and reloaded on connect. Nothing is sent to
/// the server; the filtering is entirely this side. Blocking someone
/// is one of the few things a player can do about another, so it is
/// worth having.
///
/// Tapping the name starts a tell to that player, which the game does
/// from the chat bar - a phone has no chat bar to type a name into, so
/// the list is the way in. That much is an addition rather than a
/// mirror.
/// </summary>
public partial class PlayersPanel : Control
{
    /// <summary>
    /// Air under the last button row. Without it the button's bottom
    /// edge and the panel's own are the same line, and the only way out
    /// of the window reads as cut off while every row above it has a
    /// gap.
    /// </summary>
    const float Foot = 12f;

    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 46;

    /// <summary>Raised when opened, to ask the server for the list.</summary>
    public event Action Opened;
    /// <summary>A name was tapped: start a tell to them.</summary>
    public event Action<string> Tell;

    Button _open;
    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title;
    Label _empty;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>Where the open button sits. Set by the view.</summary>

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Who" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);
        Panels.Opener(_open, "Players online", 240);

        // The scrim eats the touch that would reach the world behind,
        // and is what makes the card read as being in front of
        // something rather than being the screen.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Online");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Without this the list is only as wide as its longest name and
        // the mute column lands wherever that name ended - see
        // notes/godot-ui.md, "A ScrollContainer sizes its child to that
        // child's minimum".
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _empty = M59Skin.Empty("Nobody else is online.");
        _empty.Visible = false;
        AddChild(_empty);

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
        _close.Visible = on; _open.Visible = !on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        if (!on) _empty.Visible = false;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;


        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Sized to the list it holds: four people online is a four-row
        // window, not six hundred pixels of black under four names.
        float rowH = Mathf.Max(RowHeight, M59Skin.RowH);
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        // A list of names does not want the full width a sideways
        // phone would give it: the mute column would end up a thumb's
        // length from the name it mutes.
        Rect2 card = M59Skin.Frame(v, shown * (rowH + 4f), true, 900f);
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

        _scroll.Position = body.Position;
        _scroll.Size = body.Size;
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        _rows.CustomMinimumSize = new Vector2(M59Skin.RowsW(body), 0);

        // Over the list, where the rows would have been.
        _empty.Position = _scroll.Position;
        _empty.Size = _scroll.Size;

        // Close at the right of the footer rather than stretched across
        // the bottom as the loudest thing on the panel.
        M59Skin.FootRow(foot, _close);
    }

    public void Sync(OnlinePlayerList players)
    {
        if (_rows == null || !IsOpen) return;

        if (players == null || players.Count == 0)
        {
            if (_title != null) _title.Text = "Online (nobody yet)";
            // A sentence rather than an empty rectangle, which reads as
            // a panel that failed.
            if (_empty != null) _empty.Visible = true;
            return;
        }
        _empty.Visible = false;

        var sb = new System.Text.StringBuilder();
        foreach (OnlinePlayer p in players) 
            // Name, flags AND the name colour. NameColor is its own field
            // (`ObjectFlags.cs:183`), not part of Value, so a colour
            // change alone used to leave the row as it was. The
            // reference recolours from the same flags
            // (`UIOnlinePlayers.cpp:52-77` for the tooltip beside it).
            sb.Append(p?.Name).Append(':').Append(p?.Flags?.Value).Append(':')
              .Append(p?.Flags != null ? NameColors.GetColorFor(p.Flags) : 0u).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        foreach (OnlinePlayer p in players)
            if (p != null) _rows.AddChild(Row(p));

        _title.Text = $"Online ({players.Count})";

        // The card is sized to the row count, so a list that just
        // changed length needs the frame measured again.
        Layout();
    }

    Control Row(OnlinePlayer p)
    {
        string who = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
        int index = _rows.GetChildCount();
        bool alt = index % 2 == 1;

        // The row is one striped slab with the three columns laid on
        // it, rather than a name-shaped button with two things floating
        // beside it: the mute belongs to the name next to it, and the
        // stripe is what says so. The name button takes the same
        // stripe, so the slab reads as continuous and only the half
        // that is pressable lights under a finger.
        var back = new Panel
        {
            CustomMinimumSize = new Vector2(0, Mathf.Max(RowHeight, M59Skin.RowH)),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        back.AddThemeStyleboxOverride("panel", M59Skin.Stripe(alt));

        var line = new HBoxContainer();
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        line.OffsetLeft = 6; line.OffsetRight = -M59Skin.Gap;
        line.OffsetTop = 3; line.OffsetBottom = -3;
        back.AddChild(line);

        uint argb = p.Flags != null ? NameColors.GetColorFor(p.Flags) : NameColors.NORMAL;
        var tint = new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f);

        var b = new Button
        {
            Text = who,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = Kind(p.Flags),
            Name = $"who{index}",
        };
        // The library's name colour is handed to Dress rather than
        // re-applied after it: Dress writes every state - hover,
        // pressed, focus - and a colour painted on afterwards was
        // painted over by the next Dress or Pick. Alternating stripes,
        // so a long list keeps its place.
        M59Skin.Dress(b, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row, tint);
        b.Pressed += () => Tell?.Invoke(who);
        line.AddChild(b);

        // The tooltip's words, printed. The reference says moderator,
        // admin, GM, murderer, outlaw or lawful on hover
        // (`UIOnlinePlayers.cpp:52-77`), and a phone never hovers, so the
        // same words sit in the row (the Kind() below is the same
        // decision). The tooltip is kept for a mouse.
        //
        // A fixed column, right-aligned: this is a table, and the word
        // landing wherever each name ended is what made it read as a
        // ragged list rather than one.
        var kind = new Label
        {
            Text = Kind(p.Flags),
            Name = $"kind{index}",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(KindW, 0),
        };
        kind.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        kind.AddThemeColorOverride("font_color", M59Skin.TextDim);
        line.AddChild(kind);

        var ignore = new CheckBox
        {
            Text = "mute",
            ButtonPressed = _ignored != null && _ignored.Contains(p.Name),
            Name = $"mute{index}",
            // Its own column, so the boxes line up down the list
            // instead of each sitting at the end of its own row.
            CustomMinimumSize = new Vector2(MuteW, M59Skin.TapMin),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        ignore.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        ignore.AddThemeColorOverride("font_color", M59Skin.TextDim);
        TickStyle.Apply(ignore, 28);  // dark-on-dark by default; see TickStyle in BuyPanel.cs
        string name = p.Name;
        ignore.Toggled += on => Ignore?.Invoke(name, on);
        line.AddChild(ignore);

        return back;
    }

    /// <summary>The two fixed columns to the right of the name.</summary>
    const float KindW = 140f, MuteW = 96f;

    /// <summary>
    /// Someone was muted or unmuted. The caller owns the list, because
    /// it is the client's and it outlives this panel.
    /// </summary>
    public event Action<string, bool> Ignore;

    System.Collections.Generic.List<string> _ignored;

    /// <summary>The list the checkboxes are drawn from.</summary>
    public void Follow(System.Collections.Generic.List<string> ignored) => _ignored = ignored;

    /// <summary>
    /// What the game's tooltip says they are, decided by the same flags
    /// the colour comes from - OnlinePlayers::SetTooltip, in order.
    /// </summary>
    static string Kind(ObjectFlags f)
    {
        if (f == null) return "";
        switch (f.Player)
        {
            // Creator is the admin and DM is the game master, which is
            // the other way round from how the names read.
            case ObjectFlags.PlayerType.Moderator: return "moderator";
            case ObjectFlags.PlayerType.Creator:   return "admin";
            case ObjectFlags.PlayerType.SuperDM:   return "game master";
            case ObjectFlags.PlayerType.DM:        return "game master";
            case ObjectFlags.PlayerType.Killer:    return "murderer";
            case ObjectFlags.PlayerType.Outlaw:    return "outlaw";
            default:                               return "lawful";
        }
    }
}
