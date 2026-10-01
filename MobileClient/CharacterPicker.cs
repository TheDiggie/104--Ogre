using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Which character to play, when the account has more than one and the
/// scene did not say.
///
/// A full-screen list rather than a dropdown: this is the first thing
/// touched on a phone and there is no reason to make it small. It hides
/// itself once something is chosen, and nothing is sent to the server
/// until it is.
/// </summary>
public partial class CharacterPicker : Control
{
    [Export] public int FontSize = 20;

    public event Action<CharSelectItem> Chosen;
    /// <summary>
    /// Make a new one. The game's character selection has an empty slot
    /// per unused place and clicking one starts the wizard; the list
    /// here is only what exists, so the offer is a row of its own.
    /// </summary>
    public event Action NewWanted;

    VBoxContainer _rows;
    ScrollContainer _scroll;
    Label _title;
    ColorRect _bg;
    Panel _card, _bar, _motdBox;
    Label _motdCap;

    /// <summary>
    /// The message of the day. See <see cref="Sync"/> - this is the one
    /// thing on the selection screen that comes from the server rather
    /// than from the account.
    /// </summary>
    RichTextLabel _motd;
    string _said = null;

    /// <summary>
    /// The live welcome model, remembered from <see cref="Sync"/>. It is
    /// where the empty slots are: the list <see cref="Offer"/> is handed
    /// is only the characters that exist, so whether the account has
    /// room for another is read from the model at the moment of the
    /// offer rather than passed in.
    /// </summary>
    WelcomeInfo _welcome;

    /// <summary>
    /// The id of the first empty slot on the account, 0 when there is
    /// none. The reference hands it to the palette request
    /// (`UIWelcome.cpp:159-160`, `SendSystemMessageSendCharInfo(ID)`)
    /// and it comes back as the target of NewCharInfo
    /// (`BaseClient.cs:2644,2656`); the caller of
    /// <see cref="NewWanted"/> should pass it on.
    /// </summary>
    public uint FreeSlotId { get; private set; }

    /// <summary>
    /// Whether the last offer included "New character" - the account
    /// had an empty slot.
    /// </summary>
    public bool CanCreate { get; private set; } = true;
    float _motdH;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;

        // Opaque, not the skin's translucent Scrim: nothing is behind
        // this screen but the view's connection log down the left edge,
        // and at 0.82 that log read straight through the character names.
        _bg = new ColorRect { Color = new Color(M59Skin.Scrim.R, M59Skin.Scrim.G, M59Skin.Scrim.B) };
        AddChild(_bg);

        _card = M59Skin.Window();
        AddChild(_card);
        _bar = M59Skin.TitleBar();
        AddChild(_bar);

        _title = M59Skin.Title("Choose a character");
        AddChild(_title);

