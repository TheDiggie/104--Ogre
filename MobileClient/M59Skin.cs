using System;
using Godot;

/// <summary>
/// The look of every panel in this client, in one place.
///
/// WHY THIS EXISTS. Each panel had grown its own chrome: a full-screen
/// <c>ColorRect</c> of its own near-black, a title Label dropped at
/// `side, side`, rows that were bare Buttons, and a Close stretched
/// across the bottom edge. Nothing was wrong with any one of them and
/// the result read as a debug screen - no frame, no title bar, content
/// hugging the top with six hundred pixels of nothing under it, and the
/// most prominent thing on screen being the button that dismisses it.
/// Worse, "near-black" was a different near-black in nearly every file,
/// and several were translucent, so the chat log and the room showed
/// through the text.
///
/// WHAT IT IS NOT. It is not a theme resource and not a Control
/// subclass, because adopting either would mean rebuilding every
/// panel's node tree. It is a bag of styleboxes and three geometry
/// helpers, so a panel adopts it by changing how it BUILDS its chrome
/// and where it PUTS things, and keeps its own logic untouched.
///
/// THE SHAPE. A panel is a centred card over a scrim, never a
/// full-bleed rectangle: a title bar with the name and a round close,
/// a body, and a footer that holds the actions. The card is bounded on
/// both axes so a sideways phone does not stretch a two-column row two
/// thousand pixels wide - the same reasoning as <see cref="Panels.Band"/>,
/// which this replaces for panels that adopt it.
///
/// THE COLOURS are warm rather than blue-black, because everything
/// behind them is torchlight on stone and wood. The gold is the one the
/// panels were already reaching for by hand (1, 0.92, 0.6) with the
/// greys built around it rather than against it.
/// </summary>
public static class M59Skin
{
    // ---- palette ---------------------------------------------------

    /// <summary>
    /// Behind everything, over the world - and CLEAR.
    ///
    /// It was a 72% black wash, on the usual reasoning that a modal
    /// should push its surroundings back. Played on a real phone that
    /// reasoning is wrong: the room is the game, you are standing in it
    /// while you rummage in your bag, and dimming it to near-black to
    /// read an opaque card that needed no help is just taking the game
    /// away. The owner asked for it gone after one look.
    ///
    /// The rectangle itself stays, at zero alpha, because it is also
    /// what stops a tap meant for the panel reaching the world behind
    /// it. Invisible, still in the way - which is the whole job.
    ///
    /// <see cref="ScrimSolid"/> is the other case: a screen with no
    /// world behind it at all.
    /// </summary>
    public static readonly Color Scrim = new Color(0.02f, 0.018f, 0.015f, 0f);
    /// <summary>The card itself.</summary>
    public static readonly Color Card = new Color(0.086f, 0.078f, 0.067f);
    /// <summary>The title bar and footer bands, a shade above the card.</summary>
    public static readonly Color Band = new Color(0.125f, 0.113f, 0.094f);
    /// <summary>A list row.</summary>
    public static readonly Color Row = new Color(0.110f, 0.100f, 0.086f);
    /// <summary>Every other list row, so a long list keeps its place.</summary>
    public static readonly Color RowAlt = new Color(0.137f, 0.125f, 0.106f);
    /// <summary>A row under the finger.</summary>
    public static readonly Color RowHot = new Color(0.180f, 0.161f, 0.129f);
    /// <summary>The chosen row.</summary>
    public static readonly Color RowPick = new Color(0.216f, 0.184f, 0.129f);

    /// <summary>The dark outside edge of the card.</summary>
    public static readonly Color Edge = new Color(0.035f, 0.031f, 0.027f);
    /// <summary>The lit inside edge, which is what makes it read as raised.</summary>
    public static readonly Color EdgeLit = new Color(0.290f, 0.251f, 0.196f);
    /// <summary>A hairline between rows and under the title.</summary>
    public static readonly Color Rule = new Color(0.216f, 0.188f, 0.149f);

    public static readonly Color Gold = new Color(0.910f, 0.753f, 0.416f);
    public static readonly Color GoldBright = new Color(1.000f, 0.871f, 0.608f);
    public static readonly Color GoldDim = new Color(0.549f, 0.459f, 0.267f);
    /// <summary>
    /// The background of an inventory slot whose item is in use
    /// (worn, wielded, lit). Ashton: "make equip items have a yellow
    /// background for easy visual confirmation" - the game itself glows
    /// the background of an item in use (the composer turns its
    /// background on for exactly that), so this is the same mark, said
    /// loud enough to find on a phone. Translucent so the icon still
    /// reads over it.
    /// </summary>
    public static readonly Color InUseBg = new Color(0.85f, 0.70f, 0.12f, 0.55f);
    public static readonly Color InUseEdge = new Color(1f, 0.85f, 0.30f);

    public static readonly Color Text = new Color(0.902f, 0.871f, 0.824f);
    public static readonly Color TextDim = new Color(0.604f, 0.565f, 0.514f);
    public static readonly Color TextOff = new Color(0.400f, 0.376f, 0.345f);
    /// <summary>Destructive and refusals. Not the target red, which is the ruling's.</summary>
    public static readonly Color Danger = new Color(0.710f, 0.278f, 0.220f);

    // ---- metrics ---------------------------------------------------

