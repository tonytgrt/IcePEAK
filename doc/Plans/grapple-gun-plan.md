# Grapple Gun Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `GrappleGun` streak with a working grapple that previews aim with a diegetic laser, fires on trigger, zips the XR Origin to the hit point, self-destructs on arrival, and dry-fires on miss.

**Architecture:** Split into two components mirroring the pick/climb pattern:
- `GrappleGun` (item-level, on `Item_GrappleGun.prefab`) — owns the laser preview, barrel raycast, dry-fire flash, and rope render. Resolves the rig-level locomotion component the first time it needs to zip.
- `GrappleLocomotion` (rig-level, on XR Origin alongside `ClimbingLocomotion`) — owns the coroutine that releases embedded picks, toggles default locomotion providers, and lerps the XR Origin to `anchor + normal * surfaceOffset` over an ease curve.

**Tech Stack:** Unity 6.3 LTS, URP 17.3, C# MonoBehaviours, `Physics.Raycast` with `LayerMask` filter, `LineRenderer` (world-space), `AnimationCurve` easing, `IHoldable` / `IActivatable` / `CellKind` from `IcePEAK.Gadgets`, `FindAnyObjectByType<T>()` for the cached rig lookup. Unity MCP is used for prefab/scene edits.

**Design spec:** `doc/Plans/grapple-gun.md` (commit `d1b19e0`).

---

## Task 0: Verify branch & clean working tree

**Files:** repo root (no code change).

- [ ] **Step 1: Confirm branch**

Run: `git branch --show-current`
Expected: `GrappleGun`

- [ ] **Step 2: Confirm clean tree**

Run: `git status`
Expected: `nothing to commit, working tree clean`. If `SwingDetector.cs` or any unrelated file is dirty, stop and resolve before proceeding.

- [ ] **Step 3: Confirm spec commit is present**

Run: `git log --oneline -1`
Expected: `d1b19e0 docs(gadgets): grapple gun design spec`

- [ ] **Step 4: Confirm Unity MCP is reachable (needed for Task 3 and Task 4)**

Run the Unity MCP `get_console` or `refresh_unity` tool. Expected: a successful response. If MCP is disconnected, stop and ask the user to restart Unity / the MCP server before proceeding to prefab/scene tasks.

---

## Task 1: Add `GrappleLocomotion.cs`

**Files:**
- Create: `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`

Mirrors `ClimbingLocomotion` wiring: XR Origin transform + the two picks + an array of default locomotion providers to suspend during the zip. Exposes a public `StartZip(anchor, normal, onArrival) -> bool` that returns `false` if a zip is already running.

- [ ] **Step 1: Write `GrappleLocomotion.cs`**

```csharp
using System.Collections;
using UnityEngine;

namespace IcePEAK.Gadgets
{
    /// <summary>
    /// Rig-level zipline locomotion. Lives on the XR Origin alongside
    /// <c>ClimbingLocomotion</c>. Moves the XR Origin from its current
    /// position to a surface anchor over a fixed duration, offsetting along
    /// the surface normal so the player doesn't clip the hit geometry.
    ///
    /// While the zip is running:
    ///   - Default locomotion providers (move, turn, teleport) are disabled.
    ///   - Both ice picks are released and stowed so they can't interact.
    ///   - Additional <see cref="StartZip"/> calls are rejected.
    /// </summary>
    public class GrappleLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private IcePickController leftPick;
        [SerializeField] private IcePickController rightPick;

        [Header("Default Locomotion Providers")]
        [Tooltip("Components to disable while zipping — move, turn, teleport, etc. Re-enabled on arrival.")]
        [SerializeField] private MonoBehaviour[] locomotionProviders;

        [Header("Tunables")]
        [Tooltip("Seconds to travel from fire to arrival.")]
        [SerializeField] private float zipDuration = 0.5f;
        [Tooltip("Meters to stop short of the surface along its normal.")]
        [SerializeField] private float surfaceOffset = 0.5f;
        [SerializeField] private AnimationCurve zipEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsZipping => _isZipping;

        private bool _isZipping;

        /// <summary>
        /// Begin a zip to <paramref name="anchor"/>. Returns <c>false</c> if a
        /// zip is already running — callers should not start any rope visuals
        /// in that case.
        /// </summary>
        public bool StartZip(Vector3 anchor, Vector3 normal, System.Action onArrival)
        {
            if (_isZipping) return false;
            if (xrOrigin == null) return false;

            StartCoroutine(ZipRoutine(anchor, normal, onArrival));
            return true;
        }

        private IEnumerator ZipRoutine(Vector3 anchor, Vector3 normal, System.Action onArrival)
        {
            _isZipping = true;

            SetLocomotionProviders(false);
            if (leftPick != null) { leftPick.Release(); leftPick.SetStowed(true); }
            if (rightPick != null) { rightPick.Release(); rightPick.SetStowed(true); }

            Vector3 start = xrOrigin.position;
            Vector3 end = anchor + normal.normalized * surfaceOffset;
            float elapsed = 0f;

            while (elapsed < zipDuration)
            {
                float t = elapsed / zipDuration;
                float eased = zipEase.Evaluate(t);
                xrOrigin.position = Vector3.LerpUnclamped(start, end, eased);
                elapsed += Time.deltaTime;
                yield return null;
            }
            xrOrigin.position = end;

            if (leftPick != null) leftPick.SetStowed(false);
            if (rightPick != null) rightPick.SetStowed(false);
            SetLocomotionProviders(true);

            _isZipping = false;

            onArrival?.Invoke();
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

- [ ] **Step 2: Refresh Unity + check console**

Run Unity MCP `refresh_unity`, then `read_console` (level: error). Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs.meta
git commit -m "feat(gadgets): add GrappleLocomotion rig-level zip coroutine"
```

