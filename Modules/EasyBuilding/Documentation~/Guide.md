# Руководство разработчика EasyBuilding 0.2

Начните с первого префаба в [README](../README.md). Все операции Unity выполняются на главном потоке. ScriptableObject хранит настройки; состояние принадлежит сессии, миру и экземплярам. Дополнительные системы подключаются по мере необходимости.

## Карта возможностей

| Задача | Основной инструмент |
|---|---|
| Префабы, сетка, сокеты, зоны | BuildingDefinition + BuildingSystem; готовый ввод BuildingPlacer |
| Несколько игроков/инструментов | BuildingSession + IBuildingActor |
| Перемещение, копии, апгрейды, группы | TryMove, TryCopy, TryUpgrade, BuildingBatch |
| Шаблоны и перенос в инвентарь | BuildingBlueprint, BuildingBlueprintAsset, BuildingParcel |
| Отмена/повтор | BuildingHistory |
| Строительство за время и материалы | WorkRequired, BuildingSite, Work Queue, Building Worker |
| Внешний вид, ремонт | BuildingAppearance, EasyCombat BuildingHealth |
| Корабль, локальная сетка, планета | BuildingSpace, local/hex/isometric/XYZ/spherical grids |
| Опоры и соединённые сети | BuildingStructuralMember, BuildingStructure, BuildingUtilityPort/Network |
| Каталоги, моды, сохранения | BuildingRegistry, BuildingState, BuildingMigrations, IBuildingStatePart |
| Большой мир | BuildingLoadQueue, BuildingRegions, origin shifts |
| Сервер/реплика | BuildingAuthority, команды, ревизии и дельты |
| Каталог UI и ввод | BuildingPaletteUI, BuildingPalette, BuildingRebinding |
| 2D, Tilemap, воксели | Physics2D backend, Cell World и соответствующий renderer |
| Рельеф, дороги, трубы | Terrain Brush, Path Mesh |
| VR/AR | Отдельные дополнения Addons~/XR и Addons~/AR |

## Авторинг и единые правила

Выделите префаб в Project → **Create > EasyBuilding > Definition From Selected Prefab**. Определение создаётся с рассчитанными Bounds. Повторное **Fit Bounds** доступно в инспекторе. Для процедурной/анимированной модели проверьте объём вручную.

Bounds достаточно для простой модели. **Volumes** либо **Cells** описывают сложный занятый объём; **Clearances** резервируют проход или движение двери. Поворот отдельных объёмов требует равномерного масштаба экземпляра. **Surfaces**, **Maximum Slope**, **Align To Surface**, **Support Points** задают пол/стены/потолок и точки опоры. Передавайте нормаль попадания в PlacementRequest. При выключенном Require Surface она используется для выравнивания, без физической проверки опоры.

Grid Layout поддерживает Rectangular/Hex/Isometric; Grid Plane — XZ/XY/XYZ. Без Building Space координаты мировые, с ним — локальные. Зона независимо ограничивает площадь, владельца/команду, тег или запрещает размещение.

Сокеты имеют локальный стабильный ID, тип, роль, размер, Structural и Face To Face. Attachment Socket Id выбирает точку на устанавливаемом префабе; пустое значение использует корневой pivot. Socket Rotation Step ограничивает поворот. TryConnect соединяет уже совмещённые точки, TryDisconnect отсоединяет их. Несколько связей сохраняются вместе.

Готовые rule-assets проверяют расстояние, способности/unlocks, лимит количества и соседей. Группа объединяет AND/OR/NOT. Правила назначаются миру или определению. Своя проверка — `IBuildingRule` + `system.AddRule(rule)`. Allows — чистый запрос: не меняет сцену и ресурсы. Для поиска используйте QueryBuildings/CountNearby/FindSocket и переиспользуемые выходные списки.

## Сессии, операции и история

