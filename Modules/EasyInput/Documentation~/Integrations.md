# Built-in input integrations

All adapters below compile as framework production assemblies under `Integrations/EasyInput`. There are no internal package-version guards, manual sample prerequisites or duplicate runtime copies in Samples. Core Input retains only Unity Input System/native module dependencies; integrations form leaf assemblies over the relevant module APIs.

| Integration | Components and setup |
|---|---|
| Player | `EasyGameFramework.Editor.InputPlayerSetup.Connect(FirstPersonCharacterController)` (Editor only) wires one selected FPS player. Menu: Tools > EasyGameFramework > Input > Connect Selected Player. No global scene search. |
| EasyMovement | EasyMovementInput beside the character, same Player. Uses InputBlocked/AcquireInputBlock contract; scripted approach is never overwritten by manual callbacks or zero movement. Held buttons require release after the block. |
| EasyInteraction | EasyInteractionInput on the actor; InteractionPresenter uses it for labels/glyphs/toggle. Primary calls Actor.PressPrimary(out resolved, toggle): own active Grab precedes hover, including no hover/behind camera. Secondary explicit actions use Actor.Press(action). No mirrored priority logic, no physics changes. |
| EasyBuilding | EasyBuildingInput beside Placer, Player and ViewCamera assigned. UseFirstPersonPreset; select through the same BuildingActor session and call Enter. Exit/cancel/disable ends this request; nested window requests remain independent. Strategy fully blocks Gameplay; FPS retains Move/Look/Sprint/Settings. Never creates/disposes a borrowed session. |
| EasyCombat | EasyCombatInput connects a configured DamageEmitter and Aim. Fire is an InputActionLink (default Gameplay/Fire). Automatic polls held input; disabled player/emitter or blocked action produces no fire. Combat owns cadence, damage, jobs and physics. |
| EasyFeedbacks | PlayerFeedbackContext.Prepare calls WithDefaultGamepad(Player ? ActiveGamepad : null), preserving explicit target or explicit silence. InputFeedback follows the same rule without a provider. Assign this context to event adapters; managers/presets remain Feedbacks-owned. |
| Localization | LocalizedInputPrompt uses LocalizedString and Localization.Changed, exposes localized text and current binding Sprite. TextChanged/SpriteChanged connect to views; disable cancels pending work and unsubscribes. Ordinary text fallback remains usable while tables load. |
| uGUI | InputUIActions bridges a plain InputSystemUIInputModule. It validates required UI actions before touching existing configuration, restores the old asset/references/enabled state and suspends module input during capture. |
| EasyUI (owned by EasyUI) | EasyInputWindowProvider owns one context per window and the manager's module. Use it instead of InputUIActions on that module. Stack, focus, child/radial windows remain UI-owned. |
| EasySave (owned by EasySave) | CaptureSettings / ValidateSettings / RestoreSettings for per-user global preferences; no disk/storage or progress-slot ownership in Input. No Input-to-Save dependency. |
| EasySettings | URP/screen settings stay separate. The common menu composes both public APIs; Input keeps sensitivity/bind state. |

For a custom UI action map, use `PassThrough` for `UI/Click` (expected control type `Button`), `UI/Point` and `UI/Scroll` (both `Vector2`). Keep `UI/Navigate` as `Value`/`Vector2` and `UI/Submit` and `UI/Cancel` as `Button`/`Button`. These native InputSystemUIInputModule requirements allow each pointer to supply its own events. The adapters use the player's runtime actions and do not rewrite types in authored custom assets.

Standard controls: Interact E/West; cancel Q/North; next action V/Right Shoulder; secondary F/Select; next target B/D-pad Right; throw G/D-pad Left; rotate R/Left Shoulder plus mouse/right stick; hold distance wheel or D-pad Up/Down. Sensitivity is degrees/pixel for mouse and degrees/second for stick (one deltaTime). Item scalar analog sends a normalized value; the action owns time integration. ItemRotation/InteractionValue are distinct actions sharing physical controls with Look; the adapter consumes Look only through its own scoped request. Closing a consuming context does not replay a still-held stick/button.

Building example:
```csharp
buildingInput.UseFirstPersonPreset(); // authoring setup
buildingActor.GetSession(placer.System).Select(definition);
buildingInput.Enter();
// Cancel/close only this mode:
buildingInput.Exit();
```

Player-bound components should share one PlayerInputSettings. InputActionLink.Reference resolves by action ID into that player's runtime asset; Name is used only when Reference is empty. Clear an optional Name to omit that feature. Re-enable a component after changing its link configuration during Play Mode; runtime rebinding through Player requires no re-enable.

Editor inspectors expose required player/component links first, with advanced bindings folded away and actionable diagnostics. Native ECM2, Cinemachine, VFX Graph and Input System are standard dependencies of the framework. This module has no Netcode/XR/AR/platform SDK addon to move.
