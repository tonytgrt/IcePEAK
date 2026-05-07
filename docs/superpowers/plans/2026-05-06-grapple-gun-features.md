# Grapple Gun Feature Bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship four grapple gun changes as one playable iteration: (1) cooldown gate + diegetic indicator + dry-fire haptic, (2) aim-ray color-swap fix, (3) constant wall-hang duration on arrival, (4) auto-swap grapple gun → ice pick on arrival.

**Architecture:** Move climbing-anchor lifecycle ownership from `GrappleGun` into `GrappleLocomotion`, splitting the zip routine into a travel phase (with a hard cap) and a fixed-duration arrival/hang phase. Extract the existing belt swap mechanic into a reusable static helper `BeltSwap`, and call it from a new `OnZipArrivedAtWall` hook on the gun. The gun gains a cooldown gate, a diegetic indicator component, and a dry-fire haptic. The aim-ray color bug is a prefab-material fix, not a code change.

**Tech Stack:** Unity 6.3 LTS (`6000.3.13f1`), URP 17.3, XR Interaction Toolkit 3.3.1, new Input System 1.19. C# scripts compile into `Assembly-CSharp` (no `.asmdef`). No automated test runner — verification is in-editor + on-device per `CLAUDE.md`. Compile-check workflow: Unity MCP `refresh_unity` followed by `read_console` to catch errors before manually entering Play mode.

**Spec:** `docs/superpowers/specs/2026-05-06-grapple-gun-features-design.md`

**Commit policy:** No `Co-Authored-By: Claude` trailers, no AI markers (per user memory).

---

## File Structure

| File | Responsibility | Action |
| --- | --- | --- |
| `Assets/IcePEAK/Scripts/Gadgets/BeltSwap.cs` | Static helper: generic two-cell swap; lookups for hand owning a transform and first belt slot containing an ice pick. | Create |
| `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs` | Per-hand belt interaction. Calls `BeltSwap.Swap` instead of inlining. | Modify |
| `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs` | Owns full zip lifecycle: travel phase, arrival branch, hang timer, climbing-anchor lifecycle. | Modify |
| `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` | Aim/fire/cooldown/dry-fire/haptic. Climbing-anchor responsibilities removed. New `OnZipArrivedAtWall` hook for auto-swap. | Modify |
| `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleCooldownIndicator.cs` | Diegetic cooldown visual. Reads `GrappleGun.CooldownProgress01`, drives a `MaterialPropertyBlock` and a one-shot scale punch on ready. | Create |
| `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` | Add `BarrelCooldownStrip` child with indicator material; verify/replace LineRenderer material; wire haptics references. | Modify |

---

## Task 1: Extract `BeltSwap` static helper from `HandInteractionController`

**Goal:** Move the snapshot-take-place-place dance into a shared helper so the auto-swap in Task 4 can reuse it. Pure refactor — behavior must be identical to before.

**Files:**
- Create: `Assets/IcePEAK/Scripts/Gadgets/BeltSwap.cs`
- Modify: `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs`

- [ ] **Step 1: Create `BeltSwap.cs`**

Write `Assets/IcePEAK/Scripts/Gadgets/BeltSwap.cs` with this exact content:

```csharp
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Shared helpers for moving items between cells (hands and belt slots).
    /// Used by HandInteractionController for grip-driven swap/draw/stow and by
    /// GrappleGun for auto-swap on grapple arrival.
    /// </summary>
    public static class BeltSwap
    {
        /// <summary>
        /// Snapshots both cells, empties them, and re-places each item into the
        /// other cell. Either cell may be empty (draw or stow). No-op if both are
        /// empty. Caller is responsible for IFixedInSlot guards.
        /// </summary>
        public static void Swap(ICell a, ICell b)
        {
            if (a == null || b == null) return;
            var aItem = a.HeldItem;
            var bItem = b.HeldItem;
            if (aItem == null && bItem == null) return;

            a.Take();
            b.Take();

            if (bItem != null) PlaceInto(a, bItem, b.Kind);
            if (aItem != null) PlaceInto(b, aItem, a.Kind);
        }

        /// <summary>
        /// Reparents <paramref name="item"/> into <paramref name="cell"/>, resets
        /// its local pose, registers it via Place(), and fires
        /// IHoldable.OnTransfer(from → cell.Kind) on the item if applicable.
        /// </summary>
        public static void PlaceInto(ICell cell, GameObject item, CellKind from)
        {
            if (cell == null || item == null) return;
            item.transform.SetParent(cell.Anchor, worldPositionStays: false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            cell.Place(item);
            var holdable = item.GetComponent<IHoldable>();
            holdable?.OnTransfer(from, cell.Kind);
        }

        /// <summary>
        /// Walks parent chain from <paramref name="descendant"/> up looking for a
        /// HandCell. Returns null if none found.
        /// </summary>
        public static HandCell FindOwningHand(Transform descendant)
        {
            for (var t = descendant; t != null; t = t.parent)
            {
                if (t.TryGetComponent<HandCell>(out var hand))
                    return hand;
            }
            return null;
        }

        /// <summary>
        /// Returns the first slot in <paramref name="belt"/> whose held item has
        /// an IcePickController component, or null if none.
        /// </summary>
        public static BeltSlot FindFirstSlotWithIcePick(GadgetBelt belt)
        {
            if (belt == null || belt.Slots == null) return null;
            foreach (var slot in belt.Slots)
            {
                if (slot == null) continue;
                var held = slot.HeldItem;
                if (held == null) continue;
                if (held.GetComponent<IcePickController>() == null) continue;
                return slot;
            }
            return null;
        }
    }
}
```