```csharp
// actor is the player's BuildingActor. The actor owns this shared session.
var session = actor.GetSession(system);
session.Select(definition);
session.RepeatPlacement = true;
var request = new PlacementRequest(point, rotation, space: space);
var preview = session.Check(request);
session.TryConfirm(request, out var building, out var result);

system.TryMove(building, destination, out result, actor);
system.TryUpgrade(building, targetDefinition, out var replacement, out result, actor);
system.TryDemolish(replacement, out result, actor);

system.BeginBatch(actor)
    .Place(wall, left)
    .Place(wall, right)
    .Move(existing, destination)
    .TryCommit(out var operation);
```

Сессия хранит выбор, мир — постройки. `BuildingActor.GetSession(system)` возвращает один экземпляр на пару игрок/мир. Назначьте того же Actor в BuildingPlacer и BuildingInteractionContext: выбор из взаимодействия сразу доступен контроллеру, превью и палитре. `IBuildingSessionProvider.GetSession(BuildingSystem)` позволяет предоставить собственный источник сессии.

`placer.SetSession(session)` явно заимствует живую сессию своего мира; чужой мир или закрытая сессия вызывают ArgumentException, прежняя привязка сохраняется. `SetSession(null)` возвращает автоматическое разрешение Actor/provider/DefaultSession. Выключение и уничтожение Placer/Palette не закрывают заимствованную сессию. Выключение BuildingActor отменяет выбор, уничтожение освобождает его сессии. Automatic provider переподключается после Dispose; явно назначенную закрытую сессию замените через SetSession. Для отдельного инструмента используйте `using var independent = system.CreateSession(actor)` и освободите его сами. Dispose сессии не удаляет мир. DefaultSession сохраняет прежнее API Select/Check/TryConfirm. Null actor означает доверенную системную операцию; сервер получает actor из аутентифицированного подключения.

Короткие hooks контроллера: `Select(definition)`, `CancelSelection()`, `TryConfirm(request, out building, out result)`. `SessionChanged` сообщает о переподключении. У сессии есть `SelectionChanged` (null завершает выбор), `Confirmed` (успешная установка), `Disposed` и `System`. При подписке прочитайте текущий Selected: выбор мог существовать до подключения UI. При замене сессии снимите подписки со старой. RepeatPlacement оставляет выбор после успеха; Cancel из callback завершает выбор и повторение, уже исполняющаяся атомарная операция может закончиться. BuildingPalette.SetSession меняет источник выбора, сохраняя избранное и слоты; null временно отключает выбор. BuildingPaletteUI следует текущей Controller.Session автоматически.

Batch проверяет итог группы, допускает обмен позициями и оплачивается одним резервом. Успешный batch нельзя подтвердить повторно. BuildingPatterns формирует линии, прямоугольники/границы, кисти и расстановку вдоль ломаной с лимитом количества.

В **Upgrades** перечислите допустимые определения. Апгрейд сохраняет InstanceId, владельца, квитанцию, совместимые связи и данные адаптеров; незавершённую стройку сначала закончите. Сохраняйте ID и геометрию используемых сокетов. IBuildingUpgradeStatePart преобразует данные при смене префаба, например процент повреждения.

```csharp
using var history = new BuildingHistory(system, catalog, actor, capacity: 64);
// Обычные операции мира попадают в историю.
history.TryUndo(out var reason);
history.TryRedo(out reason);
```

Платная история требует IBuildingEconomy: ресурсы возвращаются/списываются по квитанциям. Внешнее изменение состояния, урон, упаковка и streaming инвалидируют старую ветку. История — ограниченный инструмент редактирования, сохранение игры выполняет BuildingState.

CaptureBlueprint сохраняет группу относительно pivot, TryPlaceBlueprint переносит внутренние связи и выдаёт новые ID. Состояние компонентов копируется только при includeComponentState. Asset **Create > EasyBuilding > Blueprint** хранит схему. TryCopy создаёт свежую копию.

TryPack возвращает BuildingParcel с данными и квитанциями. TryUnpack принимает его один раз; погашенные ID сохраняются в BuildingState. Сохраняйте мир и выданные предметы/parcel одной игровой операцией. Содержимое parcel формирует доверенный сервер.

## Стройка, оплата, материалы и здоровье

При **Work Required > 0** создаётся отдельная площадка: скрипты готового префаба запускаются после завершения. Construction Visual/Phases показывают этап. Upfront оплачивает размещение; Delivery собирает материалы отдельными доставками.

