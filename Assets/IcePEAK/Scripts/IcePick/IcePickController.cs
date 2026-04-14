using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRPickDriver))]
public class IcePickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SwingDetector swingDetector;
    [SerializeField] private Transform tipTransform;
    [SerializeField] private XRPickDriver pickDriver;

    [Header("Controller Reference (for ClimbingLocomotion)")]
    [Tooltip("The Left/Right Controller transform — only used to read position for climbing")]
    [SerializeField] private Transform controllerTransform;

    [Header("Embed Settings")]
    [Tooltip("How deep the pick tip sinks into the surface on embed (meters)")]
    [SerializeField] private float embedDepth = 0.03f;

    [Header("Rock Settings")]
    [Tooltip("Minimum surface normal Y to count as a horizontal ledge (0=vertical, 1=flat)")]
    [SerializeField] private float rockLedgeNormalThreshold = 0.5f;
    [Tooltip("Raycast distance from tip to find rock surface normal")]
    [SerializeField] private float rockNormalRayDistance = 0.15f;
    [Tooltip("Layer mask for rock surfaces")]
    [SerializeField] private LayerMask rockLayerMask;

    [Header("Input")]
    [Tooltip("Trigger (activate) action for this hand — use XRI > Activate Value")]
    [SerializeField] private InputActionReference triggerAction;
    [Tooltip("Trigger value above which the pick releases from the surface")]
    [SerializeField] private float triggerReleaseThreshold = 0.5f;

    // --- Public API ---
    public bool IsEmbedded => _isEmbedded;
    public Vector3 EmbedWorldPosition => _embedWorldPos;
    public Transform ControllerTransform => controllerTransform;

    /// Invoked when the pick first embeds in a surface.
    public System.Action<IcePickController, SurfaceTag> OnEmbedded;

    /// Invoked when the pick is released.
    public System.Action<IcePickController> OnReleased;

    // --- Private ---
    private bool _isEmbedded;
    private Vector3 _embedWorldPos;
    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;

        if (pickDriver == null)
            pickDriver = GetComponent<XRPickDriver>();
    }

    private void OnEnable()
    {
        if (triggerAction == null)
        {
            Debug.LogWarning($"[IcePick {gameObject.name}] triggerAction is NOT assigned in Inspector!");
            return;
        }
        if (triggerAction.action == null)
        {
            Debug.LogWarning($"[IcePick {gameObject.name}] triggerAction.action is null — broken reference?");
            return;
        }
        triggerAction.action.Enable();
    }

    // --- Trigger release ---
    private void Update()
    {
        if (!_isEmbedded) return;

        if (triggerAction == null || triggerAction.action == null) return;

        float triggerValue = triggerAction.action.ReadValue<float>();
        if (triggerValue > triggerReleaseThreshold)
        {
            Debug.Log($"[IcePick {gameObject.name}] Trigger pressed — releasing");
            Release();
        }
    }

    // --- Surface detection (via trigger colliders) ---
    private void OnTriggerEnter(Collider other)
    {
        if (_isEmbedded) return;

        SurfaceTag surface = other.GetComponentInParent<SurfaceTag>();
        if (surface == null) return;

        if (!swingDetector.IsSwingFastEnough) return;

        if (surface.Type == SurfaceType.Ice)
        {
            Debug.Log($"[IcePick {gameObject.name}] Ice embed, speed={swingDetector.CurrentSpeed:F2}");
            Embed(surface);
        }
        else if (surface.Type == SurfaceType.Rock)
        {
            // Raycast from tip into the surface to check if it's a horizontal ledge
            if (CheckRockLedge())
            {
                Debug.Log($"[IcePick {gameObject.name}] Rock ledge embed");
                Embed(surface);
            }
            else
            {
                Debug.Log($"[IcePick {gameObject.name}] Rock hit but too smooth — no grip");
            }
        }
    }

    private bool CheckRockLedge()
    {
        // Cast a ray from the tip forward (into the surface) to get the normal
        if (Physics.Raycast(tipTransform.position, tipTransform.forward,
                            out RaycastHit hit, rockNormalRayDistance, rockLayerMask))
        {
            return hit.normal.y > rockLedgeNormalThreshold;
        }
        return false;
    }

    // --- Embed ---
    private void Embed(SurfaceTag surface)
    {
        _isEmbedded = true;
        _embedWorldPos = tipTransform.position;

        // Stop the XR driver — pick freezes in world space
        pickDriver.enabled = false;

        // Nudge into the surface slightly for visual sell
        transform.position += tipTransform.forward * embedDepth;

        // TODO: audio

        OnEmbedded?.Invoke(this, surface);
    }

    // --- Release ---
    public void Release()
    {
        if (!_isEmbedded) return;

        _isEmbedded = false;

        // Resume XR-driven tracking
        pickDriver.enabled = true;

        Debug.Log($"[IcePick {gameObject.name}] Released");

        OnReleased?.Invoke(this);
    }
}
