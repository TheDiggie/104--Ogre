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

    /// <summary>True while the text field has focus and owns the keyboard.</summary>
    public bool Capturing => _entry != null && _entry.Visible;

    /// <summary>Raised with the text the player submitted, already trimmed.</summary>
    public event Action<ChatTransmissionType, string> Submitted;

    RichTextLabel _log;
    LineEdit _entry;
    Button _open;
    int _seen;

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
        _log.SetAnchorsPreset(LayoutPreset.BottomWide);
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
        _entry.SetAnchorsPreset(LayoutPreset.BottomWide);
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _entry.TextSubmitted += OnSubmitted;
        AddChild(_entry);

        _open = new Button { Text = "Say" };
        _open.Pressed += Open;
        AddChild(_open);

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

        float logH = (FontSize + 6) * Lines;
        _log.Position = new Vector2(pad, v.Y - entryH - pad * 2 - logH);
        _log.Size = new Vector2(v.X - pad * 2, logH);
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
        if (t.Length == 0) return;

        // Prefixes match what the original client's chat bar accepts, so
        // muscle memory carries over.
        ChatTransmissionType type = ChatTransmissionType.Normal;
        if (t.StartsWith(":")) { type = ChatTransmissionType.Emote; t = t.Substring(1).TrimStart(); }
        else if (t.StartsWith("!")) { type = ChatTransmissionType.Yell; t = t.Substring(1).TrimStart(); }
        else if (t.StartsWith("^")) { type = ChatTransmissionType.Everyone; t = t.Substring(1).TrimStart(); }
        else if (t.StartsWith("#")) { type = ChatTransmissionType.Guild; t = t.Substring(1).TrimStart(); }

        if (t.Length > 0) Submitted?.Invoke(type, t);
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

        var sb = new System.Text.StringBuilder();
        int from = Math.Max(0, messages.Count - Lines);
        for (int i = from; i < messages.Count; i++)
        {
            string s = messages[i]?.FullString;
            if (string.IsNullOrEmpty(s)) continue;
            // The server's own text can contain [ and ], which BBCode would
            // eat as a tag.
            sb.Append("[color=#").Append(Tint(messages[i].ChatMessageType)).Append(']');
            sb.Append(s.Replace("[", "[lb]"));
            sb.Append("[/color]\n");
        }
        _log.Text = sb.ToString();
    }

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
        _log.Text += $"[color=#8fe08f]{text.Replace("[", "[lb]")}[/color]\n";
    }
}
