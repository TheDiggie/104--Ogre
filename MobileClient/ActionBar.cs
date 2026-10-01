using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// What you can do to the thing you tapped.
///
/// This is the game's target window, `UITarget.cpp`: a picture of the
/// target, its name in the target's own colour, and a row of seven
/// buttons - Inspect, Attack, Activate, Buy, Trade, Loot, Quest. It
/// used to be four buttons of my own invention, which is why Use and
/// Get were here and Buy, Trade and Quest were not.
///
/// Every rule below is out of that file:
///
///  - the picture is the object composed with hotspot 0 and no Y
///    offset, front frame, centred - not the head. The comment there
///    says "hotspot=1 is head" and the line under it sets 0.
///  - the name is `NameColors::GetColorFor(flags)`.
///  - an invisible object shows neither name nor picture, unless it is
///    you.
///  - which buttons work is decided flag by flag: Attack needs
///    `IsAttackable`, Activate needs `IsActivatable` **or**
///    `IsContainer`, Buy needs `IsBuyable`, Trade needs `IsOfferable`,
///    Loot needs `IsGettable`, and Quest needs `IsNPCActiveQuest` or
///    `IsNPCHasQuests`. Inspect has no condition. The game greys them
///    rather than hiding them, which also tells you what the thing is.
///
/// Targeting itself is the library's: tapping sets
/// `DataController.TargetID`, and the no-argument `Send*` overloads act
/// on it. Nothing here keeps a second idea of what is selected.
///
/// The row hides itself when there is no target, because on a phone
/// every permanently visible control is screen the game does not get.
/// </summary>
public partial class ActionBar : Control
{
    [Export] public int FontSize = 16;
    [Export] public int PortraitSize = 44;

    /// <summary>
    /// Pixels at the bottom of the screen already spoken for - the chat
    /// log and its input line. The row sits above them. Widgets that each
    /// picked their own corner ended up on top of each other.
    /// </summary>
    public float BottomReserve
    {
        get => _reserve;
        set { _reserve = value; Layout(); }
    }
    float _reserve;

    // Named away from Godot's own members: a plain "Get" hides
    // GodotObject.Get, and "Show" would sit alongside CanvasItem.Show.
    public event Action LookAt, AttackTarget, ActivateTarget, BuyFrom,
                        TradeWith, LootTarget, AskQuests;

    /// <summary>
    /// How tall the whole block is - portrait, name and the button row.
    /// Anything else that wants to sit above it has to know, or it
    /// lands on top of the portrait.
    /// </summary>
    public float BlockHeight => FontSize * 2.6f + PortraitSize + 14f;

    /// <summary>
    /// Whether there is anything to act on. The row hides itself
    /// without one, and anything else that toggles this control has to
    /// respect that or the empty row comes back every frame.
    /// </summary>
    public bool HasTarget { get; private set; }

