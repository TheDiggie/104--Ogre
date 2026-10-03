using System;
using Godot;
using Meridian59.Common.Constants;
using Meridian59.Common.Enums;
using Meridian59.Data;

/// <summary>
/// The line along the top of the game: how well it is running, what
/// time it is, where you are, how many people are on, your mood and
/// whether your safety is on.
///
/// `UIStatusBar.cpp`. Everything in it is read off `Data` - TPS, RTT,
/// MeridianTime, RoomInformation.RoomName, OnlinePlayers.Count and
/// ClientPreferences.IsSafetyOff - and the only arithmetic in the file
/// is the colouring, which comes from two tables in the library rather
/// than from taste:
///
///   FPSValues: 35 and up is pale green, 25 ok yellow, 15 orange,
///   below that dark red. Greater or equal, in that order.
///   RTTValues: 150ms or under pale green, 300 yellow, 500 orange,
///   worse dark red. Smaller or equal.
///
/// Note it is TPS the FPS field shows, not the renderer's frame rate:
/// BaseClient measures how often its own tick runs, which is what
/// tells you whether the client is keeping up with the server. The
/// renderer's own rate stays on the debug line where it belongs.
///
/// The four mood buttons send ActionType.Happy, Neutral, Sad and
/// Angry, which is all they do. The safety toggle flips
/// ClientPreferences.IsSafetyOff and then tells the server - and which
/// message that is depends on the build: vanilla has a dedicated
/// safety command, and this one, like Server 104, sends the whole
/// preferences word with SendUserCommandSendPreferences. The player
/// count opens the online players window, which this client already
/// has.
///
/// Left out: the UI lock button, which locks the CEGUI windows in
/// place so they cannot be dragged. Nothing here is draggable.
///
/// THE LOOK. Six things were one undifferentiated line of text, and
/// four of them were tappable without looking like it: "0 ms 05:12
/// Somewhere" over "0   safety ...   :)  :|  :(  >:(" read as leftover
/// printf, which is roughly what it was. Half of that is the player's
/// business and half is diagnostics, so the two halves are separated
/// rather than interleaved:
///
///  - the player's half comes first, on one line: where you are and
///    what time it is on a plate of their own, then the controls - who
///    is on, your safety, your mood - as actual buttons in the panels'
///    button family, each a thumb's height.
///  - the diagnostics - tick rate and round trip - drop to a second,
///    smaller, dimmer line underneath. They keep their colour rules,
///    which are the library's, and are the first thing you want when
///    something is wrong and the last thing you want in the way when
///    it is not.
///
/// The plate is the same one the condition bars sit on: this is over
/// the world, and a room name in white text alone is unreadable the
/// moment the floor is pale.
/// </summary>
public partial class StatusBar : Control
{
    [Export] public int FontSize = 15;
    [Export] public float Margin = 12f;
    /// <summary>
    /// Leaves room for whatever owns the top-left corner. The default
    /// clears the avatar panel: its portrait is 72 tall below a 12
    /// margin, and its enchantment icons sit 6 under that at 28 tall,
    /// which lands at 118.
    /// </summary>
    [Export] public float TopReserve = 124f;

    /// <summary>A mood was picked.</summary>
    public event Action<ActionType> Mood;
    /// <summary>Safety was flipped; the caller tells the server.</summary>
    public event Action<bool> Safety;
    /// <summary>The player count was tapped.</summary>
    public event Action Players;

    static readonly Color PaleGreen = new Color(0.60f, 0.98f, 0.60f);
    static readonly Color Yellow    = new Color(1.00f, 1.00f, 0.00f);
    static readonly Color Orange    = new Color(1.00f, 0.65f, 0.00f);
    static readonly Color DarkRed   = new Color(0.55f, 0.00f, 0.00f);
    static readonly Color Plain     = new Color(0.82f, 0.84f, 0.88f);

    Label _fps, _rtt, _time, _room;
    Button _players, _safety;
    Button[] _moods;
    /// <summary>What the room name and the clock sit on. See the class note.</summary>
    Panel _plate;

