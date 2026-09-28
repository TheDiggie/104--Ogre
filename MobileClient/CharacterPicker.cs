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

    VBoxContainer _rows;
    Label _title;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;

        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.82f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

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
        float pad = Mathf.Max(24f, v.X * 0.08f);
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

        Visible = true;
        Layout();
    }
}
