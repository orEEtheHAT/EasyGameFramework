# EasyLocalization в EasyGameFramework

Модуль использует стандартные Unity Localization Settings, Locale, String Table Collection, LocalizedString, LocalizedAsset и Smart Strings. Язык принадлежит Localization; его состояние хранит EasySave в пользовательских настройках. Google Sheets используется только Editor.

## Первый запуск

1. Установите единый EasyGameFramework и откройте **Tools > EasyGameFramework > Setup**. Отдельно устанавливать Localization-адаптеры не требуется.
2. Откройте **Tools > EasyGameFramework > Localization > Translations**, раздел **First setup**. Укажите Primary language `en`, Additional codes `ru`, имя коллекции и папку внутри Assets. Нажмите **Create / connect languages and collection**.
3. **Open Unity string tables**: добавьте ключ и переводы. Для параметров включите Smart. На UI-объект добавьте **EasyGameFramework > Localization > Localized Text**, выберите стандартную таблицу/запись и соедините **Text Changed** с setter UI-текста.
4. Для готового примера импортируйте демонстрационный **First Run** из Samples единого пакета (исходник `Modules/EasyLocalization/Samples~/First Run`). Меню **Tools > EasyGameFramework > Examples > Localization > First Run** создаст сцену. В Play переключаются English/Русский, кнопка +1 изменяет Score/Счёт. Используется New Input System.

First setup создаёт только отсутствующие настройки, локали и таблицы; существующие строки/ID и конфигурация сохраняются. SO `EasyLocalizationConfig` задаёт fallback и preload. Пользовательский язык не записывается в SO или PlayerPrefs.

## Три обычных сценария

Язык и текст:
```csharp
await Localization.InitializeAsync();
await Localization.SetLanguageAsync("ru");
string title = await Localization.TextAsync(titleReference, fallback: "Unknown item");
localizedText.SetArguments(playerName, score);
```

Глобальные настройки Save/UI:
```csharp
LanguageState state = await Localization.CaptureStateAsync(cancellation);
// EasySave хранит state в Preferences, отдельно от игрового слота.
await Localization.RestoreStateAsync(state, cancellation);
// После инициализации доступен синхронный CaptureState.
// Для меню, которое ещё загружается: IsReady / TryCaptureState(out state).
```

Названия игровых определений:
```csharp
string name = resourceNames.GetName(resourceDefinition);
string reason = buildingTranslations.Resolve(placementResult);
resourceNames.Changed += RefreshView; // -= RefreshView в OnDisable
```

Для третьего сценария создайте **Create > EasyGameFramework > Localization > Localized Catalog**. ID: `resource.{ResourceDefinition.Id}`, `building.{BuildingDefinition.Id}`, `building.result.{PlacementFailure}`; значение — обычный LocalizedString. Добавьте Resource Names или Building Translations и назначьте каталог автоматически добавленному Localized Catalog Cache. Interaction Translations подключается к `presenter.TextProvider`, а его `presenter` — к тому же presenter.

Все три production-адаптера находятся в `Integrations/EasyLocalization` и доступны после одной установки. Игровой пример **Gameplay Integration** импортирует только демонстрационные UI/scene builder; он использует эти готовые компоненты. Подсказками биндов владеет EasyInput, окнами — EasyUI.

## Изменения и диагностика

Вызовы выполняются на главном потоке. `Localization.Changed` объединяет готовность settings, смену языка и явное `Localization.Refresh()` после изменения таблиц. `LocalizedText` и catalog cache подписываются сами и отменяют устаревшие запросы при disable/новых данных. Для ручного ожидания есть `RefreshAsync()`.

Каталог меняется через `SetEntries(...)` / `NotifyChanged()`, замена — через `cache.Catalog`. Inspector показывает дубли/пустые ID, отсутствующие таблицы и ключи, пропущенные переводы. Пустой перевод остаётся пустым. Пропущенный перевод ищется в fallback-языке, затем используется текст вызывающего кода. Ссылки по ID переживают переименование ключа; отображаемое имя не является ID.

После внешней замены Unity Settings вызывайте `await Localization.ReinitializeAsync(config)`. `Reset()` отменяет pending requests и отписывает старый settings, сохраняя публичных подписчиков. UI обрабатывает OperationCanceledException бесшумно. Подробности: [runtime и интеграции](Documentation~/RuntimeIntegration.md).

## Google Sheets

Новый mapping по умолчанию получает переводы через **Public link: download only**. Достаточно таблицы с правом **Все, у кого есть ссылка > Читатель**, колонками `ID, Key, en, ru` и URL нужной вкладки с gid. ID храните обычным текстом. **Preview Google → Unity** только показывает план; **Apply exactly this preview** применяет выбранные решения. Пустая ячейка — пустой перевод; исчезновение строки не означает автоматическое удаление.

Для отправки или приватных таблиц явно выберите OAuth, импортируйте Desktop OAuth JSON и подключите аккаунт в Editor. Учётные данные и токены хранятся в `Library/EasyLocalization`, mapping не содержит секретов. Runtime, Player и публичный пакет не зависят от Google SDK. Полные правила preview, конфликтов, отмены и восстановления: [Synchronization.md](Documentation~/Synchronization.md). Проверки используют имитацию транспорта, реальные пользовательские таблицы не изменяются.

## Editor API для мастера и примеров

```csharp
var mapping = EasyLocalization.Editor.LocalizationSetup.Create(
    "Assets/EasyGameFramework/Framework Playground/Localization",
    "en", new[] { "ru" }, "Framework Playground");
var collection = mapping.collection;
```

Вызов работает в чистом проекте без предварительного ручного setup, не требует Google и не перезаписывает существующую конфигурацию. Вызывать в Editor вне Play Mode. Сборка владельца builder ссылается на `EasyLocalization.Editor`; для работы с возвращённой коллекцией — также на `Unity.Localization.Editor`.

[Проверки объединения](Documentation~/Unification.md). Результаты старого самостоятельного продукта находятся в Documentation~/History и не заменяют проверки единого пакета.
