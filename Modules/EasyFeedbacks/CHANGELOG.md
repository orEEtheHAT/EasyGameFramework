# Changelog

All notable changes to this package are documented in this file.

## 1.8.0 — 2026-11-12

- Added `IFeedbackContextProvider` and an optional active provider on `FeedbackEmitter`; authored context fields and explicit gamepad choices are preserved.
- Added runtime `FeedbackSettings` capture/apply/restore and change notifications, with an optional initial profile. Master, music, sound-effects and haptic preferences update active and future pooled output while preserving authored multipliers, pause ownership and playback lifetimes.
- Added `FeedbackAudioCategory`; MusicManager marks its output as music without requiring new mixer routing.
- `WithoutGamepad()` explicitly suppresses vibration. `WithDefaultGamepad` preserves an explicit device or explicit silence, preventing keyboard-owned feedback from using another player's current gamepad. Unaddressed legacy playback keeps its fallback behavior.

## [1.7.0] - 2026-08-22

### Added

- Per-mixer-group **Ignore Global Pause** controls in the Audio Manager inspector, intended for pause-menu UI and other audio that must remain active during `FeedbackService.PauseAll()`.
- `Replace` and `Suspend Previous` interruption modes for Music Events.
- Suspended-playback state and interruption-mode details in the Music Manager and Audio Manager debugging tools.
- A unified, case-sensitive Music Event registry assembled from any number of catalogs assigned to `MusicManager`, with `RequestEvent`, duplicate rejection, optional per-catalog startup preparation, and resolved-registry debugging.

### Changed

- Mixer channel runtime status now distinguishes channels paused by the feedback service from channels that ignore its global pause.
- Music playback now uses reusable playback sessions. A `Suspend Previous` request preserves the lower track's exact playback position, playlist order, and fade state until the request is released.
- Stopping an individual mixer channel now stops only music sessions routed to that channel and leaves the rest of a suspended playback chain intact.
- Music Playlist test requests now suspend and restore active music instead of replacing it.
- Restarting an event from the Music Event Debugger now releases its previous request before creating the replacement, so suspend events reliably restart from the beginning.
- Music Event Catalogs are now data-only assets. Named event requests are resolved centrally through `MusicManager`, and the debugger can display all registered catalogs or filter by one source.

## [1.6.0] - 2026-08-20

### Added

- `CameraShakeManager` with pooled generation-safe handles and per-handle, manager-wide, and framework-wide pause composition.
- Global `FeedbackService.PauseAll()` and `FeedbackService.ResumeAll()` controls for all audio, visual, camera shake, and gamepad vibration output owned by Easy Feedbacks.
- Global `FeedbackService.StopAll()` with configured and immediate stop modes.
- Runtime camera shake pool counters and pause, resume, and immediate stop controls in the manager inspector.
- MIT license for public reuse and redistribution.

### Changed

- Camera shake playback through feedback presets and `CameraShakeService` is now owned by an active `CameraShakeManager` in the persistent bootstrap scene.
- Global feedback pause calls can be nested; every `PauseAll()` call must be paired with a `ResumeAll()` call.
- Disabled managers now reject new playback and immediately release output they own.
- Minimum supported Unity version is now Unity 6.3 (6000.3), matching Visual Effect Graph 17.3.0.
- Camera noise signals are now pooled with shake voices, and camera pool collections reserve their prewarmed capacity to avoid per-shot noise-signal and manager-list allocations after warm-up.
- Uniform camera shake events no longer keep pooled slots alive for an unnecessary propagation tail after their visible envelopes end.
- VFX Graph one-shots and their stopped tails now use explicit authored durations instead of asynchronous GPU particle-count readback.
- The visual pool now evicts the least recently used free instance when a new resource is requested at capacity; active instances are never evicted.

### Fixed

