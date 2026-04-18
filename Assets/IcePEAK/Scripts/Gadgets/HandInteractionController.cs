using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Per-hand belt interaction controller. Task 9 fleshes out proximity + trigger logic.
    /// This stub exists so the debug visualizer can read CurrentHoveredSlot.
    /// </summary>
    public class HandInteractionController : MonoBehaviour
    {
        public BeltSlot CurrentHoveredSlot { get; protected set; }
    }
}
