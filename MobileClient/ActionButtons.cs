using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data;
using Meridian59.Data.Models;

/// <summary>
/// The action buttons: the row of spells, skills, items and commands you
/// keep to hand.
///
/// The game has a grid of these - `UIActionButtons.cpp`, twelve across
/// and four down - filled from the player's own configuration and drawn
/// with the item's composed icon. This client had nothing of the sort;
/// the row it did have is a different thing, the actions you can take on
/// whatever you tapped.
///
/// What a button does is not decided here. Each one is an
/// `ActionButtonConfig` in the client's own list, and calling
/// <c>Activate</c> on it is what fires it: `BaseClient` is subscribed and
/// dispatches by type - a spell casts, a skill performs, an item is used,
/// applied or unused, an action runs, an alias runs as a chat command.
/// Reimplementing that dispatch here would be a second copy of five
/// rules, and the one in the library is the one the game uses.
///
/// Twelve by four does not fit a phone, so this shows one row of however
/// many fit across, the first buttons that are set. The rest are still
/// there in the configuration.
/// </summary>
public partial class ActionButtons : Control
{
    [Export] public int FontSize = 13;
    [Export] public int IconSize = 40;
    [Export] public int ButtonSize = 72;

    /// <summary>
    /// Pixels at the bottom already spoken for. Setting it relays out:
    /// the row is rebuilt from a signature, and a reserve that changed
    /// without touching the signature used to leave the buttons where
    /// they were - which is how they ended up drawn over the menu row.
    /// </summary>
    public float BottomReserve
    {
        get => _reserve;
        set
        {
            if (Mathf.IsEqualApprox(_reserve, value)) return;
            _reserve = value;
            _signature = "";
        }
    }
    float _reserve;

    /// <summary>
    /// Pixels at the left already spoken for - the chat block, when the
    /// screen is wide enough that the chat sits beside the hotbar
    /// rather than under it. Setting it relays out, the same way
    /// BottomReserve does.
    /// </summary>
    public float LeftReserve
    {
        get => _left;
        set
        {
            if (Mathf.IsEqualApprox(_left, value)) return;
            _left = value;
            _signature = "";
        }
    }
    float _left;

    DataController _data;
    readonly List<Button> _pool = new List<Button>();
    /// <summary>The button number showing in each screen slot, so a press
    /// can resolve what it fires at the moment it happens.</summary>
    readonly List<int> _nums = new List<int>();
    readonly Dictionary<string, ImageTexture> _icons = new Dictionary<string, ImageTexture>();
    string _signature = "";
    Meridian59.Data.Lists.ActionButtonList _list;

    /// <summary>
    /// Watches the client's button list, the way `UIActionButtons.cpp:19`
    /// does.
    ///
    /// This is not decoration over the polling below - it is the only
    /// thing that sees most changes. A button is bound to an item, a
    /// spell or a skill by the data controller as those arrive from the
    /// server, and every one of those paths matches the button *by name*
    /// and then calls SetToItem / SetToSpell / SetToSkill. Those set the
    /// type and the name as plain fields and only the data as a property,
    /// so a poll that compares number, type and name sees nothing change
    /// and never redraws - which is exactly the case where the button
    /// finally has an icon to show.
    ///
    /// BaseList re-raises any item's PropertyChanged as an ItemChanged on
    /// the list, so one subscription here covers both the list gaining
    /// and losing buttons and an individual button being rebound.
    /// </summary>
    public void Follow(DataController data)
    {
        if (data?.ActionButtons == null || ReferenceEquals(_list, data.ActionButtons)) return;
        if (_list != null) _list.ListChanged -= OnButtonsChanged;
        _list = data.ActionButtons;
        _list.ListChanged += OnButtonsChanged;
        _signature = "";
    }