- [ ] **Step 2: Replace inlined logic in `HandInteractionController`**

In `Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs`, replace the existing `ResolveBeltAction` method and delete the `PlaceInto` helper. The replacement:

```csharp
        /// <summary>
        /// Unified swap/stow/draw. Delegates to BeltSwap.Swap; preserves the
        /// IFixedInSlot opt-out so slot-locked gadgets (e.g. the drone) can
        /// claim grip-press for themselves.
        /// </summary>
        private void ResolveBeltAction(BeltSlot slot)
        {
            var slotItem = slot.HeldItem;
            if (handCell.HeldItem == null && slotItem == null) return;
            if (slotItem != null && slotItem.GetComponent<IFixedInSlot>() != null) return;

            BeltSwap.Swap(handCell, slot);

            // Held item vs placeholder may have changed — re-evaluate highlight target.
            slot.SetHighlighted(true, handCell);
        }
```

Delete the entire existing `PlaceInto` static method (it now lives on `BeltSwap`).

- [ ] **Step 3: Compile-check**

Use Unity MCP:
1. `mcp__UnityMCP__refresh_unity` to trigger a domain reload.
2. `mcp__UnityMCP__read_console` with `types: ["error"]` and `levels: ["error"]` to confirm zero errors.

Expected: no compile errors. If `IcePickController` is reported missing, double-check `BeltSwap.cs` namespace and that `IcePickController` is still global (no `using` needed).

- [ ] **Step 4: Manual smoke test (in-editor)**

Open `Assets/IcePEAK/Scenes/Main.unity` (or `TestScene.unity` — whichever currently has the belt + grapple gun set up). Press Play. With your hand:
- Hover a belt slot → confirm the slot highlights and a hint text appears.
- Grip-press over a slot containing an item with the hand empty → confirm draw works.
- Grip-press over an empty slot with a held item → confirm stow works.
- Grip-press over a slot with an item while holding a different item → confirm swap works.

If any of those regresses, revert and investigate.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/BeltSwap.cs \
        Assets/IcePEAK/Scripts/Gadgets/BeltSwap.cs.meta \
        Assets/IcePEAK/Scripts/Gadgets/HandInteractionController.cs
git commit -m "Extract BeltSwap static helper from HandInteractionController"
```

(`*.meta` is auto-generated by Unity on the first refresh; include it.)

---

## Task 2: Split `zipDuration` into `maxTravelDuration` + `wallHangDuration`; move climbing-anchor ownership into `GrappleLocomotion`

**Goal:** Travel phase has a hard cap; arrival triggers a fixed-duration wall-hang independent of travel time. `GrappleLocomotion` owns the full zip lifecycle including the climbing-anchor.

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`

This task changes the public `StartZip` signature. The gun-side caller is updated in **Task 3** — the project will not compile cleanly between tasks 2 and 3. Treat tasks 2 and 3 as a coupled pair (commit at the end of task 3).

- [ ] **Step 1: Replace the entire body of `GrappleLocomotion.cs`**

Overwrite `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs` with:

