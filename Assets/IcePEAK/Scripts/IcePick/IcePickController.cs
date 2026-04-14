using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class IcePickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SwingDetector swingDetector;
    [SerializeField] private Transform tipTransform;

    [Header("Controller Follow")]
    [Tooltip("Drag the Left/Right Controller transform here")]
    [SerializeField] private Transform controllerTarget;
    [Tooltip("Local position offset relative to the controller")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 0f, 0.08f);
    [Tooltip("Local rotation offset relative to the controller (Euler angles)")]
    [SerializeField] private Vector3 rotationOffset = new Vector3(45f, 0f, 0f);

    [Header("Embed Settings")]
    [Tooltip("How deep the pick tip sinks into the surface on embed (meters)")]
    [SerializeField] private float embedDepth = 0.03f;

    [Header("Rock Settings")]
    [Tooltip("Minimum surface normal Y to count as a horizontal ledge (0=vertical, 1=flat)")]
    [SerializeField] private float rockLedgeNormalThreshold = 0.5f;

    [Header("Input")]
    [Tooltip("Trigger (activate) action for this hand — use XRI > Activate Value")]
    [SerializeField] private InputActionReference triggerAction;
    [Tooltip("Trigger value above which the pick releases from the surface")]
    [SerializeField] private float triggerReleaseThreshold = 0.5f;

    // --- Public API ---
    public bool IsEmbedded => _isEmbedded;
    public Vector3 EmbedWorldPosition => _embedWorldPos;
    public Transform ControllerTransform => controllerTarget;

    /// Invoked when the pick first embeds in a surface.
    public System.Action<IcePickController, SurfaceTag> OnEmbedded;

    /// Invoked when the pick is released.
    public System.Action<IcePickController> OnReleased;

    // --- Private ---
    private bool _isEmbedded;
    private Vector3 _embedWorldPos;
    private Rigidbody _rb;
    private Quaternion _rotOffsetQuat;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rotOffsetQuat = Quaternion.Euler(rotationOffset);
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
        Debug.Log($"[IcePick {gameObject.name}] Trigger action enabled: '{triggerAction.action.name}'");
    }

    // --- Physics-based follow ---
    private void FixedUpdate()
    {
        if (_isEmbedded || controllerTarget == null) return;

        // Compute world-space target from controller + offset
        Vector3 targetPos = controllerTarget.TransformPoint(positionOffset);
        Quaternion targetRot = controllerTarget.rotation * _rotOffsetQuat;

        // Drive position via velocity (physics will block rock collisions)
        _rb.linearVelocity = (targetPos - _rb.position) / Time.fixedDeltaTime;

        // Drive rotation via angular velocity
        Quaternion deltaRot = targetRot * Quaternion.Inverse(_rb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (axis.sqrMagnitude > 0.001f)
            _rb.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
        else
            _rb.angularVelocity = Vector3.zero;
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

    // --- Ice detection (via trigger collider on TipCollider child) ---
    private void OnTriggerEnter(Collider other)
    {
        if (_isEmbedded) return;

        SurfaceTag surface = other.GetComponentInParent<SurfaceTag>();
        if (surface == null || surface.Type != SurfaceType.Ice) return;

        if (swingDetector.IsSwingFastEnough)
        {
            Debug.Log($"[IcePick {gameObject.name}] Ice embed, speed={swingDetector.CurrentSpeed:F2}");
            Embed(surface);
        }
    }

    // --- Rock detection (via physical collider on mesh) ---
    private void OnCollisionEnter(Collision collision)
    {
        if (_isEmbedded) return;

        SurfaceTag surface = collision.gameObject.GetComponentInParent<SurfaceTag>();
        if (surface == null || surface.Type != SurfaceType.Rock) return;

        if (!swingDetector.IsSwingFastEnough) return;

        // Check if any contact has a horizontal-enough normal (a ledge)
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > rockLedgeNormalThreshold)
            {
                Debug.Log($"[IcePick {gameObject.name}] Rock ledge embed, normal.y={contact.normal.y:F2}");
                Embed(surface);
                return;
            }
        }

        Debug.Log($"[IcePick {gameObject.name}] Rock hit but too smooth — sliding off");
    }

    // --- Embed ---
    private void Embed(SurfaceTag surface)
    {
        _isEmbedded = true;
        _embedWorldPos = tipTransform.position;

        // Freeze the pick in place — stop all physics movement
        _rb.isKinematic = true;

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

        // Resume physics-based following
        _rb.isKinematic = false;

        Debug.Log($"[IcePick {gameObject.name}] Released");

        OnReleased?.Invoke(this);
    }
}
