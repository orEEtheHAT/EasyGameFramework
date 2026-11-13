# Movement — EasyGameFramework

The Movement module provides an ECM2 first-person controller, a force-based Rigidbody motor and surface footsteps for players and NavMesh NPCs. It includes a player prefab and a separate Cinemachine output camera. Footsteps use the framework's Feedbacks presets. The module ships inside EasyGameFramework 0.1.0; install the framework once, not the former individual packages.

## Installation

1. Install EasyGameFramework and import **Easy Character Movement 2** from your Unity Asset Store purchases. The controller uses ECM2's runtime assembly; commercial ECM2 sources are not redistributed.
2. Set **Active Input Handling** to **Input System Package (New)**. Use the framework's URP setup. Cinemachine, New Input System and VFX Graph are standard dependencies.
3. Module assets live in `Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement`. Production integrations are already compiled; sample imports are only for editable demonstrations.

## First scene

Find the prefabs under **Packages → EasyGameFramework → Modules → EasyMovement → FirstPerson → Prefabs**.

- **FP_MainCamera_Sample** is the scene's output camera, with an AudioListener and a Cinemachine brain. Place it once per local view. If your scene already has a configured output camera, you can use it instead. The sample has post-processing and high-quality temporal anti-aliasing enabled. Its **FP_MainCamera_BlenderSettings** asset starts empty, so the brain uses a **Hard Out** blend of **0.5 s**.
- **FP_Player_Sample** can be placed or spawned on a floor collider on the **Default** layer. Its three Cinemachine views follow the player's **Look Pivot**. Spawning a new player leaves the scene camera in place. Adjust the ECM2 collision mask if your ground uses another layer.
- **First Person FOV** is already attached to the prefab's **Character** child. For player-owned bindings, sensitivity, feedback routing and UI contexts, select the player and use **Tools > EasyGameFramework > Input > Connect Selected Player**. EasyInput owns that setup and its runtime preferences; the basic prefab's direct New Input System reader supports a minimal first run.
- For footstep audio, place one EasyFeedbacks **AudioManager** at the scene root and assign a `FeedbackPreset` in **Character Step Controller → Step Feedback → Default Feedback**. The sample's **Step Sound Origin** is a child at the bottom of the capsule.

Press Play and focus Game View: WASD moves, mouse looks, Space jumps, Shift sprints and Ctrl/C crouches. Walking/sprinting/crouching selects its matching gait view. With a feedback preset assigned, surface footsteps play from the foot origin. No wheel, vehicle, seating or game-specific systems are created.

## Player controls and states

The **First Person Controls** input asset supports WASD and mouse, Space to jump, Shift to sprint, and C or Ctrl to crouch. On a gamepad, use the left stick to move, the right stick to look, the south button to jump, the east button to crouch and the left stick press to sprint. You can assign your own input asset and select a map containing `Move`, `Look`, `Jump`, `Sprint` and `Crouch`. Movement follows the player's body. Gameplay and UI modes control cursor capture.

In **First Person Character Controller → Movement States**, each gait has a speed, camera, and step profile. The sample walks at **5 m/s**, sprints at **8 m/s**, and crouches at **2.5 m/s**. The sprint view uses a 65° field of view, and the crouch view lowers the camera by 0.65 m. Sprint and crouch can be disabled independently. The player inherits platform translation, rotation, and velocity. **ECM2 Motor and Ground Settings** exposes the other motor settings.

To add a gait such as `Injured`, first create it under **Character Step Controller → Movement States**. Then add it under **Custom States** on the player and choose its speed, step profile, and optional camera. Try it in Play Mode with **Test State in Play Mode**. Gameplay can call `TrySetCustomState("Injured")`. UnityEvents can call `SetCustomState(string)` and `ClearCustomState()`. Crouching takes priority, and the custom gait resumes when the player returns to upright ground movement. Disabling the controller clears it. A custom state without an assigned camera uses the walk camera.

## Scripted movement and input blocks

An interaction, cutscene or UI adapter can temporarily take over from manual input:

```csharp
IDisposable inputBlock = character.AcquireInputBlock(this);
// Your script can still call SetMovementDirection, SetRotation and other motor methods.
// Stop your scripted movement when appropriate, then give manual input back:
inputBlock.Dispose();
```

