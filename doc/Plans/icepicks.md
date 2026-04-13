# Ice Picks Implementation Plan

This document covers the step-by-step implementation of the two ice picks — the
player's primary tools in IcePEAK. The scope includes the pick models, swing
detection, surface embedding, climbing locomotion, grip/release handling, and the
fall/respawn trigger. Ice destruction (cracking/shattering) is a separate but
tightly coupled system; interfacing points are noted where relevant.

> **Engine:** Unity 6.3 LTS &bull; **XR Stack:** XR Interaction Toolkit 3.3.1 +
> Meta XR SDK 85 + OpenXR 1.16 &bull; **Render Pipeline:** URP 17.3

---

## Table of Contents

1. [Project Setup & Folder Structure](#1-project-setup--folder-structure)
2. [Ice Pick Prefab](#2-ice-pick-prefab)
3. [Attaching Picks to Controllers](#3-attaching-picks-to-controllers)
4. [Swing Detection (Velocity Tracking)](#4-swing-detection-velocity-tracking)
5. [Surface Tags & Layers](#5-surface-tags--layers)
6. [Pick Embed System](#6-pick-embed-system)
7. [Grip & Release Input](#7-grip--release-input)
8. [Climbing Locomotion](#8-climbing-locomotion)
9. [Fall & Respawn Trigger](#9-fall--respawn-trigger)
10. [Feedback: Audio & Haptics](#10-feedback-audio--haptics)
11. [Test Scene Setup](#11-test-scene-setup)
12. [Tunable Parameters Summary](#12-tunable-parameters-summary)
13. [Integration Points](#13-integration-points)

---

## 1. Project Setup & Folder Structure

Create the following directory structure under `Assets/`:

```
Assets/
  IcePEAK/
    Prefabs/
      IcePick.prefab
      TestIceSurface.prefab      (temporary, for testing)
      TestRockSurface.prefab     (temporary, for testing)
    Scripts/
      IcePick/
        IcePickController.cs
        SwingDetector.cs
        ClimbingLocomotion.cs
        PlayerFallHandler.cs
      Surfaces/
        SurfaceType.cs           (enum: Ice, Rock)
        SurfaceTag.cs            (MonoBehaviour on every climbable surface)
    Materials/
      IcePickMetal.mat
    Audio/
      PickEmbed.wav
      PickBounce.wav
    Scenes/
      ClimbingTestScene.unity
```

**Steps:**
1. In Unity, create the folder tree above via right-click > Create > Folder.
2. Keep all IcePEAK gameplay assets under `Assets/IcePEAK/` to separate them
   from the VR template files.
3. Do not delete the existing `VRTemplateAssets/` or `Samples/` — they contain
   XRI starter prefabs you will reference.

---

## 2. Ice Pick Prefab

Each ice pick is a single prefab instantiated twice (left hand, right hand).

### 2.1 Model & Hierarchy

```
IcePick (root GameObject)
  ├── Model            (MeshRenderer — the visual pick mesh)
  ├── TipCollider      (child with a small trigger collider at the pick's tip)
  └── AudioSource      (for embed/bounce SFX)
```

**Steps:**
1. **Create a placeholder mesh.** Until the final art is ready, use a capsule
   scaled to roughly `(0.03, 0.15, 0.03)` as the shaft and a small flattened
   cube `(0.01, 0.05, 0.02)` rotated 45 degrees as the pick head. Parent both
   under a `Model` empty.
2. **Add TipCollider.** Create an empty child `TipCollider` positioned at the
   sharp end of the pick head. Add a `SphereCollider` (radius ~0.02 m) and mark
   it as **Is Trigger = true**. This is the collision sensor that detects
   surfaces.
3. **Add a Rigidbody** to the root `IcePick` object:
   - `Is Kinematic = true` (the pick follows the controller, not physics).
   - `Use Gravity = false`.
   - A kinematic Rigidbody is required for `OnTriggerEnter` to fire.
4. **Add an AudioSource** component to the root. Set `Play On Awake = false`,
   `Spatial Blend = 1` (3D).
5. **Save as prefab** at `Assets/IcePEAK/Prefabs/IcePick.prefab`.

### 2.2 Why a Trigger Collider (Not a Physics Collider)

Using a trigger avoids the pick physically bouncing off surfaces. We want the
script to decide embed vs. bounce — a trigger gives us `OnTriggerEnter` without
physics forces.

---

## 3. Attaching Picks to Controllers

The picks must track the Quest 3 controllers exactly so the player's hand motion
maps 1:1 to the pick's motion.

**Steps:**
1. Open your XR Origin rig (from the VR template or XRI starter assets). It
   should have a hierarchy like:

   ```
   XR Origin (XR Rig)
     ├── Camera Offset
     │     ├── Main Camera
     │     ├── Left Controller   (with XR Controller component)
     │     └── Right Controller  (with XR Controller component)
   ```

2. **Instantiate two IcePick prefabs** and child them to the controllers:
   - Drag `IcePick.prefab` as a child of `Left Controller`. Rename it
     `IcePick_Left`.
   - Duplicate and child to `Right Controller`. Rename `IcePick_Right`.
3. **Adjust the local transform** so the pick extends naturally from the hand:
   - Position: roughly `(0, 0, 0.08)` so it extends forward from the
     controller.
   - Rotation: adjust so the sharp end points outward/upward at a natural grip
     angle. This will require playtesting — start with `(45, 0, 0)` on X.
4. **Verify tracking.** Enter Play mode with the headset connected. Swing your
   arms and confirm the picks follow smoothly with no lag or jitter.

> **Note:** Because the picks are children of the controller GameObjects, their
> world-space position and velocity can be derived each frame for swing
> detection.

---

## 4. Swing Detection (Velocity Tracking)

The design doc requires a **minimum velocity threshold** to distinguish an
intentional swing from casual movement. The pick should only embed if the player
swings hard enough.

### 4.1 Script: `SwingDetector.cs`

```csharp
using UnityEngine;

public class SwingDetector : MonoBehaviour
{
    [Header("Velocity Tracking")]
    [Tooltip("Number of frames to average velocity over (smoothing)")]
    [SerializeField] private int velocityFrameWindow = 5;

    [Header("Thresholds")]
    [Tooltip("Minimum tip speed (m/s) to count as a valid swing")]
    [SerializeField] private float embedVelocityThreshold = 1.5f;

    // --- Public API ---
    public float CurrentSpeed => _currentSpeed;
    public Vector3 CurrentVelocity => _currentVelocity;
    public bool IsSwingFastEnough => _currentSpeed >= embedVelocityThreshold;

    // --- Private ---
    private Vector3[] _previousPositions;
    private int _frameIndex;
    private Vector3 _currentVelocity;
    private float _currentSpeed;

    private void Start()
    {
        _previousPositions = new Vector3[velocityFrameWindow];
        for (int i = 0; i < velocityFrameWindow; i++)
            _previousPositions[i] = transform.position;
    }

    private void Update()
    {
        // Store current position
        _previousPositions[_frameIndex] = transform.position;

        // Compute velocity as delta between oldest and newest sample
        int oldestIndex = (_frameIndex + 1) % velocityFrameWindow;
        float timeDelta = Time.deltaTime * velocityFrameWindow;

        if (timeDelta > 0f)
        {
            _currentVelocity = (_previousPositions[_frameIndex]
                              - _previousPositions[oldestIndex]) / timeDelta;
            _currentSpeed = _currentVelocity.magnitude;
        }

        _frameIndex = (_frameIndex + 1) % velocityFrameWindow;
    }
}
```

**Steps:**
1. Create `Assets/IcePEAK/Scripts/IcePick/SwingDetector.cs` with the code above.
2. Attach `SwingDetector` to the **TipCollider** child of each IcePick prefab
   (we track the tip's velocity, not the handle's).
3. The `embedVelocityThreshold` is exposed in the Inspector for tuning. Start
   with **1.5 m/s** — too low and gentle movements embed; too high and the game
   feels unresponsive. Playtest to dial in.

### 4.2 Why Track the Tip, Not the Controller

The tip moves faster than the hand during a swing (longer lever arm). Tracking
the tip gives a more accurate read on swing intent and makes the threshold
easier to tune — a wrist flick that barely moves the controller can still whip
the tip fast enough.

### 4.3 Alternative: XR Controller Velocity

XRI's `ActionBasedController` exposes device velocity via the
`XRBaseController.velocity` property. You could read this instead of computing
it manually. Pros: less code. Cons: it measures the controller origin, not the
tip, so the threshold values will differ and the feel may be less precise. Either
approach works — pick one and commit.

---

## 5. Surface Tags & Layers

Surfaces need to be identifiable so the pick knows whether to embed (ice) or
bounce (rock).

### 5.1 Script: `SurfaceType.cs` (enum)

```csharp
public enum SurfaceType
{
    Ice,
    Rock
}
```

### 5.2 Script: `SurfaceTag.cs`

```csharp
using UnityEngine;

public class SurfaceTag : MonoBehaviour
{
    [SerializeField] private SurfaceType surfaceType = SurfaceType.Ice;

    public SurfaceType Type => surfaceType;
}
```

**Steps:**
1. Create both scripts under `Assets/IcePEAK/Scripts/Surfaces/`.
2. Attach `SurfaceTag` to every climbable surface in the scene. Set the dropdown
   to `Ice` or `Rock` as appropriate.
3. Optionally also use Unity Tags (`Edit > Project Settings > Tags and Layers`)
   and add `Ice` and `Rock` as tags. The `SurfaceTag` component is more flexible
   (can carry additional data like ice crack duration later), but Unity tags work
   for quick `CompareTag` checks.

### 5.3 Physics Layers (Recommended)

Create two layers in Project Settings:
- Layer 8: `ClimbableSurface`
- Layer 9: `IcePickTip`

In the Physics collision matrix (`Edit > Project Settings > Physics`), enable
collisions **only** between `IcePickTip` and `ClimbableSurface`. This prevents
the tip trigger from firing on unrelated geometry (floor, skybox colliders,
etc.).

---

## 6. Pick Embed System

This is the core mechanic: when the tip trigger touches an ice surface and the
swing is fast enough, the pick "embeds" — it locks in place and the player can
hold on.

### 6.1 Script: `IcePickController.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody))]
public class IcePickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SwingDetector swingDetector;
    [SerializeField] private Transform tipTransform;
    [SerializeField] private AudioClip embedSound;
    [SerializeField] private AudioClip bounceSound;
    [SerializeField] private AudioSource audioSource;

    [Header("Embed Settings")]
    [Tooltip("How deep the pick tip sinks into the surface on embed (meters)")]
    [SerializeField] private float embedDepth = 0.03f;

    // --- Public API ---
    public bool IsEmbedded => _isEmbedded;
    public Vector3 EmbedWorldPosition => _embedWorldPos;

    /// Invoked when the pick first embeds in an ice surface.
    public System.Action<IcePickController, SurfaceTag> OnEmbedded;

    /// Invoked when the pick is released (grip released or ice shattered).
    public System.Action<IcePickController> OnReleased;

    // --- Private ---
    private bool _isEmbedded;
    private Vector3 _embedWorldPos;
    private Transform _controllerParent;   // original parent (the controller)
    private Vector3 _localPosInParent;     // original local position
    private Quaternion _localRotInParent;   // original local rotation

    private void Awake()
    {
        _controllerParent = transform.parent;
        _localPosInParent = transform.localPosition;
        _localRotInParent = transform.localRotation;
    }

    // --- Trigger Detection ---
    private void OnTriggerEnter(Collider other)
    {
        if (_isEmbedded) return;

        SurfaceTag surface = other.GetComponentInParent<SurfaceTag>();
        if (surface == null) return;

        if (surface.Type == SurfaceType.Ice && swingDetector.IsSwingFastEnough)
        {
            Embed(surface);
        }
        else
        {
            Bounce(surface.Type);
        }
    }

    // --- Embed ---
    private void Embed(SurfaceTag surface)
    {
        _isEmbedded = true;

        // Detach from controller so the pick stays fixed in world space
        _embedWorldPos = tipTransform.position;
        transform.SetParent(null, worldPositionStays: true);

        // Nudge the pick slightly into the surface for visual sell
        transform.position += tipTransform.forward * embedDepth;

        // Audio + haptics
        audioSource.PlayOneShot(embedSound);
        // Haptics are sent via the input system (see Section 10)

        OnEmbedded?.Invoke(this, surface);
    }

    // --- Bounce ---
    private void Bounce(SurfaceType type)
    {
        audioSource.PlayOneShot(bounceSound);
        // Spark VFX for rock can be added here later
    }

    // --- Release ---
    public void Release()
    {
        if (!_isEmbedded) return;

        _isEmbedded = false;

        // Re-attach to controller
        transform.SetParent(_controllerParent);
        transform.localPosition = _localPosInParent;
        transform.localRotation = _localRotInParent;

        OnReleased?.Invoke(this);
    }
}
```

### 6.2 How Embedding Works

1. Player swings. `SwingDetector` tracks tip velocity each frame.
2. Tip trigger enters an ice surface collider.
3. `OnTriggerEnter` fires. Script checks `swingDetector.IsSwingFastEnough`.
4. If yes and surface is Ice:
   - **Detach** the pick from the controller parent (so it stays fixed in world
     space while the controller keeps moving).
   - Record the embed world position.
   - Play embed SFX and send haptic pulse.
   - Fire `OnEmbedded` event (the ice destruction system listens to this to
     start the crack timer).
5. If the surface is Rock, or the swing is too slow: play the bounce SFX and
   do nothing else.

### 6.3 Setup Steps

1. Create `Assets/IcePEAK/Scripts/IcePick/IcePickController.cs`.
2. Attach to the root of each `IcePick` prefab.
3. Wire the Inspector references:
   - `swingDetector` → the `SwingDetector` on `TipCollider`.
   - `tipTransform` → the `TipCollider` transform.
   - `audioSource` → the AudioSource on the root.
   - `embedSound` / `bounceSound` → placeholder audio clips.
4. Ensure `TipCollider` has a trigger `SphereCollider` and is on the
   `IcePickTip` layer.
5. Ensure every ice/rock surface has a collider (non-trigger) and is on the
   `ClimbableSurface` layer.

### 6.4 Edge Cases to Handle

- **Double embed.** The `if (_isEmbedded) return;` guard prevents one pick from
  embedding in two surfaces simultaneously.
- **Embed in mid-air.** Can only happen if a collider floats. Won't occur with
  proper level design, but the layer matrix prevents triggers on non-surface
  geometry as an extra safeguard.
- **Surface destroyed while embedded.** The ice destruction system should call
  `Release()` on any pick embedded in that surface before shattering.

---

## 7. Grip & Release Input

The player holds the controller **grip button** to stay attached to an embedded
pick. Releasing grip (or ice shattering) frees the pick and returns it to the
controller.

### 7.1 Input Action Setup

XRI 3.3 uses the Input System. The VR template likely already has grip actions
mapped. Verify or create:

1. Open `Assets/XRI/Settings/` (or wherever XRI input actions are stored).
2. Confirm there is an action map with a **Grip** action bound to
   `<XRController>{LeftHand}/grip` and `<XRController>{RightHand}/grip`.
   The VR template's default action map (`XRI Default Input Actions`) should
   already include this.

### 7.2 Reading Grip in `IcePickController`

Add grip handling to `IcePickController`:

```csharp
using UnityEngine.InputSystem;

// Add to IcePickController class:

[Header("Input")]
[SerializeField] private InputActionReference gripAction;
[Tooltip("Grip value below which the player is considered to have released")]
[SerializeField] private float gripReleaseThreshold = 0.3f;

private void Update()
{
    if (!_isEmbedded) return;

    float gripValue = gripAction.action.ReadValue<float>();
    if (gripValue < gripReleaseThreshold)
    {
        Release();
    }
}
```

**Steps:**
1. Add `gripAction` field to `IcePickController`.
2. In the Inspector, assign the appropriate grip `InputActionReference` for each
   hand (left grip for left pick, right grip for right pick).
3. Set `gripReleaseThreshold` to `0.3` — this gives a comfortable dead zone so
   slight finger relaxation doesn't drop the hold.

### 7.3 Behavior Summary

| State        | Grip Pressed        | Grip Released            |
|------------- |---------------------|--------------------------|
| Not embedded | No effect           | No effect                |
| Embedded     | Stay locked in place| `Release()` → return to controller |

---

## 8. Climbing Locomotion

When a pick is embedded and the player pulls their controller downward, the XR
Origin (camera rig) moves **upward** by the inverse of that displacement. This
creates the sensation of pulling yourself up the wall.

### 8.1 Script: `ClimbingLocomotion.cs`

```csharp
using UnityEngine;

public class ClimbingLocomotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private IcePickController leftPick;
    [SerializeField] private IcePickController rightPick;

    // Track the controller world position at the moment climbing starts
    private Vector3 _leftGrabStartPos;
    private Vector3 _rightGrabStartPos;
    private bool _wasLeftEmbedded;
    private bool _wasRightEmbedded;

    private void OnEnable()
    {
        leftPick.OnEmbedded += HandleEmbed;
        rightPick.OnEmbedded += HandleEmbed;
        leftPick.OnReleased += HandleRelease;
        rightPick.OnReleased += HandleRelease;
    }

    private void OnDisable()
    {
        leftPick.OnEmbedded -= HandleEmbed;
        rightPick.OnEmbedded -= HandleEmbed;
        leftPick.OnReleased -= HandleRelease;
        rightPick.OnReleased -= HandleRelease;
    }

    private void HandleEmbed(IcePickController pick, SurfaceTag _)
    {
        // Snapshot the controller position at the moment of embed
        if (pick == leftPick)
        {
            _leftGrabStartPos = GetControllerWorldPos(leftPick);
            _wasLeftEmbedded = true;
        }
        else
        {
            _rightGrabStartPos = GetControllerWorldPos(rightPick);
            _wasRightEmbedded = true;
        }
    }

    private void HandleRelease(IcePickController pick)
    {
        if (pick == leftPick) _wasLeftEmbedded = false;
        else _wasRightEmbedded = false;
    }

    private void Update()
    {
        Vector3 totalDelta = Vector3.zero;
        int activeHands = 0;

        if (leftPick.IsEmbedded)
        {
            Vector3 currentPos = GetControllerWorldPos(leftPick);
            Vector3 delta = _leftGrabStartPos - currentPos;
            totalDelta += delta;
            _leftGrabStartPos = currentPos;
            activeHands++;
        }

        if (rightPick.IsEmbedded)
        {
            Vector3 currentPos = GetControllerWorldPos(rightPick);
            Vector3 delta = _rightGrabStartPos - currentPos;
            totalDelta += delta;
            _rightGrabStartPos = currentPos;
            activeHands++;
        }

        if (activeHands > 0)
        {
            // Average the two hands if both are gripping
            totalDelta /= activeHands;
            xrOrigin.position += totalDelta;
        }
    }

    /// Gets the world position of the controller that owns this pick.
    /// When the pick is embedded (detached from controller), we still
    /// need the controller's position — walk up to find it.
    private Vector3 GetControllerWorldPos(IcePickController pick)
    {
        // When embedded, the pick is detached. We stored the original
        // parent (the controller transform) — use that.
        // Access via a public property added to IcePickController.
        return pick.ControllerTransform.position;
    }
}
```

> **Required addition to `IcePickController`:** expose the controller transform:
> ```csharp
> public Transform ControllerTransform => _controllerParent;
> ```

### 8.2 How It Works

1. When a pick embeds, snapshot the controller's world position at that instant.
2. Each frame, compute how far the controller has moved since the snapshot
   (the delta).
3. Apply the **inverse** of that delta to the `XR Origin` transform.
   - Player pulls controller **down** → delta is negative Y → inverse is
     positive Y → rig moves **up**. The player rises.
   - Player pushes controller **left** → delta is negative X → inverse is
     positive X → rig moves **right**. Lateral traversal.
4. Update the snapshot to the current position so deltas are per-frame, not
   cumulative.
5. If both hands are embedded, average the two deltas to prevent conflicting
   motion.

### 8.3 Setup Steps

1. Create `Assets/IcePEAK/Scripts/IcePick/ClimbingLocomotion.cs`.
2. Attach to the `XR Origin` GameObject (or a manager object in the scene).
3. Wire the Inspector references:
   - `xrOrigin` → the `XR Origin` transform.
   - `leftPick` → `IcePick_Left`'s `IcePickController`.
   - `rightPick` → `IcePick_Right`'s `IcePickController`.

### 8.4 Disabling Default Locomotion

The VR template likely enables joystick/teleport locomotion by default. Disable
these so climbing is the only movement:

1. On the `XR Origin`, find any `Locomotion System`, `Continuous Move Provider`,
   `Teleportation Provider`, or `Snap Turn Provider` components.
2. **Disable** them (don't delete — you may want snap-turn back later for
   comfort).
3. Leave the `Tracked Pose Driver` on the camera and controllers — those must
   stay.

### 8.5 Gravity & Idle Behavior

When no pick is embedded and the player is not on a platform, they should fall.
This is handled by `PlayerFallHandler` (Section 9), not by the climbing script.
`ClimbingLocomotion` only moves the rig when at least one pick is embedded; when
both are free, it does nothing.

---

## 9. Fall & Respawn Trigger

Per the design doc: when both hands lose support and the player is not on a rock
platform, the player falls and respawns at the last checkpoint.

### 9.1 Script: `PlayerFallHandler.cs`

```csharp
using UnityEngine;
using System.Collections;

public class PlayerFallHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private IcePickController leftPick;
    [SerializeField] private IcePickController rightPick;
    [SerializeField] private Transform xrOrigin;

    [Header("Platform Detection")]
    [Tooltip("Layer mask for rock platforms the player can stand on")]
    [SerializeField] private LayerMask platformLayer;
    [Tooltip("Raycast distance below the camera to detect platform")]
    [SerializeField] private float groundCheckDistance = 0.3f;

    [Header("Fall Settings")]
    [SerializeField] private float fallGravity = 9.8f;
    [Tooltip("Seconds of free-fall before triggering respawn")]
    [SerializeField] private float fallDurationBeforeRespawn = 1.5f;

    [Header("Respawn")]
    [SerializeField] private Transform defaultSpawnPoint;

    // --- Public ---
    public Transform CurrentCheckpoint { get; set; }

    // --- Private ---
    private bool _isFalling;
    private float _fallTimer;
    private float _fallVelocity;

    private void Start()
    {
        CurrentCheckpoint = defaultSpawnPoint;
    }

    private void Update()
    {
        bool hasSupport = leftPick.IsEmbedded
                       || rightPick.IsEmbedded
                       || IsOnPlatform();

        if (hasSupport)
        {
            _isFalling = false;
            _fallTimer = 0f;
            _fallVelocity = 0f;
            return;
        }

        // No support — start falling
        _isFalling = true;
        _fallVelocity += fallGravity * Time.deltaTime;
        xrOrigin.position += Vector3.down * _fallVelocity * Time.deltaTime;
        _fallTimer += Time.deltaTime;

        if (_fallTimer >= fallDurationBeforeRespawn)
        {
            StartCoroutine(Respawn());
        }
    }

    private bool IsOnPlatform()
    {
        // Raycast downward from the XR Origin to check for ground
        Vector3 origin = xrOrigin.position;
        return Physics.Raycast(origin, Vector3.down, groundCheckDistance,
                               platformLayer);
    }

    private IEnumerator Respawn()
    {
        _isFalling = false;
        _fallTimer = 0f;
        _fallVelocity = 0f;

        // TODO: Fade screen to black here (use VR screen fader)

        yield return new WaitForSeconds(0.5f);

        // Force-release both picks
        leftPick.Release();
        rightPick.Release();

        // Teleport to checkpoint
        xrOrigin.position = CurrentCheckpoint.position;

        // TODO: Fade screen back in
        // TODO: Reset nearby ice surfaces to intact (ice destruction system)

        yield return null;
    }
}
```

### 9.2 Setup Steps

1. Create `Assets/IcePEAK/Scripts/IcePick/PlayerFallHandler.cs`.
2. Attach to the `XR Origin` or a `GameManager` object.
3. Wire Inspector references (both picks, XR Origin, platform layer mask,
   default spawn point).
4. Create a Unity layer `RockPlatform` (e.g., layer 10) and assign it to all
   rock platform GameObjects.
5. Set the `platformLayer` field to `RockPlatform`.

### 9.3 Checkpoint Integration

The checkpoint system (a separate task) will set
`PlayerFallHandler.CurrentCheckpoint` whenever the player reaches a new rock
platform checkpoint. For now, set `defaultSpawnPoint` to an empty GameObject at
the starting position.

---

## 10. Feedback: Audio & Haptics

Feedback makes or breaks the feel of the picks. Even with placeholder assets,
get feedback in early.

### 10.1 Audio

| Event              | Sound                             | Priority |
|--------------------|-----------------------------------|----------|
| Pick embeds in ice | Short, punchy "thunk" (ice crack)  | High     |
| Pick bounces off rock | Metallic scrape/spark            | High     |
| Grip release       | Soft slide/release                | Medium   |
| Glancing blow (too slow) | Light tap / dull thud        | Medium   |

For placeholders, use any short impact SFX. Assign them to the `embedSound` and
`bounceSound` fields on `IcePickController`.

### 10.2 Haptics

Use `UnityEngine.XR.Interaction.Toolkit.XRBaseController.SendHapticImpulse()` or
the Input System's `OpenXRDevice.SendHapticImpulse()`.

Add a helper method to `IcePickController`:

```csharp
using UnityEngine.XR.Interaction.Toolkit;

// Add field:
[SerializeField] private ActionBasedController xrController;

private void SendHaptic(float amplitude, float durationSeconds)
{
    xrController.SendHapticImpulse(amplitude, durationSeconds);
}
```

Call it at key moments:

| Event            | Amplitude | Duration |
|------------------|-----------|----------|
| Embed in ice     | 0.7       | 0.1s     |
| Bounce off rock  | 0.3       | 0.05s    |
| Ice cracking (escalating) | 0.2 → 0.9 | continuous |
| Ice shatters under pick   | 1.0       | 0.15s    |

### 10.3 Setup Steps

1. Add `xrController` field to `IcePickController`.
2. In the Inspector, assign the `ActionBasedController` from the corresponding
   hand's controller GameObject.
3. Call `SendHaptic()` inside `Embed()` and `Bounce()`.
4. The ice destruction system will call a public `SendHaptic()` on the pick
   for cracking/shatter events.

---

## 11. Test Scene Setup

Build a minimal scene to test the picks end-to-end before any real level content
exists.

### 11.1 Scene: `ClimbingTestScene`

1. **Create a new scene** at `Assets/IcePEAK/Scenes/ClimbingTestScene.unity`.
2. **Add the XR Origin** from the VR template (or XRI starter prefab). Ensure
   it has the camera, both controllers, and the two IcePick children set up per
   Sections 2–3.
3. **Add a floor plane** at Y=0. Tag it as `Rock` (or assign the
   `RockPlatform` layer). This is the starting platform.
4. **Add a vertical ice wall.** Create a Cube scaled to `(3, 5, 0.3)`,
   positioned at `(0, 2.5, 1)` so it stands in front of the player.
   - Add a `SurfaceTag` component, set to `Ice`.
   - Assign to the `ClimbableSurface` layer.
   - Add a collider (the default BoxCollider is fine; leave `Is Trigger = false`
     — the pick tip's trigger will detect the wall's non-trigger collider).
5. **Add a rock wall.** Create another Cube scaled to `(3, 2, 0.3)` next to
   the ice wall.
   - `SurfaceTag` → `Rock`.
   - Assign to `ClimbableSurface` layer.
6. **Add a rock platform ledge** at Y=5 above the ice wall (a Cube scaled
   `(3, 0.3, 2)` at `(0, 5, 1)`). Layer: `RockPlatform`. This is the climb
   target.
7. **Add a spawn point** empty GameObject at `(0, 1.5, 0)` on the floor.
   Assign to `PlayerFallHandler.defaultSpawnPoint`.
8. **Add `ClimbingLocomotion`** to the XR Origin.
9. **Add `PlayerFallHandler`** to the XR Origin.
10. **Add directional light and skybox** for visibility.

### 11.2 Testing Checklist

- [ ] Swing at ice wall — pick embeds, hear thunk, feel haptic.
- [ ] Swing at rock wall — pick bounces, hear scrape, feel lighter haptic.
- [ ] Slow movement near ice wall — pick does NOT embed.
- [ ] While embedded, hold grip — pick stays locked.
- [ ] While embedded, release grip — pick returns to controller.
- [ ] While embedded, pull controller down — XR Origin moves up.
- [ ] Climb to top of ice wall and reach rock ledge — stand safely.
- [ ] Release both picks in mid-air — fall and respawn at spawn point.
- [ ] Embed left pick, then right pick — both hold, locomotion averages.
- [ ] Embed left, release left, grab right — transition is smooth.

---

## 12. Tunable Parameters Summary

All key values should be `[SerializeField]` fields so designers can adjust in
the Inspector without code changes.

| Parameter                  | Script               | Default | Notes                            |
|--------------------------- |----------------------|---------|----------------------------------|
| `embedVelocityThreshold`   | `SwingDetector`      | 1.5 m/s | Lower = easier swings            |
| `velocityFrameWindow`      | `SwingDetector`      | 5       | Higher = smoother but laggier    |
| `embedDepth`               | `IcePickController`  | 0.03 m  | Visual only — how far tip sinks  |
| `gripReleaseThreshold`     | `IcePickController`  | 0.3     | 0–1 float, analog grip threshold |
| `groundCheckDistance`      | `PlayerFallHandler`  | 0.3 m   | Raycast length below rig         |
| `fallGravity`              | `PlayerFallHandler`  | 9.8     | Acceleration during free-fall    |
| `fallDurationBeforeRespawn`| `PlayerFallHandler`  | 1.5 s   | Seconds of fall before respawn   |

---

## 13. Integration Points

The ice pick system connects to several other systems that will be built later.
Document these interfaces now so future work plugs in cleanly.

### 13.1 Ice Destruction System (Task 2)

- **Interface:** `IcePickController.OnEmbedded` event. The ice destruction
  system subscribes to this. When a pick embeds in an ice surface, the
  destruction system starts the crack timer on that surface.
- **Reverse interface:** When ice shatters, the destruction system must call
  `IcePickController.Release()` on any pick currently embedded in that surface.
  The destruction system needs a reference to all picks (or queries for them via
  the `SurfaceTag`).

### 13.2 Cold Spray Gadget (Task 4.2)

- No direct interface with the pick system. Cold spray modifies the crack timer
  on an ice `SurfaceTag`, which is independent of the pick.

### 13.3 Pole Gadget (Task 4.5)

- Poles create an anchor with an `Ice`-tagged collider on a rock surface. The
  pick system needs **no changes** — the pick's `OnTriggerEnter` will detect
  the pole's `Ice`-tagged collider exactly like any ice surface.

### 13.4 Checkpoint System (Task 3.2)

- The checkpoint system sets `PlayerFallHandler.CurrentCheckpoint` when the
  player reaches a new platform. Expose this as a public property (already done
  in the script above).

### 13.5 Player Body & IK (Task 8)

- The IK system will read controller positions and the pick's embed state to
  drive arm poses. The IK system consumes `IcePickController.IsEmbedded` and
  `IcePickController.EmbedWorldPosition` — both are already public.

---

## Implementation Order

For the cleanest workflow, build in this order:

1. **Surface tags & layers** (Section 5) — 15 min. Set up tags and physics
   layers so everything else has surfaces to hit.
2. **Ice Pick prefab** (Section 2) — 30 min. Build the placeholder model,
   tip collider, rigidbody, audio source.
3. **Attach to controllers** (Section 3) — 15 min. Child picks to XR
   controllers, adjust transforms.
4. **Swing detection** (Section 4) — 30 min. Write and attach `SwingDetector`.
5. **Pick embed system** (Section 6) — 1 hr. Write `IcePickController`,
   test embed/bounce.
6. **Grip & release** (Section 7) — 30 min. Add grip input reading.
7. **Climbing locomotion** (Section 8) — 1 hr. Write `ClimbingLocomotion`,
   disable default locomotion, test climbing.
8. **Fall & respawn** (Section 9) — 45 min. Write `PlayerFallHandler`, test
   fall from mid-air.
9. **Audio & haptics** (Section 10) — 30 min. Wire placeholder SFX and
   haptic calls.
10. **Test scene** (Section 11) — 30 min. Build the test scene and run through
    the full checklist.

**Total estimated hands-on time: ~5–6 hours** for a first working prototype
with placeholder art and audio.
