# Changelog

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
