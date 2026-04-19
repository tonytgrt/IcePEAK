# Belt Slot Hint Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a world-space text label near a hovered belt slot that describes the action the trigger will perform (`Draw X`, `Stow Y`, `Swap Y ↔ X`), hidden when both hand and slot are empty.

**Architecture:** A new `IHintSource` interface decouples hint text from hover detection. `BeltSlot` implements it and computes the verb from hand/slot contents. A reusable `HintLabel` MonoBehaviour + prefab renders world-space TMP text with HMD billboarding and can be reused by future pickup sources.

**Tech Stack:** Unity 6.3 LTS (6000.3.13f1), URP 17.3, XR Interaction Toolkit 3.3.1, TextMeshPro (bundled via `com.unity.ugui`), C# 10.

**Project conventions that shape this plan:**
- No test framework. "Passing" means Unity compiles without errors and the play-mode checklist in Task 8 succeeds.
- Unity generates `.meta` files on editor focus; both `.cs` and `.cs.meta` must land in the same commit.
- `.unity` / `.prefab` / `.asset` are YAML — prefer Unity Editor (or Unity MCP) authoring; hand-edit only when editor access is blocked.
- Commits follow conventional style (`feat(scope): …`, `docs(scope): …`). **No AI attribution, no `Co-Authored-By: Claude` trailers.**
- Do not stage recurring dirty files: `ProjectSettings/ShaderGraphSettings.asset`, `ProjectSettings/URPProjectSettings.asset`, `Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab`.

**Reference:** Design spec at `doc/Plans/belt-slot-hint-labels.md`. Read §5 (interfaces) and §7 (control flow) before starting.

---

## Task 0: Create branch

**Files:** None — git-only.

- [ ] **Step 1: Confirm starting state**

```bash
git status --short
git log -1 --oneline
```

Expected: working tree has only the long-running dirty files listed above. Current branch `BasicItems`, HEAD at `2827678 docs(gadgets): design for belt slot hint labels` or newer.

- [ ] **Step 2: Create and switch to `HintLabels`**

```bash
git checkout -b HintLabels
```

- [ ] **Step 3: Verify branch**

```bash
git branch --show-current
```

Expected output: `HintLabels`.

---

## Task 1: Add `DisplayName` to `IHoldable` and all implementers

**Why atomic:** adding a property to `IHoldable` is a breaking change; every implementer must get the property in the same commit or the project will not compile.

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/IHoldable.cs`
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/ColdSpray.cs`
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/Piton.cs`
- Modify: `Assets/IcePEAK/Scripts/IcePick/IcePickController.cs`

- [ ] **Step 1: Extend `IHoldable` with `DisplayName`**

Replace the contents of `Assets/IcePEAK/Scripts/Gadgets/IHoldable.cs` with:

```csharp
namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Implemented by any item that can live in a HandCell or BeltSlot.
    /// OnTransfer is called once per transfer, after reparenting is complete.
    /// DisplayName is the human-readable label used by hint UI.
    /// </summary>
    public interface IHoldable
    {
        void OnTransfer(CellKind from, CellKind to);
        string DisplayName { get; }
    }
}
```

- [ ] **Step 2: Implement `DisplayName` on `GrappleGun`**

In `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`, add one `[SerializeField]` field after the `streakDuration` field and one property before `OnTransfer`.

Add this block immediately after `[SerializeField] private float streakDuration = 0.2f;`:

```csharp
        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;
```

- [ ] **Step 3: Implement `DisplayName` on `ColdSpray`**

In `Assets/IcePEAK/Scripts/Gadgets/Items/ColdSpray.cs`, add after `[SerializeField] private float burstSeconds = 0.3f;`:

```csharp
        [Header("Hint")]
        [SerializeField] private string displayName = "Cold Spray";

        public string DisplayName => displayName;
```

- [ ] **Step 4: Implement `DisplayName` on `Piton`**

In `Assets/IcePEAK/Scripts/Gadgets/Items/Piton.cs`, add after `[SerializeField] private float plantDuration = 0.2f;`:

```csharp
        [Header("Hint")]
        [SerializeField] private string displayName = "Piton";

        public string DisplayName => displayName;
