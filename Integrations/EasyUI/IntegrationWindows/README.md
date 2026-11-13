# Integrated windows

Production uGUI окна EasyGameFramework. Все assembly references разрешаются из того же пакета; импорт adapters/sample dependencies больше не нужен.

Tools → EasyGameFramework → UI → Create Integration Windows создаёт семь definitions/prefabs. Editor API: EasyUI.Integrations.Editor.IntegrationWindowBuilder.CreateAt(folder). Runtime: IntegrationWindowSet.InstantiateWindow(definition,parent) создаёт неактивное окно для привязки scene references до Opened.

- Interaction/InteractionRadial: InteractionMenuWindow.Bind(InteractionPresenter).
- Building/BuildingRadial: BuildingCatalogWindow.Bind(BuildingPlacer,BuildingCatalog); BuildingCatalogPresentation.Bind(BuildingResources,LocalizedCatalogCache).
- Settings: IntegratedSettingsWindow.Bind(cache), BindGraphics(SettingsController). AudioWindow и GraphicsWindow назначены фабрикой. Controls/rebind — EasyInput, язык — Localization, аудио — FeedbackSettings, графика — EasySettings.UI.SettingsWindow. Runtime-состояния не копируются.

Дочерние Audio/Graphics окна готовятся до Opened, наследуют контекст и отсутствующий Open preset, сохраняют явные дочерние пресеты и уничтожаются при завершении владельца. Одно EventSystem обслуживает стек, каждое окно имеет отдельный input lease. Закрытие Graphics вызывает отмену draft/preview через владеющий Settings API; controller остаётся жив.

Тексты: resource.<stableId>, building.<stableId>, ui.resource.balance ({0}=имя,{1}=остаток), ui.buildings, ui.building.choose/free/baseCosts. Controls/audio/language toolbar: ui.settings.<key>; Graphics: settings.<key> по контракту EasySettings. LocalizedCatalogCache использует стандартные LocalizedString; fallback позволяет начать без таблиц. Форматные templates общего окна — обычные non-Smart строки; dynamic values форматирует единственный владелец Text. Binding glyphs остаются у EasyInput.

Полный первый запуск и API: [EasyUI guide](../../../Modules/EasyUI/README.md).

## Готовность языка

Общее окно не инициализирует локализацию при открытии. В bootstrap игры явно вызовите `await EasyLocalization.Localization.InitializeAsync(config)` после настройки Unity Localization. В Editor первый setup доступен через Tools → EasyGameFramework → Localization → Translations. Framework Playground уже выполняет явную инициализацию.

До `Localization.IsReady` окно показывает Language unavailable, поясняет необходимость настройки/инициализации и отключает только выбор языка. Controls/rebind, Audio и Graphics продолжают работать. `IntegratedSettingsWindow.CanChangeLanguage` и `LanguageStatus` позволяют читать состояние. Вызов NextLanguage до готовности безопасен и не создаёт settings. После явной инициализации `Changed` обновляет уже открытое окно. Ошибка пользовательского переключения языка отображается в LanguageStatus и UI; она не маскируется ожиданиями логов.

Ключи fallback-состояния: ui.settings.language.unavailable, language.unavailableButton, language.failed. Единственным писателем status Text остаётся InputSettingsWindow, который получает LanguageStatus через свой TextProvider.
