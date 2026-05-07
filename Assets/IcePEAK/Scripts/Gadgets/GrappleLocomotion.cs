using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Rig-level zipline locomotion. Lives on the XR Origin alongside
    /// <c>ClimbingLocomotion</c>. Owns the full zip lifecycle:
    ///   1. Travel phase — moves the rig at constant speed toward a surface
    ///      anchor. Caps at <see cref="maxTravelDuration"/>.
    ///   2. Arrival phase — once travel succeeds, fires <c>onArrived</c>
    ///      (gun does its auto-swap), then anchors the rig via
    ///      <c>ClimbingLocomotion.SetGrappleAnchor</c> for a fixed
    ///      <see cref="wallHangDuration"/>.
    ///   3. Cleanup — re-enables locomotion providers, fires <c>onComplete</c>.
    ///
    /// While the zip is running:
    ///   - Default locomotion providers (move, turn, teleport) are disabled.
    ///   - Both ice picks are released so they can't drag through the old anchor.
    ///   - Additional <see cref="StartZip"/> calls are rejected.
    ///
    /// The arrival branch is skipped on cancel, on travel timeout, and on
    /// early-embed (a pick that swung into the arriving surface) — the player
    /// is already anchored via the pick in that last case.
    /// </summary>
    public class GrappleLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;
        [Tooltip("ClimbingLocomotion on XR Origin — used to anchor the rig during the wall-hang.")]
        [SerializeField] private ClimbingLocomotion climbingLocomotion;

        [Header("Default Locomotion Providers")]
        [Tooltip("Components to disable while zipping — move, turn, teleport, etc. Re-enabled on completion.")]
        [SerializeField] private MonoBehaviour[] locomotionProviders;

        [Header("Tunables")]
        [Tooltip("Constant travel speed during the zip (m/s).")]
        [SerializeField] private float zipSpeed = 20f;
        [Tooltip("Hard cap on travel-phase duration. If the rig hasn't arrived in this many seconds, the zip ends without a wall-hang.")]
        [SerializeField] private float maxTravelDuration = 2.0f;
        [Tooltip("Fixed seconds the rig hangs at the wall after a successful arrival, independent of how long travel took.")]
        [SerializeField] private float wallHangDuration = 2.0f;
        [Tooltip("Meters to stop the gun's nozzle short of the surface along the rope line.")]
        [SerializeField] private float surfaceOffset = 0.1f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the rope distance that must be traveled before an ice pick embed is allowed to end the zip early.")]
        [SerializeField] private float embedArmFraction = 0.8f;

        public bool IsZipping => _isZipping;

        private bool _isZipping;
        private bool _cancelRequested;

        public void CancelZip() => _cancelRequested = true;

        /// <summary>
        /// Begin a zip. The rig is translated so that <paramref name="pullPoint"/>
        /// (typically the gun's barrel tip at fire time) ends up <c>surfaceOffset</c>
        /// meters short of <paramref name="anchor"/> along the rope line.
        /// </summary>
        /// <param name="anchor">World-space target point.</param>
        /// <param name="pullPoint">Gun-tip position snapshot used for offset math.</param>
        /// <param name="climbAnchor">Transform passed to ClimbingLocomotion during the wall-hang. Must be parented to the rig so its delta cancels rig motion (matches existing climbing-anchor semantics).</param>
        /// <param name="onArrived">Fires once at the start of the arrival phase, before the wall-hang begins. Skipped on cancel/timeout/early-embed.</param>
        /// <param name="onComplete">Fires once at the end of the zip, regardless of how it ended.</param>
        /// <returns><c>false</c> if a zip is already running — callers should not start any rope visuals in that case.</returns>
        public bool StartZip(Vector3 anchor, Vector3 pullPoint, Transform climbAnchor,
                             System.Action onArrived, System.Action onComplete)
        {
            if (_isZipping) return false;
            if (xrOrigin == null) return false;

            StartCoroutine(ZipRoutine(anchor, pullPoint, climbAnchor, onArrived, onComplete));
            return true;
        }

        private IEnumerator ZipRoutine(Vector3 anchor, Vector3 pullPoint, Transform climbAnchor,
                                       System.Action onArrived, System.Action onComplete)
        {
            _isZipping = true;
            _cancelRequested = false;

            SetLocomotionProviders(false);
            // Detach any currently-embedded picks so the player doesn't drag
            // through the old anchor. Picks stay live (not stowed) during the
            // zip so the off-hand can swing one into the arriving surface.
            if (leftPick != null) leftPick.Release();
            if (rightPick != null) rightPick.Release();

            Vector3 start = xrOrigin.position;
            // Offset back along the rope line so the nozzle lands a fixed
            // distance short of the surface along the aim axis, regardless of
            // surface orientation.
            Vector3 ropeDir = anchor - pullPoint;
            Vector3 nozzleLanding = ropeDir.sqrMagnitude > 1e-6f
                ? anchor - ropeDir.normalized * surfaceOffset
                : anchor;
            Vector3 end = start + (nozzleLanding - pullPoint);
            float totalDist = Vector3.Distance(start, end);

            // --- Travel phase ---
            float travelElapsed = 0f;
            bool arrived = false;
            bool earlyEmbed = false;
            while (travelElapsed < maxTravelDuration && !_cancelRequested)
            {
                float progress = totalDist > 1e-4f
                    ? Vector3.Distance(start, xrOrigin.position) / totalDist
                    : 1f;
                bool embedArmed = progress >= embedArmFraction;

                bool leftEmbedded = leftPick != null && leftPick.IsEmbedded;
                bool rightEmbedded = rightPick != null && rightPick.IsEmbedded;

                if (embedArmed && (leftEmbedded || rightEmbedded))
                {
                    // Off-hand swung into the arriving wall — skip the hang.
                    earlyEmbed = true;
                    break;
                }

                if (!embedArmed)
                {
                    // Premature embed during the lockout — release so the pick
                    // doesn't stay stuck in a wall we'll fly past.
                    if (leftEmbedded) leftPick.Release();
                    if (rightEmbedded) rightPick.Release();
                }

                // Constant-speed travel toward the landing.
                Vector3 toEnd = end - xrOrigin.position;
                float stepDist = zipSpeed * Time.deltaTime;
                if (toEnd.sqrMagnitude <= stepDist * stepDist)
                {
                    xrOrigin.position = end;
                    arrived = true;
                    break;
                }
                else
                {
                    xrOrigin.position += toEnd.normalized * stepDist;
                }

                travelElapsed += Time.deltaTime;
                yield return null;
            }

            // --- Arrival branch ---
            // Only run hang phase if travel succeeded — not on cancel, not on
            // early-embed (player's already anchored via pick), not on timeout.
            if (arrived && !_cancelRequested && !earlyEmbed)
            {
                onArrived?.Invoke();

                if (climbingLocomotion != null && climbAnchor != null)
                    climbingLocomotion.SetGrappleAnchor(climbAnchor);

                // Hang is a hard timer per spec — intentionally does not check _cancelRequested.
                float hangElapsed = 0f;
                while (hangElapsed < wallHangDuration)
                {
                    hangElapsed += Time.deltaTime;
                    yield return null;
                }

                if (climbingLocomotion != null)
                    climbingLocomotion.SetGrappleAnchor(null);
            }

            // --- Cleanup ---
            SetLocomotionProviders(true);
            _isZipping = false;
            onComplete?.Invoke();
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
