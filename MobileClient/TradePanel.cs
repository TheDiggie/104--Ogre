using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// Trading with another player.
///
/// `UITrade.cpp`: two lists side by side - what you are offering and
/// what they are - the partner's name over theirs, and Offer, Accept
/// and Cancel underneath. `TradeInfo.IsVisible` is the switch.
///
/// Worth knowing who sets it. An `Offer` from the server does **not**:
/// `HandleOffer` puts the trade together, marks it
/// `IsBackgroundOffer` and `IsPending`, and leaves `IsVisible` false.
/// The window comes up when the player asks for it -
/// `AvatarAction.Trade` sets `IsVisible` on a pending offer, or finds a
/// nearby offerable object and starts one. So an offer arriving is not
/// a window appearing over whatever you were doing, which is the right
/// behaviour and easy to get wrong by watching the wrong flag.
///
/// Three things come out of that file rather than from taste:
///
///  - Offer sends `ReqOffer(partner, ids)` normally and
///    `ReqCounterOffer(ids)` when `IsBackgroundOffer` - that is, when
///    they opened the trade rather than you. Same button, two messages.
///  - the count on each offered line comes from the row, not from the
///    object: the source says "DO NOT USE count on objectmodel
///    (nonupdated)" and falls back to 1 for an empty box.
///  - Accept is `SendAcceptOffer` with no arguments and Cancel is
///    `SendCancelOffer`; neither carries the list.
///
/// Items get into your side by dragging them from the inventory there.
/// Here the window has an Add button that opens the bag in a picking
/// mode, because a phone cannot show both windows at once to drag
/// between them. That part is not the game's.
/// </summary>
public partial class TradePanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int IconSize = 36;
    [Export] public int RowHeight = 48;

    /// <summary>The gap between two rows, which is also the gap between
    /// two amount buttons. See where it is applied.</summary>
    const int RowGap = 8;

    /// <summary>Send what is on your side.</summary>
    public event Action<List<ObjectID>> Offer;
    public event Action Accept;
    public event Action Cancel;
    /// <summary>Open the bag to pick something to add.</summary>
    public event Action AddWanted;

    /// <summary>How many of this stackable of yours to offer.</summary>
    public event Action<ObjectBase> AmountWanted;

    /// <summary>
    /// Describe this row - raised by its Inspect button, the right
    /// click's stand-in (`UITrade.cpp:441`, `:453`), and still by a
    /// hold on the row for the thumb that learned it first.
    /// </summary>
    public event Action<uint> Look;

    /// <summary>How long a press is held before it describes the row.</summary>
    [Export] public ulong LongPressMs = 600;

    ulong _downAt;

    /// <summary>
    /// What you are parting with, by object id, where it is not the
    /// whole stack. Kept beside the model rather than in it: the object
    /// is the one in your pack, and writing a smaller number into it
    /// would be claiming you own less than you do.
    /// </summary>
    readonly Dictionary<uint, uint> _amounts = new Dictionary<uint, uint>();

    /// <summary>
    /// How many of this line go on the wire - and ZERO for anything that
    /// is not a stack, which is not a cosmetic difference.
    ///
    /// `ObjectID.WriteTo` (`ObjectID.cs:69-88`, and the pointer twin at
    /// `:113-128`) sets the multi-count flag on the id and appends a
    /// second uint **whenever Count > 0** - so a sword sent as
    /// ObjectID(id, 1) goes out eight bytes with a number attached
    /// instead of four bytes with none. The server pushes one entry onto
    /// number_list per flagged object and no entry for the others
    /// (`Server-104/blakserv/parsecli.c:342-351`), and the kod then
    /// walks item_list handing those numbers out to the NumberItems in
    /// order, advancing the number list only when the item IS one
    /// (`user.kod:6873-6880` for an offer, `:7217-7253` for a counter).
    ///
    /// So a stray number does not go to the item that carried it - it
    /// SHIFTS the list. Both lists are built with Cons as the packet is
    /// read (`parsecli.c:339,347`), so both arrive reversed and a
    /// non-stackable sent AFTER a stackable is seen first: it eats
    /// nothing, and the stackable behind it takes its "1". Offering 50
    /// coins and a sword offered one coin. On the counter-offer path it
    /// is worse than a silent loss - a shifted number larger than the
    /// stack cancels the whole trade (`user.kod:7237-7244`).
    ///
    /// The reference gets this right without saying so: it fills the
    /// row's amount box from obj->Count (`UITrade.cpp:207`), which is
    /// "0" for a non-stackable, hides the box (`:208`) but still reads
    /// its text back on Offer (`:404-414`) - "0" is not empty, so the
    /// STRINGEMPTY fallback to "1" never applies and the item goes out
    /// as ObjectID(id, 0), unflagged.
    /// </summary>
    uint Offering(ObjectBase o)
    {
        // IsStackable is Count > 0 (`ObjectID.cs:202-205`), the same
        // test the reference's box visibility uses.
        if (o == null || !o.IsStackable) return 0u;
        return Chosen(o);
    }

    /// <summary>
    /// How many of this stack you have said you are parting with, which
    /// is the whole stack until you say otherwise.
    ///
    /// This is what the amount prompt has to be opened with. Opening it
    /// on `o.Count` instead put the WHOLE STACK back in the box every
    /// time you looked at it, and since OK writes the box back, tapping
    /// the number to check it said 22 and pressing OK handed over all
    /// 25 - a trade being irreversible once accepted. The reference has
    /// no such hole because the chosen amount lives in the row's own
    /// edit box and is read straight back out of it on Offer
    /// (`UITrade.cpp:207` fills it once from obj->Count, `:398-414`
    /// reads whatever is in it); here the row is redrawn, so the number
    /// has to be handed back from the place that kept it.
    ///
    /// The CEILING is still the live stack, not this: unlike a shop line
    /// (`BuyPanel.Most`) nothing ever writes a smaller number into the
    /// object, so o.Count cannot ratchet down.
    /// </summary>
    public uint Chosen(ObjectBase o)
    {
        if (o == null) return 0u;
        uint have = o.Count;
        uint n = _amounts.TryGetValue(o.ID, out uint set) ? set : have;
        if (n < 1) n = 1;
        // Never more than the stack holds: the offer path silently
        // bounds it (`user.kod:6877-6878`) but the counter-offer path
        // cancels the trade outright (`user.kod:7237-7244`).
        if (n > have) n = have;
        return n;
    }

    /// <summary>Sets how many of one line to offer.</summary>
    public void SetAmount(uint id, uint count)
    {
        _amounts[id] = count < 1 ? 1 : count;
        _mineSignature = "";
    }

    ColorRect _panel;
    Panel _card, _bar;
    Button _x;
    Label _title, _mine, _theirs;
    /// <summary>
    /// The line down the middle. Two lists stacked one above the other
    /// read as one long list with a heading halfway down it; a rule
    /// between two columns is what says they are two sides of the same
    /// thing. Laid out side by side while the card is wider than it is
    /// tall, and stacked when it is not - see Layout.
    /// </summary>
    ColorRect _split;
    Label _noneMine, _noneTheirs;
    ScrollContainer _scrollMine, _scrollTheirs;
    VBoxContainer _rowsMine, _rowsTheirs;
    Button _add, _offer, _accept, _cancel;

    /// <summary>
    /// What has happened to your side of the offer since you built it -
    /// an in-page line, never a system dialog. See Reconcile.
    /// </summary>
    Label _notice;
    InventoryPanel _bag;

    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _theirSignature = "";
    string _mineSignature = "";
    TradeInfo _trade;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // The scrim dims the world and eats the touch that would reach
        // it. The CARD is the opaque part: a FrameWindow with no Alpha
        // (Meridian59.layout:1585, UITrade.cpp:8), so nothing draws
        // through the two lists or the notice.
        _panel = new ColorRect { Color = M59Skin.Scrim, Visible = false };
        _panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_panel);

        _card = M59Skin.Window();
        _card.Visible = false;
        AddChild(_card);

        _bar = M59Skin.TitleBar();
        _bar.Visible = false;
        AddChild(_bar);

        _title = M59Skin.Title("Trade");
        _title.Visible = false;
        AddChild(_title);

        // The round close does exactly what Cancel does - the same
        // call, not a quieter second way out of a trade.
        _x = M59Skin.CloseX(CancelTrade);
        _x.Visible = false;
        AddChild(_x);

        _split = M59Skin.Hairline();
        _split.Visible = false;
        AddChild(_split);

        _mine = Head("You offer", M59Skin.BodySize, M59Skin.GoldBright);
        _theirs = Head("They offer", M59Skin.BodySize, M59Skin.GoldBright);

        _noneMine = Settled(M59Skin.Empty("Nothing yet - Add something of yours."));
        _noneMine.Visible = false;
        AddChild(_noneMine);
        _noneTheirs = Settled(M59Skin.Empty("Nothing yet."));
        _noneTheirs.Visible = false;
        AddChild(_noneTheirs);

        _notice = Head("", M59Skin.BodySize, new Color(1f, 0.78f, 0.45f));
        _notice.Name = "tradeNotice";
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice.VerticalAlignment = VerticalAlignment.Center;

        _rowsMine = new VBoxContainer();
        // Eight, not three. Each row carries a 44-point amount button
        // inside a 48-point row, so three left FIVE points between two
        // of them - and a miss here does not scroll past something, it
        // changes an offer in a trade. Eight is the gap the guides ask
        // for between targets.
        _rowsMine.AddThemeConstantOverride("separation", RowGap);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rowsMine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollMine = new TouchScroll { Visible = false };
        _scrollMine.AddChild(_rowsMine);
        AddChild(_scrollMine);

        _rowsTheirs = new VBoxContainer();
        _rowsTheirs.AddThemeConstantOverride("separation", RowGap);
        _rowsTheirs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollTheirs = new TouchScroll { Visible = false };
        _scrollTheirs.AddChild(_rowsTheirs);
        AddChild(_scrollTheirs);

        _add = Act("Add", () => AddWanted?.Invoke());
        // Read at the press, not captured when the item was added. The
        // game is explicit that the model's count is not kept up to
        // date and reads the row instead (`UITrade.cpp:398-414`); this
        // client has no per-row amount box to read, so the object's own
        // count at the moment you offer is the closest thing to it -
        // and it is still better than the count the stack happened to
        // have when you first added it.
        //
        // Offering() decides the number, and for anything that is not a
        // stack that number is 0 - see the long note on it. The same
        // list goes to ReqOffer and to ReqCounterOffer (GameView.cs,
        // the `_trade.Offer +=` block), as it does in the reference
        // (`UITrade.cpp:417-421`), so both paths are fixed by the one
        // rule rather than by two.
        _offer = Act("Offer", () =>
        {
            // Checked again at the press, not only on the last frame: an
            // offer is irreversible once accepted, and if the pack moved
            // under it the player is shown that INSTEAD of the offer
            // going out - they press again knowing what it now holds.
            if (Reconcile(_trade)) return;
            _notice.Text = ""; Layout();
            var send = new List<ObjectID>();
            if (_trade?.ItemsYou != null)
                foreach (ObjectBase o in _trade.ItemsYou)
                    if (o != null) send.Add(new ObjectID(o.ID, Offering(o)));
            Offer?.Invoke(send);
        }, M59Skin.Kind.Primary);
        // Primary too, and not a second one on screen: Offer is up
        // while the offer is still yours to make and Accept only once
        // it is not (see the visibility rules in Sync), so the two are
        // never both shown.
        _accept = Act("Accept", () => Accept?.Invoke(), M59Skin.Kind.Primary);
        // The game sends the cancel only when a trade is actually
        // pending, and clears its own side either way
        // (`UITrade.cpp:490-495`). Sending unconditionally asks the
        // server to cancel something it has no record of, which it
        // answers by doing nothing - leaving IsVisible set and the
        // window up, so Cancel looked broken whenever you had opened
        // the window on a nearby player and nobody had offered yet.
        _cancel = Act("Cancel", CancelTrade, M59Skin.Kind.Danger);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    /// <summary>
    /// Takes the word-wrap off a label that is sized by hand.
    ///
    /// WHY. An autowrap Label shapes its text at the width it has WHEN
    /// FIRST ASKED, which for a label built hidden and sized in Layout is
    /// one pixel - a character per line, a minimum height of ~990. A
    /// Control cannot be sized below its minimum, so Layout's
    /// `Size = column` was clamped to 990 tall, and the label (centred in
    /// that) drew at the bottom of the screen, far outside the card. The
    /// minimum corrects itself a moment later, but Size does not shrink
    /// back. One line, clipped with an ellipsis if a column is ever too
    /// narrow for it, has a minimum that does not depend on its width.
    /// </summary>
    static Label Settled(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return l;
    }

    Label Head(string text, int size, Color colour)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        AddChild(l);
        return l;
    }

    Button Act(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, Visible = false };
        M59Skin.Dress(b, kind);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// Backing out of the trade. Lifted out of the Cancel button's
    /// lambda unchanged so the title bar's close can call the SAME
    /// thing rather than a second, nearly-identical path.
    /// </summary>
    void CancelTrade()
    {
        if (_trade != null && _trade.IsPending) Cancel?.Invoke();
        _trade?.Clear(true);
        Show(false);
        Clear();
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        foreach (Node n in GetChildren())
            if (n is Control c && c != this) c.Visible = on;
        _notice.Visible = on && _notice.Text != "";
        // The two "nothing yet" lines belong to their own column's
        // emptiness, not to the window's visibility, so the blanket
        // loop above must not be the last word on them.
        Empties();
        _panel.Visible = on;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        _panel.Position = Vector2.Zero;
        _panel.Size = v;

        // Room for the notice only while there is one to show, and for
        // the taller of the two lists otherwise: side by side, the card
        // is as tall as the longer column needs, not as tall as both
        // put together.
        const float headH = 30f;
        int longest = Mathf.Max(1, Mathf.Max(_rowsMine?.GetChildCount() ?? 0,
                                             _rowsTheirs?.GetChildCount() ?? 0));
        float noteH = _notice.Text != "" ? 52f : 0f;
        Rect2 card = M59Skin.Frame(v, headH + longest * (RowHeight + RowGap) + noteH + M59Skin.Gap);
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

        // The notice sits at the FOOT of the body, under both columns:
        // what it says is about the offer as a whole, and putting it
        // over one column would read as belonging to that column.
        _notice.Position = new Vector2(body.Position.X, body.Position.Y + body.Size.Y - noteH);
        _notice.Size = new Vector2(body.Size.X, noteH);
        float listsH = body.Size.Y - noteH - (noteH > 0f ? M59Skin.Gap : 0f);

        // Two columns while there is width for them. The card is capped
        // at Frame's width, so a column is never the two thousand
        // points a sideways phone would otherwise give it; below that
        // cap - a phone held upright - two columns of a name and a
        // count would be unreadable, so they stack as they did before.
        bool twoUp = body.Size.X >= 620f;
        float colW = twoUp ? (body.Size.X - M59Skin.Gap * 3f) * 0.5f : body.Size.X;
        float colH = twoUp ? listsH - headH : (listsH - headH * 2f - M59Skin.Gap) * 0.5f;
        colH = Mathf.Max(RowHeight, colH);

        float x2 = twoUp ? body.Position.X + colW + M59Skin.Gap * 3f : body.Position.X;
        float y2 = twoUp ? body.Position.Y : body.Position.Y + headH + colH + M59Skin.Gap;

        _mine.Position = body.Position;
        _mine.Size = new Vector2(colW, headH);
        _scrollMine.Position = new Vector2(body.Position.X, body.Position.Y + headH);
        _scrollMine.Size = new Vector2(colW, colH);
        // Clear of the scrollbar. This is the one list in the client
        // with a PRESSABLE control at the row's right edge - the amount
        // button - so a row laid out to the full column put a 72x44
        // target under the grabber: a thumb aimed at the bar changed an
        // offer. See M59Skin.RowsW.
        M59Skin.RowsFit(_rowsMine, new Rect2(Vector2.Zero, new Vector2(colW, 0)));
        _noneMine.Position = _scrollMine.Position;
        _noneMine.Size = _scrollMine.Size;

        _theirs.Position = new Vector2(x2, y2);
        _theirs.Size = new Vector2(colW, headH);
        _scrollTheirs.Position = new Vector2(x2, y2 + headH);
        _scrollTheirs.Size = new Vector2(colW, colH);
        M59Skin.RowsFit(_rowsTheirs, new Rect2(Vector2.Zero, new Vector2(colW, 0)));
        _noneTheirs.Position = _scrollTheirs.Position;
        _noneTheirs.Size = _scrollTheirs.Size;

        // The rule between them, only when they are side by side.
        _split.Visible = twoUp && IsOpen;
        _split.Position = new Vector2(body.Position.X + colW + M59Skin.Gap * 1.5f, body.Position.Y);
        _split.Size = new Vector2(1f, listsH);

        // Right to left: Cancel where the thumb that backs out is, then
        // Add, and whichever of Offer and Accept is live reads last.
        M59Skin.FootRow(foot, _cancel, _add, _accept, _offer);
    }

    /// <summary>Each column says so when it is empty, rather than being a blank box.</summary>
    void Empties()
    {
        if (_noneMine == null) return;
        _noneMine.Visible = IsOpen && (_rowsMine?.GetChildCount() ?? 0) == 0;
        _noneTheirs.Visible = IsOpen && (_rowsTheirs?.GetChildCount() ?? 0) == 0;
    }

    /// <summary>
    /// Adds one of yours to the offer.
    ///
    /// Into the model, not into a list of this window's own. That is
    /// where the game puts it - the drop handler adds the inventory
    /// object straight to <c>Trade.ItemsYou</c> and the list widget
    /// follows the list's own events (`UITrade.cpp:479-481`,
    /// `:21-22`). Keeping a private copy here meant your side of the
    /// trade was whatever you had asked for rather than what the server
    /// registered: when it echoes your offer back it replaces ItemsYou
    /// wholesale (`DataController.cs:2980`), and none of that reached
    /// the screen.
    /// </summary>
    public void Put(InventoryObject item)
    {
        if (item == null || _trade?.ItemsYou == null) return;
        // Already offered? The reference asks the list `Contains` before
        // it adds (`UITrade.cpp:480-481`), and `Contains` is an identity
        // test there because the drop handler hands over
        // `InventoryObjects[index]` itself every time. Here the bag is
        // rebuilt and the slot can hand back ANOTHER instance of the same
        // item (`InventoryPanel.cs:518-520` says as much about taps), so
        // the identity test let a second add of the same stack through:
        // two rows, one amount box each, and the one ID twice in
        // ReqCounterOffer. Same test, by ID - two stacks of one name have
        // two IDs and still both go in.
        foreach (ObjectBase o in _trade.ItemsYou)
            if (o != null && o.ID == item.ID) return;
        _trade.ItemsYou.Add(item);
        _mineSignature = "";
    }

    /// <summary>
    /// Keeps the things you have put up pointed at what you actually
    /// carry.
    ///
    /// <c>Put</c> stores the bag's own InventoryObject in
    /// <c>Trade.ItemsYou</c>, which is how the game does it too
    /// (`UITrade.cpp:480-481`) - but the game's rows are destroyed by the
    /// list's own ItemRemove as the model changes, so a row can never
    /// outlive its object. Here the rows are rebuilt from the list on a
    /// poll, and nothing removed the entry. A server that replaces a
    /// stack sends InventoryRemove then InventoryAdd under a NEW id
    /// (`DataController.cs:2450-2478`; the list drops the object and adds
    /// another), so the trade went on offering the old id at the old
    /// count: an object the server no longer knows, and a count that may
    /// exceed the stack - which on a counter-offer cancels the whole
    /// trade (`user.kod:7237-7244`). The clamp in <c>Chosen</c> cannot
    /// catch that, because it clamps against the stale object's own
    /// Count. The bag drops its selection the same way when the id is
    /// gone (`InventoryPanel.cs` Sync), and this follows it.
    ///
    /// What the player sees: an in-page line saying what left the offer
    /// and why, kept until they act. A row silently disappearing from
    /// an offer about to be accepted would be its own kind of wrong.
    ///
    /// Only while the offer is still yours to edit. Once the server has
    /// echoed it (Offered/CounterOffered, `DataController.cs:2971-2990`)
    /// ItemsYou holds the SERVER's objects, not the pack's, and the
    /// server decides what happens to a trade whose stuff has gone.
    ///
    /// Returns true when it changed anything.
    /// </summary>
    bool Reconcile(TradeInfo trade)
    {
        if (trade?.ItemsYou == null || trade.IsItemsYouSet || trade.ItemsYou.Count == 0) return false;

        if (_bag == null || !IsInstanceValid(_bag))
        {
            _bag = null;
            if (GetParent() != null)
                foreach (Node n in GetParent().GetChildren())
                    if (n is InventoryPanel b) { _bag = b; break; }
        }
        IList<InventoryObject> pack = _bag?.Items;
        if (pack == null) return false;

        var gone = new List<string>();
        var shrunk = new List<string>();
        var keep = new List<ObjectBase>();
        bool swapped = false;
        foreach (ObjectBase o in trade.ItemsYou)
        {
            if (o == null) continue;
            InventoryObject live = null;
            foreach (InventoryObject p in pack)
                if (p != null && p.ID == o.ID) { live = p; break; }

            if (live == null)
            {
                gone.Add(string.IsNullOrWhiteSpace(o.Name) ? "An item" : o.Name);
                _amounts.Remove(o.ID);
                continue;
            }
            if (!ReferenceEquals(live, o)) swapped = true;

            // A number you chose that the stack can no longer cover is
            // lowered, and said so: Chosen would clamp it silently.
            if (live.IsStackable && _amounts.TryGetValue(live.ID, out uint set) && set > live.Count)
            {
                _amounts[live.ID] = Math.Max(1u, live.Count);
                shrunk.Add($"{(string.IsNullOrWhiteSpace(live.Name) ? "An item" : live.Name)} (now {live.Count})");
            }
            keep.Add(live);
        }

        if (gone.Count == 0 && shrunk.Count == 0 && !swapped) return false;

        if (gone.Count > 0 || swapped)
        {
            trade.ItemsYou.Clear();
            foreach (ObjectBase o in keep) trade.ItemsYou.Add(o);
        }
        _mineSignature = "";

        if (gone.Count == 0 && shrunk.Count == 0) return false;

        string said = "";
        if (gone.Count > 0)
            said += string.Join(", ", gone) + (gone.Count == 1 ? " is" : " are") +
                    " no longer in your pack and was taken off your offer. ";
        if (shrunk.Count > 0)
            said += "Your stack changed, so the amount was lowered: " + string.Join(", ", shrunk) + ".";
        said = said.Trim();
        _notice.Text = char.ToUpperInvariant(said[0]) + said.Substring(1);
        _notice.Visible = IsOpen;
        Layout();
        return true;
    }

    /// <summary>
    /// Follows the trade the server set up. Your side is yours to fill,
    /// so only theirs is rebuilt from the model.
    /// </summary>
    public void Sync(TradeInfo trade)
    {
        if (_panel == null) return;
        _trade = trade;

        if (trade == null || !trade.IsVisible)
        {
            if (IsOpen) { Show(false); Clear(); }
            return;
        }

        if (!IsOpen) { Show(true); Clear(); }

        Reconcile(trade);

        // Which buttons are live is the model's business, not the
        // window's. Show() turns everything on; these three turn back
        // off exactly where the game turns them off
        // (`UITrade.cpp:90-107`). Without them Accept was tappable
        // before anyone had offered anything and Offer stayed tappable
        // after you had committed, so a trade could be re-offered or
        // accepted while unset.
        _offer.Visible = !trade.IsItemsYouSet;
        _add.Visible = !trade.IsItemsYouSet;
        _accept.Visible = trade.IsItemsYouSet && trade.IsItemsPartnerSet && !trade.IsBackgroundOffer;

        // An invisible partner is not named: the game hides the name
        // label on Flags.Drawing == Invisible (`UITrade.cpp:116-119`),
        // and putting it in the title would tell you who is standing
        // there unseen.
        bool hidden = trade.TradePartner != null &&
                      trade.TradePartner.Flags.Drawing == ObjectFlags.DrawingType.Invisible;
        string who = !hidden && trade.TradePartner != null && !string.IsNullOrWhiteSpace(trade.TradePartner.Name)
            ? trade.TradePartner.Name : "someone";
        _title.Text = trade.IsBackgroundOffer ? $"{who} offers you a trade" : $"Trading with {who}";

        // Your side is rebuilt from the model too, not only theirs.
        // Two signatures in one Sync, so the second cannot share the
        // first's builder: the mine half is compared before the other
        // is built, so the shared one is free again by then.
        var mine = Sig.Start();
        if (trade.ItemsYou != null)
            foreach (ObjectBase o in trade.ItemsYou)
                mine.Opt(o?.ID).Append(':').Append(o == null ? 0u : Offering(o)).Append(':').Append(o?.Name).Append(':')
                    .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        if (Sig.Changed(mine, ref _mineSignature))
        {
            foreach (Node n in _rowsMine.GetChildren()) { _rowsMine.RemoveChild(n); n.QueueFree(); }
            int m = 0;
            if (trade.ItemsYou != null)
                foreach (ObjectBase o in trade.ItemsYou)
                    if (o != null) _rowsMine.AddChild(Row(o, (int)o.Count, true, m++ % 2 == 1));
            Empties();
            Layout();
        }

        var sb = Sig.Start();
        if (trade.ItemsPartner != null)
            foreach (ObjectBase o in trade.ItemsPartner)
                sb.Opt(o?.ID).Append(':').Opt(o?.Count).Append(':').Append(o?.Name).Append(':')
                  .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        if (!Sig.Changed(sb, ref _theirSignature)) return;

        foreach (Node n in _rowsTheirs.GetChildren()) { _rowsTheirs.RemoveChild(n); n.QueueFree(); }
        int t = 0;
        if (trade.ItemsPartner != null)
            foreach (ObjectBase o in trade.ItemsPartner)
                if (o != null) _rowsTheirs.AddChild(Row(o, (int)o.Count, false, t++ % 2 == 1));
        Empties();
        Layout();
    }

    void Clear()
    {
        _amounts.Clear();
        if (_notice != null) { _notice.Text = ""; _notice.Visible = false; }
        _theirSignature = "";
        _mineSignature = "";
        foreach (Node n in _rowsMine.GetChildren()) { _rowsMine.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in _rowsTheirs.GetChildren()) { _rowsTheirs.RemoveChild(n); n.QueueFree(); }
        Empties();
    }

    /// <summary>
    /// The row's name, with the game's own suffix on anything its owner
    /// is wearing or wielding: " (in use)", from EN_MISC
    /// (`Language.cpp:118`, appended at `UITrade.cpp:203`). In a trade
    /// this is not decoration - it says which of their things are
    /// actually on them.
    /// </summary>
    static string Named(ObjectBase o)
    {
        string name = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name;
        return o.Flags != null && o.Flags.IsEquipped ? name + " (in use)" : name;
    }

    Control Row(ObjectBase o, int count, bool mine, bool alt)
    {
        // The row sits inside a button so it can be held. The game
        // looks at a trade row on a right click, on both sides
        // (`UITrade.cpp:441`, `:453`), and knowing what someone is
        // actually offering you before you accept it is the whole
        // point. A tap does nothing: the game has no left-click action
        // here either, and a tap that threw a panel over the trade
        // would be worse than none.
        var press = new Button
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            Name = (mine ? "myrow" : "theirrow") + o.ID,
        };
        // Alternating tints, so the eye keeps its place down a column
        // of near-identical lines. No picked state: a tap here does
        // nothing by design (see above), so there is nothing to mark.
        M59Skin.Dress(press, alt ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
        press.ButtonDown += () => _downAt = Time.GetTicksMsec();
        press.Pressed += () =>
        {
            ulong down = _downAt;
            _downAt = 0;
            if (down != 0 && Time.GetTicksMsec() - down >= LongPressMs) Look?.Invoke(o.ID);
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.OffsetLeft = 6; line.OffsetTop = 2; line.OffsetRight = -6; line.OffsetBottom = -2;
        line.AddThemeConstantOverride("separation", 8);
        press.AddChild(line);

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
            Text = Named(o),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
        name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
        // The name keeps a floor and is cut past it, now that Inspect
        // sits on the row's end - see M59Skin.NameMin. This matters more
        // here than on the one-column lists: a trade row is half a card.
        M59Skin.NameFits(name);
        line.AddChild(name);

        if (mine && o.IsStackable)
        {
            // How many of the stack to offer. The game keeps this on
            // the row and reads it when you press Offer, warning in its
            // own comment not to trust the object's count
            // (`UITrade.cpp:398-414`) - the object here is the one in
            // your pack, and its count is what you own, not what you
            // are parting with.
            var many = new Button
            {
                Text = $"x{Offering(o)}",
                Name = $"mine{o.ID}",
                TooltipText = "How many",
            };
            // A stepper, not a slab: a small square that opens the
            // amount prompt, and it says so by looking like one.
            many.CustomMinimumSize = new Vector2(72, 0);
            M59Skin.Dress(many, M59Skin.Kind.Step);
            many.Pressed += () => AmountWanted?.Invoke(o);
            line.AddChild(many);
        }
        else if (count > 1)
        {
            var many = new Label
            {
                Text = $"x{count}",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                CustomMinimumSize = new Vector2(72, 0),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            many.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
            many.AddThemeColorOverride("font_color", M59Skin.Gold);
            line.AddChild(many);
        }

        // Inspect: the touch-screen form of the right click that looks
        // at a trade row on either side (`UITrade.cpp:440-442`, `:452-
        // 454`). Selling to a shopkeeper IS a trade in this game, so
        // this is also how you see what you are selling - and what they
        // are actually offering you before you accept it. Same Look,
        // same id; the row itself still does nothing on a tap - see
        // M59Skin.Inspect.
        line.AddChild(M59Skin.Inspect($"inspect{press.Name}", () => Look?.Invoke(o.ID)));

        return press;
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
        catch (Exception e) { GD.PrintErr($"[TradePanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
