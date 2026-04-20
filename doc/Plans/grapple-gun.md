# Grapple Gun — Design

Date: 2026-04-20
Branch: `GrappleGun`
Status: Approved (design phase). Implementation plan pending.

## 1. Problem

The `GrappleGun` script that landed with the Basic Items pass is a placeholder: `Activate()` briefly enables a 1m `LineRenderer` streak from the barrel and ends. It exists to prove the `IActivatable` wiring works — it has no raycast, no anchor attachment, no player movement, no consumption.

The design doc (§2.2.1, §3.1.3, §3.3.1 FSM, §3.3.2) specifies a functional grapple gun: trigger press fires a raycast from the barrel, a rope `LineRenderer` draws to the hit point, and the player's position lerps to the anchor over ~0.5 s, after which the hook is consumed. The storyboard (§3.2, panel "Grappling Gun (Hook)") shows a 10–40 m usage range and a "Distant Rock Anchor" hit target. FSM state `Zipping` is added between `Climbing/Idle` and `Climbing/Idle`.

This spec turns the placeholder into the doc-faithful gadget.

## 2. Goals & Non-Goals

**Goals**
- `Fire()` performs a real raycast from the barrel tip along its forward axis, gated on a max range.
- On a valid hit (collider has a `SurfaceTag`), the player's XR rig lerps to the anchor over ~0.5 s along an ease-out curve.
- During the zip, default locomotion providers (continuous move, turn, teleport) and both ice-pick tip colliders are suspended so the lerp is the sole source of rig motion.
- On arrival, the gun GameObject is destroyed; the owning `HandCell` becomes empty.
- While held, the gun renders a diegetic laser preview (barrel → first-hit or barrel → `maxRange`) so the player can aim in VR without guessing depth.
- Separation of concerns: the gun owns the raycast/preview/rope/lifetime; a new `GrappleLocomotion` MonoBehaviour on the XR rig owns the zip.

**Non-goals**
- Supply-cache refill / pickup from world. The gun is destroyed on use; re-arming is a future system.
- Ammo count, reload, dual-hooks, zip-line modes.
- Pendulum/arc physics during zip. The motion is a direct lerp with an ease curve.
- Mid-zip cancellation. The zip is uninterruptible once started.
- Haptics, SFX, particle effects, hook-travel animation, final art materials.
- Replacement of `ClimbingLocomotion`'s locomotion-provider gating. `GrappleLocomotion` gets its own `locomotionProviders[]` list wired to the same targets.
- HUD overlays, distance indicators, reticles beyond the diegetic barrel laser.

## 3. Visible behavior

### 3.1 Held state (preview)

Each frame the gun is held in a `HandCell` and not mid-zip:
- A laser `LineRenderer` renders from `barrelTip.position` along `barrelTip.forward`.
- A `Physics.Raycast` runs for `maxRange` meters using `hitMask`.
  - **Hit a collider with a `SurfaceTag`**: laser endpoint at `hit.point`, end color `laserValidColor` (green-ish).
  - **Hit a collider without a `SurfaceTag`** or nothing at all: laser endpoint at `barrelTip + forward * maxRange`, end color `laserOutOfRangeColor` (red-ish).

### 3.2 Fire

On `IActivatable.Activate()` (HIC rung 3):
- Re-run the same raycast.
- **Hit + `SurfaceTag`** — start the zip (§3.3).
- **Anything else** — "dry fire": the laser `LineRenderer` briefly pulses (e.g., extends full length then fades) for `dryFireDuration` seconds. Gun is NOT consumed. Player retains it.

### 3.3 Zip

