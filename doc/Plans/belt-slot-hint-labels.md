# Belt Slot Hint Labels — Design

Date: 2026-04-19
Status: Approved (design phase). Implementation plan pending.

## 1. Problem

When a hand approaches a belt slot, the player cannot tell what the trigger will do. The trigger can `Draw`, `Stow`, or `Swap` items depending on the combined state of the hand and the slot, and there is no visible cue. We need a hovering text label that reveals the imminent action and the item involved.

## 2. Goals & Non-Goals

**Goals**
- While a hand is hovering a belt slot, display a short world-space text label near the slot that describes the exact action the trigger will perform.
- Keep the hint truthful: it must mirror `HandInteractionController.ResolveBeltAction` semantics.
- Design the hint as a reusable mechanism so future pickup sources (e.g., a helmet on a shelf) can adopt it.

**Non-goals**
- No hint for held-item activation (P3 `IActivatable` case) in this pass.
- No hint while the pick is embedded (P1).
- No localization, no animated fades, no disabled/greyed states.
- No change to the belt’s proximity/hover detection logic.

## 3. Visible Behavior

The hint appears only when a hand is hovering a slot (same event that triggers the slot’s highlight). It disappears when the hover ends.

Content depends on the current contents of the hovering hand and the hovered slot:

| `hand.HeldItem` | `slot.HeldItem` | Hint |
|---|---|---|
| null | null | hidden |
| null | X | `Draw {X.DisplayName}` |
| Y | null | `Stow {Y.DisplayName}` |
| Y | X | `Swap {Y.DisplayName} ↔ {X.DisplayName}` |

After a swap/stow/draw the hint immediately recomputes from the new contents, because `ResolveBeltAction` re-calls `SetHighlighted(true, handCell)` at the end of its execution.

The label is anchored to the slot (not the hand), offset ~5 cm above the slot, and billboarded so its face is directed at the HMD.

## 4. Architecture

Four pieces:

1. **`IHintSource` interface** — contract for anything that can show a contextual hint.
2. **`IHoldable.DisplayName`** — new property that supplies the item name used in hint strings.
3. **`BeltSlot`** — implements `IHintSource`, computes the verb from hand/slot state.
4. **`HintLabel` MonoBehaviour + prefab** — reusable world-space Canvas + TMP text with billboard behavior.

Hover detection is intentionally **not** part of the shared contract. Each source type keeps its own detection strategy (belt uses `GadgetBelt.TryGetNearestSlot`; a future helmet pickup might use a trigger volume). The shared contract is only the hint text + anchor.

## 5. Interfaces

### 5.1 `IHoldable` (modified)

```csharp
public interface IHoldable
{
    void OnTransfer(CellKind from, CellKind to);
    string DisplayName { get; }
}
```

Every concrete holdable exposes `[SerializeField] private string displayName = "..."` with a sensible default and returns it from the property. Defaults:

| Component | DisplayName |
|---|---|
| `GrappleGun` | `Grapple Gun` |
| `ColdSpray` | `Cold Spray` |
| `Piton` | `Piton` |
| `IcePickController` | `Ice Pick` |

### 5.2 `IHintSource` (new)

```csharp
namespace IcePEAK.Gadgets
{
    public interface IHintSource
    {
        /// Returns null or empty string to hide the hint.
        string GetHintText(HandCell hand);

        /// World-space anchor the HintLabel attaches to.
        Transform HintAnchor { get; }
    }
}
```

### 5.3 `BeltSlot` (modified)

- Implements `IHintSource`.
- New `[SerializeField] Transform hintAnchor` child (~5 cm above the slot’s existing Anchor).
- New `[SerializeField] HintLabel hintLabel` reference (the instantiated label under the anchor).
- `GetHintText(HandCell hand)` implements the table in §3.
- `SetHighlighted` signature extended to `SetHighlighted(bool highlighted, HandCell hoveringHand = null)`:
  - Retains the existing emissive toggle.
  - When `highlighted && hoveringHand != null`, calls `hintLabel.Show(GetHintText(hoveringHand))`.
  - Otherwise calls `hintLabel.Hide()`.
  - `hintLabel` is allowed to be null (slots that opt out of hints still work).

## 6. `HintLabel` component

### 6.1 Prefab structure

