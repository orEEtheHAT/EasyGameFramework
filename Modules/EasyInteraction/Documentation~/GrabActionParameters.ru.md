# Grab Action: полный справочник параметров

Справочник текущей реализации EasyInteraction. Здесь разделены настройки самого Grab Action, общие настройки Interaction Action и API текущего удержания. Значения по умолчанию относятся к **новому компоненту**, а не перезаписывают значения, уже сохранённые в prefab или сцене.

## С чего начать

1. Создайте **GameObject > EasyGameFramework > Interaction > Grab Item** либо добавьте **Grab Action** на корень своего предмета. На том же корне нужны динамический **Rigidbody** и **Interaction Target** — недостающие компоненты добавляются автоматически. Добавьте физический Collider на root или его дочерние части; модель тоже может быть дочерней. Не добавляйте отдельный Rigidbody на каждую часть одного составного предмета.
2. В **Grab Action** оставьте **Pose Mode → Authored Pose**. Начните с Local Position `(0, 0, 0)`, Local Euler Angles `(0, 0, 0)`, Hold Distance `2`. Для обычного удержания перед взглядом Grip Point, Hold Point и Actor.HandAnchor не требуются. Отдельный display-prefab не нужен: сохраните этот же root с моделью и физикой в Project.
3. Для настройки мышью нажмите **Edit Hold Pose in Scene View**. Выберите Preview Actor либо используйте фиксированный снимок Scene View; W перемещает wire-preview, E вращает. Реальный предмет не перемещается. Сохраняются только Local Position/Local Euler Angles через обычные Undo и prefab workflow; **Done** заканчивает редактирование.
4. На игроке EasyMovement выполните **Tools > EasyGameFramework > Interaction > Set Up Selected Player**. В Play штатный ввод берёт/отпускает предмет основным Interact; R+мышь доворачивает удерживаемый предмет, колесо меняет расстояние, G бросает. Конкретные кнопки зависят от действующего профиля EasyInput и переназначений.

Для большинства предметов достаточно Pose Mode, Local Position/Euler, Hold Distance и при необходимости Held Scale Multiplier. Остальные параметры раскрывайте под конкретную задачу. [Практика удержания](GrabComfort.md), [маршрутизация ввода](GrabReleaseInput.md), [Focus и обводка](FocusOutline.md), [общая инструкция модуля](../README.md).

## Где находятся поля

Удерживаемый Grab скрывает штатную focus-обводку этого Target для всех Actors до последнего release. Это не отдельный параметр Grab: на Interaction Target есть общий флаг **Focus Outline** (`HighlightWhenFocused`, default true), а цвет/толщина и render camera принадлежат **Focus Highlighter на Actor**. Отдельный компонент outline на предмете не требуется. После отпускания обычный Focus снова определяет цель; прогретая геометрия Target и материал наблюдателя при grab/drop переиспользуются. [Актуальное подключение и API](FocusOutline.md).

Основной блок содержит Grab Range, Maximum Mass, Hold Distance, Pose Mode и поля позы, **Follow Speed**, **Movement Lag (XYZ)**, Held Scale Multiplier и Input. **Advanced / rules and coordination** содержит anchors, Movement Reference, правила разрыва/дистанции/броска и общие правила взаимодействия. **Feedback events** раскрывает события. Настроек пружины и камеры в Advanced нет.

Скрытое поле другого режима сохраняет своё значение, но не участвует в расчёте, пока этот режим не выбран. Например, Hold Offset не добавляется к Local Position в Authored Pose. Числовые Min/Range в таблицах — предусмотренные границы Inspector и соответствующих публичных свойств; это не универсальная проверка произвольного JSON или прямого изменения приватных полей. Для пары Minimum/Maximum Distance самостоятельно сохраняйте `Minimum ≤ Maximum`.

## Основные параметры и авторская поза

