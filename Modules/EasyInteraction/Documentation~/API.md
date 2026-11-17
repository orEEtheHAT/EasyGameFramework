# API и правила использования

## Состояние и выбор

InteractionActor хранит выбор и запуски одного участника. InteractionTarget объединяет коллайдеры, действия, условия, цену и места. Action хранит поведение объекта; длительные операции различаются через InteractionTicket. Пресеты ScriptableObject содержат только настройки.

`IInteractionInputSource` предоставляет BindingLabel, BindingIcon, ToggleInsteadOfHold и PresentationChanged. Реализуйте его на MonoBehaviour рядом с Actor либо задайте Presenter.InputSource явно. ActiveInputSource возвращает разрешённый активный источник; legacy InteractionInput тоже реализует интерфейс. Для UI предусмотрены Presenter.Activate/Release/SetValue; `actor.GetActive(InteractionInputGesture)` находит активную операцию без привязки к reader, который её начал. Прежний constructor InteractionPrompt сохранён; новый overload принимает Sprite последним аргументом.

Компоненты работают в главном потоке Unity. После динамического изменения коллайдеров/частей вызывайте `target.Refresh()`. После прямого перемещения Transform перед физическим запросом в том же кадре вызывайте `Physics.SyncTransforms()`.

Raycast выбирает попадание, SphereCast расширяет область, Focus ранжирует точки в конусе с приоритетом и порогом переключения. Nearest использует расстояние, Contact — ближайшую поверхность в Contact Radius, Cursor — луч Camera через указатель. Для AI/XR: `SelectTarget(target)` и Explicit.

Объект может иметь Focus Point, дополнительные точки и дочерние Interaction Point. Action IDs части ограничивают команды. Participant Position не участвует в наведении. `CycleTarget()` перебирает цели; `PinTarget()` фиксирует выбор для меню; `ReleaseTargetLock()` возвращает поиск. Потеря фокуса сама по себе не отменяет физическое удержание.

Target Layers/Obstacle Layers управляют запросами; Include Triggers по умолчанию выключен. Коллайдеры Player Root игнорируются. Расстояние и видимость должны выполняться на одной точке. Это проверка лучами, а не измерение видимой площади модели.

## Жизненный цикл

Основную кнопку Interact направляйте в `actor.PressPrimary(out InteractionAction action, bool toggleInsteadOfHold = false)`; есть короткий overload `PressPrimary(bool toggleInsteadOfHold = false)`. Свой active Grab имеет приоритет над любым hover, включая пустой. Одно нажатие направляется только ему; после отпускания или отказа не запускается новая world action. Без Grab сохраняются обычный выбор, подтверждение и toggle. Reader хранит возвращённые action/ticket для matching Release, а не читает SelectedAction повторно. `false` не создаёт input ownership. Если локальной action нет, сохранён `CommandAdapter.Press(null, toggle)` для асинхронного bridge; тогда out action может быть null.

`Press()` без явной action и `Presenter.Activate()` используют primary-семантику. `Presenter.ActivatePrimary(out action)` возвращает адрес matching Release для UI. Его основная Prompt показывает Release удерживаемого Grab. Явные `Press(action)` и `Presenter.Activate(action)` сохраняют адресные команды меню/сокетов/use и позволяют взаимодействовать с другими объектами при удержании. `Interact()` сохраняет прежний немедленный сценарий без Grab, но при своём активном Grab отпускает его первым. Input reader продолжает проверять enabled action, владельца устройства и modal/context restrictions; core-команда не опрашивает устройства и не обходит эти проверки. [Подробности исправления ввода](GrabReleaseInput.md).

1. `TryExecute(action)` заново проверяет наведение. `TryExecuteDirect` / `TryBegin` не требуют наведения, но проверяют расстояние, видимость, правила, занятость и цену.
2. `Press` / `Release` обрабатывают ввод. `TryBeginInput` сохраняет HoldToConfirm при запросе сети/XR. Обычный TryBegin предназначен для уже принятого решения кода/AI и не ждёт удержания кнопки.
3. После подтверждения при необходимости выполняется подход через InteractionMotor. Затем OnBegin, Started и выбранная фаза выполнения.
4. Automatic вызывает OnExecute сразу, Timed ждёт Duration, AnimationEvent ждёт `ticket.Commit()`. Для внешнего сигнала полезен Timeout.
5. OnExecute возвращает false для мгновенного результата, true для продолжающегося действия. После применения эффекта приходит Committed. Продолжающееся действие получает OnTick до Complete/Cancel.
6. `ticket.Complete()` завершает уже выполняющееся действие и не пропускает подтверждение/подход/таймер. Commit применим к ожидающему AnimationEvent. При паузе эти сигналы откладываются до Resume.

Отключение/уничтожение отменяет операции, освобождает места и неоплаченные резервы. Пользовательский Action освобождает свои ресурсы в OnCancel и OnComplete. При переопределении OnEnable/OnDisable вызывайте base.

