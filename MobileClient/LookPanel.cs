using System;
using Godot;
using Meridian59.Data;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// What you see when you look at something.
///
/// `UIObjectDetails.cpp` is the window this follows: a picture of the
/// thing, its name in the colour the server gives it, the description the
/// server sent, and an inscription underneath when the thing carries one.
///
/// Four kinds of look share this one panel, because they are the same
/// window with different lines on it: an object, a spell, a skill, and a
/// PLAYER. The player half was missing entirely - see Player() - which
/// left the Look button doing nothing at all when it was aimed at
/// somebody, which is what it is aimed at most.
///
/// All four come from the client's own `Data.LookObject`, an `ObjectInfo`
/// the library fills from the server's reply - the object, the message,
/// the inscription, the look type and whether the window is up. The view
/// follows those; it does not decide when to appear. Sending the look is
/// the only half this client had.
///
/// The picture is composed with the **viewer's** frame, not the front one
/// - that is the one difference from the inventory's icons, and it is
/// what makes a creature in the look window face the way it faces in the
/// world.
/// </summary>
public partial class LookPanel : Control
{
    [Export] public int FontSize = 16;
    [Export] public int PictureSize = 128;

    ColorRect _shade, _panel;
    TextureRect _picture;
    Label _name;
    RichTextLabel _description;
    Label _inscription;
    Label _detail;
    Button _close;

    uint _shown;
    string _lastText = "";
    /// <summary>
    /// Which of the four things is on screen - the <see cref="Kind"/>'s
    /// name. The change test below is an id and a body of text, and ids
    /// are not unique ACROSS these four: a spell and a player can carry
    /// the same number, so without this a look at a player right after a
    /// look at a spell with the same id would be taken for "nothing
    /// changed" and the window would keep the wrong contents. The spell
    /// and skill branches used to leave it out of their own test, which
    /// is the same hole one step smaller: skill 5101 and spell 5101 with
    /// the same wording would have kept the previous kind's detail line
    /// and picture.
    /// </summary>
    string _kind = "";

    // Player-only rows. See Player() below.
    Label _titles, _websiteLine;
    TextEdit _descEdit;
    LineEdit _urlEdit;
    Button _save;
    bool _playerMode, _playerEditable;
    string _wasDesc = "", _wasUrl = "";

