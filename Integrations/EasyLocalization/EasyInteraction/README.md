# Сообщения взаимодействия

Встроенная интеграция EasyGameFramework, assembly EasyLocalization.EasyInteraction. Импорт sample не требуется.

1. Создайте Localized Catalog с исходными ключами Interaction, например interaction.Use и interaction.Busy. Регистр важен.
2. Добавьте Interaction Translations и назначьте каталог его Localized Catalog Cache.
3. Назначьте этот компонент в InteractionPresenter.TextProvider, а его presenter — на тот же presenter.

Translate(key, fallback) реализует IInteractionTextProvider. После готовности переводов presenter.Refresh обновляет видимую подсказку. Changed доступен другому UI. Disable и отключение внутри Changed не вызывают позднее обновление presenter. Для изменения данных используйте catalog.SetEntries/NotifyChanged; таблиц — Localization.Refresh.

Игровой ввод и localized binding prompt остаются у EasyInput. Этот компонент переводит сообщения, а не читает устройства.