---

## Task 2: Rewrite `GrappleGun.cs`

**Files:**
- Modify: `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`

Replace the streak placeholder with the real gun. Adds a laser preview (Update), a fire path that raycasts and either dispatches to `GrappleLocomotion.StartZip` or runs a dry-fire flash coroutine, a rope that follows the barrel during zip (LateUpdate), and an `OnArrival` callback that removes the gun from its cell and destroys it.

Key behaviors (from the spec):
- Laser is always updated while the gun is held in a hand (not stowed, not zipping, not dry-firing). Color is green when the ray hits a `SurfaceTag` within `maxRange`, red when it hits nothing or misses the tag.
- Fire is gated on `_isZipping`, `_isDryFiring`, `_isStowed`, and `GrappleLocomotion.IsZipping`.
- On hit: set rope positions (barrel tip → anchor), enable rope, call `StartZip`. Only mark `_isZipping = true` and enable rope when StartZip returns true.
- On miss: play `DryFireFlash` coroutine — brief red laser at full `maxRange`, then restore Update-driven preview.
- On stow: disable rope **and** laser so a holstered gun doesn't render a stale line.
- On arrival (called by GrappleLocomotion): disable rope, call `_owningCell.Take()` to detach cleanly, `Destroy(gameObject)`.

**Owning-cell lookup:** `OnTransfer(from, to)` records `_owningCell` when `to == CellKind.Hand` by walking `transform.parent` for an `ICell`. (`HandCell` and `BeltSlot` both implement `ICell`.) This is the same pattern the hint system uses to resolve the slot that owns a label.

