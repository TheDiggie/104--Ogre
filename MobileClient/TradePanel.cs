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

    /// <summary>Send what is on your side.</summary>
    public event Action<List<ObjectID>> Offer;
    public event Action Accept;
    public event Action Cancel;
    /// <summary>Open the bag to pick something to add.</summary>
    public event Action AddWanted;

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

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.96f), Visible = false };
        AddChild(_panel);

        _title = Head("Trade", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _mine = Head("You offer", FontSize, new Color(0.8f, 0.85f, 1f));
        _theirs = Head("They offer", FontSize, new Color(1, 0.85f, 0.8f));

        _rowsMine = new VBoxContainer();
        _rowsMine.AddThemeConstantOverride("separation", 3);
        _scrollMine = new ScrollContainer { Visible = false };
        _scrollMine.AddChild(_rowsMine);
        AddChild(_scrollMine);

        _rowsTheirs = new VBoxContainer();
        _rowsTheirs.AddThemeConstantOverride("separation", 3);
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
        _offer = Act("Offer", () =>
        {
            var send = new List<ObjectID>();
            if (_trade?.ItemsYou != null)
                foreach (ObjectBase o in _trade.ItemsYou)
                    if (o != null) send.Add(new ObjectID(o.ID, o.Count > 0 ? o.Count : 1));
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

        float y = top + height - rowH;
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
        if (_trade.ItemsYou.Contains(item)) return;
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
                mine.Append(o?.ID).Append(':').Append(o?.Count).Append(':').Append(o?.Name).Append(';');
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
            foreach (ObjectBase o in trade.ItemsPartner) sb.Append(o?.ID).Append(':').Append(o?.Count).Append(';');
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
        _theirSignature = "";
        _mineSignature = "";
        foreach (Node n in _rowsMine.GetChildren()) { _rowsMine.RemoveChild(n); n.QueueFree(); }
        foreach (Node n in _rowsTheirs.GetChildren()) { _rowsTheirs.RemoveChild(n); n.QueueFree(); }
    }

    Control Row(ObjectBase o, int count, bool mine)
    {
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        line.AddThemeConstantOverride("separation", 8);

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
        name.AddThemeFontSizeOverride("font_size", FontSize);
        name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f));
        line.AddChild(name);

        if (count > 1)
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

        return line;
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
