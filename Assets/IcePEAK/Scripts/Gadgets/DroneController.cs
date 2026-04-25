using UnityEngine;
using UnityEngine.InputSystem;
using IcePEAK.Player;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Rig-level peek controller for the slot-locked drone gadget. While the
    /// player's hand hovers the drone slot AND grip is held, the XR Origin is
    /// snapped to a designer-placed overview anchor; releasing grip (or moving
    /// the hand off the slot) restores the original rig pose.
    ///
    /// Grip-press at the drone slot is rejected by <see cref="HandInteractionController"/>
    /// (because the held item carries <see cref="IFixedInSlot"/>), so the press
    /// is unclaimed by belt logic and free for this controller to consume.
    /// </summary>
    public class DroneController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [Tooltip("Empty transform placed at the overview vantage point.")]
        [SerializeField] private Transform droneViewAnchor;
        [Tooltip("The fixed BeltSlot holding the drone.")]
        [SerializeField] private BeltSlot droneSlot;

        [Header("Hands")]
        [SerializeField] private HandInteractionController leftHand;
        [SerializeField] private HandInteractionController rightHand;
        [SerializeField] private InputActionReference leftGrip;
        [SerializeField] private InputActionReference rightGrip;

        [Header("Suspended during peek")]
        [Tooltip("Locomotion providers / climbing / grappling — anything that should not run while in drone view. All are flipped enabled=false on BeginPeek and back on EndPeek.")]
        [SerializeField] private MonoBehaviour[] suspendDuringPeek;

        [Header("Picks")]
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;

        [Header("Tunables")]
        [Range(0f, 1f)]
        [SerializeField] private float gripThreshold = 0.5f;

        [Header("Optional polish")]
        [Tooltip("Optional. If set, fades to black around the rig snap to ease motion sickness.")]
        [SerializeField] private ScreenFader fader;
        [SerializeField] private float peekFadeDuration = 0.1f;

        public bool IsPeeking => _isPeeking;

        private bool _isPeeking;
        private Vector3 _savedOriginPos;
        private Quaternion _savedOriginRot;
        private HandInteractionController _ownerHand;
        private InputActionReference _ownerGrip;

        private void OnEnable()
        {
            leftGrip?.action?.Enable();
            rightGrip?.action?.Enable();
        }

        private void Update()
        {
            if (_isPeeking)
            {
                bool stillHovering = _ownerHand != null && _ownerHand.CurrentHoveredSlot == droneSlot;
                bool stillGripping = (_ownerGrip?.action?.ReadValue<float>() ?? 0f) >= gripThreshold;
                if (!stillHovering || !stillGripping)
                    EndPeek();
                return;
            }

            // Try left then right. Only the first hand to fire its grip on a frame wins.
            if (TryStartPeek(leftHand, leftGrip)) return;
            TryStartPeek(rightHand, rightGrip);
        }

        private bool TryStartPeek(HandInteractionController hand, InputActionReference grip)
        {
            if (hand == null || grip?.action == null) return false;
            if (hand.CurrentHoveredSlot != droneSlot) return false;
            if (!grip.action.WasPressedThisFrame()) return false;
            if (leftPick != null && leftPick.IsEmbedded) return false;
            if (rightPick != null && rightPick.IsEmbedded) return false;

            BeginPeek(hand, grip);
            return true;
        }

        private void BeginPeek(HandInteractionController hand, InputActionReference grip)
        {
            if (xrOrigin == null || droneViewAnchor == null) return;

            _ownerHand = hand;
            _ownerGrip = grip;
            _isPeeking = true;

            _savedOriginPos = xrOrigin.position;
            _savedOriginRot = xrOrigin.rotation;

            // Release any embedded picks so they don't get stranded mid-air at
            // the climb position when the rig teleports to the overview.
            if (leftPick != null && leftPick.IsEmbedded) leftPick.Release();
            if (rightPick != null && rightPick.IsEmbedded) rightPick.Release();

            // Hide the snap behind a black flash if a fader is wired.
            if (fader != null) fader.SetBlackInstant();

            xrOrigin.position = droneViewAnchor.position;
            xrOrigin.rotation = droneViewAnchor.rotation;

            SetSuspended(true);

            if (fader != null && peekFadeDuration > 0f)
                fader.FadeFromBlack(peekFadeDuration);
        }

        private void EndPeek()
        {
            if (!_isPeeking) return;

            if (fader != null) fader.SetBlackInstant();

            xrOrigin.position = _savedOriginPos;
            xrOrigin.rotation = _savedOriginRot;

            SetSuspended(false);

            _isPeeking = false;
            _ownerHand = null;
            _ownerGrip = null;

            if (fader != null && peekFadeDuration > 0f)
                fader.FadeFromBlack(peekFadeDuration);
        }

        private void SetSuspended(bool suspend)
        {
            if (suspendDuringPeek == null) return;
            foreach (var mb in suspendDuringPeek)
            {
                if (mb != null) mb.enabled = !suspend;
            }
        }
    }
}
