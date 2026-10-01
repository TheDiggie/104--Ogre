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
    Panel _card, _bar;
    Button _x;
    Label _title, _hall;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    LineEdit _chest;
    Button _setPassword, _abandon, _renounce, _close, _shield;

    /// <summary>
    /// The two lists, as a pair of tabs rather than the one button that
    /// renamed itself. The reference's window IS tabs (`UIGuild.cpp:15`),
    /// and a single control reading "Diplomacy" could say where you were
    /// going but never where you were.
    /// </summary>
    Button _tabMembers, _tabDiplomacy;

    /// <summary>
    /// The column captions over the list. Outside the scroll, because a
    /// heading that scrolls away stops being a heading - the same
    /// reasoning GuildHallBuyPanel's headings carry.
    /// </summary>
    HBoxContainer _head;
    Label _hName, _hA, _hB, _hC;

    /// <summary>The guildmaster's block at the foot of the body, under a rule.</summary>
    ColorRect _rule;
    Label _masterHead, _chestCap;

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

    /// <summary>The Guild button was pressed and nothing has answered yet.</summary>
    bool _asking;
    ulong _askedAt;
    /// <summary>The "you do not belong to a guild" window is what is on screen.</summary>
    bool _noticeUp;
    Label _none;

    public bool IsOpen => _panel != null && _panel.Visible;


    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "Guild" };
        _open.AddThemeFontSizeOverride("font_size", FontSize);
        _open.Pressed += () =>
        {
            // Remember that the player asked: the server answers a guildless
            // player with a chat line and no GuildInfo, so silence after a
            // short wait is the answer (see ShowNotice).
            _askedAt = Time.GetTicksMsec(); _asking = true;
            Opened?.Invoke();
        };
        AddChild(_open);
        Panels.Opener(_open, "Your guild", 220);

        // The scrim eats the touch that would reach the world behind;
        // the card over it is opaque, which the old 0.97 panel was not -
        // the chat log read straight through the roster.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Guild");
        _title.Visible = false;
        AddChild(_title);

        _x = M59Skin.CloseX(Close);
        _x.Visible = false;
        AddChild(_x);

        _hall = Heading("", M59Skin.BodySize, M59Skin.TextDim);

        _none = M59Skin.Empty("");
        _none.Visible = false;
        _none.Name = "noGuild";
        // ClipText, which is not about clipping. A WRAPPING Label's
        // minimum height is computed from the width it had when it was
        // last shaped, so a label that is sized in one go from zero
        // asks "how tall is this wrapped at one character per line" and
        // answers 3717 - and Size is clamped to the minimum, so the
        // four lines of notice were centred a screen and a half below
        // the card, which simply looked empty. With clipping on the
        // minimum is 1x1 and the size given is the size taken; nothing
        // is ever actually clipped, because the card is measured to
        // hold the text.
        _none.ClipText = true;
        // And wrapping, which ClipText is what makes safe: the notice is
        // a sentence of prose, not a short line, and without this the
        // second paragraph ran off the 760pt card and was elided mid-word
        // ("Speak to him a..."), which loses the only instruction in it.
        // The minimum stays 1x1 because of the ClipText above, so the
        // size this is given in Layout is still the size it takes.
        _none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_none);

        _tabMembers = Push("Members", () =>
        {
            if (!_showingDiplomacy) return;
            _showingDiplomacy = false;
            _signature = "";        // force the rebuild
        });
        _tabDiplomacy = Push("Diplomacy", () =>
        {
            if (_showingDiplomacy) return;
            _showingDiplomacy = true;
            _signature = "";
        });
        M59Skin.Dress(_tabMembers, M59Skin.Kind.Tab);
        M59Skin.Dress(_tabDiplomacy, M59Skin.Kind.Tab);

        // One header row, retexted per list: the columns are the same
        // shape in both, so two of them would only be two things to keep
        // in step.
        _head = new HBoxContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _head.AddThemeConstantOverride("separation", M59Skin.GapI);
        _head.AddChild(_hName = Caption("", HorizontalAlignment.Left, 0f));
        _head.AddChild(_hA = Caption("", HorizontalAlignment.Left, ColRank));
        _head.AddChild(_hB = Caption("", HorizontalAlignment.Center, ColVote));
        _head.AddChild(_hC = Caption("", HorizontalAlignment.Center, ColExile));
        AddChild(_head);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // Or the list is only as wide as its longest member name and
        // every column after it lands where that row's text ended - see
        // notes/godot-ui.md, "A ScrollContainer sizes its child to that
        // child's minimum".
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _rule = M59Skin.Hairline();
        _rule.Visible = false;
        AddChild(_rule);
        _masterHead = Heading2("Guild hall");
        _chestCap = Caption2("Guild chest password");

        _chest = M59Skin.Field(new LineEdit { PlaceholderText = "chest password", Visible = false });
        AddChild(_chest);

        _setPassword = Push("Set password", () => Password?.Invoke(_chest.Text ?? ""));
        _abandon = Push("Abandon hall", () => AbandonHall?.Invoke());
        // IsRenounce first, then IsDisband, and nothing at all when neither
        // is set: `UIGuild.cpp:870-888` has no final else, so a window with
        // neither right has nothing to ask and sends nothing. The button is
        // not even shown then (see Show), this is the second line of defence.
        _renounce = Push("Renounce", () =>
        {
            GuildFlags f = _info?.Flags;
            if (f == null) return;
            if (f.IsRenounce) Renounce?.Invoke(false);
            else if (f.IsDisband) Renounce?.Invoke(true);
        });
        _shield = Push("Shield", () => ShieldDesigner?.Invoke());
        _close = Push("Close", Close);

        // The footer's three, by what they do: Renounce and Disband
        // destroy something, the other two do not. Close is the one a
        // thumb reaches for, so it is the plain one at the right.
        M59Skin.Dress(_setPassword, M59Skin.Kind.Secondary);
        M59Skin.Dress(_abandon, M59Skin.Kind.Secondary);
        M59Skin.Dress(_renounce, M59Skin.Kind.Danger);
        M59Skin.Dress(_shield, M59Skin.Kind.Secondary);
        M59Skin.Dress(_close, M59Skin.Kind.Secondary);

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

    /// <summary>A section heading inside the body, in the house style.</summary>
    Label Heading2(string text)
    {
        Label l = M59Skin.Heading(text);
        l.Visible = false;
        AddChild(l);
        return l;
    }

    /// <summary>The small gold line over a field.</summary>
    Label Caption2(string text)
    {
        Label l = M59Skin.Caption(text);
        l.Visible = false;
        AddChild(l);
        return l;
    }

    /// <summary>
    /// One column caption over the list. A width of zero means "take
    /// what is left", which is the name column; the rest are the fixed
    /// widths the rows use, so the two line up.
    /// </summary>
    static Label Caption(string text, HorizontalAlignment align, float width)
    {
        Label l = M59Skin.Caption(text);
        l.HorizontalAlignment = align;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.MouseFilter = MouseFilterEnum.Ignore;
        if (width > 0f) l.CustomMinimumSize = new Vector2(width, 0);
        else l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

    /// <summary>Height of the tab strip at the top of the body.</summary>
    const float TabH = 44f;
    /// <summary>Height of the column captions over the list.</summary>
    const float HeadH = 24f;
    /// <summary>
    /// The roster's columns and the diplomacy list's, as fixed widths so
    /// the header and every row agree. This is the whole of "make it a
    /// table": before, each row was an HBox that put its controls
    /// wherever its own name happened to end.
    /// </summary>
    const float ColRank = 210f, ColVote = 56f, ColExile = 112f;
    const float ColSaid = 150f, ColPick = 210f;
    /// <summary>The gutter a row insets its contents by - the header too.</summary>
    const float RowInset = 12f;
    /// <summary>Room kept for the list's scrollbar, so the header stays over its columns.</summary>
    const float BarW = 14f;

    float RowTall => Mathf.Max(RowHeight, M59Skin.RowH);

    /// <summary>
    /// How tall the guildmaster's block at the foot of the body is, which
    /// the frame has to know before it can be measured. Zero for anyone
    /// who is not the guildmaster: that whole section is theirs.
    /// </summary>
    float MasterH()
    {
        if (_masterHead == null || !_masterHead.Visible) return 0f;
        float h = 1f + M59Skin.Gap + 26f + 6f;
        return h + (_chest.Visible ? 20f + 46f : 26f);
    }

    /// <summary>Card, title bar, name and the round close, for a given frame.</summary>
    void Chrome(Rect2 card)
    {
        _card.Position = card.Position; _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;


        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // The notice is four lines of prose, so it gets a prompt-sized
        // card and not the roster's: Frame's width cap is what keeps a
        // sentence from being strung across a sideways phone.
        if (_noticeUp)
        {
            Rect2 small = M59Skin.Frame(v, 160f, true, M59Skin.Measure);
            Chrome(small);
            Rect2 nb = M59Skin.Body(small);
            _none.Position = nb.Position;
            _none.Size = nb.Size;
            M59Skin.FootRow(M59Skin.Foot(small), _close);
            return;
        }

        // Sized to what is in it: a guild of three is a three-row window
        // rather than eight hundred pixels of black under three names.
        int shown = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        float master = MasterH();
        float want = TabH + M59Skin.Gap + HeadH + shown * (RowTall + 4f) + master;
        Rect2 card = M59Skin.Frame(v, want);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);
        Chrome(card);

        // The tabs are as wide as their words, not half a card each: on
        // a sideways phone that would be two eight-hundred-pixel slabs.
        const float tabW = 180f;
        _tabMembers.Position = body.Position;
        _tabMembers.Size = new Vector2(tabW, TabH);
        _tabDiplomacy.Position = new Vector2(body.Position.X + tabW + M59Skin.Gap, body.Position.Y);
        _tabDiplomacy.Size = new Vector2(tabW, TabH);

        float y = body.Position.Y + TabH + M59Skin.Gap;
        _head.Position = new Vector2(body.Position.X + RowInset, y);
        // The rows take the scroll's full width until a scrollbar
        // appears, so the header matches that and not the reserved
        // width - a caption fourteen pixels off its column is worse,
        // every day, than one that drifts when the list overflows.
        _head.Size = new Vector2(body.Size.X - RowInset * 2f, HeadH);
        y += HeadH;

        float listH = Mathf.Max(RowTall, body.Position.Y + body.Size.Y - master - y);
        _scroll.Position = new Vector2(body.Position.X, y);
        _scroll.Size = new Vector2(body.Size.X, listH);
        // The bar is wider than this file's own BarW now; the skin
        // owns that number. See M59Skin.RowsW.
        _rows.CustomMinimumSize = new Vector2(M59Skin.RowsW(body), 0);

        // The guildmaster's controls are a SECTION of this window now,
        // under a rule and a heading, rather than three loose rows
        // floating between the list and the bottom edge.
        float my = body.Position.Y + body.Size.Y - master;
        _rule.Position = new Vector2(body.Position.X, my);
        _rule.Size = new Vector2(body.Size.X, 1f);
        my += 1f + M59Skin.Gap;
        _masterHead.Position = new Vector2(body.Position.X, my);
        _masterHead.Size = new Vector2(body.Size.X, 26f);
        my += 26f + 6f;

        // "No guild hall." and the password line are the two faces of
        // the same slot, so they are laid out there together and shown
        // one at a time: Show() makes _hall visible only when
        // PasswordSetFlag is zero, which is exactly when the chest line
        // is hidden. (It also blanks _hall's text in the other case,
        // but visibility is what keeps them apart, not that.)
        _hall.Position = new Vector2(body.Position.X, my);
        _hall.Size = new Vector2(body.Size.X, 26f);
        if (_chest.Visible)
        {
            _chestCap.Position = new Vector2(body.Position.X, my);
            _chestCap.Size = new Vector2(body.Size.X, 18f);
            my += 20f;
            float fw = Mathf.Min(360f, body.Size.X * 0.4f);
            _chest.Position = new Vector2(body.Position.X, my);
            _chest.Size = new Vector2(fw, 46f);
            _setPassword.Position = new Vector2(body.Position.X + fw + M59Skin.Gap, my);
            _setPassword.Size = new Vector2(170f, 46f);
            _abandon.Position = new Vector2(body.Position.X + fw + M59Skin.Gap + 180f, my);
            _abandon.Size = new Vector2(170f, 46f);
        }

        // Laid out from the right, so the button that dismisses is where
        // the thumb is and the destructive one is furthest from it.
        M59Skin.FootRow(foot, _close, _shield, _renounce);
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
        _asking = false;
        if (_noticeUp) { HideNotice(); return; }
        if (_info != null) _info.IsVisible = false;
        // The reference throws both models away when the window goes
        // (`UIGuild.cpp:948-956`), so the next opening asks the server
        // rather than showing what it remembered.
        _info?.Clear(true);
        _diplo?.Clear(true);
        // And the shield model, which the reference clears on the same
        // line (`UIGuild.cpp:951`). Without it the designer, opened again,
        // shows the last guild's colours and name until the reply lands.
        (ShieldInfo ?? FindShieldModel())?.Clear(true);
        _signature = "";
        Show(false);
    }

    /// <summary>
    /// The shield model, if the owner has handed it over. GameView does
    /// not (yet), so <see cref="FindShieldModel"/> finds it the only way
    /// this file can: through the designer panel beside it, which is given
    /// `Data.GuildShieldInfo` every frame.
    /// </summary>
    public GuildShieldInfo ShieldInfo;

    GuildShieldInfo FindShieldModel()
    {
        Node parent = GetParent();
        if (parent == null) return null;
        foreach (Node n in parent.GetChildren())
            if (n is GuildShieldPanel)
                return typeof(GuildShieldPanel)
                    .GetField("_shield", System.Reflection.BindingFlags.NonPublic
                                       | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(n) as GuildShieldInfo;
        return null;
    }

    /// <summary>
    /// Puts an open ConfirmPopup back above everything. The reference's
    /// popup is AlwaysOnTop (`Meridian59.layout:2859,2873`), so a window
    /// that is shown and moved to front never ends up over a question
    /// that is waiting for an answer; in this tree "on top" is just the
    /// last child, so whoever moves itself last has to put the popup back.
    /// </summary>
    void KeepPopupOnTop()
    {
        Node parent = GetParent();
        if (parent == null) return;
        foreach (Node n in parent.GetChildren())
            if (n is ConfirmPopup p && p.IsOpen) { parent.MoveChild(p, -1); break; }
    }

    /// <summary>
    /// What a player in no guild is shown when they press Guild.
    ///
    /// The reference shows nothing: the button sends the four requests
    /// (`UIMainButtonsRight.cpp:62-75`), the server answers a guildless
    /// player with the single chat line "You do not belong to a guild."
    /// and no GuildInfo (`user.kod:2749-2753`, text at `:310`), and the
    /// window opens only when GuildInfo.IsVisible goes up
    /// (`UIGuild.cpp:141-148`). On a phone the chat line scrolls away
    /// under the world and the button looks dead, so a small window says
    /// the same thing and where a guild comes from: the only way to be
    /// offered one is Frular, the guild hall executor, who sends GuildAsk
    /// to a guildless player (`gcreator.kod:332`).
    ///
    /// It is shown only after the server has stayed silent for a moment,
    /// so a guilded player's roster is never preceded by it, and it goes
    /// away on its own the moment either answer arrives - GuildInfo, or
    /// the founding window that GuildAsk opens - so it can never sit
    /// between a guildless player and the offer.
    /// </summary>
    void ShowNotice()
    {
        if (_noticeUp || CreateOpen()) return;
        _noticeUp = true;
        _none.Text = "You do not belong to a guild.\n\n"
                   + "A guild is founded through Frular, the guild hall executor. "
                   + "Speak to him and the founding window opens by itself.";
        Panels.ToFront(this);
        _panel.Visible = true; _title.Text = "Guild"; _title.Visible = true;
        _card.Visible = true; _bar.Visible = true; _x.Visible = true;
        _none.Visible = true; _close.Visible = true; _open.Visible = false;
        _scroll.Visible = false; _renounce.Visible = false;
        _tabMembers.Visible = false; _tabDiplomacy.Visible = false; _head.Visible = false;
        _shield.Visible = false; _hall.Visible = false;
        _rule.Visible = false; _masterHead.Visible = false; _chestCap.Visible = false;
        _chest.Visible = false; _setPassword.Visible = false; _abandon.Visible = false;
        GetParent()?.MoveChild(this, -1); KeepPopupOnTop();
        Layout();
    }

    void HideNotice()
    {
        if (!_noticeUp) return;
        _noticeUp = false;
        _none.Visible = false; _close.Visible = false; _title.Visible = false;
        _card.Visible = false; _bar.Visible = false; _x.Visible = false;
        _panel.Visible = false; _open.Visible = true;
        Layout();
    }

    /// <summary>Is the founding window up? Found beside this one, as the designer is.</summary>
    bool CreateOpen()
    {
        Node parent = GetParent();
        if (parent == null) return false;
        foreach (Node n in parent.GetChildren())
            if (n is GuildCreatePanel c && c.IsOpen) return true;
        return false;
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront. Only when the
        // window OPENS: the reference moves to front on the IsVisible edge
        // and not on every rebuild, so a roster that refreshes behind a
        // waiting "Are you sure you want to exile...?" stays behind it.
        bool opening = on && !_panel.Visible;
        if (opening) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _scroll.Visible = on; _close.Visible = on; _open.Visible = !on;
        _tabMembers.Visible = on; _tabDiplomacy.Visible = on; _head.Visible = on;

        // The guildmaster half is only there when the server says you
        // have a hall to have a password on - AND when you are the
        // guildmaster. The reference removes the whole tab whenever
        // IsRenounce is set, which is its way of saying you are not
        // (`UIGuild.cpp:176-212`), and only then looks at the password
        // flag (:164-173). Without the first half, any member of a
        // guild with a hall was offered 'Abandon hall'. The server
        // refuses it, but the button should not be there to press.
        GuildFlags fl = _info?.Flags;
        bool master = fl != null && !fl.IsRenounce && fl.IsDisband;
        bool hall = on && master && _info.PasswordSetFlag != 0;
        _chest.Visible = hall; _setPassword.Visible = hall; _abandon.Visible = hall;
        _chestCap.Visible = hall;
        // The rule and the heading are the section the hall controls and
        // "No guild hall." live in, so they come and go with the rank
        // that owns them, not with the hall.
        _rule.Visible = on && master; _masterHead.Visible = on && master;

        // "No guild hall." lives in the same tab as the password box
        // (`UIGuild.cpp:26`, `:164-173`), so it is the guildmaster's and
        // nobody else ever sees it. It cannot be shown to a member at all:
        // the server sends the password flag only to the MASTER of a guild
        // with a hall (`user.kod` UserGuildSendInfo, `lHall <> $ AND
        // GetRank = RANK_MASTER`), so every other rank reads 0 whether or
        // not the guild has one.
        //
        // Hidden outright when the guild HAS a hall, not merely left
        // blank: both labels are laid out in the same slot (see Layout),
        // and the only thing that kept the two from drawing over each
        // other was that this text happened to be "" whenever the
        // password was showing. The slot has one occupant by
        // construction now: the hall line for a master without a hall,
        // the password line for a master with one, neither for any
        // other rank.
        _hall.Visible = on && master && _info.PasswordSetFlag == 0;

        // Renounce and Disband are one button: it is there when the flags
        // name one of the two, and the reference does nothing with neither
        // (`UIGuild.cpp:870-888`).
        _renounce.Visible = on && fl != null && (fl.IsRenounce || fl.IsDisband);

        // The shield tab is a guildmaster's, and unlike the password and
        // the hall buttons it does not also depend on owning a hall -
        // `UIGuild.cpp:196-209` adds and removes the shield tab purely on
        // the renounce/disband flag.
        _shield.Visible = on && master;

        if (opening) { GetParent()?.MoveChild(this, -1); KeepPopupOnTop(); }
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
            if (_noticeUp)
            {
                // The founding window has taken over: the notice is stale.
                if (CreateOpen()) { _asking = false; HideNotice(); }
                return;
            }
            if (IsOpen) { _info = info; Show(false); _signature = ""; }
            else if (_asking && Time.GetTicksMsec() - _askedAt > 2000)
            {
                _asking = false;
                ShowNotice();
            }
            return;
        }
        _asking = false;
        if (_noticeUp) HideNotice();

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
                sb.Append(m?.ID).Append(':').Append(m?.Rank).Append(':').Append(m?.Name).Append(':').Append(m?.Gender).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _title.Text = string.IsNullOrWhiteSpace(info.GuildName) ? "Guild" : info.GuildName;
        _hall.Text = info.PasswordSetFlag != 0 ? "" : "No guild hall.";
        if (info.PasswordSetFlag != 0 && !_chest.HasFocus())
            _chest.Text = info.ChestPassword ?? "";

        // Renounce and Disband are the same button with two names, and
        // which one it is comes off the flags rather than off your rank.
        // IsRenounce is tested first, as the reference does
        // (`UIGuild.cpp:176-199`, `:870-888`).
        _renounce.Text = info.Flags != null && !info.Flags.IsRenounce && info.Flags.IsDisband
            ? "Disband" : "Renounce";

        // Which list you are on, said by the tabs, and the captions over
        // the columns changed with it - the two lists are different
        // tables under the same frame.
        M59Skin.Tab(_tabMembers, !_showingDiplomacy);
        M59Skin.Tab(_tabDiplomacy, _showingDiplomacy);
        _hName.Text = _showingDiplomacy ? "Guild" : "Member";
        _hA.Text = _showingDiplomacy ? "They say" : "Rank";
        _hA.CustomMinimumSize = new Vector2(_showingDiplomacy ? ColSaid : ColRank, 0);
        _hA.HorizontalAlignment = _showingDiplomacy
            ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _hB.Text = _showingDiplomacy ? "You say" : "Vote";
        _hB.CustomMinimumSize = new Vector2(_showingDiplomacy ? ColPick : ColVote, 0);
        _hB.HorizontalAlignment = _showingDiplomacy
            ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        // The third column is the Exile button, which says its own name
        // on every row; in the diplomacy list there is no third column.
        _hC.Text = "";
        _hC.CustomMinimumSize = new Vector2(ColExile, 0);
        // Hidden rather than zero-width in the diplomacy list: a hidden
        // child costs the HBox no separation either, and ten stray
        // pixels at the end would push every caption off its column.
        _hC.Visible = !_showingDiplomacy;

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

    // The rights tests, in one place so the control's Disabled flag and the
    // handler behind it can never disagree. The reference enables a control
    // by these (`UIGuild.cpp:419-433` vote, :436-447 exile, :449 rank) and
    // its click handlers re-test the flag (`:339` exile, :159/:174 rank).
    // A disabled Godot button still answers a programmatic Pressed, and a
    // scripted run emits exactly that, so the handlers test again.
    static bool CanVote(GuildFlags f, bool supported) => f != null && f.IsVote && !supported;
    static bool CanExile(GuildFlags f, bool isMe, byte rank)
        => f != null && f.IsExile && !(isMe || rank == 5 || (rank == 4 && !f.IsDisband));
    static bool CanSetRank(GuildFlags f, bool isMe) => f != null && f.IsSetRank && !isMe;

    /// <summary>
    /// Whether a change of standing is one the rights allow - the table of
    /// `UIGuild.cpp:762-847`, which GameView sends from. A change that is
    /// not in it sends nothing at all.
    /// </summary>
    static bool CanMove(GuildFlags f, int was, int now)
    {
        if (f == null || was == now) return false;
        if (was == 1 && now == 2) return f.IsDeclareEnemy;
        if (was == 0 && now == 2) return f.IsEndAlliance && f.IsDeclareEnemy;
        if (was == 1 && now == 0) return f.IsMakeAlliance;
        if (was == 2 && now == 0) return f.IsEndEnemy && f.IsMakeAlliance;
        if (was == 2 && now == 1) return f.IsEndEnemy;
        if (was == 0 && now == 1) return f.IsEndAlliance;
        return false;
    }

    static string StandingName(int s) => s == 0 ? "Ally" : (s == 2 ? "Enemy" : "Neutral");

    /// <summary>One other guild, and where the two of you stand.</summary>
    Control GuildRow(GuildInfo info, DiplomacyInfo d, GuildEntry g, int index)
    {
        uint id = g.ID;

        // A striped plate with the same fixed columns the header names,
        // rather than a bare HBox: thirty guilds of one brown give the
        // eye nothing to count down, and a standing that lands wherever
        // the name before it ended cannot be compared with the one above.
        Panel box = Plate(index);
        var line = new HBoxContainer
        {
            Name = $"guild{index}",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        line.OffsetLeft = RowInset; line.OffsetRight = -RowInset;
        line.OffsetTop = 6; line.OffsetBottom = -6;
        box.AddChild(line);

        var name = new Label
        {
            Text = g.Name ?? "(unnamed)",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", M59Skin.Text);
        line.AddChild(name);

        // Theirs toward you, which you cannot change and the reference
        // shows as plain text.
        int theirs = Standing(d, id, false);
        var said = new Label
        {
            Text = StandingName(theirs),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(ColSaid, 0),
            MouseFilter = MouseFilterEnum.Ignore,
            Name = $"theirs{index}",
        };
        said.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        said.AddThemeColorOverride("font_color",
            theirs == 0 ? new Color(0.60f, 0.82f, 0.52f)
          : theirs == 2 ? new Color(0.90f, 0.48f, 0.42f)
                        : M59Skin.TextDim);
        line.AddChild(said);

        // Yours toward them, in the reference's own order: Ally,
        // Neutral, Enemy (`UIGuild.cpp:513-518`).
        int ours = Standing(d, id, true);
        var pick = new OptionButton { Name = $"ours{index}" };
        // An OptionButton IS a Button, so Dress reaches it - which is
        // what gives the dead ones (your own guild, or no rights at all)
        // a disabled face. Most of this list is dead for most ranks, so
        // that is most of the window.
        M59Skin.Dress(pick, M59Skin.Kind.Secondary);
        pick.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize + 2);
        pick.AddItem("Ally", 0);
        pick.AddItem("Neutral", 1);
        pick.AddItem("Enemy", 2);
        pick.Selected = ours;
        pick.CustomMinimumSize = new Vector2(ColPick, M59Skin.TapMin);
        pick.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        // Dead on your own guild, and dead unless you hold at least one
        // of the four rights (`UIGuild.cpp:545-548`).
        GuildFlags f = info.Flags;
        bool any = f != null && (f.IsDeclareEnemy || f.IsEndEnemy
                              || f.IsEndAlliance || f.IsMakeAlliance);
        pick.Disabled = !any || (info.GuildID != null && info.GuildID.ID == id);

        int was = ours;
        pick.ItemSelected += now =>
        {
            // A change the rights do not cover sends nothing
            // (`UIGuild.cpp:281-284`), but the reference then leaves the
            // box showing the choice that did not happen, which is a row
            // that disagrees with the server until the next rebuild. Put
            // it back.
            if (pick.Disabled || !CanMove(info.Flags, was, (int)now))
            {
                pick.Selected = was;
                return;
            }
            Diplomacy?.Invoke(id, was, (int)now);
        };
        line.AddChild(pick);

        return box;
    }

    /// <summary>
    /// One row's background: the skin's stripe, alternating, at the
    /// list's row height. A Panel rather than a Button because nothing
    /// in these two lists is pressed by the row itself - the controls
    /// inside it are.
    /// </summary>
    Panel Plate(int index)
    {
        var box = new Panel { CustomMinimumSize = new Vector2(0, RowTall) };
        box.AddThemeStyleboxOverride("panel", M59Skin.Stripe(index % 2 == 1));
        box.MouseFilter = MouseFilterEnum.Ignore;
        return box;
    }

    Control Row(GuildInfo info, GuildMemberEntry m, GuildMemberEntry me, int index)
    {
        uint id = m.ID;
        bool isMe = id == _avatar;
        GuildFlags f = info.Flags;

        Panel box = Plate(index);
        var line = new HBoxContainer
        {
            Name = $"member{index}",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", M59Skin.GapI);
        line.OffsetLeft = RowInset; line.OffsetRight = -RowInset;
        line.OffsetTop = 6; line.OffsetBottom = -6;
        box.AddChild(line);

        var name = new Label
        {
            Text = m.Name ?? "(unnamed)",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", isMe ? M59Skin.GoldBright : M59Skin.Text);
        line.AddChild(name);

        // Rank. The names are the guild's own, five per gender, and the
        // member's gender picks the column.
        var rank = new OptionButton { Name = $"rank{index}" };
        M59Skin.Dress(rank, M59Skin.Kind.Secondary);
        rank.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize + 2);
        rank.CustomMinimumSize = new Vector2(ColRank, M59Skin.TapMin);
        rank.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        for (byte r = 1; r <= 5; r++) rank.AddItem(RankName(info, m.Gender, r), r);
        rank.Selected = Mathf.Clamp(m.Rank - 1, 0, 4);
        rank.Disabled = !CanSetRank(f, isMe);
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
        // The mark is only ever drawn during a vote: `setSelected(IsVote &&
        // isSupportedMember)` (`UIGuild.cpp:436`).
        var vote = new CheckBox
        {
            ButtonPressed = f != null && f.IsVote && supported,
            Name = $"vote{index}",
            CustomMinimumSize = new Vector2(ColVote, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        vote.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize);
        // TickStyle draws its own box, and a dimmed one for the disabled
        // state - which is most of them, since only a vote in progress
        // offers any of these at all.
        TickStyle.Apply(vote);
        vote.Disabled = !CanVote(f, supported);
        vote.Toggled += on =>
        {
            if (!on || vote.Disabled || !CanVote(info.Flags, supported)
                || info.SupportedMember == null || info.SupportedMember.ID == id) return;
            info.SupportedMember.ID = id;
            Support?.Invoke(id);
            _signature = "";
        };
        line.AddChild(vote);

        var kick = new Button { Text = "Exile", Name = $"exile{index}" };
        // Destructive, and dressed as such - and the disabled face
        // matters more here than the live one: a member may exile
        // nobody, so every row of their roster is a dead button.
        M59Skin.Dress(kick, M59Skin.Kind.Danger);
        kick.AddThemeFontSizeOverride("font_size", M59Skin.SmallSize + 2);
        kick.CustomMinimumSize = new Vector2(ColExile, M59Skin.TapMin);
        kick.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        kick.Disabled = !CanExile(f, isMe, m.Rank);
        kick.Pressed += () =>
        {
            if (kick.Disabled || !CanExile(info.Flags, isMe, m.Rank)) return;
            Exile?.Invoke(id, m.Name ?? "");
        };
        line.AddChild(kick);

        return box;
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