    void OnButtonsChanged(object sender, System.ComponentModel.ListChangedEventArgs e) => _signature = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        GetViewport().SizeChanged += () => { _signature = ""; Sync(_data); };
    }

    /// <summary>
    /// Puts a starting set of buttons in the client's list if it is empty.
    ///
    /// This part is **not** mirrored, because there is nothing to mirror:
    /// the game ships no default buttons at all. Its list comes from the
    /// player's own configuration file, written by a UI this client does
    /// not have yet, and an empty row on a phone is a row of nothing.
    /// These are the library's own avatar actions, which
    /// `BaseClient.ExecAction` already knows how to run.
    ///
    /// Item buttons bind themselves: the data controller matches a button
    /// by name against the inventory as it arrives, so a button named for
    /// an item you are carrying becomes that item.
    /// </summary>
    public static void Seed(DataController data)
    {
        if (data?.ActionButtons == null || data.ActionButtons.Count > 0) return;

        // What this character had last time wins over the starting set.
        // The game loads its saved buttons at exactly this point, when a
        // character is chosen (`OgreClient.cpp:984`), and falls back to
        // a default set when there are none.
        if (HotbarStore.Load(data)) return;

        AvatarAction[] starting =
        {
            AvatarAction.Rest,
            AvatarAction.Attack,
            AvatarAction.Loot,
            AvatarAction.Activate,
            AvatarAction.Inspect,
            AvatarAction.Buy,
            AvatarAction.Trade,
            AvatarAction.Wave,
        };

        int num = 0;
        foreach (AvatarAction a in starting)
        {
            // SetToAction, not the constructor: the constructor leaves
            // Data null, and BaseClient.OnActionButtonActivated does
            // nothing at all for a button whose Data is null - the name
            // is a label, the Data is what the press dispatches on.
            var cfg = new ActionButtonConfig(num++, ActionButtonType.Action, a.ToString());
            cfg.SetToAction(a);
            data.ActionButtons.Add(cfg);
        }
    }

    /// <summary>
    /// Binds a spell, a skill or an item to a button - the phone's
    /// stand-in for the game's drag and drop.
    ///
    /// In the game every one of the 48 slots is on screen at once, empty
    /// ones included, and you drop a spell from the spell list or an item
    /// from the inventory onto whichever one you want
    /// (`UIActionButtons.cpp:359-438`). None of that survives the port:
    /// the lists you would drag from cover the whole screen on a phone,
    /// so the hotbar you would drop onto is not even visible while you
    /// are holding the thing to drop. The list panels carry a button per
    /// row instead, and this is what it calls.
    ///
    /// The slot chosen is the first unset one, and a new one is appended
    /// when they are all taken. That is the part the game does not have
    /// to decide, because its grid is a fixed 48 and ours is however many
    /// fit across one row.
    ///
    /// The setters are the library's, so what a bound button then does
    /// when pressed is the dispatch in BaseClient - not a second copy of
    /// it here.
    /// </summary>
    /// <summary>
    /// The slot the last successful <see cref="Bind"/> used, so the row
    /// can turn to the page it landed on. The game has room for all 48
    /// at once (`UIActionButtons.cpp:23-26`) and never needs this.
    /// </summary>
    public static int LastBound { get; private set; } = -1;

    public static bool Bind(DataController data, object what)
    {
        if (data?.ActionButtons == null || what == null) return false;

        ActionButtonConfig slot = null;
        int next = 0;
        foreach (ActionButtonConfig b in data.ActionButtons)
        {
            if (b == null) continue;
            if (b.Num >= next) next = b.Num + 1;
            if (slot == null && b.ButtonType == ActionButtonType.Unset) slot = b;
        }

        if (slot == null)
        {
            slot = new ActionButtonConfig(next, ActionButtonType.Unset, "");
            data.ActionButtons.Add(slot);
        }

        switch (what)
        {
            case SpellObject spell:    slot.SetToSpell(spell); break;
            // A passive skill cannot go on a button. The reference does
            // not offer the drag in the first place - a non-active skill's
            // row is built from the non-draggable window type, with no
            // CEGUI::DragContainer around its icon
            // (`Meridian59.Ogre.Client/UISkills.cpp:67-86`) - and the
            // library refuses to perform one even if it somehow got there
            // (`Meridian59/Client/BaseClient.cs:1841-1853`). SpellsPanel
            // already withholds the row's bind button for these, so this is
            // the same refusal said once more at the place that actually
            // writes the slot: every other caller of Bind hands it a spell,
            // an item or an action, and a bound passive skill would be a
            // button that silently does nothing forever.
            case SkillObject skill when !skill.IsActiveSkill: return false;
            case SkillObject skill:    slot.SetToSkill(skill); break;
            case InventoryObject item: slot.SetToItem(item);   break;
            // The actions window binds these. SetToAction, not the
            // constructor - see Seed: a config with a null Data is a
            // button BaseClient's dispatch does nothing for.
            case AvatarAction act:     slot.SetToAction(act);   break;
            // An alias, dragged out of the alias page in the reference:
            // the row carries a CEGUI::DragContainer
            // (`Meridian59.Ogre.Client/UIOptions.cpp:1070`, its icon set
            // at `:1082`) and dropping it on a slot ends in
            // `buttonModels[indexbutton]->SetToAlias(aliasModels[index])`
            // (`UIActionButtons.cpp:427-434`). So the same library
            // setter is called here, with the same object: the
            // KeyValuePairString that lives in Config.Aliases, not a
            // copy of it. That identity is the point - `SetToAlias`
            // hangs the pair itself on Data (`ActionButtonConfig.cs:267-278`)
            // and BaseClient reads `.Value` off it at the press
            // (`BaseClient.cs:292-296`), so editing the alias in the
            // editor changes what the button sends, with no rebinding.
            case KeyValuePairString alias: slot.SetToAlias(alias); break;
            default: return false;
        }

        LastBound = slot.Num;
        HotbarStore.Save(data);
        return true;
    }

    /// <summary>
    /// Rebuilds the row when the configured buttons change. Called every
    /// frame; a signature keeps it from rebuilding for nothing.
    /// </summary>
    public void Sync(DataController data)
    {
        _data = data;
        if (data?.ActionButtons == null) { HideFrom(0); return; }

        var set = new List<ActionButtonConfig>();
        foreach (ActionButtonConfig b in data.ActionButtons)
            if (b != null && b.ButtonType != ActionButtonType.Unset) set.Add(b);

        Vector2 v = GetViewportRect().Size;
        float gap = 6f;
        int across = Math.Max(1, (int)((v.X - LeftReserve - gap) / (ButtonSize + gap)));

        // The game draws all forty-eight buttons at once, twelve by four
        // (`UIActionButtons.cpp:23-26`). A phone has one row, and what
        // this did was draw the first `across` of them and silently drop
        // the rest - so once the row was full, binding a spell from the
        // book said "it is on the hotbar" and nothing appeared, and the
        // only way to reach it was to turn the device sideways. The
        // bindings were saved the whole time; they were just invisible.
        //
        // So the row pages. The last cell becomes the page button when
        // there is more than one page, which costs a slot and is worth
        // it: a button you cannot see is worth less than none.
        bool paged = set.Count > across;
        int perPage = paged ? Math.Max(1, across - 1) : across;
        int pages = paged ? (set.Count + perPage - 1) / perPage : 1;

        // A binding just made is worth more than whatever page you were
        // on: turn to it, so pressing "+" in the spell book shows you
        // where the spell went.
        if (LastBound >= 0)
        {
            int at = set.FindIndex(b => b.Num == LastBound);
            if (at >= 0) _page = at / perPage;
            LastBound = -1;
        }

        if (_page >= pages) _page = pages - 1;
        if (_page < 0) _page = 0;

        int first = _page * perPage;
        int count = Math.Min(perPage, Math.Max(0, set.Count - first));

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append(set[first + i].Num).Append(':').Append(set[first + i].ButtonType)
              .Append(':').Append(set[first + i].Name).Append(';');
        sb.Append('@').Append(across).Append('@').Append((int)LeftReserve)
          .Append('@').Append(_page).Append('/').Append(pages);

        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _nums.Clear();

        float y = v.Y - BottomReserve - ButtonSize - 8f;
        for (int i = 0; i < count; i++)
        {
            ActionButtonConfig cfg = set[first + i];
            Button b = Take(i);

            // Label is an empty string rather than null when unset, so a
            // null-coalesce picks the blank one and every button reads "?".
            Texture2D icon = Icon(cfg);
            b.Icon = icon;
            // Every alias shares one picture, so the picture alone says
            // "an alias" and never which one. The reference can afford
            // that because the slot carries a tooltip with the name in
            // it (`UIActionButtons.cpp:177-183`) and a mouse to hover
            // with; a thumb has neither. So an alias button shows the
            // icon AND its key, which is the one departure here, and the
            // room for the text is bought by keeping the sprite at its
            // native 24 pixels rather than expanding it.
            bool isAlias = cfg.ButtonType == ActionButtonType.Alias;
            b.ExpandIcon = false;
            // Named by the button's number so a scripted run can press
            // one. A bound spell or item shows a picture and no text, so
            // until now there was nothing to find those by at all - the
            // ones worth testing were exactly the ones unreachable.
            b.Name = $"hot{cfg.Num}";
            // The game never captions a button - its only on-button text
            // is the slot number under _DEBUG (`UIActionButtons.cpp:75`).
            // A label here is not decoration but the last resort of a
            // button that has no picture to show, which is every action
            // button, so it has to be the name: Label holds the key the
            // game binds the slot to, and there are no keys on a phone.
            b.Text = icon != null && !isAlias ? "" : Short(cfg.Name, isAlias ? 6 : 8);
            // The reference's tooltip is the name over "Key: <label>"
            // (`:177-183`); Label is the keyboard binding, which a phone
            // does not have, so the second line is spent on the thing
            // the player actually wants confirmed - what the alias
            // expands to, which is the Value that `BaseClient.cs:292-296`
            // will send.
            b.TooltipText = isAlias && cfg.Data is KeyValuePairString kv
                ? cfg.Name + "\n" + kv.Value
                : cfg.Name;
            b.Position = new Vector2(LeftReserve + gap + i * (ButtonSize + gap), y);
            b.Size = new Vector2(ButtonSize, ButtonSize);
            b.Visible = true;

            // The slot, not the button number and not the config object.
            //
            // Not the object, because the client replaces the whole list
            // when it loads the player's saved buttons on login, and the
            // replacements carry the same numbers and names: holding the
            // object would leave every button pressing a config the
            // client has already thrown away, one BaseClient is no
            // longer subscribed to, so the press would do nothing.
            //
            // Not the number either, which is what this used to capture.
            // The handler is attached once per pooled Button and the
            // pool is reused, so it kept the number of whatever config
            // first happened to land in that screen position. Unset
            // buttons are filtered out and the rest compacted, so that
            // position does not hold the same button for long - bind one
            // item and slot 3 would show one button's icon and fire
            // another's. The game has no such bug because it resolves
            // the index at the click (`UIActionButtons.cpp:351`), and
            // that is what the slot gives us.
            int slot = i;
            _nums.Add(cfg.Num);
            if (b.HasMeta("wired")) continue;
            b.SetMeta("wired", true);
            // A held press REPEATS; it no longer clears. The reference
            // reads the action-button keys every input tick with
            // isKeyDown(...)->Activate() (`ControllerInput.cpp:995-1030`),
            // so holding the key keeps swinging, and the only throttle is
            // the library's own interval (`GameTick.cs:40-51`, tests at `GameTick.cs:304-313`, enforced in
            // `BaseClient.cs:1522` for attacks and :1753 for casts). The
            // old gesture - hold 600 ms to clear - erased the button the
            // player was leaning on mid-fight, so clearing moved to the
            // reference's own gesture: drag the button off the row and
            // let go (`UIActionButtons.cpp:471-473`, dropped on the root
            // window -> SetToUnset). See OnDown / OnUp / OnGui.
            b.ButtonDown += () => OnDown(slot);
            b.ButtonUp += () => OnUp(slot, b);
            b.GuiInput += ev => OnGui(slot, b, ev);
            b.Pressed += () => Fire(slot);
        }

        // The page button, last in the row, saying where you are.
        // Its own button, not one out of the pool: a pooled button
        // already carries a Pressed handler that fires whatever action
        // sat in that position, and turning the page would cast a spell.
        if (paged)
        {
            if (_turn == null)
            {
                _turn = new Button();
                _turn.AddThemeFontSizeOverride("font_size", 18);
                _turn.Name = "hotpage";
                _turn.TooltipText = "More buttons";
                _turn.Pressed += () => { _page++; _signature = ""; };
                AddChild(_turn);
            }
            _turn.Text = $"{_page + 1}/{pages}";
            _turn.Position = new Vector2(LeftReserve + gap + count * (ButtonSize + gap), y);
            _turn.Size = new Vector2(ButtonSize, ButtonSize);
            _turn.Visible = true;
        }
        else if (_turn != null) _turn.Visible = false;

        HideFrom(count);
    }

    /// <summary>
    /// Runs one activation through the GameView's gates. Arguments: the
    /// send, and whether to keep the self-target latch alive afterwards
    /// (a held button keeps it until you let go). Returns whether the
    /// send was let through - false while the server has you waiting
    /// (`ControllerInput.cpp:736`, which the keyboard path sits behind).
    /// Null in a bare harness, which then activates directly.
    /// </summary>
    public Func<Action, bool, bool> Run;

    /// <summary>Spends the self-target latch. See GameView.SpendSelfTarget.</summary>
    public Action SpendLatch;

    /// <summary>Told when a drag-off clears a button, for the chat line.</summary>
    public event Action<string> Cleared;

    /// <summary>
    /// How long a press must be held before it starts repeating. A tap
    /// still fires on release, as before; the delay is what tells a tap
    /// from a hold without firing on the press itself, because a press
    /// that turns into a drag-off must not have cast the spell it is
    /// about to clear.
    /// </summary>
    [Export] public ulong RepeatDelayMs = 250;

    /// <summary>
    /// How far outside the button, in pixels, a finger has to be when it
    /// lifts to clear the button. Big enough that thumb jitter in a
    /// fight is nowhere near it.
    /// </summary>
    [Export] public float PullOffPx = 48f;

    int _heldSlot = -1;
    int _heldNum;
    ulong _heldSince;
    bool _repeating, _outside, _sentInHold;

    void OnDown(int slot)
    {
        if (slot < 0 || slot >= _nums.Count) return;
        _heldSlot = slot;
        _heldNum = _nums[slot];
        _heldSince = Time.GetTicksMsec();
        _repeating = false; _outside = false; _sentInHold = false;
    }

    void OnGui(int slot, Button b, InputEvent ev)
    {
        if (_heldSlot != slot) return;
        Vector2? at = ev is InputEventMouseMotion m ? m.Position
                    : ev is InputEventScreenDrag d ? d.Position : (Vector2?)null;
        if (at == null) return;
        _outside = !new Rect2(-PullOffPx, -PullOffPx,
            b.Size.X + 2f * PullOffPx, b.Size.Y + 2f * PullOffPx).HasPoint(at.Value);
    }

    void OnUp(int slot, Button b)
    {
        if (_heldSlot != slot) return;
        bool pulled = _outside;
        int num = _heldNum;
        EndHold();
        if (pulled)
        {
            // The reference's clear: drop the button on the root window
            // (`UIActionButtons.cpp:471-473`).
            ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(num);
            if (cfg == null) return;
            string name = cfg.Name;
            cfg.SetToUnset();
            HotbarStore.Save(_data);
            Cleared?.Invoke(name);
        }
    }

    /// <summary>
    /// Ends a hold. The flags Pressed reads (_repeating, _outside) are
    /// cleared at the end of the frame rather than here, because Godot
    /// may raise Pressed before or after ButtonUp and either order has to
    /// see them.
    /// </summary>
    void EndHold()
    {
        if (_heldSlot < 0) return;
        _heldSlot = -1;
        if (_sentInHold) SpendLatch?.Invoke();
        _sentInHold = false;
        Callable.From(() => { _repeating = false; _outside = false; }).CallDeferred();
    }

    /// <summary>
    /// Only what the reference's server throttles as a swing: melee, a
    /// cast, a skill. Items, aliases and the toggles (Rest) fire once
    /// per press - the reference repeats those as well, but on a phone a
    /// second's hold on Rest would stand you back up.
    /// </summary>
    static bool Repeats(ActionButtonConfig cfg) =>
        cfg.ButtonType == ActionButtonType.Spell ||
        cfg.ButtonType == ActionButtonType.Skill ||
        (cfg.ButtonType == ActionButtonType.Action &&
         cfg.Data is AvatarAction a && a == AvatarAction.Attack);

    /// <summary>
    /// The input tick, `ControllerInput.cpp:995-1030`: while the press is
    /// down and past the delay, activate every frame. The library's
    /// interval (`GameTick.CanReqAttack/CanReqCast`) is the rate limit,
    /// exactly as it is for the reference.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_heldSlot < 0 || _outside) return;
        if (Time.GetTicksMsec() - _heldSince < RepeatDelayMs) return;

        ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(_heldNum);
        if (cfg == null || !Repeats(cfg)) return;
        _repeating = true;
        if (Send(cfg, true)) _sentInHold = true;
    }

    bool Send(ActionButtonConfig cfg, bool keepLatch)
    {
        bool sent = true;
        Action go = () =>
        {
            try { cfg.Activate(); }
            catch (Exception e) { GD.PrintErr($"[ActionButtons] {cfg?.Name}: {e.Message}"); }
        };
        if (Run != null) sent = Run(go, keepLatch);
        else go();
        return sent;
    }

    /// <summary>
    /// A tap, or the release of a press that did not repeat. The screen
    /// slot says which button number is showing there right now, and that
    /// number is looked up in the client's own list; the dispatch lives
    /// in BaseClient, which is subscribed to every button in the list.
    /// </summary>
    void Fire(int slot)
    {
        // A repeat already fired for this press, or it was pulled off:
        // the release is not another tap. (A scripted press arrives with
        // no press-down and passes straight through.)
        bool held = _heldSlot == slot;
        if (_repeating || _outside) { EndHold(); return; }
        if (held) EndHold();

        if (slot < 0 || slot >= _nums.Count) return;
        ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(_nums[slot]);
        if (cfg == null) return;
        Send(cfg, false);
    }

    /// <summary>
    /// The button's picture.
    ///
    /// Spells and skills get one as well as items. The game composes all
    /// three (`UIActionButtons.cpp:244-332`) - it takes a different route
    /// for spells and skills, blitting a frame straight out of the
    /// resource rather than going through the image composer, but its own
    /// comment calls that a hack for resolution, not a different picture.
    /// Composing them the same way items are composed is the same answer
    /// through one path instead of two.
    ///
    /// A button with no picture keeps its short label; that is every
    /// action button, which is what the seeded row is made of.
    ///
    /// Keyed on everything the composed picture depends on rather than on
    /// the file alone. The cache used to be keyed on the filename, so an
    /// item that was dyed, took an effect or simply animated kept the
    /// first picture it was ever drawn with for the rest of the session -
    /// where the game re-pushes the texture every time the object changes.
    /// </summary>
    Texture2D Icon(ActionButtonConfig cfg)
    {
        // An alias has no game object behind it and so nothing to
        // compose, which is why the reference gives it a fixed picture
        // instead: the one branch of ActionButtonChange that does not
        // touch an image composer just sets UI_IMAGE_ALIAS_ICON on the
        // button (`UIActionButtons.cpp:334-338`). See AliasIcon for
        // where that picture comes from.
        if (cfg.ButtonType == ActionButtonType.Alias) return AliasIcon();

        if (cfg.ButtonType == ActionButtonType.Action ||
            cfg.ButtonType == ActionButtonType.Unset) return null;
        if (cfg.Data is not ObjectBase o || o.Resource == null) return null;

        int frame = o.ViewerFrameIndex >= 0 ? o.ViewerFrameIndex : 0;
        string key = $"{o.Resource.Filename}:{frame}:{IconSize}:{o.ColorTranslation}:{o.Effect}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }

    /// <summary>
    /// The reference's alias picture, shared by the hotbar button and by
    /// the alias editor's own bind button - both places the reference
    /// draws it (`UIActionButtons.cpp:337`, `UIOptions.cpp:1082`, both
    /// naming UI_IMAGE_ALIAS_ICON, which `Constants.h:331` defines as
    /// "TaharezLook/AliasIcon").
    ///
    /// That name is a CEGUI imageset entry, not a game resource: a
    /// 24x24 rectangle at (133,304) of the UI sheet
    /// (`Resources/ui/imagesets/TaharezLook.imageset:314`), and what is
    /// in that rectangle is a letter A. There is no CEGUI here to ask
    /// for it, so it is cut out of the repo's own sheet into
    /// art/alias.png rather than invented - the same thing MiniMap does
    /// with the dial it draws on.
    ///
    /// Loaded once and kept, because it never changes and every alias
    /// button shows the same one. A null is survivable and deliberately
    /// not treated as an error: the caller falls back to the key as
    /// text, which is the better half of the label anyway.
    /// </summary>
    static Texture2D _aliasIcon;
    static bool _aliasIconTried;
    public static Texture2D AliasIcon()
    {
        if (_aliasIconTried) return _aliasIcon;
        _aliasIconTried = true;
        try { _aliasIcon = GD.Load<Texture2D>("res://art/alias.png"); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] alias icon: {e.Message}"); }
        return _aliasIcon;
    }

    static string Short(string s, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(s)) return "?";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max);
    }

    Button Take(int index)
    {
        while (_pool.Count <= index)
        {
            var b = new Button { Visible = false, ClipText = true };
            b.AddThemeFontSizeOverride("font_size", FontSize);
            AddChild(b);
            _pool.Add(b);
        }
        return _pool[index];
    }

    /// <summary>Which page of bindings the row is showing.</summary>
    int _page;

    /// <summary>The page button, when there is more than one page.</summary>
    Button _turn;

    void HideFrom(int from)
    {
        for (int i = from; i < _pool.Count; i++) _pool[i].Visible = false;
    }
}
