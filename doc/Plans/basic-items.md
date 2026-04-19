# Basic Items — Design Spec

**Date:** 2026-04-19
**Branch:** `BasicItems`
**Status:** Design approved, pending implementation plan

## 1. Context

The design doc lists four gadgets (grappling gun hook, cold spray, helmet,
poles) that the player holsters on a body-mounted harness. The gadget-belt
system (landed on `main`) defines `IHoldable`, `HandCell`, `BeltSlot`, and
`HandInteractionController` (HIC) — the inventory scaffolding — but no
concrete gadget scripts or prefabs exist yet. Belt slots today fall back to
a `Placeholder_Ring` sphere placeholder.

This spec defines the **first pass** of differentiated gadget scaffolding:
three per-item MonoBehaviours with distinct placeholder visuals, plus a
single-activation-path interface to eliminate double-fire between belt
swaps and item activation.

## 2. Goal & scope

**Goal:** Replace the generic slot placeholders with three distinct,
grabbable, trigger-activatable gadget prefabs that future gameplay systems
(grapple projectile, cold-spray crack-timer, piton anchor) can plug into
without re-architecting input.

### In scope (P0)

- Three item scripts: `GrappleGun`, `ColdSpray`, `Piton` — each a
  MonoBehaviour implementing `IHoldable` and the new `IActivatable`.
- Three prefabs from Unity primitives (no Rigidbody/Collider).
- New `IActivatable` interface.
- One HIC change: add a third priority rung that calls `Activate()` on the
  held item.
- Distinctive placeholder visuals per item (LineRenderer streak / particle
  burst / forward translation pulse).
- `TestScene` wiring: three of the four belt slots start filled with these
  items.

### Out of scope (deferred)

- **Helmet** — the design doc classifies it as worn, not held, so it
  doesn't fit the belt-slot/hand-cell abstraction. Scaffolded when the
  wear-on-head system is designed.
- Gameplay effects: actual grapple projectile/zip, crack-timer extension,
  piton-as-anchor spawn.
- Pickup from world / supply caches.
- Rigidbody/collider integration, SFX, haptics, final art materials.
- Networked/multiplayer considerations.

## 3. Key design decisions

Decisions the brainstorm locked in, with the "why" so future changes can
judge whether the reason still holds:

1. **Scaffolding depth: stub method signatures (Q1-B)** — each script
   declares the eventual gameplay method (`Fire()`, `Spray()`, `Plant()`)
   as an empty body, reflecting the design doc's vocabulary. Commits to
   method names before callers exist; avoids locking down data schemas.

2. **No shared base class (Q2-B)** — each item implements `IHoldable` and
   `IActivatable` directly. Genuinely common concerns (pickup SFX,
   haptics, "consumed" state) aren't defined yet; extracting a base before
   duplication proves real is speculative.

3. **Helmet deferred (Q3-B)** — the design doc splits held vs. worn. The
   belt-slot abstraction is for held items. Helmet belongs to a different
   system.

4. **Trigger input stays in the HIC (Q4/activation-A)** — originally Q4
   proposed each item subscribing to the trigger. That would cause a
   double-fire quirk: pressing trigger to stow a grapple while hovering a
   slot would swap *and* fire the grapple visual in the same frame. We
   resolved this by keeping the HIC as the single trigger subscriber and
   adding a priority rung that dispatches to `IActivatable.Activate()`.

5. **Differentiated visuals (Q5-B)** — each item has a signature visual
   distinct from the others: grapple shoots a line-renderer streak; cold
   spray emits a particle burst; piton does a forward translation pulse
   on its mesh. This matches the original "differentiate each item" ask.

6. **Pole renamed to Piton** — the item class changed from a straight
   stake to a piton (pointed shaft + flat eye tab). The code identifier
   (`Piton`) matches the visual. The design doc still calls the gadget
   category "Poles (Metal Stakes)" — that can be reconciled in a later
   design-doc revision; scope of this spec is code and prefabs.

## 4. Architecture

### 4.1 Input flow

The HIC is the sole subscriber to the trigger action. On every trigger
rising edge, it walks a priority ladder and picks exactly one action:

1. Pick embedded → `IcePickController.Release()`
2. Slot hovered → `ResolveBeltAction(hoveredSlot)` (swap/stow/draw)
3. **(new)** Hand cell holds an `IActivatable` → `activatable.Activate()`
4. Otherwise → no-op

The ladder is mutually exclusive: reaching rung 3 requires no slot
hovered, which is what prevents the double-fire quirk. Items themselves
hold no `InputAction` reference and never subscribe to input.

### 4.2 Interfaces

Items implement two:

- `IHoldable` (existing) — `OnTransfer(CellKind from, CellKind to)` is
  called after the item is reparented into a cell. Scaffolding
  implementation logs the transition; real gameplay plugs in here.
- `IActivatable` (new) — `Activate()` is called by the HIC on rung 3. The
  scaffolding implementation plays the item's placeholder visual.

### 4.3 Visual concurrency

Each item has a private `_isPlaying` flag set true when its visual
coroutine starts and cleared when it ends. Rapid trigger presses during
the visual window are ignored (no coroutine stacking, no visual smear).

## 5. Components

### 5.1 New interface

**`Assets/IcePEAK/Scripts/Gadgets/IActivatable.cs`**

```csharp
namespace IcePEAK.Gadgets {
    public interface IActivatable {
        void Activate();
    }
}
```

### 5.2 New item scripts

All three live in `Assets/IcePEAK/Scripts/Gadgets/Items/`, namespace
`IcePEAK.Gadgets.Items`. Each implements `IHoldable` and `IActivatable`.

**Common skeleton** — `Activate()` satisfies the `IActivatable` contract
and delegates to an item-named method (`Fire` / `Spray` / `Plant`) so the
design-doc vocabulary remains in the public API. The named method holds
the visual; `Activate()` is a one-liner:

```csharp
public class <ItemName> : MonoBehaviour, IHoldable, IActivatable {
    // [SerializeField] refs to child objects that play the visual
    bool _isPlaying;

    public void OnTransfer(CellKind from, CellKind to) {
        Debug.Log($"[<ItemName>] {from} → {to}");
    }

    public void Activate() => <NamedMethod>();

    public void <NamedMethod>() {
        if (_isPlaying) return;
        StartCoroutine(PlayVisual());
    }

    IEnumerator PlayVisual() {
        _isPlaying = true;
        // item-specific visual
        _isPlaying = false;
    }
}
```

**Per-item named methods, fields, and visuals:**

| Script | Named method | Serialized fields | Visual behavior |
|---|---|---|---|
| `GrappleGun` | `Fire()` | `LineRenderer streak`, `Transform barrelTip`, `float streakLength = 1f`, `float streakDuration = 0.2f` | Enable streak, set positions (`barrelTip`, `barrelTip + forward * streakLength`), wait `streakDuration`, disable. |
| `ColdSpray` | `Spray()` | `ParticleSystem mist`, `float burstSeconds = 0.3f` | `mist.Play()`, wait `burstSeconds`, `mist.Stop()`. |
| `Piton` | `Plant()` | `Transform visual`, `float plantDistance = 0.05f`, `float plantDuration = 0.2f` | Lerp `visual.localPosition.z` forward by `plantDistance` over `plantDuration / 2`, lerp back over the other half. |

Future gameplay code can either call `Activate()` through the interface
(uniform) or call `Fire()` / `Spray()` / `Plant()` directly when the
caller already knows the concrete type.

### 5.3 HIC modification

**`HandInteractionController.cs`**, add after the existing rung 2 block,
before the final no-op fallthrough:

```csharp
if (triggerRisingEdge && handCell.HeldItem != null) {
    if (handCell.HeldItem.TryGetComponent<IActivatable>(out var activatable)) {
        Debug.Log($"[HIC {hand}] Activate → {handCell.HeldItem.name}");
        activatable.Activate();
    }
    return; // consume the press — held item wins over no-op
}
```

The `return;` runs even when `TryGetComponent` fails, so a trigger press
while holding a non-activatable item (e.g., a non-embedded ice pick) is
consumed rather than falling to a future rung.

### 5.4 Prefabs

