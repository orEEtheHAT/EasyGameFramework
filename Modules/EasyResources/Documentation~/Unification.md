# Подготовка EasyResources к EasyGameFramework

Этап STAGING, 13 ноября 2026. Рабочая копия — `Library/EasyGameFrameworkWork/Package/Modules/EasyResources`; она пока не импортируется Unity. Старые Packages, Assets и опубликованный репозиторий не изменяются. Результаты предыдущего отдельного пакета не выдаются за проверку объединённого.

## Подготовленные изменения

- Имена `EasyResources.*` и границы сборок сохранены. Модуль не содержит package.json, вложенного .git или package-version guards на исчезнувшие Easy-пакеты.
- Команды находятся под `Tools/EasyGameFramework/Resources`; SO/компоненты — под `EasyGameFramework/Resources`. Автоматического welcome popup нет.
- ResourceContainer и InventoryContainer получили `ValidateConfiguration()`. Инспекторы проверяют не только строки, но и вместимость/фильтры/вес/объём; показывают способ исправления до Play. Проверки кешируются до изменения данных/Undo/projectChanged и не заменяют живые Store.
- `ResourceStore.ValidateState` и фасад ResourceContainer предоставляют EasySave немутирующий preflight через тот же PrepareState, что RestoreState. [Контракт загрузки](SaveAndOperations.md) явно отделяет атомарные кошельки от последующего восстановления владельцев операций.
- Basics builder принимает пустую неизменённую стартовую сцену; команды создают новые папки под `Assets/EasyGameFramework/Resources` и не переписывают существующие примеры.
- Production integrations не спрятаны в Samples: у EasyResources собственных cross-module adapters не было. Combat/Building/Interaction/Localization/Save адаптеры остаются у владельцев функций. Networking — существующая транспорт-независимая валидация/DTO, без Netcode SDK; конкретных SDK/XR/AR addons у этого модуля нет.

## Проверено без Editor

Все 17 сборок, включая примеры/Editor/тесты, скомпилированы установленным Unity Roslyn с реальными Unity reference DLLs. Зависимые сборки ссылаются на Runtime, скомпилированный из staging. Unity Editor не открывался/не обновлялся; тесты не выполнялись. Доказательства: `Audits/EasyResources/Unification/staging-compile.json`, `staging-static-checks.json`, `staging-source-manifest.json`. Повторение: `Library/EasyResourcesWork/Unification/CompileStaging.ps1`.

Подготовлено **138 EditMode** и **11 PlayMode** случаев: прежние 118 + 10, десять проверок authoring-конфигурации, десять случаев preflight/возобновления и один PlayMode-тест неизменности живого кошелька/инвентаря при ValidateConfiguration. Ожидаемые counts подтверждаются после CUTOVER, здесь не заявлен результат Unity Test Runner.

## Проверка после CUTOVER

1. Работать в новом пути, который подтвердит координатор, под общей блокировкой редактора. Проверить импорт, отсутствие дублей типов/asmdef, успешную компиляцию и Player/Editor ограничения.
2. Запустить `EasyResources.Tests.EditMode`: ожидается 138. Затем `EasyResources.Tests.PlayMode`: 10, `EasyResources.Tests.UI`: 1. Существующие регрессии составных транзакций, ошибок, переполнений, callback reentry, старых handles и уничтожения владельцев обязательны.
3. Проверить короткий первый запуск из README: общий Resource asset, два независимых контейнера, начальные 10/0/10. Ошибочные лимиты/инвентарь видны до Play; исправление убирает сообщение. Изменение состояния в Play не переписывает SO.
4. Импортировать Basics один раз, проверить 13 PASS и повторный Play. Отдельно создать Basics из чистой untitled scene и убедиться, что команда не требует лишнего сохранения и не перезаписывает существующие assets. Workflows должен сохранить 18 PASS.
5. При загрузке подтвердить, что ValidateState не меняет ревизию, активный hold или scope; wallet restore → owner rebind не списывает дважды; ошибки owner restore не скрываются. Общая координация участников принадлежит EasySave/приложению.
6. Вернуть исходную сцену/настройки и освободить Editor. Root выполняет общий набор сценариев, чистую установку и Player-сборку. Владелец модуля не дублирует целые проверочные проекты/сборки.

## Сопоставимые измерения

`Samples~/Benchmarks/ResourceBenchmark.unity` сохраняет прежние четыре нагрузки, reused/recreated costs, 15 измеряемых партий, три прогревочных, до 10 000 stores / 16 types / 8 rows / 160 000 holds и дополнительные модульные сценарии. Алгоритмы и настройки fixture не сокращены; у основного компонента изменён только путь AddComponentMenu.

`Samples~/Benchmarks/ResourceNotificationBenchmark.unity` добавляет ранее проверенную локальную fixture в единый sample: runtime-исходник побайтово совпадает с Integration Wave 1, изменены лишь GUID/assembly binding сцены. Четыре сценария, 128 прогревочных итераций, 15 × 1000 измеряемых, положительная GC-калибровка и проверка конечных балансов сохранены. `benchmark-parity.json` хранит проверку источников.

Root сравнивает одинаковые backend, hardware, Development settings и объём работы. Старые JSON в `Documentation~/Reports/IntegrationWave1` и прочих Reports — baseline. Новых CPU/GC чисел для общего фреймворка на STAGING нет. Методы preflight/configuration — выделяющие память подготовительные операции; нулевая GC-характеристика прогретых транзакций на них не распространяется. Новая Jobs/Burst-система не вводилась без измеренного потребителя.
