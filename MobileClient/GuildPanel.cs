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
/// guildmaster and the shield designer - on one window. Three of them
/// are here: members and guildmaster, which say something about your
/// own guild, and diplomacy, which is every other guild and where you
/// stand with it. The fourth, the shield designer, is its own panel -
/// GuildShieldPanel - because it wants a picture and three steppers and
/// this window is already full; the Shield button below opens it. Like
/// the reference's tab, that button is there only for a guildmaster:
/// `UIGuild.cpp:176-212` removes both the guildmaster tab and the shield
/// tab whenever IsRenounce is set, and puts them back on IsDisband.
///
/// Diplomacy is one list with two columns of standing: theirs toward
/// you, read out of DeclaredYouAllyList and DeclaredYouEnemyList, and
/// yours toward them, which you may change (`UIGuild.cpp:477-557`).
/// Changing it is not one command but sometimes two, because the
/// server has no "switch sides" - going from ally to enemy is
/// GuildEndAlliance followed by GuildMakeEnemy, and enemy to ally is
/// GuildEndEnemy then GuildMakeAlliance (:762-847). Each transition is
/// gated on its own right - IsDeclareEnemy, IsEndEnemy, IsMakeAlliance,
/// IsEndAlliance - and the row is dead unless you hold at least one of
/// them, or if the guild in it is your own.
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
    /// <summary>
    /// A change of standing toward another guild: its id, where it
    /// stood (0 ally, 1 neutral, 2 enemy) and where it should stand.
    /// The view does not decide which commands that takes - the
    /// transition table is the reference's and lives with the sending.
    /// </summary>
    public event Action<uint, int, int> Diplomacy;

    /// <summary>
    /// Open the shield designer. The reference has no equivalent because
    /// its designer is a tab of this same window; here it is a separate
    /// panel, so something has to ask for it.
    /// </summary>
    public event Action ShieldDesigner;

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
    Button _setPassword, _abandon, _renounce, _close, _tab, _shield;

    DiplomacyInfo _diplo;

    /// <summary>Which list the window is showing.</summary>
    bool _showingDiplomacy;

    GuildInfo _info;
    string _signature = "";
    uint _avatar;

    /// <summary>
    /// Out of the way while the shield designer has the screen. See
    /// <see cref="Suspend"/>.
    /// </summary>
    bool _suspended;

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
        Panels.Opener(_open);

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
        _tab = Push("Diplomacy", () =>
        {
            _showingDiplomacy = !_showingDiplomacy;
            _signature = "";        // force the rebuild
        });
        _shield = Push("Shield", () => ShieldDesigner?.Invoke());
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

        float side = Panels.Side(v, 0.06f);
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
        // guildmaster buttons, and Close - plus the gap under the last
        // of them. Without that last term the Close row's bottom edge
        // and the panel's own were the same line, so the only way out
        // of the window looked cut off, while every row above it had
        // eight pixels of air.
        const float foot = 12f;
        float below = rowH * 3f + 16f + foot;
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

        // The last row is three buttons wide now rather than two: the
        // list switch, the way into the shield designer, and Close. The
        // shield button is not always there, so its share of the row goes
        // to Close when it is not - a gap in the middle of the footer
        // would read as a button that had failed to draw.
        by += rowH + 8f;
        bool shield = _shield != null && _shield.Visible;
        float tabW = w * (shield ? 0.32f : 0.4f);
        _tab.Position = new Vector2(side, by);
        _tab.Size = new Vector2(tabW - 4f, rowH);
        if (shield)
        {
            _shield.Position = new Vector2(side + tabW + 4f, by);
            _shield.Size = new Vector2(w * 0.28f - 8f, rowH);
            _close.Position = new Vector2(side + tabW + w * 0.28f + 4f, by);
            _close.Size = new Vector2(w * 0.4f - 4f, rowH);
        }
        else
        {
            _close.Position = new Vector2(side + tabW + 4f, by);
            _close.Size = new Vector2(w - tabW - 4f, rowH);
        }
    }

    /// <summary>
    /// Steps aside for the shield designer, without closing the window.
    ///
    /// In the game the designer is not a separate window at all: it is
    /// the fourth TAB of this one (`UIGuild.cpp:15` names it
    /// `Guild.TabShield`), so exactly one of the roster and the designer
    /// can ever be on screen. Here they are two panels, and nothing said
    /// so - the designer simply opened on top, its own backdrop only 0.97
    /// opaque, and the result was two windows in one place: two titles
    /// printed over each other, the roster's rows and its Exile buttons
    /// reading through the designer, and two different Close buttons
    /// stacked at the foot. Which one a tap reached was down to tree
    /// order.
    ///
    /// Not <see cref="Close"/>, which is the real thing and throws both
    /// models away (`UIGuild.cpp:948-956`) - coming back from the
    /// designer would then show an empty window and have to re-ask the
    /// server for a roster it already had. The data stays exactly as it
    /// is; only the controls go. That is the same trip Settings and the
    /// alias editor already make, where one closes and the other opens
    /// and closing it puts the first one back.
    ///
    /// The flag is needed because <see cref="Sync"/> runs every frame and
    /// re-opens the window whenever the server still says IsVisible; a
    /// bare Show(false) would be undone before the next frame drew.
    /// </summary>
    public void Suspend()
    {
        if (_panel == null || _suspended) return;
        _suspended = true;
        Show(false);
        // Show(false) is a panel closing, so it puts its own opener back
        // along the bottom edge. Nothing has closed here, and a "Guild"
        // button under the designer would be an invitation to open a
        // third thing on top of the second.
        if (_open != null) _open.Visible = false;
    }

    /// <summary>
    /// The designer has gone: take the screen back, if there is still a
    /// guild window to take it back for. If the server withdrew the
    /// window while the designer was up, Sync has already cleared the
    /// flag and this does nothing.
    /// </summary>
    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        if (_info == null || !_info.IsVisible) return;
        _signature = "";            // rebuild: the roster may have moved on
        Show(true);
    }

    public void Close()
    {
        _suspended = false;
        if (_info != null) _info.IsVisible = false;
        // The reference throws both models away when the window goes
        // (`UIGuild.cpp:948-956`), so the next opening asks the server
        // rather than showing what it remembered.
        _info?.Clear(true);
        _diplo?.Clear(true);
        _signature = "";
        Show(false);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _hall.Visible = on;
        _scroll.Visible = on; _close.Visible = on; _open.Visible = !on;
        _tab.Visible = on;

        // The guildmaster half is only there when the server says you
        // have a hall to have a password on - AND when you are the
        // guildmaster. The reference removes the whole tab whenever
        // IsRenounce is set, which is its way of saying you are not
        // (`UIGuild.cpp:176-212`), and only then looks at the password
        // flag (:164-173). Without the first half, any member of a
        // guild with a hall was offered 'Abandon hall'. The server
        // refuses it, but the button should not be there to press.
        bool master = _info != null && _info.Flags.IsDisband;
        bool hall = on && master && _info.PasswordSetFlag != 0;
        _chest.Visible = hall; _setPassword.Visible = hall; _abandon.Visible = hall;
        _renounce.Visible = on;

        // The shield tab is a guildmaster's, and unlike the password and
        // the hall buttons it does not also depend on owning a hall -
        // `UIGuild.cpp:196-209` adds and removes the shield tab purely on
        // the renounce/disband flag.
        _shield.Visible = on && master;

        if (on) GetParent()?.MoveChild(this, -1);
        Layout();
    }

    public void Sync(GuildInfo info, DiplomacyInfo diplomacy, uint avatarID)
    {
        _diplo = diplomacy;
        if (_rows == null) return;

        if (info == null || !info.IsVisible)
        {
            // Whatever the designer was standing in front of is gone, so
            // there is nothing left to come back to: drop the flag here
            // rather than leaving Resume to re-open a dead window.
            _suspended = false;
            if (IsOpen) { _info = info; Show(false); _signature = ""; }
            return;
        }

        _info = info;
        _avatar = avatarID;
        // The shield designer has the screen. The model is kept up to
        // date above - it is the same GuildInfo the designer reads its
        // guildmaster flags off - but nothing of this window is drawn or
        // rebuilt until Resume says so.
        if (_suspended) return;
        if (!IsOpen) Show(true);

        var sb = new System.Text.StringBuilder();
        sb.Append(_showingDiplomacy ? 'D' : 'M').Append('|');
        if (_showingDiplomacy && diplomacy?.Guilds != null)
            foreach (GuildEntry g in diplomacy.Guilds)
                sb.Append(g?.ID).Append(':').Append(g?.Name).Append(':')
                  .Append(Standing(diplomacy, g?.ID ?? 0, true)).Append(':')
                  .Append(Standing(diplomacy, g?.ID ?? 0, false)).Append(';');
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
        _tab.Text = _showingDiplomacy ? "Members" : "Diplomacy";

        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        if (_showingDiplomacy)
        {
            if (diplomacy?.Guilds != null)
            {
                int gi = 0;
                foreach (GuildEntry g in diplomacy.Guilds)
                    if (g != null) _rows.AddChild(GuildRow(info, diplomacy, g, gi++));
            }
            Show(true);
            return;
        }

        if (info.GuildMembers == null) return;
        GuildMemberEntry me = null;
        foreach (GuildMemberEntry m in info.GuildMembers)
            if (m != null && m.ID == avatarID) { me = m; break; }

        int index = 0;
        foreach (GuildMemberEntry m in info.GuildMembers)
            if (m != null) _rows.AddChild(Row(info, m, me, index++));

        Show(true);
    }

    /// <summary>
    /// Where one guild stands: 0 ally, 1 neutral, 2 enemy.
    /// <paramref name="ours"/> picks whose declaration is being read -
    /// yours toward them, or theirs toward you. The reference reads the
    /// same four lists the same way (`UIGuild.cpp:499-531`).
    /// </summary>
    static int Standing(DiplomacyInfo d, uint guildID, bool ours)
    {
        if (d == null || guildID == 0) return 1;
        var ally = ours ? d.YouDeclaredAllyList : d.DeclaredYouAllyList;
        var enemy = ours ? d.YouDeclaredEnemyList : d.DeclaredYouEnemyList;
        if (ally != null && ally.GetItemByID(guildID) != null) return 0;
        if (enemy != null && enemy.GetItemByID(guildID) != null) return 2;
        return 1;
    }

    static string StandingName(int s) => s == 0 ? "Ally" : (s == 2 ? "Enemy" : "Neutral");

    /// <summary>One other guild, and where the two of you stand.</summary>
    Control GuildRow(GuildInfo info, DiplomacyInfo d, GuildEntry g, int index)
    {
        uint id = g.ID;
        var line = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = $"guild{index}",
        };
        line.AddThemeConstantOverride("separation", 8);

        var name = new Label
        {
            Text = g.Name ?? "(unnamed)",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        name.AddThemeFontSizeOverride("font_size", FontSize);
        line.AddChild(name);

        // Theirs toward you, which you cannot change and the reference
        // shows as plain text.
        int theirs = Standing(d, id, false);
        var said = new Label
        {
            Text = StandingName(theirs),
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(FontSize * 5f, 0),
            Name = $"theirs{index}",
        };
        said.AddThemeFontSizeOverride("font_size", FontSize - 2);
        said.AddThemeColorOverride("font_color",
            theirs == 0 ? new Color(0.6f, 0.9f, 0.6f)
          : theirs == 2 ? new Color(0.95f, 0.55f, 0.5f)
                        : new Color(0.7f, 0.72f, 0.78f));
        line.AddChild(said);

        // Yours toward them, in the reference's own order: Ally,
        // Neutral, Enemy (`UIGuild.cpp:513-518`).
        int ours = Standing(d, id, true);
        var pick = new OptionButton { Name = $"ours{index}" };
        pick.AddThemeFontSizeOverride("font_size", FontSize - 2);
        pick.AddItem("Ally", 0);
        pick.AddItem("Neutral", 1);
        pick.AddItem("Enemy", 2);
        pick.Selected = ours;
        pick.CustomMinimumSize = new Vector2(FontSize * 7f, 0);

        // Dead on your own guild, and dead unless you hold at least one
        // of the four rights (`UIGuild.cpp:545-548`).
        GuildFlags f = info.Flags;
        bool any = f != null && (f.IsDeclareEnemy || f.IsEndEnemy
                              || f.IsEndAlliance || f.IsMakeAlliance);
        pick.Disabled = !any || (info.GuildID != null && info.GuildID.ID == id);

        int was = ours;
        pick.ItemSelected += now => Diplomacy?.Invoke(id, was, (int)now);
        line.AddChild(pick);

        return line;
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
