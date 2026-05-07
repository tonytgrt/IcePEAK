using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Grapple gun. Held in a hand, it projects a diegetic laser forward
    /// from the barrel while idle — green when the laser would hit a
    /// <see cref="SurfaceTag"/> collider within <see cref="maxRange"/>,
    /// red otherwise. Activate (trigger) raycasts from the barrel:
    /// on hit, dispatches to <see cref="GrappleLocomotion"/> to zip the
    /// rig to the surface; on miss, plays a brief red dry-fire flash.
    /// On a successful arrival the gun is auto-swapped for an ice pick
    /// from the belt (if one is stowed there) so the player can swing it
    /// into the wall during the wall-hang. Climbing-anchor lifecycle is
    /// owned by <see cref="GrappleLocomotion"/>.
    /// </summary>
    public class GrappleGun : MonoBehaviour, IHoldable, IActivatable
    {
        [Header("Visual refs (wired on the prefab)")]
        [SerializeField] private LineRenderer laser;
        [SerializeField] private LineRenderer rope;
        [SerializeField] private Transform barrelTip;

        [Header("Raycast")]
        [Tooltip("Maximum grapple distance (meters).")]
        [SerializeField] private float maxRange = 40f;
        [Tooltip("Layers the grapple raycast hits. Leave as Everything unless specific layers need to be excluded.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Dry-fire")]
        [Tooltip("Duration of the red miss flash before returning to live laser preview.")]
        [SerializeField] private float dryFireDuration = 0.15f;

        [Header("Laser colors")]
        [SerializeField] private Color laserValidColor = new Color(0.2f, 1f, 0.4f);
        [SerializeField] private Color laserOutOfRangeColor = new Color(1f, 0.3f, 0.3f);

        [Header("Input")]
        [Tooltip("XRI Left Interaction/Activate Value")]
        [SerializeField] private UnityEngine.InputSystem.InputActionReference leftTriggerAction;
        [Tooltip("XRI Right Interaction/Activate Value")]
        [SerializeField] private UnityEngine.InputSystem.InputActionReference rightTriggerAction;
        [SerializeField] private float triggerThreshold = 0.5f;

        [Header("Haptics")]
        [Tooltip("HapticImpulsePlayer on the left controller — buzzes on left-hand dry-fire.")]
        [SerializeField] private HapticImpulsePlayer leftHaptics;
        [Tooltip("HapticImpulsePlayer on the right controller — buzzes on right-hand dry-fire.")]
        [SerializeField] private HapticImpulsePlayer rightHaptics;
        [SerializeField, Range(0f, 1f)] private float dryFireHapticAmplitude = 0.5f;
        [SerializeField] private float dryFireHapticDuration = 0.1f;

        [Header("Cooldown")]
        [Tooltip("Seconds the gun is locked after a successful zip starts. Dry-fires do not start the cooldown.")]
        [SerializeField] private float cooldownDuration = 3.0f;

        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;

        /// <summary>True while the gun is on cooldown and Fire() will be rejected.</summary>
        public bool IsOnCooldown => Time.time < _cooldownEndTime;

        /// <summary>0 = just fired, 1 = ready (or cooldownDuration ≤ 0). Drives the diegetic indicator.</summary>
        public float CooldownProgress01 => cooldownDuration <= 0f
            ? 1f
            : Mathf.Clamp01(1f - (_cooldownEndTime - Time.time) / cooldownDuration);

        private bool _isStowed = true;
        private bool _isZipping;
        private bool _isDryFiring;
        private GrappleLocomotion _locomotion;
        private GadgetBelt _belt;
        private Vector3 _zipAnchor;
        private float _cooldownEndTime;
        private UnityEngine.InputSystem.InputActionReference _activeTriggerAction;
        private HapticImpulsePlayer _activeHaptics;

        private bool TriggerHeld => (_activeTriggerAction?.action?.ReadValue<float>() ?? 0f) >= triggerThreshold;

        public void OnTransfer(CellKind from, CellKind to)
        {
            _isStowed = (to == CellKind.BeltSlot);

            if (to == CellKind.Hand)
                _activeTriggerAction = ResolveHandTriggerAction();

            if (_isStowed)
            {
                if (laser != null) laser.enabled = false;
                if (rope != null) rope.enabled = false;
            }
        }

        private UnityEngine.InputSystem.InputActionReference ResolveHandTriggerAction()
        {
            for (var t = transform.parent; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Prefer Inspector-assigned ref; fall back to the component on the controller.
                    _activeHaptics = leftHaptics != null ? leftHaptics : t.GetComponent<HapticImpulsePlayer>();
                    return leftTriggerAction;
                }
                if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _activeHaptics = rightHaptics != null ? rightHaptics : t.GetComponent<HapticImpulsePlayer>();
                    return rightTriggerAction;
                }
            }
            _activeHaptics = rightHaptics;
            return rightTriggerAction;
        }

        private void OnEnable()
        {
            leftTriggerAction?.action?.Enable();
            rightTriggerAction?.action?.Enable();
        }

        private void Update()
        {
            if (_isStowed) return;

            // Cancel mid-zip if trigger released. OnZipComplete still fires.
            if (_isZipping && !TriggerHeld)
                _locomotion?.CancelZip();
        }

        public void Activate() => Fire();

        public void Fire()
        {
            if (_isStowed || _isZipping || _isDryFiring || IsOnCooldown) return;
            if (barrelTip == null) return;

            if (!TryResolveLocomotion())
            {
                Debug.LogWarning("[GrappleGun] GrappleLocomotion not found in scene — dry-firing.");
                StartDryFire();
                return;
            }

            if (Physics.Raycast(barrelTip.position, barrelTip.forward, out RaycastHit hit,
                                maxRange, hitMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<SurfaceTag>() != null)
            {
                _zipAnchor = hit.point;

                if (!_locomotion.StartZip(_zipAnchor, barrelTip.position, transform,
                                          OnZipArrivedAtWall, OnZipComplete)) return;

                _isZipping = true;
                _cooldownEndTime = Time.time + cooldownDuration;
                if (laser != null) laser.enabled = false;
                if (rope != null)
                {
                    rope.positionCount = 2;
                    rope.SetPosition(0, barrelTip.position);
                    rope.SetPosition(1, _zipAnchor);
                    rope.enabled = true;
                }
            }
            else
            {
                StartDryFire();
            }
        }

        private void LateUpdate()
        {
            if (barrelTip == null) return;

            if (rope != null && _isZipping)
            {
                rope.SetPosition(0, barrelTip.position);
                rope.SetPosition(1, _zipAnchor);
            }

            if (laser == null) return;

            if (_isStowed || _isZipping)
            {
                laser.enabled = false;
                return;
            }

            Vector3 origin = barrelTip.position;
            Vector3 dir = barrelTip.forward;

            if (_isDryFiring)
            {
                laser.positionCount = 2;
                laser.SetPosition(0, origin);
                laser.SetPosition(1, origin + dir * maxRange);
                laser.startColor = laserOutOfRangeColor;
                laser.endColor = laserOutOfRangeColor;
                laser.enabled = true;
                return;
            }

            bool validHit = Physics.Raycast(origin, dir, out RaycastHit hit,
                                            maxRange, hitMask, QueryTriggerInteraction.Ignore)
                            && hit.collider.GetComponentInParent<SurfaceTag>() != null;

            Vector3 end = validHit ? hit.point : origin + dir * maxRange;
            Color color = validHit ? laserValidColor : laserOutOfRangeColor;

            laser.positionCount = 2;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);
            laser.startColor = color;
            laser.endColor = color;
            laser.enabled = true;
        }

        private void StartDryFire()
        {
            StartCoroutine(DryFireFlash());
            if (_activeHaptics != null && dryFireHapticAmplitude > 0f && dryFireHapticDuration > 0f)
                _activeHaptics.SendHapticImpulse(dryFireHapticAmplitude, dryFireHapticDuration);
        }

        private IEnumerator DryFireFlash()
        {
            _isDryFiring = true;
            yield return new WaitForSeconds(dryFireDuration);
            _isDryFiring = false;
        }

        /// <summary>
        /// Fires at the start of the arrival phase, before the wall-hang begins.
        /// Auto-swaps this gun for an ice pick on the belt (Feature 4) so the
        /// player has a pick in hand for the full hang window. No-op if no pick
        /// is on the belt or no owning hand can be resolved.
        /// </summary>
        private void OnZipArrivedAtWall()
        {
            var hand = BeltSwap.FindOwningHand(transform);
            if (hand == null) return;

            if (_belt == null) _belt = FindAnyObjectByType<GadgetBelt>();
            if (_belt == null) return;

            var slot = BeltSwap.FindFirstSlotWithIcePick(_belt);
            if (slot == null) return;

            BeltSwap.Swap(hand, slot);
        }

        /// <summary>
        /// Fires at the end of the zip lifecycle (whether arrival ran or not).
        /// </summary>
        private void OnZipComplete()
        {
            _isZipping = false;
            if (rope != null) rope.enabled = false;
        }

        private bool TryResolveLocomotion()
        {
            if (_locomotion != null) return true;
            _locomotion = FindAnyObjectByType<GrappleLocomotion>();
            return _locomotion != null;
        }
    }
}
