using UnityEngine;

public class ClimbingLocomotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private IcePickController leftPick;
    [SerializeField] private IcePickController rightPick;

    [Header("Ground Detection")]
    [Tooltip("Layer mask for surfaces the player can stand on")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Raycast distance below the XR Origin to detect ground")]
    [SerializeField] private float groundCheckDistance = 0.3f;

    [Header("Climbing Tuning")]
    [Tooltip("Controller movement below this (meters) is ignored — filters VR tracking noise")]
    [SerializeField] private float deadZone = 0.002f;

    [Header("Default Locomotion Providers")]
    [Tooltip("Drag all locomotion provider components to disable while climbing (move, turn, teleport, etc.)")]
    [SerializeField] private MonoBehaviour[] locomotionProviders;

    // Track controller positions for climbing delta
    private Vector3 _leftPrevPos;
    private Vector3 _rightPrevPos;

    private void OnEnable()
    {
        leftPick.OnEmbedded += OnPickEmbedded;
        rightPick.OnEmbedded += OnPickEmbedded;
        leftPick.OnReleased += OnPickReleased;
        rightPick.OnReleased += OnPickReleased;
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

        if (!leftEmbedded && !rightEmbedded)
            return;

        Vector3 totalDelta = Vector3.zero;
        int activeHands = 0;

        if (leftEmbedded)
        {
            Vector3 currentPos = leftPick.ControllerTransform.position;
            totalDelta += _leftPrevPos - currentPos;
            _leftPrevPos = currentPos;
            activeHands++;
        }

        if (rightEmbedded)
        {
            Vector3 currentPos = rightPick.ControllerTransform.position;
            totalDelta += _rightPrevPos - currentPos;
            _rightPrevPos = currentPos;
            activeHands++;
        }

        // Average if both hands are climbing
        totalDelta /= activeHands;

        // Ignore micro-movements from VR tracking noise
        if (totalDelta.magnitude < deadZone)
            return;

        xrOrigin.position += totalDelta;
    }

    private bool IsOnGround()
    {
        return Physics.Raycast(xrOrigin.position, Vector3.down,
                               groundCheckDistance, groundLayer);
    }

    private void UpdateLocomotionProviders()
    {
        bool anyEmbedded = leftPick.IsEmbedded || rightPick.IsEmbedded;
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
