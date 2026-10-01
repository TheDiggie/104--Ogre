using System;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

/// <summary>
/// The guild shield designer: pick two colours and a design, see what it
/// looks like, and claim it for your guild.
///
/// This is the fourth tab of the game's guild window - `UIGuild.cpp:15`
/// names it `Guild.TabShield` - and it was the one piece of that window
/// this client did not have. It is a tab there and a panel here for one
/// reason only: the guild window already fills a phone screen, and the
/// designer wants a picture the size of a thumb's reach next to three
/// steppers. GuildPanel opens it with a button, and closing it puts you
/// back on the roster.
///
/// What the player actually chooses is three bytes, no more
/// (`UIGuild.cpp:44-53`):
///
///  - Colour 1 and Colour 2, each 0 to NUMGUILDCOLORS-1. That constant is
///    computed rather than declared - `ColorTransformations.cs:172` takes
///    the square root of the guild colour block in the palette
///    (0x87..0xFF, :157-158), which is eleven, because a shield is a pair
///    of colour ramps and the block holds every pair. The two bytes are
///    folded back into one palette index by
///    `ColorTransformation.GetGuildShieldColor` (:907-910), which is what
///    dyes the preview.
///  - Design, which indexes the list of shield art the server sent. The
///    game's slider is zero-based and the value on the wire is not:
///    `Design` is documented as "not zero based, first one is '1'"
///    (`GuildShieldInfo.cs:222-225`) and the callback converts with a
///    `+ 1.0f` (`UIGuild.cpp:925`). The steppers here show the number the
///    player would count - design 1 of n - and store the byte the server
///    wants.
///
/// The protocol is four messages and the shape of it is worth stating,
/// because one of them does double duty:
///
///  - `UserCommandGuildShieldListReq` (type 32, one byte: the type) asks
///    for the shield art. The answer shares that type number -
///    `UserCommandGuildShieldList`, a count and that many ResourceIDBGF -
///    and the two are told apart by which way the message was travelling
///    (`UserCommandGuildShieldList.cs:26-30`). DataController stores the
///    array on `GuildShieldInfo.Shields` (`DataController.cs:2807-2814`),
///    and the resources are resolved and decompressed on the way in
///    (`MessageEnrichment.cs:438-452`), so the art is ready to draw by
///    the time it lands.
///  - `UserCommandGuildShieldInfoReq` (type 31) asks about your own
///    guild's shield; the response of the same type carries a whole
///    GuildShieldInfo - guild id, guild name, colour 1, colour 2, design
///    (`GuildShieldInfo.cs:89-112`) - and is merged into the model
///    (`DataController.cs:2794-2800`).
///  - `UserCommandClaimShield` (type 33) is the interesting one: colour 1,
///    colour 2, design and a **ReallyClaim** flag
///    (`UserCommandClaimShield.cs:36-56`). With the flag false it is a
///    question - who holds this design in these colours? - and with it
///    true it is the claim. The reference sends the question on every
///    single change of any of the three settings (`UIGuild.cpp:927-934`),
///    which is how the "claimed by" line under the picture stays true,
///    and sends the claim from the button (:940-946). That is copied
///    exactly, because nothing else tells you whether a design is free.
///  - `UserCommandGuildShieldError` (type 38) is the server's refusal,
///    a ServerString which the reference shows in an OK popup
///    (`UIGuild.cpp:261-268`). GameView already watches for that one and
///    still does; this panel does not duplicate it.
///
/// Note what the claim command does NOT carry: the three bytes are read
/// off `Data.GuildShieldInfo` when the message is built
/// (`BaseClient.cs:1312-1325`), not passed in. So the settings must be
/// written into the model before either command is sent, which is what
/// the reference's callback does (`UIGuild.cpp:923-925`) and what this
/// does too. A panel that kept its own copy of the numbers would send
/// stale ones.
///
/// Who may see this: the reference removes the shield tab, along with the
/// guildmaster tab, whenever `IsRenounce` is set - its way of saying you
/// are an ordinary member (`UIGuild.cpp:176-212`) - and puts them back
/// when `IsDisband` is. So the panel closes itself unless the flags say
/// guildmaster, the same test GuildPanel already uses for the password
/// and hall buttons.
/// </summary>
public partial class GuildShieldPanel : Control
{
    [Export] public int FontSize = 16;