Перед асинхронным вызовом сохраните ticket: устаревший ticket не влияет на новый запуск. Защищённый Complete() удобен внутри синхронного OnTick; для внешнего callback используйте захваченный ticket. Уведомления упорядочены, исключение одного подписчика не останавливает остальных. Самоотмена внутри OnExecute не публикует Committed.

InteractionAnimationBridge ставится на GameObject с Animator и получает Actor. В AnimationClip добавьте событие `CommitInteraction` в кадр контакта. Переход отмены Animator должен действительно прерывать старый клип: AnimationEvent без аргумента не несёт ID запуска. В собственной анимационной системе передавайте ticket напрямую. После Committed захват может продолжаться.

SequenceAction задаёт этапы с временем и/или ожиданием сигнала. Для внешнего callback используйте `SignalStage(ticket, expectedStage)`. Пауза сохраняет этап; отмена не выполняет следующие эффекты. Sequence эксклюзивна; общий процесс задаётся CooperativeAction.

## Условия и цена

Check — предложение для UI, запуск проверяет состояние повторно. Для логики/локализации используйте Code/TextKey; Reason — fallback, Diagnostic — настройка. Не сравнивайте пользовательские строки в коде.

Пустой список Conditions использует standalone-условия Target, явный список задаёт требования конкретного Action. All/Any и ConditionGroup комбинируют требования. Phase определяет Start/Continue/Both. Require Range/Visibility While Running включает дополнительные прерывания.

InteractionCost: Check → Reserve → IsValid → Commit/Refund; Advance оплачивает части прогресса. Резерв хранится по ticket. Commit обязан быть надёжным после успешного Reserve. При уничтожении источника пользовательский адаптер отменяет операции и освобождает резервы. OnSuccess оплачивает при успешном завершении; OnStart — после подтверждения и подхода. Отмена не возвращает уже оплаченные доли.

## Владение

По умолчанию Action эксклюзивен. Max Participants разрешает совместное использование для подходящего поведения. Use Scope блокирует действие, Target или группу с одинаковым Use Group. Одинаковый непустой Channel запрещает параллельные операции одного Actor. Grab использует carry, Inspect — view; пустой канал допускает параллельную работу.

Participant Position резервирует ближайшее свободное место; Action IDs ограничивают применимость. Approach Point задаёт обязательную точку. Пока игрок не на месте, нужен Motor. Для действий, управляющих телом, используйте общий канал body и подходящие допуски Approach Distance/Angle.

`actor.Enqueue(action)` ставит участника в FIFO-очередь. У Actor одна очередь; ClearQueue/Cancel её отменяют. При освобождении условия проверяются заново, невалидный участник пропускается. Очередь не гарантирует будущий успех.

CooperativeAction хранит WorkProgress у объекта. Пауза участника исключает его из работающих; общий эффект происходит один раз. Minimum Participants, ускорение от числа участников и сброс при уходе настраиваются в Inspector. `ResetWork()` начинает новую работу. Цена резервируется отдельно для каждого участника; общую смету задания храните в системе заданий.

## Предметы и механика

Grab требует динамический Rigidbody со столкновениями и допустимой массой. Ограниченная пружина работает в FixedUpdate. Grip Point находится на предмете, Hold Point/Hand Anchor — у участника. Отпускание возвращает изменённые настройки Rigidbody и сохраняет скорость. Пауза Grab не поддерживается.

В `PreservePickupPose` режим поворота и HoldOffset сохраняют позу взятия; `AuthoredPose` задаёт LocalPosition/LocalEulerAngles. Нулевой offset с View fallback означает точку на HoldDistance; HoldPoint/HandAnchor заменяют расстояние. `IgnoreOwnerCollisions` по умолчанию исключает твёрдые пары владельца, а штатный EasyMovement Motor дополнительно фильтрует ECM2 queries. Восстановление внутри игрока откладывается до расхождения. [Приоритеты и lifecycle](GrabComfort.md).

`AdjustDistance`, `Rotate`, `SetInspection`, `Throw`, `TryTransferTo` доступны из кода. Для двух рук: Max Participants = 2 и разные Hand Anchor. Передача проверяет получателя без промежуточного падения.

```csharp
grab.FollowSpeed = .5f;                    // 0 мягко/медленно, 1 плотно/быстро.
grab.MovementLag = new Vector3(1, 1, 0);   // Убрать продольный walking drift.
Transform reference = grab.GetMovementReference(actor);
// После согласованного телепорта rig и удерживаемого тела:
grab.ResetMotionTracking();               // Не перемещает тело и не меняет скорость.
```

`FollowSpeed` (0…1, default0.5) — единственная настройка физического возврата позиции и включённого поворота. Ноль задаёт мягкое удержание, не отключает его. Дополнительного фильтра цели нет. Внутренний демпфированный solver и пределы коррекции не являются публичной конфигурацией.

