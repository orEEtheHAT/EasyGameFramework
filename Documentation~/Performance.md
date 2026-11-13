# Performance validation

This release was compared with the original individual packages using the same immutable `CombatMixed-v1` harness on an Intel Core i7-14700KF, with Burst enabled and 27 available job workers. Both configurations ran in Play Mode. The original project and clean validation project have different surrounding Editor workloads, so the measurements do not isolate the effect of packaging or establish a guaranteed speedup.

Each scenario performed 60 warm-up and 180 measured synchronous simulation steps, repeated three times in alternating single-thread/parallel order. The reported figure is the median of the three mean step times. The harness includes real physics queries, moving targets, pooled projectiles, hitscan, splash, periodic statuses and damage fields. It excludes rendering, UI and normal frame pacing.

| Workload | Mode | Original packages | Unified package |
| --- | --- | ---: | ---: |
| Small | Single Thread | 0.182 ms | 0.163 ms |
| Small | Parallel | 0.173 ms | 0.162 ms |
| Large | Single Thread | 2.473 ms | 1.594 ms |
| Large | Parallel | 2.451 ms | 1.613 ms |

Small: 4 targets, 4 projectiles + 4 hitscans + 2 splashes per step, 2 fields. Large: 64 targets, 64 projectiles + 64 hitscans + 16 splashes per step, 16 fields, bounded pool capacity 512. In each measured large run the accepted-shot count is 23,040; this is **not 100,000 simultaneous bullets**. All compared workload counters and health checksums match.

All twelve measured runs in each configuration reported zero main-thread `GC.Alloc` events after warming. A positive-allocation control and an empty control calibrated Unity's `ProfilerRecorder`; unsupported .NET allocation counters were not treated as proof. These results do not cover worker allocations, native allocations, initialization or whole-game allocations.

The small-run p95 rose from 0.205/0.218 ms to 0.224/0.242 ms despite lower mean times. The data therefore supports retaining the bounded workload without a mean-time regression, not a universal performance improvement. Parallel mode is not faster for every workload: scheduling and synchronization can cost more than the numerical work saved.

Use the normal Unity Profiler and representative target-device builds for project decisions. Jobs/Burst remain appropriate for measured bulk calculations; Unity object access and UI/URP operations stay on the main thread.
