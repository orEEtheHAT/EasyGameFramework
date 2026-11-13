# Снимки локального мира и EasySave

`CaptureState()` захватывает полный локальный мир: instance/definition IDs, версии, owner/team, pose/scale/space, частные state parts, оплату, стройку, redeemed parcel IDs, неизвестные определения и отложенные связи. Это данные механики, не файловое хранилище. Формат/слоты/запись принадлежат EasySave. Глобальные пользовательские настройки не входят в этот снимок.

```csharp
var saved = world.CaptureState();
if (!world.ValidateSnapshot(saved, catalog.Buildings, out var reason))
    throw new System.InvalidOperationException(reason);
if (!world.TryReplaceSnapshot(saved, catalog.Buildings, out reason))
    throw new System.InvalidOperationException(reason);
```

ValidateSnapshot читает данные и конфигурацию каталога/пространств/сокетов, не создаёт GameObject, не применяет состояние, не резервирует деньги, не отменяет выбор и не вызывает события мира. Поддерживается текущая версия BuildingState. CaptureState(true) помечает ReplicationOnly; такая проекция отвергается. Для известных завершённых построек обязательны все state parts, включая приватные. Проверка снимка не применяет текущие правила новой установки (например, оплату или физические препятствия). Часть данных версии отдельного Definition должна совпадать с SaveVersion. Сохранённые IDs не следует регенерировать при загрузке.

TryReplaceSnapshot заменяет весь локальный мир. До commit объекты создаются и заполняются в неактивном staging, исходный мир остаётся. После проверки заменяются loaded/missing records, все связи и история redeemed parcels, сбрасывается старый список unloaded IDs; Revision берётся из снимка. Исчезнувшие из снимка постройки удаляются без demolition/refund. Старый выбор отменяется. События Placed/Removed и экономика не вызываются; после применения отправляется Restored. Повторный restore не означает новую покупку/награду.

## Контент, который временно отсутствует

По умолчанию загрузка строгая. Для сохранения неизвестных модов или ещё не загруженных endpoints явно выберите политику и проверьте отчёт:

```csharp
var options = new BuildingRestoreOptions {
    PreserveUnknownDefinitions = true,
    DeferMissingConnections = true
};
if (!world.ValidateSnapshot(saved, definitions, options, out var check, out var error))
    throw new System.InvalidOperationException(error);
// check.IsComplete=false: сохранится неразрешённый контент, это не скрывается.
bool ok = world.TryReplaceSnapshot(saved, definitions, options, out var applied, out error);
// При !ok и applied.Committed=true снимок уже применён; сообщите PartialRestore.
```

Отчёт: Committed, LoadedBuildings, MissingDefinitions, DeferredConnections, IsComplete. Сначала проверяйте bool результата. MissingBuildingCount/PendingConnectionCount доступны и у мира. Unknown records сохраняют исходные части и приватные данные; нельзя проверить их специфический payload, пока определение отсутствует. Когда контент вернулся, используйте TryResolveMissingBuildings / ResolveDeferredConnections.

Потеря данных через SkipUnknownDefinitions/IgnoreMissingStateParts/UseWorldPoseWhenSpaceMissing для полной замены запрещена. Пространства должны быть загружены. Регионы, выгруженные в отдельный BuildingRegionSet или хранилище приложения, не являются частью загруженного мира: их архив сохраняет владелец streaming-системы отдельно.

## Граница ошибок и пользовательские компоненты

Ошибка до commit возвращает false и Committed=false; старые зарегистрированные объекты/связи/история остаются. Ошибка завершения после commit возвращает false, Committed=true и явную причину. Ошибка одного Restored observer не пропускает остальных. EasySave должен показать частичное применение, если этот флаг установлен; нельзя считать любой false гарантией отката.

IBuildingStatePart.CanRestoreBuildingState — чистая проверка данных/авторской конфигурации. Она вызывается и на prefab без Awake/OnEnable, и на неактивной заготовке. RestoreBuildingState меняет только состояние собственного неактивного экземпляра, не оплачивает действия и не выдаёт награды. Произвольные побочные эффекты пользовательских callbacks не могут быть автоматически отменены механизмом создания объектов.

Существующий TryRestore остаётся операцией добавления группы (например, streaming). TryApplyReplicaSnapshot и CaptureState(true) обслуживают сетевые реплики и не заменяют загрузку локального слота.
Unity может сама перехватывать исключения Awake/OnEnable и записывать их в Console, не передавая исключение вызвавшему SetActive. Committed/report описывает контролируемые операции восстановления и Restored subscribers, не заменяет проверку Unity Console и контракт корректных prefab lifecycle callbacks.
