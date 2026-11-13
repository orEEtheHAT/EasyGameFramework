# Изменения модуля Localization

## Объединение в EasyGameFramework

- Resources/Building/Interaction адаптеры включены в штатные Integrations; импорт production-кода из Samples не нужен.
- Gameplay demo использует три готовых компонента и общий cache. Дублирующий объединённый wrapper удалён.
- IsReady, TryCaptureState и CaptureStateAsync позволяют Save/UI захватывать язык безопасно во время загрузки. CaptureState требует готовую session.
- Editor diagnostics проверяют исходные таблицы, ключи, пропущенные переводы и ID каталога без изменения данных.
- First setup сохраняет существующие assets и корректно назначает project locale только новому Settings, даже при другом cached runtime Instance.
- Новый mapping по умолчанию получает публичный CSV. OAuth выбирается явно; секреты остаются только в Library и Editor.
- Отмена проверяется непосредственно перед применением таблиц и перед отправкой. Preview и cancellation покрыты fake-transport регрессиями.
- Меню перенесены в Tools > EasyGameFramework; samples используют штатные зависимости единого пакета.
- Добавлены state/authoring/setup регрессии; production-adapter тесты находятся в module Tests. Test fixtures восстанавливают прежние settings.
- Версия назначается корневому EasyGameFramework. История прежнего самостоятельного пакета сохранена в Documentation~/History.
