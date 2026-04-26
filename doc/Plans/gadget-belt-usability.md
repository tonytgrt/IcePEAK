# Gadget Belt — Usability Pass

**Status:** design approved, ready to plan
**Date:** 2026-04-25
**Related:** `doc/Plans/gadget-belt.md` (P0 belt foundation — already shipped)

---

## 1. Context

The gadget belt shipped per `doc/Plans/gadget-belt.md` works mechanically but is
hard to use in practice. Three compounding issues:

1. **Tiny visuals.** Each empty slot is a Unity built-in sphere at scale 0.04
   (4 cm). The original design called for "wireframe rings" but the prefab
   ended up using small spheres that mostly disappear in peripheral vision.
2. **Tight spread.** As authored in `Assets/IcePEAK/Prefabs/GadgetBelt.prefab`:

   | Slot | x | z |
   |---|---:|---:|
   | Slot_FrontLeft  | -0.062 | +0.169 |
   | Slot_FrontRight | +0.062 | +0.169 |
   | Slot_SideLeft   | -0.156 | +0.090 |
   | Slot_SideRight  | +0.156 | +0.090 |

   Adjacent gaps (front-pair, front-to-same-side-side) are **0.123–0.124 m**.

3. **Overlapping proximity zones.** `proximityRadius = 0.15 m` is *larger* than
   the 0.123 m adjacent gap. There is a band of hand positions where the hand
   is within 15 cm of two slots simultaneously, and `TryGetNearestSlot`
   flip-flops between them on tiny jitter. Wrong-slot grabs are a deterministic
   outcome of the geometry, not bad aim.

This spec fixes all three together with one prefab pass and one small code
change.

## 2. Goal & scope

**Goal:** Reaching for a slot should land on the intended slot reliably,
without the player needing to look down or aim precisely.

### In scope

- Geometry retune — slot positions and `proximityRadius`.
- Visual size bump — keep existing sphere placeholder, increase scale.
- Hysteresis on slot selection — micro-jitter at a boundary doesn't flip the
  hovered slot.

### Out of scope

- Card / silhouette / ring placeholders. Bigger spheres are the cheapest fix;
  if discoverability still feels weak after this pass, escalate to richer
  visuals as a follow-up.
- Capacity changes (still 4 slots).
- Per-hand slot ownership / split belt. Considered and deferred — the
  geometry retune + hysteresis is expected to be sufficient.
- Look-down activation, contextual zoom, haptics, SFX.

## 3. Changes

### 3.1 Geometry (prefab edit only — no code)

**`Assets/IcePEAK/Prefabs/GadgetBelt.prefab`** — update each slot's
`localPosition`:

| Slot | x (was → new) | z (was → new) |
|---|---:|---:|
| Slot_FrontLeft  | -0.062 → **-0.12** | +0.169 → **+0.25** |
| Slot_FrontRight | +0.062 → **+0.12** | +0.169 → **+0.25** |
| Slot_SideLeft   | -0.156 → **-0.18** | +0.090 → **+0.05** |
| Slot_SideRight  | +0.156 → **+0.18** | +0.090 → **+0.05** |

**Resulting adjacent gaps:**

- FrontLeft ↔ FrontRight: 0.124 m → **0.24 m**
- FrontLeft ↔ SideLeft (same side):  0.123 m → **0.21 m**
- FrontRight ↔ SideRight (same side): 0.123 m → **0.21 m**
- SideLeft ↔ SideRight: 0.312 m → 0.36 m

**`proximityRadius` on `GadgetBelt` MonoBehaviour:** 0.15 → **0.10 m**.

With radius 0.10 and smallest adjacent gap 0.21, two slots' proximity zones
no longer overlap at all (2 × 0.10 = 0.20 < 0.21). Selection ambiguity is
removed at the geometric level before hysteresis even applies.

**Reach sanity:** front slots at z = +0.25 are ~25 cm in front of the waist —
a comfortable forward reach when looking down. Side slots at z = +0.05 sit
roughly at the hips, a natural rest position for relaxed arms. Both within
seated/standing reach without leaning.

### 3.2 Visuals (prefab edit only — no code)

Each slot's `Placeholder_Ring` child uses Unity's built-in sphere mesh at
`localScale = 0.04`. Change to **0.08** on all four slots — visible diameter
roughly 4 cm → 8 cm.

No mesh, material, or `BeltSlot` highlight code changes. The existing
`placeholderRenderer` field already targets this sphere; the highlight
emissive pathway in `BeltSlot.SetHighlighted` keeps working unchanged.

If 8 cm spheres still feel under-discoverable on-device, the next escalation
(deferred from this spec) is replacing the sphere with a card or lit ring
mesh — still routed through the same `placeholderRenderer` field, no script
change required.

### 3.3 Selection hysteresis (one code change)

The only code edit. Goal: once a slot is hovered, it stays hovered until a
competitor is *meaningfully* closer.

**`GadgetBelt.cs`** — add a tunable next to `proximityRadius`:

```csharp
[Tooltip("Sticky bias for the currently-hovered slot. A competing slot must " +
         "be at least this much closer (in meters) than the current one to " +
         "take over. Kills boundary jitter without making the hand feel laggy.")]
[SerializeField] private float stickyBias = 0.025f;

public float StickyBias => stickyBias;
```

`TryGetNearestSlot` stays pure — it returns the raw nearest. Hysteresis lives
where the per-hand hover state already lives.

