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
    /// <summary>Puts this panel above its siblings. Called when it opens.</summary>
    public static void ToFront(Control panel)
    {
        Node parent = panel?.GetParent();
        if (parent == null) return;
        // -1 is last child, which is drawn last, which is on top.
        if (parent.GetChildCount() > 0) parent.MoveChild(panel, -1);
    }
}