    /// <summary>
    /// A control's height here. Well over the 44 points a thumb needs,
    /// which the old 33 was not - and these four are pressed mid-fight.
    /// </summary>
    const float CtrlH = 46f;
    /// <summary>Inside the plate, left and right of the text.</summary>
    const float PlatePad = 12f;
    /// <summary>Between two controls; the moods get half of it, being a set.</summary>
    const float Gap = 8f;

    /// <summary>What the row last showed, as values: the comparison used to be an interpolated string per frame.</summary>
    (uint tps, uint rtt, int online, bool safetyOff, bool known, string room, long minute) _shown;
    bool _shownOnce;
    DataController _data;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Added before the labels, because Godot draws siblings in tree
        // order and a plate added after the text it backs covers it.
        _plate = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _plate.AddThemeStyleboxOverride("panel", PlateBox());
        AddChild(_plate);

        // The room is the one thing on this line you look FOR; the
        // clock is the one you glance at. Sized and coloured to say so.
        _room = Text("", FontSize + 4, M59Skin.GoldBright);
        _time = Text("", FontSize, M59Skin.TextDim);
        // The diagnostics line. Smaller, and its colours are set by the
        // library's tables in Sync.
        _fps = Text("-", FontSize - 2, Plain);
        _rtt = Text("-", FontSize - 2, Plain);

        _players = Small("0", () => Players?.Invoke(), M59Skin.Kind.Secondary);
        _safety = Small("safety", () => Flip(_data), M59Skin.Kind.Secondary);

        // The moods are one set of four, so they take one kind of their
        // own - the small gold square the panels use for steppers - and
        // sit tighter to each other than to anything else. Four faces in
        // a row in the SAME dress as the buttons beside them read as
        // eight unrelated buttons.
        _moods = new[]
        {
            Small(":)", () => Mood?.Invoke(ActionType.Happy), M59Skin.Kind.Step),
            Small(":|", () => Mood?.Invoke(ActionType.Neutral), M59Skin.Kind.Step),
            Small(":(", () => Mood?.Invoke(ActionType.Sad), M59Skin.Kind.Step),
            Small(">:(", () => Mood?.Invoke(ActionType.Angry), M59Skin.Kind.Step),
        };

        // One cluster: the room, the clock and the four controls read as
        // one line, so they move and scale together. See M59Hud.
        M59Hud.Register("status", "Status bar", this);
        M59Hud.Changed += Layout;

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree() => M59Hud.Changed -= Layout;


    /// <summary>
    /// Everything the layout store says about this piece, as one value.
    ///
    /// Compared every frame on the cheap entry point below, because a
    /// piece that lays itself out only on an event cannot see a layout
    /// LOADED after it was built, or an editor that changed a piece
    /// without raising Changed - and the failure is silent: the piece
    /// draws itself exactly where it used to be.
    /// </summary>
    static M59Hud.Stamp HudStamp(string id) => M59Hud.StampOf(id);

    M59Hud.Stamp _stamp;