```csharp
using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Rig-level zipline locomotion. Lives on the XR Origin alongside
    /// <c>ClimbingLocomotion</c>. Owns the full zip lifecycle:
    ///   1. Travel phase — moves the rig at constant speed toward a surface
    ///      anchor. Caps at <see cref="maxTravelDuration"/>.
    ///   2. Arrival phase — once travel succeeds, fires <c>onArrived</c>
    ///      (gun does its auto-swap), then anchors the rig via
    ///      <c>ClimbingLocomotion.SetGrappleAnchor</c> for a fixed
    ///      <see cref="wallHangDuration"/>.
    ///   3. Cleanup — re-enables locomotion providers, fires <c>onComplete</c>.
    ///
    /// While the zip is running:
    ///   - Default locomotion providers (move, turn, teleport) are disabled.
    ///   - Both ice picks are released so they can't drag through the old anchor.
    ///   - Additional <see cref="StartZip"/> calls are rejected.
    ///
    /// The arrival branch is skipped on cancel, on travel timeout, and on
    /// early-embed (a pick that swung into the arriving surface) — the player
    /// is already anchored via the pick in that last case.
    /// </summary>
    public class GrappleLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;
        [Tooltip("ClimbingLocomotion on XR Origin — used to anchor the rig during the wall-hang.")]
        [SerializeField] private ClimbingLocomotion climbingLocomotion;

        [Header("Default Locomotion Providers")]
        [Tooltip("Components to disable while zipping — move, turn, teleport, etc. Re-enabled on completion.")]
        [SerializeField] private MonoBehaviour[] locomotionProviders;

        [Header("Tunables")]
        [Tooltip("Constant travel speed during the zip (m/s).")]
        [SerializeField] private float zipSpeed = 20f;
        [Tooltip("Hard cap on travel-phase duration. If the rig hasn't arrived in this many seconds, the zip ends without a wall-hang.")]
        [SerializeField] private float maxTravelDuration = 2.0f;
        [Tooltip("Fixed seconds the rig hangs at the wall after a successful arrival, independent of how long travel took.")]
        [SerializeField] private float wallHangDuration = 2.0f;
        [Tooltip("Meters to stop the gun's nozzle short of the surface along the rope line.")]
        [SerializeField] private float surfaceOffset = 0.1f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the rope distance that must be traveled before an ice pick embed is allowed to end the zip early.")]
        [SerializeField] private float embedArmFraction = 0.8f;

        public bool IsZipping => _isZipping;

        private bool _isZipping;
        private bool _cancelRequested;

        public void CancelZip() => _cancelRequested = true;

        /// <summary>
        /// Begin a zip. The rig is translated so that <paramref name="pullPoint"/>
        /// (typically the gun's barrel tip at fire time) ends up <c>surfaceOffset</c>
        /// meters short of <paramref name="anchor"/> along the rope line.
        /// </summary>
        /// <param name="anchor">World-space target point.</param>
        /// <param name="pullPoint">Gun-tip position snapshot used for offset math.</param>
        /// <param name="climbAnchor">Transform passed to ClimbingLocomotion during the wall-hang. Must be parented to the rig so its delta cancels rig motion (matches existing climbing-anchor semantics).</param>
        /// <param name="onArrived">Fires once at the start of the arrival phase, before the wall-hang begins. Skipped on cancel/timeout/early-embed.</param>
        /// <param name="onComplete">Fires once at the end of the zip, regardless of how it ended.</param>
        /// <returns><c>false</c> if a zip is already running — callers should not start any rope visuals in that case.</returns>
        public bool StartZip(Vector3 anchor, Vector3 pullPoint, Transform climbAnchor,
                             System.Action onArrived, System.Action onComplete)
        {
            if (_isZipping) return false;
            if (xrOrigin == null) return false;

            StartCoroutine(ZipRoutine(anchor, pullPoint, climbAnchor, onArrived, onComplete));
            return true;
        }

        private IEnumerator ZipRoutine(Vector3 anchor, Vector3 pullPoint, Transform climbAnchor,
                                       System.Action onArrived, System.Action onComplete)
        {
            _isZipping = true;
            _cancelRequested = false;

            SetLocomotionProviders(false);
            // Detach any currently-embedded picks so the player doesn't drag
            // through the old anchor. Picks stay live (not stowed) during the
            // zip so the off-hand can swing one into the arriving surface.
            if (leftPick != null) leftPick.Release();
            if (rightPick != null) rightPick.Release();

            Vector3 start = xrOrigin.position;
            // Offset back along the rope line so the nozzle lands a fixed
            // distance short of the surface along the aim axis, regardless of
            // surface orientation.
            Vector3 ropeDir = anchor - pullPoint;
            Vector3 nozzleLanding = ropeDir.sqrMagnitude > 1e-6f
                ? anchor - ropeDir.normalized * surfaceOffset
                : anchor;
            Vector3 end = start + (nozzleLanding - pullPoint);
            float totalDist = Vector3.Distance(start, end);

            // --- Travel phase ---
            float travelElapsed = 0f;
            bool arrived = false;
            bool earlyEmbed = false;
            while (travelElapsed < maxTravelDuration && !_cancelRequested)
            {
                float progress = totalDist > 1e-4f
                    ? Vector3.Distance(start, xrOrigin.position) / totalDist
                    : 1f;
                bool embedArmed = progress >= embedArmFraction;

                bool leftEmbedded = leftPick != null && leftPick.IsEmbedded;
                bool rightEmbedded = rightPick != null && rightPick.IsEmbedded;

                if (embedArmed && (leftEmbedded || rightEmbedded))
                {
                    // Off-hand swung into the arriving wall — skip the hang.
                    earlyEmbed = true;
                    break;
                }

                if (!embedArmed)
                {
                    // Premature embed during the lockout — release so the pick
                    // doesn't stay stuck in a wall we'll fly past.
                    if (leftEmbedded) leftPick.Release();
                    if (rightEmbedded) rightPick.Release();
                }

                // Constant-speed travel toward the landing.
                Vector3 toEnd = end - xrOrigin.position;
                float stepDist = zipSpeed * Time.deltaTime;
                if (toEnd.sqrMagnitude <= stepDist * stepDist)
                {
                    xrOrigin.position = end;
                    arrived = true;
                    break;
                }
                else
                {
                    xrOrigin.position += toEnd.normalized * stepDist;
                }

                travelElapsed += Time.deltaTime;
                yield return null;
            }

            // --- Arrival branch ---
            // Only run hang phase if travel succeeded — not on cancel, not on
            // early-embed (player's already anchored via pick), not on timeout.
            if (arrived && !_cancelRequested && !earlyEmbed)
            {
                onArrived?.Invoke();

                if (climbingLocomotion != null && climbAnchor != null)
                    climbingLocomotion.SetGrappleAnchor(climbAnchor);

                float hangElapsed = 0f;
                while (hangElapsed < wallHangDuration)
                {
                    hangElapsed += Time.deltaTime;
                    yield return null;
                }

                if (climbingLocomotion != null)
                    climbingLocomotion.SetGrappleAnchor(null);
            }

            // --- Cleanup ---
            SetLocomotionProviders(true);
            _isZipping = false;
            onComplete?.Invoke();
        }

        private void SetLocomotionProviders(bool enabled)
        {
            if (locomotionProviders == null) return;
            foreach (var p in locomotionProviders)
            {
                if (p != null) p.enabled = enabled;
            }
        }
    }
}
```

- [ ] **Step 2: Note inspector wiring required after Task 3 compiles**

The new `[SerializeField] ClimbingLocomotion climbingLocomotion` field must be wired on the `GrappleLocomotion` component on `XR Origin (XR Rig)` in the scene. This is done after Task 3 makes the project compile. Note this for the Task 3 manual verification step.

(No commit yet — the project does not compile until Task 3 finishes. Do not refresh Unity in isolation; it would log compile errors against the now-stale `GrappleGun` caller.)

---

## Task 3: Update `GrappleGun` for new arrival hook and remove climbing-anchor dead code

