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
///
/// THE LOOK is the panels', not Godot's: seven default-grey buttons
/// over a lit floor are seven grey smudges, and the target's name -
/// which carries the game's own colour and is the thing you check
/// before you swing - was white-outlined text sitting straight on the
/// world. So the portrait and the name share a plate, the portrait
/// sits in a slot like an inventory item's, and the seven are the
/// button family, with Attack as the row's one primary. Everything
/// decided by the flags above is untouched: which buttons are live,
/// what Activate is called, and the name's colour.
/// </summary>
public partial class ActionBar : Control
{
    [Export] public int FontSize = 16;
    [Export] public int PortraitSize = 44;

    /// <summary>
    /// A button's height. The old FontSize * 2.6 came to 41.6, which is
    /// under the 44 points a thumb needs - and Attack is held down.
    /// </summary>
    float RowH => Mathf.Max(46f, FontSize * 2.6f);

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
                        TradeWith, LootTarget, AskQuests, Deselect;

    /// <summary>
    /// How tall the whole block is - portrait, name and the button row.
    /// Anything else that wants to sit above it has to know, or it
    /// lands on top of the portrait.
    /// </summary>
    public float BlockHeight => RowH + PortraitSize + 14f;

    /// <summary>
    /// Whether there is anything to act on. The row hides itself
    /// without one, and anything else that toggles this control has to
    /// respect that or the empty row comes back every frame.
    /// </summary>
    public bool HasTarget { get; private set; }

    Label _name;
    TextureRect _face;
    /// <summary>What the portrait and the name sit on. See the class note.</summary>
    Panel _plate;
    /// <summary>The portrait's own slot, so a face reads as a held thing.</summary>
    Panel _slot;
    Button _clear;
    Button _inspect, _attack, _activate, _buy, _trade, _loot, _quest;
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Plate, then slot, then face, then name: Godot draws siblings
        // in tree order, so anything added after a backing panel is
        // drawn over it and anything added before is covered by it.
        _plate = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _plate.AddThemeStyleboxOverride("panel", Plate());
        AddChild(_plate);

        _slot = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _slot.AddThemeStyleboxOverride("panel", M59Skin.Sunken());
        AddChild(_slot);

