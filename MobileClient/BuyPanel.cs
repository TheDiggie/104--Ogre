using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>
/// A check box that can be seen. Godot's default CheckBox draws its
/// box and tick in dark grey strokes meant for a light theme, and every
/// panel here is a near-black sheet: the Players panel's "mute" box and
/// the Buy panel's row selector were a few dark pixels on dark, and on
/// Buy the tick appeared only once the row's own "pressed" fill lit the
/// background behind it. These two pictures carry their own light
/// border and their own fill, so the box reads on any background and
/// the tick reads in every state - unchecked, checked, disabled.
/// </summary>
public static class TickStyle
{
    static readonly System.Collections.Generic.Dictionary<(int, bool, bool), ImageTexture> _cache =
        new System.Collections.Generic.Dictionary<(int, bool, bool), ImageTexture>();

    static ImageTexture Get(int n, bool ticked, bool dim)
    {
        if (!_cache.TryGetValue((n, ticked, dim), out ImageTexture t))
            _cache[(n, ticked, dim)] = t = Make(n, ticked, dim);
        return t;
    }

    public static void Apply(CheckBox box, int size = 32)
    {
        box.AddThemeIconOverride("unchecked", Get(size, false, false));
        box.AddThemeIconOverride("checked", Get(size, true, false));
        box.AddThemeIconOverride("unchecked_disabled", Get(size, false, true));
        box.AddThemeIconOverride("checked_disabled", Get(size, true, true));
        // The default icon colours tint by state and are not pure
        // white, which would dim the tick again.
        foreach (string c in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color",
                                     "icon_hover_pressed_color", "icon_focus_color" })
            box.AddThemeColorOverride(c, Colors.White);
    }

    static ImageTexture Make(int n, bool ticked, bool dim)
    {
        float a = dim ? 0.45f : 1f;
        // The box's own light, in the skin's warm greys rather than the
        // blue-white it was drawn in: everything behind it is torchlight
        // on stone, and a cold box on a warm card read as a stray
        // widget. Same three pictures, same contrast - only the hues
        // moved. The ticked fill is the Primary button's, so a ticked
        // row and the Buy button that spends on it agree.
        var border = new Color(M59Skin.Gold.R, M59Skin.Gold.G, M59Skin.Gold.B, a);
        var fill = ticked ? new Color(0.286f, 0.231f, 0.129f, a) : new Color(0.078f, 0.071f, 0.063f, a);
        var tick = new Color(M59Skin.GoldBright.R, M59Skin.GoldBright.G, M59Skin.GoldBright.B, a);

        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        int m = Mathf.Max(2, n / 12);       // border width
        int pad = Mathf.Max(1, n / 16);
        for (int y = pad; y < n - pad; y++)
            for (int x = pad; x < n - pad; x++)
            {
                bool edge = x < pad + m || y < pad + m || x >= n - pad - m || y >= n - pad - m;
                img.SetPixel(x, y, edge ? border : fill);
            }

        if (ticked)
        {
            // A check mark: down-right to the foot, then up-right, three
            // pixels thick.
            Vector2 p0 = new Vector2(n * 0.24f, n * 0.52f);
            Vector2 p1 = new Vector2(n * 0.43f, n * 0.72f);
            Vector2 p2 = new Vector2(n * 0.78f, n * 0.30f);
            Line(img, p0, p1, tick, m + 1);
            Line(img, p1, p2, tick, m + 1);
        }
        return ImageTexture.CreateFromImage(img);
    }

    static void Line(Image img, Vector2 a, Vector2 b, Color c, int w)
    {
        int steps = (int)(a.DistanceTo(b) * 2f);
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = a.Lerp(b, i / (float)Math.Max(1, steps));
            for (int dy = -w / 2; dy <= w / 2; dy++)
                for (int dx = -w / 2; dx <= w / 2; dx++)
                {
                    int x = (int)p.X + dx, y = (int)p.Y + dy;
                    if (x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight())
                        img.SetPixel(x, y, c);
                }
        }
    }
}