**Goal:** Adopt the new `StartZip` signature with split `onArrived`/`onComplete` callbacks. Wire `OnZipArrivedAtWall()` to do the auto-swap (Feature 4). Delete `EnterClimbing`/`ExitClimbing`/`_isClimbing` and the gun's `climbingLocomotion` field — those responsibilities have moved to `GrappleLocomotion`. Project should compile after this step.

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`

- [ ] **Step 1: Replace the body of `GrappleGun.cs`**

Overwrite `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` with the following. Notable changes vs. the existing file:
- Removed `climbingLocomotion` SerializeField
- Removed `_isClimbing`, `EnterClimbing`, `ExitClimbing`
- Removed climbing-trigger-watch in `Update`
- Renamed/split `OnArrival` → `OnZipArrivedAtWall` (auto-swap) + `OnZipComplete` (cleanup)
- New: `_belt` lazy-resolved field (used by `OnZipArrivedAtWall`)
- New: `Fire()` passes `transform` as `climbAnchor`
- (The cooldown gate, indicator hooks, and dry-fire haptic land in tasks 4–6 — keep them out of this task to keep the diff focused.)

```csharp
using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets.Items
{
    /// <summary>
    /// Grapple gun. Held in a hand, it projects a diegetic laser forward
    /// from the barrel while idle — green when the laser would hit a
    /// <see cref="SurfaceTag"/> collider within <see cref="maxRange"/>,
    /// red otherwise. Activate (trigger) raycasts from the barrel:
    /// on hit, dispatches to <see cref="GrappleLocomotion"/> to zip the
    /// rig to the surface; on miss, plays a brief red dry-fire flash.
    /// On a successful arrival the gun is auto-swapped for an ice pick
    /// from the belt (if one is stowed there) so the player can swing it
    /// into the wall during the wall-hang. Climbing-anchor lifecycle is
    /// owned by <see cref="GrappleLocomotion"/>.
    /// </summary>
    public class GrappleGun : MonoBehaviour, IHoldable, IActivatable
    {
        [Header("Visual refs (wired on the prefab)")]
        [SerializeField] private LineRenderer laser;
        [SerializeField] private LineRenderer rope;
        [SerializeField] private Transform barrelTip;

        [Header("Raycast")]
        [Tooltip("Maximum grapple distance (meters).")]
        [SerializeField] private float maxRange = 40f;
        [Tooltip("Layers the grapple raycast hits. Leave as Everything unless specific layers need to be excluded.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Dry-fire")]
        [Tooltip("Duration of the red miss flash before returning to live laser preview.")]
        [SerializeField] private float dryFireDuration = 0.15f;

        [Header("Laser colors")]
        [SerializeField] private Color laserValidColor = new Color(0.2f, 1f, 0.4f);
        [SerializeField] private Color laserOutOfRangeColor = new Color(1f, 0.3f, 0.3f);

        [Header("Input")]
        [Tooltip("XRI Left Interaction/Activate Value")]
        [SerializeField] private UnityEngine.InputSystem.InputActionReference leftTriggerAction;
        [Tooltip("XRI Right Interaction/Activate Value")]
        [SerializeField] private UnityEngine.InputSystem.InputActionReference rightTriggerAction;
        [SerializeField] private float triggerThreshold = 0.5f;

        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;

        private bool _isStowed = true;
        private bool _isZipping;
        private bool _isDryFiring;
        private GrappleLocomotion _locomotion;
        private GadgetBelt _belt;
        private Vector3 _zipAnchor;
        private UnityEngine.InputSystem.InputActionReference _activeTriggerAction;

        private bool TriggerHeld => (_activeTriggerAction?.action?.ReadValue<float>() ?? 0f) >= triggerThreshold;

        public void OnTransfer(CellKind from, CellKind to)
        {
            _isStowed = (to == CellKind.BeltSlot);

            if (to == CellKind.Hand)
                _activeTriggerAction = ResolveHandTriggerAction();

            if (_isStowed)
            {
                if (laser != null) laser.enabled = false;
                if (rope != null) rope.enabled = false;
            }
        }

        private UnityEngine.InputSystem.InputActionReference ResolveHandTriggerAction()
        {
            for (var t = transform.parent; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return leftTriggerAction;
                if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return rightTriggerAction;
            }
            return rightTriggerAction;
        }

        private void OnEnable()
        {
            leftTriggerAction?.action?.Enable();
            rightTriggerAction?.action?.Enable();
        }

        private void Update()
        {
            if (_isStowed) return;

            // Cancel mid-zip if trigger released. OnZipComplete still fires.
            if (_isZipping && !TriggerHeld)
                _locomotion?.CancelZip();
        }

        public void Activate() => Fire();

        public void Fire()
        {
            if (_isStowed || _isZipping || _isDryFiring) return;
            if (barrelTip == null) return;

            if (!TryResolveLocomotion())
            {
                Debug.LogWarning("[GrappleGun] GrappleLocomotion not found in scene — dry-firing.");
                StartDryFire();
                return;
            }

            if (Physics.Raycast(barrelTip.position, barrelTip.forward, out RaycastHit hit,
                                maxRange, hitMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<SurfaceTag>() != null)
            {
                _zipAnchor = hit.point;

                if (!_locomotion.StartZip(_zipAnchor, barrelTip.position, transform,
                                          OnZipArrivedAtWall, OnZipComplete)) return;

                _isZipping = true;
                if (laser != null) laser.enabled = false;
                if (rope != null)
                {
                    rope.positionCount = 2;
                    rope.SetPosition(0, barrelTip.position);
                    rope.SetPosition(1, _zipAnchor);
                    rope.enabled = true;
                }
            }
            else
            {
                StartDryFire();
            }
        }

        private void LateUpdate()
        {
            if (barrelTip == null) return;

            if (rope != null && _isZipping)
            {
                rope.SetPosition(0, barrelTip.position);
                rope.SetPosition(1, _zipAnchor);
            }

            if (laser == null) return;

            if (_isStowed || _isZipping)
            {
                laser.enabled = false;
                return;
            }

            Vector3 origin = barrelTip.position;
            Vector3 dir = barrelTip.forward;

            if (_isDryFiring)
            {
                laser.positionCount = 2;
                laser.SetPosition(0, origin);
                laser.SetPosition(1, origin + dir * maxRange);
                laser.startColor = laserOutOfRangeColor;
                laser.endColor = laserOutOfRangeColor;
                laser.enabled = true;
                return;
            }

            bool validHit = Physics.Raycast(origin, dir, out RaycastHit hit,
                                            maxRange, hitMask, QueryTriggerInteraction.Ignore)
                            && hit.collider.GetComponentInParent<SurfaceTag>() != null;

            Vector3 end = validHit ? hit.point : origin + dir * maxRange;
            Color color = validHit ? laserValidColor : laserOutOfRangeColor;

            laser.positionCount = 2;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);
            laser.startColor = color;
            laser.endColor = color;
            laser.enabled = true;
        }

        private void StartDryFire()
        {
            StartCoroutine(DryFireFlash());
        }

        private IEnumerator DryFireFlash()
        {
            _isDryFiring = true;
            yield return new WaitForSeconds(dryFireDuration);
            _isDryFiring = false;
        }

        /// <summary>
        /// Fires at the start of the arrival phase, before the wall-hang begins.
        /// Auto-swaps this gun for an ice pick on the belt (Feature 4) so the
        /// player has a pick in hand for the full hang window. No-op if no pick
        /// is on the belt or no owning hand can be resolved.
        /// </summary>
        private void OnZipArrivedAtWall()
        {
            var hand = BeltSwap.FindOwningHand(transform);
            if (hand == null) return;

            if (_belt == null) _belt = FindAnyObjectByType<GadgetBelt>();
            if (_belt == null) return;

            var slot = BeltSwap.FindFirstSlotWithIcePick(_belt);
            if (slot == null) return;

            BeltSwap.Swap(hand, slot);
        }

        /// <summary>
        /// Fires at the end of the zip lifecycle (whether arrival ran or not).
        /// </summary>
        private void OnZipComplete()
        {
            _isZipping = false;
            if (rope != null) rope.enabled = false;
        }

        private bool TryResolveLocomotion()
        {
            if (_locomotion != null) return true;
            _locomotion = FindAnyObjectByType<GrappleLocomotion>();
            return _locomotion != null;
        }
    }
}
```

- [ ] **Step 2: Compile-check**

1. `mcp__UnityMCP__refresh_unity` to trigger reload.
2. `mcp__UnityMCP__read_console` filtered for errors.

Expected: zero errors. If `BeltSwap` is unresolved, Task 1 didn't land cleanly — re-check. If `GrappleLocomotion.StartZip` signature mismatch is reported, the new signature in Task 2 hasn't been saved.

- [ ] **Step 3: Wire the new `climbingLocomotion` reference on `GrappleLocomotion`**

In the editor:
1. Open the scene that hosts the active gameplay setup (`Assets/IcePEAK/Scenes/Main.unity`).
2. Select the GameObject hosting the `GrappleLocomotion` component (likely on `XR Origin (XR Rig)`).
3. Drag the same GameObject's `ClimbingLocomotion` component (sibling on XR Origin) into the new `Climbing Locomotion` slot on `GrappleLocomotion`.
4. Save the scene.

If the gun prefab still has a `climbingLocomotion` SerializeField reference on its `GrappleGun` component — that field has been removed from the script. The Inspector will show a yellow "Missing reference" / unused entry that should disappear after a Unity refresh. If a "Missing script field" warning appears, ignore — it'll be cleaned by Unity on save.

- [ ] **Step 4: Manual smoke test**

Press Play. With the grapple gun in hand:
- Aim at an ice surface within range. Confirm the laser is visible and *attempts* a green/red color (the actual color may still be wrong — that's Task 7).
- Pull trigger on a valid surface. Confirm the rig zips. Once arrived, confirm the rig hangs at the wall. **Time the hang from arrival to drop.** Roughly compare a short-distance shot vs a long-distance shot — both hang times should look approximately equal. Exact verification comes in Task 8.
- Pull trigger aimed at sky. Confirm dry-fire red flash (no zip).
- Mid-zip, release trigger. Confirm the zip cancels and the rig stops.

Auto-swap is wired but won't visibly succeed unless an ice pick is on a belt slot — set that up before this test if you want to see it fire. Detailed auto-swap verification is in Task 8.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs \
        Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs \
        Assets/IcePEAK/Scenes/Main.unity
git commit -m "Constant wall-hang on arrival; auto-swap gun for ice pick"
```