```
HintLabel (empty GO + HintLabel MB)
└── Canvas (Render Mode = World Space, scale ≈ 0.003)
    └── TMP_Text (centered, small padding, optional dark pill background image)
```

### 6.2 Script

```csharp
public class HintLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private GameObject root;

    private Camera _hmd;

    void Awake()
    {
        _hmd = Camera.main;
        root.SetActive(false);
    }

    public void Show(string text)
    {
        if (string.IsNullOrEmpty(text)) { Hide(); return; }
        label.text = text;
        root.SetActive(true);
    }

    public void Hide() => root.SetActive(false);

    void LateUpdate()
    {
        if (!root.activeSelf || _hmd == null) return;
        var dir = transform.position - _hmd.transform.position;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
}
```

Rationale:
- `LateUpdate` for billboarding runs after camera movement, which avoids one-frame jitter in VR.
- `Camera.main` is cached once; the HMD camera is tagged `MainCamera`.
- Hide by toggling `root.SetActive(false)` so the Canvas does no layout/render work when invisible.

## 7. Control flow

### 7.1 Hover change (in `HandInteractionController.Update`)

Before:
```csharp
if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false);
CurrentHoveredSlot = nearest;
if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true);
```

After:
```csharp
if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false, handCell);
CurrentHoveredSlot = nearest;
if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true, handCell);
```

The `handCell` reference is already a field on `HandInteractionController`.

### 7.2 Post-swap refresh (in `HandInteractionController.ResolveBeltAction`)

Change the existing `slot.SetHighlighted(true)` at the end of the method to `slot.SetHighlighted(true, handCell)`. The call flows through `GetHintText`, which reads the now-updated `HeldItem` references on both the slot and the hand.

### 7.3 `OnDisable`

`CurrentHoveredSlot.SetHighlighted(false)` — passing no hand is fine since we’re only hiding.

## 8. Edge cases

- **Both hands hover the same slot:** the slot stores no per-hand state. Whichever hand most recently called `SetHighlighted(true, hand)` determines the text. Acceptable — the conflict is brief and rare.
- **`hintLabel` reference is null on a slot:** `SetHighlighted` short-circuits the hint path. The emissive highlight still works.
- **`Camera.main` is null at startup:** `LateUpdate` early-exits. The label renders with its spawn-time orientation until a camera appears.
- **Display name missing on an item:** the `DisplayName` field has a compile-time default; if a designer clears it, the hint shows `Draw ` / `Stow ` with a trailing space. This is a content bug, not a crash.

## 9. Files touched

**New**
- `Assets/IcePEAK/Scripts/Gadgets/IHintSource.cs`
- `Assets/IcePEAK/Scripts/Gadgets/UI/HintLabel.cs`
- `Assets/IcePEAK/Prefabs/UI/HintLabel.prefab`

**Modified — scripts**
- `Assets/IcePEAK/Scripts/Gadgets/IHoldable.cs` (add `DisplayName`)
- `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` (add `displayName` field)
- `Assets/IcePEAK/Scripts/Gadgets/Items/ColdSpray.cs` (add `displayName` field)
- `Assets/IcePEAK/Scripts/Gadgets/Items/Piton.cs` (add `displayName` field)
- `Assets/IcePEAK/Scripts/IcePick/IcePickController.cs` (add `displayName` field and `DisplayName` property — already implements `IHoldable`)
- `Assets/IcePEAK/Scripts/Gadgets/BeltSlot.cs` (implement `IHintSource`, extend `SetHighlighted` signature, wire hint label)
- `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs` (pass `handCell` into the three `SetHighlighted` calls)

**Modified — assets**
- `Assets/IcePEAK/Prefabs/GadgetBelt.prefab` (add `hintAnchor` + `HintLabel` prefab instance under each of the four `BeltSlot` children; wire `hintLabel` field on each slot)

## 10. Out of scope (future work)

- **P3 activation hint** (`Fire` / `Spray` / `Plant` when holding an `IActivatable` away from the belt) — will likely live on a second `HandHintLabel` owned by `HandInteractionController`.
- **World pickup hint** (e.g., helmet) — implements `IHintSource` and reuses the `HintLabel` prefab. Its policy for a hand-already-full case is decided at that time.
- Fade tween on show/hide.
- Localization.
- Visual design refinement (background pill, outlines, font sizing) — tune in Inspector during implementation.
