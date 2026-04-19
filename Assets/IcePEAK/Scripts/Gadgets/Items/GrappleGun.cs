using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Placeholder grappling hook. Activate() fires a short LineRenderer streak
    /// from the barrel tip. No gameplay effect yet; the named method <see cref="Fire"/>
    /// mirrors the design-doc vocabulary so future callers can bind to it.
    /// </summary>
    public class GrappleGun : MonoBehaviour, IHoldable, IActivatable
    {
        [Header("Visual refs (wired on the prefab)")]
        [SerializeField] private LineRenderer streak;
        [SerializeField] private Transform barrelTip;

        [Header("Tunables")]
        [SerializeField] private float streakLength = 1f;
        [SerializeField] private float streakDuration = 0.2f;

        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;

        private bool _isPlaying;

        public void OnTransfer(CellKind from, CellKind to)
        {
            Debug.Log($"[GrappleGun] {from} -> {to}");
        }

        public void Activate() => Fire();

        public void Fire()
        {
            if (_isPlaying) return;
            if (streak == null || barrelTip == null) return;
            StartCoroutine(PlayStreak());
        }

        private IEnumerator PlayStreak()
        {
            _isPlaying = true;
            streak.positionCount = 2;
            streak.SetPosition(0, barrelTip.position);
            streak.SetPosition(1, barrelTip.position + barrelTip.forward * streakLength);
            streak.enabled = true;
            yield return new WaitForSeconds(streakDuration);
            streak.enabled = false;
            _isPlaying = false;
        }
    }
}
