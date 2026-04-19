using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Placeholder piton. Activate() pulses the visual forward along its local Z
    /// (as if being driven into rock) then back. No gameplay effect yet;
    /// <see cref="Plant"/> mirrors the design-doc vocabulary.
    /// </summary>
    public class Piton : MonoBehaviour, IHoldable, IActivatable
    {
        [Header("Visual refs (wired on the prefab)")]
        [SerializeField] private Transform visual;

        [Header("Tunables")]
        [SerializeField] private float plantDistance = 0.05f;
        [SerializeField] private float plantDuration = 0.2f;

        [Header("Hint")]
        [SerializeField] private string displayName = "Piton";

        public string DisplayName => displayName;

        private bool _isPlaying;

        public void OnTransfer(CellKind from, CellKind to)
        {
            Debug.Log($"[Piton] {from} -> {to}");
        }

        public void Activate() => Plant();

        public void Plant()
        {
            if (_isPlaying) return;
            if (visual == null) return;
            StartCoroutine(PlayPlant());
        }

        private IEnumerator PlayPlant()
        {
            _isPlaying = true;
            var rest = visual.localPosition;
            var driven = rest + new Vector3(0f, 0f, plantDistance);
            var half = plantDuration * 0.5f;

            // Drive in
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                visual.localPosition = Vector3.Lerp(rest, driven, t / half);
                yield return null;
            }
            visual.localPosition = driven;

            // Pull back
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                visual.localPosition = Vector3.Lerp(driven, rest, t / half);
                yield return null;
            }
            visual.localPosition = rest;

            _isPlaying = false;
        }
    }
}