- [ ] **Step 1: Overwrite the file with the new implementation**

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
    /// rig to the surface and self-destructs on arrival; on miss, plays a
    /// brief red dry-fire flash.
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

        [Header("Hint")]
        [SerializeField] private string displayName = "Grapple Gun";

        public string DisplayName => displayName;

        private bool _isStowed = true;
        private bool _isZipping;
        private bool _isDryFiring;
        private ICell _owningCell;
        private GrappleLocomotion _locomotion;
        private Vector3 _zipAnchor;

        public void OnTransfer(CellKind from, CellKind to)
        {
            _isStowed = (to == CellKind.BeltSlot);

            if (to == CellKind.Hand)
            {
                _owningCell = ResolveOwningCell();
            }
            else
            {
                _owningCell = null;
            }

            if (_isStowed)
            {
                if (laser != null) laser.enabled = false;
                if (rope != null) rope.enabled = false;
            }
        }

        public void Activate() => Fire();

        public void Fire()
        {
            if (_isStowed || _isZipping || _isDryFiring) return;
            if (barrelTip == null) return;

            if (!TryResolveLocomotion()) { StartDryFire(); return; }
            if (_locomotion.IsZipping) return;

            if (Physics.Raycast(barrelTip.position, barrelTip.forward, out RaycastHit hit,
                                maxRange, hitMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<SurfaceTag>() != null)
            {
                _zipAnchor = hit.point;
                if (rope != null)
                {
                    rope.positionCount = 2;
                    rope.SetPosition(0, barrelTip.position);
                    rope.SetPosition(1, _zipAnchor);
                    rope.enabled = true;
                }

                if (_locomotion.StartZip(_zipAnchor, hit.normal, OnArrival))
                {
                    _isZipping = true;
                }
                else
                {
                    if (rope != null) rope.enabled = false;
                }
            }
            else
            {
                StartDryFire();
            }
        }

        private void Update()
        {
            if (_isStowed || _isZipping || _isDryFiring) return;
            if (laser == null || barrelTip == null) return;

            Vector3 origin = barrelTip.position;
            Vector3 dir = barrelTip.forward;

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

        private void LateUpdate()
        {
            if (!_isZipping || rope == null || barrelTip == null) return;
            rope.SetPosition(0, barrelTip.position);
            rope.SetPosition(1, _zipAnchor);
        }

        private void StartDryFire()
        {
            StartCoroutine(DryFireFlash());
        }

        private IEnumerator DryFireFlash()
        {
            _isDryFiring = true;

            if (laser != null && barrelTip != null)
            {
                laser.positionCount = 2;
                laser.SetPosition(0, barrelTip.position);
                laser.SetPosition(1, barrelTip.position + barrelTip.forward * maxRange);
                laser.startColor = laserOutOfRangeColor;
                laser.endColor = laserOutOfRangeColor;
                laser.enabled = true;
            }

            yield return new WaitForSeconds(dryFireDuration);

            _isDryFiring = false;
        }

        private void OnArrival()
        {
            if (rope != null) rope.enabled = false;
            if (laser != null) laser.enabled = false;

            if (_owningCell != null)
            {
                _owningCell.Take();
            }
            Destroy(gameObject);
        }

        private bool TryResolveLocomotion()
        {
            if (_locomotion != null) return true;
            _locomotion = FindAnyObjectByType<GrappleLocomotion>();
            return _locomotion != null;
        }

        private ICell ResolveOwningCell()
        {
            Transform t = transform.parent;
            while (t != null)
            {
                var cell = t.GetComponent<ICell>();
                if (cell != null) return cell;
                t = t.parent;
            }
            return null;
        }
    }
}
```

- [ ] **Step 2: Refresh Unity + check console**

Run Unity MCP `refresh_unity`, then `read_console` (level: error). Expected: no compile errors. If `ICell.Take()` doesn't exist or has a different signature, stop and inspect `Assets/IcePEAK/Scripts/Gadgets/ICell.cs`; adapt the call to match.

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs
git commit -m "feat(gadgets): grapple gun fires, zips, and self-destructs on arrival"
```

---

## Task 3: Update `Item_GrappleGun.prefab` (laser child + rope rename)

**Files:**
- Modify: `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab`

Goal: The prefab currently has a `Streak` child with a LineRenderer. Replace it with two children — `Rope` (repurposed from the current Streak) and a new `Laser` — both LineRenderers with `useWorldSpace = true`, `enabled = false` at rest, and thin widths. Then wire the new `laser` / `rope` serialized fields on the `GrappleGun` component.

**Do this via Unity MCP `execute_code`** (not by hand-editing the YAML). Read existing values before unload so you can build the edit without stale data; save with `PrefabUtility.SaveAsPrefabAsset`.

- [ ] **Step 1: Inspect the current prefab structure**

Run Unity MCP `execute_code` with:

```csharp
var prefabPath = "Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
string dump = "";
void Walk(UnityEngine.Transform t, int d) {
    dump += new string(' ', d * 2) + t.name + "\n";
    foreach (var c in t.GetComponents<UnityEngine.Component>()) dump += new string(' ', d * 2 + 2) + "- " + (c == null ? "<missing>" : c.GetType().Name) + "\n";
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1);
}
Walk(root.transform, 0);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
UnityEngine.Debug.Log(dump);
return dump;
```

Expected: output containing `Item_GrappleGun`, a `Visual` subtree, `BarrelTip`, and `Streak` with a `LineRenderer`. Record what's under `Streak` so we know the width/material to reuse.

- [ ] **Step 2: Rename `Streak` → `Rope`, add sibling `Laser`, wire both on the `GrappleGun` component**