| Поле Inspector / serialized имя | Default, единицы и границы | Что делает и когда применяется |
| --- | --- | --- |
| **Grab Range** / `grabRange` | `3` м, минимум `0.1` | Максимальная дистанция от Actor.View до GripPosition при начале физического взятия. Работает дополнительно к Range и геометрическим проверкам Actor. Само по себе не ограничивает уже удерживаемый предмет; для продолжения есть общие правила и Break Distance. |
| **Maximum Mass** / `maximumMass` | `20`, минимум `0.01` | Предельная `Rigidbody.mass`, с которой можно начать взятие. Не меняет массу и не является силой пружины. Не превращает изменение массы во время удержания в автоматический drop. |
| **Hold Distance** / `holdDistance` | `2` м, минимум `0.1` | Начальное расстояние вперёд от View. Используется, только когда нет Hold Point и Actor.HandAnchor. При новом захвате текущая дистанция сбрасывается к этому значению. Initial Hold Distance может лежать вне Minimum/Maximum Distance: их clamp применяется при `AdjustDistance`, а не при самом захвате. API `AdjustDistance` изменяет текущую дистанцию, не переписывая конфигурацию Hold Distance. |
| **Pose Mode** / `poseMode` | `AuthoredPose = 1` | Authored Pose задаёт одну локальную позу независимо от исходного world rotation и использует стабилизированную физику. `PreservePickupPose = 0` включает прежние Hold Offset/Rotation Mode. Значения enum не переставляются; явно сериализованный режим сохраняется. Если поля режима ещё не было в данных, действует новый initializer Authored. |
| **Local Position** / `localPosition` | `(0, 0, 0)` м, без числового лимита | Видно и используется в **Authored Pose**. Позиция root Rigidbody либо назначенного Grip Point в локальных осях выбранного frame. Для View дополнительно прибавляется `(0, 0, текущая дистанция)`; при осмотре — Inspection Distance. Это не мировая позиция и не позиция дочерней модели. |
| **Local Euler Angles** / `localEulerAngles` | `(0, 0, 0)` градусов | Видно и используется в **Authored Pose**. Локальная ориентация **root Rigidbody** относительно выбранного frame. Считается кватернионом `frame.rotation * Quaternion.Euler(value)`. Поворот самого Grip Point её не заменяет. Ручной доворот текущего удержания добавляется после неё. |
| **Held Scale Multiplier** / `heldScaleMultiplier` | `1`, диапазон `0.1…1` | Виден в обоих режимах. Временный равномерный множитель захваченного `root.localScale`: `0.5` уменьшает root, модель, дочернюю геометрию Grip Point и коллайдеры вместе. Не является только визуальным масштабом; массу не меняет. Подробности восстановления ниже. |

## Frame, root и Grip Point

В runtime frame всегда выбирается в порядке **Grab.HoldPoint → Actor.HandAnchor → Actor.View**. Позиция frame и его quaternion используются, а **масштаб frame не умножает** Local Position/Hold Offset или Hold Distance. Явные anchors заменяют дистанцию перед View: при назначенном Hold Point/HandAnchor колесо и Inspection Distance не отодвигают цель от anchor.

Без Grip Point авторская позиция относится к **root Rigidbody**. Смещение centre of mass не меняет этот смысл. В Preserve без Grip Point позиционируется **world centre of mass**. При назначенном Grip Point целевой позиции следует эта точка на предмете; root смещается так, чтобы точка совпала с целью, а его ориентация остаётся авторской либо определяется Rotation Mode. Локальный поворот Grip Point игнорируется.

Масштаб самого предмета и его родителей — часть геометрии предмета. Preview учитывает полную матрицу родителя, дочернюю геометрию и Held Scale Multiplier; игнорирование scale **frame** не означает игнорирование масштаба **предмета**. Для предсказуемой динамической физики предпочтителен один Rigidbody root с положительным масштабом, а визуальные пропорции можно хранить в дочерней модели. Ограничения Unity Physics для сложной неравномерно масштабированной иерархии не исчезают от включения preview.

