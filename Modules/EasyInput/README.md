# Input in EasyGameFramework

One `PlayerInputSettings` owns a player's runtime actions, sensitivity and rebinding. Controllers, prompts and windows borrow those actions; each context owner releases only its own request. Source InputActionAsset and InputProfile assets remain configuration.

## First player

1. Install EasyGameFramework through the common Setup and provide ECM2. Cinemachine, VFX Graph and New Input System are standard framework dependencies. Use **Input System Package (New)** in Active Input Handling.
2. Use a scene with a collider floor. Place `Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab` above it and add the corresponding camera prefab. Select the player and run **Tools > EasyGameFramework > Input > Connect Selected Player**.
3. The command reuses/adds one PlayerInputSettings and connects EasyMovementInput, InteractionActor, EasyInteractionInput, InteractionPresenter and PlayerFeedbackContext. Empty CharacterStepController context links inherit this player feedback context; explicit providers are preserved. It touches only that character, is repeatable and supports Undo. Leave Source Actions/Profile empty for the standard controls.
4. Add **EasyGameFramework > Input > Input Settings Panel** to the settings object for a small standalone settings view. Assign Player. Press Play: WASD/mouse move/look; Space jumps; E interacts and releases a held object, including when looking away. Tab/Start opens settings. Change sensitivity, rebind Interact to K and see the prompt update immediately.
For a pickup check, create a cube about two metres in front of the camera, add Rigidbody and GrabAction (default mass/settings). E picks it up; turn the camera and press E again to release it even outside the crosshair. This is optional scene content, not a second input reader.

5. For framework windows, use the EasyUI-owned `EasyInputWindowProvider` on WindowManager. It manages the same actions and one request per window. Do not attach InputUIActions to the same input module. InputUIActions is for a plain uGUI EventSystem without WindowManager.

Expected: one settings owner, a single runtime asset, immediate sensitivity/prompt updates, no gameplay press while a blocking window/rebind is active, and input resumes after the opening button/stick is released.

All production adapters are available immediately under `Integrations/EasyInput`; importing Samples is unnecessary. Optional demos only: import Getting Started or EasyMovement from the framework's examples and use **Tools > EasyGameFramework > Input > Create Getting Started Scene / Create EasyMovement Scene**. These generate project-local scenes under `Assets/EasyGameFramework/Input`. The core inspector works with no sample import.

## Public settings and saving

```csharp
// Global preferences for this user's stable player key, separately from progress slots.
InputSettingsSnapshot saved = player.CaptureSettings();
string json = JsonUtility.ToJson(saved); // hand this payload to EasySave's preferences route

if (player.ValidateSettings(saved, out string error))
    player.RestoreSettings(saved, out error);
// Or player.RestoreSettingsJson(json, out error).

// Before creating any player: read-only preflight against authored configuration.
bool valid = PlayerInputSettings.ValidateSettings(saved, sourceActions, profile, out error);
// Null source/profile uses the module's standard Resources assets.
```

Both ValidateSettings overloads and RestoreSettings use one validation/normalization implementation. Validation does not clone actions, create a player, cancel rebinding, enable maps, mutate settings or raise events. Use the Unity main thread: configuration assets and JsonUtility are read. Invalid restore returns before changing live state. Valid restore cancels a pending rebind and applies this player's settings. Save owns multi-module ordering and partial-failure reporting; input preflight is not a world transaction. Snapshot Version 1 contains values plus binding overrides, not active contexts, controller transforms, runtime devices or ScriptableObject state. Unknown setting keys/removed binding IDs are ignored; missing values use profile defaults.

```csharp
player.MouseSensitivity = 1.5f;
var controls = player.Settings; controls.InvertY = true; player.SetSettings(controls);
var capture = player.BeginRebind(player.Action("Gameplay/Interact"), 0, this);
player.CancelRebind();
player.ResetBinding(player.Action("Gameplay/Interact"), 0);
var display = player.GetPresentation("Gameplay/Interact"); // Text, Sprite, Parts
```

```csharp
InputContextHandle menu = player.AcquireContext(this, InputContextRequest.UI());
// On this owner's close / disable:
menu.Dispose(); // repeated disposal is harmless; other owners remain in force
```

For custom InputActionAssets, preserve consistent map/action names or configure InputActionLink references. Never Enable/Disable a borrowed action in a controller. Place `easyInputStick` on stick Vector2 bindings and `easyInputAxis` on scalar stick bindings. ReadLookDelta applies mouse degrees/pixel or gamepad degrees/second exactly once; consumers must not multiply sensitivity or deltaTime a second time.

## Modules and views

See [integrations](Documentation~/Integrations.md) for Building, Combat, UI, feedback and localization wiring, and [contracts](Documentation~/Contracts.md) for context/capture details. Create configuration through **Create > EasyGameFramework > Input > Profile / Glyph Set**. InputPrompt supplies TextChanged/SpriteChanged. LocalizedInputPrompt supplies CurrentText, Presentation, CurrentSprite and TextChanged/SpriteChanged using standard LocalizedString (`{button}` in a non-Smart entry); both react to binding/device/profile changes.

Use PairDevices for local multiplayer and designate one owner of the shared desktop cursor. Keyboard-only feedback explicitly suppresses global-gamepad fallback. Hardware layouts and native event behavior are covered by virtual-device regressions; this does not certify physical controllers or console platforms.

## Verification

`Tests/EditMode`, `Tests/PlayMode`, `Tests/Integrations` and `Tests/EditorSetup` are part of the framework testable package. Production integrations compile directly; tests require no old imported adapter copies. Use Unity CLI with the shared editor lock after CUTOVER. [Staging validation plan](Documentation~/Validation.md) distinguishes static checks from runtime results. Earlier standalone-package reports are historical and do not validate the unified package.
