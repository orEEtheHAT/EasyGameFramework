# Stock cube outline regression

Completed owner polish on 2026-11-16 after inspecting the real Playground screenshot with disconnected hard-normal face plates.

Readable source meshes now use a lazy, shared, reference-counted **outline-only Mesh copy**. Extrusion normals at exactly coincident vertex positions share a normalized average; the source geometry, indices, UVs, normals and material are unchanged. The production shader/depth behavior and public OutlineColor/Width API remain unchanged. No mesh clone or normal rebuild occurs per warmed frame.

Copies survive focus switches and same-source Refresh, and are released on final component lifetime/source replacement/null. Disabled/focus-lost source replacement is processed before the drawing early return. Only private copies are destroyed. Non-readable sources retain raw normals without CPU access, cloning or forcing importer Read/Write; Inspector and `FallbackRendererCount` explain remaining seam limitations. `SmoothedRendererCount` reports current smoothed renderer leases. Meshes are static snapshots; assign a replacement mesh or release all consumers before rebuilding in-place geometry edits.

**11/11 targeted PlayMode cases passed**, with zero failed/skipped/inconclusive and compilation errors. Previous six outline tests remained intact, including calibrated main-thread warm GC (positive control detected; zero events over128 warmed routing/draw submissions). Five new regressions cover stock cube continuity/source immutability, shared/refcount lifetime and Refresh reuse, source replacement/null while unfocused, non-readable source ownership, and real URP rendering.

The GPU regression uses a normal gold cube as positive render/readback control (>500 pixels), requires visible cyan outline (>100 pixels), and exactly one meaningful connected cyan region. The resulting isolated **cube-smooth.png** was visually inspected: continuous clean silhouette, no corner plates/gaps. This is an actual URP render request, not a gizmo or CPU geometry-only assertion. It does not replace root's final common-scene and Player screenshots. A separate all-zero hidden Player capture is not attributed to the outline without a valid ordinary-object render control.

After tests the same main Editor PID24668 answered at **11:56:10.8121623 UTC /14:56:10 Moscow**, with runner inactive, stopped, not compiling/updating and original Playground clean. Own atomic lease released. No source changes to shared sample/probe, other modules, version or Git publication. No unrelated suite repeat; pose/editor APIs were unchanged.

Runtime cache allocates only during first acquisition/configuration; source discovery/material/mesh creation are outside the warm-GC claim. Exact grouping does not weld merely nearby vertices. Open/nonmanifold geometry, non-readable hard-normal fallback, transparency/alpha clipping, skinned rendering and automatic LOD limitations remain documented in [Focus Outline](../FocusOutline.md).
