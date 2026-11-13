# EasySave — модуль EasyGameFramework

Слоты, метаданные, отдельные Preferences, безопасные локальные файлы и восстановление через API владельцев состояния. Устанавливается вместе с **com.oreethehat.easy-game-framework**. Production adapters уже включены в `Integrations/EasySave`; импортировать Samples для них не нужно.

## Начать через Inspector

1. Create → EasyGameFramework → Save Configuration. Autosave по умолчанию выключен.
2. Добавьте Save Controller, назначьте конфигурацию.
3. Добавьте Framework Save Bindings: задайте постоянные Player/Wallet/Interaction IDs, назначьте явные ссылки на владельцев и каталог ресурсов. Input/audio/language/graphics автоматически относятся к Preferences, движение/ресурсы/взаимодействия — к Progress.
4. Добавьте Framework Save Bindings в список Participants Save Controller. Для языка перед первой операцией Preferences вызовите `await bindings.InitializeAsync()`.
5. Кнопки игры вызывают `await controller.QuickSaveAsync()` / `QuickLoadAsync()`; отдельные кнопки меню — `SavePreferencesAsync()` / `LoadPreferencesAsync()`. Проверяйте `Success`, показывайте `Message` и обрабатывайте `PartialRestore`.

Состав мира с Combat/Building задаётся явными registries/catalogs через фабрики ниже. Игровой bootstrap должен регистрировать все необходимые участники до первого запроса. Default IDs не заменяют уникальные IDs вашего мира.

## Регистрация из кода

```csharp
var saves = configuration.CreateService(); // Unity main thread
using var bindings = saves.RegisterAll(
    EasySaveAdapters.Movement("player/movement", character),
    EasySaveAdapters.Input("player/input", input),
    EasySaveAdapters.Combat("world/combat", combatCatalog, actorRegistry),
    EasySaveAdapters.Buildings("world/buildings", buildings, buildingCatalog),
    EasySaveAdapters.Feedbacks("global/audio"),
    SettingsSaveAdapter.Create(graphics, "global/graphics"));

var written = await saves.SaveAsync("slot-1", displayName: "Перед мостом");
var loaded = await saves.LoadAsync("slot-1");
// Владельцу bootstrap: Dispose bindings и saves при завершении сессии.
```

## Глобальные настройки и checkpoints

```csharp
await EasyLocalization.Localization.InitializeAsync();
using var language = saves.RegisterAll(EasySaveAdapters.Language("global/language"));
await saves.SavePreferencesAsync(); // input/audio/language/URP, не прогресс

var checkpoint = saves.CaptureSnapshot(); // только Progress, без файлов
var result = await saves.RestoreSnapshotAsync(checkpoint);
if (!result.Success) Debug.LogWarning(result);
```

CaptureSnapshot использует те же адаптеры, а RestoreSnapshotAsync — те же миграции, preflight и порядок, что файловая загрузка. Snapshot содержит сериализованные значения, не live Unity objects. RegisterAll либо регистрирует весь набор, либо ничего; Dispose handle освобождает только свой набор.

`ListAsync(scope)` возвращает метаданные/ошибки слотов; `DeleteAsync(slot,scope)` удаляет выбранный слот и backup. Имена слотов lowercase, UI DisplayName отдельно. Disable контроллера отменяет owned session, повторное включение создаёт новую. Ни сохранения при Quit без await, ни скрытого глобального менеджера нет.

[Контракты](Documentation~/Contracts.md) · [Хранилище](Documentation~/Storage.md) · [Production интеграции](Documentation~/Integrations.md) · [Первый запуск](Documentation~/FirstRun.md).

Unity-проверки объединённого staging выполняются только после CUTOVER координатора. Исторические Reports относятся к прежним отдельным пакетам и не являются подтверждением текущего staging.