    /// <summary>
    /// How big the picture is drawn. The reference sizes its composer off
    /// the layout's own pixel size (`UIGuild.cpp:60-61`); there is no
    /// layout file here, so this is the box and the art is scaled into it.
    /// </summary>
    [Export] public int PreviewSize = 176;

    /// <summary>
    /// Ask the server for the shield art and for our own shield, which is
    /// the pair of requests the reference sends before showing the window
    /// (`UIGuild.cpp:618-619`, `UIMainButtonsRight.cpp:73-74`).
    /// </summary>
    public event Action Requested;

    /// <summary>
    /// A setting changed: send ClaimShield with ReallyClaim false, which
    /// asks the server about this design rather than taking it
    /// (`UIGuild.cpp:932-933`).
    /// </summary>
    public event Action Preview;

    /// <summary>Take this shield: ClaimShield with ReallyClaim true (:943).</summary>
    public event Action Claim;

    ColorRect _panel;
    Label _title, _claimedByDesc, _claimedBy, _colour1Desc, _colour2Desc, _designDesc, _note;
    TextureRect _image;
    Button _claim, _close;
    Button _c1Less, _c1More, _c2Less, _c2More, _dLess, _dMore, _turnLeft, _turnRight;

    GuildShieldInfo _shield;

    /// <summary>What the picture and the labels were last built from.</summary>
    string _signature = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.97f), Visible = false };
        AddChild(_panel);

        _title = Heading("Guild Shield", FontSize + 4, new Color(1, 0.92f, 0.6f));

        // The picture. KeepAspectCentered rather than a stretch: the
        // composed frame is already centred in a square box of its own
        // (see Build below), and stretching it again would squash art
        // that is taller than it is wide.
        _image = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
            Name = "shieldimage",
        };
        AddChild(_image);

        // Said when there is nothing to draw - before the shield list has
        // arrived, or for a design whose art would not decompress. The
        // reference has no equivalent because its composer simply leaves
        // the image window empty, and an empty square on a phone reads as
        // a broken panel rather than as "waiting".
        _note = Heading("", FontSize - 2, new Color(0.7f, 0.72f, 0.78f));

        _colour1Desc = Heading("Color 1", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _colour2Desc = Heading("Color 2", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _designDesc = Heading("Design", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _claimedByDesc = Heading("Claimed by", FontSize, new Color(0.86f, 0.88f, 0.92f));
        _claimedBy = Heading("", FontSize, new Color(1, 0.92f, 0.6f));

        // Steppers, not drag bars. The reference's three controls are
        // CEGUI sliders with `setClickStep(1.0f)` (`UIGuild.cpp:44-46`) -
        // whole steps through a short range - and OptionsPanel already
        // settled the house answer for that shape: a thin slider is not
        // something a thumb can place, and these have eleven stops at
        // most. Minus and plus it is.
        _c1Less = Push("-", () => StepColour1(-1), "c1less");
        _c1More = Push("+", () => StepColour1(+1), "c1more");
        _c2Less = Push("-", () => StepColour2(-1), "c2less");
        _c2More = Push("+", () => StepColour2(+1), "c2more");
        _dLess = Push("-", () => StepDesign(-1), "dless");
        _dMore = Push("+", () => StepDesign(+1), "dmore");

        // Turning the shield. In the game you do it with the mouse wheel
        // over the picture, one notch being 200 of the angle's 4096
        // (`UIGuild.cpp:974-983`); a phone has no wheel, so the same two
        // hundred goes on a pair of buttons. It is local only - nothing
        // is sent, and the angle is not part of the design.
        _turnLeft = Push("<", () => Turn(-200), "turnleft");
        _turnRight = Push(">", () => Turn(+200), "turnright");

        _claim = Push("Claim shield", () => Claim?.Invoke(), "claim");
        _close = Push("Close", Close, "close");

        GetViewport().SizeChanged += Layout;
    }

    Label Heading(string text, int size, Color color)
    {
        var l = new Label { Text = text, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
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
    /// Opens the designer and asks for what it needs. The reference never
    /// opens the window itself - the guild window arrives because the
    /// server sent a GuildInfo - but it does send both shield requests
    /// every time it is about to be shown (`UIGuild.cpp:618-619`), and the
    /// art list in particular is needed before any of the controls mean
    /// anything.
    /// </summary>
    public void Open()
    {
        if (_panel == null) return;
        _signature = "";
        _image.Texture = null;
        _note.Text = "Waiting for the shield designs.";
        Show(true);
        Requested?.Invoke();
    }

    public void Close()
    {
        _signature = "";
        Show(false);
    }

    void Show(bool on)
    {
        if (on) Panels.ToFront(this);
        _panel.Visible = on;
        _title.Visible = on;
        _image.Visible = on && _image.Texture != null;
        _note.Visible = on && _image.Texture == null;
        _colour1Desc.Visible = on; _colour2Desc.Visible = on; _designDesc.Visible = on;
        _claimedByDesc.Visible = on; _claimedBy.Visible = on;
        _c1Less.Visible = on; _c1More.Visible = on;
        _c2Less.Visible = on; _c2More.Visible = on;
        _dLess.Visible = on; _dMore.Visible = on;
        _turnLeft.Visible = on; _turnRight.Visible = on;
        _claim.Visible = on; _close.Visible = on;
        Layout();
    }

    void Layout()
    {
        if (_panel == null || !_panel.Visible) return;
        Vector2 v = GetViewportRect().Size;

        float side = Panels.Side(v, 0.06f);
        float rowH = FontSize * 2.6f;
        float height = Mathf.Min(v.Y * 0.8f, 780f);
        float top = v.Y - height - side * 0.5f;
        float w = v.X - side * 2f;

        _panel.Position = new Vector2(side * 0.5f, top - 12f);
        _panel.Size = new Vector2(v.X - side, height + 12f);

        float y = top;
        _title.Position = new Vector2(side, y); y += FontSize * 1.9f;

        // The picture, centred, with the two turn buttons either side of
        // it - which is where a wheel over the image would have been.
        float box = Mathf.Min(PreviewSize, Mathf.Max(96f, w - rowH * 2f - 24f));
        float imgX = side + (w - box) * 0.5f;
        _image.Position = new Vector2(imgX, y);
        _image.Size = new Vector2(box, box);
        _note.Position = new Vector2(side, y + box * 0.5f - FontSize);
        _note.Size = new Vector2(w, FontSize * 2f);

        _turnLeft.Position = new Vector2(side, y + box * 0.5f - rowH * 0.5f);
        _turnLeft.Size = new Vector2(rowH, rowH);
        _turnRight.Position = new Vector2(side + w - rowH, y + box * 0.5f - rowH * 0.5f);
        _turnRight.Size = new Vector2(rowH, rowH);

        y += box + 12f;

        // Three stepper rows: label on the left, minus and plus on the
        // right. The label carries the value, because a separate value
        // column costs width a phone does not have.
        y = StepperRow(_colour1Desc, _c1Less, _c1More, side, y, w, rowH);
        y = StepperRow(_colour2Desc, _c2Less, _c2More, side, y, w, rowH);
        y = StepperRow(_designDesc, _dLess, _dMore, side, y, w, rowH);

        _claimedByDesc.Position = new Vector2(side, y);
        _claimedBy.Position = new Vector2(side + w * 0.35f, y);
        y += rowH;

        float by = top + height - rowH * 2f - 8f;
        _claim.Position = new Vector2(side, by);
        _claim.Size = new Vector2(w, rowH);
        by += rowH + 8f;
        _close.Position = new Vector2(side, by);
        _close.Size = new Vector2(w, rowH);
    }

    float StepperRow(Label label, Button less, Button more, float side, float y, float w, float rowH)
    {
        label.Position = new Vector2(side, y + rowH * 0.25f);
        less.Position = new Vector2(side + w - rowH * 2f - 8f, y);
        less.Size = new Vector2(rowH, rowH);
        more.Position = new Vector2(side + w - rowH, y);
        more.Size = new Vector2(rowH, rowH);
        return y + rowH + 6f;
    }

    /// <summary>
    /// The one place any of the three settings is changed, and it is the
    /// reference's own guard: nothing is applied and nothing is sent
    /// until the shield list has arrived and the design index is inside
    /// it (`UIGuild.cpp:916-917`). Before that the model has no art to
    /// point the preview at, and the server has no design to answer
    /// about.
    ///
    /// The values are written into the shared model rather than kept
    /// here, because that is where the claim command reads them from
    /// (`BaseClient.cs:1312-1325`), and a change is only sent when it is
    /// a change (`UIGuild.cpp:927-931`) - the reference compares the old
    /// three bytes with the new ones, and a stepper at the end of its
    /// range must not put a message on the wire.
    /// </summary>
    void Apply(Func<GuildShieldInfo, bool> change)
    {
        GuildShieldInfo s = _shield;
        if (s == null || s.Shields == null) return;
        int design = s.Design - 1;
        if (design < 0 || design >= s.Shields.Length) return;

        byte was1 = s.Color1, was2 = s.Color2, wasD = s.Design;
        if (!change(s)) return;
        if (s.Color1 == was1 && s.Color2 == was2 && s.Design == wasD) return;

        _signature = "";           // the picture and the labels are stale
        Preview?.Invoke();
    }

    void StepColour1(int by) => Apply(s =>
    {
        int want = Mathf.Clamp(s.Color1 + by, 0, ColorTransformation.NUMGUILDCOLORS - 1);
        s.Color1 = (byte)want;
        return true;
    });

    void StepColour2(int by) => Apply(s =>
    {
        int want = Mathf.Clamp(s.Color2 + by, 0, ColorTransformation.NUMGUILDCOLORS - 1);
        s.Color2 = (byte)want;
        return true;
    });

    void StepDesign(int by) => Apply(s =>
    {
        // Stored one-based, so the clamp is against the count rather than
        // the last index (`GuildShieldInfo.cs:222-225`).
        int want = Mathf.Clamp(s.Design + by, 1, s.Shields.Length);
        s.Design = (byte)want;
        return true;
    });

    /// <summary>
    /// Turns the example model. Local: the angle decides which frame of
    /// the art is picked, nothing else, and the reference sends nothing
    /// when the wheel moves (`UIGuild.cpp:974-983`).
    /// </summary>
    void Turn(int by)
    {
        ObjectBase model = _shield?.ExampleModel;
        if (model == null) return;
        model.ViewerAngle = (ushort)(model.ViewerAngle + by);
        _signature = "";
    }

    /// <summary>
    /// Called every frame with the live model. Rebuilds nothing unless
    /// something it draws has moved, which matters here more than in the
    /// list panels: composing the picture reads and scales a BGF frame.
    /// </summary>
    public void Sync(GuildShieldInfo shield, GuildInfo guild)
    {
        _shield = shield;
        if (!IsOpen) return;

        // The tab does not exist for an ordinary member, and it goes away
        // the moment the flags say so (`UIGuild.cpp:176-212`); and the
        // whole guild window closing takes it with it, since the
        // reference's is a tab inside that window and clears this model
        // on the way out (:948-956).
        bool master = guild != null && guild.IsVisible
                      && guild.Flags != null && guild.Flags.IsDisband;
        if (shield == null || !master) { Close(); return; }

        ObjectBase model = shield.ExampleModel;
        var sb = new System.Text.StringBuilder();
        sb.Append(shield.Color1).Append('|').Append(shield.Color2).Append('|')
          .Append(shield.Design).Append('|')
          .Append(shield.Shields != null ? shield.Shields.Length : 0).Append('|')
          .Append(shield.GuildName).Append('|')
          .Append(model != null ? model.ViewerAngle : 0).Append('|')
          .Append(model?.Resource != null ? '+' : '-');
        string now = sb.ToString();
        if (now == _signature) return;
        _signature = now;

        int count = shield.Shields != null ? shield.Shields.Length : 0;
        _colour1Desc.Text = $"Color 1    {shield.Color1}";
        _colour2Desc.Text = $"Color 2    {shield.Color2}";
        _designDesc.Text = count > 0
            ? $"Design    {shield.Design} of {count}"
            : "Design    waiting for the list";

        // "Claimed by", straight off the model, and the button's state
        // with it: the reference enables Claim only when the name is
        // empty, which is the server's way of saying the design is free
        // (`UIGuild.cpp:239-246`). An unclaimed shield arrives with a
        // zero guild id, and GuildShieldInfo blanks the name itself in
        // that case (`GuildShieldInfo.cs:145-156`); the white-on-white
        // pair 9/9 is the one it labels "Design not available" instead
        // (:34-35, :188-190).
        string claimedBy = shield.GuildName ?? "";
        _claimedBy.Text = string.IsNullOrEmpty(claimedBy) ? "nobody - it is free" : claimedBy;
        _claim.Disabled = !string.IsNullOrEmpty(claimedBy) || count == 0;

        Build(model);
        Show(true);
    }

    /// <summary>
    /// Composes the preview.
    ///
    /// The reference builds it with an ImageComposer set up four ways
    /// that all differ from a room object's (`UIGuild.cpp:56-63`): no Y
    /// offset, no power-of-two padding, the front frame rather than the
    /// viewer's, and centred in a fixed box. That is exactly what
    /// M59Compose.Icon does for an ObjectBase, so the picture comes out
    /// of the same arithmetic as the game's - including the last of those
    /// four, which the library quietly overrides for an ObjectBase
    /// anyway (ImageComposer.cs:253 takes the viewer path regardless; see
    /// the note on M59Compose.Face).
    ///
    /// The dyeing is not done here and must not be: GuildShieldInfo folds
    /// the two colour bytes into one palette index in its own setters
    /// (`GuildShieldInfo.cs:194`, :215) via
    /// `ColorTransformation.GetGuildShieldColor`, and RenderInfo picks
    /// that up as the main frame's palette (RenderInfo.cs:275, :306). So
    /// setting Color1 is the whole of "recolour the shield".
    ///
    /// One thing has to be done that the reference does not do, and it is
    /// a consequence of nobody ticking this object. The example model is a
    /// bare ObjectBase, and that constructor leaves Animation null
    /// (ObjectBase.cs:589-596 - only Clear fills one in, :725).
    /// UpdateFrameIndices refuses to choose a frame without an animation
    /// (:1185-1198), and it only runs at all from ProcessAppearance, which
    /// runs from Tick (:1031-1048). In the game the composer is driven by
    /// the model's AppearanceChanged event and the object rides the
    /// client's tick; here the panel owns the only reference to it. So the
    /// animation is filled in if it is missing and the object is ticked
    /// once, which is what settles ViewerFrame - the same one-tick nudge
    /// UiShot uses for its made-up objects (UiShot.cs:91, :218).
    /// </summary>
    void Build(ObjectBase model)
    {
        _image.Texture = null;
        _note.Text = "";

        if (model == null || _shield?.Shields == null || _shield.Shields.Length == 0)
        {
            _note.Text = "Waiting for the shield designs.";
            return;
        }

        try
        {
            if (model.Animation == null) model.Animation = new AnimationNone();
            model.Tick(0, 1);

            if (model.Resource == null)
            {
                _note.Text = "This design's art is not available.";
                return;
            }

            _image.Texture = M59Assets.FromTex(M59Compose.Icon(model, PreviewSize));
            if (_image.Texture == null) _note.Text = "This design cannot be drawn here.";
        }
        catch (Exception e)
        {
            // A CRUSH-compressed frame cannot be decoded off Windows and
            // throws rather than returning nothing (see M59Compose.Blit).
            // That costs the preview, not the panel: the colours and the
            // design number are still true, and the claim still works.
            GD.PrintErr($"[GuildShieldPanel] preview: {e.Message}");
            _note.Text = "This design cannot be drawn here.";
        }
    }
}