```csharp
system.TryDeliver(site, materials, out var result, actor);
system.TryWork(site, 1, out var current, out result, actor);
system.TryPauseConstruction(current, true, out result, actor);
system.TryCancelConstruction(current, out result, actor);
```

Сохраняются прогресс, материалы и пауза. Завершение заменяет площадку готовым объектом с тем же ID: используйте `current` или повторный поиск по InstanceId. Work Queue хранит задания по ID и переживает выгрузку. Building Worker вносит работу рядом с целью; перемещение и навигация остаются у AI игры.

BuildingAppearance содержит варианты материалов с ID, привязками renderer и стоимостью. TrySetVariant меняет существующий объект без индивидуальных экземпляров Material. Привязки должны находиться внутри префаба.

Встроенная интеграция EasyCombat предоставляет BuildingHealth: здоровье сохраняется, смерть удаляет постройку, TryRepair оплачивает реально восстановленное значение, апгрейд сохраняет долю повреждения. Ремонт отклоняется внутри текущей боевой транзакции. Остальные статусы/защиты сохраняет EasyCombat. Встроенная интеграция EasyFeedbacks связывает события размещения/удаления с пресетами. Необязательный Context Provider принимает активный MonoBehaviour с EasyFeedbacks.IFeedbackContextProvider: например, PlayerFeedbackContext из EasyInput добавляет геймпад игрока, сохраняя явный TargetGamepad, запрет вибрации WithoutGamepad() и все остальные поля контекста. Пустой или выключенный provider сохраняет исходное поведение. PrepareContext позволяет применить ту же настройку к своему FeedbackContext.

Встроенная интеграция EasyResources использует публичный ResourceTransaction, несколько контейнеров, резервы и возвраты. Цены — целые `long` минимальные единицы, без промежуточного float. Подробности: [контракт оплаты](Payments.md).

Полная загрузка игрового слота: ValidateSnapshot + TryReplaceSnapshot. Подробная семантика и ограничения — [Snapshots](Snapshots.md). TryRestore добавляет группу, а не заменяет мир.

## Состояние, опоры и пространства

IBuildingStatePart на компоненте префаба задаёт уникальный StateKey, версию, проверку/восстановление и признак ReplicateState. Проверка выполняется до активации. После внешнего изменения данных вызывайте NotifyStateChanged. Подготовленная платная мутация использует IBuildingPreparedMutation: чистая CanApply, гарантированно завершающийся Apply.

Structural Member задаёт массу, ёмкость и Foundation. Structural-сокеты формируют граф. BuildingStructure строит детерминированное дерево ближайших опор: нагрузка целиком передаётся выбранному родителю. Есть состояние Unsupported/Overloaded, задержка и бюджет удаления. Это дискретная игровая модель; распределение нагрузки по нескольким физическим балкам не вычисляется. Автоматическое обрушение приостановлено до разрешения отложенных связей загрузки.

Utility Port задаёт направление, пропускную способность и открытое состояние. Socket Type — канал (power/water). BuildingUtilityNetwork находит кратчайший путь по числу связей и достижимые постройки. Производство, расход, давление и потери рассчитывает игра.

BuildingSpace живёт на постоянном корне сцены, корабля/платформы. Сохраняются его ID и локальная поза постройки. Масштаб положительный равномерный. Radial gravity + Sphere Radius/Step задают сферическую сетку. ShiftOrigin сдвигает корень, накапливает double-смещение и публикует событие для остальных систем; сам корень/offset сохраняйте системой мира.

BuildingSystem/BuildingSpace не помещаются внутрь заменяемого building prefab. Для составной конструкции используется blueprint, для помещений на транспорте — постоянное пространство транспорта.

## Моды, сохранения и большой мир

BuildingRegistry атомарно регистрирует именованные наборы; повторные ID отвергают весь набор. BuildingState v2 читает v1, сохраняет transform/space, владельца, оплату, стройку, связи и component parts. BuildingMigrations работает с копией. PreserveUnknownDefinitions удерживает неизвестные записи для последующего разрешения; геометрия для них не создаётся.

