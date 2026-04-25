using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Slot-locked overview gadget. Lives permanently in one <see cref="BeltSlot"/>
    /// (carries <see cref="IFixedInSlot"/> so the belt's swap/draw/stow path
    /// declines it). All peek behavior — view snap, locomotion suspension —
    /// is owned by the rig-level <c>DroneController</c>; this component is
    /// just a marker plus a display name.
    /// </summary>
    public class Drone : MonoBehaviour, IHoldable, IFixedInSlot
    {
        [Header("Hint")]
        [SerializeField] private string displayName = "Drone";
        [SerializeField] private string hintText = "Hold grip to scout";

        public string DisplayName => displayName;
        public string HintText => hintText;

        public void OnTransfer(CellKind from, CellKind to)
        {
            // Never moves — this is informational only.
            Debug.Log($"[Drone] OnTransfer {from} -> {to} (unexpected; drone is slot-locked)");
        }
    }
}