(The scene file is included because Step 3 wires the `climbingLocomotion` reference. If that wiring lives on a prefab instead, swap the path.)

---

## Task 4: Add cooldown gate to `GrappleGun`

**Goal:** Successful zips lock the gun for `cooldownDuration` seconds. Dry-fires do not start the cooldown.

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`

- [ ] **Step 1: Add cooldown fields and properties**

In `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`, find the `[Header("Hint")]` section and insert a new `[Header("Cooldown")]` section above it:

```csharp
        [Header("Cooldown")]
        [Tooltip("Seconds the gun is locked after a successful zip starts. Dry-fires do not start the cooldown.")]
        [SerializeField] private float cooldownDuration = 3.0f;
```

In the private fields region (right after `private Vector3 _zipAnchor;`), add:

```csharp
        private float _cooldownEndTime;
```

In the public-API region (right after `public string DisplayName => displayName;`), add:

```csharp
        /// <summary>True while the gun is on cooldown and Fire() will be rejected.</summary>
        public bool IsOnCooldown => Time.time < _cooldownEndTime;

        /// <summary>0 = just fired, 1 = ready (or cooldownDuration ≤ 0). Drives the diegetic indicator.</summary>
        public float CooldownProgress01 => cooldownDuration <= 0f
            ? 1f
            : Mathf.Clamp01(1f - (_cooldownEndTime - Time.time) / cooldownDuration);
