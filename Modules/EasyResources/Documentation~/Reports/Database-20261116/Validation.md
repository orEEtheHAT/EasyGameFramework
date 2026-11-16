# Resource Database — 16 ноября 2026

Единая база создаёт настоящие ResourceDefinition-записи внутри одного файла. SO хранит конфигурацию, потребители — прямые typed refs, сохранения — прежние стабильные ID. Инструкция: [ResourceDatabase.md](../../ResourceDatabase.md), награды: [Rewards.md](../../Rewards.md).

## Проверенный результат

| Проверка в Unity 6000.3.22f1, Games Factory | Результат | Доказательство |
|---|---|---|
| Общая компиляция после исправления native ownership | completed, 0 ошибок | [native-final-compile.json](native-final-compile.json) |
| Полный EasyResources EditMode | **180/180** | [module-editmode.json](module-editmode.json) |
| Полный EasyResources PlayMode, включая UI | **15/15** | [module-playmode.json](module-playmode.json) |
| Владение четырьмя записями существующей общей базы | Все 4 прошли без изменения данных | [native-owner-proof.json](native-owner-proof.json) |
| Первый запуск: база + 2 записи + профиль + атомарная выдача | PASS, дробные единицы сохранены | [database-first-run.json](database-first-run.json), [reward-first-run.json](reward-first-run.json) |
| Инспекторы базы и профиля | Проверены визуально | [база](database-inspector.png), [награда](reward-inspector.png) |

Проверяются создание нескольких записей в одном файле, копирование всех полей/ID через ImportDefinitions, явный old→new remap, неизменность оригиналов, запрет дубликатов до изменения базы, Undo/Redo добавления/правки/удаления, инвалидация кеша после child ID/precision edits и Undo, parent replacement, сохранение и синхронный импорт. Все вложенные записи обнаруживаются валидатором ID. Внешний persistent ресурс и неперечисленный native record отклоняются.

Удаление используемой записи блокируется как для reward asset, так и для сохранённого prefab и загруженного несохранённого компонента в изолированной preview-сцене. Неиспользуемую соседнюю запись того же файла можно удалить. Runtime-проверки покрывают прямые ссылки, чужие одноимённые/одно-ID объекты, проверку до RNG, снимки и атомарные награды. Прежний тест прогретого TryGrant с положительным GC-контролем остался зелёным.

## Native ownership в Unity 6.3

На реальном sample четыре скрытых ResourceDefinition имели `IsSubAsset=false` и `IsMainAsset=false`. При этом каждый объект persistent, имеет путь базы, представлен тем же экземпляром в LoadAllAssetsAtPath и содержит тот же GUID с уникальным ненулевым local fileID. Один fileID отрицателен — это допустимое значение `long`. [Фактические идентичности](native-identity-proof.json).

`IsSubAsset` больше не является единственным условием. Публичный Editor-предикат **ResourceDatabaseEditorUtility.IsOwnedResource(db, entry)** проверяет объявленное членство, persistent main database, путь файла, exact loaded object presence, одинаковый непустой GUID и отличающиеся ненулевые fileID. ValidateAsset дополнительно проверяет уникальность всех native fileID и отсутствие orphan records. External catalog entries по-прежнему отклоняются; guard не сводится к совпадению пути.

Регрессия проверяет эти условия после сохранения и ForceSynchronousImport при HideInHierarchy, None и снова HideInHierarchy. Никакие существующие записи общего sample не пересоздаются ради обхода флага. Причина внутреннего native-поведения IsSubAsset не объявляется доказанным engine bug. Документированный LoadAllAssetsAtPath включает скрытые assets и не гарантирует main object на нулевой позиции: [Unity API](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/assetdatabase/loadallassetsatpath).

Первый authoring-прогон дал 8/10: два fixtures пытались создать additive scene поверх untitled Test Runner scene. Они заменены на изолированные preview-сцены с теми же проверяемыми ссылками. После исправления весь модуль 180/180; начальный отчёт сохранён как история диагностики в Audits/EasyResources/Database-20261116/native-authoring.json.

## Масштаб базы и память

Замер Editor, Intel Core i7-14700KF. Полная валидация строит локальный кеш один раз; Contains/TryGet и повторная валидация затем не обходят каталог. Child OnValidate/OnDisable, parent changes, Undo/Deserialize и явное InvalidateConfiguration сбрасывают кеш без глобального сервиса. Profile проверяет только свои строки после готовой базы.

В базе **1000 записей**, 256 прогревочных итераций и **32 000** измеряемых пакетов Validate + Contains + present/missing TryGet: **6,0779 мс**, **0 GC.Alloc событий**. Положительный контроль зарегистрировал 1 выделение.

Одинаковая однострочная награда, seed 1729, 64 прогревочных Roll, 8 партий по 128:

| Записей в базе | Медиана на 128 Roll | Максимум | Выделений за 1024 Roll | Checksum |
|---|---:|---:|---:|---:|
| 1 | 0,3763 мс | 0,3913 мс | 10 240 | 5155 |
| 1000 | 0,3905 мс | 0,4664 мс | 10 240 | 5155 |

Roll создаёт независимый снимок и выделяет память на событии. Увеличение каталога не добавило выделений и не превратило каждый Roll в полный обход 1000 записей. Сравниваются равные объём работы/seed; строгого нестабильного CPU-порога в тесте нет. Подробный отчёт: [scaling.json](scaling.json). Это Editor-замер, не новый Player/IL2CPP benchmark.

## Передача координатору

После тестов осталась единственная активная чистая FrameworkPlayground, 10 roots; Editor ready/stopped, autoRefresh enabled, runInBackground true. Временный first-run fixture удалён, исходный Inspector selection восстановлен. Общая блокировка освобождена перед оформлением отчёта.

Координатор продолжает приостановленный перенос общего sample, remap его согласованной области, проверку reload и retirement старых файлов. Старые отдельные resources намеренно ещё существовали во время owner-прогона; production duplicate-ID guard не ослаблялся и глобальная чистота ID этим отчётом не заявляется. Миграция общей сцены, Combat acceptance и общий релиз принадлежат их владельцам. Владелец Resources не менял общую сцену, metadata, версии или Git-публикацию.
