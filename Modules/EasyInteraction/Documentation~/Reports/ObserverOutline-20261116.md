# Observer-owned Focus Outline — owner validation

Completed2026-11-16 after the coordinator's atomic source/common-scene migration. No owner source fixes were required after import. The imported18-file stage matches the approved hashes; the former4 per-item runtime/Inspector files/metas were retired by the coordinated transition.

## Result and current workflow

`InteractionTarget.HighlightWhenFocused` defaults true. Each Target needs only its existing component and checkbox; `OutlineRenderers` is an optional advanced explicit selection. `InteractionFocusHighlighter` on the Actor owns cyan `OutlineColor`, world-space `Width` default0.015 and `CameraOverride`. Without an override it resolves the exact Camera on Actor.View. There is no Camera.main or all-camera fallback. `HighlightedTarget` replaces the old required per-item component reference.

Geometry is lazy, plain Target-owned cached data; observer owns its material and submits only to its resolved camera. The existing private smoothed source-mesh cache and Resources shader remain. Target.Update maintains existing sources before its queue early-return, so unfocused replacement/removal releases leases. Observer disable releases its material, while Target geometry persists until Target opt-out/disable/destroy/source lifetime ends. Held suppression remains global until the final owner releases; ordinary focus/hold changes reuse warm resources.

## Tests actually performed

| Run | Result | Evidence |
| --- | --- | --- |
| Editor | **17/17 PASS** | Existing setup/Undo/prefab authoring plus fresh Target defaulttrue and saved false/explicit renderer selection after prefab reload |
| Outline/mesh PlayMode | **13/13 PASS** | Target permission toggle, no required item component, missing/exact/override camera, multi-observer lifecycle, material/source immutability, shared geometry/cache lifetime, replacement while unfocused, non-readable fallback, real URP cube pixels and two-camera isolation, calibrated warm GC |
| Held PlayMode | **12/12 PASS** | Global own/other-observer suppression, transfer/multiowner, final release/focus recovery, drop/throw/cancel/disable/destroy/pooling, material/geometry reuse and held/unheld calibrated warm GC |

Zero failed, skipped or inconclusive cases. Root-imported compilation completed without errors. Raw results and pre/post Editor states: `Audits/EasyInteraction/ObserverOutline-20261116/{editor-1,outline-1,held-1}`.

The actual GPU test requires more than500 ordinary gold cube pixels as positive render/readback control and more than100 cyan pixels. It verifies one connected meaningful cyan silhouette; another unfocused camera sees ordinary source pixels but zero cyan, then its own magenta outline after acquiring focus, while the first camera remains cyan-only. `observer-cyan.png` was visually inspected and shows a continuous clean silhouette. Historical cube evidence overwritten by the existing fixture path was backed up/restored; the new image was copied to this audit.

Warm scopes detect a512-byte positive allocation control, then measure128 warmed routing/draw steps. Both held/unheld cases with two observers, and the ordinary outline scope, passed with zero main-thread allocation events. Initial geometry discovery/mesh/material creation is outside this claim. Material/private mesh identity and changed ownership lifetimes are asserted separately.

**Depth evidence bound:** these25 PlayMode cases do not place a foreground wall in front of the target. They prove real pixels, per-camera isolation and the listed lifecycle/GC behavior, not opaque-wall GPU occlusion. The shader still uses depth testing, but static source inspection is not a replacement for that visual proof. After the42 planned tests, the coordinator explicitly transferred Editor to Combat and instructed no further owner runs. Therefore the existing isolated/common Playground depth probe was not executed by this owner; root handles it in independent common acceptance. No Player/Addressables build was run.

## Final Editor / handoff

The same Editor PID24668 responded at **2026-11-16 14:40:46.0108108 UTC /17:40:46 Moscow**, stopped, runner inactive, not compiling/updating, original FrameworkPlayground clean. Own atomic lease released at **14:40:47.0880811 UTC /17:40:47 Moscow**. Coordinator independently confirmed results/free lease and assigned the next turn to Combat. Runtime/API/tests/stage remain frozen; no additional owner Editor operations or imported edits.

The current translational-inertia explanation is read-only: MaximumPositionLag0 removes only added positional filtering for all axes; Strength0/Inertia0 remove extra target lag, not physical Rigidbody tracking error. No GrabAction solver, inertia setting or demo value changed. Complete parameter reference remains [Grab Action](../GrabActionParameters.ru.md); new setup/camera/migration details are in [Focus Outline](../FocusOutline.md).
