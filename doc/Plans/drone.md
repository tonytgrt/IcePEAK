# Drone Gadget — Design

Date: 2026-04-22
Branch: `Drone` (proposed)
Status: Design proposal — pending approval before implementation.

## 1. Problem

The player needs a way to plan their climbing route from a high vantage. Existing
gadgets (Piton, ColdSpray, GrappleGun) all share the same lifecycle: they live
in a `BeltSlot`, can be drawn into a `HandCell` with grip, and are activated
with trigger while held. The drone breaks all three of those assumptions:

1. It must **never leave its slot** — it is a fixed feature of the belt, not
   a swappable item.
2. Its activation is **continuous** (held), not impulse (single trigger press).
3. Its activation **changes the camera viewpoint**, not the world state.

This doc specifies the minimal extensions to the gadget-belt system that
support a "fixed in slot, grip-to-peek" gadget without breaking the existing
swap/draw/stow flow for normal items.

## 2. Goals & Non-Goals

**Goals**
- A `Drone` item that lives in one specific `BeltSlot` and rejects all draw /
  swap / stow attempts.
- While the player's hand is hovering that slot **and** grip is held, the rig's
  view is replaced with a fixed scene camera ("Drone View") that gives an
  overview of the map.
- Releasing grip restores the normal first-person HMD view, instantly.
- Fall handler must NOT respawn the player while in Drone View (the rig is
  artificially repositioned during the peek).
- The change is additive: no behavior change for existing gadgets.

**Non-goals**
- Free-flying drone or drone movement. The "drone view" is a single fixed
  camera placed by a level designer; there's no controllable camera rig.
- Multi-camera switching (looking at different overviews). One scene = one
  drone vantage point for the MVP.
- Drone HUD overlays (waypoints, route markers, distance indicators). Future
  work — the MVP just shows the unmodified scene from a high angle.
- Drone view in multiplayer / shared state.
- Drone-cam audio mix (muffling SFX when "looking through the drone"). Out of
  scope for MVP.
- Cooldown / battery / consumable state. The drone is always available.
- Smooth camera transitions / lerps between viewpoints. The view snap is
  instant (with optional black fade — see §6).
- Replacement of HMD orientation tracking. The player still looks around
  freely with their head while in Drone View; only the rig's *world position*
  is overridden (see §5.2).

## 3. Visible behavior

### 3.1 Idle (drone in slot)

- The drone visual sits in its assigned slot (e.g., `Slot_SideLeft`) like any
  other gadget — slot highlight on hover, hint label reads "Hold grip to scout".
- Trigger press while hovering does **nothing** (drone is not held in hand).
- The drone is never adopted into a `HandCell`, even via grip press.

### 3.2 Peek begin

When **all** of the following are true on the same frame:
- A hand's `HandCell.Anchor` is within the belt's `proximityRadius` of the
  drone slot (i.e., `CurrentHoveredSlot == droneSlot`),
- That hand's grip action transitions from below-threshold to above-threshold,
- No ice pick is currently embedded on either hand,

the player enters Drone View:
- The XR rig's world position is overridden to the drone-view anchor's
  position (orientation: a designer-placed transform in the scene).
- Default locomotion providers (move, turn, teleport, climbing, grapple) are
  disabled so the player can't drift while in the overview.
- Both ice-pick tip colliders are disabled (no embedding on geometry that
  happens to overlap the drone vantage).
- `FallHandler` is told to suppress respawns (analogous to how it already
  checks `GrappleLocomotion.IsZipping`).

The player's HMD orientation is still live — they turn their head to look
around the overview.

### 3.3 Peek end

When **any** of the following becomes true:
- The triggering hand's grip drops below threshold,
- The triggering hand moves outside `proximityRadius` of the drone slot,
- The XR session loses focus (paused, app-suspended),

the rig is restored to its pre-peek world position, all suspended systems are
re-enabled, and the player resumes normal play.

### 3.4 Edge cases

- **Both hands simultaneously grip the drone slot.** Only the first hand to
  enter the peek owns the session; the second is ignored until the first
  releases. If both grip on the same frame, the left hand wins by convention.
- **Player presses trigger during peek.** Ignored — the held-item activate
  path doesn't run because no item is in the hand.
- **Player attempts to grip-swap with the drone slot.** Rejected (see §4.1).
  No swap, no stow, no draw occurs. The grip press still enters the peek.
- **Drone slot is somehow emptied at runtime.** The peek system keys off the
  drone slot reference, not its `HeldItem`, so an empty slot still works as
  the trigger zone but with no visual. Not expected to occur in normal play.

## 4. Architecture

Three new files, one extension to existing logic, plus scene wiring.

### 4.1 `Drone.cs` — the gadget item

`Assets/IcePEAK/Scripts/Gadgets/Items/Drone.cs`

Implements `IHoldable` (so it can sit in a `BeltSlot`) but adds a marker
behavior `IFixedInSlot` to opt out of swap/draw/stow.

