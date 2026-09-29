using System;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// One square of the bag, and the thing you can drag.
///
/// The game's inventory is a grid of CEGUI DragContainers: dragging one
/// onto another removes the item from the list and reinserts it at the
/// other's position, then tells the server with
/// `SendReqInventoryMoveMessage(from->getID(), to->getID())` - which,
/// since each slot's window id is set to the object's id, is a move
/// between two objects rather than two indices
/// (UIInventory.cpp, `Inventory::OnItemDropped`). Dropping on a slot
/// that holds nothing is ignored there, because the index falls outside
/// the data list, so it is ignored here too.
///
/// A tap is handled here rather than by a child Button: a Button under
/// the finger swallows the press, and then nothing is ever a drag.
/// </summary>
public partial class InventorySlot : Panel
{
    /// <summary>What this slot holds, or null for an empty one.</summary>
    public InventoryObject Item;

    /// <summary>A texture to drag around under the finger.</summary>
    public Texture2D Preview;

    /// <summary>Tapped. The panel decides whether that picks or uses.</summary>
    public Action<InventoryObject> Tapped;

    /// <summary>Dragged onto another slot: move From to where To is.</summary>
    public Action<InventoryObject, InventoryObject> Moved;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Item == null) return default;

        if (Preview != null)
        {
            var ghost = new TextureRect
            {
                Texture = Preview,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = Size * 0.8f,
                Size = Size * 0.8f,
                Modulate = new Color(1, 1, 1, 0.8f),
            };
            // Centred on the finger, so the thing being moved is not
            // hidden underneath it.
            var holder = new Control();
            holder.AddChild(ghost);
            ghost.Position = -ghost.Size * 0.5f;
            SetDragPreview(holder);
        }

        return this;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
        => Item != null && data.As<InventorySlot>() is InventorySlot from
           && from != this && from.Item != null;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.As<InventorySlot>() is InventorySlot from && from.Item != null && Item != null)
            Moved?.Invoke(from.Item, Item);
    }

    public override void _GuiInput(InputEvent @event)
    {
        // A press that never became a drag is a tap. Godot has already
        // taken the drag away by the time this arrives if one started,
        // so there is nothing to disambiguate.
        if (@event is InputEventMouseButton mb &&
            mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            if (Item != null) Tapped?.Invoke(Item);
            AcceptEvent();
        }
    }
}