- Visual feedback launched after `VisualFeedbackManager.PauseAll()` now starts paused and waits for `ResumeAll()`.
- Visual effects stopped while globally paused no longer resume their remaining particles before the global pause is released.
- Global audio pause remains authoritative when the application or `AudioListener` has already suspended a source.
- Global resume no longer removes a pause applied separately through a handle or an individual manager.
- Repeated audio stop calls no longer restart and prolong an active fade-out.
- Expired Cinemachine impulse events emitted by Easy Feedbacks are recycled even when no active Impulse Listener is present.
- Music zones release disabled, destroyed, or retagged player colliders even when Unity does not send `OnTriggerExit`, and re-register with a replacement Music Manager when necessary.
- Cinemachine Noise Presets are discovered through Unity's Asset Database, so the profile menu works with virtual UPM package paths.
- Duplicate managers remove only their own component instead of destroying sibling components on the same GameObject.
- VFX Graph instances no longer risk becoming permanently active or returning too early because of stale `aliveParticleCount` values.
- Pooled VFX Graph playback now sends one authoritative initial event per launch instead of redundantly calling both `Reinit` and `Play`.

### Removed

- The redundant `CameraShakeService.Play(entry, context)` forwarding overload left from the pre-manager camera shake path. Use `FeedbackService` for preset entries or the profile-based direct API for standalone shakes.

## [1.5.0] - 2026-08-20

### Added

- Dual-motor gamepad vibration feedback layers with one-shot and loop playback, fades, pause, resume, and stop support.
- `GamepadVibrationManager` with pooled requests, per-device maximum mixing, focus handling, and runtime controls.
- Reusable `GamepadVibrationProfile` assets with per-layer strength and playback timing.
- Ten built-in starting profiles for gamepad vibration and ten for Cinemachine camera shake.
- Built-in profile menus in feedback layers and one-click editable copies under `Assets`.
- Independent low- and high-frequency motor patterns for smooth pulses, impacts, rumbles, and repeating haptics.
- Recommended duration, fade, and loop settings when a built-in profile is added through a layer's `+` menu.
- Optional explicit gamepad targeting through `FeedbackContext.WithGamepad`; otherwise playback uses `Gamepad.current` at launch time.

### Fixed

- Feedback prewarming no longer exits early when a Visual Feedback Manager is absent.
- FeedbackEmitter inspector playback can drive gamepad vibration while the Inspector has focus without allowing normal gameplay rumble to continue in the background.
- The built-in Weapon Recoil camera shake no longer uses Directional Kick.

## [1.4.0] - 2026-08-17

### Added

- FeedbackEmitter lifecycle controls with Manual, On Start, and On Enable playback plus optional Stop On Disable behavior.

## [1.3.0] - 2026-08-07

### Added

- `DebugFeedbackTrigger` for launching a FeedbackEmitter from a New Input System action during testing.

## [1.2.1] - 2026-08-07

### Changed

- Simplified `FeedbackEmitter` playback controls to `Stop`, `Pause`, and `Resume`; each controls the emitter's most recent launch.

### Fixed

- FeedbackEmitter inspector playback now returns finished audio voices to the pool while the Game view is unfocused.

## [1.2.0] - 2026-08-07

### Added

- Per-playback volume and pitch ranges for audio feedback layers.
- Multiple AudioClips per audio feedback layer with uniform random selection.
- Multi-clip drag-and-drop onto audio feedback and Music Playlist headers.

### Changed

- Audio feedback layers, direct AudioManager playback, and Music Playlists now use AudioClip references.
- Feedback layer headers and nested AudioClip lists use clearer spacing and consistent reorderable containers.

### Removed

- Audio Random Container support.

## [1.1.0] - 2026-08-07

### Changed

- Renamed the product to Easy Feedbacks.
- Changed the package identifier to `com.oreethehat.easy-feedbacks`.
- Renamed public namespaces and assemblies to `EasyFeedbacks`.
- Renamed Unity creation and component menus to Easy Feedbacks.
- Updated repository and author metadata for the renamed GitHub account and repository.

## [1.0.0] - 2026-08-06

### Added

- Reusable feedback presets with independently enabled audio, visual, and camera shake layers.
- World-position and follow-transform playback with per-layer positioning overrides.
- Generation-safe aggregate feedback handles with stop, pause, and resume operations.
- Pooled audio playback with fades, mixer-group controls, and composite pause reasons.
- Pooled Particle System and VFX Graph playback with prewarming, scaling, offsets, and explicit lifetime policies.
- Cinemachine 3 directional kick and noise shake profiles, including loops and invocation fades.
- Priority-based music selection for events, zones, additive scenes, and fallback playlists.
- Music event catalog and runtime debugging tools.
- Dedicated Runtime and Editor assembly definitions.
