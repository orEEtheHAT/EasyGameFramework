# Локализованный игровой пример

Импортируйте этот демонстрационный sample из единого EasyGameFramework. Resources, Building, Interaction и их Localization-адаптеры уже находятся в пакете.

1. Выполните **Tools > EasyGameFramework > Examples > Localization > Gameplay**.
2. Откройте созданную Gameplay сцену и нажмите Play.
3. English/Русский меняют Wood/Дерево, Wall/Стена, причину отказа постройки и Use/Использовать, Busy/Занято в реальном InteractionPresenter.

Builder вызывает LocalizationSetup.Create и создаёт недостающие Settings, Locale en/ru и стандартные таблицы; ручной предварительный setup не нужен. Повторный вызов сохраняет существующие таблицы/строки/ID и сцену.

На корневом объекте один LocalizedCatalogCache и три готовых production-компонента: ResourceNames, BuildingTranslations, InteractionTranslations. Presenter.TextProvider ссылается на InteractionTranslations; обратная ссылка presenter обновляет видимую подсказку. UI примера подписан на общий cache.Changed и presenter.Changed; Disable/Destroy освобождает подписки.

ID: resource.{ResourceDefinition.Id}, building.{BuildingDefinition.Id}, building.result.CannotAfford, interaction.Use, interaction.Busy. Регистр важен. Значения — стандартные LocalizedString с ID записи таблицы. Исходные имена игровых определений остаются fallback.

Синхронизация Google для работы примера не требуется. Demo UI, BusyExampleAction и scene builder принадлежат Samples; production-адаптеры находятся в Integrations/EasyLocalization и повторно не импортируются.
