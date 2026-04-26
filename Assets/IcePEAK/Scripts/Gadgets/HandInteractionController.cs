using UnityEngine;
using UnityEngine.InputSystem;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Per-hand belt interaction controller. Priority order each frame:
    ///   1. Pick embedded → climbing; skip belt/activate entirely.
    ///   2. Grip rising-edge + hand over a slot → swap/stow/draw.
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
        [Tooltip("Grip (XRI Select Value) — used to swap/stow/draw items at a belt slot.")]
        [SerializeField] private InputActionReference gripAction;
        [Tooltip("Trigger (XRI Activate Value) — used to Activate() the item held in this hand.")]
        [SerializeField] private InputActionReference triggerAction;

        public BeltSlot CurrentHoveredSlot { get; private set; }

        private void OnEnable()
        {
            if (gripAction != null && gripAction.action != null)
                gripAction.action.Enable();
            if (triggerAction != null && triggerAction.action != null)
                triggerAction.action.Enable();
        }

        private void Update()
        {
            if (handCell == null || belt == null) return;

            belt.TryGetNearestSlot(handCell.Anchor.position, out var rawNearest);

            // Hysteresis: keep the current hovered slot until a competitor is meaningfully
            // closer. Filters the slot decision only — the hand's tracked position itself
            // is never smoothed, so the hand stays 1:1 with the controller.
            BeltSlot effectiveNearest = rawNearest;
            if (CurrentHoveredSlot != null
                && rawNearest != null
                && rawNearest != CurrentHoveredSlot)
            {
                Vector3 handPos = handCell.Anchor.position;
                float currentDist    = Vector3.Distance(handPos, CurrentHoveredSlot.Anchor.position);
                float competitorDist = Vector3.Distance(handPos, rawNearest.Anchor.position);

                // Once the hand has clearly left the current slot's zone, drop the bias.
                if (currentDist <= belt.ProximityRadius
                    && competitorDist > currentDist - belt.StickyBias)
                {
                    effectiveNearest = CurrentHoveredSlot;
                }
            }

            if (effectiveNearest != CurrentHoveredSlot)
            {
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false, handCell);
                CurrentHoveredSlot = effectiveNearest;
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true, handCell);
            }

            // P1: pick embedded → climbing; ignore belt/activate for this hand.
            if (pick != null && pick.IsEmbedded) return;

            // P2: grip press + hovered slot → swap/stow/draw.
            if (CurrentHoveredSlot != null &&
                gripAction != null && gripAction.action != null &&
                gripAction.action.WasPressedThisFrame())
            {
                ResolveBeltAction(CurrentHoveredSlot);
                return;
            }

            // P3: trigger press + held item implements IActivatable → Activate().
            if (triggerAction != null && triggerAction.action != null &&
                triggerAction.action.WasPressedThisFrame() &&
                handCell.HeldItem != null &&
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

            // Slot-locked gadgets (e.g. the drone) opt out of swap/draw/stow.
            // Grip-press here is left unclaimed so DroneController can use it.
            if (slotItem != null && slotItem.GetComponent<IFixedInSlot>() != null)
                return;

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
