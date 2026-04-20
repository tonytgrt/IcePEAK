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
        [Tooltip("Constant travel speed during the zip (m/s). The rig moves toward the anchor at this speed regardless of rope length, so long and short grapples feel the same.")]
        [SerializeField] private float zipSpeed = 20f;
        [Tooltip("Total seconds the grappled state lasts. The rig travels to the landing at zipSpeed, then hangs there until this timer expires (or until an ice pick embeds).")]
        [SerializeField] private float zipDuration = 2.0f;
        [Tooltip("Meters to stop the gun's nozzle short of the surface along the rope line. Keep small so the off-hand can reach the wall with an ice pick.")]
        [SerializeField] private float surfaceOffset = 0.1f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the rope distance that must be traveled before an ice pick embed is allowed to end the zip early. Guards against a pick that was already swinging at fire time clipping a nearby wall and canceling the zip immediately.")]
        [SerializeField] private float embedArmFraction = 0.8f;

        public bool IsZipping => _isZipping;

        private bool _isZipping;

        /// <summary>
        /// Begin a zip. The rig is translated so that <paramref name="pullPoint"/>
        /// (typically the gun's barrel tip at fire time) ends up <c>surfaceOffset</c>
        /// meters short of <paramref name="anchor"/> along the rope line. This
        /// keeps the gun on the aim axis regardless of surface orientation, so
        /// an angled wall doesn't shove the player sideways.
        /// Returns <c>false</c> if a zip is already running — callers should
        /// not start any rope visuals in that case.
        /// </summary>
        public bool StartZip(Vector3 anchor, Vector3 pullPoint, System.Action onArrival)
        {
            if (_isZipping) return false;
            if (xrOrigin == null) return false;

            StartCoroutine(ZipRoutine(anchor, pullPoint, onArrival));
            return true;
        }

        private IEnumerator ZipRoutine(Vector3 anchor, Vector3 pullPoint, System.Action onArrival)
        {
            _isZipping = true;

            SetLocomotionProviders(false);
            // Detach any currently-embedded picks so the player doesn't drag
            // through the old anchor. Picks stay live (not stowed) during the
            // zip so the off-hand can swing one into the arriving surface.
            if (leftPick != null) leftPick.Release();
            if (rightPick != null) rightPick.Release();

            Vector3 start = xrOrigin.position;
            // Offset back along the rope line (anchor → pullPoint) so the nozzle
            // lands a fixed distance short of the surface along the aim axis,
            // regardless of surface orientation. This keeps angled walls from
            // pushing the landing point far off to the side.
            Vector3 ropeDir = anchor - pullPoint;
            Vector3 nozzleLanding = ropeDir.sqrMagnitude > 1e-6f
                ? anchor - ropeDir.normalized * surfaceOffset
                : anchor;
            // Translate xrOrigin by the delta that brings pullPoint to nozzleLanding.
            Vector3 end = start + (nozzleLanding - pullPoint);
            float totalDist = Vector3.Distance(start, end);
            float elapsed = 0f;

            while (elapsed < zipDuration)
            {
                // Arm pick-based early-out only after the rig has traveled far
                // enough. Without this, a pick that was already mid-swing at
                // fire time can clip a nearby wall and cancel the zip before
                // the player has gone anywhere.
                float progress = totalDist > 1e-4f
                    ? Vector3.Distance(start, xrOrigin.position) / totalDist
                    : 1f;
                bool embedArmed = progress >= embedArmFraction;

                bool leftEmbedded = leftPick != null && leftPick.IsEmbedded;
                bool rightEmbedded = rightPick != null && rightPick.IsEmbedded;

                if (embedArmed && (leftEmbedded || rightEmbedded))
                {
                    // End the zip the instant the off-hand swings a pick into
                    // the arriving surface — the player is now anchored, so
                    // handing control to ClimbingLocomotion mid-flight feels
                    // more responsive than waiting for the zip timer.
                    break;
                }

                if (!embedArmed)
                {
                    // Premature embed during the lockout — release so the pick
                    // doesn't stay stuck in a wall we'll fly past and then
                    // yank us backward once ClimbingLocomotion re-enables.
                    if (leftEmbedded) leftPick.Release();
                    if (rightEmbedded) rightPick.Release();
                }

                // Constant-speed travel toward the landing. Once we arrive, the
                // rig hangs at the anchor for the remaining duration so the
                // off-hand still has time to land a swing even on short ropes.
                Vector3 toEnd = end - xrOrigin.position;
                float stepDist = zipSpeed * Time.deltaTime;
                if (toEnd.sqrMagnitude <= stepDist * stepDist)
                {
                    xrOrigin.position = end;
                }
                else
                {
                    xrOrigin.position += toEnd.normalized * stepDist;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

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
