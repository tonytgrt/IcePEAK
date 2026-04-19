using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using IcePEAK.Gadgets;

[RequireComponent(typeof(Rigidbody))]
public class IcePickController : MonoBehaviour, IHoldable
{
    [Header("References")]
    [SerializeField] private SwingDetector swingDetector;
    [SerializeField] private Transform tipTransform;
    [Tooltip("Trigger collider on the tip — disabled while stowed so it doesn't embed in ice.")]
    [SerializeField] private Collider tipCollider;
    [SerializeField] private AudioClip embedSound;
    [SerializeField] private AudioClip bounceSound;
    [SerializeField] private AudioSource audioSource;

    [Header("Embed Settings")]
    [Tooltip("How deep the pick tip sinks into the surface on embed (meters)")]
    [SerializeField] private float embedDepth = 0.03f;

    [Header("Input")]
    [Tooltip("Trigger (activate) action for this hand — use XRI > Activate Value")]
    [SerializeField] private InputActionReference triggerAction;
    [Tooltip("Trigger value above which the pick releases from the surface")]
    [SerializeField] private float triggerReleaseThreshold = 0.5f;

    [Header("Hint")]
    [SerializeField] private string displayName = "Ice Pick";

    public string DisplayName => displayName;

    // --- Public API ---
    public bool IsEmbedded => _isEmbedded;
    public Vector3 EmbedWorldPosition => _embedWorldPos;
    public Transform ControllerTransform => _controllerParent;

    /// Invoked when the pick first embeds in an ice surface.
    public System.Action<IcePickController, SurfaceTag> OnEmbedded;

    /// Invoked when the pick is released (trigger released or ice shattered).
    public System.Action<IcePickController> OnReleased;

    // --- Private ---
    private bool _isEmbedded;
    private Vector3 _embedWorldPos;
    private Transform _controllerParent;   // cached controller transform
    private Vector3 _localPosInParent;     // original local position
    private Quaternion _localRotInParent;   // original local rotation

    private void Awake()
    {
        _controllerParent = transform.parent;
        _localPosInParent = transform.localPosition;
        _localRotInParent = transform.localRotation;
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
        Debug.Log($"[IcePick {gameObject.name}] Trigger action enabled: '{triggerAction.action.name}' in map '{triggerAction.action.actionMap?.name}'");
    }

    private void Update()
    {
        if (!_isEmbedded) return;

        if (triggerAction == null || triggerAction.action == null)
        {
            Debug.LogWarning($"[IcePick {gameObject.name}] Embedded but triggerAction is null — cannot read input!");
            return;
        }

        float triggerValue = triggerAction.action.ReadValue<float>();
        Debug.Log($"[IcePick {gameObject.name}] trigger={triggerValue:F3}, threshold={triggerReleaseThreshold}");

        if (triggerValue > triggerReleaseThreshold)
        {
            Debug.Log($"[IcePick {gameObject.name}] Trigger pressed — calling Release()");
            Release();
        }
    }

    // --- Trigger Detection ---
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[IcePick] Trigger hit: {other.gameObject.name}, speed={swingDetector.CurrentSpeed:F2}");

        if (_isEmbedded) return;

        SurfaceTag surface = other.GetComponentInParent<SurfaceTag>();
        if (surface == null) return;

        if (surface.Type == SurfaceType.Ice && swingDetector.IsSwingFastEnough)
        {
            Embed(surface);
        }
        else
        {
            Bounce(surface.Type);
        }
    }

    // --- Embed ---
    private void Embed(SurfaceTag surface)
    {
        _isEmbedded = true;

        // Record where the tip hit
        _embedWorldPos = tipTransform.position;

        // Detach from controller so the pick stays fixed in world space
        transform.SetParent(null, worldPositionStays: true);

        // Nudge the pick slightly into the surface for visual sell
        transform.position += tipTransform.forward * embedDepth;

        // TODO: audio (assign audioSource and clips in Inspector first)

        Debug.Log($"[IcePick {gameObject.name}] Embedded at {_embedWorldPos}");

        OnEmbedded?.Invoke(this, surface);
    }

    // --- Bounce ---
    private void Bounce(SurfaceType type)
    {
        // TODO: audio
    }

    // --- Release ---
    public void Release()
    {
        if (!_isEmbedded) return;

        _isEmbedded = false;

        // Re-attach to controller
        transform.SetParent(_controllerParent);
        transform.localPosition = _localPosInParent;
        transform.localRotation = _localRotInParent;

        Debug.Log($"[IcePick {gameObject.name}] Released");

        OnReleased?.Invoke(this);
    }

    // --- IHoldable ---

    /// <summary>
    /// Called by the belt/hand cell after the pick has been reparented into the new cell.
    /// Stow disables the tip so a holstered pick can't embed in ice.
    /// </summary>
    public void OnTransfer(CellKind from, CellKind to)
    {
        SetStowed(to == CellKind.BeltSlot);

        // PlaceInto() in HandInteractionController resets the item's local pose to
        // (0, identity). The pick needs its design-time offset (~5cm down, ~13cm
        // back, ~27° pitch) to sit correctly in the hand — restore the Awake cache.
        if (to == CellKind.Hand)
        {
            transform.localPosition = _localPosInParent;
            transform.localRotation = _localRotInParent;
        }
    }

    /// <summary>
    /// Disables/enables the tip trigger collider and the swing detector. Safe to call repeatedly.
    /// </summary>
    public void SetStowed(bool stowed)
    {
        if (tipCollider != null) tipCollider.enabled = !stowed;
        if (swingDetector != null) swingDetector.enabled = !stowed;
        Debug.Log($"[IcePick {gameObject.name}] SetStowed({stowed})");
    }
}