```

- [ ] **Step 2: Gate `Fire()` and start the cooldown on a successful zip**

In `Fire()`:

Change the early-out from:
```csharp
            if (_isStowed || _isZipping || _isDryFiring) return;
```
to:
```csharp
            if (_isStowed || _isZipping || _isDryFiring || IsOnCooldown) return;
```

Inside the successful-hit branch, immediately after the `_isZipping = true;` line, add:
```csharp
                _cooldownEndTime = Time.time + cooldownDuration;
```

(Place this on its own line right after `_isZipping = true;`; do not move the rope-setup lines that follow.)

- [ ] **Step 3: Compile-check**

`mcp__UnityMCP__refresh_unity` then `mcp__UnityMCP__read_console` for errors. Expected: zero errors.

- [ ] **Step 4: Manual verification**

Press Play. Fire grapple successfully → confirm a second trigger pull within ~3 seconds is rejected (no dry-fire flash either, because the gate runs before the raycast). Wait ~3s → confirm the gun fires again. Pull trigger on a miss (sky) repeatedly → confirm rapid dry-fires are not gated by the cooldown.

For numeric verification, temporarily add `Debug.Log($"[Gun] cd={CooldownProgress01:F2}");` in `LateUpdate` (remove before commit) or use the editor Inspector to watch the `_cooldownEndTime` private field while paused.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs
git commit -m "Gate grapple gun with cooldown after successful zip"
```

---

## Task 5: Diegetic cooldown indicator

**Goal:** A small visual on the gun's barrel reflects cooldown progress and "blips" when ready. Implemented as a self-contained component reading the gun's `CooldownProgress01`.

**Files:**
- Create: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleCooldownIndicator.cs`
- Modify: `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (manual editor work)

- [ ] **Step 1: Create the indicator component**

Write `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleCooldownIndicator.cs` with this exact content:

```csharp
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
```

- [ ] **Step 2: Compile-check**

`mcp__UnityMCP__refresh_unity` then `mcp__UnityMCP__read_console`. Expected: zero errors.

- [ ] **Step 3: Add `BarrelCooldownStrip` child to the gun prefab**

In the Unity editor:
1. Open `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` for editing.
2. Right-click the prefab root → 3D Object → Cylinder. Rename to `BarrelCooldownStrip`.
3. Position/scale it along the barrel — a thin strip parallel to `barrelTip.forward`, e.g. `localScale ≈ (0.005, 0.04, 0.005)` rotated to lie flat on the barrel. Tune visually.
4. Remove the auto-added Capsule Collider (we don't want collisions).
5. Create a new Material at `Assets/IcePEAK/Materials/GrappleCooldownStrip.mat`.
   - Shader: `Universal Render Pipeline/Unlit` (a basic unlit URP material — the indicator just needs a `_BaseColor` + a `_Fill` float).
   - **Custom-shader option (optional, nicer visual):** if you want the strip to *actually fill* (not just change color), create a small custom Shader Graph at `Assets/IcePEAK/Shaders/CooldownFill.shadergraph` that takes a `_Fill` (Float, 0–1) and a `_BaseColor` and outputs `_BaseColor` only where `UV.y < _Fill` (alpha 0 elsewhere). Use that as the material's shader.
   - **Minimal-effort option:** any URP shader with a writable float property. The indicator script will write to whatever `fillProperty` you name — just keep them consistent.
6. Assign the material to the strip's `MeshRenderer`.
7. Add a `GrappleCooldownIndicator` component to `BarrelCooldownStrip` (or any child of the gun root — wherever is convenient).
8. Wire fields:
   - `Gun` → the prefab root's `GrappleGun` component.
   - `Fill Renderer` → the strip's `MeshRenderer`.
   - `Fill Property` → matches your shader property (e.g. `_Fill` for the custom shader, or `_BaseColor` if you settle for color-pulsing instead of fill).
9. Save the prefab.

- [ ] **Step 4: Manual verification**

Press Play. Fire grapple. Observe the strip animating from 0 → 1 over the cooldown duration. On ready, a brief scale punch should fire. Tune `readyPunchDuration` / `readyPunchScale` / strip dimensions / shader as needed.

If you used the minimal-effort option (no custom fill shader), the strip will pulse color instead of filling — acceptable as a v1 visual. The `_Fill` write becomes a no-op; the punch on ready still works.

- [ ] **Step 5: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/Items/GrappleCooldownIndicator.cs \
        Assets/IcePEAK/Scripts/Gadgets/Items/GrappleCooldownIndicator.cs.meta \
        Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab \
        Assets/IcePEAK/Materials/ \
        Assets/IcePEAK/Shaders/
git commit -m "Add diegetic cooldown indicator on grapple gun barrel"
```

(Adjust the `Materials/` and `Shaders/` paths if you didn't create the optional shader.)

---

## Task 6: Dry-fire haptic buzz

**Goal:** Pulling trigger on a miss buzzes the firing hand alongside the existing red flash. Reuses the `HapticImpulsePlayer` pattern already used by `IcePickController`.

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`
- Modify: `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (wire haptics refs)

- [ ] **Step 1: Add haptic fields and resolve them per-hand**

In `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`, add `using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;` near the top (alongside the existing `using` lines).

Find the `[Header("Input")]` block and add a new `[Header("Haptics")]` block right below it (above `[Header("Cooldown")]`):

```csharp
        [Header("Haptics")]
        [Tooltip("HapticImpulsePlayer on the left controller — buzzes on left-hand dry-fire.")]
        [SerializeField] private HapticImpulsePlayer leftHaptics;
        [Tooltip("HapticImpulsePlayer on the right controller — buzzes on right-hand dry-fire.")]
        [SerializeField] private HapticImpulsePlayer rightHaptics;
        [SerializeField, Range(0f, 1f)] private float dryFireHapticAmplitude = 0.5f;
        [SerializeField] private float dryFireHapticDuration = 0.1f;
```

In the private-fields region (next to `private UnityEngine.InputSystem.InputActionReference _activeTriggerAction;`), add:

```csharp
        private HapticImpulsePlayer _activeHaptics;
```

- [ ] **Step 2: Resolve haptics alongside the trigger**

Replace the existing `ResolveHandTriggerAction` method with this version that resolves both:

```csharp
        private UnityEngine.InputSystem.InputActionReference ResolveHandTriggerAction()
        {
            for (var t = transform.parent; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _activeHaptics = leftHaptics;
                    return leftTriggerAction;
                }
                if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _activeHaptics = rightHaptics;
                    return rightTriggerAction;
                }
            }
            _activeHaptics = rightHaptics;
            return rightTriggerAction;
        }