        _face = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_face);

        // Vertically centred against the portrait rather than dropped
        // at a hand-picked offset, so a one-line name and a two-word
        // one sit on the same line.
        _name = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _name.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _name.AddThemeConstantOverride("outline_size", 4);
        AddChild(_name);

        // Clears the target: the reference's Close key sets TargetID to
        // MaxValue (`ControllerInput.cpp:555-562`, line 560). A phone has no key.
        // The round close of a title bar, because that is what it is:
        // the one control here that dismisses rather than acts.
        _clear = Make("✕", () => Deselect?.Invoke(), M59Skin.Kind.Close);
        _clear.Name = "target_clear";
        _clear.TooltipText = "Clear target";

        _inspect  = Make("Look",   () => LookAt?.Invoke());
        // The one thing the row is for, in the family's one primary
        // dress. Not Danger: the panels spend that on destroying your
        // own things, and a red button among six grey ones reads as the
        // one you must not press.
        _attack   = Make("Attack", OnAttackPressed, M59Skin.Kind.Primary);
        _attack.Name = "target_attack";
        _attack.ButtonDown += () => { _attackDown = true; _attackSince = Time.GetTicksMsec(); _attackRepeating = false; };
        _attack.ButtonUp += EndAttackHold;
        _activate = Make("Open",   () => ActivateTarget?.Invoke());
        _buy      = Make("Buy",    () => BuyFrom?.Invoke());
        _trade    = Make("Trade",  () => TradeWith?.Invoke());
        _loot     = Make("Get",    () => LootTarget?.Invoke());
        _quest    = Make("Quest",  () => AskQuests?.Invoke());

        GetViewport().SizeChanged += Layout;
        Layout();
        SetTarget(null, 0);
    }

    // Hold-to-repeat on Attack. The reference reads the attack key every
    // input tick with isKeyDown(...)->Activate()
    // (`ControllerInput.cpp:995-1030`), so holding it keeps swinging, and
    // the only limiter is the library's interval (`GameTick.cs:304-308`,
    // INTERVALREQATTACK at :40-51, enforced in `BaseClient.cs:1522`), so
    // this just asks every frame and lets the library say no. A tap
    // still fires on release; the delay is what tells a tap from a hold.
    [Export] public ulong RepeatDelayMs = 250;
    bool _attackDown, _attackRepeating;
    ulong _attackSince;

    void OnAttackPressed()
    {
        // Pressed is the release. If the hold already swung, that
        // release is not one more tap. (A scripted press arrives with no
        // press-down and fires once.)
        bool repeated = _attackRepeating;
        EndAttackHold();
        if (!repeated) AttackTarget?.Invoke();
    }

    void EndAttackHold()
    {
        _attackDown = false;
        // Deferred: Pressed may come before or after ButtonUp.
        Callable.From(() => _attackRepeating = false).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (!_attackDown || !HasTarget || _attack.Disabled) return;
        if (Time.GetTicksMsec() - _attackSince < RepeatDelayMs) return;
        _attackRepeating = true;
        AttackTarget?.Invoke();
    }

    Button Make(string text, Action pressed, M59Skin.Kind kind = M59Skin.Kind.Secondary)
    {
        var b = new Button { Text = text, ClipText = true };
        M59Skin.Dress(b, kind);
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// What the portrait and the name sit on: the panels' card with
    /// their lit inner edge, nearly opaque. This row sits over the
    /// floor, and the floor in this game is as often pale stone as it
    /// is a dark cellar - a name in the server's own colour has to be
    /// readable against both, and several of those colours are light.
    /// </summary>
    static StyleBoxFlat Plate()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(M59Skin.Card.R, M59Skin.Card.G, M59Skin.Card.B, 0.88f),
            AntiAliasing = true,
        };
        s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
        s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = (int)M59Skin.Radius;
        s.BorderWidthTop = s.BorderWidthBottom = s.BorderWidthLeft = s.BorderWidthRight = 1;
        s.BorderColor = M59Skin.EdgeLit;
        s.ShadowColor = new Color(0, 0, 0, 0.5f);
        s.ShadowSize = 8;
        return s;
    }

    void Layout()
    {
        if (_name == null) return;
        Vector2 v = GetViewportRect().Size;
        float pad = 10f;
        float h = RowH;
        float y = v.Y - _reserve - pad - h;

        // Sideways the row keeps to the left, over the chat, rather
        // than stretching the seven buttons across two thousand pixels
        // with a thumb's width of gap between them. Same share of the
        // width the chat block takes, so the two line up.
        float block = v.X > v.Y ? Mathf.Min(v.X - pad * 2f, v.X * 0.52f) : v.X - pad * 2f;

        // The head of the row: portrait and name on one plate, with the
        // dismiss at its far end.
        const float inset = 6f, xs = 36f;
        float plateH = PortraitSize + inset * 2f;
        float plateY = y - plateH - 6f;

        _plate.Position = new Vector2(pad, plateY);
        _plate.Size = new Vector2(block, plateH);

        _slot.Position = new Vector2(pad + inset, plateY + inset);
        _slot.Size = new Vector2(PortraitSize, PortraitSize);
        // Inside the slot's rim, so the picture does not sit on it.
        _face.Position = _slot.Position + new Vector2(3f, 3f);
        _face.Size = new Vector2(PortraitSize - 6f, PortraitSize - 6f);

        float nameX = pad + inset + PortraitSize + 10f;
        _name.Position = new Vector2(nameX, plateY + inset);
        _name.Size = new Vector2(Mathf.Max(0f, pad + block - inset - xs - 8f - nameX), PortraitSize);

        _clear.Position = new Vector2(pad + block - inset - xs, plateY + (plateH - xs) * 0.5f);
        _clear.Size = new Vector2(xs, xs);

        Button[] row = { _inspect, _attack, _activate, _buy, _trade, _loot, _quest };
        const float gap = 6f;
        float w = (block - gap * (row.Length - 1)) / row.Length;
        for (int i = 0; i < row.Length; i++)
        {
            row[i].Position = new Vector2(pad + i * (w + gap), y);
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
        // The empty slot goes with it: an invisible object shows no
        // picture, and a lit slot with nothing in it reads as a picture
        // that failed to load.
        _slot.Visible = !hidden;
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
