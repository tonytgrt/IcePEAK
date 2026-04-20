using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Grapple gun. Held in a hand, it projects a diegetic laser forward
    /// from the barrel while idle — green when the laser would hit a
    /// <see cref="SurfaceTag"/> collider within <see cref="maxRange"/>,
    /// red otherwise. Activate (trigger) raycasts from the barrel:
    /// on hit, dispatches to <see cref="GrappleLocomotion"/> to zip the
    /// rig to the surface and self-destructs on arrival; on miss, plays a
    /// brief red dry-fire flash.
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

        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;

        private bool _isStowed = true;
        private bool _isZipping;
        private bool _isDryFiring;
        private ICell _owningCell;
        private GrappleLocomotion _locomotion;
        private Vector3 _zipAnchor;

        public void OnTransfer(CellKind from, CellKind to)
        {
            _isStowed = (to == CellKind.BeltSlot);

            if (to == CellKind.Hand)
            {
                _owningCell = ResolveOwningCell();
            }
            else
            {
                _owningCell = null;
            }

            if (_isStowed)
            {
                if (laser != null) laser.enabled = false;
                if (rope != null) rope.enabled = false;
            }
        }

        public void Activate() => Fire();

        public void Fire()
        {
            if (_isStowed || _isZipping || _isDryFiring) return;
            if (barrelTip == null) return;

            if (!TryResolveLocomotion()) { StartDryFire(); return; }
            if (_locomotion.IsZipping) return;

            if (Physics.Raycast(barrelTip.position, barrelTip.forward, out RaycastHit hit,
                                maxRange, hitMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<SurfaceTag>() != null)
            {
                _zipAnchor = hit.point;
                if (rope != null)
                {
                    rope.positionCount = 2;
                    rope.SetPosition(0, barrelTip.position);
                    rope.SetPosition(1, _zipAnchor);
                    rope.enabled = true;
                }

                if (_locomotion.StartZip(_zipAnchor, hit.normal, OnArrival))
                {
                    _isZipping = true;
                }
                else
                {
                    if (rope != null) rope.enabled = false;
                }
            }
            else
            {
                StartDryFire();
            }
        }

        private void Update()
        {
            if (_isStowed || _isZipping || _isDryFiring) return;
            if (laser == null || barrelTip == null) return;

            Vector3 origin = barrelTip.position;
            Vector3 dir = barrelTip.forward;

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

        private void LateUpdate()
        {
            if (!_isZipping || rope == null || barrelTip == null) return;
            rope.SetPosition(0, barrelTip.position);
            rope.SetPosition(1, _zipAnchor);
        }

        private void StartDryFire()
        {
            StartCoroutine(DryFireFlash());
        }

        private IEnumerator DryFireFlash()
        {
            _isDryFiring = true;

            if (laser != null && barrelTip != null)
            {
                laser.positionCount = 2;
                laser.SetPosition(0, barrelTip.position);
                laser.SetPosition(1, barrelTip.position + barrelTip.forward * maxRange);
                laser.startColor = laserOutOfRangeColor;
                laser.endColor = laserOutOfRangeColor;
                laser.enabled = true;
            }

            yield return new WaitForSeconds(dryFireDuration);

            _isDryFiring = false;
        }

        private void OnArrival()
        {
            if (rope != null) rope.enabled = false;
            if (laser != null) laser.enabled = false;

            if (_owningCell != null)
            {
                _owningCell.Take();
            }
            Destroy(gameObject);
        }

        private bool TryResolveLocomotion()
        {
            if (_locomotion != null) return true;
            _locomotion = FindAnyObjectByType<GrappleLocomotion>();
            return _locomotion != null;
        }

        private ICell ResolveOwningCell()
        {
            Transform t = transform.parent;
            while (t != null)
            {
                var cell = t.GetComponent<ICell>();
                if (cell != null) return cell;
                t = t.parent;
            }
            return null;
        }
    }
}