```csharp
public interface IFixedInSlot { }  // marker — pure type tag

public class Drone : MonoBehaviour, IHoldable, IFixedInSlot
{
    [SerializeField] private string displayName = "Drone";
    public string DisplayName => displayName;
    public void OnTransfer(CellKind from, CellKind to) { /* never called */ }
}
```

The item itself does not implement `IActivatable`, because activation is
hand-and-slot-coupled, not held-item-coupled — see §4.3.

### 4.2 `HandInteractionController` — reject swap on fixed items

In `ResolveBeltAction`, before the existing snapshot/take/place sequence,
short-circuit if the slot's held item carries `IFixedInSlot`:

```csharp
if (slot.HeldItem != null && slot.HeldItem.GetComponent<IFixedInSlot>() != null)
    return;  // drone-style slot — grip-press doesn't move it
```

Net effect: pressing grip while hovering the drone slot is a no-op for the
swap/draw/stow path, leaving grip free to be used by the new peek path.

The hint string for fixed items is overridden in `BeltSlot.GetHintText` (or
read off a new `IFixedInSlot.HintText` getter — TBD; simplest approach is to
add an interface method `string FixedHintText { get; }` returning the right
copy, e.g. "Hold grip to scout").

### 4.3 `DroneController.cs` — the per-rig peek controller

`Assets/IcePEAK/Scripts/Gadgets/DroneController.cs`

Lives on the XR Origin (XR Rig) alongside `GrappleLocomotion` and
`ClimbingLocomotion`. Owns the peek state and camera/rig manipulation.

```csharp
public class DroneController : MonoBehaviour
{
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private Transform droneViewAnchor;   // designer-placed
    [SerializeField] private BeltSlot droneSlot;          // the fixed slot
    [SerializeField] private HandInteractionController leftHand;
    [SerializeField] private HandInteractionController rightHand;
    [SerializeField] private InputActionReference leftGrip;
    [SerializeField] private InputActionReference rightGrip;
    [SerializeField] private float gripThreshold = 0.5f;
    [SerializeField] private MonoBehaviour[] suspendDuringPeek; // climbing,
                                                                // grapple,
                                                                // locomotion
                                                                // providers
    [SerializeField] private IcePickController leftPick;
    [SerializeField] private IcePickController rightPick;
    [SerializeField] private ScreenFader fader;            // optional

    public bool IsPeeking { get; private set; }
    private Vector3 _savedOrigin;
    private Quaternion _savedRotation;
    private HandInteractionController _ownerHand;
}
```

Each frame:
1. If not peeking and either hand is hovering `droneSlot` and that hand's grip
   went above threshold this frame, and no pick is embedded on either hand →
   `BeginPeek(thatHand)`.
2. If peeking and (`_ownerHand`'s grip below threshold OR `_ownerHand` no
   longer hovering `droneSlot`) → `EndPeek()`.

`BeginPeek` saves `xrOrigin.position/rotation`, snaps to
`droneViewAnchor.position/rotation`, disables all `suspendDuringPeek` MBs,
releases & disables both pick colliders, raises `IsPeeking = true`. (Optional:
black fade-in/out via `ScreenFader` — see §6.)

`EndPeek` restores transform, re-enables MBs and pick colliders, lowers
`IsPeeking`.

### 4.4 `FallHandler` — suppress respawn during peek

Add a serialized reference and OR it into the airborne-cancel check (same
pattern as the existing `grappleLocomotion.IsZipping`):

```csharp
[SerializeField] private DroneController droneController;
...
bool isPeeking = droneController != null && droneController.IsPeeking;
if (anyEmbedded || isZipping || isPeeking || grounded) {
    _airborneTime = 0f; return;
}
```

## 5. Technical concerns

### 5.1 Why grip-pressed-this-frame, not grip-currently-held

`HandInteractionController` already consumes `gripAction.action.WasPressedThisFrame()`
on rung 2 (swap/stow/draw). If we used "grip held" detection, we'd race with
that path: the very first frame of grip-down would either swap (rung 2 wins)
or peek (DroneController wins) depending on call order.

By having `HandInteractionController` early-return on `IFixedInSlot` (§4.2),
rung 2 cleanly declines the grip press at the drone slot, so DroneController
can claim the same press without competing for it. After that, grip-held
sustains the peek; the two systems never disagree on grip ownership.

### 5.2 Camera vs rig snap

VR camera position is driven by the HMD's tracked pose offset from the XR
Origin. There are two ways to "switch view":

(a) **Move the XR Origin** to the drone vantage transform. The HMD then
renders from `(droneAnchor + tracked HMD offset)`. Player can still look
around, but a 1m physical step still produces a 1m world step from the new
vantage.

(b) **Disable HMD pose driver** and override the camera transform directly.
This locks out head tracking, which causes severe VR sickness. **Rejected.**

