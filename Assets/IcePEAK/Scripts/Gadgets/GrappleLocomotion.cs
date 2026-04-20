using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Rig-level zipline locomotion. Lives on the XR Origin alongside
    /// <c>ClimbingLocomotion</c>. Moves the XR Origin from its current
    /// position to a surface anchor over a fixed duration, offsetting along
    /// the surface normal so the player doesn't clip the hit geometry.
    ///
    /// While the zip is running:
    ///   - Default locomotion providers (move, turn, teleport) are disabled.
    ///   - Both ice picks are released and stowed so they can't interact.
    ///   - Additional <see cref="StartZip"/> calls are rejected.
    /// </summary>
    public class GrappleLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;

        [Header("Default Locomotion Providers")]
        [Tooltip("Components to disable while zipping — move, turn, teleport, etc. Re-enabled on arrival.")]
        [SerializeField] private MonoBehaviour[] locomotionProviders;

        [Header("Tunables")]
        [Tooltip("Seconds to travel from fire to arrival. Longer = more time for the off-hand to swing an ice pick into the arriving surface.")]
        [SerializeField] private float zipDuration = 1.0f;
        [Tooltip("Meters to stop the gun's nozzle short of the surface along its normal. Keep small so the off-hand can reach the wall with an ice pick.")]
        [SerializeField] private float surfaceOffset = 0.1f;
        [SerializeField] private AnimationCurve zipEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsZipping => _isZipping;

        private bool _isZipping;

        /// <summary>
        /// Begin a zip. The rig is translated so that <paramref name="pullPoint"/>
        /// (typically the gun's barrel tip at fire time) ends up at
        /// <c>anchor + normal * surfaceOffset</c>. This means the body travels
        /// along the rope line rather than from its feet to the hit point.
        /// Returns <c>false</c> if a zip is already running — callers should
        /// not start any rope visuals in that case.
        /// </summary>
        public bool StartZip(Vector3 anchor, Vector3 normal, Vector3 pullPoint, System.Action onArrival)
        {
            if (_isZipping) return false;
            if (xrOrigin == null) return false;

            StartCoroutine(ZipRoutine(anchor, normal, pullPoint, onArrival));
            return true;
        }

        private IEnumerator ZipRoutine(Vector3 anchor, Vector3 normal, Vector3 pullPoint, System.Action onArrival)
        {
            _isZipping = true;

            SetLocomotionProviders(false);
            // Detach any currently-embedded picks so the player doesn't drag
            // through the old anchor. Picks stay live (not stowed) during the
            // zip so the off-hand can swing one into the arriving surface.
            if (leftPick != null) leftPick.Release();
            if (rightPick != null) rightPick.Release();

            Vector3 start = xrOrigin.position;
            Vector3 nozzleLanding = anchor + normal.normalized * surfaceOffset;
            // Translate xrOrigin by the delta that brings pullPoint to nozzleLanding.
            Vector3 end = start + (nozzleLanding - pullPoint);
            float elapsed = 0f;

            while (elapsed < zipDuration)
            {
                float t = elapsed / zipDuration;
                float eased = zipEase.Evaluate(t);
                xrOrigin.position = Vector3.LerpUnclamped(start, end, eased);
                elapsed += Time.deltaTime;
                yield return null;
            }
            xrOrigin.position = end;

            SetLocomotionProviders(true);

            _isZipping = false;

            onArrival?.Invoke();
        }

        private void SetLocomotionProviders(bool enabled)
        {
            if (locomotionProviders == null) return;
            foreach (var p in locomotionProviders)
            {
                if (p != null) p.enabled = enabled;
            }
        }
    }
}
