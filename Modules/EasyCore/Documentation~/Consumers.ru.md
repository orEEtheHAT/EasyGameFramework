# Реальные потребители EasyCore

Аудит staging 13 ноября 2026. Пути в таблице относительно
`Packages/com.oreethehat.easy-game-framework/Modules` после CUTOVER.
На этапе STAGING соответствующие исходники находятся в
`Library/EasyGameFrameworkWork/Package/Modules`.

| Общий механизм | Реальные места применения | Совпадающая семантика |
| --- | --- | --- |
| Numeric finite/positive/nonnegative | EasySettings/Runtime/SettingsValidation.cs, SettingsSession.cs, UrpSettingsBackend.cs уже используют Core; EasySave/Runtime/SaveConfiguration.cs проверяет AutoSaveSeconds; EasyCombat/Runtime/DamageContracts.cs и EasyBuilding/Runtime/BuildingDefinition.cs содержат эквивалентные predicates | Конечность и знак, без выбора fallback/epsilon |
| Numeric.IsFinite(Vector3) | CombatMath и BuildingMath в указанных файлах | Все компоненты конечны; не проверяется длина |
| Numeric double predicates | EasyCombat/Runtime/Execution/CombatExecutionScope.cs, EasyBuilding/Runtime/BuildingAuthority.cs и BuildingLoadQueue.cs | Конечное double, знак задаётся вызывающим; единицы не смешиваются |
| IClock/ManualClock/ScaledClock | CombatClock/ManualCombatClock; контролируемые timed-сценарии ниже | Источник абсолютных секунд; явное ручное продвижение для теста |
| UnscaledClock | EasyUI/Runtime/WindowManager.cs nextMove, EasyInteraction/Runtime/InteractionPresenter.cs nextRefresh, EasySave/Runtime/SaveController.cs nextAutoSave | Абсолютное время кадра, независимое от timeScale |
| DisposableScope | EasyResources/Samples~/Workflows/ResourceWorkflowsDemo.cs список owned; EasyUI/Runtime/StandaloneInputProvider.cs Request.Gates и pendingGates | Владение конкретными ресурсами и отписками до конца выбранного lifetime |

Наличие потребителя означает доказанную возможность использования общего механизма,
а не утверждение, что каждый указанный модуль уже переписан. Settings уже имеет
assembly reference на Core. Остальные владельцы применяют опубликованный контракт
в своём модуле; изменения вне EasyCore здесь не выполнялись.

## Предложения владельцам

- Save: заменить проверку NaN/Infinity/<0 интервала на
  `!Numeric.IsNonNegative(AutoSaveSeconds)` и добавить ссылку EasyCore.Runtime.
  SaveController может использовать UnscaledClock, если нужна инъекция часов
  в тесты; один лишь вызов Time не требует отдельного refactor.
- Combat/Building/Input/Interaction: использовать готовые predicates там, где
  семантика совпадает. Core не переносит доменные границы и тексты ошибок.
- Combat: при унификации часов допустим прямой переход на IClock;
  legacy wrappers и миграторы ради прежнего API не требуются.
- Resources/UI: адаптировать завершённые lifetime-наборы после проверки порядка
  queue/profile-before-store, acquire failure, nested windows и exception cleanup.
  Отложенное освобождение UI gate после neutral frame должно остаться у UI.

## Что остаётся у владельцев

Settings.Tick принимает unscaled delta и не требует absolute clock. Его clamp,
fallback, URP capabilities, FOV/volume ownership не переносятся в Core.
Save сохраняет UTC ticks для метаданных; frame time не заменяет UTC. Async I/O,
CancellationToken и commit границы не являются обычным DisposableScope.
Save-specific participant/storage contracts остаются в Save.

CombatExecutionScope задаёт RNG/physics/event isolation со стековым порядком.
ResourceReservationScope использует owner ID/generation; ResourceNotificationScope
управляет уведомлениями. InputContextHandle и window leases вычисляют ограничения
владельцев. Core может владеть IDisposable, но не подменяет эти политики.

BuildingAuthority и Input rebinding используют realtimeSinceStartup. UnscaledClock
возвращает frame time и не является их заменой. InteractionActor.Tick — delta API.
Resource long milliseconds, fixed-point/BigInteger и постоянные ID не обобщаются
с double seconds и transient generations.

Новых публичных типов для диагностики, контейнера сервисов, event bus, game loop,
Jobs, async cleanup или универсального clamp не требуется: совпадающей потребности
двух модулей для них не подтверждено. Jobs/Burst остаются решением для измеренного
workload потребителя, а не обязательной зависимостью Core.
