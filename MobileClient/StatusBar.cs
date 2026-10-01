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

    string _shown = "";
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

        GetViewport().SizeChanged += Layout;
        Layout();
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
        float y = TopReserve;
        float x = Margin;

        // ---- the player's line ----------------------------------------
        // Widths come from the combined minimum, not from Size: Size is
        // whatever was last assigned and lags a frame behind a text that
        // has just changed, which would put the clock inside the room
        // name on the frame you walk through a door.
        float roomW = string.IsNullOrEmpty(_room.Text) ? 0f : _room.GetCombinedMinimumSize().X;
        float clockW = string.IsNullOrEmpty(_time.Text) ? 0f : _time.GetCombinedMinimumSize().X;
        float inner = roomW + clockW + (roomW > 0f && clockW > 0f ? 14f : 0f);

        _plate.Visible = inner > 0f;
        _plate.Position = new Vector2(x, y);
        _plate.Size = new Vector2(inner + PlatePad * 2f, CtrlH);

        _room.Position = new Vector2(x + PlatePad, y);
        _room.Size = new Vector2(roomW, CtrlH);
        _time.Position = new Vector2(x + PlatePad + roomW + (roomW > 0f ? 14f : 0f), y);
        _time.Size = new Vector2(clockW, CtrlH);

        if (inner > 0f) x += _plate.Size.X + Gap + 4f;

        foreach (Button b in new[] { _players, _safety })
        {
            float w = Mathf.Max(96f, b.GetCombinedMinimumSize().X + 26f);
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(w, CtrlH);
            x += w + Gap;
        }

        // A set, so a wider gap before it and a narrow one inside it.
        x += 6f;
        foreach (Button b in _moods)
        {
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(54f, CtrlH);
            x += 54f + 4f;
        }
        _noteX = x + 10f;
        if (_note != null && _note.Visible) PlaceNote();

        // ---- the diagnostics line, under it ---------------------------
        float y2 = y + CtrlH + 4f;
        float dx = Margin + 2f;
        foreach (Label l in new[] { _fps, _rtt })
        {
            l.Position = new Vector2(dx, y2);
            l.Size = new Vector2(l.GetCombinedMinimumSize().X, DiagH);
            dx += l.Size.X + 14f;
        }
    }

    /// <summary>The second line's height: the small print.</summary>
    float DiagH => FontSize * 1.5f;

    /// <summary>Where a note would start, right of the last control.</summary>
    float _noteX;

    /// <summary>How tall this is, so the next thing down can clear it.</summary>
    public float BlockHeight => CtrlH + 4f + DiagH + 8f;

    public void Sync(DataController data)
    {
        if (data == null || _fps == null) return;
        _data = data;

        uint tps = data.TPS, rtt = data.RTT;
        int online = data.OnlinePlayers != null ? data.OnlinePlayers.Count : 0;
        bool safetyOff = data.ClientPreferences != null && data.ClientPreferences.IsSafetyOff;
        bool known = PrefsKnown(data);
        string room = data.RoomInformation != null ? data.RoomInformation.RoomName : "";
        string clock = data.MeridianTime.ToShortTimeString();

        // The labels are rebuilt only when something in them changed;
        // this runs every frame.
        string now = $"{tps}|{rtt}|{online}|{safetyOff}|{known}|{room}|{clock}";
        if (now == _shown) return;
        _shown = now;

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
        _note.Position = new Vector2(_noteX, TopReserve);
        _note.Size = new Vector2(_note.GetCombinedMinimumSize().X, CtrlH);
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
            _note = Text("", FontSize, Yellow);
        }
        _note.Text = text;
        // On the controls' own row, past the last of them: the line
        // below belongs to the diagnostics and then to the debug text,
        // and a note drawn over either cannot be read.
        PlaceNote();
        _note.Visible = true;
        int token = ++_noteToken;
        GetTree().CreateTimer(6.0).Timeout += () =>
        {
            if (token == _noteToken && _note != null) _note.Visible = false;
        };
    }
}
