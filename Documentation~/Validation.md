# Release verification

## 0.2.0 — Editor and native-input acceptance

This update is verified in Unity 6000.3.22f1 Windows Editor/Play Mode. Health composition, resource authoring/rewards, simplified carrying and the half-lag defaults have owner and independent coordinator evidence. Setup has a reproduced-before/fixed-after native persistence regression, legacy-marker migration and focused Setup tests.

See [0.2.0 verification scope](Release-0.2.0.md) for the final release checks and limitations. No new Player, Addressables or WebGL build was run. The results below document **0.1.0 only** and are not certification of the changed 0.2.0 implementation.

---

# Release verification — 0.1.0

Verified on 2026-11-13 with Unity 6000.3.22f1, URP/VFX Graph 17.3.0, Cinemachine 3.1.7 and Input System 1.20.0. The independent verification project contained the framework, its declared Unity dependencies, the test/development tools, and a privately installed licensed ECM2 copy. Commercial ECM2 sources are not distributed in this repository.

| Gate | Final result |
| --- | --- |
| Editor tests | 911 passed, 0 failed, 0 skipped |
| PlayMode tests | 596 passed, 0 failed, 0 skipped |
| UPM archive installation | Successful; installed content matched its source, with Unity's generated manifest fingerprint accounted for |
| Setup | All 12 module assemblies loaded, dependency diagnostics checked, default Parallel preserved |
| Playground creation | One input owner, one camera, zero missing scripts; repeated creation preserved the scene |
| Playground in Editor | 13 checks passed; input and world snapshots restored |
| Real keyboard/mouse release | All 3 stationary/turning E-key cases passed, including loss of reticle selection |
| Windows Mono Development build | Succeeded, 0 errors; final incremental build recorded 1 Unity shader warning |
| Windows Player gameplay | 13 checks passed, exit code 0, no captured Unity errors |
| Windows Player URP/Cinemachine | Final run passed, exit code 0, 6 explicit renders, no captured warnings/errors |

The gameplay check covers shared input and context ownership, native keyboard movement/menu input, nested windows, paid construction, delayed abilities, capture/restore during a live paid cast, physical grabbing, death and repeated restore, independent progress/preferences, language switching, projectile impact, damage fields and burn. It uses scoped synthetic devices and restores their ownership afterward; it does not simulate every possible gameplay input or gamepad model.

The Settings probe runs with a real D3D11 graphics device and a hidden window. It submits two explicit URP renders into a private 128×128 texture for each of the source, changed clone and restored pipelines. It verifies real render callbacks, render-scale readback, Cinemachine-owned FOV, source-asset JSON immutability, restoration and clone/texture cleanup. This verifies the API and pipeline lifecycle; offscreen rendering does not prove on-screen resolution scaling or displayed frame rate. The probe is private verification infrastructure, not a component installed by the package.

## Findings retained rather than suppressed

- Earlier Editor runs emitted a native VFX missing-script warning during URP pipeline replacement/cleanup. A minimal Unity-only reproduction also emitted it, with no framework behaviours involved. The final complete PlayMode run did not reproduce it. No log suppression, SDK/cache patch or engine-version change was used; this remains an observed intermittent engine/editor limitation.
- One Settings Player run passed all functional and restoration checks but failed its strict log gate on a graphics ring-buffer warning during bootstrap. The same binary and launch settings passed on the subsequent run with no warnings. No command-buffer override or warning filter was added.
- The final build warning was Unity's `Hidden/Core/DebugOccluder` shader vector truncation warning on D3D11. A clean compilation also reported three existing ECM2 CS0109 warnings. These external SDK sources were not edited.
- The local tarball triggered Unity's Missing Signature advisory. Its contents were verified against the prepared source; signature verification was not disabled.

## Scope

The archived build source and final release have the same runtime/sample implementation. Final packaging removes invalid metadata for Unity-ignored `~` folders and adds this report; GUID metadata inside the examples is preserved.

Performance workload counts, checksums, GC calibration and timing limits are documented in [Performance.md](Performance.md). No general speedup or FPS guarantee follows from combining packages.

This release's standalone acceptance target is Windows Mono/D3D11. IL2CPP, WebGL, other operating systems, physical gamepads/headsets, live Google authentication and specific multiplayer/AR deployments need their own target-project checks. Netcode/XR/AR extensions remain separately installed packages and are not certified by the base framework's desktop tests.