/// <summary>
/// The coin a price is counted in, as a picture.
///
/// WHAT A PRICE IS. The buy list carries one number per line and no
/// coin: `BuyListMessage.ReadFrom` is the merchant, a count, and a
/// `TradeOfferObject` per line (`BuyListMessage.cs:67-77`), and that
/// object is an `ObjectBase` plus a single `uint price`
/// (`TradeOfferObject.cs:33,48`, "Per unit price of the object", `:83`).
/// Nothing in the protocol names a currency, so the reference writes the
/// bare number (`UIBuy.cpp:122`, `toString(obj->Price)`) and sums it
/// bare (`:211`). The unit is the server's: Blakod's Money class IS the
/// shilling - `money_name_one_rsc = "shilling"`, `money_icon_rsc =
/// coin.bgf` (`kod/object/item/passitem/numbitem/money.kod:19-22`) -
/// and it is the only coin the protocol knows a name for
/// (`ResourceStrings.cs:266`, `SHILLING = "shilling"`). Platinum and
/// doubloons are items with a value, not a unit anything is priced in.
///
/// WHERE THE PICTURE COMES FROM, in order:
///
///  1. The player's own shilling stack. An inventory stack arrives with
///     its BGF already resolved (`InventoryObject.Resource`), so when
///     you are carrying any shillings the icon is exactly the art the
///     bag draws for them - the server's own choice of file, whatever a
///     server calls it, matched by name the way Purse matches it.
///  2. Failing that, the well-known name, `coin.bgf`, which is the
///     file money.kod names and every resource dump ships. A player
///     with an empty purse still needs to know what a price is counted
///     in - that is the player who most needs to know.
///
/// Composed through `Tex.FromSprite` rather than read as a flat frame,
/// for the reason Vitals.StatIcon gives: object art has a transparent
/// key round it that a flat read turns into a cyan block.
/// </summary>
public static class CoinArt
{
    /// <summary>money.kod:22 - `money_icon_rsc = coin.bgf`.</summary>
    public const string ShillingBgf = "coin.bgf";

    /// <summary>The word Purse matches a shilling stack by (Purse.cs, `Coins`).</summary>
    public const string ShillingWord = "shilling";

    /// <summary>
    /// The shilling's BGF: the stack you carry, else the named file,
    /// else null when neither is readable yet.
    /// </summary>
    public static BgfFile Shilling(Meridian59.Data.DataController data, Meridian59.Files.ResourceManager res)
    {
        if (data?.InventoryObjects != null)
            foreach (InventoryObject o in data.InventoryObjects)
            {
                string name = o?.Name;
                if (string.IsNullOrEmpty(name) || o.Resource == null || o.Resource.Frames.Count == 0) continue;
                if (name.IndexOf(ShillingWord, StringComparison.OrdinalIgnoreCase) >= 0) return o.Resource;
            }
        BgfFile named = null;
        try { named = res?.GetObject(ShillingBgf); }
        catch (Exception e) { GD.PrintErr($"[CoinArt] {ShillingBgf}: {e.Message}"); }
        return named != null && named.Frames.Count > 0 ? named : null;
    }

    static readonly Dictionary<string, ImageTexture> _cache = new Dictionary<string, ImageTexture>();

    /// <summary>
    /// The coin as a texture, once per file. A miss is not cached -
    /// the art may simply not be read yet (notes/godot-ui.md, "a
    /// failed compose is never cached").
    /// </summary>
    public static ImageTexture Texture(BgfFile bgf)
    {
        if (bgf == null || bgf.Frames.Count == 0) return null;
        string key = bgf.Filename ?? "";
        if (_cache.TryGetValue(key, out ImageTexture had)) return had;
        ImageTexture made = null;
        try { made = M59Assets.FromTex(Tex.FromSprite(bgf, 0)); }
        catch (Exception e) { GD.PrintErr($"[CoinArt] {key}: {e.Message}"); }
        if (made != null) _cache[key] = made;
        return made;
    }

    /// <summary>
    /// The coin at a text's height, to sit immediately before a number.
    /// KeepAspectCentered, so a tall stack and a flat coin both land
    /// inside the same square and the digits after it start in the same
    /// place on every row.
    /// </summary>
    public static TextureRect Mark(ImageTexture tex, float side)
    {
        return new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(side, side),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = tex != null,
        };
    }
}

