using UnityEngine;
using IcePEAK.Gadgets.UI;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Single belt slot. Same adoption/initialPrefab pattern as HandCell.
    /// SetHighlighted toggles emissive on the held item's renderers, or on the
    /// placeholder renderer if the slot is empty.
    ///
    /// Convention (same as HandCell): the only design-time children under a BeltSlot
    /// GameObject should be (a) the placeholder renderer wired into placeholderRenderer
    /// and (b) optionally a held item. The placeholder child is explicitly skipped at
    /// Awake so it won't be mis-adopted as HeldItem; any OTHER extra child (debug gizmo,
    /// mesh helper, etc.) WILL be mis-adopted. Keep visuals on the belt parent instead.
    /// </summary>
    public class BeltSlot : MonoBehaviour, ICell, IHintSource
    {
        [Tooltip("If the anchor has no existing child at Awake, instantiate this prefab. Optional.")]
        [SerializeField] private GameObject initialPrefab;

        [Tooltip("Renderer on the empty-slot wireframe placeholder. Highlighted when slot is empty.")]
        [SerializeField] private Renderer placeholderRenderer;

        [Tooltip("Emissive color applied on highlight.")]
        [SerializeField] private Color highlightEmissive = new Color(1f, 0.85f, 0.3f) * 2f;

        [Header("Hint")]
        [Tooltip("Empty transform placed ~5cm above the slot. Labels anchor here.")]
        [SerializeField] private Transform hintAnchor;

        [Tooltip("Reusable HintLabel instance that actually displays the text. May be null if this slot has no hint.")]
        [SerializeField] private HintLabel hintLabel;

        public GameObject HeldItem { get; private set; }
        public Transform Anchor => transform;
        public CellKind Kind => CellKind.BeltSlot;

        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        public string GetHintText(HandCell hand)
        {
            var slotItem = HeldItem;
            var handItem = hand != null ? hand.HeldItem : null;

            if (handItem == null && slotItem == null) return null;

            // Slot-locked gadgets supply their own hint and don't participate in swap/draw/stow.
            if (slotItem != null && slotItem.TryGetComponent<IFixedInSlot>(out var fixedItem))
                return fixedItem.HintText;

            string slotName = GetDisplayName(slotItem);
            string handName = GetDisplayName(handItem);

            if (handItem == null) return $"Draw {slotName}";
            if (slotItem == null) return $"Stow {handName}";
            return $"Swap {handName} ↔ {slotName}";
        }

        private static string GetDisplayName(GameObject go)
        {
            if (go == null) return string.Empty;
            if (go.TryGetComponent<IHoldable>(out var h) && !string.IsNullOrEmpty(h.DisplayName))
                return h.DisplayName;
            return go.name;
        }

        private bool _highlighted;
        private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            // The placeholder renderer is a child of this slot (e.g., a wireframe ring).
            // It should NOT count as an "adopted" held item. Skip it when checking for adoption.
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i).gameObject;
                // Skip the placeholder subtree. Use IsChildOf so a placeholder nested deeper than
                // a direct child is still recognized (IsChildOf returns true for self too).
                if (placeholderRenderer != null && placeholderRenderer.transform.IsChildOf(child.transform))
                    continue;
                // Skip the hint anchor subtree — it hosts a UI canvas, not a held item.
                if (hintAnchor != null && hintAnchor.IsChildOf(child.transform))
                    continue;
                HeldItem = child;
                break;
            }

            if (HeldItem == null && initialPrefab != null)
            {
                var inst = Instantiate(initialPrefab, transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                HeldItem = inst;
            }
        }

        public void Place(GameObject item)
        {
            HeldItem = item;
            RefreshHighlightTarget();
        }

        public GameObject Take()
        {
            var item = HeldItem;
            HeldItem = null;
            RefreshHighlightTarget();
            return item;
        }

        public void SetHighlighted(bool on) => SetHighlighted(on, null);

        public void SetHighlighted(bool on, HandCell hoveringHand)
        {
            _highlighted = on;
            RefreshHighlightTarget();

            if (hintLabel == null) return;
            if (on && hoveringHand != null)
                hintLabel.Show(GetHintText(hoveringHand));
            else
                hintLabel.Hide();
        }

        private void RefreshHighlightTarget()
        {
            // Full slot → light up the held item. Empty slot → light up the placeholder.
            var renderers = HeldItem != null
                ? HeldItem.GetComponentsInChildren<Renderer>()
                : (placeholderRenderer != null ? new[] { placeholderRenderer } : System.Array.Empty<Renderer>());

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mat = r.material; // instance, not shared — acceptable for P0
                if (_highlighted)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColorID, highlightEmissive);
                }
                else
                {
                    mat.SetColor(EmissionColorID, Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
            }
        }
    }
}
