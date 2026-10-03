using System;
using Godot;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// What you are carrying in coin, beside the condition bars.
///
/// DIVERGENCE, and a deliberate one. The reference has no money
/// readout at all: coin is an inventory item like any other
/// (`ObjectID.Count` is the stack's amount, `ObjectID.cs:182-186`),
/// and the only way to know how much you have is to open the pack and
/// look. On a desktop that is one window away. On a phone the pack
/// covers the whole screen and the world with it, so "can I afford
/// this" costs you sight of the room you are standing in. The four
/// numbers are small and they are read often, which is exactly the
/// case for putting them on the HUD.
///
/// WHAT IT COUNTS. The server does not label an item "currency" - it
/// is a name and a count - so the names are matched, case-insensitively
/// and by substring, because a stack reaches the client with its
/// article and its plural attached: "a gold doubloon", "12 shillings".
/// Substring is right here and would be wrong almost anywhere else; it
/// is why the match is spelled out rather than hidden in a helper, so
/// the next person can see what it will and will not catch.
///
/// A count of zero means NOT STACKABLE (`ObjectID.IsStackable`), which
/// for a coin means a single one - so it counts as one rather than as
/// nothing. Several stacks of the same coin add up.
///
/// ONLY WHAT YOU CARRY IS LISTED. Ashton: "only display currencies you
/// have on your person. so if i only have shillings only show
/// shillings." A row with none of that coin is not drawn, and the plate
/// shrinks to the rows it has; with no coin at all the plate is not
/// drawn either. The rows keep their order (shillings, silver,
/// doubloons, souls), so a coin that arrives slots in where it belongs
/// rather than at the end. In the HUD editor an empty purse keeps a
/// one-row natural rect so it is still a handle the player can pick up
/// and place, as RoomBuffsPanel does for an empty row - a piece with a
/// zero rect cannot be picked (HudEditor.Drawn).
///
/// SOULS are money on Server 104: `Souls is Money`, named "contained
/// soul" / "contained souls" (`kod/object/item/passitem/numbitem/money/
/// souls.kod:19,26`), so "soul" is the word to match.
/// </summary>
public partial class Purse : Control
{
    [Export] public int FontSize = 13;

    /// <summary>The gutter the names sit in, matching the bars' own.</summary>
    [Export] public float NameW = 74f;
    [Export] public float ValueW = 64f;
    [Export] public float RowH = 20f;
    [Export] public float Pad = 8f;

    /// <summary>
    /// The coins, in the order he asked for them, each with the word to
    /// look for in an item's name.
    /// </summary>
    static readonly (string Label, string Match)[] Coins =
    {
        ("shillings", "shilling"),
        // silver.kod:19-20 - "silver coin(s)", with platinum.bgf for a
        // picture, which is how it came to be called platinum here.
        ("silver",    "silver coin"),
        ("doubloons", "doubloon"),
        ("souls",     "soul"),
    };

    /// <summary>Where the block sits: to the right of the vitals plate.</summary>
    public float Left { get; set; } = 300f;
    public float Top { get; set; } = 14f;