`MovementLag` (XYZ0…1, default `(0.5,0.5,0.5)`) компенсирует выбранную постоянную поступательную скорость в демпфировании:0 убирает walking drift оси,1 оставляет его. Reference: явный override → Actor.PlayerRoot для своего hold frame → сам внешний frame. Неподвижный внешний HoldPoint не получает скорость несвязанного Actor. Поворот камеры остаётся обычной физической ошибкой пружины; автоматической camera velocity/acceleration поддержки нет. Контакты, разгон и fixed step могут задерживать тело. [Параметры](GrabActionParameters.ru.md#два-параметра-поведения).

Socketable задаёт категорию, SocketAction — совместимость и Anchor. Вставка отпускает предмет у единственного владельца, выравнивает и фиксирует Rigidbody. Извлечение восстанавливает настройки. Предмет не становится дочерним сокета и переживает его уничтожение. Отложенная вставка повторно проверяет исходный предмет.

MechanicalAction хранит Value в 0..1. Axis/Minimum/Maximum отображают его в перемещение/поворот; Resistance замедляет изменение, Detents фиксирует значение при отпускании. ConfigurableJoint должен иметь настроенные оси и привод. `mechanical.SetValue(0..1)` задаёт состояние; `actor.SetValue(mechanical, -1..1)` задаёт пользовательское направление изменения.

## Авторская поза удержания

```csharp
grab.PoseMode = GrabPoseMode.AuthoredPose; // default нового компонента (1)
grab.LocalPosition = new Vector3(.2f, -.1f, 0);
grab.LocalEulerAngles = new Vector3(0, 90, 0);
```

Новый компонент начинает с Authored Pose, нулевых LocalPosition/LocalEulerAngles, HoldDistance 2, FollowSpeed 0.5 и IgnoreOwnerCollisions=true. Local Position задаёт позицию root (или назначенного GripPoint), Local Euler Angles — поворот root в frame HoldPoint > HandAnchor > View без scale frame. View добавляет текущую дистанцию по Z. Authored Pose использует Stabilized physics; HoldOffset/RotationMode остаются настройками Preserve Pickup Pose. Числовые значения enum сохранены: PreservePickupPose=0, AuthoredPose=1; явно сериализованный выбор режима не перезаписывается. Rotate добавляется поверх позы и сбрасывается при новом захвате. [Grip semantics, multi-owner и миграция HoldPoint](GrabComfort.md#одинаковая-поза-при-каждом-взятии).

## Настройка позы, масштаба и отклика

```csharp
grab.HeldScaleMultiplier = .65f; // default1; исходный localScale вернётся при последнем release
grab.FollowSpeed = .5f; // единственный control физического возврата
var pose = GrabPoseUtility.GetRootPose(grab, new Pose(view.position, view.rotation), usesViewDistance: true);
// GetLocalPose переводит желаемый мировой root Pose обратно в LocalPosition/local rotation.
```

`UnheldLocalScale` — исходный snapshot в активном удержании, иначе текущий localScale. Utility учитывает root/GripPoint, HeldScaleMultiplier и полный parent matrix; frame scale игнорируется. Editor-only `EasyInteraction.Editor.GrabPoseAuthoring.Begin/End`, `SetPreviewFrame`, `SetSceneViewFrame`, `TryGetPreviewPose` и `ApplyRootPose` доступны для инструментов Editor; Inspector уже вызывает их через кнопку. [First run](GrabComfort.md#визуальная-настройка-позы).

Новые Actor используют `InteractionSelectionMode.Focus` (enum1); `Raycast` остаётся enum0 и выбирается явно. Явно сериализованный режим сохраняется, Setup его не переписывает.

Обводка: `InteractionTarget.HighlightWhenFocused` включает её для предмета; `OutlineRenderers` и `RefreshOutlineRenderers()` задают геометрию. На Actor используется один `InteractionFocusHighlighter` с `OutlineColor`, `Width`, `CameraOverride`, `HighlightedTarget` и `RefreshHighlight()`. Без override берётся Camera непосредственно на Actor.View; глобального fallback нет. [URP, диагностика и ограничения](FocusOutline.md).

## Сохранение

```csharp
InteractionTargetState state = target.CaptureState();
string json = UnityEngine.JsonUtility.ToJson(state);
var loaded = UnityEngine.JsonUtility.FromJson<InteractionTargetState>(json);
bool restored = target.TryRestoreState(loaded, out var error);
```

Нужны уникальный Persistent ID и стабильные Action ID. Find при дубликатах возвращает null. Дверь, механика, сокет и общая работа реализуют IInteractionState. Активные tickets/наведение/физическое удержание не сериализуются. По умолчанию занятые цели не сохраняются и не восстанавливаются.

Сначала создайте все объекты, затем восстанавливайте связи сокетов. Валидация всех записей Target предшествует их применению. Пользовательский RestoreState обязан принимать всё, что разрешил ValidateState: произвольные побочные эффекты стороннего кода не откатываются. EasySave оркестрирует слоты, форматы и хранилища через Capture/Restore модулей; игровое состояние принадлежит соответствующей механике. Активные interaction tickets следует отменить перед общим восстановлением.

## Производительность

Поиск, сессии и уведомления ядра переиспользуют память. Рост буферов, создание компонентов, подписки, сохранения и сетевой JSON могут выделять память. Focus не обрезает результаты: при переполнении буфер растёт. Тысячи закрытых объектов требуют многих лучей; ограничивайте Range и Target Layers. Измерения: Validation.md.
