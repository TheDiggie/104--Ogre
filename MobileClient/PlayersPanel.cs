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
/// The game's row has a checkbox next to the name for an ignore list
/// that its own source marks "todo: set ignorestate" and never
/// implements. There is no checkbox here. Tapping a name starts a tell
/// to that player instead, which the game does from the chat bar - a
/// phone has no chat bar to type a name into, so the list is the way
/// in. That much is not mirrored, and is marked here because it is the
/// only part that is not.
/// </summary>
public partial class PlayersPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 46;

    /// <summary>Raised when opened, to ask the server for the list.</summary>
    public event Action Opened;
    /// <summary>A name was tapped: start a tell to them.</summary>
    public event Action<string> Tell;

    Button _open;
    ColorRect _panel;
    Label _title;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _close;

    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>Where the open button sits. Set by the view.</summary>
    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Who" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += Open;
        AddChild(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        AddChild(_panel);

        _title = new Label { Text = "Online", Visible = false };
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

        _open.Size = new Vector2(64, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 64, v.Y - ButtonBottom - 40);

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

    public void Sync(OnlinePlayerList players)
    {
        if (_rows == null || !IsOpen) return;

        if (players == null || players.Count == 0)
        {
            if (_title != null) _title.Text = "Online (nobody yet)";
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (OnlinePlayer p in players) sb.Append(p?.Name).Append(':').Append(p?.Flags?.Value).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        foreach (OnlinePlayer p in players)
            if (p != null) _rows.AddChild(Row(p));

        _title.Text = $"Online ({players.Count})";
    }

    Control Row(OnlinePlayer p)
    {
        string who = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;

        var b = new Button
        {
            Text = who,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, RowHeight),
            TooltipText = Kind(p.Flags),
        };
        b.AddThemeFontSizeOverride("font_size", FontSize);

        uint argb = p.Flags != null ? NameColors.GetColorFor(p.Flags) : NameColors.NORMAL;
        b.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f));

        b.Pressed += () => Tell?.Invoke(who);
        return b;
    }

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
