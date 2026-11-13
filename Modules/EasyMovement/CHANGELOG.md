# Changelog

## EasyGameFramework 0.1.0 — staging

- Moved Combat speed support to the framework's production integrations, without former-package version guards. Added one-button, undoable Inspector setup and dependency/duplicate-target guidance.
- Added non-mutating `ValidateState` preflight using the same validation as `TryRestoreState` for the Save module.
- Included the gait FOV owner in the shipped FPS prefab. Kept Input, Settings and Combat public contracts and physics units unchanged.
- Updated setup paths and menus for the single framework; core regressions no longer depend on importing the Rigidbody sample. Added preflight, adapter rebinding/profile immutability and authoring regressions.

The entries below describe the source module's history before framework consolidation; they are not separately installable framework module versions.

## 5.2.0 — 2026-11-13

- Added RigidbodyMotor with reusable tuning profiles, bounded acceleration/braking/coasting, signed forward/reverse targets and independent owned speed factors. Forces affect only the configured axis; velocity, gravity, collisions and external sideways impulses remain physics-owned.
- Added the shipped optional RigidbodyCombatSpeed assembly for EasyCombat 0.7.0+. It consumes MovementInfluences.Multiplier once, including strongest spatial slow and timed haste/slow, without a new mandatory Combat dependency.
- Added an editable New Input System first-run example and two _Sample motor profiles.
- Added FirstPersonFov base overrides and additive gameplay offsets across all authored gait views, preserving relative FOV differences, instance ownership and lifecycle restoration without a competing frame writer or preference storage.
- Added real Rigidbody/collision/trigger/status tests, Cinemachine ownership/exception/reentrancy regressions and calibrated main-thread allocation checks.

## 5.1.0 — 2026-11-12

- Added independent, idempotent `AcquireInputBlock(owner)` handles, `InputBlocked` and `InputBlockedChanged`. Disabled/destroyed owners and controller pooling release their own blocks while scripted motor calls remain available.
- The standalone FPS reader respects blocks in Update and every callback, releases only its held intents once, and requires held buttons to be released before resuming. It does not overwrite another movement provider with zero input; sprint, crouch and NoClip behavior are preserved.
- Added validated `FirstPersonCharacterState` capture/restore for world pose, view pitch and supported gait requests, without automatic storage or input-settings persistence.
- Added optional `CharacterStepController.ContextProvider` with inspector diagnostics and legacy fallback when no active valid provider is assigned. The existing EasyFeedbacks dependency now requires 1.8.0 for `IFeedbackContextProvider`.
- Added overlapping-owner, callback, external-motion, disable/destroy and footstep-routing regressions. Allocation checks now use a calibrated main-thread GC.Alloc recorder with a positive control; the earlier per-thread .NET counter result is not allocation evidence.