## Preserve Pickup Pose и Rotation Mode

| Поле | Default / границы | Применение |
| --- | --- | --- |
| **Hold Offset** / `holdOffset` | `(0, 0, 0)` м | Видно и используется только в **Preserve Pickup Pose**. Смещение точки удержания по локальным осям frame. Для View добавляется к дистанции вперёд, для anchors — к их позиции. В Authored полностью заменено Local Position. |
| **Rotation Mode** / `rotationMode` | `Stabilized = 3` | Видно и используется только в **Preserve Pickup Pose**. В Authored его сохранённое значение не управляет вращением. Числовые значения: Free `0`, KeepInitial `1`, FollowView `2`, Stabilized `3`. |

| Rotation Mode | Реальное поведение |
| --- | --- |
| **Free** | Угловая пружина обычно выключена: предмет может свободно вращаться от физики. Сила в точке хвата тоже может создавать вращение. Rotate, Inspection или несколько владельцев включают управление ориентацией; это не абсолютная блокировка любых вращательных воздействий. |
| **Keep Initial** | Сохраняет начальную **мировую** ориентацию root, снятую при первом захвате. Не следует обычному повороту View; ручной доворот добавляется поверх неё. |
| **Follow View** | Сохраняет начальный quaternion-offset относительно **Actor.View** и следует его повороту. Даже при позиционировании через Hold Point/HandAnchor вращательный frame этого режима — View. |
| **Stabilized** | Сохраняет начальную ориентацию относительно позиционного frame **Hold Point/HandAnchor/View** и следует его повороту. Сила прикладывается в центре массы, ориентация удерживается демпфированной пружиной; скорость возврата задаёт Follow Speed. |

Позиция общего хвата нескольких владельцев усредняется. При двух владельцах изменение оси между их целями добавляет поворот к исходной hand-axis базе и может переопределить обычное поведение Rotation Mode. Для Authored базой служит авторская ориентация первого владельца, без повторного применения ручного offset. Это один общий Rigidbody/Grip Point, а не отдельные IK-позы рук; при большем числе участников ориентационная ось по-прежнему берётся по первым двум.

## Два параметра поведения

| Поле / API | Значение | Результат |
| --- | --- | --- |
| Follow Speed / `FollowSpeed` (`followSpeed`) | `0…1`, default `0.5` | Одна скорость возврата позиции и управляемого поворота: 0 — мягко и медленно, 1 — быстро и плотно. Ноль не выключает удержание. |
| Movement Lag (XYZ) / `MovementLag` (`movementLag`) | По оси `0…1`, default `(0.5,0.5,0.5)` | Отставание при ходьбе: X боковое, Y вертикальное, Z продольное. 0 компенсирует дрейф от демпфирования выбранной оси при равномерном свободном движении, 1 сохраняет его; промежуточные значения дают промежуточный эффект. |

Параметры независимы: Movement Lag не меняет отклик на поворот камеры, Follow Speed меняет физическую пружину. Дополнительного target smoothing и каскада Inertia/Strength/Time/Camera Lag/Frequency/Damping нет. Удержание использует динамический Rigidbody и силы/моменты; препятствие может остановить предмет. Разгон, контакты и шаг симуляции остаются физическими, жёсткого прикрепления к виду нет.

### Источник ходьбы и сброс истории

`GetMovementReference(actor)` возвращает явный override, либо Actor.PlayerRoot для принадлежащего ему frame, либо сам внешний frame (HoldPoint/HandAnchor/View). Компенсируется только его поступательное перемещение. Локальные XYZ берутся из этого reference. Ссылка на удерживаемый объект/его потомка недопустима. Для нестандартной иерархии назначьте источник, который действительно переносит цель.

При смене источника/владельца и больших разрывах измерение начинается заново. После телепорта rig и предмета вызовите `ResetMotionTracking()`; он не меняет позу и скорость Rigidbody. После глубокого изменения hierarchy также сбросьте историю.

## Правила и anchors — Advanced

