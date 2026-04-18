using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Per-hand belt interaction controller. This phase: hover tracking only.
    /// Task 10 adds trigger input and swap/stow/draw.
    /// </summary>
    public class HandInteractionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandCell handCell;
        [SerializeField] private GadgetBelt belt;

        public BeltSlot CurrentHoveredSlot { get; private set; }

        private void Update()
        {
            if (handCell == null || belt == null) return;

            belt.TryGetNearestSlot(handCell.Anchor.position, out var nearest);

            if (nearest == CurrentHoveredSlot) return;

            if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false);
            CurrentHoveredSlot = nearest;
            if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true);
        }

        private void OnDisable()
        {
            if (CurrentHoveredSlot != null)
            {
                CurrentHoveredSlot.SetHighlighted(false);
                CurrentHoveredSlot = null;
            }
        }
    }
}
