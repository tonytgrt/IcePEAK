# Grapple Gun Feature Bundle — Design

**Date:** 2026-05-06
**Scope:** Four related changes to the grappling gun + zip flow:
1. Cooldown gate + diegetic indicator + dry-fire haptic buzz
2. Aim-ray color-swap fix (debug, not redesign)
3. Constant wall-hang duration on arrival, decoupled from travel time
4. Auto-swap grapple gun → ice pick on arrival

The four are bundled because they share files (`GrappleGun.cs`, `GrappleLocomotion.cs`, the gun prefab) and ship together as one playable iteration.

---

## Files touched

- `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`
- `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`
- `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs` (extract a shared swap helper)
- `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (new child for cooldown indicator; LineRenderer material check)
- New file: `Assets/IcePEAK/Scripts/Gadgets/GrappleCooldownIndicator.cs` (small per-prefab component)

No changes to ice-pick, climbing, or surface code.

---

## Section 1 — Constant wall-hang on arrival (Feature 3)

### Current bug

`GrappleLocomotion.ZipRoutine` uses `while (elapsed < zipDuration && !_cancelRequested)` where `elapsed` accumulates from zip start. Hang time at the wall = `zipDuration - travelTime`, which varies with grapple distance. At max range (40m / 20 m/s = 2.0s travel) and `zipDuration = 2.0s`, hang time is 0s.

### Desired behavior

Constant `wallHangDuration` measured from arrival, independent of travel time.

### Change

Split the single `zipDuration` field into two:

```csharp
[SerializeField] private float maxTravelDuration = 2.0f;   // hard cap on travel-phase
[SerializeField] private float wallHangDuration = 2.0f;    // fixed hang once arrived
```

Remove `zipDuration`.

### New `ZipRoutine` flow

1. **Travel phase** — existing constant-speed loop, but its termination changes:
   - exits on arrival (`xrOrigin.position == end` after step), OR
   - `travelElapsed >= maxTravelDuration`, OR
   - `_cancelRequested`, OR
   - `embedArmed && (leftEmbedded || rightEmbedded)` (existing early-out)
2. **Arrival branch** — *only if* the travel phase exited via "arrival" (not cancel, not embed-shortcut, not travel timeout):
   - Invoke a new arrival hook on the gun (`OnZipArrivedAtWall()`) so the auto-swap runs before the hang (Section 2).
   - Set the grapple anchor on `ClimbingLocomotion` using the gun's transform — the gun is parented to the rig, so the anchor's delta cancels rig motion (existing `SetGrappleAnchor` semantics). `GrappleLocomotion` needs the gun transform; pass it as a parameter to `StartZip` (cleanest) so the locomotion code doesn't reach back into the gun.
   - Run a `wallHangDuration` timer. Single `while (hangElapsed < wallHangDuration) yield return null;` loop with no other exit conditions (non-cancellable hang).
   - Clear the grapple anchor on `ClimbingLocomotion`.
3. **Cleanup** — re-enable locomotion providers, set `_isZipping = false`, fire `onArrival`.

### Climbing-anchor lifecycle ownership

The existing `EnterClimbing`/`ExitClimbing` methods on `GrappleGun` (and the `_isClimbing` flag, and the trigger-released-during-climb watch in `Update`) become **dead code** with this design — the anchor is owned end-to-end by `GrappleLocomotion`. Delete them.

### Cancel semantics

- Mid-travel cancel (`CancelZip()` while in transit): travel phase ends, **no hang phase runs**, `onArrival` still fires for cleanup. Existing rope/laser cleanup in `GrappleGun.OnArrival` is unaffected.
- Mid-hang cancel: not supported. The hang is a hard timer. (Player can no longer hold the trigger to cancel anyway, because Feature 4 swaps the gun out at arrival.)

### Tunable defaults

- `maxTravelDuration = 2.0f` (matches `maxRange / zipSpeed = 40 / 20`)
- `wallHangDuration = 2.0f`

---

## Section 2 — Auto-swap grapple gun → ice pick on arrival (Feature 4)

### Trigger point

Called at the *start* of the arrival branch in `ZipRoutine` (Section 1, step 2), before the hang timer runs. The player has the pick in-hand for the full hang window so they can swing it into the wall.

Implementation: `GrappleLocomotion.ZipRoutine` invokes a new public method on the gun, `OnZipArrivedAtWall()`, before setting the grapple anchor. That method runs the auto-swap. The existing `onArrival` callback (passed via `StartZip`) remains and fires after the hang completes, for cleanup. Two distinct callbacks, two distinct moments — no overloading.

### Selection logic (option B from brainstorming)

In a new helper (`BeltAutoSwap.SwapGunForIcePick(GrappleGun gun)` or similar — exact location TBD during planning):

1. Walk up from the grapple gun's transform to find the owning `HandCell`.
2. Locate the `GadgetBelt` via `FindAnyObjectByType<GadgetBelt>()` (one-time lookup, cached).
3. Iterate `belt.Slots`; pick the **first slot** whose `HeldItem` has an `IcePickController` component. If none, no-op.
4. Reuse the snapshot-take-place dance from `HandInteractionController.ResolveBeltAction`. Refactor that private method into a static helper (`BeltSwap.Swap(ICell a, ICell b)`) so it's callable from both grip-press and auto-arrival paths without duplicating code.

### Edge cases (all handled by no-op)

- No ice pick on belt → gun stays in hand; hang continues normally; player has whatever the off-hand holds.
- Both picks are already in hands → no pick on belt → no-op.
- `IFixedInSlot` items are skipped naturally (we only match `IcePickController`).

### Interaction with `IHoldable.OnTransfer`

- Gun's `OnTransfer(Hand → BeltSlot)`: existing logic stows it (laser/rope off, `_isStowed = true`, `_isClimbing` cleared). Already correct.
- Pick's `OnTransfer(BeltSlot → Hand)`: currently empty. Acceptable — picks behave correctly when re-parented.

### No new fields on `GrappleGun`

The auto-swap is driven by existing references; no new SerializeField on the gun itself.

---

## Section 3 — Cooldown + diegetic indicator + dry-fire haptic (Feature 1)

### Cooldown gate

In `GrappleGun`:

```csharp
[SerializeField] private float cooldownDuration = 3.0f;
private float _cooldownEndTime;   // Time.time at which gun is ready
public bool IsOnCooldown => Time.time < _cooldownEndTime;
public float CooldownProgress01 => cooldownDuration <= 0f
    ? 1f
    : Mathf.Clamp01(1f - (_cooldownEndTime - Time.time) / cooldownDuration);
