using UnityEngine;
using UnityEngine.InputSystem;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Per-hand belt interaction controller. Priority order each frame:
    ///   1. Pick embedded → pick owns trigger (we early-return, pick handles release).
    ///   2. Trigger rising-edge + hand over a slot → swap/stow/draw.
    ///   3. Trigger rising-edge + held item implements IActivatable → Activate().
    ///   4. Otherwise no-op.
    /// </summary>
    public class HandInteractionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandCell handCell;
        [SerializeField] private GadgetBelt belt;
        [Tooltip("This hand's pick, for the IsEmbedded priority check. Leave null if this hand never holds a pick.")]
        [SerializeField] private IcePickController pick;

        [Header("Input")]
        [Tooltip("Same XRI Activate Value action the pick uses for release.")]
        [SerializeField] private InputActionReference triggerAction;

        public BeltSlot CurrentHoveredSlot { get; private set; }

        private void OnEnable()
        {
            if (triggerAction != null && triggerAction.action != null)
                triggerAction.action.Enable();
        }

        private void Update()
        {
            if (handCell == null || belt == null) return;

            belt.TryGetNearestSlot(handCell.Anchor.position, out var nearest);
            if (nearest != CurrentHoveredSlot)
            {
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false, handCell);
                CurrentHoveredSlot = nearest;
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true, handCell);
            }

            // P1: pick embedded → pick's own Update handles trigger-to-release.
            if (pick != null && pick.IsEmbedded) return;

            if (triggerAction == null || triggerAction.action == null) return;
            if (!triggerAction.action.WasPressedThisFrame()) return;

            // P2: hovered slot → swap/stow/draw.
            if (CurrentHoveredSlot != null)
            {
                ResolveBeltAction(CurrentHoveredSlot);
                return;
            }

            // P3: held item implements IActivatable → Activate().
            if (handCell.HeldItem != null &&
                handCell.HeldItem.TryGetComponent<IActivatable>(out var activatable))
            {
                Debug.Log($"[{name}] Activate -> {handCell.HeldItem.name}");
                activatable.Activate();
            }
            // P4: otherwise no-op.
        }

        private void OnDisable()
        {
            if (CurrentHoveredSlot != null)
            {
                CurrentHoveredSlot.SetHighlighted(false);
                CurrentHoveredSlot = null;
            }
        }

        /// <summary>
        /// Unified swap/stow/draw. Snapshot both cells, empty both, re-place into swapped cells.
        /// Draw = handItem null. Stow = slotItem null. Swap = both non-null. No-op = both null.
        /// </summary>
        private void ResolveBeltAction(BeltSlot slot)
        {
            var handItem = handCell.HeldItem;
            var slotItem = slot.HeldItem;

            if (handItem == null && slotItem == null) return;

            handCell.Take();
            slot.Take();

            if (slotItem != null) PlaceInto(handCell, slotItem, CellKind.BeltSlot);
            if (handItem != null) PlaceInto(slot,     handItem, CellKind.Hand);

            // Held item vs placeholder may have changed — re-evaluate highlight target.
            slot.SetHighlighted(true, handCell);
        }

        private static void PlaceInto(ICell cell, GameObject item, CellKind from)
        {
            item.transform.SetParent(cell.Anchor, worldPositionStays: false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            cell.Place(item);
            var holdable = item.GetComponent<IHoldable>();
            holdable?.OnTransfer(from, cell.Kind);
        }
    }
}