Store the handle for the whole operation, including coroutines, and dispose it in the owner's cleanup. Each handle is independent: releasing one cannot release another operation's block. Disposing twice is harmless. The controller exposes `InputBlocked` and `InputBlockedChanged` for custom readers. Check the property in both your frame update and every input callback; do not write a zero movement vector while blocked. Acquire the block **before** sending scripted movement or button requests. A block does not stop the motor or clear its current direction or velocity; the controlling script decides how to move or stop it.

The built-in reader releases its own Jump/Crouch/Sprint requests once when blocked. Blocked callbacks and disabling a blocked reader do not cancel commands subsequently issued by a script. Move and Look resume immediately after the last block ends. Buttons held across a block, controller disable or reader reconnect must be released and pressed again, including NoClip ascent/descent and toggle. Existing NoClip mode is preserved by input blocks.

Destroyed owners are removed automatically. A disabled `Behaviour`, inactive `GameObject`, or component on an inactive object also expires on the next controller update or `InputBlocked` read. For other `UnityEngine.Object` owners, destruction is the lifetime boundary. Controller disable clears every handle; after pooling, acquire a new handle. Acquiring with an inactive owner/controller returns an empty handle; a null or destroyed owner is an argument error. Derived controllers that implement `Update` must call `base.Update()` to retain automatic owner cleanup when there are no input readers.

## Capturing a player's pose

`CaptureState()` returns a serializable `FirstPersonCharacterState`. Use it with your game's save system:

```csharp
FirstPersonCharacterState saved = character.CaptureState();
// Optional: JsonUtility.ToJson(saved), and FromJson<FirstPersonCharacterState>(json).

using (character.AcquireInputBlock(this))
{
    if (!character.TryRestoreState(saved, out string error))
        Debug.LogWarning(error);
}
```

The snapshot contains world position and rotation (including pending yaw), local view pitch, crouch/sprint requests and the configured custom gait ID. Restore validates the complete snapshot before changing the character. It teleports into normal grounded/falling movement, stops jump, velocity and accumulated forces, clears pending rotation input, preserves existing input blocks and reapplies the gait requests. Crouch still respects capsule clearance, and sprint becomes active when there is movement. Enable the controller and exit NoClip before restoring. Pause other movement providers during restore and choose a saved position valid for the current level.

Save can call `character.ValidateState(saved, out error)` before applying any module. This read-only preflight shares all DTO, pose, controller readiness, capability and custom-state checks with `TryRestoreState`. It does not release input, move the body, query level clearance or reserve readiness. Restore validates again immediately before mutation. Validation failure leaves the character unchanged; successful preflight cannot promise a valid world position or a transaction across modules. The Save module owns storage and loading order.

This is a pose and gait snapshot, not a full physics replay: velocity, platforms, swimming/flight modes, active interactions, input blocks and input sensitivity are not saved. There is no automatic storage or global save manager. EasyInput owns player input settings. Unknown custom gait IDs, unsupported versions, invalid numeric values and unsupported gait capabilities produce a readable error without a partial restore.

## NoClip

With **Allow NoClip** enabled, **F8** or a click of the **right gamepad stick** toggles collision-free flight. The `ToggleNoclip` action is optional in your own input maps. Move forward or back along the look direction, strafe with Move, ascend with Jump, descend with Crouch and boost with Sprint. Tune **Flight Speed** and **Boost Multiplier** in the player inspector.

Toggle again in open space to resume regular movement at the current position. In midair the character falls from there. If its capsule overlaps solid geometry, flight stays active until there is room to exit. Disabling the motor restores collisions and uses the flight entry position if the current position is blocked. Code can call `TrySetNoclip(bool)` to check whether a transition succeeded. Use `ToggleNoclip()` for a UnityEvent.

## Footsteps

For player-owned footstep output, assign an active `IFeedbackContextProvider` component to **Character Step Controller → Context Provider**. Use the included EasyInput `PlayerFeedbackContext` with this player's `PlayerInputSettings`; **Connect Selected Player** creates the provider. Confirm the step controller's Context Provider assignment. No production-adapter sample import is needed. The provider receives the complete step context, including authored volume and position; its returned explicit gamepad or `WithoutGamepad()` choice is preserved.

An empty, invalid, disabled or destroyed provider retains legacy playback, including the `Gamepad.current` fallback. The inspector explains invalid or disabled assignments. Keep the provider active when player-specific routing is required; the step controller does not create managers or store feedback preferences.

For a NavMesh NPC, add **NavMeshFootstepSource** and **CharacterStepController** to the object with the `NavMeshAgent`. The step controller finds a movement source on the same object by default. For a source on another object, assign it in **Motion Source**. The NavMesh source uses the agent's actual speed, suspends steps on off-mesh links, and probes for a surface at foot contact or when you query the ground collider.

