# Названия ресурсов

Встроенная интеграция EasyGameFramework, assembly EasyLocalization.EasyResources. Импорт sample не требуется.

1. Создайте **Create > EasyGameFramework > Localization > Localized Catalog**.
2. ID строки — resource. + ResourceDefinition.Id; Text — обычная ссылка на запись таблицы.
3. Добавьте **EasyGameFramework > Localization > Resource Names**. Назначьте каталог автоматически добавленному Localized Catalog Cache.
4. Читайте resourceNames.GetName(definition); подпишитесь на Changed в OnEnable и отпишитесь в OnDisable.

Fallback — definition.DisplayName, явный пустой перевод остаётся пустым. Переименование определения не меняет ID. catalog.SetEntries/NotifyChanged и Localization.Refresh обновляют тот же Changed. Для замены используйте cache.Catalog / resourceNames.Cache.
