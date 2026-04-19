using TMPro;
using UnityEngine;

namespace IcePEAK.Gadgets.UI
{
    /// <summary>
    /// World-space text label used as a contextual hint near a hoverable object.
    /// Shows/hides by toggling <see cref="root"/>. Billboards to the main camera
    /// (HMD) in LateUpdate so the text always faces the player.
    ///
    /// Typically instantiated as a child of an <see cref="IHintSource.HintAnchor"/>.
    /// </summary>
    public class HintLabel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text label;
        [Tooltip("GameObject toggled to show/hide the label. Usually the Canvas child.")]
        [SerializeField] private GameObject root;

        private Camera _hmd;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        /// <summary>
        /// Show the label with the given text. Null or empty hides the label.
        /// </summary>
        public void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            if (label != null) label.text = text;
            if (root != null) root.SetActive(true);
            FaceHmd();
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private void LateUpdate()
        {
            if (root == null || !root.activeSelf) return;
            FaceHmd();
        }

        private void FaceHmd()
        {
            if (_hmd == null) _hmd = ResolveHmd();
            if (_hmd == null) return;

            // Yaw-only billboard. Flatten the HMD→label vector onto the XZ plane so the
            // label stays vertical when the player looks down at the belt and only yaws
            // around world-up to track HMD position.
            var dir = transform.position - _hmd.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static Camera ResolveHmd()
        {
            var main = Camera.main;
            if (main != null) return main;
            // Fallback for XR rigs whose center-eye camera isn't tagged MainCamera.
            var cams = Camera.allCameras;
            return cams.Length > 0 ? cams[0] : null;
        }
    }
}