Use **+** under **Step Timing → Movement States** to add a named profile. Each profile defines the step distance, feedback volume, head-bob multiplier, and movement amount for explicit steps. The starting distances are **1.95 m** for Walk, **2.25 m** for Sprint and **1.4 m** for Crouch. For example, add `Patrol` with a distance of 2.6 m and set `navMeshSource.StateId = "Patrol"` when the AI starts patrolling. Matching is case-insensitive. A missing state uses the first named profile. An empty list disables footsteps.

Assign a default `FeedbackPreset` and optional physics material overrides. A **FootstepSurface** on the ground collider or its parent takes priority over material entries. Unmatched surfaces and a NavMesh without ground colliders use the default preset. Moving the **Step Sound Origin** changes where the feedback plays. The ground collider still determines the surface. An unassigned origin uses the step controller's transform.

Assign a child transform to **Head Bob Target** to apply the step curves. Once the camera settles, the next walk starts from the target's current position. For animation-driven timing, select **External Events** and call `TriggerLeftStep` or `TriggerRightStep` from animation events. For an explicit step, call `PlayExternalStep("Patrol", -1)`. Subscribe to `Stepped` for the state ID, foot side, speed and surface. `CurrentStateId` and `StepEvent.StateId` expose the resolved state.

## Rigidbody force motor

**Add Component > EasyGameFramework > Movement > Rigidbody Motor** gives a Rigidbody forward/reverse propulsion along one local axis. Leave Profile empty for useful defaults, or assign the provided **CoastingMotor_Sample / CoastDragMotor_Sample** tuning assets. `SetThrottle(-1..1)`, `SetBrake(0..1)` and `ResetInput()` are the complete input interface. Speeds use m/s, acceleration/braking use m/s² independent of mass. It applies bounded forces without replacing velocity, leaving gravity and sideways impulses to physics.

Import the framework's **Rigidbody Motor** sample and choose **Tools > EasyGameFramework > Movement > Create Rigidbody Motor Scene** for an editable New Input System example. Press **Enable Combat Speed Effects** in the motor Inspector to add the included Combat adapter once to the body. It consumes the already-composed strongest-zone/timed-stat multiplier once and scales speed limits only. Each area needs a trigger Collider, Combat Slow Volume and its effect SO. [First run, exact physics/ownership semantics and public API](Documentation~/RigidbodyMotor.md).

## User FOV and gameplay zoom

The shipped prefab includes **First Person FOV** beside the FPS controller. For a custom player, add it using the controller Inspector button. It operates on runtime default/crouch/sprint/custom Cinemachine views and preserves their authored differences. An authored default 60 / sprint 70 becomes 80 / 90 when user base FOV is 80. Each final vertical FOV clamps to 1..179; near the limits, differences can compress. Perspective non-physical scene-camera instances are required.

```csharp
using EasyMovement.FirstPerson;

FirstPersonFov.FovOverride preference = fov.AcquireBaseFovOverride(this, 80);
preference.TrySet(85);                        // update this request, keep its ordering
FirstPersonFov.FovOverride zoom = fov.AcquireOffset(zoomOwner, -15);
float userBase = fov.BaseFieldOfView;          // excludes gameplay offsets
zoom.Dispose();                              // removes only this zoom
preference.Dispose();                        // reveals older owner or authored base
```

The last acquired live base request wins; additive offsets sum. Updating an older request does not move it above another owner. `AuthoredBaseFov`, `IsAvailable`, `UnavailableReason`, `Changed` and `RefreshCameras()` support settings adapters. EasySettings owns the user's base-FOV preference and its Capture/Restore contract; EasySave owns storage. This component stores only transient ownership/captured instance lenses. Callers check availability and handle.IsActive; reacquire after component disable/pooling. NaN/infinity requests are rejected; base values clamp to 1..179 and individual offsets to -178..178.

Camera-reference changes and expired owners are detected without unconditional per-frame lens writes. A disabled Behaviour owner, inactive object or destroyed owner releases its request. Component disable expires all handles and restores captured values still equal to its last write. Gameplay lens effects should use owned offsets; direct Lens.FieldOfView writers while owned are outside this composition contract. Cleanup preserves a newer unrelated direct write. Changed subscribers cannot interrupt ownership cleanup by throwing, and a reentrant ownership change stops the superseded notification dispatch. One FirstPersonFov should own a given set of gait cameras.
