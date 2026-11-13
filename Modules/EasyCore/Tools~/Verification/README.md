# Core verification fixture for the framework coordinator

Source lives at `Modules/EasyCore/Tools~/Verification` in the unified
`com.oreethehat.easy-game-framework` package. This folder is excluded from import.
After CUTOVER, the coordinator can copy it and **Core / Getting Started** to the
framework verification area; do not import duplicate sample assemblies.

The fixture verifies first-run, paused/resumed clocks and hot numeric/clock calls
on 32 and 100000 items. Report schema 2 records workload, cycle contents, synchronous
execution mode, warmup/repetition counts, manual delta and each result checksum.
All seven Numeric overloads are exercised per data item; the inline baseline uses
the same inputs and seven predicates. Nanoseconds are per item, not per predicate
or FPS. Baseline and helper checksums must match.

Allocation evidence uses main-thread GC.Alloc event counts with a 512-byte positive
control and a warmed empty control. An unavailable/noisy meter fails verification.
Setup, Own/Defer, exception paths, serialization and I/O are not allocation-free.
ManualClock advances and reads on each loop iteration; Unity clocks are sampled
synchronously without yielding. Jobs/Burst are not used.

For the coordinator's explicit fixture build, the existing helper
`EasyCore.Verification.Editor.CoreVerificationBuild.Build(absoluteExePath)`
writes `Assets/PackageTests/EasyGameFramework/EasyCore/EasyCoreFirstRun.unity`.
It preserves a saved active scene; save untitled work before calling it.
The module owner does not run an independent full build/clean project for unification.

Development Player arguments: `--easycore-report <absolute json path>
--easycore-auto-quit -batchmode -nographics -logFile <absolute log path>`.
No output file is written without the report argument. The fixture changes
timeScale and Profiler.enabled temporarily, restoring both in finally.
No performance result for this new fixture is claimed before CUTOVER.