```

- [ ] **Step 5: Implement `DisplayName` on `IcePickController`**

In `Assets/IcePEAK/Scripts/IcePick/IcePickController.cs`, add after `[SerializeField] private float triggerReleaseThreshold = 0.5f;` (end of `[Header("Input")]` block):

```csharp
    [Header("Hint")]
    [SerializeField] private string displayName = "Ice Pick";

    public string DisplayName => displayName;
```

- [ ] **Step 6: Trigger Unity compile**

Have Unity refresh (either focus the editor or run `mcp__UnityMCP__refresh_unity`). Then read the console:

```
mcp__UnityMCP__read_console
```

Expected: no compile errors. If any implementer of `IHoldable` elsewhere was missed, the console will point to it.

- [ ] **Step 7: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/IHoldable.cs \
        Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs \
        Assets/IcePEAK/Scripts/Gadgets/Items/ColdSpray.cs \
        Assets/IcePEAK/Scripts/Gadgets/Items/Piton.cs \
        Assets/IcePEAK/Scripts/IcePick/IcePickController.cs
git commit -m "feat(gadgets): add DisplayName to IHoldable for hint UI"
```

---

## Task 2: Add `IHintSource` interface

**Files:**
- Create: `Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs`
- Create: `Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs.meta` (Unity generates)

- [ ] **Step 1: Create the interface file**

Write `Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs`:

```csharp
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Implemented by any object that can show a contextual hint when a hand
    /// hovers near it. The hint text is computed from the hand's current
    /// contents, so the source can return different strings depending on
    /// whether the hand is empty, holding the same item, or holding a different
    /// item.
    /// </summary>
    public interface IHintSource
    {
        /// <summary>
        /// Returns the hint string for the given hovering hand.
        /// Return null or empty to hide the hint.
        /// </summary>
        string GetHintText(HandCell hand);

        /// <summary>
        /// World-space transform the hint label should anchor to.
        /// </summary>
        Transform HintAnchor { get; }
    }
}
```

- [ ] **Step 2: Trigger Unity compile**

```
mcp__UnityMCP__refresh_unity
mcp__UnityMCP__read_console
```

Expected: no compile errors. A `.cs.meta` file is generated alongside.

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs \
        Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs.meta
git commit -m "feat(gadgets): add IHintSource interface"
```

---

## Task 3: Add `HintLabel` MonoBehaviour

**Files:**
- Create: `Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs`
- Create: `Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs.meta` (Unity generates)

- [ ] **Step 1: Create the script file**

Write `Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs`:

```csharp
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
            _hmd = Camera.main;
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
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private void LateUpdate()
        {
            if (root == null || !root.activeSelf) return;
            if (_hmd == null) _hmd = Camera.main;
            if (_hmd == null) return;

            var dir = transform.position - _hmd.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}
```

- [ ] **Step 2: Trigger Unity compile**

```
mcp__UnityMCP__refresh_unity
mcp__UnityMCP__read_console
```

Expected: no compile errors. If `using TMPro;` fails, verify `com.unity.ugui` is in `Packages/manifest.json` (it is — TMP ships with it in Unity 6).

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs \
        Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs.meta
git commit -m "feat(gadgets): add HintLabel world-space text component"
```

---

## Task 4: Create `HintLabel.prefab`

**Files:**
- Create: `Assets/IcePEAK/Prefabs/UI/HintLabel.prefab`
- Create: `Assets/IcePEAK/Prefabs/UI/HintLabel.prefab.meta` (Unity generates)

**Prefab structure:**
```
HintLabel (empty GO; HintLabel component)
└── Canvas (World Space, scale 0.003)
    └── Text (TextMeshProUGUI)
```

- [ ] **Step 1: Create the prefab root via Unity MCP**

Open a temporary scene or use `manage_gameobject create` to build the hierarchy. The following `execute_code` block creates the full prefab and saves it:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 1. Root.
var root = new GameObject("HintLabel");

// 2. Canvas child.
var canvasGO = new GameObject("Canvas");
canvasGO.transform.SetParent(root.transform, worldPositionStays: false);
var canvas = canvasGO.AddComponent<Canvas>();
canvas.renderMode = RenderMode.WorldSpace;
canvasGO.AddComponent<CanvasScaler>();
canvasGO.AddComponent<GraphicRaycaster>();
var canvasRT = canvasGO.GetComponent<RectTransform>();
canvasRT.sizeDelta = new Vector2(200f, 50f);
canvasRT.localScale = Vector3.one * 0.003f;