Run Unity MCP `execute_code` with the following. The script:
1. Loads the prefab.
2. Finds the `Streak` child (if still named that) and renames it to `Rope`; grabs the existing `LineRenderer`.
3. Creates a new sibling `Laser` GameObject with a `LineRenderer` configured to be thin, emissive, unlit, world-space, 2 positions, disabled.
4. Grabs the `GrappleGun` component on root; assigns `laser` and `rope` via `SerializedObject`.
5. Saves the prefab.

```csharp
var prefabPath = "Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);

// 1. Find or rename Rope
UnityEngine.Transform ropeT = root.transform.Find("Rope");
if (ropeT == null) ropeT = root.transform.Find("Streak");
if (ropeT == null) { UnityEditor.PrefabUtility.UnloadPrefabContents(root); return "ERR: no Streak/Rope child"; }
ropeT.gameObject.name = "Rope";
var ropeLR = ropeT.GetComponent<UnityEngine.LineRenderer>();
if (ropeLR == null) ropeLR = ropeT.gameObject.AddComponent<UnityEngine.LineRenderer>();
ropeLR.useWorldSpace = true;
ropeLR.enabled = false;
ropeLR.positionCount = 2;
ropeLR.startWidth = 0.01f;
ropeLR.endWidth = 0.01f;

// 2. Create Laser child if missing
UnityEngine.Transform laserT = root.transform.Find("Laser");
if (laserT == null)
{
    var go = new UnityEngine.GameObject("Laser");
    laserT = go.transform;
    laserT.SetParent(root.transform, worldPositionStays: false);
    laserT.localPosition = UnityEngine.Vector3.zero;
    laserT.localRotation = UnityEngine.Quaternion.identity;
    laserT.localScale = UnityEngine.Vector3.one;
}
var laserLR = laserT.GetComponent<UnityEngine.LineRenderer>();
if (laserLR == null) laserLR = laserT.gameObject.AddComponent<UnityEngine.LineRenderer>();
laserLR.useWorldSpace = true;
laserLR.enabled = false;
laserLR.positionCount = 2;
laserLR.startWidth = 0.004f;
laserLR.endWidth = 0.004f;
var laserMat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));
laserMat.color = new UnityEngine.Color(1f, 0.3f, 0.3f);
laserLR.sharedMaterial = laserMat;

// 3. Wire GrappleGun serialized fields
var gun = root.GetComponent<IcePEAK.Gadgets.Items.GrappleGun>();
if (gun == null) { UnityEditor.PrefabUtility.UnloadPrefabContents(root); return "ERR: GrappleGun component missing on root"; }
var so = new UnityEditor.SerializedObject(gun);
so.FindProperty("laser").objectReferenceValue = laserLR;
so.FindProperty("rope").objectReferenceValue = ropeLR;
so.ApplyModifiedPropertiesWithoutUndo();

// 4. Save
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
return "OK";
```

Expected return: `"OK"`.

- [ ] **Step 3: Verify the prefab**

Run the inspection `execute_code` from Step 1 again. Expected: `Rope` and `Laser` children are both present, both with `LineRenderer` components. No `Streak` child remains.

Also run `read_console` for errors. Expected: none.

- [ ] **Step 4: Commit**

```bash
git add Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab
git commit -m "feat(gadgets): grapple gun prefab adds Laser child + renames Streak -> Rope"
```

---

## Task 4: Wire `GrappleLocomotion` into `TestScene`

**Files:**
- Modify: `Assets/IcePEAK/Scenes/TestScene.unity`

Goal: Add a `GrappleLocomotion` component to the same GameObject that already holds `ClimbingLocomotion`, and copy `ClimbingLocomotion`'s wiring (`xrOrigin`, `leftPick`, `rightPick`, `locomotionProviders`) onto it.

Do this via Unity MCP `execute_code`. Open the scene, add the component if not present, copy serialized references from the existing `ClimbingLocomotion`, mark the scene dirty, and save.

- [ ] **Step 1: Add component + copy wiring from `ClimbingLocomotion`**

