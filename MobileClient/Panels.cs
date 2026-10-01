using Godot;

/// <summary>
/// Shared behaviour for the full-screen panels.
///
/// There is only one rule here so far, and it is one the game states
/// plainly: a window that becomes visible is brought to the front
/// (`UIBuy.cpp:66`, `UITrade.cpp:80`, `UIObjectContents.cpp:61` all call
/// <c>moveToFront</c>).
///
/// On a desktop that is a nicety, because the windows are small and you
/// can see the one behind. Here it is the difference between working and
/// not. Every panel is a child of the same layer and covers most of the
/// screen, and Godot draws siblings in tree order - which is the order
/// GameView happened to build them in. So opening the spell book while
/// the bag was open put the book *behind* the bag: the button worked,
/// the panel was visible, and nothing appeared to happen. Several panels
/// in a row could pile up unseen.
/// </summary>
public static class Panels
{
    /// <summary>
    /// The buttons along the bottom edge that open the panels.
    ///
    /// Each one belongs to the panel it opens, which is right - a panel
    /// that hides itself should take its own button with it. What no
    /// single panel could do is hide the OTHERS: the row stayed up under
    /// a full-screen panel, and across the chat box while you were
    /// typing in it, where it shares a line with the entry. Registering
    /// them here lets the view hide the row as a row.
    /// </summary>
    /// <summary>
    /// The side margin a panel should lay itself out inside.
    ///
    /// Portrait returns what every panel used to compute for itself -
    /// a share of the width, never under sixteen. Landscape adds
    /// enough margin to keep the content in a centred band: a phone
    /// held sideways is two thousand pixels across, and a row with a
    /// name at one end and a number at the other, stretched over that,
    /// is a row nobody can read in one glance. The frame stays wider
    /// than the band, which reads as padding rather than as a gap.
    ///
    /// The band is the taller of 900 and 1.45 screen-heights, so it
    /// grows with the screen instead of being a number that looks
    /// right on one device.
    /// </summary>
    public static float Side(Vector2 v, float fraction, float least = 16f)
    {
        float side = Mathf.Max(least, v.X * fraction);
        float usable = v.X - side * 2f;
        float band = Mathf.Min(usable, Mathf.Max(900f, v.Y * 1.45f));
        return side + Mathf.Max(0f, (usable - band) * 0.5f);
    }

    static readonly System.Collections.Generic.List<Button> Openers =
        new System.Collections.Generic.List<Button>();

    /// <summary>Called by a panel once, for the button that opens it.</summary>
    public static void Opener(Button b)
    {
        if (b != null && !Openers.Contains(b)) Openers.Add(b);
    }

    /// <summary>
    /// Shows or hides the whole row. Safe to call every frame: a panel
    /// that is open makes the caller pass false anyway, so this never
    /// fights a panel over its own button.
    /// </summary>
    public static void ShowOpeners(bool on)
    {
        for (int i = Openers.Count - 1; i >= 0; i--)
        {
            Button b = Openers[i];
            if (!GodotObject.IsInstanceValid(b)) { Openers.RemoveAt(i); continue; }
            b.Visible = on;
        }
    }

    /// <summary>Puts this panel above its siblings. Called when it opens.</summary>
    public static void ToFront(Control panel)
    {
        Node parent = panel?.GetParent();
        if (parent == null) return;
        // -1 is last child, which is drawn last, which is on top.
        if (parent.GetChildCount() > 0) parent.MoveChild(panel, -1);
    }
}
