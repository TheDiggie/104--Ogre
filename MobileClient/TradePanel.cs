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
    /// <summary>
    /// Air under the last button row. Without it the button's bottom
    /// edge and the panel's own are the same line, and the only way out
    /// of the window reads as cut off while every row above it has a
    /// gap.
    /// </summary>
    const float Foot = 12f;

    [Export] public int FontSize = 16;
    [Export] public int IconSize = 36;
    [Export] public int RowHeight = 48;

    /// <summary>Send what is on your side.</summary>
    public event Action<List<ObjectID>> Offer;
    public event Action Accept;
    public event Action Cancel;
    /// <summary>Open the bag to pick something to add.</summary>
    public event Action AddWanted;

    /// <summary>How many of this stackable of yours to offer.</summary>
    public event Action<ObjectBase> AmountWanted;

    /// <summary>Describe this row - raised by a hold, the right click's stand-in.</summary>
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
    Label _title, _mine, _theirs;
    ScrollContainer _scrollMine, _scrollTheirs;
    VBoxContainer _rowsMine, _rowsTheirs;
    Button _add, _offer, _accept, _cancel;

    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _theirSignature = "";
    string _mineSignature = "";
    TradeInfo _trade;

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Opaque: a FrameWindow with no Alpha (Meridian59.layout:1585, UITrade.cpp:8).
        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 1f), Visible = false };
        AddChild(_panel);

        _title = Head("Trade", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _mine = Head("You offer", FontSize, new Color(0.8f, 0.85f, 1f));
        _theirs = Head("They offer", FontSize, new Color(1, 0.85f, 0.8f));

        _rowsMine = new VBoxContainer();
        _rowsMine.AddThemeConstantOverride("separation", 3);
        // The rows span the panel: a ScrollContainer sizes its child
        // to that child's MINIMUM width unless it asks to expand, so
        // without this the list is only as wide as its longest line and
        // every column after the name lands wherever that row's text
        // ended. Same one line in SpellsPanel, where it was found.
        _rowsMine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollMine = new ScrollContainer { Visible = false };
        _scrollMine.AddChild(_rowsMine);
        AddChild(_scrollMine);

        _rowsTheirs = new VBoxContainer();
        _rowsTheirs.AddThemeConstantOverride("separation", 3);
        _rowsTheirs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollTheirs = new ScrollContainer { Visible = false };
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
            var send = new List<ObjectID>();
            if (_trade?.ItemsYou != null)
                foreach (ObjectBase o in _trade.ItemsYou)
                    if (o != null) send.Add(new ObjectID(o.ID, Offering(o)));
            Offer?.Invoke(send);
        });
        _accept = Act("Accept", () => Accept?.Invoke());
        // The game sends the cancel only when a trade is actually
        // pending, and clears its own side either way
        // (`UITrade.cpp:490-495`). Sending unconditionally asks the
        // server to cancel something it has no record of, which it
        // answers by doing nothing - leaving IsVisible set and the
        // window up, so Cancel looked broken whenever you had opened
        // the window on a nearby player and nobody had offered yet.
        _cancel = Act("Cancel", () =>
        {
            if (_trade != null && _trade.IsPending) Cancel?.Invoke();
            _trade?.Clear(true);
            Show(false);
            Clear();
        });

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Head(string text, int size, Color colour)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        AddChild(l);
        return l;
    }

    Button Act(string text, Action pressed)
    {
        var b = new Button { Text = text, Visible = false };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        foreach (Node n in GetChildren())
            if (n is Control c && c != this) c.Visible = on;
        _panel.Visible = on;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Mathf.Max(12f, v.X * 0.04f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.78f, 780f);
        float top = v.Y - height - side;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        _title.Position = new Vector2(side, top);
        _title.Size = new Vector2(w, rowH);

        float listTop = top + rowH;
        float listH = (height - rowH * 3f - 28f) / 2f;

        _mine.Position = new Vector2(side, listTop);
        _mine.Size = new Vector2(w, rowH * 0.8f);
        _scrollMine.Position = new Vector2(side, listTop + rowH * 0.8f);
        _scrollMine.Size = new Vector2(w, listH);
        _rowsMine.CustomMinimumSize = new Vector2(w, 0);

        float theirTop = listTop + rowH * 0.8f + listH + 8f;
        _theirs.Position = new Vector2(side, theirTop);
        _theirs.Size = new Vector2(w, rowH * 0.8f);
        _scrollTheirs.Position = new Vector2(side, theirTop + rowH * 0.8f);
        _scrollTheirs.Size = new Vector2(w, listH);
        _rowsTheirs.CustomMinimumSize = new Vector2(w, 0);

        float y = top + height - rowH - Foot;
        Button[] row = { _add, _offer, _accept, _cancel };
        float bw = (w - 6f * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(side + i * (bw + 6f), y);
            row[i].Size = new Vector2(bw, rowH);
        }
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
        var mine = new System.Text.StringBuilder();
        if (trade.ItemsYou != null)
            foreach (ObjectBase o in trade.ItemsYou)
                mine.Append(o?.ID).Append(':').Append(o == null ? 0u : Offering(o)).Append(':').Append(o?.Name).Append(':')
                    .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        string nowMine = mine.ToString();
        if (nowMine != _mineSignature)
        {
            _mineSignature = nowMine;
            foreach (Node n in _rowsMine.GetChildren()) { _rowsMine.RemoveChild(n); n.QueueFree(); }
            if (trade.ItemsYou != null)
                foreach (ObjectBase o in trade.ItemsYou)
                    if (o != null) _rowsMine.AddChild(Row(o, (int)o.Count, true));
        }

        var sb = new System.Text.StringBuilder();
        if (trade.ItemsPartner != null)
            foreach (ObjectBase o in trade.ItemsPartner)
                sb.Append(o?.ID).Append(':').Append(o?.Count).Append(':').Append(o?.Name).Append(':')
                  .Append(o?.Flags != null && o.Flags.IsEquipped).Append(';');
        string now = sb.ToString();
        if (now == _theirSignature) return;
        _theirSignature = now;

        foreach (Node n in _rowsTheirs.GetChildren()) { _rowsTheirs.RemoveChild(n); n.QueueFree(); }
        if (trade.ItemsPartner != null)
            foreach (ObjectBase o in trade.ItemsPartner)
                if (o != null) _rowsTheirs.AddChild(Row(o, (int)o.Count, false));
    }

    void Clear()
    {
        _amounts.Clear();
        _theirSignature = "";
        _mineSignature = "";
        foreach (Node n in _rowsMine.GetChildren()) { _rowsMine.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in _rowsTheirs.GetChildren()) { _rowsTheirs.RemoveChild(n); n.QueueFree(); }
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

    Control Row(ObjectBase o, int count, bool mine)
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
            Flat = true,
            Name = (mine ? "myrow" : "theirrow") + o.ID,
        };
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
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
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
            many.AddThemeFontSizeOverride("font_size", FontSize);
            many.Pressed += () => AmountWanted?.Invoke(o);
            line.AddChild(many);
        }
        else if (count > 1)
        {
            var many = new Label
            {
                Text = $"x{count}",
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            many.AddThemeFontSizeOverride("font_size", FontSize);
            many.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.76f));
            line.AddChild(many);
        }

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