Location: `Assets/IcePEAK/Prefabs/Items/`. All meshes use a shared
`Materials/Item_Placeholder.mat` (neutral light-grey URP Lit). Size budget
per item ≤ `proximityRadius` (0.15 m) in the longest dimension so items
read as "fits the slot."

**`Item_GrappleGun.prefab`** — L-shape pistol.
```
Item_GrappleGun                [empty, GrappleGun script]
├── Visual
│   ├── Grip                   [Cube,     scale (0.03, 0.08, 0.04)]
│   └── Barrel                 [Cylinder, scale (0.02, 0.05, 0.02), Z-rotated 90°]
├── BarrelTip                  [empty at muzzle — streak start]
└── Streak                     [empty, LineRenderer disabled]
```

**`Item_ColdSpray.prefab`** — canister with nozzle.
```
Item_ColdSpray                 [empty, ColdSpray script]
├── Visual
│   ├── Canister               [Cylinder, scale (0.04, 0.06, 0.04)]
│   └── Nozzle                 [Cylinder, scale (0.015, 0.015, 0.015), atop canister]
└── Mist                       [empty, ParticleSystem stopped, conical emitter at nozzle]
```

**`Item_Piton.prefab`** — pointed shaft with eye tab.
```
Item_Piton                     [empty, Piton script]
└── Visual
    ├── Shaft                  [Cylinder, scale (0.012, 0.06, 0.012), Z-rotated 90°]
    └── EyeTab                 [Cube,     scale (0.025, 0.025, 0.008), at shaft's blunt end]
```

All prefab roots are empty GameObjects: no `Rigidbody`, no `Collider`.
They live as transform-parented children of a `HandCell` or `BeltSlot`.

### 5.5 Scene wiring

`TestScene.unity` — the four `BeltSlot` GameObjects have an `initialPrefab`
serialized field (gadget-belt §3.5 pattern). Assignments:

| Slot | `initialPrefab` |
|---|---|
| `Slot_FrontLeft`  | `Item_GrappleGun` |
| `Slot_FrontRight` | `Item_ColdSpray`  |
| `Slot_SideLeft`   | `Item_Piton`      |
| `Slot_SideRight`  | (empty — reserved for empty-slot swap test) |

## 6. Test plan

No automated tests — manual play-mode verification in the editor (project
norm, per `CLAUDE.md`).

| # | Action | Expected |
|---|---|---|
| 1 | Unity compile | No errors, no new warnings |
| 2 | Enter Play, look at belt | 3 filled slots + 1 empty; primitive-shape items visible |
| 3 | Reach to a filled slot, trigger | Item transfers to hand; Console logs `OnTransfer BeltSlot → Hand` |
| 4 | Hold item, trigger away from any slot | Item-specific visual plays; HIC logs `Activate → <item>` |
| 5 | Hold item, hover a slot, trigger | Swap happens, no activation visual during swap (quirk prevention) |
| 6 | Empty hand, trigger | No errors, no visual |
| 7 | Hold non-embedded ice pick, trigger | No errors, no visual (not `IActivatable`) |
| 8 | Rapid-fire trigger on held item | Visuals don't stack (`_isPlaying` guard) |
| 9 | Stow + redraw | Console shows `Hand → BeltSlot` then `BeltSlot → Hand` |

## 7. File manifest

**Added:**
```
Assets/IcePEAK/Scripts/Gadgets/IActivatable.cs
Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs
Assets/IcePEAK/Scripts/Gadgets/Items/ColdSpray.cs
Assets/IcePEAK/Scripts/Gadgets/Items/Piton.cs
Assets/IcePEAK/Prefabs/Items/Materials/Item_Placeholder.mat
Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab
Assets/IcePEAK/Prefabs/Items/Item_ColdSpray.prefab
Assets/IcePEAK/Prefabs/Items/Item_Piton.prefab
```
(plus `.meta` for each)

**Modified:**
```
Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs
Assets/IcePEAK/Scenes/TestScene.unity
```

## 8. Commit plan

1. Add `IActivatable` interface
2. Add three item scripts with `OnTransfer` log + `Activate()` visual stubs
3. Add HIC rung 3
4. Add placeholder material and three item prefabs
5. Wire items into `TestScene` belt slots
