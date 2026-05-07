using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace IcePEAK.Player
{
    /// <summary>
    /// Tracks elapsed climb time. Starts automatically on enable.
    /// Call <see cref="Stop"/> (e.g. from <see cref="DestinationTrigger"/>) to freeze the clock.
    /// Call <see cref="ResetTimer"/> to restart (e.g. after a respawn).
    ///
    /// Assign a 3D TextMeshPro object (GameObject > 3D Object > Text - TextMeshPro)
    /// parented to the HMD camera, positioned in front of the player's view.
    /// No Canvas required.
    /// </summary>
    public class ClimbTimer : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("3D TextMeshPro object parented to the HMD camera. " +
                 "Create via GameObject > 3D Object > Text - TextMeshPro.")]
        [SerializeField] private TextMeshPro timerLabel;

        [Header("Events")]
        [Tooltip("Fired once when the timer is stopped by reaching the destination.")]
        [SerializeField] private UnityEvent<float> onStopped;

        public float ElapsedSeconds { get; private set; }
        public bool IsRunning { get; private set; }

        private void OnEnable() => ResetTimer();

        private void Update()
        {
            if (!IsRunning) return;
            ElapsedSeconds += Time.deltaTime;
            UpdateLabel();
        }

        /// <summary>Freeze the timer. Safe to call multiple times.</summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            UpdateLabel();
            Debug.Log($"[ClimbTimer] Stopped at {FormatTime(ElapsedSeconds)}");
            onStopped?.Invoke(ElapsedSeconds);
        }

        /// <summary>Reset and restart the timer from zero.</summary>
        public void ResetTimer()
        {
            ElapsedSeconds = 0f;
            IsRunning = true;
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (timerLabel != null)
                timerLabel.text = FormatTime(ElapsedSeconds);
        }

        public static string FormatTime(float seconds)
        {
            int m  = (int)(seconds / 60f);
            int s  = (int)(seconds % 60f);
            int cs = (int)((seconds - Mathf.Floor(seconds)) * 100f);
            return $"{m:00}:{s:00}.{cs:00}";
        }
    }
}
