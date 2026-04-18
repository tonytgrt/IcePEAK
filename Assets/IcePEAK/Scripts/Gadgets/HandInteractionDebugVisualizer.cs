using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Per-hand debug lines — one LineRenderer per belt slot.
    /// Line is hidden beyond approachRadius; dim white when in approach range; bright green
    /// when the slot is the current hover target (i.e. would fire on trigger press).
    /// </summary>
    public class HandInteractionDebugVisualizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandCell handCell;
        [SerializeField] private GadgetBelt belt;
        [Tooltip("Sibling controller — used to read the current hover target.")]
        [SerializeField] private HandInteractionController handController;

        [Header("Settings")]
        [SerializeField] private bool debugEnabled = true;
        [Tooltip("Lines appear when hand is within this distance of a slot.")]
        [SerializeField] private float approachRadius = 0.30f;
        [SerializeField] private Color activeColor = new Color(0.2f, 1f, 0.3f, 1f);
        [SerializeField] private Color approachColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private float activeWidth = 0.004f;
        [SerializeField] private float approachWidth = 0.0015f;

        private LineRenderer[] _lines;

        private void Start()
        {
            if (belt == null || belt.Slots == null) return;

            _lines = new LineRenderer[belt.Slots.Length];
            for (int i = 0; i < belt.Slots.Length; i++)
            {
                var go = new GameObject($"DebugLine_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.enabled = false;
                _lines[i] = lr;
            }
        }

        private void Update()
        {
            if (!debugEnabled || _lines == null || handCell == null || belt == null) return;

            Vector3 handPos = handCell.Anchor.position;
            var hovered = handController != null ? handController.CurrentHoveredSlot : null;

            for (int i = 0; i < belt.Slots.Length; i++)
            {
                var slot = belt.Slots[i];
                var lr = _lines[i];
                if (slot == null || lr == null) continue;

                Vector3 slotPos = slot.Anchor.position;
                float dist = Vector3.Distance(handPos, slotPos);

                if (dist > approachRadius)
                {
                    lr.enabled = false;
                    continue;
                }

                lr.enabled = true;
                lr.SetPosition(0, handPos);
                lr.SetPosition(1, slotPos);

                bool isActive = slot == hovered;
                lr.startColor = lr.endColor = isActive ? activeColor : approachColor;
                lr.startWidth = lr.endWidth = isActive ? activeWidth : approachWidth;
            }
        }
    }
}