| Поле | Назначение |
| --- | --- |
| Grip Point (`gripPoint`, `GripPoint`) | Точка на самом Rigidbody, которая приходит к целевой позиции; её вращение не учитывается. |
| Hold Point (`holdPoint`, `HoldPoint`) | Внешний frame, имеющий приоритет над Actor.HandAnchor и View. Anchor заменяет Hold Distance и inspection distance. |
| Movement Reference (`movementReference`, `MovementReference`) | Необязательный источник поступательного движения и локальных XYZ для Movement Lag. |
| Ignore Owner Collisions (`ignoreOwnerCollisions`, `IgnoreOwnerCollisions`) | Исключение собственных коллайдеров владельца; по умолчанию включено. |
| Break Distance (`breakDistance`, `BreakDistance`) | Дистанция разрыва удержания, default 5 м, минимум 0.1 м. |
| Minimum / Maximum Distance (`minimumDistance`, `maximumDistance`) | Диапазон AdjustDistance при View fallback, default 0.5…4 м, каждое поле от 0.1 м. |
| Throw Speed (`throwSpeed`, `ThrowSpeed`) | Добавочная скорость броска, default 6 м/с, минимум 0. |
| Inspection Distance (`inspectionDistance`) | Расстояние осмотра при View fallback, default 0.65 м, минимум 0.1 м. |

## Временный размер и восстановление

При первом физическом захвате сохраняются исходные root.localScale, свойства centre of mass/inertia tensor и прежние gravity/interpolation/collision-detection значения. Held Scale Multiplier применяется к **снимку исходного localScale**, а не к уже уменьшенному размеру. Передача и добавление владельца продолжают ту же сессию, поэтому множитель не накапливается.

Вместе с root меняются визуальная геометрия, коллайдеры и положение Grip Point. Масса Rigidbody остаётся прежней. Для автоматических COM/inertia tensor вызывается пересчёт Unity; для вручную настроенных значений компонент использует сохранённый COM с множителем и inertia tensor с квадратом множителя, затем восстанавливает исходные значения. Это изменение настоящей геометрии физического тела, не отдельная уменьшенная display-модель.

Исходный масштаб возвращается после последнего drop/throw/cancel/complete, при отключении или удалении Grab Action и при pooling через отключение GameObject. При полном уничтожении предмета живого тела для продолжения уже нет. Смерть игрока освобождает предмет через обычный путь отмены/отключения, который должна вызывать интеграция игрока; Grab не заменяет систему здоровья. Не меняйте параллельно root.localScale вручную во время удержания: при освобождении восстанавливается снятый в начале снимок.

В runtime публичное свойство `HeldScaleMultiplier` применяет новое значение немедленно во время захвата, всегда от исходного scale. Прямое редактирование сериализованного поля в Inspector Play Mode не равнозначно вызову setter: OnValidate проверяет границы, но не меняет физику сцены; такой настройкой удобнее пользоваться до захвата. Getter `UnheldLocalScale` сам ничего не восстанавливает: пока captured он возвращает снимок, иначе — текущий localScale.

## Input — жест запуска

**Input** / `inputGesture` находится в основном блоке и задаёт общий `InteractionInputGesture`. Эффективный default Grab — **Press**. Это выбор поведения action, а не назначение клавиши; бинды, чувствительность, устройства и блокирующие контексты принадлежат EasyInput/источнику ввода.

| Input / значение enum | Поведение при штатном dispatch |
| --- | --- |
| **Press = 0** | Нажатие начинает action; отпускание клавиши не прекращает Grab. Повторный основной Interact через `PressPrimary` в первую очередь отменяет собственный Grab, даже при потере hover. |
| **Hold To Confirm = 1** | Сначала нужно удерживать подтверждение. Отпускание в фазе Confirming отменяет запуск; успешное подтверждение начинает обычное исполнение. После подтверждения одно лишь отпускание не превращает этот жест в Hold While Active. |
| **Hold While Active = 2** | Запуск по нажатию, отмена активной action по matching Release. Accessibility Toggle Instead Of Hold на источнике ввода может заменить удержание повторным нажатием. |
| **Toggle = 3** | Нажатие начинает, следующее нажатие активной action отменяет; Release не прекращает её. |
| **Analog = 4** | Жест для непрерывной action с `Actor.SetValue`; matching Release отменяет. Сам Grab не переопределяет обработчик analog Value, поэтому этот выбор не создаёт новую механику вращения/дистанции: используйте Rotate/AdjustDistance. |

