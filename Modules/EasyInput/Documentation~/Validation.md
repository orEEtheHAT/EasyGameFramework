# Unified staging verification

Status: source/static checks only until coordinator CUTOVER. No Unity/editor/player result from the old individual packages proves this staging tree.

Planned real behavior checks after CUTOVER:
- Core EditMode/PlayMode: runtime cloning, binding conflicts/capture, settings preflight/restore equivalence, no mutation or rebind cancellation during ValidateSettings, contexts, neutral gates and device reconnection.
- EditorSetup: Connect Selected Player is repeatable, shares one owner and resolves the new module prefab path.
- Integrations: Movement scripted blocks, actual Interact/Grab/PressPrimary (E + Mouse.delta in one update, no hover/other hover/behind camera), rebinding, hold/toggle, disabled readers, nested UI, shared Building sessions/confirm without jump or fire leakage, selected/explicit-none gamepad feedback and localization lifecycle.
- New uGUI misconfiguration test preserves the existing module and references. Localization test checks sprite presentation and no text/sprite events after disable.
- Framework root performs clean first run, shared scenario and Player/performance checks. Do not launch a duplicate editor/build during STAGING.

Source tests are compiled from Modules/EasyInput/Tests; no copied adapters from Assets or Samples are needed. Integration fixtures remain a PlayMode TestAssemblies assembly (autoReferenced=false); fixtures using Editor APIs are wrapped in UNITY_EDITOR. EditorSetup/EditMode have Editor-only asmdefs. Test assemblies are excluded from ordinary Player builds; runtime source never references them. Sample builders and benchmarks remain opt-in examples, not production prerequisites.

Historical baseline measurements/reports in this module describe the earlier individual package. They must remain labelled as historical if included in a release; new performance numbers require measurements after CUTOVER. No Jobs/Burst was introduced for ordinary input/event dispatch.
