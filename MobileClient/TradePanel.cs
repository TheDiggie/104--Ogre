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

    readonly List<ObjectID> _offering = new List<ObjectID>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _theirSignature = "";

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
        _offer = Act("Offer", () => Offer?.Invoke(new List<ObjectID>(_offering)));
        _accept = Act("Accept", () => Accept?.Invoke());
        _cancel = Act("Cancel", () => Cancel?.Invoke());

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

    /// <summary>Adds one of yours to the offer, with a count.</summary>
    public void Put(InventoryObject item)
    {
        if (item == null) return;
        foreach (ObjectID had in _offering) if (had.ID == item.ID) return;

        // The game reads the count off the row and treats an empty box
        // as one, deliberately not trusting the object's own count.
        _offering.Add(new ObjectID(item.ID, item.IsStackable ? item.Count : 1));
        _rowsMine.AddChild(Row(item, item.IsStackable ? (int)item.Count : 1, true));
    }

    /// <summary>
    /// Follows the trade the server set up. Your side is yours to fill,
    /// so only theirs is rebuilt from the model.
    /// </summary>
    public void Sync(TradeInfo trade)
    {
        if (_panel == null) return;

        if (trade == null || !trade.IsVisible)
        {
            if (IsOpen) { Show(false); Clear(); }
            return;
        }

        if (!IsOpen) { Show(true); Clear(); }

        string who = trade.TradePartner != null && !string.IsNullOrWhiteSpace(trade.TradePartner.Name)
            ? trade.TradePartner.Name : "someone";
        _title.Text = trade.IsBackgroundOffer ? $"{who} offers you a trade" : $"Trading with {who}";

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
        _offering.Clear();
        _theirSignature = "";
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
        string key = $"{o.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[TradePanel] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
