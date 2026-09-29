using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;

/// <summary>
/// Chat log and input line for the live view.
///
/// Meridian is a talking game - most of what happens arrives as text -
/// so the view is not usable without this even though it renders fine.
///
/// The log mirrors the client's own ChatMessages list rather than keeping
/// its own copy: the library already caps that list and already resolves
/// the server's string resources and inline variables into FullString.
///
/// The input line is hidden until asked for, because on a phone an always
/// present LineEdit means an always present software keyboard over half
/// the screen. While it is open <see cref="Capturing"/> is true and the
/// view stops reading movement keys, so typing "was" does not walk you
/// into a wall.
/// </summary>
public partial class ChatOverlay : Control
{
    /// <summary>How many lines of history to show.</summary>
    [Export] public int Lines = 8;
    [Export] public int FontSize = 16;

    /// <summary>
    /// How much of the bottom of the screen this occupies, so other
    /// widgets can stay clear of it rather than each guessing.
    /// </summary>
    public float BlockHeight => FontSize * 2.4f + 12f * 2f + (FontSize + 6) * Lines + 12f;

    /// <summary>True while the text field has focus and owns the keyboard.</summary>
    public bool Capturing => _entry != null && _entry.Visible;

    /// <summary>Raised with the text the player submitted, already trimmed.</summary>
    public event Action<string> Submitted;

    RichTextLabel _log;
    LineEdit _entry;
    Button _open, _history;
    int _seen;

    // The full log, behind a button. The corner shows the last few lines
    // because that is what you want while walking; the whole thing is what
    // you want when you missed something, and the library keeps 200.
    ColorRect _fullBack;
    ScrollContainer _fullScroll;
    RichTextLabel _full;
    Button _fullClose;
    readonly List<string> _lines = new List<string>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;      // taps fall through to the view

        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // No anchor preset: Layout() places this explicitly, and an anchor
        // would fight it. The root Control is the anchored one.
        _log.AddThemeFontSizeOverride("normal_font_size", FontSize);
        _log.AddThemeColorOverride("default_color", new Color(1, 1, 1));
        _log.AddThemeConstantOverride("outline_size", 4);
        _log.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        AddChild(_log);

        _entry = new LineEdit
        {
            PlaceholderText = "say something",
            Visible = false,
            CaretBlink = true,
        };
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _entry.TextSubmitted += OnSubmitted;
        AddChild(_entry);

        _open = new Button { Text = "Say" };
        _open.Pressed += Open;
        AddChild(_open);

        _history = new Button { Text = "Log" };
        _history.AddThemeFontSizeOverride("font_size", FontSize);
        _history.Pressed += ShowHistory;
        AddChild(_history);

