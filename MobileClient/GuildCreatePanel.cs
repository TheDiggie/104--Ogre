using System;
using Godot;
using Meridian59.Common.Constants;
using Meridian59.Data.Models;

/// <summary>
/// Founding a guild: a name, ten rank titles, and whether it is secret.
///
/// `UIGuildCreate.cpp`. Like the hall list, this window is not something
/// the player opens - you ask the right NPC and the server answers with a
/// UserCommand (155) carrying GuildAsk. `DataController` merges the two
/// costs into `GuildAskData` and marks it visible
/// (`Meridian59/Data/DataController.cs:2802-2804`); the reference hooks
/// that model's PropertyChanged in Initialize (`UIGuildCreate.cpp:42-43`)
/// and shows the window on IsVisible (`:74-82`). The mobile client had no
/// screen for it, so the offer arrived, the flag went up, and nothing on
/// screen moved.
///
/// What the message carries is only the two prices - CostNormal and
/// CostSecret, two uints and nothing else (`GuildAskData.cs:50-54`,
/// `:112-141`). The reference puts one of them in a read-only Cost field
/// and swaps which one on every change of the secret checkbox
/// (`:85-96`, `:102-116`). So the cost is a quoted price, not something
/// sent back: the create command has no cost field at all
/// (`UserCommandGuildCreate.cs:34-50`). The server bills whichever one
/// the flag it receives implies, which is precisely why the number beside
/// the checkbox has to follow the checkbox - a player who ticks "secret"
/// and still reads the ordinary price has been told the wrong thing about
/// an irreversible spend.
///
/// What is actually sent, and when: `SendUserCommandGuildCreate` off the
/// Create button, with twelve arguments read straight out of the boxes -
/// the guild name, five male rank titles, five female ones, and the
/// checkbox (`:118-143`, `BaseClient.cs:1327-1360`). Then the window is
/// marked hidden and that is all (`:138-140`). There is no echo and no
/// wait: the next thing the player hears is either a guild window or a
/// refusal in the chat.
///
/// The reference's validation on that button is, in full, nothing. It
/// does not check the name, it does not check the ranks, it does not
/// notice that Create is pressable the instant the window opens, and it
/// does not ask before spending the guild-founding fee. Its only guard is
/// upstream and structural: Initialize caps every box at the server's own
/// string lengths - MAX_GUILD_NAME_LEN and MAX_GUILD_RANK_LEN, 30 and 20
/// (`:29-39`, `Meridian59/Common/Constants/Constants.cs:140-141`) - so
/// the strings can never be longer than the server will take. That cap is
/// kept exactly, on all eleven boxes.
///
/// Two deliberate departures, both flagged because the reference is the
/// specification and these are not in it:
///
///  - Create raises <see cref="Found"/> and GameView asks first with the
///    client's own ConfirmPopup. Founding a guild costs money and cannot
///    be undone; the shield claim already takes this line
///    (see GameView's wiring), and the confirmation names the price so
///    the number the player agreed to is the number they were shown.
///  - a blank name or a blank rank shows an inline note and sends
///    nothing. The reference would send it and the server would refuse
///    it, which on a desktop costs a line of chat and here costs the
///    player any idea of what went wrong - and `UserCommandGuildCreate`
///    measures every string to size its buffer
///    (`UserCommandGuildCreate.cs:34-50`), so a null would not even
///    reach the wire. The boxes start filled with the layout's own
///    defaults (`Meridian59.layout:2124-2191`), so this note is only
///    reachable by clearing one.
///
/// Closing: the frame's close button and Escape both set IsVisible false
/// and nothing else (`:145-171`) - notably they do NOT clear the costs,
/// so an offer refused and re-asked shows the same numbers. That is
/// copied: <see cref="Closed"/> lowers the flag and leaves the model
/// alone.
/// </summary>
public partial class GuildCreatePanel : Control
{
    [Export] public int FontSize = 15;

    /// <summary>
    /// Everything the create command needs, in one piece, because an
    /// event with twelve arguments is an event nobody reads. Cost is not
    /// sent - it is here so the confirmation can name the price the
    /// player was actually shown.
    /// </summary>
    public sealed class Founding
    {
        public string Name = "";
        /// <summary>Rank 1 to 5, male. Index 0 is rank 1.</summary>
        public string[] Male = new string[5];
        /// <summary>Rank 1 to 5, female. Index 0 is rank 1.</summary>
        public string[] Female = new string[5];
        public bool Secret;
        public uint Cost;
    }

    /// <summary>Create the guild as described. The caller asks first.</summary>
    public event Action<Founding> Found;

    /// <summary>
    /// Closed without founding: mark the offer not visible, which is all
    /// the reference's close and Escape paths do (`UIGuildCreate.cpp:162-171`,
    /// `:145-160`).
    /// </summary>
    public event Action Closed;

    ColorRect _panel;
    Label _title, _nameDesc, _maleDesc, _femaleDesc, _costDesc, _cost, _note;
    LineEdit _name;
    readonly LineEdit[] _male = new LineEdit[5];
    readonly LineEdit[] _female = new LineEdit[5];
    CheckBox _secret;
    Button _create, _close;

