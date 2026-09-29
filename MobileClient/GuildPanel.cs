using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;

/// <summary>
/// Your guild: who is in it, what rank they hold, and the things a
/// guildmaster can do about it.
///
/// `UIGuild.cpp`. In the game this is four tabs - members, diplomacy,
/// guildmaster and the shield designer - on one window. This is the
/// members tab and the guildmaster tab, which are the two that say
/// something about your own guild; diplomacy is a list of every other
/// guild and its standing, and the shield designer is a pixel editor
/// with a scroll wheel on it. Neither belongs on a first phone pass,
/// and leaving them out is said here rather than left to be discovered.
///
/// Like the shop and the quest offer, the window is the server's
/// decision: `GuildInfo.IsVisible` goes up when a UserCommandGuildInfo
/// arrives, and the view follows it.
///
/// What a row may do is entirely in `GuildInfo.Flags`, and the file's
/// conditions are copied as they stand:
///
///  - support (the vote for guildmaster) is offered only when IsVote is
///    set, and only on someone who is not already the supported member.
///    There is no echo from the server, so the client sets
///    SupportedMember itself and sends the vote.
///  - exile is offered when IsExile is set, and never on yourself, on a
///    rank 5, or on a rank 4 unless you can disband - which is the
///    file's way of saying you cannot exile your equals.
///  - rank may be set when IsSetRank is set, never on yourself, and
///    only downwards from your own: `avatar->Rank > rank &&
///    avatar->Rank > member->Rank`. A rank 5 picking rank 5 for someone
///    else is not a rank change at all - it is abdication, and goes as
///    a different command.
///
/// Rank names come from the guild rather than from the client: ten
/// strings on GuildInfo, five for each gender, and a member's own
/// gender picks the column.
///
/// None of these actions is echoed, so the file clears the data and
/// re-requests after each one. That is copied too, because a roster
/// that silently disagrees with the server is worse than a reload.
/// </summary>
public partial class GuildPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int RowHeight = 52;

    /// <summary>Vote for this member as guildmaster.</summary>
    public event Action<uint> Support;
    /// <summary>Throw this member out. Carries the name, for the ask.</summary>
    public event Action<uint, string> Exile;
    /// <summary>Give this member this rank.</summary>
    public event Action<uint, byte> SetRank;
    /// <summary>Hand the guild to this member. Carries the name, for the ask.</summary>
    public event Action<uint, string> Abdicate;
    /// <summary>Set the guild chest password.</summary>
    public event Action<string> Password;
    /// <summary>Give up the guild hall.</summary>
    public event Action AbandonHall;
    /// <summary>Leave the guild, or disband it.</summary>
    public event Action<bool> Renounce;
    /// <summary>Something changed with no echo: reload.</summary>
    public event Action Reload;
    /// <summary>
    /// The button was pressed: ask the server for the guild.
    ///
    /// Not the game's. There, the window opens because something else
    /// asked - a guild hall, a typed command - and the client never
    /// opens it on its own. A phone has no command line, so there is a
    /// button, and it sends the request the game would have sent.
    /// </summary>
    public event Action Opened;

    Button _open;
    ColorRect _panel;
    Label _title, _hall;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    LineEdit _chest;
    Button _setPassword, _abandon, _renounce, _close;

    GuildInfo _info;
    string _signature = "";
    uint _avatar;

    public bool IsOpen => _panel != null && _panel.Visible;

    [Export] public float ButtonRight = 12f;
    [Export] public float ButtonBottom = 12f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Guild" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += () => Opened?.Invoke();
        AddChild(_open);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Guild", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _hall = Heading("", FontSize, new Color(0.75f, 0.78f, 0.84f));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll = new ScrollContainer { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _chest = new LineEdit { PlaceholderText = "chest password", Visible = false };
        _chest.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_chest);

        _setPassword = Push("Set password", () => Password?.Invoke(_chest.Text ?? ""));
        _abandon = Push("Abandon hall", () => AbandonHall?.Invoke());
        _renounce = Push("Renounce", () =>
            Renounce?.Invoke(_info != null && _info.Flags != null && _info.Flags.IsDisband));
        _close = Push("Close", Close);

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

        _open.Size = new Vector2(78, 40);
        _open.Position = new Vector2(v.X - ButtonRight - 78, v.Y - ButtonBottom - 40);

        float side = Mathf.Max(16f, v.X * 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 780f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.8f;
        _hall.Position = new Vector2(side, y); y += FontSize * 1.8f;

        // Three rows below the roster: the password line, the two
        // guildmaster buttons, and Close.
        float below = rowH * 3f + 16f;
        _scroll.Position = new Vector2(side, y);
        _scroll.Size = new Vector2(w, top + height - below - 8f - y);
        _rows.CustomMinimumSize = new Vector2(w, 0);

        float by = top + height - below;
        _chest.Position = new Vector2(side, by);
        _chest.Size = new Vector2(w * 0.55f - 4f, rowH);
        _setPassword.Position = new Vector2(side + w * 0.55f + 4f, by);
        _setPassword.Size = new Vector2(w * 0.45f - 4f, rowH);

        by += rowH + 8f;
        _abandon.Position = new Vector2(side, by);
        _abandon.Size = new Vector2(w * 0.5f - 4f, rowH);
        _renounce.Position = new Vector2(side + w * 0.5f + 4f, by);
        _renounce.Size = new Vector2(w * 0.5f - 4f, rowH);

        by += rowH + 8f;
        _close.Position = new Vector2(side, by);
        _close.Size = new Vector2(w, rowH);
    }

    public void Close()
    {
        if (_info != null) _info.IsVisible = false;
        Show(false);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _hall.Visible = on;
        _scroll.Visible = on; _close.Visible = on; _open.Visible = !on;

        // The guildmaster half is only there when the server says you
        // have a hall to have a password on.
        bool hall = on && _info != null && _info.PasswordSetFlag != 0;
        _chest.Visible = hall; _setPassword.Visible = hall; _abandon.Visible = hall;
        _renounce.Visible = on;

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Sync(GuildInfo info, uint avatarID)
    {
        if (_rows == null) return;

        if (info == null || !info.IsVisible)
        {
            if (IsOpen) { _info = info; Show(false); _signature = ""; }
            return;
        }

        _info = info;
        _avatar = avatarID;
        if (!IsOpen) Show(true);

        var sb = new System.Text.StringBuilder();
        sb.Append(info.GuildName).Append('|').Append(info.PasswordSetFlag).Append('|')
          .Append(info.SupportedMember?.ID).Append('|')
          .Append(info.Flags != null ? info.Flags.Flags : 0u).Append('|');
        if (info.GuildMembers != null)
            foreach (GuildMemberEntry m in info.GuildMembers)
                sb.Append(m?.ID).Append(':').Append(m?.Rank).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _title.Text = string.IsNullOrWhiteSpace(info.GuildName) ? "Guild" : info.GuildName;
        _hall.Text = info.PasswordSetFlag != 0 ? "" : "No guild hall.";
        if (info.PasswordSetFlag != 0 && !_chest.HasFocus())
            _chest.Text = info.ChestPassword ?? "";

        // Renounce and Disband are the same button with two names, and
        // which one it is comes off the flags rather than off your rank.
        _renounce.Text = info.Flags != null && info.Flags.IsDisband ? "Disband" : "Renounce";

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        if (info.GuildMembers == null) return;
        GuildMemberEntry me = null;
        foreach (GuildMemberEntry m in info.GuildMembers)
            if (m != null && m.ID == avatarID) { me = m; break; }

        int index = 0;
        foreach (GuildMemberEntry m in info.GuildMembers)
            if (m != null) _rows.AddChild(Row(info, m, me, index++));

        Show(true);
    }

    Control Row(GuildInfo info, GuildMemberEntry m, GuildMemberEntry me, int index)
    {
        uint id = m.ID;
        bool isMe = id == _avatar;
        GuildFlags f = info.Flags;

        var line = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = $"member{index}",
        };
        line.AddThemeConstantOverride("separation", 8);

        var name = new Label
        {
            Text = m.Name ?? "(unnamed)",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color",
            isMe ? new Color(1, 0.92f, 0.6f) : new Color(0.86f, 0.88f, 0.92f));
        line.AddChild(name);

        // Rank. The names are the guild's own, five per gender, and the
        // member's gender picks the column.
        var rank = new OptionButton { Name = $"rank{index}" };
        rank.AddThemeFontSizeOverride("font_size", FontSize - 2);
        for (byte r = 1; r <= 5; r++) rank.AddItem(RankName(info, m.Gender, r), r);
        rank.Selected = Mathf.Clamp(m.Rank - 1, 0, 4);
        rank.Disabled = f == null || !f.IsSetRank || isMe;
        rank.ItemSelected += which =>
        {
            byte want = (byte)(which + 1);
            if (me == null || want == m.Rank) return;

            // Rank 5 choosing rank 5 for someone else is not a rank
            // change: it hands the guild over.
            if (f != null && f.IsAbdicate && me.Rank == 5 && want == 5)
            {
                // Asked first, and the dropdown is put back until the
                // answer comes - the file resets the combobox to the
                // data model for every path it does not act on.
                rank.Selected = Mathf.Clamp(m.Rank - 1, 0, 4);
                Abdicate?.Invoke(id, m.Name ?? "");
                return;
            }

            if (f != null && f.IsSetRank && me.Rank > want && me.Rank > m.Rank)
            { SetRank?.Invoke(id, want); Reload?.Invoke(); }
            else rank.Selected = Mathf.Clamp(m.Rank - 1, 0, 4);
        };
        line.AddChild(rank);

        // Support: the vote for guildmaster. One at a time, and the
        // client moves it itself because the server does not echo.
        bool supported = info.SupportedMember != null && info.SupportedMember.ID == id;
        var vote = new CheckBox { ButtonPressed = supported, Name = $"vote{index}" };
        vote.AddThemeFontSizeOverride("font_size", FontSize - 2);
        vote.Disabled = f == null || !f.IsVote || supported;
        vote.Toggled += on =>
        {
            if (!on || info.SupportedMember == null || info.SupportedMember.ID == id) return;
            info.SupportedMember.ID = id;
            Support?.Invoke(id);
            _signature = "";
        };
        line.AddChild(vote);

        var kick = new Button { Text = "Exile", Name = $"exile{index}" };
        kick.AddThemeFontSizeOverride("font_size", FontSize - 2);
        kick.Disabled = f == null || !f.IsExile
                        || isMe || m.Rank == 5 || (m.Rank == 4 && !f.IsDisband);
        kick.Pressed += () => Exile?.Invoke(id, m.Name ?? "");
        line.AddChild(kick);

        return line;
    }

    static string RankName(GuildInfo info, Gender g, byte rank)
    {
        bool female = g == Gender.Female;
        switch (rank)
        {
            case 1: return female ? info.Rank1Female : info.Rank1Male;
            case 2: return female ? info.Rank2Female : info.Rank2Male;
            case 3: return female ? info.Rank3Female : info.Rank3Male;
            case 4: return female ? info.Rank4Female : info.Rank4Male;
            default: return female ? info.Rank5Female : info.Rank5Male;
        }
    }
}