// 3. Text child.
var textGO = new GameObject("Text");
textGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);
var text = textGO.AddComponent<TextMeshProUGUI>();
text.text = "Hint";
text.fontSize = 36f;
text.alignment = TextAlignmentOptions.Center;
text.color = Color.white;
text.enableWordWrapping = false;
var textRT = text.rectTransform;
textRT.anchorMin = Vector2.zero;
textRT.anchorMax = Vector2.one;
textRT.offsetMin = Vector2.zero;
textRT.offsetMax = Vector2.zero;

// 4. HintLabel component, wired to Canvas (root) and Text (label).
var hint = root.AddComponent<IcePEAK.Gadgets.UI.HintLabel>();
var so = new SerializedObject(hint);
so.FindProperty("label").objectReferenceValue = text;
so.FindProperty("root").objectReferenceValue = canvasGO;
so.ApplyModifiedPropertiesWithoutUndo();

// 5. Save as prefab.
if (!AssetDatabase.IsValidFolder("Assets/IcePEAK/Prefabs/UI"))
{
    AssetDatabase.CreateFolder("Assets/IcePEAK/Prefabs", "UI");
}
var prefabPath = "Assets/IcePEAK/Prefabs/UI/HintLabel.prefab";
PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
Object.DestroyImmediate(root);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

Debug.Log($"[HintLabel] saved to {prefabPath}");
```

Run via `mcp__UnityMCP__execute_code`.

Expected console line: `[HintLabel] saved to Assets/IcePEAK/Prefabs/UI/HintLabel.prefab`.

- [ ] **Step 2: Verify the prefab compiles and the wiring stuck**

```
mcp__UnityMCP__read_console
```

Expected: no errors or warnings about missing references.

Optional sanity check — open the prefab in Unity and confirm:
- Root `HintLabel` has a `HintLabel` component.
- `HintLabel.label` points to the Text.
- `HintLabel.root` points to the Canvas.
- Canvas Render Mode = World Space, scale (0.003, 0.003, 0.003).

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Prefabs/UI/HintLabel.prefab \
        Assets/IcePEAK/Prefabs/UI/HintLabel.prefab.meta \
        Assets/IcePEAK/Prefabs/UI.meta
git commit -m "feat(gadgets): add HintLabel world-space UI prefab"
```

(If `Assets/IcePEAK/Prefabs/UI.meta` is not present on disk, drop it from the `git add` list — Unity may have generated it under a sibling folder.)

---

## Task 5: Modify `BeltSlot` to implement `IHintSource`

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/BeltSlot.cs`

- [ ] **Step 1: Add the new fields and interface to `BeltSlot`**

In `Assets/IcePEAK/Scripts/Gadgets/BeltSlot.cs`:

Change the class declaration on line 16 from:

```csharp
    public class BeltSlot : MonoBehaviour, ICell
```

to:

```csharp
    public class BeltSlot : MonoBehaviour, ICell, IHintSource
```

Add this `using` at the top of the file (below `using UnityEngine;`):

```csharp
using IcePEAK.Gadgets.UI;
```

After the existing `highlightEmissive` field declaration (line 25), add:

```csharp
        [Header("Hint")]
        [Tooltip("Empty transform placed ~5cm above the slot. Labels anchor here.")]
        [SerializeField] private Transform hintAnchor;

        [Tooltip("Reusable HintLabel instance that actually displays the text. May be null if this slot has no hint.")]
        [SerializeField] private HintLabel hintLabel;
```

- [ ] **Step 2: Implement the `IHintSource` members**

After the `Kind` property (line 29), add:

```csharp
        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        public string GetHintText(HandCell hand)
        {
            var slotItem = HeldItem;
            var handItem = hand != null ? hand.HeldItem : null;

            if (handItem == null && slotItem == null) return null;

            string slotName = GetDisplayName(slotItem);
            string handName = GetDisplayName(handItem);

            if (handItem == null) return $"Draw {slotName}";
            if (slotItem == null) return $"Stow {handName}";
            return $"Swap {handName} ↔ {slotName}";
        }

        private static string GetDisplayName(GameObject go)
        {
            if (go == null) return string.Empty;
            return go.TryGetComponent<IHoldable>(out var h) ? h.DisplayName : go.name;
        }