```csharp
var regions = new BuildingRegions(system, catalog, regionSize: 64);
regions.Add(savedWorld);
regions.SetInterest(playerPosition, radius: 128);
// В Update:
regions.Tick(maximumBuildings: 64, milliseconds: 2, batchSize: 8);
// Для сохранения:
BuildingState all = regions.CaptureAll();
// Сохраните all перед Dispose().
```

Для нескольких игроков передайте список BuildingInterest: загружается объединение областей и связанных зависимостей. Позиция локальна к указанному SpaceId. Меняйте интерес после завершения текущего задания. После регистрации новых модов вызовите RefreshCatalog. Архивные объёмы и clearances блокируют размещение до загрузки соответствующих регионов; неизвестный контент блокирует своё пространство консервативно.

Архив находится в памяти. Файловое IO, БД и сжатие задаёт игра. Выгрузка целой связанной группы атомарна и может занять больше кадра — выбирайте разумный размер групп мира. BuildingLoadQueue доступна отдельно: каждая маленькая пачка атомарна, время проверяется между пачками. Один тяжёлый Awake способен превысить бюджет. Cancel оставляет загруженное; CaptureRemaining сохраняет остаток для продолжения.

## FPS с EasyMovement и EasyInput

1. Используйте игрока EasyMovement на ECM2. Добавьте BuildingActor на объект игрока. BuildingSystem и BuildingPlacer держите на постоянном объекте сцены, чтобы замена игрока не уничтожала мир.
2. В Placer назначьте Actor игрока, View Camera камеры EasyMovement, включите Aim From Screen Center, задайте Reach, например 6. При новом игроке обновите Actor и камеру; Session разрешится заново.
3. Используйте штатную интеграцию **EasyBuilding** модуля EasyInput. На объект Placer добавьте **EasyBuilding Input**, назначьте Player Input Settings и Placer. Примените **Use First Person Preset** из контекстного меню компонента до Play. В общем input asset задайте действия Building/Confirm и Building/Cancel; вращение и снос подключаются теми же InputActionLink по необходимости.
4. При выборе из каталога или взаимодействия вызовите `if (placer.Select(definition)) buildingInput.Enter();`. `buildingInput.Exit()` отменяет выбор и освобождает свой контекст. Успешная одиночная установка и внешний Cancel также завершают режим. FPS-пресет сохраняет ходьбу и обзор; конфликтующие игровые действия блокирует EasyInput. Проверяйте возвращаемый bool Enter, если контроллер временно выключен.
5. Если нужен EasyInteraction, назначьте тот же BuildingActor через BuildingInteractionContext. Выбор, полученный `context.GetSession(system)`, должен быть тем же объектом, что `placer.Session`. Для отклика добавьте штатный BuildingFeedbacks и назначьте пресеты; Context Provider может ссылаться на PlayerFeedbackContext этого игрока.

Ожидаемый результат: выбор из UI или взаимодействия появляется в центре FPS-камеры; ходьба и обзор работают, подтверждение ставит один объект, отмена не списывает ресурсы. Повторное открытие UI не создаёт вторую сессию. Общие переназначения и профили принадлежат EasyInput, окна и фокус — EasyUI; самостоятельный BuildingPaletteUI остаётся доступен без этих интеграций.

## Сервер и ввод

```csharp
using var authority = new BuildingAuthority(serverWorld, registry);
var session = authority.OpenSession(serverSessionId, authenticatedActor);
var response = authority.Execute(session, command);
```

Цена, actor и его позиция определяются сервером. Повтор Sequence с тем же payload возвращает прежний ответ, с другим — отклоняется. ExpectedRevision/ExpectedVersion, ограничения количества/размера/частоты, дистанции, масштаба и работы проверяются авторитетно. CaptureReplayState сохраняйте вместе с миром. Пропуск журнала дельт требует свежего снимка. Клиент использует SetReadOnly(true), TryApplyReplicaSnapshot/Delta; локальное размещение блокируется.

