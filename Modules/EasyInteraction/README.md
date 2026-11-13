# EasyInteraction — модуль EasyGameFramework

Выбор объектов, команды, физическое удержание, механизмы, условия, совместная работа и состояние мира. Модуль устанавливается вместе со всем EasyGameFramework; производственные интеграции уже доступны. ECM2, Cinemachine и New Input System входят в штатное окружение автора. Netcode и XR находятся в отдельных дополнениях.

## Первый запуск

1. Создайте или выберите FPS-игрока EasyMovement через общий Setup. Выберите его корень или камеру и выполните **Tools > EasyGameFramework > Interaction > Set Up Selected Player**. Команда связывает Actor, Presenter, Motor и EasyInput с существующим Character; повторный вызов не дублирует компоненты.
2. Создайте **GameObject > EasyGameFramework > Interaction > Grab Item**. Поставьте куб в 2–3 метрах перед камерой. Rigidbody динамический; Stabilized, owner collision exclusion, Hold Distance 2 и нулевой offset уже настроены.
3. Нажмите Play: **E** поднимает предмет, повторный **E** отпускает даже при движении мыши и потере фокуса. Предмет плавно следует за взглядом, перестаёт вращаться после остановки и сталкивается со стенами. **Q** отменяет, **G** бросает, **R + мышь** вращает, колесо меняет расстояние. EasyInput управляет переназначениями и блокирующими контекстами.
4. Для двери или механизма выберите соседние команды **Door**, **Linear Handle**, **Physics Lever**. Linear Handle использует Analog: E начинает управление, горизонтальное движение мыши меняет Value; отпускание E завершает его. Для собственного UI используйте Presenter.

Выбор задаётся в Actor: Raycast — точное наведение, Focus — конус и приоритет направления; Range и слои ограничивают поиск. Дополнительно доступны SphereCast, Cursor, Nearest, Contact и Explicit. **Tools > EasyGameFramework > Interaction > Validate Open Scenes** объясняет отсутствующие коллайдеры и дубликаты ID.

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
grab.HoldOffset = new Vector3(.15f, -.1f, 0);
grab.Inertia = .12f; // 0 убирает сглаживание target; физическая пружина остаётся.
grab.Throw(actor);
// Линейная ручка: начните action, затем передавайте направление -1..1.
actor.SetValue(handle, 1);
```

Primary-команда: `PressPrimary(out action, toggle)` возвращает выбранную action для matching Release/ticket tracking. `Press(action)` и `Presenter.Activate(action)` остаются явными командами. При изменениях UI/input context разрешения проверяет reader, а не core. [Маршрутизация ввода](Documentation~/GrabReleaseInput.md).

## Настройка удержания и механики

Stabilized — default новых Grab. Free оставляет свободное физическое вращение; KeepInitial/FollowView доступны для специальных случаев. Приоритет точки: **HoldPoint > Actor.HandAnchor > View**. Первые две точки заменяют Hold Distance; без них нулевой offset означает центр перед камерой на Hold Distance. GripPoint находится на предмете. Advanced содержит пружину, ограничения инерции и Break Distance. [Удержание, collision ownership, pooling и transfer](Documentation~/GrabComfort.md).

MechanicalAction управляет линейным/угловым MovingPart или настроенным ConfigurableJoint. Value хранится в диапазоне 0..1; Axis и Minimum/Maximum задают движение, Speed/Resistance — отклик. Условия, цена, каналы, анимационный commit и stages включаются по необходимости. SO InteractionPreset копирует конфигурацию, не хранит runtime-состояние.

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