| Поле | Default / границы | Когда видно и что значит |
| --- | --- | --- |
| **Confirmation Seconds** / `confirmationSeconds` | `0.5` с, минимум `0.05` | В основном блоке только при Hold To Confirm. Время подтверждения перед стартом; не Duration и не время удержания тела. |

## Исполнение — Advanced

| Поле | Default / границы | Применение |
| --- | --- | --- |
| **Execution** / `execution` | `Automatic = 0` | После подтверждения и подхода физическое взятие выполняется сразу. `Timed = 1` откладывает commit до Duration; `AnimationEvent = 2` ждёт `Actor.Commit(ticket)`/Animation Bridge. Grab после успешного commit остаётся продолжительной action. |
| **Duration** / `duration` | `2` с, минимум `0.01` | Показывается только при Execution=Timed. Это задержка **до физического захвата**, а не время, через которое предмет автоматически упадёт. После commit таймер не завершает удержание. |
| **Timeout** / `timeout` | `0` с, минимум `0` | Ноль выключает таймаут. Положительное значение отменяет слишком долгую фазу подхода/запущенную сессию по её elapsed; подтверждение имеет отдельный таймер и не увеличивает этот elapsed. После успешного подхода StartSession сбрасывает elapsed. Для AnimationEvent помогает обработать пропавший commit; для обычного удержания с ненулевым Timeout приведёт к drop по времени. |

## Имена, UI, каналы и участники — Advanced

| Поле | Эффективный default | Реальный смысл |
| --- | --- | --- |
| **Action Label** / `actionLabel` | Базовое сохранённое значение `Interact` | Общая подпись Action, но **GrabAction.GetLabel** возвращает собственные `Grab`/`Release` в зависимости от удержания этим Actor. Изменение Action Label не переименовывает штатную подсказку Grab; для перевода используйте Label Key и text provider. |
| **Action Id** / `actionId` | `GrabAction`, когда поле пусто | Идентификатор action внутри Target; Reset/getter используют имя типа при пустом значении. Должен быть уникальным среди действий одного Target для адресных вызовов, costs и сохранений. Это не Persistent ID самого предмета. |
| **Label Key** / `labelKey` | Пусто | Ключ для `IInteractionTextProvider` у Presenter. Без провайдера используется GetLabel fallback; сам Grab не хранит язык или таблицу локализации. |
| **Icon** / `icon` | Не назначена | Метаданные Sprite для UI, который их читает. Не заменяет модель и не задаёт обводку. Стандартный Prompt отдельно содержит BindingIcon от источника ввода; Icon action и иконка клавиши — разные данные. |
| **Hide When Unavailable** / `hideWhenUnavailable` | `false` | Используется `Actor.CycleAction`, чтобы пропускать недоступное действие при переключении. Не удаляет action из Target.Actions, не гарантирует её скрытие произвольным UI и не отключает Target, Collider или Renderer. |
| **Channel** / `channel` с `useDefaultChannel` | Для Grab **`carry`** | На одном Actor две активные action с одинаковым непустым каналом конфликтуют. Пустая строка снимает именно это ограничение; остальные проверки остаются. Сравнение строк точное. Технический backing default `hands` игнорируется, пока включён useDefaultChannel; Inspector показывает эффективный `carry`. |
| **Use Scope** / `useScope` | `Action = 0` | Правило конфликта claims на одном Target. Action позволяет разные action, если прочие правила допускают. `Target = 1` блокирует другие action этого Target. `Group = 2` конфликтует с другими Group-action того же Use Group. Каналы Actor и scope Target — разные ограничения. |
| **Use Group** / `useGroup` | Пусто | Видно только при Scope=Group. Имя группы claims; совпавшие пустые группы тоже совпадают. Не является глобальной группой объектов или именем input context. |
| **Max Participants** / `maxParticipants` с `useDefaultParticipants` | `1`, минимум `1` | Максимум владельцев этой action. Значение `2` включает существующий общий физический хват двух Actor; IK/анимационные руки не создаются. Занятые participant InteractionPoint и очередь Target могут дополнительно ограничить допуск. Transfer штатно передаёт одному получателю, не требуя постоянно выставлять Max Participants=2. |