Addons~/Networking/EasyBuilding содержит ограниченный TCP localhost с поздним подключением и дельтами, без службы аккаунтов/лобби. Интернет-транспорт и интерес клиента предоставляет игра. CellState и TerrainState имеют отдельные API и передаются собственным серверным протоколом.

BuildingPaletteUI требует uGUI, Controller и Catalog. Есть поиск, категории, избранное, 10 слотов: B/gamepad Y открывает меню, 1–0 выбирают, Shift+цифра назначает слот. Controls — Input System rebinding. Пустой Preferences Key отключает PlayerPrefs; для разных профилей задавайте разные ключи.

BuildingMessages переводит причины размещения. MessageProvider либо MessageResolver принимает IBuildingMessageResolver, в том числе отдельный адаптер Unity Localization/EasyLocalization. Подписи стандартного UI английские. BuildingPalette можно использовать со своим интерфейсом. EasyInput может владеть общими действиями/профилями; подключайте их через InputActionReference или IBuildingPlacementInput и используйте экран настроек EasyInput. Адаптер локализованной подсказки кнопки принадлежит EasyInput.

SetInputBlocked(owner, bool) сохраняет независимые блокировки разных меню. VisibilityChanged помогает связать каталог с курсором/обзором EasyMovement. Свой targeting-контроллер включает External Targeting, вызывает UpdatePreview и Session.TryConfirm; при потере цели — HidePreview. Предпросмотр копирует только меши, не запускает игровые скрипты.

## Ячейки, Tilemap, воксели, пути и Terrain

Cell Palette содержит устойчивые ID типов. 0 означает пустоту; первая строка палитры — 1. Cell World хранит разреженные чанки 16³ компактных ushort-значений.

```csharp
var edits = new[] { new EasyBuilding.Cells.BuildingCellEdit(new Vector3Int(2,0,-1), 1) };
cells.TryApply(edits, out var stroke, out var reason, actor);
cells.TryUndo(stroke, out reason, actor);
var saved = cells.Capture();
cells.TryRestore(saved, out reason);
```

Поддержаны отрицательные координаты, сохранение с ID-пересопоставлением палитры, лимиты правки/сохранения, агрегатная оплата и отмена последней правки один раз. Стирание само по себе не возвращает ресурсы; undo возвращает её фиксированный платёж. IBuildingCellRule задаёт ограничения. Не переставляйте палитру во время сессии; ID-пересопоставление происходит при restore.

Voxel Renderer создаёт меш и опциональный MeshCollider на видимый чанк, убирает общие грани между чанками, обновляет меш/коллайдер с бюджетом. Материал цветов необязателен, Cell Size настраивается. Значения непрозрачные: прозрачные жидкости, LOD и greedy meshing можно реализовать другим renderer поверх того же Cell World.

Tilemap Renderer связывает Cell World с отдельной пустой Tilemap и Cell Id → TileBase. Layout задаёт Unity Grid, коллайдер — TilemapCollider2D. Renderer владеет записанными клетками и очищает их при выключении; независимое ручное содержимое размещайте в другой Tilemap.

Path Mesh создаёт дорогу/открытую с торцов трубу из локальных точек, со сглаживанием Catmull–Rom. TrySetPoints сохраняет форму; у PlacedBuilding операция учитывает права и цену. Cost Per Unit относится к полной новой локальной длине. Геометрия должна помещаться в размещаемый объём определения. Длинные трассы делите на сегменты; перекрёстки и движение транспорта задаются игрой. Сингулярные развороты и превышение лимита выборок отклоняются.

Terrain Brush работает в Play Mode с собственной копией TerrainData, синхронизирует TerrainCollider и возвращает исходные ссылки при уничтожении. Terrain использует единичный поворот/масштаб. TryPrepareCircle создаёт патч RaiseLower/Flatten/Smooth; TryApply/TryUndo учитывают фактические квантованные высоты и конфликт свежей правки. RaiseLower меняет мировую высоту, Flatten/Smooth принимают силу 0–1. TerrainState сохраняется рядом с BuildingState; экономику/серверную проверку кисти выполняет инструмент игры.

XR/AR setup и границы аппаратной проверки указаны в README соответствующих Addons~. Базовый пакет не зависит от этих SDK.