        // The message of the day, between the heading and the list.
        //
        // The reference puts it on this screen and nowhere else
        // (`Meridian59.Ogre.Client/UIWelcome.cpp:36-38` sets it while the
        // window is being built), which is the only sensible place for
        // it: it is the one moment in a session when the player is
        // reading rather than playing. The server sends it inside the
        // same Characters message the list itself comes from - the
        // library puts it on `WelcomeInfo.MOTD`
        // (`Meridian59/Data/Models/WelcomeInfo.cs:157-170`) when
        // `HandleCharacters` folds the message into the model
        // (`Meridian59/Data/DataController.cs:2094-2097`).
        //
        // So the client has had the text in hand every single login and
        // has never once shown it. On this server that is where downtime,
        // events and rule changes are announced, and a player of the
        // mobile client would simply never hear any of it.
        //
        // Scrollable and BBCode-enabled because it is the only piece of
        // free server text on this screen and it can be any length; a
        // Label would either clip it or push the character buttons off
        // the bottom.
        _motd = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            FitContent = false,
            Visible = false,
        };
        _motd.AddThemeFontSizeOverride("normal_font_size", M59Skin.BodySize - 2);
        _motd.AddThemeColorOverride("default_color", M59Skin.Text);
        // On a sunken panel, because it is the one thing on this screen
        // that is READ rather than pressed - everything else here is a
        // raised button, and server prose on the same surface as the
        // rows read as another row.
        _motdBox = new Panel { Visible = false };
        _motdBox.AddThemeStyleboxOverride("panel", M59Skin.Sunken());
        AddChild(_motdBox);
        _motdCap = M59Skin.Caption("Message of the day");
        _motdCap.Visible = false;
        AddChild(_motdCap);
        AddChild(_motd);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", M59Skin.GapI);
        // A ScrollContainer sizes its child to that child's MINIMUM
        // width unless the child asks to expand - without this the rows
        // would be as wide as the longest name, with the rest of the
        // card going spare beside them. See notes/godot-ui.md.
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new TouchScroll();
        _scroll.AddChild(_rows);
        AddChild(_scroll);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_rows == null) return;
        Vector2 v = GetViewportRect().Size;
        // Sized in Layout rather than anchored. These panels live in a
        // CanvasLayer whose Control parents have no rect of their own,
        // so an anchored child comes out zero by zero and never draws -
        // which is how the chat log window managed to be invisible for
        // its whole existence.
        _bg.Position = Vector2.Zero;
        _bg.Size = v;

        // The card is the size of the list in it: an account with two
        // characters is a two-row window, not a full-bleed black page
        // with two buttons stranded at the top of it.
        int shown = Mathf.Max(1, _rows.GetChildCount());
        // Plus a point of slack: a list one pixel taller than its
        // box grows a scrollbar beside two rows.
        float rowsH = shown * M59Skin.RowH + (shown - 1) * M59Skin.Gap + 4f;
        float motdH = 0f;
        if (_motd != null && _motd.Visible)
            // Capped at a third of the screen: the characters are what
            // this screen is for and a long announcement must not push
            // them off it.
            motdH = Mathf.Min(_motdH + M59Skin.Pad * 2f, v.Y * 0.33f) + CapH + M59Skin.Gap;

        // Narrower than a list panel would be: a column of names wants
        // to be read down, not stretched across a sideways phone.
        Rect2 card = M59Skin.Frame(v, motdH + rowsH, false, CardW(v, 720f));
        Rect2 body = M59Skin.Body(card, false);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _bar.Position = card.Position;
        _bar.Size = new Vector2(card.Size.X, M59Skin.TitleH);
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);

        float top = body.Position.Y;
        if (_motd != null && _motd.Visible)
        {
            float h = Mathf.Min(_motdH + M59Skin.Pad * 2f, v.Y * 0.33f);
            _motdCap.Position = new Vector2(body.Position.X, top);
            _motdCap.Size = new Vector2(body.Size.X, CapH);
            _motdBox.Position = new Vector2(body.Position.X, top + CapH);
            _motdBox.Size = new Vector2(body.Size.X, h);
            // Inset inside its own sunken panel, so the text does not
            // sit on the frame.
            _motd.Position = _motdBox.Position + new Vector2(M59Skin.Pad, M59Skin.Pad * 0.5f);
            _motd.Size = _motdBox.Size - new Vector2(M59Skin.Pad * 2f, M59Skin.Pad);
            top += CapH + h + M59Skin.Gap;
        }

        _scroll.Position = new Vector2(body.Position.X, top);
        _scroll.Size = new Vector2(body.Size.X,
                                   Mathf.Max(M59Skin.RowH, body.Position.Y + body.Size.Y - top));
        // Clear of the scrollbar: a row laid out to the full body
        // runs its last control - a bind "+", a price - under the bar,
        // and a thumb aimed at one hits the other. See M59Skin.RowsW.
        _rows.CustomMinimumSize = new Vector2(M59Skin.RowsW(body), 0);
    }


    /// <summary>
    /// How wide the card should ask to be.
    ///
    /// Frame caps the width, and for a prompt-shaped card the cap is a
    /// fixed number of points - which is right on a sideways phone and
    /// wrong on an upright one, where the viewport is as wide as the
    /// landscape one (the project stretches canvas items and expands
    /// the aspect, so a portrait window grows the HEIGHT and keeps
    /// X at 1920) and a 560-point card is a third of the glass with
    /// nothing either side of it. Held tall, the card takes the screen.
    ///
    /// M59Skin could grow this; Frame's wantW is the place for it.
    /// </summary>
    static float CardW(Vector2 v, float wide)
        => v.Y > v.X ? Mathf.Max(wide, v.X * 0.9f) : wide;

    /// <summary>Height of a caption over a block.</summary>
    const float CapH = 22f;

    /// <summary>
    /// Follows the server's welcome information - which today means the
    /// message of the day and nothing else.
    ///
    /// Called every frame rather than once, because once is not enough.
    /// The reference sets the text while building the window
    /// (`UIWelcome.cpp:36-38`) AND again whenever the model's MOTD
    /// property changes (`:55-63`, watching
    /// `WelcomeInfo::PROPNAME_MOTD`), and it needs both: the selection
    /// screen is reachable more than once in a session - back out of the
    /// creation wizard and `SendSendCharactersMessage` fetches the whole
    /// welcome again - so a value read once at build time would be the
    /// wrong one, or absent, on every visit after the first. Polling a
    /// string is the same answer as subscribing to its change, without a
    /// handler to unsubscribe.
    ///
    /// Only the text is touched here. Whether this screen is up at all is
    /// <see cref="Offer"/>'s business, and a child of a hidden Control
    /// draws nothing either way.
    /// </summary>
    public void Sync(WelcomeInfo info)
    {
        if (info != null) _welcome = info;
        if (_motd == null) return;

        string text = info?.MOTD ?? "";
        if (text == _said) return;
        _said = text;

        // A '[' in server text would open a BBCode tag and swallow the
        // rest of the message. The reference writes the MOTD into a
        // plain edit box (`UIWelcome.cpp:36-38`) where a bracket is just
        // a bracket; here it is markup, so it is escaped - the same
        // thing LookPanel, MailPanel and NewsPanel each do with server
        // strings.
        _motd.Text = text.Replace("[", "[lb]");

        bool any = !string.IsNullOrWhiteSpace(text);
        _motd.Visible = any;
        _motdBox.Visible = any;
        _motdCap.Visible = any;
        _motdH = 0f;
        if (any)
        {
            // Measured rather than guessed, so the button list starts
            // directly under the message instead of below a fixed block
            // of empty space. Measured against the CARD's text width,
            // not the screen's: the message lives inside the card now,
            // and measuring across 1920 points reported one line where
            // the card wraps to three.
            Vector2 v = GetViewportRect().Size;
            float wide = M59Skin.Frame(v, 0f, false, CardW(v, 720f)).Size.X - M59Skin.Pad * 4f;
            _motdH = _motd.GetThemeFont("normal_font").GetMultilineStringSize(
                         _motd.Text, HorizontalAlignment.Left, wide,
                         _motd.GetThemeFontSize("normal_font_size")).Y * 1.2f + 8f;
        }
        Layout();
    }

    /// <summary>
    /// Makes a row's FOCUS state look chosen - the lit fill and the
    /// gold edge down the left that this skin marks a picked row with.
    ///
    /// Focus is where the mark lives: GameView remembers the character
    /// you played last and marks its row by overriding the three font
    /// colours and calling GrabFocus (`GameView.PreselectCharacter`,
    /// which explains why focus is the only per-row state there is to
    /// set here). Dressed alone, that was a slightly lighter brown and
    /// gold text - true, and not something you would notice. Nothing
    /// about WHEN a row is marked changes; only what being marked
    /// looks like.
    /// </summary>
    /// <summary>
    /// Pushes a left-aligned row's text in off its own edge. Dress's
    /// styleboxes carry no content margin, so a left-aligned caption
    /// started at pixel zero - against the gold edge that marks the
    /// remembered character, which it then looked like part of.
    /// </summary>
    static void Indent(Button b)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            if (b.GetThemeStylebox(state) is StyleBoxFlat s) s.ContentMarginLeft = M59Skin.Pad;
    }

    static void Marked(Button b)
    {
        var s = new StyleBoxFlat
        {
            BgColor = M59Skin.RowPick,
            BorderWidthLeft = 4,
            BorderColor = M59Skin.Gold,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = M59Skin.Pad,
            AntiAliasing = true,
        };
        b.AddThemeStyleboxOverride("focus", s);
    }

    public void Offer(IList<CharSelectItem> characters)
    {
        // Removed first, then freed. QueueFree alone is deferred, so the
        // old rows would still be children - still holding the name
        // "newCharacter" - when the new one is added, and Godot would
        // rename the newcomer to @Button@NNN. CreateCharacter.Build
        // does it the same way.
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        // The wizard is reachable only from an empty slot
        // (`UIWelcome.cpp:157,201,287`); an account with none has no
        // such row, so it gets no "New character" either. Without a
        // welcome model to ask, keep the offer - the old behaviour.
        CanCreate = true;
        FreeSlotId = 0;
        if (_welcome?.Characters != null)
        {
            CanCreate = false;
            foreach (CharSelectItem c in _welcome.Characters)
                if (c != null && c.IsEmptySlot) { CanCreate = true; FreeSlotId = c.ID; break; }
        }

        int index = 0;
        foreach (CharSelectItem c in characters)
        {
            CharSelectItem captured = c;          // do not close over the loop variable
            var b = new Button
            {
                // The name is the button's OWN text, not a child label:
                // GameView marks the character you played last by
                // overriding this button's font colours and grabbing its
                // focus (`GameView.PreselectCharacter`), and a colour
                // override on a Button does not reach a Label inside it.
                Text = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name,
                CustomMinimumSize = new Vector2(0, M59Skin.RowH),
                Alignment = HorizontalAlignment.Left,
            };
            M59Skin.Dress(b, index++ % 2 == 1 ? M59Skin.Kind.RowAlt : M59Skin.Kind.Row);
            // Choosing a person, not ticking a list item: the name
            // carries the weight of a heading and sits where a name
            // sits, at the left margin of the row.
            b.AddThemeFontSizeOverride("font_size", M59Skin.BodySize + 4);
            Indent(b);
            Marked(b);
            b.Pressed += () => { Visible = false; Chosen?.Invoke(captured); };
            _rows.AddChild(b);
        }

        if (CanCreate)
        {
            var make = new Button
            {
                Text = "New character",
                CustomMinimumSize = new Vector2(0, M59Skin.RowH),
                Name = "newCharacter",
            };
            // Secondary, not a row: it makes somebody rather than
            // choosing one of them, and a list of people with an action
            // in the middle of it reads as a person called New character.
            M59Skin.Dress(make, M59Skin.Kind.Secondary);
            make.AddThemeFontSizeOverride("font_size", M59Skin.BodySize);
            make.Pressed += () => { Visible = false; NewWanted?.Invoke(); };
            _rows.AddChild(make);
        }
        else
        {
            // Said, not merely absent. The row for making a character is
            // the only way to reach the wizard, so an account with no
            // empty slot simply has no such row - and a screen that is
            // missing a thing looks the same as a screen that forgot it.
            // The reference never has to say this because its grid draws
            // every slot the account owns, full or not
            // (`UIWelcome.cpp:96-130`), so the absence is self-evident
            // there; here the list is only the characters, so the reason
            // has to be written down.
            Label full = M59Skin.Caption(
                "This account has no free character slot, so there is nothing to make.");
            full.HorizontalAlignment = HorizontalAlignment.Center;
            full.CustomMinimumSize = new Vector2(0, M59Skin.RowH);
            full.VerticalAlignment = VerticalAlignment.Center;
            _rows.AddChild(full);
        }

        Visible = true;
        Layout();
    }
}