## Условия, стоимость и продолжение — Advanced

| Поле | Default | Что проверяется |
| --- | --- | --- |
| **Condition Match** / `conditionMatch` | `All = 0` | All требует успеха всех применимых условий, `Any = 1` — хотя бы одного. Пустой набор применимых условий разрешает действие даже при Any. Invalid Configuration от условия считается ошибкой конфигурации. |
| **Conditions** / `conditions` | Пустой массив | Пустой список использует standalone InteractionCondition-компоненты на том же GameObject, что и Target. Непустой — только явно указанные ссылки; это замена, не добавка к автоматическому набору. Учитываются enabled и Phase Start/Continue/Both каждого условия. Conditions — не стоимости. |
| **Cost Timing** / `costTiming` | `OnSuccess = 0` | Стоимости заранее проверяются и резервируются. OnSuccess оплачивает их при нормальном **Complete**, а не при событии On Committed. `OnStart = 1` оплачивает при StartSession после подтверждения/подхода, до выполнения физического захвата. Уже оплаченная стоимость автоматически не возвращается при последующей отмене. |
| **Require Range While Running** / `requireRangeWhileRunning` | `false` | Если включено, Actor продолжает геометрически проверять свою Range во время сессии. Это не повтор Grab Range: собственный continuation Grab разрешён после capture, а общая геометрия Actor проверяется отдельно. |
| **Require Visibility While Running** / `requireVisibilityWhileRunning` | `false` | Если включено, Actor отменяет продолжение при потере требуемой видимости. Обычное начало также может требовать видимость по настройке самого Actor. False не отключает столкновения с миром. |

У Grab Action **нет сериализованного массива Costs**. Стоимости — отдельные компоненты, наследующие `InteractionCost`, зарегистрированные на InteractionTarget. Пустой `InteractionCost.ActionId` применяет стоимость ко всем action Target, непустой — к совпадающему Action Id. Их адаптер владеет Reserve/Commit/Refund и проверкой действительности резерва.

Обычный drop через повторный Interact и `Throw` используют **Cancel**. При default OnSuccess их ещё не оплаченные резервы возвращаются; событие On Committed при взятии само по себе не означает оплату. Если требуется списание за факт взятия, выбирайте OnStart и учитывайте, что оно происходит раньше успешного OnExecute. Для списания за конкретную игровую операцию используйте её отдельную action/cost, а не предполагайте, что любое отпускание Grab является Completed.

## Подход к точке — Advanced

| Поле | Default / границы | Назначение |
| --- | --- | --- |
| **Approach Point** / `approachPoint` | Не назначен | Необязательная точка подхода Actor перед исполнением. Это позиция **игрока**, не Hold Point предмета. При необходимости подхода нужен активный InteractionMotor либо игрок уже должен быть у точки. Свободный participant InteractionPoint на Target может предоставить фактическую точку вместо неё. |
| **Approach Distance** / `approachDistance` | `0.2` м, минимум `0.01` | Допуск расстояния от Actor.PlayerRoot до точки подхода. Не радиус Grip Point и не Grab Range. |
| **Approach Angle** / `approachAngle` | `20°`, `0…180°` | Допуск quaternion-угла между Actor.PlayerRoot.rotation и rotation точки подхода. Нужны одновременно расстояние и угол. |