    /// <summary>
    /// The inscription as the server last sent it, which is what the
    /// Write button compares against. See that handler, and
    /// `UIObjectDetails.cpp:258-260`.
    /// </summary>
    string _wasIns = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // A shade behind it, which is what makes it a window rather
        // than a picture lying on the panel underneath. Opened from the
        // bag, the Look window left the bag's own Use / Drop / Look /
        // Hotbar row and its Close button live and undimmed below it -
        // two Close buttons on screen, and pressing the lower one shut
        // the bag and left this orphaned. MouseFilter.Stop is the half
        // that matters: it swallows the taps.
        _shade = new ColorRect { Color = new Color(0, 0, 0, 0.5f), Visible = false,
                                 MouseFilter = MouseFilterEnum.Stop };
        AddChild(_shade);

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.98f), Visible = false };
        AddChild(_panel);

        _picture = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_picture);

        _name = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeFontSizeOverride("font_size", FontSize + 4);
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _name.AddThemeConstantOverride("outline_size", 3);
        AddChild(_name);

        _description = new RichTextLabel { Visible = false, BbcodeEnabled = true, ScrollActive = true };
        _description.AddThemeFontSizeOverride("normal_font_size", FontSize);
        AddChild(_description);

        _inscription = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _inscription.AddThemeFontSizeOverride("font_size", FontSize - 1);
        _inscription.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.6f));
        AddChild(_inscription);

        // School, level and costs for a spell; school and level for a
        // skill; nothing for an object. One line under the name.
        _detail = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _detail.AddThemeFontSizeOverride("font_size", FontSize - 1);
        _detail.AddThemeColorOverride("font_color", new Color(0.72f, 0.78f, 0.9f));
        AddChild(_detail);

        // Signing a book, a tombstone or a deed. The reference makes
        // the inscription box writable and puts an OK beside it when
        // the object says it is both inscribed and editable
        // (`UIObjectDetails.cpp:188-201`), and OK sends
        // ChangeDescription with the object's id (:239-266). This
        // client showed the inscription as a plain label and had no way
        // to send one at all, so writing on anything was unreachable.
        _writing = new TextEdit { Visible = false };
        _writing.AddThemeFontSizeOverride("font_size", FontSize - 1);
        AddChild(_writing);

        _write = new Button { Text = "Write", Visible = false };
        _write.AddThemeFontSizeOverride("font_size", FontSize);
        // Only when the text has actually changed, which is what the
        // reference tests before it sends: OK on the object-details
        // window compares the box against `lookInfo->Inscription->
        // FullString` and sends ChangeDescription only if they differ
        // (`UIObjectDetails.cpp:258-260`). This button sent on every
        // press, so reading a scroll, pressing Write and closing rewrote
        // the inscription to exactly what it already said - a wasted
        // command against the client's rate limiter, and on a shared
        // object an edit the player never made.
        //
        // The compared value is what ARRIVED, remembered in Take below,
        // not whatever the box happens to hold: the box is refilled from
        // the server on every look, so comparing it against itself would
        // always say "unchanged". The reference's newline strip on the
        // same lines has no counterpart here - CEGUI's multi-line editbox
        // appends one and Godot's TextEdit does not - so there is nothing
        // to strip, and stripping regardless would silently eat a
        // trailing blank line the player typed on purpose.
        _write.Pressed += () =>
        {
            if (_shown == 0) return;
            string now = _writing.Text ?? "";
            if (now == _wasIns) return;
            _wasIns = now;
            Inscribe?.Invoke(_shown, now);
        };
        AddChild(_write);

        // Looking at a PLAYER. UserCommand (155) carrying a LookPlayer
        // fills `Data.LookPlayer` and raises its IsVisible
        // (`Meridian59/Data/DataController.cs:2774-2777`), and that is
        // all the client has ever done with it: this panel owned
        // LookObject, LookSpell and LookSkill and never read LookPlayer
        // at all, so aiming the Look button at another player sent a
        // request, got an answer, and showed nothing. The button was
        // dead for the one target players use it on most.
        //
        // The reference's window is `UIPlayerDetails.cpp`. It subscribes
        // to the model (`:36-37`) and reacts property by property
        // (`:76-164`): the object gives the picture and the name in the
        // colour the flags choose (`:82-99`), Titles is its own block
        // that GROWS the window (`:102-132`), Message is the description
        // (`:135-142`), Website is a line of its own (`:145-152`),
        // IsEditable decides whether the description and the website are
        // writable (`:155-160`), and IsVisible alone decides whether the
        // window is up (`:163-168`). Each of those has a row here.
        //
        // Titles sits under the name rather than in the bottom detail
        // line, because that is where the reference puts it and because
        // it is part of how a player is addressed - it reads as part of
        // the name, not as a footnote.
        _titles = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore,
                              AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _titles.AddThemeFontSizeOverride("font_size", FontSize - 1);
        _titles.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.62f));
        AddChild(_titles);

        _websiteLine = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _websiteLine.AddThemeFontSizeOverride("font_size", FontSize - 1);
        _websiteLine.AddThemeColorOverride("font_color", new Color(0.66f, 0.78f, 0.95f));
        AddChild(_websiteLine);

        // The writable pair, which appear in place of the read-only
        // description and website when the server says this look is
        // editable - which in practice is when you looked at yourself.
        // The reference does not hide them and show others; it flips
        // ReadOnly on the same two boxes (`UIPlayerDetails.cpp:155-160`).
        // Godot has no read-only RichTextLabel worth the name, so the
        // same effect is two widgets sharing one rectangle.
        _descEdit = new TextEdit { Visible = false, WrapMode = TextEdit.LineWrappingMode.Boundary };
        _descEdit.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_descEdit);

        _urlEdit = new LineEdit { Visible = false, PlaceholderText = "Website" };
        _urlEdit.AddThemeFontSizeOverride("font_size", FontSize - 1);
        AddChild(_urlEdit);

        // The reference has no Save button: its OK button both saves and
        // closes (`UIPlayerDetails.cpp:203-245`). A separate one is kept
        // here because Close on a phone is also what a back gesture
        // reaches, and a gesture should not be a way to publish a
        // half-typed description. Close saves too, for the same reason
        // the reference's OK does - see Close() - so nothing typed is
        // lost either way; this is just the explicit half.
        _save = new Button { Text = "Save", Visible = false };
        _save.AddThemeFontSizeOverride("font_size", FontSize);
        _save.Pressed += SaveSelf;
        AddChild(_save);

        // Named, because "Close" as a piece of TEXT is not this button:
        // the spell book behind this panel has one too, and a scripted
        // run that asks for a button reading "Close" gets the book's,
        // shuts the book, and photographs a look window that is still
        // up - which reads exactly like the soft-lock this panel used to
        // have and hid a run of it. AliasEditor names its own for the
        // same reason.
        _close = new Button { Text = "Close", Visible = false, Name = "lookClose" };
        _close.AddThemeFontSizeOverride("font_size", FontSize);
        _close.Pressed += Close;
        AddChild(_close);

        GetViewport().SizeChanged += Layout;
        Layout();
    }

    void Layout()
    {
        if (_panel == null) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float w = v.X - side;

        // As tall as it needs to be. A fixed 55% of the screen meant a
        // one-line description sat above two hundred pixels of empty
        // black with the Close button parked at the bottom of it - and
        // an inscription box floating in the middle of that void. The
        // text decides, between enough for the picture and the old
        // maximum.
        float textW = w - side;
        float bodyH = _description != null
            ? _description.GetThemeFont("normal_font").GetMultilineStringSize(
                  _description.Text ?? "", HorizontalAlignment.Left, textW,
                  _description.GetThemeFontSize("normal_font_size")).Y
            : 0f;
        float needed = PictureSize + 26f          // the picture and the name
                     + bodyH * 1.15f + 16f        // the description
                     + FontSize * 9.5f;           // detail, inscription, Close and air
        float h = Mathf.Clamp(needed, Mathf.Min(v.Y * 0.30f, 260f),
                              Mathf.Min(v.Y * 0.8f, 620f));
        float top = v.Y * 0.18f;

        _shade.Position = Vector2.Zero;
        _shade.Size = v;

        _panel.Position = new Vector2(side * 0.5f, top);
        _panel.Size = new Vector2(w, h);

        _picture.Position = new Vector2(side, top + 14f);
        _picture.Size = new Vector2(PictureSize, PictureSize);

        _name.Position = new Vector2(side + PictureSize + 16f, top + 16f);

        float textTop = top + PictureSize + 26f;
        _description.Position = new Vector2(side, textTop);
        _description.Size = new Vector2(w - side, h - (textTop - top) - FontSize * 5f);

        _inscription.Position = new Vector2(side, top + h - FontSize * 4.4f);
        _inscription.Size = new Vector2(w - side, FontSize * 2f);

        // The writable version takes the label's place, with the button
        // beside it rather than under, so the Close row does not move.
        float writeW = FontSize * 5f;
        _writing.Position = _inscription.Position;
        _writing.Size = new Vector2(w - side - writeW - 8f, FontSize * 2.4f);
        _write.Position = new Vector2(side + (w - side) - writeW, _inscription.Position.Y);
        _write.Size = new Vector2(writeW, FontSize * 2.4f);

        _detail.Position = new Vector2(side, top + h - FontSize * 6.4f);
        _detail.Size = new Vector2(w - side, FontSize * 1.8f);

        // The player rows reuse the slots the object rows sit in, which
        // is what keeps the two shapes of this window the same size and
        // the Close button in the same place under both. Titles takes
        // the space beside the picture under the name; the website takes
        // the inscription's line, with Save beside it exactly as Write
        // sits beside the inscription box.
        _titles.Position = new Vector2(side + PictureSize + 16f, top + 16f + FontSize + 10f);
        _titles.Size = new Vector2(w - side - PictureSize - 16f, FontSize * 3.2f);

        _descEdit.Position = _description.Position;
        _descEdit.Size = _description.Size;

        _websiteLine.Position = _inscription.Position;
        _websiteLine.Size = _inscription.Size;

        _urlEdit.Position = _inscription.Position;
        _urlEdit.Size = new Vector2(w - side - writeW - 8f, FontSize * 2.4f);
        _save.Position = new Vector2(side + (w - side) - writeW, _inscription.Position.Y);
        _save.Size = new Vector2(writeW, FontSize * 2.4f);

        _close.Position = new Vector2(side, top + h - FontSize * 2.4f - 8f);
        _close.Size = new Vector2(w - side, FontSize * 2.4f);
    }

    public void Close()
    {
        // Closing a look at your OWN description is how the reference
        // saves it: its OK button sends the description and the website
        // if either differs from what arrived, and only then clears
        // IsVisible (`UIPlayerDetails.cpp:203-245`). Not doing that here
        // would make the panel's edit boxes decorative.
        if (_playerMode && _playerEditable) SaveSelf();

        Show(false);

        // Writing the flag back is the whole of closing. The view does
        // not decide whether this window is up - the model's IsVisible
        // does, and `Sync` reads it again on the very next frame - so a
        // Close that only hid the controls was undone before it was
        // seen: the panel came back up, its full-screen shade went on
        // eating every touch, and with `GameView.PanelUp` true the
        // bottom row, the hotbar and all movement stayed gone for the
        // rest of the session. Only a relog cleared it, and a phone has
        // no ESC to fall back on.
        //
        // The reference does exactly this, once per window:
        // `UISpellDetails.cpp:178-187` (closed) and `:164-176` (ESC),
        // `UISkillDetails.cpp:167-176` and `:153-162`,
        // `UIPlayerDetails.cpp:243`. It gets away with one line each
        // because it has one window each. This panel is all four at
        // once, and it cleared only two of the four flags - LookObject
        // and LookPlayer - because those were the only two it had ever
        // kept a reference to.
        //
        // So the reference is not kept by hand any more. `_showing` is
        // whichever <see cref="Kind"/> row of the table below is on
        // screen, and every row carries its own `Hide`. A fifth kind of
        // look cannot forget to write its flag back, because a kind
        // that is not a row is never drawn - `Sync` walks the table and
        // nothing else - and a row cannot be written without a `Hide`:
        // the constructor demands one.
        _showing?.Hide(_data);
        _showing = null;
    }

    /// <summary>
    /// Sends a changed description and a changed website, and only if
    /// they changed.
    ///
    /// Both conditions are the reference's. It sends nothing unless the
    /// object looked at is your own avatar - `lookObj->ID ==
    /// Data->AvatarID` (`UIPlayerDetails.cpp:207-208`) - and it compares
    /// each field against what arrived before sending it (`:223-224`,
    /// `:238-239`). IsEditable is the same test arriving from the other
    /// direction: the server sets it on a look at yourself, and it is
    /// what the reference already trusts to decide whether the boxes are
    /// writable at all (`:155-160`).
    ///
    /// The comparison is not politeness. These are two separate server
    /// commands - ChangeDescription and a ChangeURL user command - and
    /// sending them on every close would spend the client's rate limiter
    /// on nothing and rewrite a profile the player only looked at.
    /// </summary>
    void SaveSelf()
    {
        if (!_playerMode || !_playerEditable) return;

        string desc = _descEdit.Text ?? "";
        if (desc != _wasDesc) { _wasDesc = desc; Describe?.Invoke(desc); }

        string url = _urlEdit.Text ?? "";
        if (url != _wasUrl) { _wasUrl = url; Homepage?.Invoke(url); }
    }

    void Show(bool on)
    {
        // Above whatever else is open - see Panels.ToFront.
        if (on) Panels.ToFront(this);
        // A description is asked for from somewhere - the spell book,
        // the quest log, a tap on the world - and has to land in front
        // of whatever asked. Those panels are siblings added later, so
        // without this it opens behind them.
        if (on) GetParent()?.MoveChild(this, -1);

        // Object, spell and skill rows on one side; the player rows on
        // the other. They share the panel and the slots, so exactly one
        // set is up at a time - a player's website in the inscription's
        // line while an inscription is also drawn there would be two
        // strings on top of each other.
        bool thing = on && !_playerMode;
        bool who = on && _playerMode;

        _shade.Visible = on;
        _panel.Visible = on;
        _picture.Visible = on && _picture.Texture != null;
        _name.Visible = on;
        // In player mode the read-only description gives way to the
        // writable one when the server says this look is editable, which
        // is the reference's ReadOnly flip (`UIPlayerDetails.cpp:155-160`)
        // expressed as two widgets in one rectangle.
        _description.Visible = on && !(who && _playerEditable);
        _inscription.Visible = thing && !_editable && !string.IsNullOrWhiteSpace(_inscription.Text);
        _writing.Visible = thing && _editable;
        _write.Visible = thing && _editable;
        _detail.Visible = thing && !string.IsNullOrWhiteSpace(_detail.Text);

        _titles.Visible = who && !string.IsNullOrWhiteSpace(_titles.Text);
        _descEdit.Visible = who && _playerEditable;
        _websiteLine.Visible = who && !_playerEditable && !string.IsNullOrWhiteSpace(_websiteLine.Text);
        _urlEdit.Visible = who && _playerEditable;
        _save.Visible = who && _playerEditable;

        _close.Visible = on;
        // The window is as tall as its text, so it has to be laid out
        // again every time the text changes - which is every time it
        // is shown.
        Layout();
    }

    /// <summary>
    /// One kind of look: the model flag that says it is up, the way to
    /// write that flag back, and the way to put it on screen.
    ///
    /// The point of the type is the middle one. Four models carry an
    /// IsVisible and none of them share an interface that has it, so
    /// before this each branch reached for its own model by hand and two
    /// of the four never stored theirs - which is the soft-lock Close()
    /// describes. A row cannot exist without a Hide, and a kind that has
    /// no row is never drawn, so the next kind of look added here is
    /// closable by construction rather than by remembering.
    /// </summary>
    sealed class Kind
    {
        public readonly string Name;
        /// <summary>Whether the server has this kind's window up.</summary>
        public readonly Func<DataController, bool> Up;
        /// <summary>Clears the flag, which is what closing means.</summary>
        public readonly Action<DataController> Hide;
        /// <summary>Fills the rows from this kind's model.</summary>
        public readonly Action<DataController> Draw;
        /// <summary>Whether it was up last frame - see Sync.</summary>
        public bool WasUp;

        public Kind(string name, Func<DataController, bool> up,
                    Action<DataController> hide, Action<DataController> draw)
        {
            Name = name; Up = up; Hide = hide; Draw = draw;
        }
    }

    Kind[] _kinds;
    /// <summary>The row on screen, and so the flag Close must clear.</summary>
    Kind _showing;
    /// <summary>The last data seen, so Close can reach the model.</summary>
    DataController _data;

    /// <summary>
    /// The four kinds, in the order a tie is broken.
    ///
    /// Ties are rarer than they look. The library keeps LookObject,
    /// LookSpell and LookSkill mutually exclusive itself - each of
    /// `DataController.cs:2900-2928` raises one and clears the other two
    /// - so at most one of those three is ever up. LookPlayer is the odd
    /// one out: `DataController.cs:2774-2777` raises it and clears
    /// nothing, and the object/spell/skill handlers do not clear it
    /// either, so a look at a player and a look at a thing really can
    /// both be up at once. The reference has a window each and shows
    /// both; this panel is one window, so it shows the one that arrived
    /// LAST - see Sync - and this order only decides what to do if two
    /// were already up when the panel first looked.
    /// </summary>
    Kind[] Kinds() => _kinds ??= new[]
    {
        new Kind("spell",  d => d?.LookSpell?.IsVisible == true,
                           d => { if (d?.LookSpell  != null) d.LookSpell.IsVisible  = false; },
                           DrawSpell),
        new Kind("skill",  d => d?.LookSkill?.IsVisible == true,
                           d => { if (d?.LookSkill  != null) d.LookSkill.IsVisible  = false; },
                           DrawSkill),
        new Kind("player", d => d?.LookPlayer?.IsVisible == true,
                           d => { if (d?.LookPlayer != null) d.LookPlayer.IsVisible = false; },
                           DrawPlayer),
        new Kind("object", d => d?.LookObject?.IsVisible == true,
                           d => { if (d?.LookObject != null) d.LookObject.IsVisible = false; },
                           DrawObject),
    };

    /// <summary>
    /// A new inscription for the object being looked at: its id and the
    /// text. Raised by the Write button.
    /// </summary>
    public event System.Action<uint, string> Inscribe;

    /// <summary>
    /// A new description for your OWN avatar, from the player-details
    /// half of this window. Separate from <see cref="Inscribe"/> because
    /// the server commands are separate: the reference sends the
    /// one-argument ChangeDescription for yourself
    /// (`UIPlayerDetails.cpp:225`) and the two-argument one, carrying an
    /// object id, for a thing you are carrying
    /// (`UIObjectDetails.cpp:239-266`).
    /// </summary>
    public event System.Action<string> Describe;

    /// <summary>
    /// A new homepage for your own avatar. Its own server command
    /// entirely - a ChangeURL user command, not a description
    /// (`UIPlayerDetails.cpp:240`).
    /// </summary>
    public event System.Action<string> Homepage;

    TextEdit _writing;
    Button _write;
    bool _editable;

    /// <summary>
    /// Follows the client's look object. Everything here - whether the
    /// window is up, what is in it - is the server's, by way of the
    /// library.
    /// </summary>
    public void Sync(DataController data)
    {
        _data = data;

        // A spell or a skill description arrives in its own place -
        // LookSpell and LookSkill, each with its own IsVisible - and the
        // game gives each its own window (UISpellDetails.cpp,
        // UISkillDetails.cpp). They are the same window with different
        // lines on it, so this one does all four, and the extra lines
        // come from the same places the game reads them.
        //
        // Whichever ARRIVED last wins, not whichever sits earliest in
        // the table: a flag that was down last frame and is up this one
        // is a look that has just come in, and a look that has just come
        // in is the one the player asked for. Without that a look at a
        // player - the one flag the library does not clear when another
        // look arrives (`DataController.cs:2774-2777`) - would pin this
        // window shut against every object look after it until it was
        // closed by hand.
        Kind arrived = null, anyUp = null;
        foreach (Kind k in Kinds())
        {
            bool up = k.Up(data);
            if (up && !k.WasUp) arrived = k;
            if (up && anyUp == null) anyUp = k;
            k.WasUp = up;
        }

        if (arrived != null) _showing = arrived;
        // What was showing has been closed - by this panel, by another
        // look replacing it, or by the library clearing all four on a
        // save or a logout (`DataController.cs:1035`, `:2363`). Fall
        // back to whatever else is still up, which is normally nothing.
        else if (_showing == null || !_showing.Up(data)) _showing = anyUp;

        if (_showing == null)
        {
            _playerMode = false;
            if (IsOpen) Show(false);
            return;
        }

        _showing.Draw(data);
        if (!IsOpen) Show(true);
    }

    /// <summary>
    /// A look at a thing. `UIObjectDetails.cpp` is the window.
    /// </summary>
    void DrawObject(DataController data)
    {
        _playerMode = false;
        ObjectInfo _info = data.LookObject;

        ObjectBase o = _info.ObjectBase;
        string text = _info.Message?.FullString ?? "";
        string ins = _info.Inscription?.FullString ?? "";
        _detail.Text = "";

        uint id = o?.ID ?? 0;
        if (id != _shown || text != _lastText || _kind != "object")
        {
            _shown = id;
            _lastText = text;
            _kind = "object";

            _name.Text = o?.Name ?? "";
            if (o?.Flags != null)
            {
                uint argb = NameColors.GetColorFor(o.Flags);
                _name.AddThemeColorOverride("font_color", new Color(
                    ((argb >> 16) & 0xFF) / 255f,
                    ((argb >> 8) & 0xFF) / 255f,
                    (argb & 0xFF) / 255f));
            }

            _description.Text = Safe(text);
            _inscription.Text = Safe(ins);

            // Both flags, as the reference tests both: inscribed says
            // there is an inscription, editable says you may change it
            // (LookTypeFlags.cs:64, :73).
            _editable = _info.LookType != null
                     && _info.LookType.IsInscribed && _info.LookType.IsEditable;
            _writing.Text = ins;
            // What arrived, for the Write button to compare against.
            _wasIns = ins ?? "";

            try
            {
                // The viewer's frame, which is what this window uses and
                // the inventory does not.
                _picture.Texture = M59Assets.FromTex(Viewer(o, PictureSize));
            }
            catch (Exception e) { GD.PrintErr($"[Look] {o?.Name}: {e.Message}"); }
        }
    }

    /// <summary>
    /// A spell description. `UISpellDetails.cpp` shows the name, the
    /// school, the level, and what it costs in mana and vigor - each a
    /// `ServerString` the server sends already worded, so none of it is
    /// composed here.
    /// </summary>
    void DrawSpell(DataController data)
    {
        SpellInfo info = data.LookSpell;
        _playerMode = false;

        ObjectBase o = info.ObjectBase;
        string text = info.Message?.FullString ?? "";
        uint id = o?.ID ?? 0;

        // _kind belongs in the test here as much as in the object and
        // player branches, and was missing from both this one and the
        // skill one: the fixture's skill 5101 and a spell of the same
        // number with the same wording would have left the previous
        // kind's school/level/mana line and picture in place.
        if (id != _shown || text != _lastText || _kind != "spell")
        {
            _shown = id; _lastText = text; _kind = "spell";
            _name.Text = o?.Name ?? "";
            Tint(o);
            _description.Text = Safe(text);
            _inscription.Text = "";
            _detail.Text = Join(
                info.SchoolName?.FullString,
                info.SpellLevel?.FullString,
                info.ManaCost?.FullString,
                info.VigorCost?.FullString);
            Picture(o);
        }
    }

    /// <summary>
    /// A skill description - the same window with two lines instead of
    /// four, which is all `UISkillDetails.cpp` has.
    /// </summary>
    void DrawSkill(DataController data)
    {
        SkillInfo info = data.LookSkill;
        _playerMode = false;

        ObjectBase o = info.ObjectBase;
        string text = info.Message?.FullString ?? "";
        uint id = o?.ID ?? 0;

        if (id != _shown || text != _lastText || _kind != "skill")
        {
            _shown = id; _lastText = text; _kind = "skill";
            _name.Text = o?.Name ?? "";
            Tint(o);
            _description.Text = Safe(text);
            _inscription.Text = "";
            _detail.Text = Join(info.SchoolName?.FullString, info.SkillLevel?.FullString);
            Picture(o);
        }
    }

    /// <summary>
    /// A look at another player - or at yourself.
    ///
    /// `Data.LookPlayer` is a `PlayerInfo`, filled and raised by the data
    /// layer when UserCommand (155) arrives carrying a LookPlayer
    /// (`Meridian59/Data/DataController.cs:2774-2777`). Everything on
    /// screen here is one of its properties, and the reference reacts to
    /// each of them by name (`UIPlayerDetails.cpp:76-164`):
    ///
    ///   ObjectBase  the picture and the name, coloured by the flags
    ///               (`:82-99`) - the same NameColors the object branch
    ///               uses, so a guildmate reads as a guildmate here too.
    ///   Titles      its own block under the name (`:102-132`). A
    ///               ServerString on this build, a plain string on
    ///               VANILLA (`PlayerInfo.cs:203-232`).
    ///   Message     the description (`:135-142`).
    ///   Website     a line of its own (`:145-152`).
    ///   IsEditable  whether the description and website are writable
    ///               (`:155-160`).
    ///   IsVisible   whether the window is up at all (`:163-168`).
    ///
    /// IsVisible is the server's flag and the only thing consulted about
    /// whether to appear - this view does not decide that, exactly as the
    /// object, spell and skill branches above do not.
    ///
    /// It is a row of the table in Kinds() like the other three, not a
    /// case in place of the object one: four kinds of look, one panel,
    /// whichever kind the server most recently declared visible. It is
    /// also the one whose flag the library never clears for you, which
    /// is why Sync goes by arrival rather than by table order.
    /// </summary>
    void DrawPlayer(DataController data)
    {
        PlayerInfo info = data.LookPlayer;
        _playerMode = true;

        ObjectBase o = info.ObjectBase;
        string text = info.Message?.FullString ?? "";
        uint id = o?.ID ?? 0;

        // Editability arrives from the server, and it is what the
        // reference trusts rather than comparing ids itself when it
        // decides whether the boxes are writable (`:155-160`).
        _playerEditable = info.IsEditable;

        if (id != _shown || text != _lastText || _kind != "player")
        {
            _shown = id; _lastText = text; _kind = "player";

            _name.Text = o?.Name ?? "";
            Tint(o);

            // Titles is a ServerString on this build - the library's
            // non-VANILLA branch (`PlayerInfo.cs:203-217`) - and the
            // reference reads FullString off it for exactly this
            // (`UIPlayerDetails.cpp:106`).
            _titles.Text = info.Titles?.FullString ?? "";

            _description.Text = Safe(text);
            _inscription.Text = "";
            _detail.Text = "";

            string url = info.Website ?? "";
            _websiteLine.Text = string.IsNullOrWhiteSpace(url) ? "" : url;

            // The originals, kept so a close can tell a changed field
            // from an untouched one - which is the comparison the
            // reference makes before it sends anything (`:223-224`,
            // `:238-239`).
            _wasDesc = text;
            _wasUrl = url;
            _descEdit.Text = text;
            _urlEdit.Text = url;

            Picture(o);
        }
    }

    static string Join(params string[] parts)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (string p in parts)
            if (!string.IsNullOrWhiteSpace(p)) kept.Add(p.Trim());
        return string.Join("   ", kept);
    }

    void Tint(ObjectBase o)
    {
        if (o?.Flags == null) return;
        uint argb = NameColors.GetColorFor(o.Flags);
        _name.AddThemeColorOverride("font_color", new Color(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f));
    }

    void Picture(ObjectBase o)
    {
        try { _picture.Texture = M59Assets.FromTex(Viewer(o, PictureSize)); }
        catch (Exception e) { GD.PrintErr($"[Look] {o?.Name}: {e.Message}"); }
    }

    /// <summary>
    /// Composes with the viewer's frame. `M59Compose.Icon` uses the front
    /// frame, as the inventory wants; this window wants the other.
    /// </summary>
    static Tex Viewer(ObjectBase o, int size)
    {
        if (o is RoomObject ro)
        {
            Tex t = M59Compose.Build(ro, out _, out _);
            if (t != null) return t;
        }
        return M59Compose.Icon(o, size);
    }

    /// <summary>
    /// Text on its way into a BBCode label, with the one character
    /// that would be read as markup taken out of its way.
    ///
    /// The reference writes a description into a plain edit box
    /// (`UIObjectDetails.cpp:107`) where a bracket is a bracket. Here
    /// the label parses BBCode, so a '[' anywhere in a server string
    /// opens a tag and swallows the rest of the line. The other panels
    /// in this client already knew that - MailPanel, NewsPanel and
    /// NpcQuestsPanel all do this - and this one did not.
    /// </summary>
    static string Safe(string s) => s == null ? "" : s.Replace("[", "[lb]");
}
