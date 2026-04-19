using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Placeholder cold-spray canister. Activate() plays a short particle burst at
    /// the nozzle. No gameplay effect yet; <see cref="Spray"/> mirrors the design-doc
    /// vocabulary so future crack-timer extension code can bind to it.
    /// </summary>
    public class ColdSpray : MonoBehaviour, IHoldable, IActivatable
    {
        [Header("Visual refs (wired on the prefab)")]
        [SerializeField] private ParticleSystem mist;

        [Header("Tunables")]
        [SerializeField] private float burstSeconds = 0.3f;

        private bool _isPlaying;

        public void OnTransfer(CellKind from, CellKind to)
        {
            Debug.Log($"[ColdSpray] {from} -> {to}");
        }

        public void Activate() => Spray();

        public void Spray()
        {
            if (_isPlaying) return;
            if (mist == null) return;
            StartCoroutine(PlayBurst());
        }

        private IEnumerator PlayBurst()
        {
            _isPlaying = true;
            mist.Play();
            yield return new WaitForSeconds(burstSeconds);
            mist.Stop();
            _isPlaying = false;
        }
    }
}
