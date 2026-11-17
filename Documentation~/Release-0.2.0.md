# EasyGameFramework 0.2.0 verification

Environment: Unity 6000.3.22f1, Windows Editor, URP 17.3.0, New Input System 1.20.0 and locally licensed ECM2. The package does not distribute ECM2 source.

## Completed behavior checks

- Native Health composition: 403 Editor, 228 Play Mode and 20 downstream motor checks passed in the accepted composition run. Coordinated UI/input/resource/save integration acceptance is recorded separately; repeated focused runs overlap those cases.
- ResourceDatabase/rewards: 180 Editor and 15 Play Mode cases passed on the accepted source. Cached lookup allocation measurements apply only to the stated warmed lookup path; reward Roll creates snapshots.
- Final simplified interaction: 257 cases passed (192 core Play Mode, 17 Editor setup, 12 shared-input and 36 integration cases). Native input comparisons and an independent W/S/A/D, selective-axis, wall, sustained return and E-during-look acceptance passed.
- MovementLag half default: initializer-only runtime change; 26 relevant existing checks passed. Saved Playground and actual Play startup both read (0.5,0.5,0.5), with FollowSpeed 0.5.
- Setup welcome: the old marker lost shownVersion on a cold singleton reload. The state now has its own matching script; bounded migration preserves the original marker. 26 Setup cases passed, plus native cold-load, unknown-version first-display, manual menu access and scheduled suppression checks. Combat selection/assets remain unchanged.
- Playground revision 10: existing 236 menu object/component identities, 18 buttons, prefab GUID and native localization IDs were retained; repeated update was a no-op and saved-scene reload had no missing scripts.

## Installation and restart

Installed the 0.2.0 UPM archive into a new project containing only the licensed ECM2 dependency and minimal URP settings. The automatic welcome appeared exactly once, survived a native domain reload and a full Editor restart without reopening, and remained available through the manual menu. The native marker retained a valid MonoScript reference.

Imported Framework Playground through UPM Samples and created it from scratch: four owned embedded resource records, native Health statuses/feedbacks/death rewards, authored dynamic holding, FollowSpeed 0.5, MovementLag (0.5, 0.5, 0.5), saved clean scene and zero missing scripts. All 13 shipped Editor Play Mode smoke checks passed with world/input restored.

The accepted archive used the same runtime, Editor, test and example assets as the final source. Post-verification edits only append this report and normalize documentation EOF. These are Editor checks, not a Player build or a displayed-FPS benchmark.

## Limits

No new Player, Addressables, WebGL, IL2CPP, mobile or platform-SDK build was run for 0.2.0. The 0.1.0 Windows Player results in Validation.md are historical. Device-specific gamepad/XR/AR/network and live Google authentication acceptance remain target-project work.

The local Games Factory FPS improvement from disabling its Plastic/UVCS background scan is an Editor-project setting, not a shipped framework runtime optimization or package speedup claim.

Dynamic holding still allows contact/acceleration/interpolation deviation. FollowSpeed 0 means soft active holding; MovementLag 0 compensates walking velocity damping and does not make an exact camera attachment.