**`HandInteractionController.cs::Update()`** — wrap the existing
`belt.TryGetNearestSlot(...)` / hover-transfer block:

```csharp
belt.TryGetNearestSlot(handCell.Anchor.position, out var rawNearest);

BeltSlot effectiveNearest = rawNearest;
if (CurrentHoveredSlot != null
    && rawNearest != null
    && rawNearest != CurrentHoveredSlot)
{
    Vector3 handPos = handCell.Anchor.position;
    float currentDist    = Vector3.Distance(handPos, CurrentHoveredSlot.Anchor.position);
    float competitorDist = Vector3.Distance(handPos, rawNearest.Anchor.position);

    // Keep current slot only while it's still inside its own proximity zone —
    // once the hand has clearly left, drop the bias and use the raw nearest.
    if (currentDist <= belt.ProximityRadius
        && competitorDist > currentDist - belt.StickyBias)
    {
        effectiveNearest = CurrentHoveredSlot;
    }
}

if (effectiveNearest != CurrentHoveredSlot)
{
    if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(false, handCell);
    CurrentHoveredSlot = effectiveNearest;
    if (CurrentHoveredSlot != null) CurrentHoveredSlot.SetHighlighted(true, handCell);
}
```

**Why this isn't input smoothing:** the hand's tracked position itself is not
filtered — only the *slot decision* is biased. The hand stays 1:1 with the
controller; only the "which slot are we considering hovered" answer is sticky.

**Edge cases:**

| Situation | Behavior |
|---|---|
| Hand outside all proximity zones | `rawNearest = null` → `effectiveNearest = null`, hover drops. |
| Hand entering belt from nothing (first contact) | `CurrentHoveredSlot = null` → bias doesn't apply, raw nearest wins. |
| Current slot's hand-distance > `proximityRadius` | Bias drops, raw nearest wins (prevents "stuck on a slot the hand has clearly left"). |
| Current slot becomes null mid-frame (defensive) | Bias doesn't apply, raw nearest wins. |

## 4. Tunables

| Field | Default (was → new) | Where | Notes |
|---|---:|---|---|
| `proximityRadius` | 0.15 → **0.10** m | `GadgetBelt` | Smaller — non-overlapping zones with new geometry. |
| `stickyBias` | — → **0.025** m | `GadgetBelt` (new) | Distance margin to overcome to switch off the current hovered slot. |
| Front slot x | ±0.062 → **±0.12** | belt prefab | |
| Front slot z | +0.169 → **+0.25** | belt prefab | |
| Side slot x | ±0.156 → **±0.18** | belt prefab | |
| Side slot z | +0.090 → **+0.05** | belt prefab | |
| Placeholder sphere scale | 0.04 → **0.08** | belt prefab (each `Placeholder_Ring`) | |

All values above are starting points. Final dial-in happens during
on-device play-testing — the XR Simulator gives only an approximation of
reach feel.

## 5. Manual test plan

In `Assets/IcePEAK/Scenes/TestScene.unity` via XR Simulation or on Quest 3.

| # | Behavior | Verification |
|---|---|---|
| 1 | Slots are visibly larger | At Play, sphere placeholders are clearly larger; visible from waist height with a glance, not a stare. |
| 2 | Front pair reachable independently | Hand at front-left, drift toward front-right — hover transfers cleanly with a clear no-hover gap in the middle. No flip-flop. |
| 3 | Front-side pair reachable independently | Hand at front-left, drift sideways to side-left — hover transfers cleanly with a gap. (The worst flip-flop case before.) |
| 4 | Hysteresis stick — micro-jitter | Park hand near a slot boundary so two slots are roughly equidistant. Add small hand tremor: hover stays on the originally selected slot, doesn't flicker. |
| 5 | Hysteresis release — clear move | From the same parked position, deliberately move hand ~3+ cm toward the competitor: hover transfers. |
| 6 | Hysteresis exit — leave proximity | Hover a slot, move hand far away: hover drops cleanly (no "stuck" state). |
| 7 | Front slots reachable when looking down | Comfortable forward reach to z = +0.25; doesn't require leaning. |
| 8 | Side slots reachable at rest | Arm at natural rest near the hip lands within side-slot proximity. |
| 9 | Draw / stow / swap unchanged | Existing `gadget-belt.md` §7 tests #6, #7, #8 all still pass. |
| 10 | Climbing priority unchanged | Embed pick + grip near a slot → pick releases, belt inert (`gadget-belt.md` §7 #9). |

## 6. Implementation order

1. `GadgetBelt.cs` — add `stickyBias` field + `StickyBias` getter. Compile,
   verify console clean.
2. `HandInteractionController.cs` — replace the existing
   `TryGetNearestSlot`+hover-transfer block with the hysteresis version
   from §3.3. Compile, verify console clean.
3. Belt prefab — bump each `Placeholder_Ring`'s `localScale` from 0.04 to
   0.08. Visual check in Scene view.
4. Belt prefab — update each slot's `localPosition` per §3.1.
5. Belt prefab — set `proximityRadius` to 0.10 on the `GadgetBelt`
   component.
6. Run manual test plan §5 in `TestScene`.
7. Tune values on-device if needed; commit.

## 7. Open questions

None for this spec. Deferred:

- Whether 8 cm spheres are visible enough on-device, or whether the next
  pass needs cards/rings/silhouettes.
- Whether 4 slots is enough as the gadget roster grows past
  grapple/spray/piton/drone — capacity is a separate spec.