    DataController _data;
    readonly Label[] _names = new Label[Coins.Length];
    readonly Label[] _values = new Label[Coins.Length];
    readonly long[] _held = new long[Coins.Length];
    string _signature = "";
    M59Hud.Stamp _stamp;
    float _dressedAt;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        for (int i = 0; i < Coins.Length; i++)
        {
            _names[i] = new Label { MouseFilter = MouseFilterEnum.Ignore, Name = $"purseName{i}" };
            _names[i].AddThemeColorOverride("font_color", M59Skin.TextDim);
            AddChild(_names[i]);

            _values[i] = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                Name = $"purseValue{i}",
            };
            _values[i].AddThemeColorOverride("font_color", M59Skin.Text);
            AddChild(_values[i]);
        }

        // Its own piece in the layout store, so it can be moved, resized,
        // faded or hidden like everything else on the HUD. See M59Hud.
        M59Hud.Register("purse", "Coin", this);
        M59Hud.Changed += QueueRedraw;
    }

    public override void _ExitTree() => M59Hud.Changed -= QueueRedraw;

    static M59Hud.Stamp HudStamp() => M59Hud.StampOf("purse");

    static float HudScale()
    {
        M59Hud.Piece p = M59Hud.Get("purse");
        return p == null ? 1f : Mathf.Clamp(p.Scale, M59Hud.MinScale, M59Hud.MaxScale);
    }

    static int Pt(int at1, float scale) => Mathf.Max(8, Mathf.RoundToInt(at1 * scale));

    /// <summary>
    /// Counts the coin, every frame, and redraws only when a number has
    /// actually moved. The same shape as Vitals.Follow: the inventory
    /// changes for a hundred reasons that are not money.
    /// </summary>
    public void Follow(DataController data)
    {
        _data = data;
        M59Hud.Dress("purse");
        M59Hud.Stamp stamp = HudStamp();
        if (stamp != _stamp) { _stamp = stamp; QueueRedraw(); }

        for (int i = 0; i < Coins.Length; i++) _held[i] = 0;

        if (data?.InventoryObjects != null)
        {
            foreach (InventoryObject o in data.InventoryObjects)
            {
                string name = o?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                for (int i = 0; i < Coins.Length; i++)
                {
                    if (name.IndexOf(Coins[i].Match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Zero means not stackable, which for a coin is one
                    // of it rather than none.
                    _held[i] += o.Count > 0 ? o.Count : 1;
                    break;
                }
            }
        }

        var sb = Sig.Start();
        for (int i = 0; i < Coins.Length; i++) sb.Append(_held[i]).Append(';');
        if (!Sig.Changed(sb, ref _signature)) return;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_data == null) { HideRows(); return; }
        if (!M59Hud.Shows("purse")) { HideRows(); return; }

        float sc = HudScale();
        Redress(sc);
        float pad = Pad * sc, nameW = NameW * sc, valueW = ValueW * sc, rowH = RowH * sc;

        int rows = 0;
        for (int i = 0; i < Coins.Length; i++) if (_held[i] > 0) rows++;

        // Empty: nothing on the glass, but in the editor a one-row rect
        // so the piece can still be picked up (see the class comment).
        // Place is called either way so the store's Natural follows the
        // plate's real size - an editor handle over a stale rect would be
        // a handle over nothing.
        int shape = rows > 0 ? rows : (M59Hud.Editing ? 1 : 0);
        float plateW = pad + nameW + valueW + pad;
        float plateH = pad * 2f + rowH * shape;
        Rect2 at = M59Hud.Place("purse",
                                new Rect2(Left, Top, shape > 0 ? plateW : 0f, shape > 0 ? plateH : 0f),
                                GetViewportRect().Size);
        if (shape == 0) { HideRows(); return; }

        DrawStyleBox(Vitals.Plate(), new Rect2(at.Position, at.Size));

        float row = at.Position.Y + pad;
        for (int i = 0; i < Coins.Length; i++)
        {
            if (_held[i] <= 0)
            {
                _names[i].Visible = false;
                _values[i].Visible = false;
                continue;
            }
            _names[i].Text = Coins[i].Label;
            _names[i].Position = new Vector2(at.Position.X + pad, row);
            _names[i].Size = new Vector2(nameW, rowH);
            _names[i].Visible = true;

            _values[i].Text = _held[i].ToString("N0");
            _values[i].Position = new Vector2(at.Position.X + pad + nameW, row);
            _values[i].Size = new Vector2(valueW, rowH);
            _values[i].Visible = true;

            row += rowH;
        }
    }

    /// <summary>
    /// The font size the labels were last built at. A Godot Label keeps
    /// whatever size it was given, so a scale change has to re-apply it
    /// or the plate grows and the digits on it do not.
    /// </summary>
    void Redress(float scale)
    {
        if (Mathf.IsEqualApprox(_dressedAt, scale)) return;
        _dressedAt = scale;
        for (int i = 0; i < Coins.Length; i++)
        {
            _names[i].AddThemeFontSizeOverride("font_size", Pt(FontSize, scale));
            _values[i].AddThemeFontSizeOverride("font_size", Pt(FontSize + 1, scale));
        }
    }

    /// <summary>
    /// Named HideRows, not Hide: CanvasItem already has a Hide() that
    /// makes the CONTROL invisible, and a same-named method here would
    /// have meant two different things depending on the type of the
    /// variable it was called through - the trap AvatarPanel.HeadSize
    /// records.
    /// </summary>
    void HideRows()
    {
        for (int i = 0; i < Coins.Length; i++)
        {
            if (_names[i] != null) _names[i].Visible = false;
            if (_values[i] != null) _values[i].Visible = false;
        }
    }
}
