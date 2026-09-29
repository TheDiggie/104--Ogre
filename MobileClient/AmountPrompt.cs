using System;
using Godot;

/// <summary>
/// How many of a stack.
///
/// `UIInventory.cpp` draws the line: dropping something that is not
/// stackable sends `ReqDrop(id, 0)` straight away, and dropping a stack
/// opens this instead, prefilled with the whole count
/// (`Amount::ShowValues(dataItem->ID, dataItem->Count)`). OK sends
/// `ReqDrop(id, amount)`. That is the whole of the game's dialog: one
/// box and an OK.
///
/// The box is the same here, with a minus, a plus and an All beside it,
/// because typing a number on a phone to drop four arrows is worse than
/// tapping twice. The value is clamped to the stack, and the prompt
/// never opens for something that is not one.
/// </summary>
public partial class AmountPrompt : Control
{
    [Export] public int FontSize = 18;

    /// <summary>Confirmed: this many of the object it was opened for.</summary>
    public event Action<uint, int> Chosen;

    ColorRect _panel;
    Label _title;
    LineEdit _entry;
    Button _less, _more, _all, _ok, _cancel;

    uint _id;
    int _max = 1;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.03f, 0.03f, 0.05f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", FontSize);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
        AddChild(_title);

        _entry = new LineEdit { Visible = false, Alignment = HorizontalAlignment.Center };
        _entry.AddThemeFontSizeOverride("font_size", FontSize + 8);
        _entry.TextChanged += _ => Clamp(false);
        _entry.TextSubmitted += _ => Accept();
        AddChild(_entry);

        _less = Small("-", () => Nudge(-1));
        _more = Small("+", () => Nudge(+1));
        _all = Small("All", () => { _entry.Text = _max.ToString(); Clamp(true); });
        _ok = Small("OK", Accept);   // the game's button is an OK
        _cancel = Small("Cancel", Close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Small(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>Opens for a stack. Count is what the game prefills.</summary>
    public void Ask(uint id, int count, string name)
    {
        _id = id;
        _max = Math.Max(1, count);
        _entry.Text = _max.ToString();
        _title.Text = string.IsNullOrWhiteSpace(name) ? "How many?" : $"{name} - how many?";
        Show(true);
        // A modal has to be over whatever opened it, and what opened
        // this is the bag - which was added to the same parent later
        // and so draws on top of it.
        GetParent()?.MoveChild(this, -1);
        _entry.GrabFocus();
        _entry.SelectAll();
    }

    public void Close() => Show(false);

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _entry.Visible = on;
        _less.Visible = on; _more.Visible = on; _all.Visible = on;
        _ok.Visible = on; _cancel.Visible = on;
        Layout();
    }

    void Nudge(int by)
    {
        int.TryParse(_entry.Text, out int n);
        _entry.Text = Math.Clamp(n + by, 1, _max).ToString();
        Clamp(true);
    }

    /// <summary>
    /// Keeps the box to a number within the stack. While typing, an
    /// empty box is left alone - clamping it to 1 on every keystroke
    /// makes it impossible to clear and retype.
    /// </summary>
    void Clamp(bool now)
    {
        string t = _entry.Text;
        if (!now && t.Length == 0) return;

        var digits = new System.Text.StringBuilder();
        foreach (char c in t) if (char.IsDigit(c)) digits.Append(c);

        int n = 0;
        if (digits.Length > 0) int.TryParse(digits.ToString(), out n);
        if (now || n > _max)
        {
            n = Math.Clamp(n, 1, _max);
            string fixedText = n.ToString();
            if (fixedText != t) { _entry.Text = fixedText; _entry.CaretColumn = fixedText.Length; }
        }
        else if (digits.Length != t.Length)
        {
            _entry.Text = digits.ToString();
            _entry.CaretColumn = _entry.Text.Length;
        }
    }

    void Accept()
    {
        Clamp(true);
        if (!int.TryParse(_entry.Text, out int n)) return;
        Close();
        Chosen?.Invoke(_id, Math.Clamp(n, 1, _max));
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float w = Mathf.Min(v.X * 0.86f, 460f);
        float h = FontSize * 2.8f;
        float x = (v.X - w) * 0.5f;
        float y = v.Y * 0.3f;

        _panel.Position = new Vector2(x - 16, y - 16);
        _panel.Size = new Vector2(w + 32, h * 3f + 48);

        _title.Position = new Vector2(x, y);
        _title.Size = new Vector2(w, h);

        float gap = 8f;
        float side = h;
        _less.Position = new Vector2(x, y + h + gap);
        _less.Size = new Vector2(side, h);
        _entry.Position = new Vector2(x + side + gap, y + h + gap);
        _entry.Size = new Vector2(w - (side + gap) * 2f - side - gap, h);
        _more.Position = new Vector2(x + w - side - gap - side, y + h + gap);
        _more.Size = new Vector2(side, h);
        _all.Position = new Vector2(x + w - side, y + h + gap);
        _all.Size = new Vector2(side, h);

        float bw = (w - gap) / 2f;
        _ok.Position = new Vector2(x, y + (h + gap) * 2f);
        _ok.Size = new Vector2(bw, h);
        _cancel.Position = new Vector2(x + bw + gap, y + (h + gap) * 2f);
        _cancel.Size = new Vector2(bw, h);
    }
}
