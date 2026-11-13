# EasyCore module changes

## EasyGameFramework 0.1.0 — staging

- Moved documentation and first-run to the unified package and shared testable root.
- Added an undoable first-run command under Tools > EasyGameFramework and sample Inspector feedback.
- Added nested cleanup and failed reentrant registration regressions.
- Expanded the verification fixture to all seven Numeric overloads, equivalent inline work,
  recorded workload/configuration and result checksums.
- Kept the six small runtime contracts and zero-allocation helper implementations.
  No new service container, loop, dependencies or compatibility wrappers.
- Previous standalone results are historical evidence in Documentation~/Reports/StandaloneBaseline.