```

- [ ] **Step 3: Pulse haptics on dry-fire**

Replace `StartDryFire` with:

```csharp
        private void StartDryFire()
        {
            StartCoroutine(DryFireFlash());
            if (_activeHaptics != null && dryFireHapticAmplitude > 0f && dryFireHapticDuration > 0f)
                _activeHaptics.SendHapticImpulse(dryFireHapticAmplitude, dryFireHapticDuration);
        }
```

- [ ] **Step 4: Compile-check**

`mcp__UnityMCP__refresh_unity` then `mcp__UnityMCP__read_console`. Expected: zero errors. If the `HapticImpulsePlayer` type is unresolved, the `using` line is missing or the package version doesn't match — verify the namespace by checking `Assets/IcePEAK/Scripts/IcePick/IcePickController.cs:4` (it imports the same).

- [ ] **Step 5: Wire haptics references on the prefab**

In the Unity editor:
1. Open `Item_GrappleGun.prefab`.
2. Locate the `GrappleGun` component on the root.
3. The `Left Haptics` / `Right Haptics` slots are now visible under the new `Haptics` header. Since the gun prefab is instanced into either hand at runtime (it doesn't know its hand at edit time), these references need to point at scene-level `HapticImpulsePlayer` components. **Wire on the scene instance, not the prefab** — open the scene, find the gun instances under each hand, and drag the matching controller's `HapticImpulsePlayer` component into the corresponding slot on the gun's `GrappleGun` script.
4. Save the scene.

If the gun is instantiated into hands dynamically and there are no scene instances pre-placed, fall back to a runtime resolver: in `OnTransfer(_, Hand)` after `ResolveHandTriggerAction()`, if `_activeHaptics` is still null, walk the parent chain looking for a `HapticImpulsePlayer` component. If you go this route, replace `_activeHaptics = leftHaptics;` in the resolver with a parent-chain `GetComponentInParent<HapticImpulsePlayer>()` lookup, and remove the SerializeField slots — but verify the controllers actually have a `HapticImpulsePlayer` first (they do if the project uses XRI's Haptic samples).

- [ ] **Step 6: Manual verification**

Press Play. With the gun held: aim at sky and pull trigger. Confirm the firing controller buzzes briefly while the red flash plays. Aim at a valid ice surface and fire — **no buzz** (it's a successful zip, not a dry-fire). Pull trigger during cooldown — **no buzz** (the gate rejects before `StartDryFire`).

- [ ] **Step 7: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs \
        Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab \
        Assets/IcePEAK/Scenes/Main.unity
git commit -m "Buzz firing controller on grapple gun dry-fire"
```

---

## Task 7: Aim-ray color swap fix

**Goal:** The existing color-swap logic in `GrappleGun.LateUpdate` is correct, but the laser shows up as a single color in-editor — almost certainly a material/shader issue on the LineRenderer.

**Files:**
- Modify: `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (likely just the laser's material)
- Possibly modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs` (only if a property-based fix is chosen)

- [ ] **Step 1: Inspect the prefab**

In the Unity editor:
1. Open `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab`.
2. Select the `LineRenderer` GameObject wired into `GrappleGun.laser`.
3. Note the assigned material asset path. Open it. Note the shader.
4. Back on the `GrappleGun` script, confirm `Laser Valid Color` and `Laser Out Of Range Color` are visibly different.

- [ ] **Step 2: Diagnose**

