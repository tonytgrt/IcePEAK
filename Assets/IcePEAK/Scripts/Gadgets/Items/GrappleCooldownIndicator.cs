using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Drives a barrel-mounted cooldown visual on the grapple gun. Reads
    /// CooldownProgress01 from the gun and writes it into the indicator
    /// renderer's material via MaterialPropertyBlock (avoids leaking shared
    /// material instances). Plays a one-shot scale punch when the gun
    /// transitions from cooling to ready.
    /// </summary>
    public class GrappleCooldownIndicator : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private GrappleGun gun;
        [SerializeField] private Renderer fillRenderer;

        [Header("Material binding")]
        [Tooltip("Shader float property name on the indicator material. 0 = cooling, 1 = ready.")]
        [SerializeField] private string fillProperty = "_Fill";

        [Header("Ready punch")]
        [Tooltip("Scale punch duration on the cooling→ready edge.")]
        [SerializeField] private float readyPunchDuration = 0.2f;
        [Tooltip("Peak local scale multiplier during the punch.")]
        [SerializeField] private float readyPunchScale = 1.2f;

        private MaterialPropertyBlock _mpb;
        private int _fillID;
        private bool _wasOnCooldown;
        private Vector3 _baseScale;
        private float _punchUntil;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _fillID = Shader.PropertyToID(fillProperty);
            if (fillRenderer != null) _baseScale = fillRenderer.transform.localScale;
        }

        private void Update()
        {
            if (gun == null || fillRenderer == null) return;

            float progress = gun.CooldownProgress01;
            fillRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_fillID, progress);
            fillRenderer.SetPropertyBlock(_mpb);

            bool isOnCooldown = gun.IsOnCooldown;
            if (_wasOnCooldown && !isOnCooldown)
                _punchUntil = Time.time + readyPunchDuration;
            _wasOnCooldown = isOnCooldown;

            if (Time.time < _punchUntil && readyPunchDuration > 0f)
            {
                float t = 1f - (_punchUntil - Time.time) / readyPunchDuration;
                float pulse = Mathf.Sin(t * Mathf.PI);
                fillRenderer.transform.localScale = _baseScale * (1f + (readyPunchScale - 1f) * pulse);
            }
            else
            {
                fillRenderer.transform.localScale = _baseScale;
            }
        }
    }
}
