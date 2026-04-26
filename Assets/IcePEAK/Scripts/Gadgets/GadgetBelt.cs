using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Waist-height belt. Parent to the XR Origin (XR Rig) root, NOT Camera Offset.
    /// Position is static in the prefab; only yaw is updated each LateUpdate from the HMD.
    /// </summary>
    public class GadgetBelt : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Main Camera (HMD) transform — belt yaw tracks this.")]
        [SerializeField] private Transform hmd;

        [Tooltip("All belt slots, in the order you want them iterated.")]
        [SerializeField] private BeltSlot[] slots;

        [Header("Tunables")]
        [Tooltip("Max hand→slot distance that counts as 'hovered'. Meters.")]
        [SerializeField] private float proximityRadius = 0.10f;

        [Tooltip("Sticky bias for the currently-hovered slot. A competing slot must be at " +
                 "least this much closer (in meters) than the current one to take over. " +
                 "Kills boundary jitter without making the hand feel laggy.")]
        [SerializeField] private float stickyBias = 0.025f;

        [Tooltip("Belt sits this many meters below the HMD. Belt Y tracks HMD Y minus this " +
                 "value each LateUpdate, so the belt is at the player's waist whether they " +
                 "stand or sit. Compromise default ~0.65 (HMD→waist is ~0.70 standing, ~0.55 seated).")]
        [SerializeField] private float waistOffsetBelowHMD = 0.65f;

        public BeltSlot[] Slots => slots;
        public float ProximityRadius => proximityRadius;
        public float StickyBias => stickyBias;

        private void LateUpdate()
        {
            if (hmd == null) return;
            // Derive yaw from a flattened forward vector, not eulerAngles.y — the latter
            // jitters/flips near pitch = ±90° (gimbal lock), which is common in ice climbing
            // where the player looks straight up at a route or straight down at their feet.
            Vector3 fwd = hmd.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                // Pure vertical gaze — fall back to the HMD's up vector (top of head points
                // opposite of view direction when looking straight up/down).
                fwd = hmd.up;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) return;
            }
            transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            // Position tracks HMD: XZ follow the HMD so the belt stays a consistent forward
            // distance from the player's face when they physically lean or step around the
            // playspace; Y is offset below HMD so the belt sits at the waist regardless of
            // standing/seated/crouched. Slots' local +z offset is preserved through the yaw
            // rotation set above, so they stay in front of the gaze direction.
            transform.position = new Vector3(
                hmd.position.x,
                hmd.position.y - waistOffsetBelowHMD,
                hmd.position.z);
        }

        /// <summary>
        /// Nearest slot to <paramref name="handWorldPos"/> within proximityRadius.
        /// Deterministic: returns the single closest slot, or null if none in range.
        /// </summary>
        public bool TryGetNearestSlot(Vector3 handWorldPos, out BeltSlot nearest)
        {
            nearest = null;
            if (slots == null) return false;

            float bestSqr = proximityRadius * proximityRadius;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                float sqr = (s.Anchor.position - handWorldPos).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = s;
                }
            }
            return nearest != null;
        }
    }
}