    public const float TitleH = 58f;
    public const float FootH = 68f;
    /// <summary>Inside the card, on every edge.</summary>
    public const float Pad = 18f;

    /// <summary>
    /// The title bar's close button, square.
    ///
    /// 34 for a long time, which is a mouse's number: it is the size the
    /// glyph wanted, not the size a thumb needs, and the UI layer is
    /// scaled a further ~0.95 inside the safe area, so it reached the
    /// glass at about 32. Apple asks 44 points and Material 48 density
    /// pixels, and the control that DISMISSES something is the one a
    /// player reaches for in a hurry. The title bar is 58 tall, so 44
    /// still sits centred in it with room on both sides.
    ///
    /// It lives here rather than in each panel because it was written
    /// out by hand in twenty of them, which is twenty places for the
    /// next change to miss one.
    /// </summary>
    public const float CloseSize = 44f;

    /// <summary>
    /// The smallest a control that is PRESSED may be, on either axis.
    ///
    /// Apple asks 44 points, Material 48 density pixels. Several things
    /// here were written as RowH minus a margin, which came to 40 - near
    /// enough to look right in a screenshot and not near enough for a
    /// thumb while the other hand is holding the phone. The research is
    /// clearer about separation than about size above about 40, so the
    /// gaps are left alone; it is the targets that move.
    /// </summary>
    public const float TapMin = 44f;

    /// <summary>
    /// How wide a card holding a LIST of label-and-value rows should be.
    ///
    /// <see cref="Frame"/>'s default is the widest a card may ever be,
    /// which on a landscape phone works out at about five sixths of the
    /// screen. That is right for a grid - the pack, a roster - and wrong
    /// for a column of short rows: a book with two spells in it became a
    /// sixteen-hundred-point window with a name at one end of each row
    /// and a number at the other, and the eye has to travel the whole
    /// way to pair them up. Typography's own answer is a measure, which
    /// is what <see cref="Measure"/> is; this is that plus the card's
    /// padding, so the ROW is a measure wide rather than the card.
    /// </summary>
    public const float ListW = Measure + Pad * 2f;
    /// <summary>Between two things that belong together.</summary>
    public const float Gap = 10f;
    /// <summary>A list row. Comfortably over the 44px a thumb needs.</summary>
    public const float RowH = 56f;
    public const float Radius = 10f;
    /// <summary>Title text, body text, and the small print.</summary>
    public const int TitleSize = 26, BodySize = 20, SmallSize = 16;

    // ---- geometry --------------------------------------------------

    /// <summary>
    /// Where the card goes, given the screen.
    ///
    /// Bounded on both axes and centred. The width cap is the reason
    /// this exists: a sideways phone is over 2300 points wide and a row
    /// with a name at one end and a number at the other is unreadable
    /// across it. The height cap keeps a short panel from becoming a
    /// tall empty box - pass the content height and a panel with three
    /// rows in it is three rows tall.
    /// </summary>
    /// <param name="v">Viewport size.</param>
    /// <param name="wantH">
    /// Content height the panel would like, excluding the title bar and
    /// footer. Zero or less means "as tall as allowed".
    /// </param>
    /// <param name="foot">Whether a footer band is wanted.</param>
    /// <param name="wantW">
    /// Width the panel would like. Zero means "as wide as allowed",
    /// which is what a list wants. A PROMPT does not: four words of
    /// question in a 1620-point card is a sentence lost in a field, so
    /// every prompt asked for this and four of them grew the same three
    /// lines of local arithmetic before it existed.
    /// </param>
    public static Rect2 Frame(Vector2 v, float wantH = 0f, bool foot = true, float wantW = 0f)
    {
        float margin = Mathf.Max(16f, Mathf.Min(v.X, v.Y) * 0.04f);
        float maxW = Mathf.Min(v.X - margin * 2f, Mathf.Max(820f, v.Y * 1.5f));
        if (wantW > 0f) maxW = Mathf.Min(maxW, Mathf.Max(360f, wantW));
        float maxH = v.Y - margin * 2f;

        float chrome = TitleH + (foot ? FootH : 0f);
        float h = wantH > 0f ? Mathf.Min(maxH, wantH + chrome + Pad * 2f) : maxH;
        // Never so short that the chrome is the whole window.
        h = Mathf.Max(h, chrome + RowH + Pad * 2f);
        h = Mathf.Min(h, maxH);

        return new Rect2(Mathf.Round((v.X - maxW) * 0.5f), Mathf.Round((v.Y - h) * 0.5f),
                         Mathf.Round(maxW), Mathf.Round(h));
    }

    /// <summary>The body rectangle inside a card: under the title, over the footer.</summary>
    public static Rect2 Body(Rect2 card, bool foot = true)
        => new Rect2(card.Position.X + Pad,
                     card.Position.Y + TitleH + Pad,
                     card.Size.X - Pad * 2f,
                     card.Size.Y - TitleH - (foot ? FootH : 0f) - Pad * 2f);

    /// <summary>The footer band of a card, inset by the padding.</summary>
    public static Rect2 Foot(Rect2 card)
        => new Rect2(card.Position.X + Pad,
                     card.Position.Y + card.Size.Y - FootH + (FootH - 48f) * 0.5f,
                     card.Size.X - Pad * 2f, 48f);

    // ---- pieces ----------------------------------------------------

