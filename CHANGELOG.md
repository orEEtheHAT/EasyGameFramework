# Changelog

## 0.3.0 — 2026-11-24

- Narrative Studio adds World Facts, preset filtering, readers and writers of named facts, links to the exact dialogue reply and per-quest test scenarios.
- The package includes the ready-to-assign Quest Markers library (Main, Side, Enemy and a directional edge sprite), the Wayfinder and Icon Finish shaders, and Shimmer and Soft Pulse materials.

- Narrative Studio brings the character, dialogue and quest catalogs, graph editors, asset settings and scene connections together. Double-clicking a quest, dialogue or speaker opens the studio. The Playground includes a shortcut to the ferryman's negotiation.

- EasyNarrative adds graph branch editing, scene diagnostics and regression scenarios, repeatable orders with scoped facts, a searchable journal and marker clusters. Dialogue supports grouped requirements, paid choices, skill outcomes, voices, interruption and transcripts. Narrative localization can collect and review quest and conversation text with translator context.

- The quest graph can wire scene objectives, count incoming or held resources, and author resource hand-ins. Each ending can grant its own resource profile automatically or at a bound recipient. Framework Playground includes the two-route Workshop order.

- Quest marker captions default to hidden, search regions use a compact centre marker, and icon shimmer follows sprite UVs. The optional tracked-quest HUD is enabled by default. Narrative/Journal (J) toggles the journal and supports rebinding.

- Quest markers support group anchors, per-icon caption and edge behaviour, objective overrides and caption distance. EasyUI includes shared pulse and shimmer sprite materials. The Playground patrol exercise demonstrates ten live markers and restores individual inspections.

- Quest markers support named sprite libraries, quest defaults, objective overrides and per-icon screen-edge margins. The Playground demonstrates Main, Side and Enemy presets.

- Optional quest-wide fact rules redirect active quests, preserve interrupted progress and expose the rule and destination in the graph. The Playground delivery can be redirected to an assistant.

- Narrative authoring adds searchable event and marker keys, two visibility slots for scene reactions, a scene-object quest creation command and isolated interactive dialogue previews. The Playground includes the Survey post and the simplified bridge setup.

- EasyNarrative exposes ordered quest and step transitions, inspector bindings for persistent world state and one-shot events, and a playable bridge repair example with save/load.

- Wayfinder draws target badges, edge chevrons and search regions. Spoken lines and standalone TMP subtitles support a skippable, unscaled reveal with per-player motion preferences.

- EasyUI adds a theme with six text roles, shared Letterpress SDF materials and per-player text scale, reduced motion and high contrast. Framework Playground previews these in Tab → Text and includes reading preferences in its global save.

- EasyNarrative now includes branching dialogue, localized replies, a Talk Action and the ferryman conversation. Quests and conversations share one world state.
- The quest subsystem adds world facts, visual quest graphs, scoped event progress, persistence, rewards, a journal, and HUD markers. The Playground includes a branching parcel delivery.

- EGF-007: Built-in EasyUI windows, graphics settings and the Framework Playground HUD and menu use TextMeshPro. Set a window set’s font on its Window Manager.

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
