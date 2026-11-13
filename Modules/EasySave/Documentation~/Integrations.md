# Production Capture/Restore интеграции

`Integrations/EasySave/EasyGameFramework` (`EasySave.EasyGameFramework`) и `Integrations/EasySave/EasySettings` (`EasySave.EasySettings`) входят в комплект. Единственная компилируемая копия адаптеров находится здесь, их тесты доступны без sample import. API namespace `EasySave.Integrations`.

| Фабрика | Scope / порядок | Owner API |
| --- | --- | --- |
| Resources(key,store,definitions,order=-100) | Progress | CaptureState, ValidateState, RestoreState; простой idle store |
| ResourceLedgerSave.Create(key,ledgerKeys,stores,definitions,includeReservations=false,nowMilliseconds=null) | Progress / -100 | Полная проверка owners, атомарный ResourceSnapshots.RestoreAll нескольких ledger; явные stable keys |
| Buildings(key,system,catalog,preserveUnresolved=false,restored=null,order=0) | Progress | Full CaptureState(false), ValidateSnapshot, TryReplaceSnapshot |
| Combat(key,catalog,references,order=10) | Progress | TryCapture, TryValidate, TryRestore с явным actor registry |
| Interaction(key,target,order=20) | Progress | CaptureState, ValidateState, TryRestoreState; target/action stable IDs |
| Movement(key,character,order=30) | Progress | EasyMovement на ECM2: CaptureState, ValidateState, TryRestoreState |
| Input(key,player) | Preferences | CaptureSettings, ValidateSettings, RestoreSettings; bindings/sensitivity у владельца |
| Feedbacks(key) | Preferences | FeedbackSettings.Capture/Restore; finite multipliers 0..1 |
| Language(key) | Preferences / -100 | Localization.IsReady/CaptureState/RestoreStateAsync; native initialization ожидается заранее |
| SettingsSaveAdapter.Create(controller/session,key) | Preferences | Только confirmed Capture, Validate, Restore(snapshot,false); экран не переключается |

Полные owner preflight используются непосредственно. Application-validator callbacks прежнего sample больше не требуются. Custom состояние подключается JsonSaveParticipant<T> с настоящим validator владельца. Preflight не резервирует мир: игра приостанавливает команды/симуляцию на время восстановления, owners повторно проверяют состояние перед commit. При callback/commit exception результат PartialRestore, без обещания глобального rollback.

## Building

Factory Buildings заменяет весь loaded local snapshot: stable IDs, private parts, redeemed parcel history, missing records и deferred links. Append TryRestore/replica snapshots не используются. Strict default отказывает при отсутствующем контенте. `preserveUnresolved:true` включает owner PreserveUnknownDefinitions/DeferMissingConnections и требует `Action<BuildingSnapshotReport>`: обработайте IsComplete/counts, не представляйте отсутствующий контент как загруженный. Commit failure, включая report.Committed=true, становится PartialRestore через SaveService.

Состояние выгруженных регионов, находящееся в отдельном BuildingRegionSet, остаётся у его owner и должно быть зарегистрировано отдельно; loaded-world CaptureState не заявляется архивом всех внешних регионов. Custom building part validators должны быть чистыми и работать на prefab configuration; Restore/lifecycle не должны выдавать внешние награды повторно.

## Resources и активные операции

Простой Resources — idle ledger. Для нескольких кошельков используйте ResourceLedgerSave: он сопоставляет states по стабильным ledger keys, а не порядку массива, и устанавливает все кошельки через ResourceSnapshots.RestoreAll. При `includeReservations:true` обязателен clock в milliseconds того же домена, что у owner операций. Capture хранит named OwnerId/hold ID/remaining cost/time; restore использует ResumeReservations и ReplaceActiveReservations.

Snapshot активной операции обязательно регистрируется в том же Progress-составе после кошельков. Owner восстанавливает phase/timer/remaining cost и делает TryGetReservation(id,ownerId,out hold), а не новый Reserve/повторную оплату. Production/progression owners используют свои FromState/RestoreState; синхронный inventory craft не имеет незавершённого persisted handle. Опция includeReservations сама не создаёт и не оживляет owners. Если application не подготовило coordinated operation snapshot, оставайтесь на безопасной idle границе. В новом тесте сохранённый named hold перепривязывается и списывает сумму один раз.

## Playground

`Samples~/Framework Playground/Runtime/EasySuiteSave.cs` — исполняемая композиция production adapters: шесть Progress entries (два кошелька в одном ledger set, Building, Combat, Interaction, Movement, crate) и четыре Preferences entries (Input/Feedbacks/Localization/Settings). Crate — единственный sample-specific JsonSaveParticipant. Самописного JSON file I/O и общего DTO со смешанными настройками больше нет.

Root builder назначает `EasySuiteLab.SaveWorkbench` и `GraphicsSettings`. Методы `Save`/`Load` работают с quick Progress; `SavePreferences`/`LoadPreferences` — отдельные UI-вызовы. Под капотом async methods Saves.*Async. LastResult содержит recover/error/partial details. Sample владеет временным input block и Time.timeScale pause вокруг world operation и снимает их при disable/cancellation; production SaveService сам не управляет игрой.

Smoke использует SaveSnapshot для memory checks, отдельно захватывает/возвращает временные global preferences при cleanup и проверяет, что progress restore не меняет input/audio/language. Исходные игрок/мир из обычного save file для smoke не читаются.