using System.Collections;
using UnityEngine;

namespace IcePEAK.Player
{
    /// <summary>
    /// Spawns the player at spawn point on Start, and respawns them there with
    /// a black-screen blink whenever they are falling midair (no pick embedded
    /// and no ground beneath the rig). The instant-black teleport hides the
    /// translation from the player's view to avoid VR motion sickness.
    /// </summary>
    public class FallHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [Tooltip("HMD (main) camera. Used so the player's head lands on the spawn point regardless of in-room offset.")]
        [SerializeField] private Transform xrCamera;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private ScreenFader fader;
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;

        [Header("Ground Detection")]
        [SerializeField] private LayerMask groundLayer;
        [Tooltip("Raycast distance below the XR Origin to detect ground.")]
        [SerializeField] private float groundCheckDistance = 0.3f;

        [Header("Fall")]
        [Tooltip("Seconds airborne (no ground, no pick embedded) before a respawn triggers. Debounces brief transitions between holds.")]
        [SerializeField] private float airborneGraceSeconds = 0.25f;
        [Tooltip("Seconds to hold the black screen after teleport before fading back in.")]
        [SerializeField] private float blackoutHoldSeconds = 0.15f;
        [Tooltip("Fade-in duration after respawn.")]
        [SerializeField] private float fadeInSeconds = 0.5f;
        [Tooltip("If true, fade in from black on game start. Otherwise just teleport silently.")]
        [SerializeField] private bool fadeInOnStart = true;

        private float _airborneTime;
        private bool _respawning;

        private void Start()
        {
            if (fadeInOnStart && fader != null)
            {
                fader.SetBlackInstant();
                TeleportToSpawn();
                fader.FadeFromBlack(fadeInSeconds);
            }
            else
            {
                TeleportToSpawn();
            }
        }

        private void Update()
        {
            if (_respawning) return;

            bool anyEmbedded = (leftPick != null && leftPick.IsEmbedded)
                            || (rightPick != null && rightPick.IsEmbedded);
            bool grounded = IsOnGround();

            if (anyEmbedded || grounded)
            {
                _airborneTime = 0f;
                return;
            }

            _airborneTime += Time.deltaTime;
            if (_airborneTime >= airborneGraceSeconds)
                StartCoroutine(RespawnRoutine());
        }

        private bool IsOnGround()
        {
            if (xrOrigin == null) return true;
            // Lift the ray start so it doesn't begin inside the ground when the
            // rig is standing exactly on floor level (XR Origin sits at foot Y).
            const float startUp = 0.1f;
            return Physics.Raycast(xrOrigin.position + Vector3.up * startUp,
                                   Vector3.down,
                                   groundCheckDistance + startUp,
                                   groundLayer);
        }

        /// <summary>
        /// Public entry point so other systems (death triggers, menus) can
        /// force a respawn with the same black-screen blink.
        /// </summary>
        public void Respawn() => StartCoroutine(RespawnRoutine());

        private IEnumerator RespawnRoutine()
        {
            if (_respawning) yield break;
            _respawning = true;

            if (fader != null) fader.SetBlackInstant();

            if (leftPick != null && leftPick.IsEmbedded) leftPick.Release();
            if (rightPick != null && rightPick.IsEmbedded) rightPick.Release();

            TeleportToSpawn();

            if (blackoutHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(blackoutHoldSeconds);

            if (fader != null) fader.FadeFromBlack(fadeInSeconds);

            _airborneTime = 0f;
            _respawning = false;
        }

        private void TeleportToSpawn()
        {
            if (xrOrigin == null || spawnPoint == null) return;

            // Offset the rig so the HMD ends up at the spawn point, regardless
            // of where the player is standing in their physical playspace.
            // Vertical is set directly so feet land on the spawn's floor height.
            if (xrCamera != null)
            {
                Vector3 camOffset = xrCamera.position - xrOrigin.position;
                camOffset.y = 0f;
                Vector3 target = spawnPoint.position - camOffset;
                target.y = spawnPoint.position.y;
                xrOrigin.position = target;
            }
            else
            {
                xrOrigin.position = spawnPoint.position;
            }
        }
    }
}
