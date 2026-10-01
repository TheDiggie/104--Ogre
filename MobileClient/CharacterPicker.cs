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
        _rows.Position = new Vector2(pad, pad + FontSize * 3f);
        _rows.Size = new Vector2(v.X - pad * 2, v.Y - pad * 2 - FontSize * 3f);
    }

    public void Offer(IList<CharSelectItem> characters)
    {
        foreach (Node n in _rows.GetChildren()) n.QueueFree();

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

        Visible = true;
        Layout();
    }
}
