using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Shared helpers for moving items between cells (hands and belt slots).
    /// Used by HandInteractionController for grip-driven swap/draw/stow and by
    /// GrappleGun for auto-swap on grapple arrival.
    /// </summary>
    public static class BeltSwap
    {
        /// <summary>
        /// Snapshots both cells, empties them, and re-places each item into the
        /// other cell. Either cell may be empty (draw or stow). No-op if both are
        /// empty. Caller is responsible for IFixedInSlot guards.
        /// </summary>
        public static void Swap(ICell a, ICell b)
        {
            if (a == null || b == null) return;
            var aItem = a.HeldItem;
            var bItem = b.HeldItem;
            if (aItem == null && bItem == null) return;

            a.Take();
            b.Take();

            if (bItem != null) PlaceInto(a, bItem, b.Kind);
            if (aItem != null) PlaceInto(b, aItem, a.Kind);
        }

        /// <summary>
        /// Reparents <paramref name="item"/> into <paramref name="cell"/>, resets
        /// its local pose, registers it via Place(), and fires
        /// IHoldable.OnTransfer(from → cell.Kind) on the item if applicable.
        /// </summary>
        public static void PlaceInto(ICell cell, GameObject item, CellKind from)
        {
            if (cell == null || item == null) return;
            item.transform.SetParent(cell.Anchor, worldPositionStays: false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            cell.Place(item);
            var holdable = item.GetComponent<IHoldable>();
            holdable?.OnTransfer(from, cell.Kind);
        }

        /// <summary>
        /// Walks parent chain from <paramref name="descendant"/> up looking for a
        /// HandCell. Returns null if none found.
        /// </summary>
        public static HandCell FindOwningHand(Transform descendant)
        {
            for (var t = descendant; t != null; t = t.parent)
            {
                if (t.TryGetComponent<HandCell>(out var hand))
                    return hand;
            }
            return null;
        }

        /// <summary>
        /// Returns the first slot in <paramref name="belt"/> whose held item has
        /// an IcePickController component, or null if none.
        /// </summary>
        public static BeltSlot FindFirstSlotWithIcePick(GadgetBelt belt)
        {
            if (belt == null || belt.Slots == null) return null;
            foreach (var slot in belt.Slots)
            {
                if (slot == null) continue;
                var held = slot.HeldItem;
                if (held == null) continue;
                if (held.GetComponent<IcePickController>() == null) continue;
                return slot;
            }
            return null;
        }
    }
}