On a valid fire:
- `GrappleLocomotion.StartZip(hit.point, hit.normal, onArrival)` runs.
- Rope `LineRenderer` is enabled on the gun. Endpoint 0 follows `barrelTip` each frame; endpoint 1 is pinned at world-space `hit.point`.
- Any embedded pick is released cleanly. Both picks' tip colliders and swing detectors are disabled for the zip duration.
- Default locomotion providers (continuous move, turn, teleport, etc.) are disabled.
- `xrOrigin.position` is lerped from its start position to `hit.point + hit.normal * surfaceOffset` over `zipDuration` seconds with an ease curve.
- Trigger re-presses during the zip are ignored (the held gun's `_isZipping` flag suppresses re-fire).

### 3.4 Arrival

When the coroutine completes:
- Pick colliders + swing detectors re-enabled.
- Locomotion providers re-enabled.
- `onArrival` callback (provided by the gun) runs: rope disabled, owning `HandCell.Take()` is called to decouple the gun from the cell, then `Destroy(gameObject)` removes the gun. The hand is empty; the player can immediately swing a pick (now that tips are re-enabled).

### 3.5 Stowed state

When the gun is in a `BeltSlot`:
- Laser and rope `LineRenderer`s disabled.
- `Update` short-circuits (no preview raycast).

## 4. Architecture

Two components, one prefab, one scene wire-up.

1. **`GrappleGun.cs`** — extends the existing placeholder. Owns: laser preview, fire raycast, rope visual, `_isZipping` gate, self-destruct callback.
2. **`GrappleLocomotion.cs`** — new scene-level component on the XR rig. Owns: the 0.5 s lerp coroutine, locomotion-provider gating, pick suspension, release-on-zip-start.

The gun finds the locomotion component once per instance via `FindAnyObjectByType<GrappleLocomotion>()` (cached). No static events, no singletons, no script-execution-order dependencies. One-shot callback (`System.Action onArrival`) wired per fire.

This mirrors the existing `ClimbingLocomotion` split: rig-level locomotion behavior lives on the rig; per-item behavior lives on the item.

## 5. Components

### 5.1 `GrappleGun.cs` — modified

Location: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` (existing).
Namespace: `IcePEAK.Gadgets.Items`.

**Serialized fields** — `(new)` flags added in this spec; `(removed)` flags placeholder fields retired.

- `Transform barrelTip` (existing)
- `string displayName = "Grapple Gun"` (existing)
- `LineRenderer streak` (removed — renamed and repurposed as `rope`)
- `float streakLength` (removed — superseded by `maxRange` which bounds both preview and fire)
- `float streakDuration` (removed — superseded by `dryFireDuration` and `GrappleLocomotion.zipDuration`)
- `LineRenderer laser` (new) — preview, driven every frame while held
- `LineRenderer rope` (new) — drawn during zip; disabled otherwise (the renamed `streak` reference is re-wired here)
- `float maxRange = 40f` (new)
- `LayerMask hitMask` (new) — raycast filter; scene-wired to include ClimbableSurface + RockPlatform layers, exclude IcePickTip and player layers
- `float dryFireDuration = 0.15f` (new)
- `Color laserValidColor = new Color(0.2f, 1f, 0.4f)` (new)
- `Color laserOutOfRangeColor = new Color(1f, 0.3f, 0.3f)` (new)

**Runtime state:**
- `bool _isZipping` — set true when a valid `Fire()` call hits; cleared by the `OnArrival` callback. Blocks preview updates and re-fires.
- `bool _isStowed` — set in `OnTransfer` based on target `CellKind`. Preview short-circuits when stowed.
- `GrappleLocomotion _locomotion` — cached on first `Fire()` via `FindAnyObjectByType`.
- `HandCell _owningCell` — cached in `OnTransfer(from, CellKind.Hand)` from `transform.parent.GetComponent<HandCell>()`. Cleared when stowed.
- `Vector3 _ropeAnchor` — world-space rope endpoint during zip.

**Method responsibilities:**

| Method | Responsibility |
|---|---|
| `Activate()` | `=> Fire();` |
| `Update()` | If `_isStowed` or `_isZipping` → skip. Else drive laser preview (raycast, LineRenderer, endpoint color). |
| `LateUpdate()` | If rope enabled → update endpoint 0 to `barrelTip.position`, endpoint 1 stays at `_ropeAnchor`. |
| `Fire()` | Raycast. Hit + SurfaceTag → `_locomotion.StartZip(hit, normal, OnArrival)`, set `_isZipping`, enable rope. Else `StartCoroutine(DryFireFlash())`. |
| `OnArrival()` | Disable rope. `_owningCell?.Take()`. `Destroy(gameObject)`. |
| `OnTransfer(from, to)` | If `to == Hand` → cache `_owningCell` from parent, `_isStowed = false`. Else → `_owningCell = null`, `_isStowed = true`, `laser.enabled = false` (otherwise the stowed gun keeps rendering its last-frame preview line). |

### 5.2 `GrappleLocomotion.cs` — new

Location: `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`.
Namespace: `IcePEAK.Gadgets`.

**Serialized fields:**
- `Transform xrOrigin` — the rig transform to lerp
- `IcePickController leftPick`, `rightPick` — to release and suspend during zip
- `MonoBehaviour[] locomotionProviders` — components to toggle off during zip (same shape as `ClimbingLocomotion`)
- `float zipDuration = 0.5f`
- `float surfaceOffset = 0.5f`
- `AnimationCurve zipEase = AnimationCurve.EaseInOut(0, 0, 1, 1)` — tuned to an ease-out shape in Inspector

**Public API:**
```csharp
public bool IsZipping { get; private set; }
public bool StartZip(Vector3 anchor, Vector3 normal, System.Action onArrival);
```
`StartZip` returns `false` if a zip is already running (rejected). Caller inspects the return value before setting its own `_isZipping` flag. On `true`, the coroutine runs and `onArrival` is invoked exactly once when it completes.

**Zip coroutine:**
1. `IsZipping = true`. Disable `locomotionProviders`. Release any embedded pick (`leftPick.Release()` / `rightPick.Release()` — both are null-safe no-ops if not embedded). Stow both picks via `SetStowed(true)` (disables tip colliders + swing detectors).
2. `start = xrOrigin.position`. `target = anchor + normal * surfaceOffset`.
3. Loop `t` from 0 to `zipDuration`: `xrOrigin.position = Vector3.Lerp(start, target, zipEase.Evaluate(t / zipDuration))`. `yield return null`.
4. Snap to `target` on completion.
5. `SetStowed(false)` on both picks. Re-enable `locomotionProviders`. `IsZipping = false`.
6. `onArrival?.Invoke()`.

### 5.3 `Item_GrappleGun.prefab` — modified

Current structure (from basic-items):
```
Item_GrappleGun       [GrappleGun]
├── Visual
│   ├── Grip
│   └── Barrel
├── BarrelTip
└── Streak            [LineRenderer disabled]
```

Target structure:
```
Item_GrappleGun       [GrappleGun]
├── Visual
│   ├── Grip
│   └── Barrel
├── BarrelTip
├── Laser             [LineRenderer, world-space, 2 points, thin, emissive material]
└── Rope              [LineRenderer, world-space, 2 points, thicker, disabled]
```

Rename `Streak` → `Rope`. Add `Laser` child. Both `LineRenderer`s use world-space positions so the rope endpoint stays planted at the anchor while the rig (and thus the gun) moves during the zip.

Wire the gun's serialized `laser`, `rope`, `barrelTip` fields to the new children.

### 5.4 Scene wiring — `TestScene.unity`

- Add a `GrappleLocomotion` component to the XR Origin GameObject alongside the existing `ClimbingLocomotion`.
- Wire `xrOrigin`, `leftPick`, `rightPick` to the same references `ClimbingLocomotion` uses.
- Wire `locomotionProviders` to the same list of movement provider MonoBehaviours already wired on `ClimbingLocomotion`.
- `Slot_FrontLeft` already has `Item_GrappleGun` as its `initialPrefab` from the basic-items pass. No change needed.

## 6. Control flow

### 6.1 Held + aiming

```
HIC.Update (per frame, each hand)
├── not over a slot, no pick embedded
│   └── rung 3: IActivatable handled on trigger rising edge
└── GrappleGun.Update (same frame)
    ├── _isStowed || _isZipping → return
    └── Physics.Raycast(barrelTip.position, barrelTip.forward, maxRange, hitMask)
        ├── hit + SurfaceTag → laser to hit.point, green
        └── otherwise       → laser full length, red
```

### 6.2 Trigger (fire)

```
HIC.Update detects trigger rising edge, no slot hovered, no embedded pick
→ IActivatable.Activate() → GrappleGun.Fire()
  └── raycast
      ├── hit + SurfaceTag
      │   ├── cache _locomotion (first call)
      │   ├── _locomotion.StartZip(hit.point, hit.normal, OnArrival)
      │   │   ├── false (already zipping) → abort, no side effects
      │   │   └── true (accepted)
      │   │       ├── _isZipping = true
      │   │       ├── rope.enabled = true
      │   │       └── _ropeAnchor = hit.point
      │   └── (coroutine runs on locomotion)
      └── miss / no SurfaceTag → StartCoroutine(DryFireFlash)
```

### 6.3 During zip

```
GrappleGun.Update   → _isZipping guard → no preview work
GrappleGun.LateUpdate → rope endpoint 0 ← barrelTip.position
GrappleLocomotion coroutine → xrOrigin.position lerp
```

### 6.4 Arrival

```
Coroutine finishes
→ un-stow picks
→ re-enable locomotion providers
→ IsZipping = false
→ onArrival.Invoke()
   └── GrappleGun.OnArrival
       ├── rope.enabled = false
       ├── _owningCell?.Take()
       └── Destroy(gameObject)
```

## 7. Edge cases

- **Preview raycast hits an untagged collider** → treated as miss: laser at `maxRange`, red color. Firing at that aim is a dry-fire (no consumption).
- **`FindAnyObjectByType<GrappleLocomotion>()` returns null** (scene misconfiguration) → `Fire()` logs a warning and returns without firing. Gun stays in hand.
- **Fire while a pick is embedded** → `GrappleLocomotion.StartZip` calls `Release()` on both picks unconditionally before suspending them. `Release()` is a no-op when `!IsEmbedded`. `ClimbingLocomotion.Update` sees `IsEmbedded == false` after release and does nothing; the zip lerp is the sole rig motion.
- **Trigger held through arrival** → `HandInteractionController.triggerAction.WasPressedThisFrame()` reads rising edge only; holding the trigger doesn't re-fire. After arrival the hand is empty anyway.
- **Two grapple guns fired simultaneously** (both hands, point-blank) — can't happen in practice (player can only trigger-press once per hand per frame, and `GrappleLocomotion.StartZip` rejects the second with `false`). The rejected gun does NOT set `_isZipping` or enable its rope, so it remains usable afterward. If both were truly fired in the same frame, the first call wins and the second is a no-op.
- **Room-scale drift during zip** — tracking continues layered over `xrOrigin.position`. The lerp overrides `xrOrigin.position` each frame, so drift is additive and small. Acceptable.
- **Framerate spikes** — the coroutine accumulates `t` with `Time.deltaTime`, so a single long frame advances proportionally. No divergence.
- **Landing target inside geometry** — accepted for this iteration. Level design responsibility; no runtime physics check.
- **Gun stowed with `Laser` LineRenderer still showing the last preview** — `Update` short-circuits on `_isStowed`, but the LineRenderer retains its last frame. Fix in `OnTransfer`: explicitly `laser.enabled = false` when stowing.
- **Gun drawn from belt mid-VR-session; `_owningCell` must be re-cached** — handled by `OnTransfer(Hand, BeltSlot → Hand)` reading `transform.parent.GetComponent<HandCell>()` on every Hand-arrival.

## 8. Files touched

**New**
- `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`

**Modified — scripts**
- `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` (preview, fire raycast, rope, self-destruct, `_owningCell` tracking)

**Modified — assets**
- `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (rename `Streak` → `Rope`, add `Laser` child, wire serialized fields)
- `Assets/IcePEAK/Scenes/TestScene.unity` (add `GrappleLocomotion` to XR Origin and wire fields)

## 9. Test plan

Per project norm (CLAUDE.md: no test suite). Unity in-editor play-mode, manual.

| # | Action | Expected |
|---|---|---|
| 1 | Unity compile | No errors or new warnings |
| 2 | Enter Play, look at belt | `Slot_FrontLeft` holds gun; draw it into hand |
| 3 | Hold gun, aim at ice wall within 40 m | Laser visible, endpoint on wall, green color |
| 4 | Aim at sky / beyond 40 m | Laser extends full length, red color |
| 5 | Aim at a non-`SurfaceTag` collider | Laser endpoint on collider, red color |
| 6 | Press trigger aimed at valid ice/rock | Rope draws, rig lerps to offset-from-anchor over ~0.5 s, gun destroyed, hand empty |
| 7 | Press trigger aimed at sky | Dry-fire pulse on laser, gun persists, hand retains gun |
| 8 | Press trigger aimed at untagged collider | Same as #7 |
| 9 | Hold trigger through the zip | No second zip starts; first zip completes normally |
| 10 | Fire with an ice pick embedded | Embedded pick releases at zip start; rig lerps; on arrival, both picks usable again |
| 11 | Observe locomotion providers during zip | Continuous move / snap turn / teleport disabled during zip, re-enabled on arrival |
| 12 | Land on ice face, swing pick immediately | Pick embeds normally |
| 13 | Stow gun (swap with empty slot) | Laser disabled while stowed; re-draw → laser re-enables |
| 14 | Draw gun, swap with other hand's item mid-aim | Laser follows whichever hand now holds gun; previous hand's laser disabled |

## 10. Out of scope (future work)

- **Hook-travel animation** — a pre-lerp "the hook is flying to the anchor" phase (~0.1 s). Nice polish; current design starts the zip instantly.
- **Haptics / SFX** — trigger haptic on fire, low-rumble during zip, landing thump.
- **Supply-cache refill** — a new system spawns/placements for replacement guns.
- **Distance indicator** — storyboard shows a "10–40 m" bar near the barrel. Skipped per design doc §2.4.1 "no HUD."
- **Cancel mechanics** — mid-zip cancellation, velocity preservation on arrival, pendulum swing.
- **Multi-shot / zip-line** — firing a second gun while the first is still attached to create an A-to-B line.
- **Grapple-to-pole** — the `Piton`'s eye tab could become a grappleable feature; requires the Pole system to tag its anchors.
