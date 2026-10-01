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
    Label _title;
    ColorRect _bg;

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

        _bg = new ColorRect { Color = new Color(0, 0, 0, 0.82f) };
        AddChild(_bg);

        _title = new Label { Text = "Choose a character" };
        _title.AddThemeFontSizeOverride("font_size", FontSize + 6);
        _title.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
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
        _motd.AddThemeFontSizeOverride("normal_font_size", FontSize - 2);
        _motd.AddThemeColorOverride("default_color", new Color(0.82f, 0.85f, 0.92f));
        AddChild(_motd);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 10);
        AddChild(_rows);

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

        float pad = Panels.Side(v, 0.08f, 24f);
        _title.Position = new Vector2(pad, pad);

        // The message takes as much room as it needs and no more, up to
        // a third of the screen - past which it scrolls, because the
        // character buttons are what this screen is for and they must
        // not be pushed off it by a long announcement.
        float top = pad + FontSize * 3f;
        if (_motd != null && _motd.Visible)
        {
            _motd.Position = new Vector2(pad, top);
            _motd.Size = new Vector2(v.X - pad * 2, Mathf.Min(_motdH, v.Y * 0.33f));
            top += _motd.Size.Y + FontSize;
        }

        _rows.Position = new Vector2(pad, top);
        _rows.Size = new Vector2(v.X - pad * 2, v.Y - pad - top);
    }

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
        _motdH = 0f;
        if (any)
        {
            // Measured rather than guessed, so the button list starts
            // directly under the message instead of below a fixed block
            // of empty space.
            Vector2 v = GetViewportRect().Size;
            float pad = Panels.Side(v, 0.08f, 24f);
            _motdH = _motd.GetThemeFont("normal_font").GetMultilineStringSize(
                         _motd.Text, HorizontalAlignment.Left, v.X - pad * 2,
                         _motd.GetThemeFontSize("normal_font_size")).Y * 1.2f + 8f;
        }
        Layout();
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

        foreach (CharSelectItem c in characters)
        {
            CharSelectItem captured = c;          // do not close over the loop variable
            var b = new Button
            {
                Text = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name,
                CustomMinimumSize = new Vector2(0, FontSize * 2.8f),
            };
            b.AddThemeFontSizeOverride("font_size", FontSize);
            b.Pressed += () => { Visible = false; Chosen?.Invoke(captured); };
            _rows.AddChild(b);
        }

        if (CanCreate)
        {
            var make = new Button
            {
                Text = "New character",
                CustomMinimumSize = new Vector2(0, FontSize * 2.8f),
                Name = "newCharacter",
            };
            make.AddThemeFontSizeOverride("font_size", FontSize);
            make.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.6f));
            make.Pressed += () => { Visible = false; NewWanted?.Invoke(); };
            _rows.AddChild(make);
        }

        Visible = true;
        Layout();
    }
}
