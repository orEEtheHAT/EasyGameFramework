# RigidbodyMotor

Included in EasyGameFramework's Movement module. This is a straight-axis force motor for a Rigidbody. It owns no wheels, suspension, seating, steering, fuel, input devices or save storage.

## First run

1. Add a Collider, **Rigidbody** and **EasyGameFramework > Movement > Rigidbody Motor** to a scene object. It moves along local **+Z** by default. Keep the axis horizontal if gravity must remain outside motor control. Choose collision detection, rotation constraints, friction and gravity for your object as ordinary Rigidbody settings.
2. Leave **Profile** empty for forward 8 / reverse 4 m/s, acceleration 6 / braking 10 m/s² and free coasting. Or assign **Packages > EasyGameFramework > Modules > EasyMovement > Rigidbody > Profiles > CoastingMotor_Sample**. **CoastDragMotor_Sample** adds neutral drag. **Create > EasyGameFramework > Movement > Rigidbody Motor Profile** creates your own reusable tuning asset.
3. From a controller or script, call `motor.SetThrottle(1)`. `-.5f` requests half reverse speed. `SetBrake(1)` brakes; `ResetInput()` releases throttle and brake without changing velocity.
4. For a playable example, import **Rigidbody Motor** from EasyGameFramework's Package Manager Samples. Choose **Tools > EasyGameFramework > Movement > Create Rigidbody Motor Scene**, then Play. W/S or gamepad left stick controls throttle, Space/left trigger brakes, J/gamepad West applies a sideways impulse. The orange block coasts, reverses and collides with the wall. Output is `Assets/EasyGameFramework Examples/Movement/Rigidbody`. The first-run scene opens additively; rerunning returns existing content without overwriting edits.

The sample uses its own New Input System actions. Core does not require PlayerInput, PlayerInputSettings or sample scripts. Bind your own actions to the short API below for production input.

```csharp
using EasyMovement;

motor.SetThrottle(throttle);      // -1..1, fraction of the signed speed limit
motor.SetBrake(brake);            // 0..1; nonzero brake takes priority over throttle
motor.ResetInput();               // call when your input owner disconnects

float speed = motor.CurrentSpeed; // signed m/s along WorldDirection
float cap = motor.EffectiveForwardSpeed;
```

## Physics and units

FixedUpdate applies `AddForce(direction * acceleration, ForceMode.Acceleration)`. Acceleration is in m/s² and is independent of Rigidbody mass. The motor reads velocity; it never replaces it or changes position. Gravity, collisions, sideways motion and unrelated impulses remain physics-owned. A deliberately tilted/vertical axis controls the corresponding part of vertical motion.

Throttle requests a signed target speed. When reversing, the motor brakes toward zero before accelerating in the other direction. Speed increases use **Acceleration**; overspeed/reversal use **Braking**. **SetBrake** multiplies that rate and ignores throttle. **CoastingDeceleration = 0** leaves neutral motion below the speed caps unchanged. Nonzero coasting drag approaches zero at the configured rate.

Forward/reverse caps are soft: axial overspeed is reduced by bounded braking, including at neutral throttle. An external axial impulse is preserved initially but the motor then brakes it toward its current target/cap. External sideways impulses are not damped by this component. Rigidbody damping/friction/collisions can still change them. A zero speed modifier produces zero caps and finite braking, never division by the modifier or an instant velocity clamp.

Changing **Profile** resets input, keeps velocity and live speed modifiers, and uses the new tuning on the next physics step. Runtime never writes the SO. Invalid nonfinite/negative tuning suspends forces and resets input; each value must be at most 100000 and braking must be positive. Kinematic bodies skip forces and reset input. Disable/inactive resets input and expires all speed handles without touching velocity; reacquire after reuse.

## Speed modifiers

```csharp
RigidbodyMotor.SpeedModifier wet = motor.AcquireSpeedMultiplier(this, .5f);
wet.TrySet(.75f);                 // independent owned request
wet.Dispose();                   // idempotent; other owners remain
```

Multipliers affect **forward and reverse speed caps only**. Acceleration, braking and coasting rates stay in physical units. Factors multiply once; each request and the final result are capped at 100. A factor of zero wins. Finite nonnegative values are accepted; negative/NaN/infinity are rejected without replacing a valid handle value. Throttle/brake NaN/infinity instead resets that input to zero. Disabled Behaviour owners, inactive GameObjects/components and destroyed owners expire automatically. The handle exposes `IsActive` and `Value`.

`LocalDirection` accepts a finite nonzero vector and normalizes it. `WorldDirection`, `CurrentSpeed`, `TargetSpeed`, `SpeedMultiplier`, `EffectiveForwardSpeed` and `EffectiveReverseSpeed` describe the current controller state. Inspector diagnostics explain invalid profiles and kinematic bodies.

## EasyCombat, included adapter

Press **Enable Combat Speed Effects** in the motor Inspector. This adds and connects **Rigidbody Combat Speed** and **Movement Influences** in one undoable operation without changing your motor profile or Rigidbody settings. It is safe to call the setup again. To edit a prefab, open Prefab Mode. The runtime component is also available under **Add Component > EasyGameFramework > Movement > Rigidbody Combat Speed**. Combat support is included in the framework; no sample source or separate installation is required. The motor assembly itself has no Combat dependency.

1. For a volume, add a trigger Collider and EasyCombat **Slow Volume** (`MovementSurface`). Assign a **Movement Influence** asset with `SpeedMultiplier = .5`.
2. For timed slow/haste, add EasyCombat **Status Controller** (with Health) to the motor object. Create a **Stat Key**, assign it to **Movement Influences > Movement Speed Stat**, then use that key in a Status `StatModifiers` row. A multiplier above 1 is haste; zero is a full speed-cap stop. Apply the status through Combat's public API.
3. The adapter reads `MovementInfluences.Multiplier` once before the motor step. Combat already combines the strongest spatial slow with the timed stat multiplier. Two zones at .8/.5 and a timed factor 3 produce `.5 * 3 = 1.5`, not .6 or .75. `MovementInfluenceDefinition` remains deliberately limited to [0,1]; haste belongs to timed stat effects.

The body needs one adapter regardless of the number of volumes. The volume needs only its trigger collider and selected effect asset. Do not put a motor adapter on every volume or multiply the status factor a second time. The adapter Inspector explains disabled dependencies and warns about cross-object assignments that could apply Combat twice.

The adapter owns only its speed handle. Disabling it, changing Motor, disabling Influences or pooling releases/rebinds that request; independent gameplay modifiers remain. Zone exit, status expiry and Health life reset follow Combat's existing ownership rules. `Refresh()` is public when a synchronous refresh is needed outside the normal FixedUpdate ordering. Add StatusController before enabling MovementInfluences, or re-enable Influences after adding status support at runtime so Combat refreshes its component cache.

No broad device-performance guarantee is implied. Regression allocation measurements cover warmed managed motor/adapter dispatch with a calibrated main-thread GC.Alloc positive control; sample IMGUI, initialization, physics engine work and handle acquisition are separate workloads.
