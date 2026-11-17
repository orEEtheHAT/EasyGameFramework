# EasyInteraction — модуль EasyGameFramework

Выбор объектов, команды, физическое удержание, механизмы, условия, совместная работа и состояние мира. Модуль устанавливается вместе со всем EasyGameFramework; производственные интеграции уже доступны. ECM2, Cinemachine и New Input System входят в штатное окружение автора. Netcode и XR находятся в отдельных дополнениях.

## Первый запуск

1. Создайте или выберите FPS-игрока EasyMovement через общий Setup. Выберите его корень или камеру и выполните **Tools > EasyGameFramework > Interaction > Set Up Selected Player**. Команда связывает Actor, Presenter, Motor и EasyInput с существующим Character; повторный вызов не дублирует компоненты.
2. Создайте **GameObject > EasyGameFramework > Interaction > Grab Item**. Поставьте куб в 2–3 метрах перед камерой. Rigidbody динамический; Authored Pose, owner collision exclusion, Hold Distance 2, нулевые Local Position/Local Euler Angles и Follow Speed 0.5 уже настроены.
3. Нажмите Play: **E** поднимает предмет, повторный **E** отпускает даже при движении мыши и потере фокуса. Предмет плавно следует за взглядом, перестаёт вращаться после остановки и сталкивается со стенами. **Q** отменяет, **G** бросает, **R + мышь** вращает, колесо меняет расстояние. EasyInput управляет переназначениями и блокирующими контекстами.
4. Для двери или механизма выберите соседние команды **Door**, **Linear Handle**, **Physics Lever**. Linear Handle использует Analog: E начинает управление, горизонтальное движение мыши меняет Value; отпускание E завершает его. Для собственного UI используйте Presenter.

Новые Actor используют **Focus**: конус60° с приоритетом направления и проверкой видимости; Range и слои ограничивают поиск. Для точного луча выберите **Raycast** явно; сохранённый выбор существующих Actor не перезаписывается. Дополнительно доступны SphereCast, Cursor, Nearest, Contact и Explicit. **Tools > EasyGameFramework > Interaction > Validate Open Scenes** объясняет отсутствующие коллайдеры и дубликаты ID.

## Короткое API

```csharp
actor.SelectionMode = InteractionSelectionMode.Focus;
actor.Range = 3;
actor.FocusAngle = 30;
actor.PressPrimary(); // Основная кнопка: свой Grab раньше любого hover.
```

```csharp
// Адресные команды меню/use/socket не подменяются отпусканием Grab.
presenter.Activate(door);
actor.TryBegin(action, out var ticket);
actor.Cancel(ticket); // Отменяется конкретный запуск, а не новый запуск той же action.
```

```csharp
grab.LocalPosition = new Vector3(.15f, -.1f, 0);
grab.LocalEulerAngles = new Vector3(0, 90, 0);
grab.FollowSpeed = .5f; // 0 — мягко, 1 — быстро.
// Уменьшить продольное отставание при ходьбе, оставив боковое/вертикальное и повороты:
grab.MovementLag = new Vector3(1, 1, 0);
grab.HeldScaleMultiplier = .65f; // Опционально: уменьшить весь предмет только на время удержания (default1).
grab.Throw(actor);
// Линейная ручка: начните action, затем передавайте направление -1..1.
actor.SetValue(handle, 1);
```

Primary-команда: `PressPrimary(out action, toggle)` возвращает выбранную action для matching Release/ticket tracking. `Press(action)` и `Presenter.Activate(action)` остаются явными командами. При изменениях UI/input context разрешения проверяет reader, а не core. [Маршрутизация ввода](Documentation~/GrabReleaseInput.md).

## Настройка удержания и механики

Новый Grab Action использует **Authored Pose**: **Local Position** и **Local Euler Angles** задают позу Rigidbody относительно **HoldPoint > HandAnchor > View**. View добавляет Hold Distance; anchors заменяют расстояние. GripPoint меняет только позиционируемую точку. **Follow Speed** (`0…1`, default `0.5`) задаёт мягкое или быстрое физическое возвращение позиции и управляемого поворота. **Movement Lag XYZ** задаёт отдельное отставание от ходьбы. Preserve Pickup Pose сохраняет ориентацию взятия через Rotation Mode/HoldOffset. Rigidbody остаётся динамическим; препятствия могут остановить предмет. [Настройка удержания](Documentation~/GrabComfort.md).

## Свой grab-префаб

Полная таблица всех параметров Grab Action и общих правил/событий: [справка по параметрам](Documentation~/GrabActionParameters.ru.md).

