# Changelog

## Unreleased

## 0.2.0 — 2026-11-17

### Added

- ResourceDatabase: one asset containing embedded resource records, searchable selectors, validation, Undo-aware caches and protected deletion. Resource IDs remain save identities.
- ResourceRewardProfile: several resource entries with inclusive minimum/maximum quantities, explicit Roll snapshots and atomic grants.
- Health now owns optional ranged death rewards with a recipient, once-per-life payment and save/restore safeguards.
- Visual Scene View authoring of held poses, including Undo and prefab edits, plus temporary held scale with original scale and mass-property restoration.
- URP focus outlines with per-target eligibility and shared observer/camera-owned styling; default width is 0.015 metres.

### Changed

- Health owns statuses and damage/death/healing feedback configuration directly. Both features default on; status storage is created lazily.
- Interaction Focus is the default for new actors. Authored Pose is the default for new Grab Actions.
- Carry feel uses two controls: FollowSpeed (0–1, default 0.5) and local-axis MovementLag (XYZ 0–1, default 0.5 on each axis). Holding remains a dynamic Rigidbody spring with physical contacts.
- Framework Playground demonstrates a four-resource database, ranged enemy rewards, native Health features, authored poses, held scale and focus outlines. Its walking comparison starts from the authored half-lag vector and preserves custom baselines.
- Focused, repeatable Playground updates preserve existing scene/prefab/localization identities and unrelated authored changes. English/Russian help and carry comparison controls are synchronized.

### Fixed

- Setup's first-run marker now has a matching MonoScript asset and survives cold native reload. Existing markers from the old unbound script are migrated; automatic welcome is once per project, including package updates. Manual menu access remains available.
- Held items stop drawing focus outlines for every observing actor and restore highlighting after release. Outline rendering follows the observing camera and respects opaque-wall depth.
- Native Health status, movement, feedback and save integrations use one owner. Configured death rewards and death feedback are emitted once at the committed life boundary, including a Downed finishing hit.
- Regressions cover primary release during mouse look, modal input, rebinding, pose/scale/transfer/pooling and downstream movement status composition.

### API and authoring

- Use Health.Statuses and Health's status API instead of adding a StatusController component. Configure feedbacks and resource rewards on Health; HealthFeedbacks and the separate death-reward receiver are retired.
- Author resource definitions inside ResourceDatabase. Reward profile creation receives its database and ranged entries; standalone sample resource assets were replaced with database records.
- GrabAction's old Inertia/InertiaStrength/Frequency/DampingRatio/MaximumAcceleration/MaximumPositionLag/MaximumRotationLag/CameraPositionLag/CameraRotationLag controls were removed. Use FollowSpeed and MovementLag.
- Imported Samples remain editable project copies. Updating UPM does not silently overwrite consumer scenes; use the sample's explicit Create or Update command when appropriate.

### Verification

- Editor/Play Mode module, downstream integration and native-input checks are recorded in the current release verification report. Overlapping focused reruns are not added as independent test totals.
- No new Player, Addressables or WebGL build was run for this update. Earlier 0.1.0 Player results remain historical evidence.

## 0.1.0

First unified EasyGameFramework preview for Unity 6.3 URP.

- Twelve modules, one UPM package and version, with built-in production integrations.
- One Setup window with dependency diagnostics, module guides, optional examples and projectile processing selection. Parallel processing remains the default.
- A Framework Playground connecting first-person movement, combat, interaction, building, resources, feedbacks, input, windows, localization, persistence and graphics settings.
- Shared saving composition with module-owned validation and restoration; progress slots and global preferences remain separate.
- Netcode, XR and AR SDK adapters packaged as separate extensions. Their device/transport acceptance is separate from desktop framework validation.
- Corrected UI input ownership cleanup, unconfigured-localization cleanup, non-periodic status serialization and Editor setup/Undo behavior found during clean-project validation.
- URP runtime assets are cloned lazily only when a pipeline setting is changed. Audio pool cleanup and allocation measurements use real Unity lifecycle/profiling APIs.
- Catalog refresh awaits the current snapshot across delayed native language notifications; old-language text becomes unavailable immediately, while disabling/resetting still cancels the old lifetime.
- Standard UI pointer clicks use PassThrough without changing gameplay button semantics, action IDs or existing bindings.

This first framework release establishes the module/API layout without legacy package migration shims. Independently used standalone EasyFeedbacks source and releases are unchanged.