```

`Fire()` early-out is extended:

```csharp
if (_isStowed || _isZipping || _isDryFiring || IsOnCooldown) return;
```

Cooldown starts **only on a successful zip**, immediately after `_locomotion.StartZip(...)` returns true:

```csharp
_cooldownEndTime = Time.time + cooldownDuration;
```

Dry-fires do **not** start the cooldown (chosen explicitly — lets the player keep aiming/scanning without being locked out).

### Cooldown lifecycle interactions

- Default `cooldownDuration = 3.0f`. With max travel 2s + hang 2s = 4s grappled, the gun is ready before the player drops off the wall. Player can re-fire as soon as they drop. Tunable.
- `OnTransfer(_, BeltSlot)`: should the cooldown reset when the gun is stowed? **No** — cooldown is per-gun absolute time; stowing doesn't bypass it. (Acceptable: stowing during cooldown still leaves the gun on cooldown when re-drawn.)

### Diegetic indicator

New small component `GrappleCooldownIndicator` on the gun prefab:

- `[SerializeField] GrappleGun gun;` reference (sibling on the same prefab).
- `[SerializeField] Renderer fillRenderer;` — the indicator's mesh renderer.
- `[SerializeField] string fillProperty = "_Fill";` — float property name.
- `[SerializeField] string colorProperty = "_BaseColor";` — for ready-state blip.
- Each `Update`:
  - Read `gun.CooldownProgress01`.
  - Apply via `MaterialPropertyBlock` (avoids leaking shared material instance).
  - On the frame `progress` crosses 1.0 from below, kick a one-off scale punch coroutine (e.g. `localScale 1.0 → 1.2 → 1.0` over 0.2s) to signal "ready."

Prefab work:

- New child GameObject under the gun, e.g. `BarrelCooldownStrip`. Mesh: thin cylinder/quad along the barrel.
- Material: URP Unlit (vertex-color-aware or with `_Fill` and `_BaseColor` properties). Existing project shaders preferred; a tiny custom shader is acceptable if needed.

This is a polish layer on top of the gate. **The cooldown gate ships even if the visual is incomplete.**

### Dry-fire haptic

In `GrappleGun`:

```csharp
[SerializeField] private float dryFireHapticAmplitude = 0.5f;
[SerializeField] private float dryFireHapticDuration = 0.1f;
[SerializeField] private UnityEngine.XR.Interaction.Toolkit.Haptics.HapticImpulsePlayer leftHaptics;
[SerializeField] private UnityEngine.XR.Interaction.Toolkit.Haptics.HapticImpulsePlayer rightHaptics;
```

Resolved per-hand the same way `ResolveHandTriggerAction` resolves trigger refs (walk parent hierarchy for "Left"/"Right"). Cached on draw (`OnTransfer` → Hand).

In `StartDryFire`:

```csharp
_activeHaptics?.SendHapticImpulse(dryFireHapticAmplitude, dryFireHapticDuration);
```

Fires alongside the existing red-flash coroutine. Visuals + haptic land together.

---

## Section 4 — Aim-ray color swap fix (Feature 2)

### Status

Debug/fix item, not a redesign. The existing `LateUpdate` logic in `GrappleGun` already does what's wanted:

```csharp
bool validHit = Physics.Raycast(...) && hit.collider.GetComponentInParent<SurfaceTag>() != null;
Color color = validHit ? laserValidColor : laserOutOfRangeColor;
laser.startColor = color;
laser.endColor = color;
```

User reports the laser is "always one color." Almost certainly a material/shader wiring issue on the prefab.

### Investigation steps (in order)

1. Open `Item_GrappleGun.prefab`. Inspect the laser `LineRenderer`'s assigned material.
2. Check the material's shader. If it's URP `Lit` or any shader that doesn't sample vertex colors, that's the bug — `LineRenderer.startColor`/`endColor` write to vertex colors only, which Lit ignores.
3. Confirm by inspecting the `laserValidColor` and `laserOutOfRangeColor` Inspector values are visibly distinct.

### Fix paths (pick whichever is cleaner)

- **Material swap:** assign a URP `Unlit` material that samples vertex colors. This is the standard fix and requires no code change.
- **Property-based color:** if the project standardizes on `_BaseColor`-driven materials, update `GrappleGun.LateUpdate` to write color via `MaterialPropertyBlock` on the LineRenderer instead of (or in addition to) `startColor`/`endColor`.

No code change is committed without first reproducing the symptom and confirming the cause.

---

## Testing

No automated test infrastructure is wired up (per CLAUDE.md). Verification is in-editor and on-device:

1. **Cooldown gate** — fire grapple, confirm second fire is rejected for ~3s after first. Check `Debug.Log` or temporary on-screen value of `IsOnCooldown` if visual indicator isn't ready yet.
2. **Cooldown visual** — confirm strip fills 0→1 over `cooldownDuration` and "blips" on ready.
3. **Dry-fire haptic** — aim at sky, pull trigger, confirm a buzz on the firing hand. Confirm full-cooldown pulls (during cooldown) do NOT buzz (they're gated before `Fire()` body).
4. **Aim-ray color** — point at ice surface (green), point at empty sky (red), point at non-tagged geometry (red). Confirm color actually changes in-editor.
5. **Wall-hang constancy** — fire at a 5m target; time the hang from arrival to drop. Fire at a 35m target; time again. Should both be `wallHangDuration` ± frame jitter.
6. **Auto-swap** — start with grapple gun in right hand, ice pick on right belt slot. Fire and arrive. Confirm right hand now holds the pick, slot now holds the gun. Repeat for left hand. Repeat with no pick on belt — confirm no-op.
7. **Combined flow** — fire with right hand → arrive → pick auto-equips → swing pick into wall → embed → hang timer expires → grapple anchor releases → player is now climbing on the embedded pick. Confirm seamless.

XR Simulation (`Assets/XR/Resources/XRSimulationRuntimeSettings.asset`) is sufficient for items 1–4. On-device verification recommended for 5–7 due to physical swing dynamics.

---

## Open questions deferred to implementation planning

- Exact location of the shared swap helper (`BeltSwap` static? method on `HandInteractionController`? new `BeltAutoSwap` MonoBehaviour?).
- Whether the cooldown indicator material reuses an existing shader or needs a new one.
- Final tunable values once playtested.
