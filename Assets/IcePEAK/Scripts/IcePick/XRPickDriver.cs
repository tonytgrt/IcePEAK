using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Positions the ice pick directly from XR tracking data, at the same timing
/// as TrackedPoseDriver (onAfterUpdate). No velocity chasing, no jitter.
/// Disable this component when the pick is embedded; re-enable on release.
/// </summary>
public class XRPickDriver : MonoBehaviour
{
    [Header("XR Input (same actions as the controller's TrackedPoseDriver)")]
    [SerializeField] private InputActionReference positionAction;
    [SerializeField] private InputActionReference rotationAction;

    [Header("Space Reference")]
    [Tooltip("The Camera Offset transform under XR Origin — tracking data is local to this")]
    [SerializeField] private Transform cameraOffset;

    [Header("Grip Offset")]
    [Tooltip("Position offset from the controller origin to the pick grip (local space)")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 0f, 0.08f);
    [Tooltip("Rotation offset from the controller to the pick grip (Euler angles)")]
    [SerializeField] private Vector3 rotationOffset = new Vector3(45f, 0f, 0f);

    private Quaternion _rotOffsetQuat;

    private void Awake()
    {
        _rotOffsetQuat = Quaternion.Euler(rotationOffset);
    }

    private void OnEnable()
    {
        if (positionAction != null && positionAction.action != null)
            positionAction.action.Enable();
        if (rotationAction != null && rotationAction.action != null)
            rotationAction.action.Enable();

        InputSystem.onAfterUpdate += UpdatePose;
    }

    private void OnDisable()
    {
        InputSystem.onAfterUpdate -= UpdatePose;
    }

    private void UpdatePose()
    {
        if (positionAction == null || rotationAction == null) return;

        // Read raw tracking data (same values the controller's TrackedPoseDriver reads)
        Vector3 trackingPos = positionAction.action.ReadValue<Vector3>();
        Quaternion trackingRot = rotationAction.action.ReadValue<Quaternion>();

        // Convert from tracking space to world space via Camera Offset
        // (this is what being a child of Camera Offset does implicitly)
        Vector3 worldPos = cameraOffset.TransformPoint(trackingPos);
        Quaternion worldRot = cameraOffset.rotation * trackingRot;

        // Apply the grip offset (where the pick sits relative to the hand)
        worldPos += worldRot * positionOffset;
        worldRot *= _rotOffsetQuat;

        transform.SetPositionAndRotation(worldPos, worldRot);
    }
}
