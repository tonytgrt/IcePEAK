using UnityEngine;
using UnityEngine.Events;

namespace IcePEAK.Player
{
    /// <summary>
    /// Place on an empty GameObject at the top of the cliff with a Trigger Collider.
    /// When the player enters, stops the ClimbTimer and fires onReached.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DestinationTrigger : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ClimbTimer to stop when the player arrives.")]
        [SerializeField] private ClimbTimer climbTimer;

        [Header("Detection")]
        [Tooltip("Layers that count as the player — match the layer on XR Origin.")]
        [SerializeField] private LayerMask playerLayer = ~0;

        [Header("Events")]
        [Tooltip("Fired once when the player reaches the destination. Hook celebration VFX/SFX here.")]
        [SerializeField] private UnityEvent onReached;

        private bool _reached;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        /// <summary>Re-arms the trigger so the destination can be reached again after a restart.</summary>
        public void ResetTrigger() => _reached = false;

        private void OnTriggerEnter(Collider other)
        {
            if (_reached) return;
            if ((playerLayer.value & (1 << other.gameObject.layer)) == 0) return;

            _reached = true;
            climbTimer?.Stop();
            onReached?.Invoke();
            Debug.Log("[DestinationTrigger] Player reached the destination!");
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.6f);
            var col = GetComponent<Collider>();
            if (col != null)
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}