        _fullBack = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
        _fullBack.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fullBack);

        _full = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
        };
        _full.AddThemeFontSizeOverride("normal_font_size", FontSize);
        _full.SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand;

        _fullScroll = new ScrollContainer { Visible = false };
        _fullScroll.AddChild(_full);
        AddChild(_fullScroll);

        _fullClose = new Button { Text = "Close", Visible = false };
        _fullClose.AddThemeFontSizeOverride("font_size", FontSize);
        _fullClose.Pressed += HideHistory;
        AddChild(_fullClose);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        Vector2 v = GetViewportRect().Size;
        float pad = 12f;
        float entryH = FontSize * 2.4f;
        float btnW = FontSize * 5f;

        _entry.Position = new Vector2(pad, v.Y - entryH - pad);
        _entry.Size = new Vector2(v.X - pad * 2, entryH);

        _open.Position = new Vector2(pad, v.Y - entryH - pad);
        _open.Size = new Vector2(btnW, entryH);

        _history.Position = new Vector2(pad + btnW + 8f, v.Y - entryH - pad);
        _history.Size = new Vector2(btnW, entryH);

        float side = Mathf.Max(16f, v.X * 0.05f);
        _fullScroll.Position = new Vector2(side, side);
        _fullScroll.Size = new Vector2(v.X - side * 2f, v.Y - side * 2f - entryH - 8f);
        _fullClose.Position = new Vector2(side, v.Y - entryH - side * 0.5f);
        _fullClose.Size = new Vector2(v.X - side * 2f, entryH);

        float logH = (FontSize + 6) * Lines;
        _log.Position = new Vector2(pad, v.Y - entryH - pad * 2 - logH);
        _log.Size = new Vector2(v.X - pad * 2, logH);
    }

    /// <summary>True while the full log is covering the screen.</summary>
    public bool ShowingHistory => _fullBack != null && _fullBack.Visible;

    void ShowHistory()
    {
        _full.Text = string.Join("\n", _lines);
        _fullBack.Visible = true; _fullScroll.Visible = true; _fullClose.Visible = true;
        Layout();
        // Newest at the bottom, which is where you were looking. Deferred
        // because the scrollbar does not know its range until the label
        // has been laid out.
        Callable.From(() => _fullScroll.ScrollVertical = (int)_fullScroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    void HideHistory()
    {
        _fullBack.Visible = false; _fullScroll.Visible = false; _fullClose.Visible = false;
    }

    /// <summary>
    /// Opens the entry with something already in it and the caret at the
    /// end - "tell Alice " and then whatever you type. The game starts a
    /// tell by typing the whole command; a phone has no keyboard sitting
    /// there to type it into, so the parts that know a name can fill it
    /// in.
    /// </summary>
    public void Compose(string prefix)
    {
        Open();
        _entry.Text = prefix ?? "";
        _entry.CaretColumn = _entry.Text.Length;
        DisplayServer.VirtualKeyboardShow(_entry.Text);
    }

    public void Open()
    {
        _entry.Visible = true;
        _open.Visible = false;
        _entry.GrabFocus();
        DisplayServer.VirtualKeyboardShow(_entry.Text);
    }

    public void Close()
    {
        _entry.Visible = false;
        _entry.Text = "";
        _open.Visible = true;
        _entry.ReleaseFocus();
        DisplayServer.VirtualKeyboardHide();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (!Capturing && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter))
            {
                Open();
                GetViewport().SetInputAsHandled();
            }
            else if (Capturing && k.Keycode == Key.Escape)
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    void OnSubmitted(string text)
    {
        string t = text.Trim();
        Close();
        if (t.Length > 0) Submitted?.Invoke(t);
    }

    /// <summary>
    /// Redraws the log if the client has new messages. Cheap to call every
    /// frame: it compares counts and does nothing when nothing arrived.
    /// </summary>
    public void Sync(IList<ServerString> messages)
    {
        if (_log == null || messages == null) return;
        if (messages.Count == _seen) return;
        _seen = messages.Count;

        _lines.Clear();
        foreach (ServerString m in messages)
        {
            string line = Markup(m);
            if (line != null) _lines.Add(line);
        }

        int from = Math.Max(0, _lines.Count - Lines);
        _log.Text = string.Join("\n", _lines.GetRange(from, _lines.Count - from));

        if (ShowingHistory) _full.Text = string.Join("\n", _lines);
    }

    /// <summary>
    /// One message as the game renders it: a run of styles over the text,
    /// each with its own colour and weight, rather than one tint for the
    /// whole line.
    ///
    /// The server does not send a coloured string - it sends the text plus
    /// a list of styles, each naming a start, a length, a colour and
    /// whether it is bold, italic or underlined. The Ogre client's
    /// Util::GetChatString walks exactly this list to build its markup,
    /// and a message with no styles at all is drawn plain.
    ///
    /// Public because chat is not the only place the server sends styled
    /// text: quest requirements arrive the same way, and
    /// `UINPCQuestList` draws them through the same `GetChatString`.
    /// </summary>
    public static string Markup(ServerString m)
    {
        string text = m?.FullString;
        if (string.IsNullOrEmpty(text)) return null;

        // Nothing styled: the whole line in the colour its kind gets.
        if (m.Styles == null || m.Styles.Count == 0)
            return $"[color=#{Tint(m.ChatMessageType)}]{Escape(text)}[/color]";

        var sb = new System.Text.StringBuilder();
        foreach (ChatStyle style in m.Styles)
        {
            if (style == null) continue;
            int start = Math.Clamp(style.StartIndex, 0, text.Length);
            int len = Math.Clamp(style.Length, 0, text.Length - start);
            if (len == 0) continue;

            string part = Escape(text.Substring(start, len));

            if (style.IsBold) part = $"[b]{part}[/b]";
            if (style.IsCursive) part = $"[i]{part}[/i]";
            if (style.IsUnderline) part = $"[u]{part}[/u]";
            if (style.IsStrikeout) part = $"[s]{part}[/s]";

            sb.Append($"[color=#{Tint(style.Color)}]{part}[/color]");
        }

        return sb.Length > 0 ? sb.ToString()
                             : $"[color=#{Tint(m.ChatMessageType)}]{Escape(text)}[/color]";
    }

    /// <summary>The server's own text can contain [, which BBCode eats.</summary>
    static string Escape(string s) => s.Replace("[", "[lb]");

    /// <summary>
    /// The chat colours, as the client defines them in Constants.h.
    ///
    /// The six at the top are the ones vanilla has, and they are not the
    /// obvious ones: chat red is 0x800000 and green 0x006400, both dark,
    /// purple is 0x8F26AA. The rest exist only in the flavour Server 104
    /// runs - the same flavour this library is built as - and a client
    /// that only knows the six draws two dozen server colours as white.
    /// </summary>
    static string Tint(ChatColor c) => c switch
    {
        ChatColor.Black => "000000",
        ChatColor.Blue => "0000ff",
        ChatColor.Green => "006400",
        ChatColor.Purple => "8f26aa",
        ChatColor.Red => "800000",
        ChatColor.White => "ffffff",

        ChatColor.Aquamarine => "7fffd4",
        ChatColor.Cyan => "2eeafa",
        ChatColor.Drab => "404000",
        ChatColor.Emerald => "00fa78",
        ChatColor.Fire => "e10000",
        ChatColor.Champagne => "e8d5c3",
        ChatColor.ImperialBlue => "000080",
        ChatColor.Jonquil => "ffb432",
        ChatColor.Lime => "00f000",
        ChatColor.Magenta => "cd00cd",
        ChatColor.Orange => "fa7800",
        ChatColor.Pink => "ff00a6",
        ChatColor.Steel => "004792",
        ChatColor.ToxicGreen => "78fa00",
        ChatColor.OffWhite => "f5f4ef",
        ChatColor.Violet => "800080",
        ChatColor.Golden => "f5cd5a",
        ChatColor.Yellow => "e6e619",
        ChatColor.Bronze => "e6be8a",
        ChatColor.Gray1 => "0a0a0a",
        ChatColor.Gray2 => "141414",
        ChatColor.Gray3 => "1e1e1e",
        ChatColor.Gray4 => "282828",
        ChatColor.Gray5 => "323232",
        ChatColor.Gray6 => "c8c8c8",
        ChatColor.Gray7 => "d2d2d2",
        ChatColor.Gray8 => "dcdcdc",
        ChatColor.Gray9 => "e6e6e6",
        ChatColor.Gray10 => "f0f0f0",
        ChatColor.QuestGreen => "00960f",
        ChatColor.QuestRed => "b41400",
        ChatColor.MercenaryColor => "ffd1b0",

        _ => "ffffff",
    };

    static string Tint(ChatMessageType t) => t switch
    {
        ChatMessageType.SystemMessage => "a0d8ff",
        ChatMessageType.ServerChatMessage => "d8d8c0",
        _ => "ffffff",          // ObjectChatMessage: someone talking
    };

    /// <summary>Adds a line of our own, for status the server did not send.</summary>
    public void Local(string text)
    {
        if (_log == null) return;
        string line = $"[color=#8fe08f]{text.Replace("[", "[lb]")}[/color]";
        _lines.Add(line);
        _log.Text += line + "\n";
    }
}