/// <summary>
/// The shopkeeper's stock.
///
/// `UIBuy.cpp`: a multi-select list of what the merchant sells, each row
/// an icon, the name and the price, with a running sum underneath and an
/// OK button that buys everything ticked in one message. Three details
/// come from that file rather than from taste:
///
///  - the window is the server's decision. `BuyInfo.IsVisible` goes up
///    when the merchant sends their stock and down when OK is pressed;
///    the view does not open it. `AvatarAction.Buy` asks - it looks for
///    a buyable object near you and sends `SendReqBuyMessage`.
///  - the sum counts a stackable item as `Count * Price` and everything
///    else as `Price` once, which is `Buy::CalculateSum` exactly.
///  - OK sends one `ReqBuyItems` carrying the merchant's id and an
///    ObjectID per ticked row, each with that row's count - not one
///    message per item.
///
/// Multi-select is a checkbox per row here rather than ctrl-click,
/// because a finger has no ctrl.
/// </summary>
public partial class BuyPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 40;
    [Export] public int RowHeight = 56;

    /// <summary>
    /// The coin beside a price: the text's own height. BodySize is the
    /// font's point size; the glyphs' line box is a few points taller,
    /// and the coin fills that box rather than the x-height, so it reads
    /// as a character in the line and not a dot before it.
    /// </summary>
    public const float CoinSide = M59Skin.BodySize + 6;

    /// <summary>Buy these, from the merchant the server named.</summary>
    public event Action<List<TradeOfferObject>> Buy;
    /// <summary>How many of this stackable line to buy.</summary>
    public event Action<TradeOfferObject> AmountWanted;

    /// <summary>
    /// Describe this one. The game looks at a row on a right click
    /// (`UIBuy.cpp:223`) - reading what something is before paying
    /// twelve hundred for it is not a luxury. The Inspect button on
    /// each row stands in for the right button; a held press on the
    /// row still does the same, for the thumb that learned it first.
    /// </summary>
    public event Action<uint> Look;

    /// <summary>
    /// Where the coin's picture comes from - see CoinArt. The view
    /// supplies it because the panel sees only the merchant's list, and
    /// the stack you carry and the resource folder are both the view's.
    /// </summary>
    public Func<BgfFile> Coin { get; set; }

    /// <summary>How long a press is held before it describes instead of ticking.</summary>
    [Export] public ulong LongPressMs = 600;

    ulong _downAt;
    bool _reverting;

    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title, _sum, _sumValue, _empty;
    TextureRect _sumCoin;
    HBoxContainer _sumRow;
    ScrollContainer _scroll;
    VBoxContainer _rows;
    Button _ok, _close;

    readonly HashSet<uint> _ticked = new HashSet<uint>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    readonly List<TradeOfferObject> _stock = new List<TradeOfferObject>();
    /// <summary>
    /// How many of each line the merchant actually has, by id - the
    /// ceiling for "how many", which the line's own count stops being
    /// the moment you choose fewer.
    ///
    /// Choosing an amount writes it into the line, which is what the
    /// reference does (`UIBuy.cpp:255`). The reference can get away with
    /// it because its amount box has no maximum at all - you type over
    /// it. Here the amount prompt is clamped to the number it is opened
    /// with, so re-opening it on the line's current count made every
    /// choice one-way: three of a hundred, and three was the ceiling
    /// from then on. Kept beside the model rather than in it, the way
    /// TradePanel keeps its own amounts.
    /// </summary>
    readonly Dictionary<uint, uint> _stockMost = new Dictionary<uint, uint>();
    // The object each id was last built from, to tell the merchant's new
    // line from our own write into the old one - see Sync.
    readonly Dictionary<uint, TradeOfferObject> _seen = new Dictionary<uint, TradeOfferObject>();
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim dims the world and eats the touch that would reach
        // it; the CARD over it is opaque, and that opacity is load
        // bearing rather than taste.
        //
        // At 0.95 the chat overlay (added to the same layer, and
        // anchored to the bottom-left) shows through at 5%, which is
        // plenty for white text on this colour: on 1920x1080 the running
        // total sits low on the panel, exactly where the chat lines
        // draw, and the one number a player must read before spending
        // had chat running through it.
        //
        // Why the whole window rather than the alternatives. MOVING the
        // total only relocates the collision: the chat block is anchored
        // to the screen, not to this panel, and in portrait it spans the
        // full width, so nowhere in the lower half of the panel is safe.
        // GIVING THE TOTAL ITS OWN BACKING fixes one label and leaves the
        // prices, names and button captions beside it ghosted by the same
        // bleed. The game has neither problem because its shop is an
        // opaque CEGUI frame (`UIBuy.cpp:12` takes it as a FrameWindow),
        // so nothing draws behind any of it; M59Skin.Window() is the same
        // thing, and the 5% it gives up showed nothing of the world
        // anyway. ChatOverlay's own full-screen log is opaque for the
        // same reason (ChatOverlay.cs, `_fullBack`).
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("For sale");
        _title.Visible = false;
        AddChild(_title);

        // The round close in the title bar does what the footer's Close
        // does - Dismiss - rather than a second, quieter way out.
        _x = M59Skin.CloseX(Dismiss);
        _x.Visible = false;
        AddChild(_x);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _scroll = new TouchScroll { Visible = false };
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        _empty = M59Skin.Empty("This merchant has nothing for sale.");
        _empty.Visible = false;
        AddChild(_empty);

        // The total is the one number a player must read before
        // spending, so it is title-sized and gold and sits in the
        // footer band beside the button that spends it - not a line of
        // small print lost above a row of grey slabs.
        //
        // It is three pieces in a row - the word, the coin, the number -
        // because the number carries the same coin the rows do. A sum
        // with no coin on it was the one place a player still had to
        // guess what he was about to spend.
        _sumRow = new HBoxContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _sumRow.AddThemeConstantOverride("separation", 8);
        _sum = new Label { Text = "", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _sum.AddThemeFontSizeOverride("font_size", M59Skin.TitleSize);
        _sum.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        _sumRow.AddChild(_sum);
        _sumCoin = CoinArt.Mark(null, M59Skin.TitleSize + 6);
        _sumRow.AddChild(_sumCoin);
        _sumValue = new Label { Text = "", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _sumValue.AddThemeFontSizeOverride("font_size", M59Skin.TitleSize);
        _sumValue.AddThemeColorOverride("font_color", M59Skin.GoldBright);
        _sumRow.AddChild(_sumValue);
        AddChild(_sumRow);

        _ok = Action("Buy", () =>
        {
            var want = new List<TradeOfferObject>();
            foreach (TradeOfferObject o in _stock)
                if (o != null && _ticked.Contains(o.ID)) want.Add(o);
            if (want.Count > 0) Buy?.Invoke(want);
            Dismiss();
        }, M59Skin.Kind.Primary);
        _close = Action("Close", Dismiss);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Button Action(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// The skin's <see cref="M59Skin.Pick"/>, plus the states a TOGGLE
    /// button draws in. A row here is a CheckBox, so while it is ticked
    /// Godot draws its "pressed" box rather than its "normal" one -
    /// marking only "normal", which is all Pick does, left a ticked row
    /// looking exactly like an unticked one.
    /// </summary>
    static void Mark(Button b, bool on, bool alt)
    {
        M59Skin.Pick(b, on, alt);
        b.AddThemeStyleboxOverride("pressed", b.GetThemeStylebox("normal"));
        b.AddThemeStyleboxOverride("hover_pressed", b.GetThemeStylebox(on ? "normal" : "hover"));
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // The card is sized to the list it holds, so a shop with three
        // lines is a three-line window rather than a tall empty box -
        // the thing that made this read as a debug screen.
        int lines = Mathf.Max(1, _rows != null ? _rows.GetChildCount() : 1);
        float want = lines * (RowHeight + 4f);
        Rect2 card = M59Skin.Frame(v, want);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f - 44f, M59Skin.TitleH);
        _x.Size = new Vector2(M59Skin.CloseSize, M59Skin.CloseSize);
        _x.Position = new Vector2(card.Position.X + card.Size.X - M59Skin.CloseSize - M59Skin.Pad,
                                  card.Position.Y + (M59Skin.TitleH - M59Skin.CloseSize) * 0.5f);

        _scroll.Position = body.Position;
        _scroll.Size = body.Size;
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        M59Skin.RowsFit(_rows, body);
        _empty.Position = body.Position;
        _empty.Size = body.Size;

        // Buy reads last in the line and Close sits where the thumb
        // that dismisses it is; the total takes whatever the two of
        // them leave, which keeps it clear of both.
        float left = M59Skin.FootRow(foot, _close, _ok);
        _sumRow.Position = foot.Position;
        _sumRow.Size = new Vector2(Mathf.Max(0f, left - foot.Position.X - M59Skin.Gap), foot.Size.Y);
    }

    /// <summary>
    /// Closes the shop the way the game closes it - by telling the model,
    /// which is the only thing the window's visibility follows.
    ///
    /// Hiding the Controls alone did not work: Sync runs every frame,
    /// saw IsVisible still set, and reopened the window on the next one,
    /// so both Buy and Close looked like they did nothing. The game sets
    /// the flag false after buying (`UIBuy.cpp:294`) and clears the stock
    /// as well when the window is closed outright (`:308`), since what a
    /// shopkeeper had is not worth remembering once you have walked away.
    /// </summary>
    /// <summary>
    /// Rebuilds the rows and the total now. Called when something
    /// outside changed a line - choosing how many of a stackable to
    /// buy - because the signature is what normally drives a rebuild
    /// and a count change is exactly what it watches.
    /// </summary>
    public void Refresh() => _signature = "";

    public void Dismiss()
    {
        if (_buy != null) { _buy.IsVisible = false; _buy.Clear(true); }
        Close();
    }

    public void Close()
    {
        Show(false);
        _ticked.Clear();
        _stockMost.Clear();
        _seen.Clear();
        _signature = "";
    }

    /// <summary>
    /// The most of this line there is to buy - the ceiling for the
    /// amount prompt, which is not the line's current count once you
    /// have chosen fewer than all of them.
    /// </summary>
    public uint Most(TradeOfferObject line)
    {
        if (line == null) return 1u;
        return _stockMost.TryGetValue(line.ID, out uint most) && most > line.Count
            ? most : line.Count;
    }

    /// <summary>
    /// The merchant's line for this id as the model holds it right now, or
    /// null if the shop is shut or no longer sells it. A re-sent stock list
    /// (`DataController.HandleBuyList`, `DataController.cs:3019-3032`)
    /// clears the list and adds brand-new objects, so anything that keeps a
    /// line across a frame keeps the id and asks here - the reference does
    /// the same by index at the moment it writes (`UIBuy.cpp:240-258`).
    /// </summary>
    public TradeOfferObject Line(uint id)
    {
        if (!IsOpen || _buy?.Items == null) return null;
        foreach (TradeOfferObject o in _buy.Items)
            if (o != null && o.ID == id) return o;
        return null;
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        _panel.Visible = on; _title.Visible = on; _scroll.Visible = on;
        _sumRow.Visible = on; _ok.Visible = on; _close.Visible = on;
        _card.Visible = on; _bar.Visible = on; _x.Visible = on;
        _empty.Visible = on && _rows.GetChildCount() == 0;
        Layout();
    }

    /// <summary>
    /// Follows the merchant's stock. `IsVisible` is the server's switch,
    /// not the view's.
    /// </summary>
    BuyInfo _buy;

    public void Sync(BuyInfo buy)
    {
        if (_rows == null) return;
        _buy = buy;

        // Visibility is the model's to decide, not the item count's.
        // The reference opens the window on IsVisible alone
        // (`UIBuy.cpp:60-66`), so a shop with nothing in stock is an
        // open, empty shop rather than a tap that appeared to do
        // nothing.
        if (buy == null || !buy.IsVisible)
        {
            if (IsOpen) Close();
            return;
        }

        if (!IsOpen) Show(true);

        if (buy.Items == null || buy.Items.Count == 0)
        {
            if (_signature != "")
            {
                _signature = "";
                foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            }
            _stock.Clear();
            _empty.Visible = true;
            Layout();
            return;
        }

        // The coin's art is part of what the rows draw, so its
        // resolution state is part of the signature - a list built
        // before the shilling's BGF was readable would otherwise keep
        // its bare numbers for the session (notes/godot-ui.md, "A polled
        // signature must hold everything the panel draws").
        BgfFile coin = Coin?.Invoke();
        ImageTexture coinTex = CoinArt.Texture(coin);
        var sb = new System.Text.StringBuilder();
        sb.Append(coinTex != null ? coin.Filename : "").Append('|');
        foreach (TradeOfferObject o in buy.Items)
            sb.Append(o?.ID).Append(':').Append(o?.Count).Append(':').Append(o?.Price).Append(';');
        string now = sb.ToString();

        if (now != _signature)
        {
            _signature = now;
            _stock.Clear();

            // What you had picked is kept across a rebuild, minus
            // anything the shop no longer has. The rebuild fires on any
            // change to a count or a price - the server's doing, not
            // yours - and clearing the whole selection meant a stock
            // update silently unticked everything you had chosen. The
            // game has no such problem: its selection lives on the list
            // widget and an unrelated item changing does not touch it.
            var stillThere = new HashSet<uint>();
            foreach (TradeOfferObject o in buy.Items)
                if (o != null && _ticked.Contains(o.ID)) stillThere.Add(o.ID);
            _ticked.Clear();
            foreach (uint id in stillThere) _ticked.Add(id);

            foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

            foreach (TradeOfferObject o in buy.Items)
                if (o != null)
                {
                    // The high-water mark, because our own writes only
                    // ever lower a line's count and the server's can
                    // raise it. Never lowered here, so choosing fewer
                    // does not shrink the ceiling - which is the bug -
                    // and a shop that genuinely restocks upwards still
                    // lets you ask for the lot.
                    //
                    // A line that is a NEW object is the merchant speaking
                    // (`DataController.cs:3019-3032` replaces every line),
                    // not our own write, so its count is the real stock
                    // and the mark follows it down as well as up - else a
                    // shop that sold some off still let you ask for more
                    // than it has.
                    bool fresh = !_seen.TryGetValue(o.ID, out TradeOfferObject was) || !ReferenceEquals(was, o);
                    if (fresh || !_stockMost.TryGetValue(o.ID, out uint had) || o.Count > had)
                        _stockMost[o.ID] = o.Count;
                    _seen[o.ID] = o;

                    // Alternating tints, so the eye keeps its place down
                    // a list of near-identical lines.
                    _stock.Add(o); _rows.AddChild(Row(o, _stock.Count % 2 == 0, coinTex));
                }

            _empty.Visible = _stock.Count == 0;
            Layout();

            string who = buy.TradePartner != null && !string.IsNullOrWhiteSpace(buy.TradePartner.Name)
                ? buy.TradePartner.Name : "For sale";
            _title.Text = $"{who} ({_stock.Count})";
        }

        Total();
    }

    /// <summary>
    /// What the ticked rows come to. Buy::CalculateSum counts a stackable
    /// item as count times price and everything else as its price once.
    /// </summary>
    void Total()
    {
        long sum = 0;
        foreach (TradeOfferObject o in _stock)
        {
            if (o == null || !_ticked.Contains(o.ID)) continue;
            sum += o.IsStackable ? (long)o.Count * o.Price : o.Price;
        }
        bool any = _ticked.Count > 0;
        _sum.Text = any ? "Total" : "Nothing picked";
        _sumValue.Text = any ? $"{sum}  ({_ticked.Count})" : "";
        _sumValue.Visible = any;
        // The same picture the rows carry; hidden with the number, so
        // "Nothing picked" is not followed by a coin with nothing after it.
        _sumCoin.Texture = CoinArt.Texture(Coin?.Invoke());
        _sumCoin.Visible = any && _sumCoin.Texture != null;
        _ok.Disabled = !any;
    }

    Control Row(TradeOfferObject o, bool alt, ImageTexture coin)
    {
        TradeOfferObject captured = o;

        var tick = new CheckBox
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            // A row rebuilt while it was picked comes back picked.
            ButtonPressed = _ticked.Contains(o.ID),
            // Named so a scripted run can tick one: the row's text lives
            // in child labels, so there is nothing to find it by.
            Name = $"buy{o.ID}",
        };
        M59Skin.Dress(tick, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
        Mark(tick, _ticked.Contains(o.ID), alt);
        TickStyle.Apply(tick);
        tick.ButtonDown += () => _downAt = Time.GetTicksMsec();
        tick.Toggled += on =>
        {
            // Held describes rather than ticks, and puts the tick back.
            // Setting the property raises this again, hence the guard.
            if (_reverting) return;
            ulong down = _downAt;
            _downAt = 0;
            if (down != 0 && Time.GetTicksMsec() - down >= LongPressMs)
            {
                _reverting = true;
                tick.ButtonPressed = !on;
                _reverting = false;
                Look?.Invoke(captured.ID);
                return;
            }
            if (on) _ticked.Add(captured.ID); else _ticked.Remove(captured.ID);
            Mark(tick, on, alt);
            Total();
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.AddThemeConstantOverride("separation", 10);
        line.OffsetLeft = RowHeight; line.OffsetTop = 6;
        line.OffsetRight = -8; line.OffsetBottom = -6;
        tick.AddChild(line);

        line.AddChild(new TextureRect
        {
            Texture = Icon(o),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        uint argb = o.Flags != null ? NameColors.GetColorFor(o.Flags) : NameColors.NORMAL;
        var name = new Label
        {
            Text = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
        // The name keeps a floor and is cut past it, now that a fourth
        // column sits on the row's end - see M59Skin.NameMin.
        M59Skin.NameFits(name);
        line.AddChild(name);

        if (o.IsStackable)
        {
            // How many, and a way to change it. The game gives a
            // stackable line its own edit box and writes what you type
            // back into the item (`UIBuy.cpp:240-258`), so you can buy
            // five of a hundred; we had a label, and the only amount you
            // could buy was the whole pile.
            //
            // A box to type in is the wrong control on a phone. The
            // amount prompt already exists for dropping part of a stack
            // and knows how to ask this question, so the row asks for
            // it instead.
            var many = new Button
            {
                Text = $"x{o.Count}",
                Name = $"many{o.ID}",
                TooltipText = "How many",
            };
            // A stepper, not a slab: it is a small square that opens
            // the amount prompt, and it says so by looking like one.
            many.CustomMinimumSize = new Vector2(72, 0);
            M59Skin.Dress(many, M59Skin.Kind.Step);
            many.Pressed += () => AmountWanted?.Invoke(o);
            line.AddChild(many);
        }
        // There is no second branch. IsStackable IS Count > 0
        // (`ObjectID.cs:202-205`), so an `else if (o.Count > 1)` behind
        // that test can never run: what stood here was a label for a
        // count that the branch above had already claimed. Deleted
        // rather than left looking like a case that is handled.

        // The price, with the coin it is counted in immediately before
        // the digits. The player's words: "right now it's just a number
        // and a player won't know what currency they need." The
        // reference shows the bare number (`UIBuy.cpp:122`) and relies
        // on the desktop player knowing; the icon says it without a
        // word, which is the game's own vocabulary for a shilling - the
        // bag never writes "shilling" either, it draws coin.bgf.
        //
        // A box packed to its END, not a right-aligned label: the coin
        // has to hug the digits, so both are laid out at their natural
        // width and pushed right together. The box keeps the 96-point
        // floor the old label had plus the coin's own, so the column's
        // right edge is where it was and Inspect does not move.
        var cost = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            CustomMinimumSize = new Vector2(96 + CoinSide + 6, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        cost.AddThemeConstantOverride("separation", 6);
        cost.AddChild(CoinArt.Mark(coin, CoinSide));
        var price = new Label
        {
            Text = o.Price.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        price.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        price.AddThemeColorOverride("font_color", M59Skin.Gold);
        cost.AddChild(price);
        line.AddChild(cost);

        // Inspect: the touch-screen form of the right click that looks
        // at a shop line (`UIBuy.cpp:222-224`). Reading what something
        // is before paying twelve hundred for it is not a luxury, and
        // the hold above gave no sign it was there. Same Look, same id;
        // the row's tick is untouched because the button takes the
        // press - see M59Skin.Inspect.
        line.AddChild(M59Skin.Inspect($"inspect{tick.Name}", () => Look?.Invoke(captured.ID)));

        return tick;
    }

    ImageTexture Icon(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        // Keyed on everything the composed picture depends on. The
        // file alone was not enough: two items sharing a BGF but dyed
        // differently compose to different pictures, and an item that
        // changes appearance kept the first icon it was ever drawn
        // with - where the game's composer re-pushes a texture every
        // time the object changes.
        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}:{IconSize}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[BuyPanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
