using UnityEngine;

public class ClimbingLocomotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private IcePickController leftPick;
    [SerializeField] private IcePickController rightPick;
    [Tooltip("Optional. If assigned, this component defers provider state to GrappleLocomotion while a zip is in flight, so a pick-release event during the zip won't re-enable gravity.")]
    [SerializeField] private IcePEAK.Gadgets.GrappleLocomotion grappleLocomotion;

    [Header("Ground Detection")]
    [Tooltip("Layer mask for surfaces the player can stand on")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Raycast distance below the XR Origin to detect ground")]
    [SerializeField] private float groundCheckDistance = 0.3f;

    [Header("Default Locomotion Providers")]
    [Tooltip("Drag all locomotion provider components to disable while climbing (move, turn, teleport, etc.)")]
    [SerializeField] private MonoBehaviour[] locomotionProviders;

    // Track controller positions for climbing delta
    private Vector3 _leftPrevPos;
    private Vector3 _rightPrevPos;
    private Transform _grappleAnchor;
    private Vector3 _grapplePrevPos;

    public void SetGrappleAnchor(Transform anchor)
    {
        _grappleAnchor = anchor;
        if (anchor != null)
            _grapplePrevPos = anchor.position;
        UpdateLocomotionProviders();
    }

    private void OnEnable()
    {
        leftPick.OnEmbedded += OnPickEmbedded;
        rightPick.OnEmbedded += OnPickEmbedded;
        leftPick.OnReleased += OnPickReleased;
        rightPick.OnReleased += OnPickReleased;

        // If a pick is already embedded at enable time, its OnEmbedded was
        // missed — GrappleLocomotion can disable us for the duration of a zip,
        // during which the off-hand may have swung a pick into the arriving
        // surface. Sync prev pos so the first Update doesn't apply a stale delta.
        if (leftPick.IsEmbedded) _leftPrevPos = leftPick.ControllerTransform.position;
        if (rightPick.IsEmbedded) _rightPrevPos = rightPick.ControllerTransform.position;
    }

    private void OnDisable()
    {
        leftPick.OnEmbedded -= OnPickEmbedded;
        rightPick.OnEmbedded -= OnPickEmbedded;
        leftPick.OnReleased -= OnPickReleased;
        rightPick.OnReleased -= OnPickReleased;
    }

    private void OnPickEmbedded(IcePickController pick, SurfaceTag _)
    {
        // Snapshot the controller position at the moment of embed
        if (pick == leftPick)
            _leftPrevPos = leftPick.ControllerTransform.position;
        else
            _rightPrevPos = rightPick.ControllerTransform.position;

        UpdateLocomotionProviders();
    }

    private void OnPickReleased(IcePickController pick)
    {
        UpdateLocomotionProviders();
    }

    private void Update()
    {
        bool leftEmbedded = leftPick.IsEmbedded;
        bool rightEmbedded = rightPick.IsEmbedded;
        bool grappleAnchored = _grappleAnchor != null;

        if (!leftEmbedded && !rightEmbedded && !grappleAnchored)
            return;

        Vector3 totalDelta = Vector3.zero;
        int activeHands = 0;

        if (leftEmbedded)
        {
            Vector3 currentPos = leftPick.ControllerTransform.position;
            totalDelta += _leftPrevPos - currentPos;
            activeHands++;
        }

        if (rightEmbedded)
        {
            Vector3 currentPos = rightPick.ControllerTransform.position;
            totalDelta += _rightPrevPos - currentPos;
            activeHands++;
        }

        if (grappleAnchored)
        {
            Vector3 currentPos = _grappleAnchor.position;
            totalDelta += _grapplePrevPos - currentPos;
            activeHands++;
        }

        // Average if multiple hands are climbing
        totalDelta /= activeHands;
        xrOrigin.position += totalDelta;

        // Update prev positions AFTER moving the origin so they include
        // the origin shift — this prevents the oscillation feedback loop
        if (leftEmbedded)
            _leftPrevPos = leftPick.ControllerTransform.position;
        if (rightEmbedded)
            _rightPrevPos = rightPick.ControllerTransform.position;
        if (grappleAnchored)
            _grapplePrevPos = _grappleAnchor.position;
    }

    private bool IsOnGround()
    {
        return Physics.Raycast(xrOrigin.position, Vector3.down,
                               groundCheckDistance, groundLayer);
    }

    private void UpdateLocomotionProviders()
    {
        // During a zip, GrappleLocomotion owns provider state — its
        // SetLocomotionProviders(false) call must not be undone by a
        // pick-release event firing partway through ZipRoutine.
        if (grappleLocomotion != null && grappleLocomotion.IsZipping) return;

        bool anyEmbedded = leftPick.IsEmbedded || rightPick.IsEmbedded || _grappleAnchor != null;
        bool onGround = IsOnGround();

        // Disable default locomotion when climbing off the ground
        bool enableDefault = !anyEmbedded || onGround;

        Debug.Log($"[Climbing] anyEmbedded={anyEmbedded}, onGround={onGround}, defaultLocomotion={enableDefault}");

        foreach (var provider in locomotionProviders)
        {
            if (provider != null)
                provider.enabled = enableDefault;
        }
    }
}
