# Runtime и встроенные интеграции

Все API ниже принадлежат модулю EasyLocalization единого EasyGameFramework. Ядро не зависит от Resources/Building/Interaction/Save/UI; production-связки компилируются отдельно в Integrations/EasyLocalization и доступны без импорта samples.

## Состояние языка

`LanguageState` — serializable struct с полем `string languageCode`. Сохраняется стабильный код текущего, уже выбранного языка. Незавершённый запрос выбора ещё не меняет snapshot. Хранилище относится к EasySave Preferences, не к игровому слоту.

- `InitializeAsync(LocalizationConfig config = null)`: одна общая инициализация текущего settings/config.
- `SetLanguageAsync(string code, CancellationToken cancellation = default)`: последний запрос имеет приоритет; возвращает реально выбранный код. Неизвестный код выбирает fallback, затем первый Locale.
- `IsReady`: без запуска загрузки проверяет готовность текущей session.
- `TryCaptureState(out LanguageState state)`: false до готовности или после смены settings/reset.
- `CaptureState()`: быстрый синхронный захват после инициализации. До готовности — понятная InvalidOperationException вместо незаметного сохранения пустого кода.
- `CaptureStateAsync(CancellationToken cancellation = default)`: безопасный холодный захват; отмена клиента не отменяет общую инициализацию.
- `RestoreStateAsync(LanguageState state, CancellationToken cancellation = default)`: обычная политика SetLanguageAsync; отменяемый restore. Не меняет configuration SO. Если Save требует строгой проверки удалённых locale, он проверяет код по `Languages` после InitializeAsync.

## Жизненный цикл

Session хранит конкретный Unity LocalizationSettings.Instance, config и lifetime token. Новые settings не наследуют config прежнего владельца. `Configuration` позволяет захватить явный config перед временной заменой.

`Reset()` отсоединяется от фактического прежнего settings, отменяет его задачи и сохраняет публичных подписчиков. Не уничтожает Unity settings и не меняет выбранный Locale. `ReinitializeAsync(config)` делает Reset и начинает новую инициализацию. Внешнюю замену Instance завершайте этим вызовом: сама запись в Unity Instance не генерирует событие EasyLocalization до обращения к его API.

В fixture сначала отключите/уничтожьте представления, затем Reset, восстановите прежний Instance и дождитесь ReinitializeAsync(previousConfig). Стандартные Unity LocalizedString владеют отдельными подписками; их освобождает создавший адаптер. Отменённые старые задачи завершаются OperationCanceledException.

## Обновление представления

`Localization.Changed` сообщает о готовности, языке и явном `Refresh()` после изменения source tables. `LanguageChanged` передаёт код и также уведомляет при готовности нового settings с прежним кодом. Подписка в OnEnable, отписка в OnDisable. `LocalizedText` делает это сам; `SetArguments`, новая загрузка и Disable отменяют устаревшее ожидание; `RefreshAsync()` позволяет дождаться конкретного результата.

`LocalizedCatalog`: стандартные LocalizedString, сопоставленные стабильным ID определения/сообщения. Меняйте список через `SetEntries(CatalogTranslation[])`; после правки массива вызывайте `NotifyChanged()`. Inspector уведомляет cache при изменении каталога в Play Mode.

`LocalizedCatalogCache` публикует целый снимок через `Changed`. `Get(id, fallback)` использует актуальный fallback при отсутствующем переводе или неготовом settings. Явно пустой перевод остаётся пустым. `IsReady`, `Catalog`, `RefreshAsync()` доступны для UI. Disable отменяет загрузку и отписывает фактический старый каталог.

`SetLanguageAsync` завершает выбор языка; уведомление Unity и обновление представлений могут прийти позже. Кэш сразу считает снимок прежнего языка неготовым и возвращает fallback. Для ожидания используйте `await Localization.SetLanguageAsync(code); await cache.RefreshAsync();`: ожидание кэша следует за более новым обновлением в той же session, включая запоздавшее уведомление Unity. Одновременные вызовы дожидаются актуального полного снимка. Disable (даже с немедленным Enable), Reset и замена session отменяют прежнее ожидание; новое ожидание начинайте отдельно. Настоящие ошибки загрузки остаются ошибками задачи.

## Production-компоненты

Namespace `EasyLocalization.Integrations`:

| Сборка | Компонент | Вызов / ID |
| --- | --- | --- |
| EasyLocalization.EasyResources | ResourceNames | GetName(ResourceDefinition), resource.{Id} |
| EasyLocalization.EasyBuilding | BuildingTranslations | GetName(BuildingDefinition), building.{Id}; Resolve(in PlacementResult), building.result.{Failure} |
| EasyLocalization.EasyInteraction | InteractionTranslations | Translate(key, fallback), исходный ключ сообщения |

База `LocalizedCatalogAdapter` предоставляет Changed, Cache, Translate и Refresh, автоматически добавляет cache. BuildingTranslations реализует IBuildingMessageResolver; назначается в BuildingPaletteUI.MessageProvider. InteractionTranslations реализует IInteractionTextProvider; назначьте presenter.TextProvider и обратную ссылку adapter.presenter. Названия берутся из общего cache; количества/прогресс остаются у игровых модулей.

Общий игровой пример использует три production-компонента на одном cache. Дублирующего GameplayTranslations wrapper нет. EasyInput владеет localized binding prompt, EasyUI — представлением текста окон. Зависимостей ядра от этих модулей нет.

## Диагностика исходных таблиц

Editor API `LocalizationDiagnostics.Inspect(LocalizedString, string subject = "Text")` и `Inspect(LocalizedCatalog)` возвращает IReadOnlyList<LocalizationIssue>. У issue есть Code, Subject, Message, Severity. Коды: catalog.missing, catalog.id.empty, catalog.id.duplicate, reference.empty, table.missing, key.missing, key.by-name, translation.missing.

Проверка читает стандартные source tables и не меняет ID/ключи/переводы. Ошибки показываются в Inspector LocalizedText/LocalizedCatalog. Ссылки по ID не требуют обновления после RenameKey. Пустое значение не считается пропущенным переводом.
