using UnityEngine;

public class SwingDetector : MonoBehaviour
{
    [Header("Velocity Tracking")]
    [Tooltip("Number of frames to average velocity over (smoothing)")]
    [SerializeField] private int velocityFrameWindow = 5;

    [Header("Thresholds")]
    [Tooltip("Minimum tip speed (m/s) to count as a valid swing")]
    [SerializeField] private float embedVelocityThreshold = 1.5f;

    // --- Public API ---
    public float CurrentSpeed => _currentSpeed;
    public Vector3 CurrentVelocity => _currentVelocity;
    public bool IsSwingFastEnough => _currentSpeed >= embedVelocityThreshold;

    // --- Private ---
    private Vector3[] _previousPositions;
    private int _frameIndex;
    private Vector3 _currentVelocity;
    private float _currentSpeed;

    private void Start()
    {
        _previousPositions = new Vector3[velocityFrameWindow];
        for (int i = 0; i < velocityFrameWindow; i++)
            _previousPositions[i] = transform.position;
    }

    private void Update()
    {
        // Store current position
        _previousPositions[_frameIndex] = transform.position;

        // Compute velocity as delta between oldest and newest sample
        int oldestIndex = (_frameIndex + 1) % velocityFrameWindow;
        float timeDelta = Time.deltaTime * velocityFrameWindow;

        if (timeDelta > 0f)
        {
            _currentVelocity = (_previousPositions[_frameIndex]
                              - _previousPositions[oldestIndex]) / timeDelta;
            _currentSpeed = _currentVelocity.magnitude;
        }

        _frameIndex = (_frameIndex + 1) % velocityFrameWindow;
    }
}