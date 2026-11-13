# Benchmarks

**Package:** EasyGameFramework. This is an optional demonstration, not a production adapter. No prerequisite sample imports.

Open **ResourceBenchmark.unity** and build it as a **Windows x86_64 Development Build**. Include only this scene. Defaults: 15 measured batches per case, three warmup batches, automatic player exit.

```powershell
ResourceBenchmark.exe -batchmode -nographics -easyResourcesReport "C:\Temp\EasyResources-report.json"
```

The report contains four workloads, each with reused and recreated costs. The largest workload has 10,000 independent stores, 16 resource types, 8 cost rows and 160,000 simultaneous reservations. It checks final balances, atomic failures and repeated Commit/Cancel calls.

`gcProbeAllocationCount` must be positive. `gcAllocationCount = 0` proves zero GC allocations in the measured operation. When allocations exist, `gcBytesPerBatch = -1` means their size in bytes is not measured; use `gcAllocationsPerBatch` for the event count. The recorder sums samples, so its one-element buffer does not truncate events within the synchronous batch.

The scene also runs in Editor without quitting it, but use Player results for performance comparisons. See the package's **Documentation~/Validation.md** for measured CPU, GC and memory, methodology and limitations.

## Deferred notification fixture

`ResourceNotificationBenchmark.unity` preserves the Integration Wave 1 fixture: four scenarios (legacy spend/return, nested admission/pulse, cancellation without events, multi-store roundtrip), 128 warm iterations, then 15 batches of 1000 iterations. The runtime source is copied unchanged from the measured local fixture; only scene script GUID/assembly binding changed for this sample. Build this scene alone as a Windows x64 Development Player and pass `-resourceNotificationReport "<absolute JSON path>"`. Expect a positive allocation control, four passing balance/hold invariants, and zero warmed allocation events. This measures reusable core operations, not allocating adapters or snapshot/UI creation. Compare the same backend, settings, hardware and workloads; do not compare a shorter loop against the previous baseline.
