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

    /// <summary>
    /// Backed out of, with nothing chosen.
    ///
    /// More than one thing asks this question now - a drop, a trade
    /// line, a shop line - and which one asked is remembered by the
    /// caller while the prompt is up. Cancel used to raise nothing, so
    /// that note was never torn up: the next drop of a stack was
    /// answered into whichever window had asked last, the drop was never
    /// sent, and nothing said so.
    /// </summary>
    public event Action Cancelled;

    ColorRect _shade;
    /// <summary>The card. Named _panel still because IsOpen is read off it.</summary>
    Panel _panel;
    Panel _bar;
    Label _title, _ceiling;
    LineEdit _entry;
    Button _less, _more, _all, _ok, _cancel;

    /// <summary>
    /// The number is what this prompt is FOR, and it is money as often
    /// as it is arrows, so it is read at arm's length rather than
    /// squinted at: three times the client's ordinary text.
    /// </summary>
    int CountSize => FontSize * 3;

    uint _id;
    int _max = 1;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim is what makes the prompt read as being in front of
        // the bag rather than being part of it, and it eats the tap that
        // would otherwise reach the slot underneath.
        _shade = new ColorRect { Color = M59Skin.Scrim, Visible = false, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_shade);

        _panel = M59Skin.Window();
        _panel.Visible = false;
        AddChild(_panel);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("");
        _title.Visible = false;
        AddChild(_title);

        _entry = new LineEdit { Visible = false, Alignment = HorizontalAlignment.Center };
        _entry.AddThemeFontSizeOverride("font_size", CountSize);
        _entry.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        _entry.AddThemeColorOverride("font_selected_color", M59Skin.GoldBright);
        _entry.AddThemeColorOverride("caret_color", M59Skin.Gold);
        // The box opens with the number selected (Ask), so the selection
        // is what the player SEES the number as - the engine's default
        // steel blue over gold is not this client.
        _entry.AddThemeColorOverride("selection_color", new Color(0.33f, 0.27f, 0.14f));
        // M59Skin dresses buttons, not text boxes, so the box is built
        // out of the same palette by hand - see the report.
        _entry.AddThemeStyleboxOverride("normal", Sunk());
        _entry.AddThemeStyleboxOverride("focus", Sunk());
        _entry.AddThemeStyleboxOverride("read_only", Sunk());
        _entry.TextChanged += _ => Clamp(false);
        _entry.TextSubmitted += _ => Accept();
        AddChild(_entry);

        // The ceiling, under the number. Without it the player is
        // tapping + against a limit nobody told them about.
        _ceiling = M59Skin.Body("", true);
        _ceiling.HorizontalAlignment = HorizontalAlignment.Center;
        _ceiling.Visible = false;
        AddChild(_ceiling);

        // Named as well as captioned: the minimap's zoom buttons are
        // also "-" and "+", so a scripted run asking for one by text
        // could reach either.
        // Thumb-sized steppers either side of the number, which is the
        // whole reason this prompt exists rather than a keyboard.
        _less = Small("-", () => Nudge(-1), M59Skin.Kind.Step, "amountLess");
        _more = Small("+", () => Nudge(+1), M59Skin.Kind.Step, "amountMore");
        // One glyph each, on a button a thumb is meant to find without
        // looking: the skin's step size is for a caption, not for a sign.
        _less.AddThemeFontSizeOverride("font_size", FontSize + 14);
        _more.AddThemeFontSizeOverride("font_size", FontSize + 14);
        _all = Small("All", () => { _entry.Text = _max.ToString(); Clamp(true); }, M59Skin.Kind.Step, "amountAll");
        _ok = Small("OK", Accept, M59Skin.Kind.Primary);   // the game's button is an OK
        _cancel = Small("Cancel", Close, M59Skin.Kind.Secondary);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Small(string text, Action pressed, M59Skin.Kind kind, string name = null)
    {
        var b = new Button { Text = text, Visible = false };
        if (name != null) b.Name = name;
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>The number's box: the card's own dark, sunk into it.</summary>
    static StyleBoxFlat Sunk()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.047f, 0.043f, 0.039f),
            BorderColor = M59Skin.Rule,
            AntiAliasing = true,
        };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 8;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 1;
        return s;
    }

    /// <summary>Opens for a stack. Count is what the game prefills.</summary>
    public void Ask(uint id, int count, string name) => Ask(id, count, count, name);

    /// <summary>
    /// Opens for a stack, prefilled with <paramref name="count"/> and
    /// capped at <paramref name="most"/>.
    ///
    /// The two are not the same number once something can ask twice. A
    /// shop line keeps the amount you chose in the line itself, which is
    /// what the game does (`UIBuy.cpp:255`) - so re-opening the prompt
    /// with that as BOTH the value and the ceiling made every choice a
    /// ratchet: three of a hundred, and three was the most you could
    /// ever ask for again. The reference has no clamp at all there; the
    /// stack is the ceiling here, and the value is where you left it.
    /// </summary>
    public void Ask(uint id, int count, int most, string name)
    {
        _id = id;
        _max = Math.Max(1, most);
        _entry.Text = Math.Clamp(Math.Max(1, count), 1, _max).ToString();
        // The item names the card and the question follows it. Not
        // "How many <name>?": the server's names carry their own
        // article ("a gold doubloon"), and the question has to read
        // around that rather than in front of it.
        _title.Text = string.IsNullOrWhiteSpace(name) ? "How many?" : $"{name} - how many?";
        _ceiling.Text = $"of {_max}";
        Show(true);
        // A modal has to be over whatever opened it, and what opened
        // this is the bag - which was added to the same parent later
        // and so draws on top of it.
        GetParent()?.MoveChild(this, -1);
        _entry.GrabFocus();
        _entry.SelectAll();
    }

    /// <summary>
    /// Backs out. Raises Cancelled, so whoever asked can forget that it
    /// did - and only when the prompt was actually up, so closing an
    /// already-closed prompt is not a cancellation.
    /// </summary>
    public void Close()
    {
        bool wasOpen = IsOpen;
        Show(false);
        if (wasOpen) Cancelled?.Invoke();
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _shade.Visible = on; _panel.Visible = on; _bar.Visible = on;
        _title.Visible = on; _entry.Visible = on; _ceiling.Visible = on;
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
        // Show, not Close: accepting is not cancelling, and Close now
        // tells the caller to forget which window asked.
        Show(false);
        Chosen?.Invoke(_id, Math.Clamp(n, 1, _max));
    }

    void Layout()
    {
        if (_panel == null) return;
        if (!IsInsideTree()) return;
        Vector2 v = GetViewportRect().Size;

        _shade.Position = Vector2.Zero;
        _shade.Size = v;

        // A card sized to one number, not to the screen. The height is
        // the stepper row plus the ceiling under it; the width is capped
        // here because M59Skin.Frame has no width argument and its own
        // cap is the one a LIST wants - see the report.
        float step = Mathf.Max(72f, FontSize * 4f);     // a thumb's worth
        float under = Mathf.Max(38f, M59Skin.BodySize * 1.8f);   // All is a tap target too
        float wantH = step + M59Skin.Gap + under;
        Rect2 full = M59Skin.Frame(v, wantH);
        float w = Mathf.Min(full.Size.X, Mathf.Clamp(v.X * 0.36f, 380f, 620f));
        Rect2 card = new Rect2(Mathf.Round((v.X - w) * 0.5f), full.Position.Y,
                               Mathf.Round(w), full.Size.Y);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _panel.Position = card.Position;
        _panel.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        // Minus, the number, plus - in that order across the body, with
        // the number taking everything the two square steppers leave.
        float gap = M59Skin.Gap;
        float num = body.Size.X - (step + gap) * 2f;
        float y = body.Position.Y;
        _less.Position = new Vector2(body.Position.X, y);
        _less.Size = new Vector2(step, step);
        _entry.Position = new Vector2(body.Position.X + step + gap, y);
        _entry.Size = new Vector2(num, step);
        _more.Position = new Vector2(body.Position.X + body.Size.X - step, y);
        _more.Size = new Vector2(step, step);

        // The ceiling sits under the number it caps, and All sits at the
        // end of that line because pressing it IS asking for the ceiling.
        float underY = y + step + gap * 0.5f;
        float underH = under;
        _ceiling.Position = new Vector2(body.Position.X + step + gap, underY);
        _ceiling.Size = new Vector2(num, underH);
        _all.Position = new Vector2(body.Position.X + body.Size.X - step, underY);
        _all.Size = new Vector2(step, underH);

        // Right to left, the house order: Cancel under the thumb, OK -
        // the one thing this prompt is for - away from it.
        M59Skin.FootRow(foot, _cancel, _ok);
    }
}
