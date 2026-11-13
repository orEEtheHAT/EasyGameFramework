# Названия и сообщения строительства

Встроенная интеграция EasyGameFramework, assembly EasyLocalization.EasyBuilding. Импорт sample не требуется.

1. В Localized Catalog используйте building.{BuildingDefinition.Id} и building.result.{PlacementFailure}, например building.result.CannotAfford.
2. Добавьте Building Translations и назначьте каталог его Localized Catalog Cache.
3. GetName(definition) возвращает название; Resolve(in PlacementResult) — причину. Компонент реализует IBuildingMessageResolver и назначается в BuildingPaletteUI.MessageProvider.
4. Changed объединяет обновление каталога и языка. Подписывайте UI в OnEnable, отписывайте в OnDisable.

Fallback — definition.Name / result.Reason. Стабильный ID не зависит от показанного имени. catalog.SetEntries/NotifyChanged, cache.Catalog и Localization.Refresh идут через общий cache.Changed. Изменения состояния строительства остаются у Building.