**Decision: (a).** The drone vantage transform is placed with the assumption
that the player will turn their head and possibly take a step. Designer
ensures the vantage has enough clearance around it that small physical steps
during peek don't push the camera into geometry. (Optional: clamp the rig
position back to `droneViewAnchor` each LateUpdate if drift is observed in
playtests.)

### 5.3 Disabling locomotion / climbing providers

The drone peek must not fight other locomotion systems. The
`suspendDuringPeek[]` array is the single source of truth for "what to turn
off"; designers wire it in the inspector to:
- All entries in `ClimbingLocomotion.locomotionProviders[]`
- `ClimbingLocomotion` itself (so its Update doesn't run climbing math)
- `GrappleLocomotion` itself (no zip can start during peek)
- Any continuous-move / snap-turn / teleport providers from XR Interaction
  Toolkit

All are flipped `enabled = false` in `BeginPeek` and `enabled = true` in
`EndPeek`. No state needs to be saved beyond the rig transform.

### 5.4 Pick handling

Both `IcePickController.Release()` is called (cleanly, so events fire and any
crack timers stop), and tip colliders are disabled, before the rig snap.
Otherwise picks may end up "embedded" inside drone-view geometry on a release
frame. Re-enabled on `EndPeek`.

### 5.5 No respawn during peek

The rig position during peek is artificial — the player is "actually" still at
the climbing position they peeked from. `FallHandler` already tolerates the
analogous case for grappling (`isZipping`). A second flag `isPeeking` is
trivial to add. See §4.4.

## 6. Optional polish: fade

VR snap teleports cause discomfort for some players. A black fade-in / fade-out
via the existing `ScreenFader` (used by `FallHandler` for respawn) is a one-
line addition in `BeginPeek` / `EndPeek`. Recommended but not required for
MVP. Tunable: `peekFadeDuration` defaulting to `0.1 s`.

## 7. Inspector wiring (per scene)

| Field on `DroneController` | Source |
|---|---|
| `xrOrigin` | XR Origin (XR Rig) Transform |
| `droneViewAnchor` | Designer-placed empty GameObject at the overview point |
| `droneSlot` | The chosen `BeltSlot` (e.g., `Slot_SideLeft`) holding the drone |
| `leftHand` / `rightHand` | Left/Right `HandInteractionController` |
| `leftGrip` / `rightGrip` | XRI Left/Right Interaction → Select Value |
| `suspendDuringPeek` | All locomotion providers + ClimbingLocomotion + GrappleLocomotion |
| `leftPick` / `rightPick` | The two `IcePickController`s |
| `fader` | The `ScreenFader` on Player Services (optional) |

Plus `FallHandler.droneController` → the new `DroneController`.

The drone item is placed as a child of its chosen slot in the scene (or wired
via `BeltSlot.initialPrefab` as a one-shot). Authoring equivalent to existing
items.

## 8. Implementation order

1. **Marker + opt-out** — Add `IFixedInSlot` interface; add the early-return
   in `HandInteractionController.ResolveBeltAction`. Verify other gadgets
   still swap/draw/stow normally.
2. **Drone item** — Add `Drone.cs`; build the `Drone.prefab` (placeholder
   visual: small cube with screen, or borrow the grapple gun mesh until art
   ships). Slot it into one BeltSlot in TestScene; verify grip press is a
   no-op there.
3. **DroneController + scene wiring** — Add the controller. Wire references
   in TestScene first. Test peek / end-peek transitions with grip held.
4. **FallHandler integration** — Add the `isPeeking` short-circuit. Test that
   peeking from mid-air doesn't respawn the player on grip release (rig is
   restored to the original mid-air position, which is itself airborne — the
   normal grace period applies again only after release, which is correct).
5. **Polish** — Optional black fade. Hint text override. Verify locomotion
   providers re-enable cleanly after long peek sessions.
6. **Main scene wiring** — Replicate scene wiring in Main.unity (same pattern
   as how `FallHandler.grappleLocomotion` was wired separately for both
   scenes).

## 9. Open questions

- **Which slot houses the drone?** Suggest `Slot_SideLeft` or `Slot_SideRight`
  so the player's dominant hand stays free for picks. Up to design.
- **Can the player still see their hands during peek?** Yes by default — XR
  controller tracking continues. If this is distracting (because hands aren't
  "really" at the drone vantage), we can disable the controller visuals on
  `BeginPeek` and re-enable on `EndPeek`. Cheap to add.
- **Drift correction.** If physical-step drift while peeking turns out to feel
  wrong, change the rig snap from "set once" to "lerp to drone anchor each
  LateUpdate". Trivial change, deferrable.
- **Multi-vantage future.** If we later want multiple overview points (one
  per region), `DroneController` becomes a manager with a list of anchors and
  picks the nearest one to the player's current `xrOrigin.position`. The
  contract from `HandInteractionController` doesn't change.