```csharp
var scenePath = "Assets/IcePEAK/Scenes/TestScene.unity";
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

ClimbingLocomotion climbing = UnityEngine.Object.FindAnyObjectByType<ClimbingLocomotion>();
if (climbing == null) return "ERR: ClimbingLocomotion not found in scene";

var host = climbing.gameObject;
var grapple = host.GetComponent<IcePEAK.Gadgets.GrappleLocomotion>();
if (grapple == null) grapple = host.AddComponent<IcePEAK.Gadgets.GrappleLocomotion>();

var climbSO = new UnityEditor.SerializedObject(climbing);
var grappleSO = new UnityEditor.SerializedObject(grapple);

grappleSO.FindProperty("xrOrigin").objectReferenceValue = climbSO.FindProperty("xrOrigin").objectReferenceValue;
grappleSO.FindProperty("leftPick").objectReferenceValue = climbSO.FindProperty("leftPick").objectReferenceValue;
grappleSO.FindProperty("rightPick").objectReferenceValue = climbSO.FindProperty("rightPick").objectReferenceValue;

var climbProviders = climbSO.FindProperty("locomotionProviders");
var grappleProviders = grappleSO.FindProperty("locomotionProviders");
grappleProviders.arraySize = climbProviders.arraySize;
for (int i = 0; i < climbProviders.arraySize; i++)
{
    grappleProviders.GetArrayElementAtIndex(i).objectReferenceValue =
        climbProviders.GetArrayElementAtIndex(i).objectReferenceValue;
}

grappleSO.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "OK";
```

Expected return: `"OK"`.

- [ ] **Step 2: Verify the component + wiring**

```csharp
var scenePath = "Assets/IcePEAK/Scenes/TestScene.unity";
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
var grapple = UnityEngine.Object.FindAnyObjectByType<IcePEAK.Gadgets.GrappleLocomotion>();
if (grapple == null) return "ERR: GrappleLocomotion not on scene";
var so = new UnityEditor.SerializedObject(grapple);
string s = "";
s += "xrOrigin=" + (so.FindProperty("xrOrigin").objectReferenceValue != null) + "\n";
s += "leftPick=" + (so.FindProperty("leftPick").objectReferenceValue != null) + "\n";
s += "rightPick=" + (so.FindProperty("rightPick").objectReferenceValue != null) + "\n";
s += "providers=" + so.FindProperty("locomotionProviders").arraySize + "\n";
return s;
```

Expected: `xrOrigin=True`, `leftPick=True`, `rightPick=True`, and `providers` matches the count on `ClimbingLocomotion`.

Also `read_console`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/IcePEAK/Scenes/TestScene.unity
git commit -m "feat(gadgets): wire GrappleLocomotion onto XR Origin in TestScene"
```

---

## Task 5: Manual play-mode verification (user)

This task is run by the user inside Unity. The subagent must not mark it complete; they just assemble the checklist below in the final handoff message.

Open `Assets/IcePEAK/Scenes/TestScene.unity`, enter Play mode, and verify:

- [ ] **Belt**: draw the grapple gun from its belt slot into either hand.
- [ ] **Laser preview**: a thin line projects forward from the barrel. It's green when pointed at an ice or rock surface within ~40 m, red when pointing at sky / out of range.
- [ ] **Fire on hit**: press trigger while the laser is green → the rig smoothly zips to the surface over ~0.5 s, stopping ~0.5 m off the wall along its normal; the gun disappears from the hand on arrival; both ice picks are functional again immediately after.
- [ ] **Fire on miss**: press trigger pointing at the sky → a brief red flash along the barrel, no zip, the gun remains in hand and the green/red preview resumes after ~0.15 s.
- [ ] **Stow preview**: return the gun to the belt → laser and rope both disappear; they do not re-appear from the stowed belt slot.
- [ ] **Climb interaction**: fire while an ice pick is embedded → the embedded pick releases cleanly at zip start and the rig completes the zip.
- [ ] **Regression**: draw / stow / swap other gadgets (Cold Spray, Piton, ice picks) still behave the same as before this branch.

---

## Files touched (summary)

**New**
- `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs`
- `Assets/IcePEAK/Scripts/Gadgets/GrappleLocomotion.cs.meta` (Unity-generated)

**Modified — scripts**
- `Assets/IcePEAK/Scripts/Gadgets/Items/GrappleGun.cs`

**Modified — assets**
- `Assets/IcePEAK/Prefabs/Items/Item_GrappleGun.prefab` (`Streak` → `Rope`, add `Laser`, wire both on component)
- `Assets/IcePEAK/Scenes/TestScene.unity` (add `GrappleLocomotion` to XR Origin, wire four fields)