    Label _name;
    TextureRect _face;
    Button _inspect, _attack, _activate, _buy, _trade, _loot, _quest;
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _face = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_face);

        _name = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeFontSizeOverride("font_size", FontSize + 2);
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _name.AddThemeConstantOverride("outline_size", 4);
        AddChild(_name);

        _inspect  = Make("Look",   () => LookAt?.Invoke());
        _attack   = Make("Attack", () => AttackTarget?.Invoke());
        _activate = Make("Open",   () => ActivateTarget?.Invoke());
        _buy      = Make("Buy",    () => BuyFrom?.Invoke());
        _trade    = Make("Trade",  () => TradeWith?.Invoke());
        _loot     = Make("Get",    () => LootTarget?.Invoke());
        _quest    = Make("Quest",  () => AskQuests?.Invoke());

        GetViewport().SizeChanged += Layout;
        Layout();
        SetTarget(null, 0);
    }

    Button Make(string text, Action pressed)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    void Layout()
    {
        if (_name == null) return;
        Vector2 v = GetViewportRect().Size;
        float pad = 10f;
        float h = FontSize * 2.6f;
        float y = v.Y - _reserve - pad - h;

        // Sideways the row keeps to the left, over the chat, rather
        // than stretching the seven buttons across two thousand pixels
        // with a thumb's width of gap between them. Same share of the
        // width the chat block takes, so the two line up.
        float block = v.X > v.Y ? Mathf.Min(v.X - pad * 2f, v.X * 0.52f) : v.X - pad * 2f;

        _face.Position = new Vector2(pad, y - PortraitSize - 4f);
        _face.Size = new Vector2(PortraitSize, PortraitSize);

        _name.Position = new Vector2(pad + PortraitSize + 8f, y - PortraitSize + 6f);
        _name.Size = new Vector2(block - PortraitSize - 8f, h);

        Button[] row = { _inspect, _attack, _activate, _buy, _trade, _loot, _quest };
        float w = (block - 4f * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(pad + i * (w + 4f), y);
            row[i].Size = new Vector2(w, h);
        }
    }

    /// <summary>
    /// Follows the target. <paramref name="avatarId"/> is needed for the
    /// one exception the game makes: an invisible object shows nothing,
    /// unless it is you.
    ///
    /// Takes an ObjectBase rather than a RoomObject because that is what
    /// `DataController.TargetObject` is: the library resolves a target
    /// id against the room first and then against your own inventory,
    /// so a carried thing is as much a target as a creature, and the
    /// game's target window shows either.
    ///
    /// The object is listened to while it is the target.
    /// `UITarget.cpp` re-runs the colour, the invisibility rule and all
    /// seven buttons whenever the object's Flags or Name change, and
    /// without that the row freezes as it was at the moment you tapped:
    /// a monster you have just killed keeps Attack lit and Get greyed
    /// until you tap the corpse again, an NPC that gains a quest never
    /// lights its Quest button, and a player who turns outlaw keeps the
    /// old name colour.
    /// </summary>
    public void SetTarget(ObjectBase target, uint avatarId)
    {
        if (!ReferenceEquals(_target, target))
        {
            if (_target != null) _target.PropertyChanged -= OnTargetChanged;
            _target = target;
            if (_target != null) _target.PropertyChanged += OnTargetChanged;
        }
        _avatar = avatarId;

        HasTarget = target != null;
        Visible = HasTarget;
        if (!HasTarget) return;

        Refresh();
    }

    ObjectBase _target;
    uint _avatar;

    void OnTargetChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e == null) return;
        if (e.PropertyName != ObjectBase.PROPNAME_FLAGS && e.PropertyName != ObjectBase.PROPNAME_NAME)
            return;
        if (_target != null) Refresh();
    }

    void Refresh()
    {
        ObjectBase target = _target;
        uint avatarId = _avatar;

        bool hidden = target.ID != avatarId
                   && target.Flags != null
                   && target.Flags.Drawing == ObjectFlags.DrawingType.Invisible;

        _name.Text = hidden ? "" : (string.IsNullOrWhiteSpace(target.Name) ? "" : target.Name);
        _face.Visible = !hidden;
        _face.Texture = hidden ? null : Face(target);

        uint argb = target.Flags != null ? NameColors.GetColorFor(target.Flags) : NameColors.NORMAL;
        _name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f));

        ObjectFlags f = target.Flags;
        _inspect.Disabled  = false;
        _attack.Disabled   = f == null || !f.IsAttackable;
        _activate.Disabled = f == null || !(f.IsActivatable || f.IsContainer);
        _buy.Disabled      = f == null || !f.IsBuyable;
        _trade.Disabled    = f == null || !f.IsOfferable;
        _loot.Disabled     = f == null || !f.IsGettable;
        _quest.Disabled    = f == null || !(f.IsNPCActiveQuest || f.IsNPCHasQuests);

        // `SetTooltips`: the Activate control is labelled Items when
        // the target is a container. On a desktop that is a tooltip; on
        // a phone the caption is the only thing telling you whether the
        // button pulls a lever or opens a bag.
        _activate.Text = f != null && f.IsContainer ? "Items" : "Activate";
    }

    ImageTexture Face(ObjectBase o)
    {
        if (o?.Resource == null) return null;
        string key = $"{o.Resource.Filename}:{PortraitSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, PortraitSize)); }
        catch (Exception e) { GD.PrintErr($"[ActionBar] portrait: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }
}
