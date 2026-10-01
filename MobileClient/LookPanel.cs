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

    ColorRect _panel;
    TextureRect _picture;
    Label _name;
    RichTextLabel _description;
    Label _inscription;
    Label _detail;
    Button _close;

    uint _shown;
    string _lastText = "";

    public bool IsOpen => _panel != null && _panel.Visible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _panel = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.95f), Visible = false };
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
        _write.Pressed += () =>
        {
            if (_shown != 0) Inscribe?.Invoke(_shown, _writing.Text ?? "");
        };
        AddChild(_write);

        _close = new Button { Text = "Close", Visible = false };
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
        float h = Mathf.Min(v.Y * 0.55f, 520f);
        float top = v.Y * 0.18f;

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

        _close.Position = new Vector2(side, top + h - FontSize * 2.4f - 8f);
        _close.Size = new Vector2(w - side, FontSize * 2.4f);
    }

    public void Close()
    {
        Show(false);
        // The server's own flag, so the client and the view agree on
        // whether the window is up.
        if (_info != null) _info.IsVisible = false;
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

        _panel.Visible = on;
        _picture.Visible = on && _picture.Texture != null;
        _name.Visible = on;
        _description.Visible = on;
        _inscription.Visible = on && !_editable && !string.IsNullOrWhiteSpace(_inscription.Text);
        _writing.Visible = on && _editable;
        _write.Visible = on && _editable;
        _detail.Visible = on && !string.IsNullOrWhiteSpace(_detail.Text);
        _close.Visible = on;
    }

    ObjectInfo _info;

    /// <summary>
    /// A new inscription for the object being looked at: its id and the
    /// text. Raised by the Write button.
    /// </summary>
    public event System.Action<uint, string> Inscribe;

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
        // A spell or a skill description arrives in its own place -
        // LookSpell and LookSkill, each with its own IsVisible - and the
        // game gives each its own window (UISpellDetails.cpp,
        // UISkillDetails.cpp). They are the same window with different
        // lines on it, so this one does all three, and the extra lines
        // come from the same places the game reads them.
        if (Spell(data) || Skill(data)) return;

        _info = data?.LookObject;
        if (_info == null) { if (IsOpen) Show(false); return; }

        if (!_info.IsVisible) { if (IsOpen) Show(false); return; }

        ObjectBase o = _info.ObjectBase;
        string text = _info.Message?.FullString ?? "";
        string ins = _info.Inscription?.FullString ?? "";
        _detail.Text = "";

        uint id = o?.ID ?? 0;
        if (id != _shown || text != _lastText)
        {
            _shown = id;
            _lastText = text;

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

            try
            {
                // The viewer's frame, which is what this window uses and
                // the inventory does not.
                _picture.Texture = M59Assets.FromTex(Viewer(o, PictureSize));
            }
            catch (Exception e) { GD.PrintErr($"[Look] {o?.Name}: {e.Message}"); }
        }

        if (!IsOpen) Show(true);
    }

    /// <summary>
    /// A spell description. `UISpellDetails.cpp` shows the name, the
    /// school, the level, and what it costs in mana and vigor - each a
    /// `ServerString` the server sends already worded, so none of it is
    /// composed here.
    /// </summary>
    bool Spell(DataController data)
    {
        SpellInfo info = data?.LookSpell;
        if (info == null || !info.IsVisible) return false;

        ObjectBase o = info.ObjectBase;
        string text = info.Message?.FullString ?? "";
        uint id = o?.ID ?? 0;

        if (id != _shown || text != _lastText)
        {
            _shown = id; _lastText = text;
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

        if (!IsOpen) Show(true);
        return true;
    }

    /// <summary>
    /// A skill description - the same window with two lines instead of
    /// four, which is all `UISkillDetails.cpp` has.
    /// </summary>
    bool Skill(DataController data)
    {
        SkillInfo info = data?.LookSkill;
        if (info == null || !info.IsVisible) return false;

        ObjectBase o = info.ObjectBase;
        string text = info.Message?.FullString ?? "";
        uint id = o?.ID ?? 0;

        if (id != _shown || text != _lastText)
        {
            _shown = id; _lastText = text;
            _name.Text = o?.Name ?? "";
            Tint(o);
            _description.Text = Safe(text);
            _inscription.Text = "";
            _detail.Text = Join(info.SchoolName?.FullString, info.SkillLevel?.FullString);
            Picture(o);
        }

        if (!IsOpen) Show(true);
        return true;
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