    static StyleBoxFlat Flat(Color bg, float radius = 0f)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        if (radius > 0f)
        {
            s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
            s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)radius;
        }
        return s;
    }

    /// <summary>
    /// The card's own background: an outer dark edge and an inner lit
    /// one, which is the whole trick that makes a flat rectangle read as
    /// a window rather than a hole.
    /// </summary>
    public static Panel Window()
    {
        var s = Flat(Card, Radius);
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 2;
        s.BorderColor = EdgeLit;
        s.ShadowColor = new Color(0, 0, 0, 0.55f);
        s.ShadowSize = 18;
        s.AntiAliasing = true;
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    /// <summary>The title band, square at the bottom so it meets the body.</summary>
    public static Panel TitleBar()
    {
        var s = Flat(Band);
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight = (int)Radius - 1;
        s.BorderWidthBottom = 1;
        s.BorderColor = Rule;
        s.AntiAliasing = true;
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    /// <summary>A hairline, for under a title or between sections.</summary>
    public static ColorRect Hairline() => new ColorRect { Color = Rule };

    /// <summary>The panel's name, in the title bar.</summary>
    public static Label Title(string text = "")
    {
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", TitleSize);
        l.AddThemeColorOverride("font_color", GoldBright);
        return l;
    }

    /// <summary>Body text.</summary>
    public static Label Body(string text = "", bool dim = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", BodySize);
        l.AddThemeColorOverride("font_color", dim ? TextDim : Text);
        return l;
    }

    // ---- buttons ---------------------------------------------------

    public enum Kind
    {
        /// <summary>The one thing the panel is for. One per panel, at most.</summary>
        Primary,
        /// <summary>Everything else in the footer.</summary>
        Secondary,
        /// <summary>Destroys something.</summary>
        Danger,
        /// <summary>A list row.</summary>
        Row,
        /// <summary>A list row, the other stripe.</summary>
        RowAlt,
        /// <summary>An inventory slot.</summary>
        Slot,
        /// <summary>A tab across the top of a body.</summary>
        Tab,
        /// <summary>The round close in the title bar.</summary>
        Close,
        /// <summary>A small square stepper: +, -, a count.</summary>
        Step,
    }

    static void Style(Button b, string which, StyleBoxFlat s) => b.AddThemeStyleboxOverride(which, s);

    /// <summary>
    /// Gives a button the house look. Call once, after the button
    /// exists; nothing here depends on its size, so a later Layout is
    /// free to move it.
    /// </summary>
    /// <param name="keep">
    /// A colour the caller owns - a quest row's type colour, a player's
    /// name colour - which Dress must not overwrite. Without it every
    /// data-coloured list has to re-apply its colours after both Dress
    /// and Pick, and three of them did.
    /// </param>
    public static Button Dress(Button b, Kind kind, Color? keep = null)
    {
        if (b == null) return null;

        Color fill, hot, down, line, text;
        float radius = 8f;
        int border = 1;
        int size = BodySize;

        switch (kind)
        {
            case Kind.Primary:
                fill = new Color(0.286f, 0.231f, 0.129f); hot = new Color(0.357f, 0.286f, 0.157f);
                down = new Color(0.227f, 0.184f, 0.102f); line = Gold; text = GoldBright;
                break;
            case Kind.Danger:
                fill = new Color(0.255f, 0.110f, 0.090f); hot = new Color(0.310f, 0.137f, 0.110f);
                down = new Color(0.200f, 0.086f, 0.071f); line = Danger; text = new Color(1f, 0.78f, 0.72f);
                break;
            case Kind.Row:
            case Kind.RowAlt:
                fill = kind == Kind.Row ? Row : RowAlt; hot = RowHot; down = RowPick;
                line = new Color(0, 0, 0, 0); text = Text; radius = 6f; border = 0;
                break;
            case Kind.Slot:
                fill = new Color(0.078f, 0.071f, 0.063f); hot = new Color(0.137f, 0.125f, 0.106f);
                down = RowPick; line = Rule; text = Text; radius = 6f;
                break;
            case Kind.Tab:
                fill = new Color(0.098f, 0.090f, 0.078f); hot = new Color(0.149f, 0.133f, 0.110f);
                down = new Color(0.216f, 0.184f, 0.129f); line = Rule; text = TextDim;
                radius = 6f;
                break;
            case Kind.Close:
                fill = new Color(0.173f, 0.153f, 0.125f); hot = new Color(0.400f, 0.188f, 0.149f);
                down = new Color(0.310f, 0.137f, 0.110f); line = Rule; text = Text;
                radius = 16f; size = TitleSize - 4;
                break;
            case Kind.Step:
                fill = new Color(0.157f, 0.141f, 0.118f); hot = new Color(0.216f, 0.192f, 0.157f);
                down = new Color(0.267f, 0.224f, 0.157f); line = Rule; text = GoldBright;
                radius = 6f; size = BodySize + 2;
                break;
            default: // Secondary
                fill = new Color(0.153f, 0.141f, 0.122f); hot = new Color(0.204f, 0.188f, 0.161f);
                down = new Color(0.118f, 0.110f, 0.094f); line = Rule; text = Text;
                break;
        }

        StyleBoxFlat Make(Color c)
        {
            var s = Flat(c, radius);
            if (border > 0)
            {
                s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = border;
                s.BorderColor = line;
            }
            s.AntiAliasing = true;
            return s;
        }

        Style(b, "normal", Make(fill));
        Style(b, "hover", Make(hot));
        Style(b, "pressed", Make(down));
        Style(b, "focus", Make(hot));

        var off = Make(new Color(fill.R * 0.7f, fill.G * 0.7f, fill.B * 0.7f));
        off.BorderColor = new Color(line.R, line.G, line.B, line.A * 0.4f);
        Style(b, "disabled", off);

        if (keep.HasValue) text = keep.Value;
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", text);
        b.AddThemeColorOverride("font_hover_color", kind == Kind.Row || kind == Kind.RowAlt ? GoldBright : text);
        b.AddThemeColorOverride("font_pressed_color", GoldBright);
        b.AddThemeColorOverride("font_focus_color", text);
        b.AddThemeColorOverride("font_disabled_color", TextOff);
        return b;
    }

    /// <summary>
    /// Marks a TOGGLE's on state.
    ///
    /// <see cref="Dress"/>'s pressed fill is a shade DARKER than its
    /// normal one, which is right for a button you are holding down and
    /// wrong for one that stays down: autorun latched on looked almost
    /// exactly like autorun off. A toggle that is on says so in gold,
    /// the way a chosen row and an active tab do.
    /// </summary>
    public static void Latch(Button b)
    {
        if (b == null) return;
        var s = Flat(new Color(0.286f, 0.231f, 0.129f), 8f);
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 2;
        s.BorderColor = Gold;
        s.AntiAliasing = true;
        Style(b, "pressed", s);
        Style(b, "hover_pressed", s);
        b.AddThemeColorOverride("font_pressed_color", GoldBright);
    }

    /// <summary>
    /// Marks a row as the chosen one: the fill the pressed state uses,
    /// plus a gold edge down the left, which is what tells you which row
    /// you are looking at when the fill alone is a shade of brown.
    /// </summary>
    public static void Pick(Button b, bool on, bool alt = false)
    {
        if (b == null) return;
        var s = Flat(on ? RowPick : (alt ? RowAlt : Row), 6f);
        if (on)
        {
            s.BorderWidthLeft = 4;
            s.BorderColor = Gold;
        }
        s.AntiAliasing = true;
        Style(b, "normal", s);
        // A row that is a CheckBox draws its PRESSED box while it is
        // ticked, so overriding only `normal` left a ticked row looking
        // exactly like an unticked one - which is the bug this whole
        // method exists to prevent. Two panels grew the same four-line
        // workaround before this line did.
        Style(b, "pressed", s);
        Style(b, "hover_pressed", s);
        b.AddThemeColorOverride("font_color", on ? GoldBright : Text);
    }

    /// <summary>
    /// The same stripe as a row, for something that is READ rather than
    /// pressed - an attribute, a heading, a line of a table. Dress only
    /// takes a Button; this takes the stylebox to a Panel.
    /// </summary>
    public static StyleBoxFlat Stripe(bool alt)
    {
        var s = Flat(alt ? RowAlt : Row, 6f);
        s.AntiAliasing = true;
        return s;
    }

    /// <summary>
    /// Marks a tab as the active one. Same idea as <see cref="Pick"/>,
    /// said in the idiom of a tab: the mark is along the BOTTOM, where a
    /// tab joins the thing it reveals, rather than down the left, which
    /// is a list idiom.
    /// </summary>
    public static void Tab(Button b, bool active)
    {
        if (b == null) return;
        var s = Flat(active ? new Color(0.216f, 0.184f, 0.129f) : new Color(0.098f, 0.090f, 0.078f), 6f);
        s.BorderWidthBottom = active ? 3 : 1;
        s.BorderColor = active ? Gold : Rule;
        s.AntiAliasing = true;
        Style(b, "normal", s);
        b.AddThemeColorOverride("font_color", active ? GoldBright : TextDim);
    }

    /// <summary>
    /// A sunken surface: a page to read, or a box to type in. Everything
    /// else in here is raised, and a reading surface that is raised
    /// reads as another button.
    /// </summary>
    public static StyleBoxFlat Sunken()
    {
        var s = Flat(new Color(0.055f, 0.051f, 0.045f), 8f);
        s.BorderWidthTop = 2;
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthBottom = 1;
        s.BorderColor = new Color(0.035f, 0.031f, 0.027f);
        s.ContentMarginLeft = s.ContentMarginRight = 12;
        s.ContentMarginTop = s.ContentMarginBottom = 8;
        s.AntiAliasing = true;
        return s;
    }

    /// <summary>Gives a text box the sunken look and the right colours.</summary>
    public static T Field<T>(T box) where T : Control
    {
        if (box == null) return null;
        box.AddThemeStyleboxOverride("normal", Sunken());
        var hot = Sunken(); hot.BorderColor = GoldDim;
        box.AddThemeStyleboxOverride("focus", hot);
        box.AddThemeStyleboxOverride("read_only", Sunken());
        box.AddThemeFontSizeOverride("font_size", BodySize);
        box.AddThemeColorOverride("font_color", Text);
        box.AddThemeColorOverride("font_placeholder_color", TextOff);
        box.AddThemeColorOverride("caret_color", Gold);
        box.AddThemeColorOverride("selection_color", new Color(0.35f, 0.29f, 0.16f));
        return box;
    }

    /// <summary>The small gold label over a field or a block of text.</summary>
    public static Label Caption(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", SmallSize);
        l.AddThemeColorOverride("font_color", GoldDim);
        return l;
    }

    /// <summary>A section heading inside a body: gold, with a rule under it.</summary>
    public static Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", BodySize);
        l.AddThemeColorOverride("font_color", Gold);
        return l;
    }

    /// <summary>
    /// A progress bar that is not two grey slabs. Godot's default is
    /// exactly that, which is what made six attributes read as six
    /// smudges.
    /// </summary>
    public static ProgressBar Bar(Color? fill = null)
    {
        var trough = Flat(new Color(0.055f, 0.051f, 0.045f), 4f);
        trough.BorderWidthTop = trough.BorderWidthBottom =
        trough.BorderWidthLeft = trough.BorderWidthRight = 1;
        trough.BorderColor = Rule;
        trough.AntiAliasing = true;

        var full = Flat(fill ?? Gold, 4f);
        full.AntiAliasing = true;

        var b = new ProgressBar { ShowPercentage = false };
        b.AddThemeStyleboxOverride("background", trough);
        b.AddThemeStyleboxOverride("fill", full);
        return b;
    }

    /// <summary>
    /// How wide a line of prose should be allowed to get. A letter, a
    /// news story or a quest description running the full width of a
    /// sideways phone is a line nobody can follow back to the start.
    /// </summary>
    public const float Measure = 760f;

    /// <summary>Gap, for the theme constants that insist on an int.</summary>
    public const int GapI = (int)Gap;

    /// <summary>The round close for a title bar. Caller positions it.</summary>
    public static Button CloseX(Action pressed)
    {
        var b = new Button { Text = "✕", TooltipText = "Close" };
        Dress(b, Kind.Close);
        if (pressed != null) b.Pressed += pressed;
        return b;
    }

    /// <summary>
    /// Lays a row of footer buttons out from the RIGHT edge, which is
    /// where the last one wants to be: on a phone the thumb that
    /// dismisses a panel is on the side it came from, and the primary
    /// action reads last in the line. Returns the left edge reached, so
    /// a caller can put something else beside them.
    /// </summary>
    /// <summary>
    /// Puts one button at the LEFT end of a footer, away from the row
    /// of them on the right.
    ///
    /// For an action that is destructive and cannot be undone. Drop is
    /// the case this exists for: it sat in a row with Use, Look and
    /// Hotbar, all four the same size and a thumb's width apart, and a
    /// miss by one button throws away whatever the player was holding.
    /// Separation is the only thing that helps - the guides are clear
    /// that a destructive control belongs away from the ones next to
    /// it, and a confirm on every drop would be worse, because a prompt
    /// answered fifty times a session stops being read.
    /// </summary>
    public static void FootLeft(Rect2 foot, Button b)
    {
        if (b == null) return;
        float w = Mathf.Max(110f, b.Text.Length * 11f + 44f);
        b.Position = foot.Position;
        b.Size = new Vector2(w, foot.Size.Y);
    }

    public static float FootRow(Rect2 foot, params Button[] rightToLeft)
    {
        float x = foot.Position.X + foot.Size.X;
        foreach (Button b in rightToLeft)
        {
            if (b == null || !b.Visible) continue;
            float w = Mathf.Max(110f, b.Text.Length * 11f + 44f);
            x -= w;
            b.Position = new Vector2(x, foot.Position.Y);
            b.Size = new Vector2(w, foot.Size.Y);
            x -= Gap;
        }
        return x;
    }

    /// <summary>
    /// A vertical scrollbar's width, and the room a list leaves for it.
    ///
    /// It was 16 and the theme drew something thinner inside that, hard
    /// against the right edge of the body - which is where every row's
    /// bind button also is. A thumb aimed at the bar hit the "+" and
    /// bound a spell. 28 is still not a thumb, deliberately: the bar is
    /// a position indicator you CAN grab, not the way you are expected
    /// to scroll, which is dragging the list itself (see TouchScroll).
    /// The separation is what the research is actually about, so the
    /// rows are inset by this plus a gap rather than the bar being
    /// widened until it collides with something else.
    /// </summary>
    public const float ScrollBarW = 52f;

    /// <summary>
    /// How much of that width is empty space on the bar's LEFT, between
    /// the rows and the part of the bar you can see.
    ///
    /// This is where the separation has to live, and the first attempt
    /// put it in the wrong place. Narrowing the rows does nothing: a
    /// ScrollContainer lays its child out at its own width less the
    /// bar's, so a CustomMinimumSize on the row box is a FLOOR and not a
    /// width, and the content goes on ending exactly where the bar
    /// begins however small that minimum is. Measured in a frame after
    /// the first try: about seven points between the Inspect button and
    /// the grabber, which is nothing.
    ///
    /// So the bar is made wide and most of that width is given away as
    /// margin. What the player sees is a 16-point grabber sitting 30
    /// points clear of the last button on every row; what the finger
    /// gets is the whole 52, because the margin is inside the control
    /// and still takes the press. That is the right way round: the
    /// target stays large while the two targets move apart. 44pt (Apple)
    /// and 48dp (Material) say how big a target must be and say nothing
    /// about how far apart two of them have to be before a thumb can
    /// choose, and the thumb reaching for "+" was the thumb landing on
    /// the bar.
    /// </summary>
    public const float BarInset = 30f;

    /// <summary>
    /// Dresses a list's vertical scrollbar: a visible track, a grabber
    /// wide enough to see and long enough to grab, and the width above.
    ///
    /// A grabber whose length is proportional all the way down becomes a
    /// few points tall on a long list, which is the one case where you
    /// most want to drag it. Godot has no minimum, so the grabber gets
    /// vertical margins instead of being allowed to vanish.
    /// </summary>
    public static void Scroller(ScrollContainer sc)
    {
        if (sc == null) return;
        VScrollBar bar = sc.GetVScrollBar();
        if (bar == null) return;
        bar.CustomMinimumSize = new Vector2(ScrollBarW, 0f);

        // The visible bar is what is left after the inset; the control
        // keeps the whole width, so the dead margin is still a press
        // that scrolls rather than a press that falls through to the row
        // behind it.
        float seen = ScrollBarW - BarInset;

        // ExpandMargin, NEGATIVE, and not ContentMargin: a content
        // margin tells a stylebox where its CHILD content goes, and a
        // scrollbar has no child, so setting it changed nothing at all -
        // measured in a frame, the gold ran the full 52 points and still
        // sat flush against the Inspect buttons. A negative expand
        // margin is what shrinks the box that actually gets drawn.
        var track = Flat(new Color(0f, 0f, 0f, 0.28f), seen * 0.5f);
        track.ExpandMarginLeft = -BarInset;
        bar.AddThemeStyleboxOverride("scroll", track);
        bar.AddThemeStyleboxOverride("scroll_focus", track);

        var grab = Flat(GoldDim, seen * 0.5f);
        grab.ExpandMarginLeft = -BarInset;
        bar.AddThemeStyleboxOverride("grabber", grab);
        var hot = Flat(Gold, seen * 0.5f);
        hot.ExpandMarginLeft = -BarInset;
        bar.AddThemeStyleboxOverride("grabber_highlight", hot);
        bar.AddThemeStyleboxOverride("grabber_pressed", hot);
    }

    /// <summary>
    /// The slim bar on a grid that sits OVER THE WORLD rather than in a
    /// panel - the inventory dock - and what it is allowed to take.
    ///
    /// Scroller above is 52 wide because its bar shares a right edge
    /// with every row's bind button and the separation is the point.
    /// The dock has no button beside its bar - the last column of
    /// slots is a hand's width of empty square - so it does not need
    /// the inset, and a 52-point bar on a piece a player has squeezed
    /// to the corner of the glass would be wider than the gutter the
    /// piece sits in. 28 is what the thumb gets: above the 24 the
    /// brief set as the floor, and the same number Scroller's own
    /// comment calls "still not a thumb, deliberately" - the bar is an
    /// indicator you CAN grab, the way you scroll is dragging the grid.
    /// </summary>
    public const float SlimBarW = 28f;

    /// <summary>
    /// Dresses a grid's vertical scrollbar at the slim width: the track
    /// and grabber drawn 16 wide inside a 28-point press, and the
    /// grabber never shorter than a thumb.
    ///
    /// The minimum is the one thing Scroller promises and does not
    /// keep: Godot sizes a grabber in proportion to the content, with
    /// no floor of its own - the floor it does respect is the grabber
    /// stylebox's MINIMUM size, which for a flat box is its content
    /// margins. So the grabber gets 22 points of margin top and bottom
    /// and can never draw shorter than 44, a thumb (Apple's 44pt), even
    /// when the box is two rows of a hundred-item pack.
    /// </summary>
    public static void SlimScroller(ScrollContainer sc)
    {
        if (sc == null) return;
        VScrollBar bar = sc.GetVScrollBar();
        if (bar == null) return;
        bar.CustomMinimumSize = new Vector2(SlimBarW, 0f);

        const float seen = 16f;
        float side = (SlimBarW - seen) * 0.5f;
        var track = Flat(new Color(0f, 0f, 0f, 0.45f), seen * 0.5f);
        track.ExpandMarginLeft = -side; track.ExpandMarginRight = -side;
        bar.AddThemeStyleboxOverride("scroll", track);
        bar.AddThemeStyleboxOverride("scroll_focus", track);

        StyleBoxFlat Grab(Color c)
        {
            var g = Flat(c, seen * 0.5f);
            g.ExpandMarginLeft = -side; g.ExpandMarginRight = -side;
            g.ContentMarginTop = 22f; g.ContentMarginBottom = 22f;
            return g;
        }
        bar.AddThemeStyleboxOverride("grabber", Grab(GoldDim));
        bar.AddThemeStyleboxOverride("grabber_highlight", Grab(Gold));
        bar.AddThemeStyleboxOverride("grabber_pressed", Grab(Gold));
    }

    /// <summary>
    /// How much of a body a list's rows may use: everything but the bar.
    ///
    /// A FLOOR, not a width - see BarInset. A ScrollContainer sizes its
    /// child to its own width less the bar's whatever this says, so the
    /// clear space between a row's last button and the grabber is the
    /// bar's own left margin and not this number. This is still worth
    /// setting, because a row box narrower than the container would
    /// otherwise shrink to its longest line and every value column would
    /// land wherever that row's text ended.
    /// </summary>
    public static float RowsW(Rect2 body) => Mathf.Max(120f, body.Size.X - ScrollBarW);

    /// <summary>
    /// Sizes a list's row box so it stops short of the scrollbar, and
    /// MEANS it.
    ///
    /// CustomMinimumSize alone does not, which is the trap this exists
    /// to close. A ScrollContainer fits its child with the child's own
    /// size flags, and every row box in this client is ExpandFill - it
    /// has to be, or a VBox shrinks to its longest line and every value
    /// column lands wherever that row's text ended. Expanding, the child
    /// takes the container's whole width and draws underneath the bar,
    /// so the minimum is a floor the layout never reaches and the gap
    /// asked for never appears. Photographed twice before it was
    /// believed: the Inspect buttons ended exactly where the grabber
    /// began, both before and after the number was raised.
    ///
    /// ShrinkBegin with an exact minimum is the fix: the box is given
    /// that width, at the left, and its own children still fill it. The
    /// two calls belong together, so they live here rather than in
    /// sixteen Layout methods that each remembered one of them.
    /// </summary>
    public static void RowsFit(Control rows, Rect2 body)
    {
        if (rows == null) return;
        rows.CustomMinimumSize = new Vector2(RowsW(body), rows.CustomMinimumSize.Y);
        rows.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
    }

    /// <summary>
    /// The Inspect column on an object row. Wider than TapMin because the
    /// word has to fit inside it as well as the thumb - "Inspect" at
    /// SmallSize is around seventy points of glyphs - and fixed so the
    /// button's edge is in the same place on every row. The number is
    /// CreateCharacter's, where the column was first built.
    /// </summary>
    public const float InspectW = 104f;

    /// <summary>
    /// How much of a row the NAME keeps once Inspect sits on the end of
    /// it. Past this the name is cut with an ellipsis rather than pushing
    /// the count, the price and the button off the row's right edge -
    /// a Label's minimum width is its whole unwrapped line, so without
    /// a floor and a trim one long name widens the HBox past the row.
    /// </summary>
    public const float NameMin = 180f;

    /// <summary>
    /// The Inspect button at the end of an object row - a shop line, a
    /// thing on the floor, a thing in a box, a thing in a trade.
    ///
    /// WHY A BUTTON. The reference looks at a list row on a RIGHT CLICK:
    /// `UIBuy.cpp:223`, `UILootList.cpp:257`, `UIObjectContents.cpp:259`
    /// and `UITrade.cpp:441,453` are the same three lines each -
    /// `if (args.button == RightButton) SendReqLookMessage(itm->getID())`.
    /// A phone has no right button. The hold that stood in for it is
    /// invisible: nothing on the row says it is there, and the player's
    /// words were that he could not see what he was buying or selling.
    /// This is the right-click made visible - one Secondary-dressed
    /// button per row, which sends exactly the look the right click
    /// sends. The row keeps its own job (tick, pick, nothing).
    ///
    /// It must NOT be MouseFilter.Ignore, unlike the labels beside it.
    /// Godot picks the deepest control under the touch whose filter is
    /// not Ignore, and a Button stops what it handles there rather than
    /// passing it up to an ancestor - so this press inspects and the row
    /// does not also tick. The HBox around it is Ignore and that is
    /// fine: a parent's Ignore excludes the parent from picking, not
    /// its children. Same construction as CreateCharacter.Ability,
    /// driven headless there to be sure (notes/harness.md).
    ///
    /// Dressed Secondary rather than as part of the row: a lighter fill
    /// and a Rule border against the row's flat stripe, so it reads as a
    /// control sitting ON the row rather than as the row's right end.
    /// </summary>
    /// <param name="node">The node name - `inspect` plus the row's, so a scripted run can press one.</param>
    public static Button Inspect(string node, Action pressed)
    {
        var look = new Button
        {
            Text = "Inspect",
            Name = node,
            CustomMinimumSize = new Vector2(InspectW, TapMin),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        Dress(look, Kind.Secondary);
        look.AddThemeFontSizeOverride("font_size", SmallSize);
        look.Pressed += pressed;
        return look;
    }

    /// <summary>
    /// What the name label on a row needs once Inspect shares the row
    /// with it: a floor it keeps, and a cut rather than a push past it.
    /// See NameMin.
    /// </summary>
    public static void NameFits(Label name)
    {
        if (name == null) return;
        name.CustomMinimumSize = new Vector2(NameMin, name.CustomMinimumSize.Y);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    /// <summary>
    /// The scrim for a screen with no world behind it - login, the
    /// character picker, creation. There is nothing to see through to
    /// except the view's own connection log, which read through the
    /// translucent one. Three screens built this by hand first.
    /// </summary>
    public static readonly Color ScrimSolid = new Color(0.02f, 0.018f, 0.015f, 1f);

    /// <summary>
    /// How wide a card should be, given what it would LIKE.
    ///
    /// The project stretches canvas items with aspect "expand", so a
    /// portrait window keeps the viewport 1920 wide and grows Y - which
    /// means a fixed width asked for in landscape comes out as a third
    /// of the glass when the phone is turned. Upright, the card takes
    /// nearly all of it. Four screens wrote this rule locally before it
    /// lived here.
    /// </summary>
    public static float CardWidth(Vector2 v, float wide)
        => v.Y > v.X ? v.X * 0.9f : wide;

    /// <summary>
    /// How much of the bottom of the glass the on-screen keyboard is
    /// covering, in VIEWPORT units.
    ///
    /// DisplayServer reports it in window pixels, and this project
    /// stretches canvas items, so the two are not the same number -
    /// using the raw value lifts a card by the wrong amount on every
    /// device whose window is not exactly the viewport. Zero when there
    /// is no keyboard, which is every desktop and every headless run.
    /// </summary>
    public static float KeyboardH(Viewport vp)
    {
        if (vp == null) return 0f;
        float px = DisplayServer.VirtualKeyboardGetHeight();
        if (px <= 0f) return 0f;
        float win = DisplayServer.WindowGetSize().Y;
        if (win <= 0f) return 0f;
        return px * (vp.GetVisibleRect().Size.Y / win);
    }

    /// <summary>
    /// Moves a card clear of the keyboard.
    ///
    /// A centred card is centred on the whole glass, and the keyboard
    /// takes the bottom third of it - so on a phone the field you are
    /// typing into is behind the keys you are typing with. The card
    /// slides up by exactly the overlap and no further, and never past
    /// the top edge; with no keyboard this returns the card untouched,
    /// so a caller can apply it unconditionally.
    /// </summary>
    public static Rect2 ClearOfKeyboard(Rect2 card, Vector2 v, float keyboard)
    {
        if (keyboard <= 0f) return card;
        float visible = v.Y - keyboard;
        float over = card.Position.Y + card.Size.Y - visible;
        if (over <= 0f) return card;
        float y = Mathf.Max(8f, card.Position.Y - over);
        return new Rect2(card.Position.X, y, card.Size.X, card.Size.Y);
    }

    /// <summary>
    /// A row that is read rather than pressed: a striped panel at row
    /// height with the inset already applied, ready for a label on the
    /// left and a control on the right. Six panels grew their own.
    /// </summary>
    public static PanelContainer Plate(bool alt, float height = RowH)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(0, height) };
        var s = Stripe(alt);
        s.ContentMarginLeft = s.ContentMarginRight = 14;
        s.ContentMarginTop = s.ContentMarginBottom = 4;
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    /// <summary>
    /// The card, its title bar, its name and its close - built and
    /// positioned together, because the same nine lines were copied
    /// into every converted panel's Layout and every one of them could
    /// get the close button's inset subtly wrong on its own.
    ///
    /// The panel owns the nodes; this only makes them and moves them.
    /// Add them to the tree yourself, in this order, so the title draws
    /// over the bar.
    /// </summary>
    public sealed class Chrome
    {
        public readonly Panel Card = Window();
        public readonly Panel Bar = TitleBar();
        public readonly Label Name = Title();
        public readonly Button X;

        public Chrome(Action close) { X = close == null ? null : CloseX(close); }

        /// <summary>Adds the four nodes to <paramref name="parent"/>, back to front.</summary>
        public void AddTo(Node parent)
        {
            if (parent == null) return;
            parent.AddChild(Card);
            parent.AddChild(Bar);
            parent.AddChild(Name);
            if (X != null) parent.AddChild(X);
        }

        /// <summary>Shows or hides all four at once.</summary>
        public void Show(bool on)
        {
            Card.Visible = on; Bar.Visible = on; Name.Visible = on;
            if (X != null) X.Visible = on;
        }

        /// <summary>Lays the four out over <paramref name="card"/>. Returns it, for chaining.</summary>
        public Rect2 Place(Rect2 card)
        {
            const float xs = CloseSize;
            Card.Position = card.Position;
            Card.Size = card.Size;
            Bar.Position = card.Position;
            Bar.Size = new Vector2(card.Size.X, TitleH);
            Name.Position = new Vector2(card.Position.X + Pad, card.Position.Y);
            Name.Size = new Vector2(card.Size.X - Pad * 2f - (X != null ? xs + Gap : 0f), TitleH);
            if (X != null)
            {
                X.Size = new Vector2(xs, xs);
                X.Position = new Vector2(card.Position.X + card.Size.X - xs - Pad,
                                         card.Position.Y + (TitleH - xs) * 0.5f);
            }
            return card;
        }
    }

    /// <summary>
    /// What a panel with no content should say, rather than showing an
    /// empty box. Every list panel had a different answer to this and
    /// several had none.
    /// </summary>
    /// <remarks>
    /// NOT autowrapped, and that is the whole point. A wrapping Label
    /// computes its minimum height from the width it last SHAPED at,
    /// and a panel builds this hidden and sizes it in a later Layout -
    /// so the shaping width is 1 point, the text wraps one character
    /// per line, and the minimum height comes out in the thousands.
    /// Godot clamps a Control's Size up to its minimum, so assigning
    /// position and size in one go gave one panel a 989-point label and
    /// another a 3717-point one, each centring its text most of a
    /// screen below the card it belonged to. The minimum fixes itself a
    /// frame or two later and the size never shrinks back.
    ///
    /// Two separate panels hit this within an hour of each other and
    /// worked around it locally, which is the signal that it belongs
    /// here. ClipText pins the minimum at 1x1, so the label is exactly
    /// the size it is given; a message that does not fit is elided
    /// rather than reflowing the window. Keep these short.
    /// </remarks>
    public static Label Empty(string text)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        l.AddThemeFontSizeOverride("font_size", BodySize);
        l.AddThemeColorOverride("font_color", TextDim);
        return l;
    }
}