    GuildAskData _ask;

    public bool IsOpen => _panel != null && _panel.Visible;

    /// <summary>
    /// The layout's own starting titles, rank 1 to rank 5
    /// (`Meridian59.layout:2124-2191`). They are defaults in the
    /// reference because they are properties on the edit boxes, so a
    /// player who founds a guild without typing in this half gets these -
    /// and a guild whose ranks read "Initiate" up to "Guildmaster" is the
    /// game's own idea of a guild.
    /// </summary>
    static readonly string[] MaleDefaults =
        { "Initiate", "Member", "Lord", "Hero", "Guildmaster" };
    static readonly string[] FemaleDefaults =
        { "Initiate", "Member", "Lady", "Heroine", "Guildmistress" };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        // The frame's caption and the three labels, all the layout's words
        // (`Meridian59.layout:2088`, `:2098`, `:2111`, `:2118`).
        _title = Heading("Create Guild", FontSize + 4, new Color(1, 0.92f, 0.6f));
        _nameDesc = Heading("Name:", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _maleDesc = Heading("Male ranks:", FontSize, new Color(0.75f, 0.78f, 0.84f));
        _femaleDesc = Heading("Female ranks:", FontSize, new Color(0.75f, 0.78f, 0.84f));

        _name = Box("guild name", BlakservStringLengths.MAX_GUILD_NAME_LEN, "guildname");

        // Rank 1 at the top, rank 5 at the bottom. The layout stacks them
        // the other way up - MaleRank5 first at y=70 down to MaleRank1 at
        // y=190 (`:2124-2156`) - so the guildmaster sits at the top of the
        // window. Ordered low-to-high here instead, because the column is
        // a ladder and every other rank control in this client counts up:
        // GuildPanel's rank dropdown is built `for (byte r = 1; r <= 5;
        // r++)` (`GuildPanel.cs:478`), and two rank lists in the same app
        // that disagree about which end is the top is the sort of thing
        // that gets somebody promoted by accident.
        for (int i = 0; i < 5; i++)
        {
            _male[i] = Box(MaleDefaults[i], BlakservStringLengths.MAX_GUILD_RANK_LEN, $"male{i + 1}");
            _male[i].Text = MaleDefaults[i];
            _female[i] = Box(FemaleDefaults[i], BlakservStringLengths.MAX_GUILD_RANK_LEN, $"female{i + 1}");
            _female[i].Text = FemaleDefaults[i];
        }

        // "Secret guild" (`:2195`). Ticking it changes nothing but the
        // price shown and the byte sent.
        _secret = new CheckBox { Text = "Secret guild", Visible = false, Name = "secret" };
        _secret.AddThemeFontSizeOverride("font_size", FontSize);
        _secret.Toggled += _ => Quote();
        AddChild(_secret);

        _costDesc = Heading("Cost:", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _cost = Heading("0", FontSize, new Color(1, 0.86f, 0.4f));

        _note = Heading("", FontSize - 2, new Color(0.95f, 0.55f, 0.5f));

        // The layout has one button, Create (`:2214-2217`); the way out is
        // the frame's close cross, which this has no frame for, so Close
        // is a button of its own. It is on the left, away from the one
        // that spends money.
        _close = Push("Close", () => Closed?.Invoke(), "guildclose");
        _create = Push("Create", Confirm, "guildcreate");

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    /// <summary>
    /// One text box, capped at what the server will accept. That cap is
    /// the reference's only validation and it is applied in the same
    /// place - as the control is built (`UIGuildCreate.cpp:29-39`) - so
    /// there is no path by which an over-long string exists to be sent.
    /// </summary>
    LineEdit Box(string placeholder, int maxLength, string name)
    {
        var e = new LineEdit
        {
            PlaceholderText = placeholder,
            MaxLength = maxLength,
            Visible = false,
            Name = name,
        };
        e.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(e);
        return e;
    }

    Button Push(string text, Action pressed, string name)
    {
        var b = new Button { Text = text, Visible = false, Name = name };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    /// <summary>
    /// The price on screen: CostSecret when the box is ticked, CostNormal
    /// when it is not. This is `OnSecretGuildSelectChange`
    /// (`UIGuildCreate.cpp:102-116`) and the two cost-property branches of
    /// the model listener (`:85-96`) collapsed into one place, because
    /// they compute the same thing - the reference only needs three
    /// entry points because it is reacting to three separate events.
    /// </summary>
    void Quote()
    {
        uint price = _ask == null ? 0u : (_secret.ButtonPressed ? _ask.CostSecret : _ask.CostNormal);
        _cost.Text = price.ToString();
    }

    /// <summary>
    /// The Create press. `OnCreateClicked` (`UIGuildCreate.cpp:118-143`)
    /// reads the boxes and sends; this reads the boxes and hands them
    /// out, because the spend is confirmed first - see the class note.
    ///
    /// The blank check is not the reference's. It is here because the
    /// alternative on a phone is a message the server silently refuses.
    /// </summary>
    void Confirm()
    {
        var f = new Founding
        {
            Name = (_name.Text ?? "").Trim(),
            Secret = _secret.ButtonPressed,
            Cost = _ask == null ? 0u : (_secret.ButtonPressed ? _ask.CostSecret : _ask.CostNormal),
        };
        for (int i = 0; i < 5; i++)
        {
            f.Male[i] = (_male[i].Text ?? "").Trim();
            f.Female[i] = (_female[i].Text ?? "").Trim();
        }

        bool blank = f.Name.Length == 0;
        for (int i = 0; i < 5 && !blank; i++)
            blank = f.Male[i].Length == 0 || f.Female[i].Length == 0;

        if (blank)
        {
            _note.Text = "A guild needs a name and all ten rank titles.";
            _note.Visible = true;
            Layout();
            return;
        }

        _note.Visible = false;
        Found?.Invoke(f);
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.4f;
        float height = Mathf.Min(v.Y * 0.88f, 900f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.9f;

        _nameDesc.Position = new Vector2(side, y + rowH * 0.25f);
        _name.Position = new Vector2(side + w * 0.22f, y);
        _name.Size = new Vector2(w * 0.78f, rowH);
        y += rowH + 8f;

        // Two columns, male and female, as the layout has them
        // (`Meridian59.layout:2110-2121`) - the two titles for one rank
        // side by side, which is the pairing the server reads them in
        // (`UserCommandGuildCreate.cs:59-120` interleaves them on the
        // wire).
        float colW = w * 0.5f - 6f;
        _maleDesc.Position = new Vector2(side, y);
        _femaleDesc.Position = new Vector2(side + w * 0.5f + 6f, y);
        y += FontSize * 1.7f;

        for (int i = 0; i < 5; i++)
        {
            _male[i].Position = new Vector2(side, y);
            _male[i].Size = new Vector2(colW, rowH);
            _female[i].Position = new Vector2(side + w * 0.5f + 6f, y);
            _female[i].Size = new Vector2(colW, rowH);
            y += rowH + 4f;
        }

        y += 6f;
        _secret.Position = new Vector2(side, y);
        _secret.Size = new Vector2(w * 0.5f, rowH);
        _costDesc.Position = new Vector2(side + w * 0.55f, y + rowH * 0.2f);
        _cost.Position = new Vector2(side + w * 0.75f, y + rowH * 0.2f);
        y += rowH + 6f;

        _note.Position = new Vector2(side, y);
        _note.Size = new Vector2(w, FontSize * 2f);

        float by = top + height - rowH - 12f;
        _close.Position = new Vector2(side, by);
        _close.Size = new Vector2(w * 0.5f - 6f, rowH);
        _create.Position = new Vector2(side + w * 0.5f + 6f, by);
        _create.Size = new Vector2(w * 0.5f - 6f, rowH);
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront, and the
        // reference's own moveToFront on becoming visible
        // (`UIGuildCreate.cpp:80-81`).
        if (on) Panels.ToFront(this);
        _panel.Visible = on;
        _title.Visible = on;
        _nameDesc.Visible = on; _name.Visible = on;
        _maleDesc.Visible = on; _femaleDesc.Visible = on;
        for (int i = 0; i < 5; i++) { _male[i].Visible = on; _female[i].Visible = on; }
        _secret.Visible = on;
        _costDesc.Visible = on; _cost.Visible = on;
        _create.Visible = on; _close.Visible = on;
        if (!on) _note.Visible = false;

        // No MoveChild of its own: ToFront above is the one raise, and it
        // puts an open ConfirmPopup back over this panel (a GuildAsk can
        // arrive while the exile question is waiting).
        Layout();
    }

    /// <summary>
    /// Follows the model, every frame - the same job the reference's one
    /// PropertyChanged handler does for all three of its properties
    /// (`UIGuildCreate.cpp:69-97`).
    ///
    /// The costs are re-quoted on every sync rather than only on a
    /// change, which is cheap and covers the ordering the reference has
    /// to work around: DataController writes the costs and only then sets
    /// IsVisible (`DataController.cs:2803-2804`), so its cost handlers
    /// fire while the window is still hidden. Polling has no such edge.
    /// </summary>
    public void Sync(GuildAskData ask)
    {
        if (_panel == null) return;

        if (ask == null || !ask.IsVisible)
        {
            if (IsOpen) { _ask = ask; Show(false); }
            return;
        }

        bool fresh = _ask != ask || !IsOpen;
        _ask = ask;

        // A new offer starts from the game's own titles again. The
        // reference gets this for free - its boxes are rebuilt from the
        // layout - and without it a guild abandoned half-typed would
        // haunt the next attempt.
        if (fresh)
        {
            if (!_name.HasFocus()) _name.Text = "";
            for (int i = 0; i < 5; i++)
            {
                if (!_male[i].HasFocus()) _male[i].Text = MaleDefaults[i];
                if (!_female[i].HasFocus()) _female[i].Text = FemaleDefaults[i];
            }
            _note.Visible = false;
        }

        if (!IsOpen) Show(true);
        Quote();
    }
}