## Feedback events

Все пять UnityEvent по умолчанию пусты. Это события action, а не автоматическое добавление звука, VFX или вибрации. Для такого отклика подключайте штатные интеграции/Feedbacks через события. Focus-обводка использует флаг Target и отдельный Highlighter наблюдателя, а не эти UnityEvent; [инструкция Focus/Outline](FocusOutline.md).

| Поле / событие | Когда вызывается |
| --- | --- |
| **On Started** / `onStarted` | После успешного подтверждения/подхода, когда action подготовлена к исполнению. Может предшествовать физическому взятию на Duration или до AnimationEvent commit. |
| **On Committed** / `onCommitted` | После успешного вызова OnExecute: для Grab тело захвачено и перешло в режим удержания. Не означает Completed или оплату OnSuccess. |
| **On Progress** / `onProgress` | Передаёт нормализованный progress из сессии. Меняется при подтверждении и Timed-ожидании; notifications подавляются, если изменение меньше `0.001`. Обычный Automatic Grab не считает расстояние до целевой позы как progress и не наращивает progress всё время удержания. |
| **On Completed** / `onCompleted` | Нормальное успешное завершение через Complete после исполнения. Владение и ресурсные связи освобождаются перед уведомлением. Обычный primary drop и throw идут по Cancel, поэтому ждать здесь любое отпускание нельзя. |
| **On Cancelled** / `onCancelled` | Отмена: drop, throw, отключение, превышение Break Distance, timeout/потеря требований и другие причины. Перед уведомлением удаляется владелец и выполняется cleanup; масштаб восстанавливается, когда больше нет владельцев. |

Для обычного успешного Automatic запуска порядок состояний **Started → OnExecute → Committed**. Grab возвращает sustained=true и остаётся активным до Cancel/Complete. При Timed/AnimationEvent между Started и OnExecute есть ожидание. Подтверждение может выдавать Progress ещё **до Started**. Проверка/резервирование могут отказать до начала, поэтому не каждый неуспешный запрос даёт Started. События уведомляются через очередь после согласованного изменения состояния; слушатель не должен считать OnStarted гарантией того, что предмет ещё не захвачен к моменту callback.

## Технические переключатели общих defaults

Эти три сериализованных поля скрыты обычным Inspector. Он показывает эффективные значения **Channel**, **Input**, **Max Participants**, а при ручной смене помечает соответствующий default как переопределённый.

| Скрытое поле | Начальное значение | Значение для Grab |
| --- | --- | --- |
| `useDefaultChannel` | `true` | Channel берётся из DefaultChannel Grab=`carry`, а не из backing string `channel="hands"`. Запись публичного Channel/поля Inspector выключает этот флаг. |
| `useDefaultGesture` | `true` | InputGesture берётся из DefaultInputGesture=`Press`. Явный выбор Input создаёт override и выключает флаг. |
| `useDefaultParticipants` | `true` | MaxParticipants берётся из DefaultMaxParticipants=`1`. Явное изменение создаёт override и выключает флаг. |

Ссылка **Copy settings from preset** — состояние самого Editor, а не дополнительное сериализованное поле Grab. **Apply action preset** копирует только Input, Execution, Duration, Confirmation Seconds и два Require…WhileRunning. Она не меняет позу, масштаб, anchors, пружину, канал, стоимости или текущий progress. Сам ScriptableObject preset не владеет живой сессией.

Стандартный toggle Enabled компонента — обычное состояние MonoBehaviour: отключение Grab отменяет его владельцев. Script/reference компонента не является игровой настройкой. Скрытые runtime-кеши body, initialRotation, smoothedPosition и снимки scale/COM не должны редактироваться как параметры prefab.

## API текущего удержания — это не новые поля Inspector