```

- [ ] **Step 3: Extend `SetHighlighted` with an optional hand arg**

Replace the existing `SetHighlighted` method (lines 72–76) with:

```csharp
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
```

The single-arg overload preserves the existing interface for any caller that doesn't know about the hand (e.g., `OnDisable` paths).

- [ ] **Step 4: Trigger Unity compile**

```
mcp__UnityMCP__refresh_unity
mcp__UnityMCP__read_console
```

Expected: no compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/BeltSlot.cs
git commit -m "feat(gadgets): BeltSlot computes and routes hint text"
```

---

## Task 6: Thread the hand into `HandInteractionController.SetHighlighted` calls

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs`

- [ ] **Step 1: Update the hover-change block**

In `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs`, replace the block on lines 38–43:

```csharp
            if (nearest != CurrentHoveredSlot)
            {
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false);
                CurrentHoveredSlot = nearest;
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true);
            }
```

with:

```csharp
            if (nearest != CurrentHoveredSlot)
            {
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false, handCell);
                CurrentHoveredSlot = nearest;
                if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true, handCell);
            }
```

- [ ] **Step 2: Update the post-swap refresh in `ResolveBeltAction`**

Replace line 95:

```csharp
            slot.SetHighlighted(true);
```

with:

```csharp
            slot.SetHighlighted(true, handCell);
```

- [ ] **Step 3: Leave `OnDisable` alone**

Confirm `OnDisable` still calls `CurrentHoveredSlot.SetHighlighted(false)` (the single-arg overload on `BeltSlot` handles it). No change required.

- [ ] **Step 4: Trigger Unity compile**

```
mcp__UnityMCP__refresh_unity
mcp__UnityMCP__read_console
```

Expected: no compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs
git commit -m "feat(gadgets): HIC passes hand into BeltSlot.SetHighlighted for hint lookup"
```

---

## Task 7: Wire `GadgetBelt.prefab` — add hintAnchor + HintLabel per slot

**Context:** The belt has 4 `BeltSlot` children: `Slot_FrontLeft`, `Slot_SideLeft`, `Slot_FrontRight`, `Slot_SideRight`. Each needs a `HintAnchor` child at local `(0, 0.05, 0)` with a `HintLabel` prefab instance under it, then the slot's `hintAnchor` and `hintLabel` fields must be wired.

**Files:**
- Modify: `Assets/IcePEAK/Prefabs/GadgetBelt.prefab`

- [ ] **Step 1: Open the GadgetBelt prefab for editing**

Use Unity MCP to open the prefab in isolation (so the scene is not touched):

```
mcp__UnityMCP__execute_menu_item  args={"menu_path": "Assets/Open", "target": "Assets/IcePEAK/Prefabs/GadgetBelt.prefab"}
```

If the above is not available, use `manage_prefabs` to enter prefab-edit mode on `Assets/IcePEAK/Prefabs/GadgetBelt.prefab`.

- [ ] **Step 2: Script-add the HintAnchor + HintLabel instance under each slot and wire the fields**

Run via `mcp__UnityMCP__execute_code`:

```csharp
using UnityEditor;
using UnityEngine;
using IcePEAK.Gadgets;
using IcePEAK.Gadgets.UI;

const string beltPath = "Assets/IcePEAK/Prefabs/GadgetBelt.prefab";
const string hintLabelPath = "Assets/IcePEAK/Prefabs/UI/HintLabel.prefab";

var root = PrefabUtility.LoadPrefabContents(beltPath);
var hintLabelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hintLabelPath);

var slots = root.GetComponentsInChildren<BeltSlot>(includeInactive: true);
Debug.Log($"[GadgetBelt] found {slots.Length} BeltSlot children");

foreach (var slot in slots)
{
    // Skip if the anchor already exists (idempotent re-run).
    var existing = slot.transform.Find("HintAnchor");
    Transform anchor = existing;
    if (anchor == null)
    {
        var anchorGO = new GameObject("HintAnchor");
        anchorGO.transform.SetParent(slot.transform, worldPositionStays: false);
        anchorGO.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        anchorGO.transform.localRotation = Quaternion.identity;
        anchor = anchorGO.transform;
    }

    // Instantiate HintLabel under the anchor, if not already present.
    HintLabel labelInstance = anchor.GetComponentInChildren<HintLabel>(includeInactive: true);
    if (labelInstance == null)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(hintLabelPrefab, anchor);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        labelInstance = inst.GetComponent<HintLabel>();
    }

    // Wire the serialized fields on the slot.
    var so = new SerializedObject(slot);
    so.FindProperty("hintAnchor").objectReferenceValue = anchor;
    so.FindProperty("hintLabel").objectReferenceValue = labelInstance;
    so.ApplyModifiedPropertiesWithoutUndo();

    Debug.Log($"[GadgetBelt] wired {slot.name}: anchor={anchor.name}, label={labelInstance.name}");
}

PrefabUtility.SaveAsPrefabAsset(root, beltPath);
PrefabUtility.UnloadPrefabContents(root);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Debug.Log("[GadgetBelt] saved");
```

Expected console (in order):
- `[GadgetBelt] found 4 BeltSlot children`
- 4× `[GadgetBelt] wired Slot_… : anchor=HintAnchor, label=HintLabel`
- `[GadgetBelt] saved`

- [ ] **Step 3: Verify the diff**

```bash
git diff --stat Assets/IcePEAK/Prefabs/GadgetBelt.prefab
```

Expected: the prefab gains inserted lines (new GameObject / Transform / PrefabInstance / MonoBehaviour / CanvasRenderer blocks per slot) and the `hintAnchor` / `hintLabel` fields on each `BeltSlot` MonoBehaviour block change from `{fileID: 0}` to real fileIDs.

- [ ] **Step 4: Commit**

```bash
git add Assets/IcePEAK/Prefabs/GadgetBelt.prefab
git commit -m "feat(gadgets): add HintAnchor + HintLabel instances to belt slots"
```

---

## Task 8: Play-mode verification

This task is **not executable by an agent**; it requires the user in VR.

- [ ] **Step 1: Start Unity play mode in `Assets/IcePEAK/Scenes/TestScene.unity`** with a Quest 3 connected (Link/AirLink) or XR Simulation.

- [ ] **Step 2: Run through this checklist**

| # | Scenario | Expected |
|---|---|---|
| 1 | Hover empty hand over slot containing GrappleGun | Label shows `Draw Grapple Gun` |
| 2 | Hover empty hand over slot containing ColdSpray | Label shows `Draw Cold Spray` |
| 3 | Hover empty hand over slot containing Piton | Label shows `Draw Piton` |
| 4 | Draw GrappleGun, re-hover empty slot | Label shows `Stow Grapple Gun` |
| 5 | Hold GrappleGun, hover slot containing Piton | Label shows `Swap Grapple Gun ↔ Piton` |
| 6 | Hover empty hand over the empty slot (`Slot_SideRight`) | Label is hidden |
| 7 | Move hand away from any slot | Label is hidden on the previously-hovered slot |
| 8 | Rotate head/body while hovering | Label always faces the HMD (billboarded) |
| 9 | Draw, then swap, then stow in rapid succession | Label refreshes after every action to match current contents |
| 10 | Embed an ice pick in a wall while holding it; trigger while embedded | Label does not appear (P1 overrides) |

- [ ] **Step 3: File any bugs found**

Any failing row becomes its own follow-up (or, if the fix is small, a hot-patch on this branch before merge).

- [ ] **Step 4: Clean up task tracker**

Mark the plan complete. Branch is ready for merge once the checklist passes.

---

## Post-merge follow-ups (explicitly out of scope here)

- Hand-anchored hint for P3 `IActivatable` activation (`Fire` / `Spray` / `Plant`).
- World-pickup `IHintSource` implementations (helmet, etc.) — they reuse `HintLabel.prefab` unchanged.
- Visual polish: background pill, outline, fade tween on show/hide.
- Localization.