Позицию не обязательно подбирать цифрами: в Grab Action нажмите **Edit Hold Pose in Scene View**, выберите Preview Actor либо используйте фиксированную камеру Scene View. Cyan wire-preview показывает предмет в руках; **W** перемещает, **E** вращает его стандартными handles. Реальный предмет остаётся на месте. **Done**, Undo/Redo и обычное сохранение prefab завершают настройку. В Prefab Mode подходит тот же фиксированный frame без ссылки на игрока из сцены. [Подробности визуальной настройки и масштаба](Documentation~/GrabComfort.md#визуальная-настройка-позы).

1. Создайте корневой GameObject предмета с моделью на нём или дочерним объектом. На этом же корне должны находиться **Rigidbody**, **Interaction Target** и **Grab Action**. Добавление Grab Action автоматически добавляет недостающие Rigidbody и Interaction Target. Добавьте подходящий Collider на корень или дочерние части этого Rigidbody; выключите **Is Trigger** для физических контактов. У Rigidbody оставьте **Is Kinematic** выключенным. Масса должна укладываться в Maximum Mass.
2. В Inspector компонента **Grab Action** поле **Pose Mode** находится в основном блоке, выше Local Position/Local Euler Angles, без открытия Advanced. Оставьте **Authored Pose**. **Local Position** сдвигает root в метрах, **Local Euler Angles** задаёт его поворот. При пустых anchors **Hold Distance** задаёт расстояние перед View; например `(0.2, -0.1, 0)` и Hold Distance `2` означают root справа/ниже на двух метрах вперёд.
3. **Grip Point** и **Hold Point** в Advanced можно оставить пустыми. Grip Point нужен только для привязки конкретной точки на самом предмете вместо root. Hold Point — необязательная внешняя цель; Actor.HandAnchor также необязателен. Оба anchor заменяют Hold Distance, поэтому для обычного удержания перед камерой очистите их. Отдельный display-prefab не требуется: сохраняйте этот же объект с моделью и физикой как обычный prefab.
4. Перетащите root из Hierarchy в Project. Разместите экземпляр перед настроенным игроком EasyMovement и нажмите Play: E берёт и отпускает, R+мышь временно доворачивает, G бросает. Два экземпляра с разными исходными поворотами должны приходить к одной локальной ориентации. Поза редактируется на prefab до Play; для постоянного результата сохраните изменения prefab, а не временный доворот осмотра.

MechanicalAction управляет линейным/угловым MovingPart или настроенным ConfigurableJoint. Value хранится в диапазоне 0..1; Axis и Minimum/Maximum задают движение, Speed/Resistance — отклик. Условия, цена, каналы, анимационный commit и stages включаются по необходимости. SO InteractionPreset копирует конфигурацию, не хранит runtime-состояние.

## Обводка выбранного предмета

На **Interaction Target** флаг **Focus Outline** (`HighlightWhenFocused`) включён по умолчанию. Добавьте один **Focus Highlighter** на Actor-наблюдатель и задайте общий **Outline Color/Width**; отдельный компонент обводки на каждом предмете не требуется. Рисование идёт только в камеру этого наблюдателя: по умолчанию берётся Camera непосредственно на Actor.View, для другого render camera назначьте **Camera Override** (в том числе при Cinemachine). Fallback к Main Camera отсутствует; без подходящей камеры обводка не рисуется, Inspector показывает причину.

Дочерние статические MeshRenderer собираются автоматически; исключения задаются в Advanced Interaction Target через Outline Renderers. Штатный URP shader использует обычный depth test и скрывается непрозрачными стенами; исходные материалы и renderer features не меняются. Удерживаемые Grab-предметы не подсвечиваются до последнего release. Два наблюдателя могут выбирать разные цели/стили для разных камер. [Настройка, диагностика, очистка и перенос прежних компонентов](Documentation~/FocusOutline.md).

**Movement Lag (XYZ)** работает по локальным осям источника движения: X вбок, Y вверх, Z вперёд. `(0,0,0)` убирает постоянный walking drift, `(1,1,1)` сохраняет его, `(1,1,0)` убирает только продольный. Новые компоненты и стандартный общий Playground начинают с `(0.5,0.5,0.5)`; явно сохранённые значения остаются прежними. Поворот камеры остаётся обычным физическим движением.

По умолчанию источник ходьбы — Actor.PlayerRoot для принадлежащего ему frame либо сам внешний HoldPoint. Movement Reference в Advanced позволяет выбрать источник нестандартного rig; после телепорта rig и тела вызовите `ResetMotionTracking()`. [Справочник параметров](Documentation~/GrabActionParameters.ru.md).

## Штатные интеграции

- EasyMovement: подход к точке и независимые input locks; исключение удерживаемого тела из ECM2 queries.
- Resources: стоимость/резервы, pickup, deposit и атомарная передача.
- Building: каноническая сессия BuildingActor, общая с Placer.
- Combat: ограничения здоровья и отмена от damage/death/restore.
- Feedbacks: контекст игрока, звук/VFX/камера/вибрация через публичный FeedbackContext.
- Localization: `EasyLocalization.Integrations.InteractionTranslations` назначается в Presenter.TextProvider; язык и cache принадлежат Localization.
- EasyInput и EasyUI предоставляют свои штатные адаптеры ввода, окон и меню. NavMeshInteractionMotor доступен для NPC.

Ни один из этих адаптеров не требует импорта Sample. [Подключение и владение](Documentation~/Integrations.md).

## Примеры и дополнения

**Tools > EasyGameFramework > Interaction > Import Interaction Gallery** импортирует только демонстрацию: двери, предмет, socket, анимацию, последовательности и sample HUD со списком/кругом. Для её пересоздания после импорта — **Create Interaction Gallery**. Это небольшая стендовая камера с New Input System; основной FPS-путь описан выше. Root manifest регистрирует Gallery и Door and Crate как samples единого пакета.

Netcode и XR — `Addons~/Netcode/EasyInteraction` и `Addons~/XR/EasyInteraction`. Они требуют своих SDK и отдельной проверки после подключения; transport-independent authority/contracts остаются в runtime.

## Проверка

Tests/PlayMode, Tests/Integrations, Tests/SharedInput и Tests/Editor входят в один testable package root EasyGameFramework. Gallery HUD/scene проверки находятся внутри Gallery sample; SDK-тесты — в дополнениях. [API и lifecycle](Documentation~/API.md), [проверочный стенд](Tools~/README.md). Исторические raw-отчёты в Documentation~ относятся к прежним отдельным пакетам; готовность нового staging ими не подтверждается. Unity validation выполняется после CUTOVER.