| API | Назначение и границы |
| --- | --- |
| `Body` | Rigidbody этого root; getter может закешировать ссылку. Не создаёт другое display-тело. |
| `IsHeld`, `IsInspecting` | Текущее состояние, не исходная конфигурация. IsHeld требует captured и владельца. |
| `GripPosition` | Текущая **мировая** позиция назначенного Grip Point; иначе root для Authored или world COM для Preserve. Это фактическая точка на теле, не желаемая цель. |
| `UnheldLocalScale` | Снимок исходного localScale во время capture, иначе текущий localScale. Getter не включает/выключает объект и не запускает восстановление. |
| `Owner`, `OwnerCount`, `GetOwner(index)`, `Owns(actor)` | Владельцы живой action; Owner — первый участник, а не обязательно локальный игрок. |
| `Target`, `ActionId`, `GetLabel(actor)` | Target и метаданные action. GetLabel Grab даёт Grab либо Release для его владельца. |
| `GetAvailability(actor)` | Проверка Body/dynamic/collisions/mass/Grab Range самим Grab. Для полной проверки вместе с claims, условиями, costs и геометрией используйте `actor.Check` либо `CheckDirect`. |
| `GetContinuationAvailability(actor)` | После capture собственная проверка Grab разрешает продолжение; общие требования Actor/conditions и физический FixedUpdate всё ещё могут отменить сессию. Это не обход Break Distance или Require…WhileRunning. |
| `GetMovementReference(actor)` | Явный MovementReference, иначе Actor.PlayerRoot для его собственного/дочернего hold frame, иначе внешний hold frame. При null Actor без override возвращает null. Выбор источника/осей, не изменение позиции предмета. |
| `ResetMotionTracking()` | Сбрасывает историю измерения поступательного движения после teleport/rig discontinuity; следующий physics sample начинает измерение заново. Не пишет Rigidbody pose/velocity и не выключает Break Distance. |
| `AdjustDistance(delta)` | Только при удержании: изменяет текущую дистанцию в пределах Minimum/Maximum, не Hold Distance. При anchor цель остаётся у anchor; во время осмотра обычная дистанция сохранится для выхода из осмотра. |
| `Rotate(Vector2 degrees)` | Только при удержании: добавляет временный ручной quaternion-offset. Не переписывает Local Euler Angles. Сбрасывается при новом захвате после полного освобождения. |
| `SetInspection(bool)` | Включает/выключает текущий осмотр; без отдельного автоматического таймера или изменения prefab. |
| `Throw(actor)` | Требует единственного владельца и Body. Отменяет его сессию, восстанавливает принадлежащие Grab настройки и задаёт ограниченную результирующую скорость броска. |
| `TryTransferTo(receiver)` | Только от одного владельца другому допустимому Actor. Продолжает одну сессию физического capture без повторного scale multiplier. Manual rotation, inspection и текущая дистанция сохраняются; frame меняется на frame получателя, если общий Hold Point его не переопределяет. |
| `actor.PressPrimary`, `actor.Cancel(grab)`, `actor.Complete(grab)` | Основной Interact приоритетно освобождает собственный Grab; Cancel отменяет, Complete нормально завершает уже исполненную action. У Grab нет отдельного публичного serialized Release-флага. |
| `CanPause` | У Grab всегда `false`: индивидуальная пауза ticket не поддерживается. Это не запрет общей Time.timeScale-паузы физики. |
| `GrabPoseUtility`, `GrabPoseAuthoring` | Утилита перевода root/local pose и Editor-сессия handles. Preview Actor/View/Frame не сериализуются в Grab и не создают runtime hold-helper объектов. |

Настраивать prefab надёжнее до захвата. LocalPosition/Euler и параметры пружины читаются при расчёте, но runtime-сессия также имеет начальные quaternion/hand-axis/scale снимки: произвольная смена режимов и иерархии посреди удержания не заменяет новый согласованный захват. Runtime setters и Inspector-сериализация — разные пути; особенно это важно для немедленно применяемого HeldScaleMultiplier.
