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

    string _shown = "";
    DataController _data;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _fps = Text("-");
        _rtt = Text("-");
        _time = Text("");
        _room = Text("");

        _players = Small("0", () => Players?.Invoke());
        _safety = Small("safety", () => Flip(_data));

        _moods = new[]
        {
            Small(":)", () => Mood?.Invoke(ActionType.Happy)),
            Small(":|", () => Mood?.Invoke(ActionType.Neutral)),
            Small(":(", () => Mood?.Invoke(ActionType.Sad)),
            Small(">:(", () => Mood?.Invoke(ActionType.Angry)),
        };

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Text(string s)
    {
        var l = new Label { Text = s, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", FontSize);
        l.AddThemeColorOverride("font_color", Plain);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        l.AddThemeConstantOverride("outline_size", 4);
        AddChild(l);
        return l;
    }

    Button Small(string s, Action pressed)
    {
        var b = new Button { Text = s, Flat = true };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        if (pressed != null) b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_fps == null) return;
        float y = TopReserve;
        float x = Margin;

        foreach (Label l in new[] { _fps, _rtt, _time, _room })
        {
            l.Position = new Vector2(x, y);
            x += l.Size.X + 14f;
        }

        // The tappable half goes on its own line: a finger needs a
        // bigger box than a word does.
        float y2 = y + FontSize * 1.8f;
        float bx = Margin;
        foreach (Button b in new[] { _players, _safety })
        {
            b.Position = new Vector2(bx, y2);
            b.Size = new Vector2(Mathf.Max(56f, b.Size.X), FontSize * 2.2f);
            bx += b.Size.X + 6f;
        }
        foreach (Button b in _moods)
        {
            b.Position = new Vector2(bx, y2);
            b.Size = new Vector2(46f, FontSize * 2.2f);
            bx += 50f;
        }
    }

    /// <summary>How tall this is, so the next thing down can clear it.</summary>
    public float BlockHeight => FontSize * 1.8f + FontSize * 2.2f + 8f;

    public void Sync(DataController data)
    {
        if (data == null || _fps == null) return;
        _data = data;

        uint tps = data.TPS, rtt = data.RTT;
        int online = data.OnlinePlayers != null ? data.OnlinePlayers.Count : 0;
        bool safetyOff = data.ClientPreferences != null && data.ClientPreferences.IsSafetyOff;
        string room = data.RoomInformation != null ? data.RoomInformation.RoomName : "";
        string clock = data.MeridianTime.ToShortTimeString();

        // The labels are rebuilt only when something in them changed;
        // this runs every frame.
        string now = $"{tps}|{rtt}|{online}|{safetyOff}|{room}|{clock}";
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

        _players.Text = online.ToString();
        _safety.Text = safetyOff ? "safety off" : "safety on";
        _safety.AddThemeColorOverride("font_color", safetyOff ? DarkRed : PaleGreen);

        Layout();
    }

    /// <summary>
    /// Flips safety the way the file does: the preference first, then
    /// the server is told. Wired by the caller because only it has a
    /// client to tell.
    /// </summary>
    public void Flip(DataController data)
    {
        if (data?.ClientPreferences == null) return;
        data.ClientPreferences.IsSafetyOff = !data.ClientPreferences.IsSafetyOff;
        Safety?.Invoke(data.ClientPreferences.IsSafetyOff);
    }
}