    /// <summary>The player's size for this cluster, inside the model's band.</summary>
    static float HudScale()
    {
        M59Hud.Piece p = M59Hud.Get("status");
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    /// <summary>
    /// The smallest a control a thumb presses may become. Four of these
    /// six are pressed mid-fight, so the scale's floor of 0.7 is not
    /// allowed to take them under it - see where it clamps in Layout.
    /// </summary>
    const float TapFloor = 44f;

    /// <summary>A scaled font size, never small enough to stop being text.</summary>
    static int Pt(int at1, float scale) => Mathf.Max(8, Mathf.RoundToInt(at1 * scale));

    /// <summary>
    /// The size every label and button was last built at. A Godot control
    /// keeps whatever font size it was given, so a scale change has to
    /// re-apply all of them - otherwise the row grows and the text in it
    /// does not.
    /// </summary>
    float _dressedAt;

    void Redress(float s)
    {
        if (Mathf.IsEqualApprox(_dressedAt, s)) return;
        _dressedAt = s;
        _room.AddThemeFontSizeOverride("font_size", Pt(FontSize + 4, s));
        _time.AddThemeFontSizeOverride("font_size", Pt(FontSize, s));
        _fps.AddThemeFontSizeOverride("font_size", Pt(FontSize - 2, s));
        _rtt.AddThemeFontSizeOverride("font_size", Pt(FontSize - 2, s));
        if (_note != null) _note.AddThemeFontSizeOverride("font_size", Pt(FontSize, s));
        _players.AddThemeFontSizeOverride("font_size", Pt(FontSize, s));
        _safety.AddThemeFontSizeOverride("font_size", Pt(FontSize, s));
        foreach (Button b in _moods) b.AddThemeFontSizeOverride("font_size", Pt(FontSize + 3, s));
    }

    /// <summary>
    /// The plate behind the room and the clock. The panels' card colour
    /// with their lit inner edge, which is what makes a rectangle read
    /// as a thing in front of the world; nearly opaque, because a pale
    /// floor shows through anything less.
    /// </summary>
    static StyleBoxFlat PlateBox()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(M59Skin.Card.R, M59Skin.Card.G, M59Skin.Card.B, 0.88f),
            AntiAliasing = true,
        };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)M59Skin.Radius;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 1;
        s.BorderColor = M59Skin.EdgeLit;
        s.ShadowColor = new Color(0, 0, 0, 0.5f);
        s.ShadowSize = 8;
        return s;
    }

    Label Text(string s, int size, Color colour)
    {
        var l = new Label
        {
            Text = s,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        l.AddThemeConstantOverride("outline_size", 4);
        AddChild(l);
        return l;
    }

    Button Small(string s, Action pressed, M59Skin.Kind kind)
    {
        // Not Flat any more. Flat is why these read as text: a control
        // the player is meant to press has to have an edge, and over a
        // world rather than a panel it has to have a fill too.
        var b = new Button { Text = s };
        M59Skin.Dress(b, kind);
        b.AddThemeFontSizeOverride("font_size", kind == M59Skin.Kind.Step ? FontSize + 3 : FontSize);
        if (pressed != null) b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_fps == null) return;

        // THE NATURAL RECT IS MEASURED BEFORE IT IS PLACED, which is the
        // one structural change here: the row used to walk x along as it
        // went, and a piece has to know how wide it is before it can be
        // told where it goes. So the widths are computed first, handed to
        // M59Hud as the rect the designer wants, and the row is laid out
        // from what comes back.
        //
        // Every metric is multiplied by the player's scale - the plate's
        // padding, the gaps, the control height, the mood squares and the
        // font sizes - with a floor of 44 points on anything pressed.
        float s = HudScale();
        Redress(s);
        float ctrlH = Mathf.Max(TapFloor, CtrlH * s);
        float plateP = PlatePad * s, gap = Gap * s;
        float moodW = Mathf.Max(TapFloor, 54f * s);
        float moodGap = 4f * s;
        float split = 14f * s;

        // Widths come from the combined minimum, not from Size: Size is
        // whatever was last assigned and lags a frame behind a text that
        // has just changed, which would put the clock inside the room
        // name on the frame you walk through a door.
        float roomW = string.IsNullOrEmpty(_room.Text) ? 0f : _room.GetCombinedMinimumSize().X;
        float clockW = string.IsNullOrEmpty(_time.Text) ? 0f : _time.GetCombinedMinimumSize().X;
        float inner = roomW + clockW + (roomW > 0f && clockW > 0f ? split : 0f);
        float plateW = inner + plateP * 2f;

        float playersW = Mathf.Max(96f * s, _players.GetCombinedMinimumSize().X + 26f * s);
        float safetyW = Mathf.Max(96f * s, _safety.GetCombinedMinimumSize().X + 26f * s);

        float wide = (inner > 0f ? plateW + gap + 4f * s : 0f)
                   + playersW + gap + safetyW + gap
                   + 6f * s + _moods.Length * (moodW + moodGap);

        Rect2 at = M59Hud.Place("status", new Rect2(Margin, TopReserve, wide, BlockHeight),
                                GetViewportRect().Size);
        float x = at.Position.X, y = at.Position.Y;
        _rowY = y;

        // The player's own choice, separate from the client's: the host
        // still hides the whole control out of the world.
        bool show = M59Hud.Shows("status");
        M59Hud.Dress("status");
        Bottom = show ? at.Position.Y + at.Size.Y : 0f;

        // ---- the player's line ----------------------------------------
        _plate.Visible = show && inner > 0f;
        _plate.Position = new Vector2(x, y);
        _plate.Size = new Vector2(plateW, ctrlH);

        _room.Visible = _time.Visible = show;
        _room.Position = new Vector2(x + plateP, y);
        _room.Size = new Vector2(roomW, ctrlH);
        _time.Position = new Vector2(x + plateP + roomW + (roomW > 0f ? split : 0f), y);
        _time.Size = new Vector2(clockW, ctrlH);

        if (inner > 0f) x += plateW + gap + 4f * s;

        foreach (Button b in new[] { _players, _safety })
        {
            float w = b == _players ? playersW : safetyW;
            b.Visible = show;
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(w, ctrlH);
            x += w + gap;
        }

        // A set, so a wider gap before it and a narrow one inside it.
        x += 6f * s;
        foreach (Button b in _moods)
        {
            b.Visible = show;
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(moodW, ctrlH);
            x += moodW + moodGap;
        }
        _noteX = x + 10f * s;
        if (_note != null) { _note.Visible = _note.Visible && show; if (_note.Visible) PlaceNote(); }

        // ---- the diagnostics line, under it ---------------------------
        float y2 = y + ctrlH + 4f * s;
        float dx = at.Position.X + 2f * s;
        foreach (Label l in new[] { _fps, _rtt })
        {
            l.Visible = show;
            l.Position = new Vector2(dx, y2);
            l.Size = new Vector2(l.GetCombinedMinimumSize().X, DiagH);
            dx += l.Size.X + 14f * s;
        }
    }

    /// <summary>The second line's height: the small print.</summary>
    float DiagH => FontSize * 1.5f * HudScale();

    /// <summary>Where a note would start, right of the last control.</summary>
    float _noteX;

    /// <summary>The row's top, wherever the player left it.</summary>
    float _rowY;

    /// <summary>
    /// The lowest point this row actually occupies on screen, after the
    /// player's own offset and scale. Published for the same reason
    /// Vitals.Bottom is: the buff icons have to clear everything in the
    /// top-left corner, and both the reserve and the scale are things a
    /// player can move. Zero when the row is hidden, so nothing below
    /// leaves a gap for a row that is not drawn.
    /// </summary>
    public float Bottom { get; private set; }

    /// <summary>
    /// How tall this is, so the next thing down can clear it. Scaled with
    /// the piece: the debug line is placed under it and would be drawn
    /// through a scaled-up row otherwise.
    /// </summary>
    public float BlockHeight =>
        Mathf.Max(TapFloor, CtrlH * HudScale()) + 4f * HudScale() + DiagH + 8f * HudScale();

    public void Sync(DataController data)
    {
        if (data == null || _fps == null) return;
        _data = data;

        // The player's layout, before the early-out below: this line
        // changes only when they move something, and the row is not
        // rebuilt for anything else.
        M59Hud.Stamp stamp = HudStamp("status");
        if (stamp != _stamp) { _stamp = stamp; Layout(); }

        uint tps = data.TPS, rtt = data.RTT;
        int online = data.OnlinePlayers != null ? data.OnlinePlayers.Count : 0;
        bool safetyOff = data.ClientPreferences != null && data.ClientPreferences.IsSafetyOff;
        bool known = PrefsKnown(data);
        string room = data.RoomInformation != null ? data.RoomInformation.RoomName : "";
        // The short time string changes with the minute and with nothing
        // else, so the minute is what is compared and the string is made
        // when it moves.
        DateTime when = data.MeridianTime;
        long minute = when.Ticks / TimeSpan.TicksPerMinute;

        // The labels are rebuilt only when something in them changed;
        // this runs every frame.
        var now = (tps, rtt, online, safetyOff, known, room, minute);
        if (_shownOnce && now == _shown) return;
        _shown = now; _shownOnce = true;
        string clock = when.ToShortTimeString();

        _fps.Text = $"{tps} tps";
        _fps.AddThemeColorOverride("font_color",
            tps >= FPSValues.GOOD ? PaleGreen :
            tps >= FPSValues.OK   ? Yellow :
            tps >= FPSValues.BAD  ? Orange : DarkRed);

        _rtt.Text = $"{rtt} ms";
        _rtt.AddThemeColorOverride("font_color",
            rtt <= RTTValues.GOOD ? PaleGreen :
            rtt <= RTTValues.OK   ? Yellow :
            rtt <= RTTValues.BAD  ? Orange : DarkRed);

        _time.Text = clock;
        _room.Text = string.IsNullOrWhiteSpace(room) ? "" : room;

        // The count alone was a bare number with nothing saying what it
        // counted or that it could be pressed. The word costs twelve
        // points and is the whole caption.
        _players.Text = $"{online} online";
        // Before the server's word arrives every flag reads zero, so
        // "safety on" would be a guess dressed as a fact - say so.
        _safety.Text = !known ? "safety ..." : safetyOff ? "safety off" : "safety on";
        // Dress gave the button the family's text colour; the state is
        // worth more than that here, so it is re-applied over the top.
        // DarkRed is unreadable on a dark fill, so the OFF state uses a
        // lit version of the same hue - the panels' refusal colour.
        _safety.AddThemeColorOverride("font_color",
            !known ? M59Skin.TextDim : safetyOff ? new Color(1f, 0.52f, 0.44f) : PaleGreen);

        Layout();
    }

    /// <summary>
    /// Whether the server's preference word has arrived
    /// (UC_RECEIVE_PREFERENCES sets PreferencesFlags.Enabled,
    /// DataController.cs:2784-2786).
    /// </summary>
    static bool PrefsKnown(DataController data) =>
        data?.ClientPreferences != null && data.ClientPreferences.Enabled;

    Label _note;
    int _noteToken;

    void PlaceNote()
    {
        if (_note == null) return;
        _note.Position = new Vector2(_noteX, _rowY);
        _note.Size = new Vector2(_note.GetCombinedMinimumSize().X,
                                 Mathf.Max(TapFloor, CtrlH * HudScale()));
    }

    /// <summary>
    /// Flips safety the way the file does: the preference first, then
    /// the server is told. Wired by the caller because only it has a
    /// client to tell.
    ///
    /// DIVERGES from the reference, on purpose. UIStatusBar.cpp:222-235
    /// (OnSafetyClicked) flips and sends with no check. But the send is
    /// the WHOLE 32-bit word (BaseClient.cs:979-989), and until
    /// UC_RECEIVE_PREFERENCES arrives that word reads zero, so the tap
    /// would send CF_SAFETY_OFF alone and user.kod:2366-2406
    /// (UserCommandSetPreferences) would apply every differing bit -
    /// quietly clearing autoloot, grouping, bags and the rest on the
    /// server, with safety (which stops you hitting innocents,
    /// user.kod:2226-2249) the one thing turned OFF. OptionsPanel gates
    /// its switches on the same flag for the same reason
    /// (OptionsPanel.cs:850). So the tap does nothing until the word is
    /// known - and says why, in the page, rather than being a dead
    /// button.
    /// </summary>
    public void Flip(DataController data)
    {
        if (data?.ClientPreferences == null) return;
        if (!PrefsKnown(data)) { Explain("Still loading your settings from the server - try again in a moment."); return; }
        data.ClientPreferences.IsSafetyOff = !data.ClientPreferences.IsSafetyOff;
        Safety?.Invoke(data.ClientPreferences.IsSafetyOff);
    }

    void Explain(string text)
    {
        if (_note == null)
        {
            _note = Text("", Pt(FontSize, HudScale()), Yellow);
        }
        _note.Text = text;
        // On the controls' own row, past the last of them: the line
        // below belongs to the diagnostics and then to the debug text,
        // and a note drawn over either cannot be read.
        PlaceNote();
        // Shown only if the player has not put this cluster away.
        _note.Visible = M59Hud.Shows("status");
        int token = ++_noteToken;
        GetTree().CreateTimer(6.0).Timeout += () =>
        {
            if (token == _noteToken && _note != null) _note.Visible = false;
        };
    }
}
