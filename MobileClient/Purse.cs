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
/// this" costs you sight of the room you are standing in. The three
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
/// A row is drawn even when you have none of that coin. A readout that
/// disappears is a readout you have to think about: three lines that
/// are always in the same place can be read with one glance, and zero
/// is an answer.
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
        ("platinum",  "platinum"),
        ("doubloons", "doubloon"),
    };

    /// <summary>Where the block sits: to the right of the vitals plate.</summary>
    public float Left { get; set; } = 300f;
    public float Top { get; set; } = 14f;

    DataController _data;
    readonly Label[] _names = new Label[Coins.Length];
    readonly Label[] _values = new Label[Coins.Length];
    readonly long[] _held = new long[Coins.Length];
    string _signature = "";
    string _stamp = "";
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

    static string HudStamp()
    {
        M59Hud.Piece p = M59Hud.Get("purse");
        if (p == null) return "";
        return $"{p.Offset.X},{p.Offset.Y},{p.Scale},{p.Alpha},{(p.Hidden ? 1 : 0)},{(M59Hud.Editing ? 1 : 0)}";
    }

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
        string stamp = HudStamp();
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

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Coins.Length; i++) sb.Append(_held[i]).Append(';');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_data == null) { HideRows(); return; }
        if (!M59Hud.Shows("purse")) { HideRows(); return; }

        float sc = HudScale();
        Redress(sc);
        float pad = Pad * sc, nameW = NameW * sc, valueW = ValueW * sc, rowH = RowH * sc;
        float plateW = pad + nameW + valueW + pad;
        float plateH = pad * 2f + rowH * Coins.Length;

        Rect2 at = M59Hud.Place("purse", new Rect2(Left, Top, plateW, plateH),
                                GetViewportRect().Size);
        DrawStyleBox(Vitals.Plate(), new Rect2(at.Position, at.Size));

        float row = at.Position.Y + pad;
        for (int i = 0; i < Coins.Length; i++)
        {
            _names[i].Text = Coins[i].Label;
            _names[i].Position = new Vector2(at.Position.X + pad, row);
            _names[i].Size = new Vector2(nameW, rowH);
            _names[i].Visible = true;

            _values[i].Text = _held[i].ToString("N0");
            // Dim at nothing, bright when there is some: a zero you are
            // not meant to act on should not pull the eye like a number
            // that changed.
            _values[i].AddThemeColorOverride("font_color",
                _held[i] > 0 ? M59Skin.Text : M59Skin.TextOff);
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
