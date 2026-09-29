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

    /// <summary>Pixels at the bottom already spoken for.</summary>
    public float BottomReserve { get; set; }

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
        int across = Math.Max(1, (int)((v.X - gap) / (ButtonSize + gap)));
        int count = Math.Min(set.Count, across);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
            sb.Append(set[i].Num).Append(':').Append(set[i].ButtonType).Append(':').Append(set[i].Name).Append(';');
        sb.Append('@').Append(across);

        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        _nums.Clear();

        float y = v.Y - BottomReserve - ButtonSize - 8f;
        for (int i = 0; i < count; i++)
        {
            ActionButtonConfig cfg = set[i];
            Button b = Take(i);

            // Label is an empty string rather than null when unset, so a
            // null-coalesce picks the blank one and every button reads "?".
            string caption = string.IsNullOrWhiteSpace(cfg.Label) ? cfg.Name : cfg.Label;
            b.Text = cfg.ButtonType == ActionButtonType.Item ? "" : Short(caption);
            b.Icon = Icon(cfg);
            b.TooltipText = cfg.Name;
            b.Position = new Vector2(gap + i * (ButtonSize + gap), y);
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
            b.Pressed += () => Fire(slot);
        }

        HideFrom(count);
    }

    /// <summary>
    /// Fires the button the way the client does: the screen slot says
    /// which button number is showing there right now, and that number is
    /// looked up in the client's own list. The dispatch lives in
    /// BaseClient, which is subscribed to every button in the list.
    /// </summary>
    void Fire(int slot)
    {
        if (slot < 0 || slot >= _nums.Count) return;
        ActionButtonConfig cfg = _data?.ActionButtons?.GetByNum(_nums[slot]);
        try { cfg?.Activate(); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] {cfg?.Name}: {e.Message}"); }
    }

    /// <summary>An item button shows its icon; the rest show a short label.</summary>
    ImageTexture Icon(ActionButtonConfig cfg)
    {
        if (cfg.ButtonType != ActionButtonType.Item) return null;
        if (cfg.Data is not ObjectBase o || o.Resource == null) return null;

        string key = $"{o.Resource.Filename}:{IconSize}";
        if (_icons.TryGetValue(key, out ImageTexture cached)) return cached;

        ImageTexture tex = null;
        try { tex = M59Assets.FromTex(M59Compose.Icon(o, IconSize)); }
        catch (Exception e) { GD.PrintErr($"[ActionButtons] icon: {e.Message}"); }
        _icons[key] = tex;
        return tex;
    }

    static string Short(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "?";
        s = s.Trim();
        return s.Length <= 8 ? s : s.Substring(0, 8);
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

    void HideFrom(int from)
    {
        for (int i = from; i < _pool.Count; i++) _pool[i].Visible = false;
    }
}
