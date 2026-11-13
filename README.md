# EasyGameFramework

An integrated Unity 6.3 URP toolkit with twelve focused modules. Install one package, use the components and ScriptableObject definitions needed by your game, and connect them through the supplied integrations.

## First project

1. Create a Unity 6.3 **URP** project and install your licensed **Easy Character Movement 2 (ECM2)** asset.
2. In Package Manager, choose **Install package from git URL** and enter `https://github.com/orEEtheHAT/EasyGameFramework.git#v0.1.0`. Unity Input System, Cinemachine, VFX Graph, Localization and the other declared Unity dependencies are resolved by the package manifest.
3. Select **Input System Package (New)** for Active Input Handling if the project does not already use it; complete Unity's requested restart.
4. Open **Tools > EasyGameFramework > Setup**. Resolve the actionable diagnostics. Projectile processing defaults to **Parallel**; select **Single Thread** when needed. Closing Setup does not overwrite your choice.
5. Import **Framework Playground** from this package's Samples tab, then choose **Tools > EasyGameFramework > Examples > Create Framework Playground**. Enter Play Mode. The example creates its own definitions, feedbacks and localization content.

The standalone Easy packages and this framework define overlapping assemblies. Use the framework as the single source of these modules in a project. Existing standalone EasyFeedbacks installations in other projects can continue using their own releases.

[Русская инструкция первого запуска](Documentation~/QuickStart.ru.md) · [Performance measurements](Documentation~/Performance.md) · [Release verification and limitations](Documentation~/Validation.md)

## Modules

| Module | Responsibility |
| --- | --- |
| [EasyCore](Modules/EasyCore/README.md) | Small, tested shared primitives with explicit ownership |
| [EasyMovement](Modules/EasyMovement/README.md) | ECM2 first-person movement, FOV, footsteps and Rigidbody propulsion |
| [EasyCombat](Modules/EasyCombat/README.md) | Health, protection, damage delivery, statuses, areas and abilities |
| [EasyInteraction](Modules/EasyInteraction/README.md) | Selection, actions, physical carrying and interaction presentation |
| [EasyBuilding](Modules/EasyBuilding/README.md) | Free, zone, grid and socket placement with transactional costs |
| [EasyResources](Modules/EasyResources/README.md) | Resource definitions, containers, reservations and operations |
| [EasyFeedbacks](Modules/EasyFeedbacks/README.md) | Audio, visuals, camera shake, haptics and music |
| [EasyInput](Modules/EasyInput/README.md) | Player input, rebinding, sensitivity, devices and button presentation |
| [EasyUI](Modules/EasyUI/README.md) | Windows, focus, nested navigation, dialogs and radial menus |
| [EasyLocalization](Modules/EasyLocalization/README.md) | Unity Localization authoring and Editor Google Sheets synchronization |
| [EasySave](Modules/EasySave/README.md) | Slots, validated persistence, versions and storage contracts |
| [EasySettings](Modules/EasySettings/README.md) | URP/display settings, profiles, confirmation and rollback |

## Working conventions

- ScriptableObjects describe authored configuration. Runtime state belongs to the corresponding component or service.
- Production integrations ship in `Integrations/`; examples in `Samples~` are optional. No adapter-copy step is needed between framework modules.
- Gameplay progress and global user preferences have separate save scopes. Loading a world slot does not change the player's bindings, language, audio or display preferences.
- Components acquire and release their own input, movement, UI and speed requests. Releasing one owner's request preserves other active owners.
- Keep gameplay code on the relevant module's public API. Concrete SDK extensions live separately under `Addons~`; their availability and tested scope are documented individually.
- Jobs/Burst are used for suitable measured workloads. Unity objects, UI and URP operations remain on the main thread. Parallel projectile processing is the default; browser/unsupported runtimes use the supported single-thread path.

See [architecture](Documentation~/Architecture.md) for ownership and extension rules. The package contains no commercial ECM2 sources or credentials for online services.

## Development and validation

Module tests are shipped with the package. Add `com.oreethehat.easy-game-framework` to the project's `testables` list to run them with Unity Test Runner. Editor-only test assemblies and examples are excluded from normal Player code unless explicitly imported/used as described.

Release verification covers compilation, module and integration behavior, first installation in a clean project, a standalone Player, and comparable performance workloads. Reports record the actual tested configuration and limitations; shipping the modules together does not imply an automatic speedup.

MIT license, copyright orEEtheHAT. See [LICENSE.md](LICENSE.md).