Likely findings (from most to least probable):
- **(a) The shader is URP `Lit` (or another shader that doesn't sample vertex colors).** `LineRenderer.startColor`/`endColor` write to vertex colors only. Lit ignores them. **Fix in Step 3a.**
- **(b) The two color SerializeField values are nearly identical.** Tweak them in the Inspector to clearly distinct colors and re-test. If they're already distinct, move on.
- **(c) The laser is enabled = false at the wrong time.** Add a temporary `Debug.Log($"laser.enabled={laser.enabled}, color={laser.startColor}");` at the bottom of `LateUpdate` to confirm. Remove before commit.

- [ ] **Step 3a: Fix path A — material swap (preferred)**

If the shader is `Lit` or any shader that doesn't honor vertex colors:
1. Either reuse an existing project Unlit material that's known to work for line renderers, or create a new one at `Assets/IcePEAK/Materials/GrappleLaser.mat`.
2. Set its shader to `Universal Render Pipeline/Particles/Unlit` (vertex-color-aware) or any URP shader explicitly designed for lines.
3. Assign to the laser's `LineRenderer.Material`.
4. Save the prefab.
5. Press Play. Aim at ice → green; aim at sky → red. Confirm.

- [ ] **Step 3b: Fix path B — property-based**

If you'd rather not change materials, write color via `MaterialPropertyBlock` on the LineRenderer instead. In `GrappleGun.LateUpdate`, after the existing `laser.startColor = color; laser.endColor = color;` lines, add:

```csharp
            if (_laserMpb == null) _laserMpb = new MaterialPropertyBlock();
            laser.GetPropertyBlock(_laserMpb);
            _laserMpb.SetColor(BaseColorID, color);
            laser.SetPropertyBlock(_laserMpb);
```

And add to the private fields:

```csharp
        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _laserMpb;
```

Confirm the laser material has a `_BaseColor` property (URP Lit and most Unlit shaders do). If it has `_Color` instead (Built-in), change `_BaseColor` → `_Color`.

- [ ] **Step 4: Compile-check (only if Step 3b was used)**

`mcp__UnityMCP__refresh_unity` then `mcp__UnityMCP__read_console`. Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
# If Step 3a:
git add Assets/IcePEAK/Materials/ Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab
git commit -m "Fix grapple gun laser material so vertex color swap is visible"

# If Step 3b:
git add Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab
git commit -m "Drive grapple gun laser color via MaterialPropertyBlock"
```

---

## Task 8: End-to-end editor verification

**Goal:** Walk through the combined flow in-editor and verify all four features work together. No code change.

- [ ] **Step 1: Setup**

1. Open `Assets/IcePEAK/Scenes/Main.unity` (or whichever scene has the active gameplay setup).
2. Confirm the player starts with: ice picks in both hands, grapple gun stowed on a belt slot, and at least one belt slot will eventually contain an ice pick after the player picks up the grapple gun.
3. If using XR Simulation, ensure `Assets/XR/Resources/XRSimulationRuntimeSettings.asset` is active. Otherwise plan to test on-device.

- [ ] **Step 2: Flow walkthrough**

Press Play. Execute this sequence and verify each:

1. **Stow right ice pick → draw grapple gun.** Hover the right belt slot containing the gun, grip-press → confirm right-hand ice pick goes to slot, gun comes to hand. ✅ if same as before refactor.
2. **Aim ray colors.** Aim at ice surface → green. Aim at sky → red. ✅ if Task 7 succeeded.
3. **Cooldown indicator idle.** Confirm the indicator strip shows "ready" (full / unpunched).
4. **Fire at a near surface (~5m).** Confirm: zip travel, arrival, hang. **Time the hang from arrival to drop.** Note duration (target: ≈ `wallHangDuration`, default 2.0s).
5. **During the hang**: confirm the gun is no longer in the firing hand — an ice pick is. Swing it into the wall. Confirm pick embed and that the player is anchored by the pick after the hang ends.
6. **Repeat with a far surface (~30m).** Time the hang again. Confirm it's approximately the same as the near-shot hang (Δ < 0.2s — the only difference should be frame jitter).
7. **Cooldown gate.** Immediately after dropping off the wall, pull trigger again. If you fired ≤ 3s ago (default `cooldownDuration`), expect rejection — gun does nothing. Wait a bit, fire again — expect success.
8. **Dry-fire haptic.** Aim at sky, pull trigger → confirm controller buzzes briefly with red flash.
9. **No-pick fallback.** Move both ice picks into hands manually before firing the grapple. Now belt has no pick. Fire grapple → arrival → confirm gun **stays** in firing hand (no auto-swap), hang still runs, player drops normally.

- [ ] **Step 3: Note any regressions**

If anything fails, file the regression as its own follow-up task. Do not fix in this task — that breaks the test gate.

- [ ] **Step 4: Mark plan complete**

No commit (no code change). Optionally tag the head of branch:

```bash
git tag -a grapple-gun-bundle-v1 -m "Grapple gun feature bundle complete"
```

---

## Self-review checklist (already applied)

- **Spec coverage:** Section 1 (constant hang) → Tasks 2+3. Section 2 (auto-swap) → Tasks 1+3. Section 3 (cooldown + indicator + haptic) → Tasks 4+5+6. Section 4 (laser color fix) → Task 7. Climbing-anchor lifecycle move → Task 2. Dead-code removal → Task 3. End-to-end → Task 8.
- **No placeholders:** every code-bearing step contains the actual code; every prefab step lists the exact action; no "TBD"/"add error handling later".
- **Type/method consistency:** `BeltSwap.Swap`, `BeltSwap.PlaceInto`, `BeltSwap.FindOwningHand`, `BeltSwap.FindFirstSlotWithIcePick` are referenced consistently between Task 1 (definition) and Task 3 (usage). `OnZipArrivedAtWall` / `OnZipComplete` callback names are consistent between Task 2 (definition site in `GrappleLocomotion.ZipRoutine`) and Task 3 (handler site in `GrappleGun.Fire`). `CooldownProgress01` / `IsOnCooldown` defined in Task 4 are consumed in Task 5.
- **Dependencies declared:** Tasks 2 and 3 are coupled (project doesn't compile between them) — flagged at the top of Task 2.
