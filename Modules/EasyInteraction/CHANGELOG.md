# Изменения EasyInteraction

## EasyGameFramework 0.1.0 — staging

- Production-интеграции перенесены из Samples в штатные Integrations; отдельные module package guards удалены.
- Один Localization provider от EasyLocalization; Netcode/XR вынесены в Addons~. Основные Tests не зависят от sample HUD.
- Настройка существующего FPS-игрока одной Editor-командой, единые меню, линейная ручка и Stabilized по умолчанию.
- Свежие GrabComfort/ECM2/PressPrimary исправления и regression fixtures сохранены. Unity validation выполняется после CUTOVER.

Ниже — история прежнего отдельного пакета, не версии модулей нового фреймворка.

## 0.3.1 — local candidate, 2026-11-13

- Единый Actor.PressPrimary(out action, toggle): повторный основной Interact отпускает свой active Grab независимо от hover и движения мыши. Explicit Press/Activate(action) сохраняют адресные действия меню/сокетов.
- Штатный Input и default Presenter используют primary-команду; matching Release хранит фактически выбранную action. Подсказка показывает Release удерживаемого предмета, а input context остаётся у reader.
- Добавлены native Keyboard/Mouse регрессии ввода, hold/toggle/rebind/blocked action и совместной работы с адаптером EasyInput.

- Grab исключает физические пары владельца по умолчанию; EasyMovement adapter также исключает held body из ECM2 sweeps/overlaps/pushing, сохраняя прежние фильтры и владение.
- Безопасное восстановление после drop/throw внутри владельца, compound/pooling/transfer/multiple owners; trigger-сенсоры не продлевают отсрочку.
- Stabilized, HoldOffset и регулируемая Inertia: ограниченное отставание целевой позы и затухание вращения без телепортации. Прежние enum values и Free сохранены.
- Понятный основной Inspector, приоритет HoldPoint/HandAnchor/View и руководство GrabComfort; реальные ECM2/Rigidbody regression tests и calibrated GC.

## 0.3.0 — 2026-11-12

- Добавлен IInteractionInputSource: Presenter выбирает активный адаптер, обновляет подпись/иконку/hold-toggle и принимает UI analog через SetValue. Явная выключенная ссылка не подменяется legacy reader; Actor.GetActive позволяет найти уже начатое действие.
- EasyMovement Motor/Lock используют независимые Character.AcquireInputBlock. Скриптовое движение сохраняется, отмена/disable/destroy освобождает только собственный запрос.
- BuildingInteractionContext заимствует каноническую сессию BuildingActor/System; CombatInteractionGuard учитывает StateRestored/Revived и возвращает только своё отключение Actor.
- InteractionFeedbacks принимает необязательный ContextProvider и сохраняет полный контекст, включая явный геймпад и explicit-none. Core не получает обязательных зависимостей от других Easy-пакетов.
- Исправлены две проверки GC и нагрузочный стенд: калиброванный main-thread GC.Alloc, явная недоступность измерителя и число событий отдельно от байтов. Исторические выводы zero GC от некалиброванного счётчика отозваны; raw-отчёты сохранены.

## 0.2.0

- SphereCast, Cursor, Nearest, Contact, Explicit, части и несколько точек объекта, приоритеты, ручной выбор целей/команд.
- Tickets, каналы, подтверждение, таймер, пауза, применение эффекта отдельно от завершения, события анимации, этапы.
- Условия, резервирование цены, совместное владение, позиции, FIFO, общая работа и сохранение состояния мира.
- Вращение/расстояние/бросок/осмотр, два участника, передача, сокеты и линейная/угловая механика.
- Пресеты, инспекторы, диагностика, меню создания, галерея с prefabs и UI списка/круга.
- Отдельные адаптеры Resources, Building, Combat, Feedbacks, EasyMovement, NavMesh, Localization, NGO и XRI.
- Переиспользование памяти в прогретом ядре; Focus использует кучу кандидатов и последовательную проверку при множестве перекрытий.
- Значения Raycast/Focus и базовое API 0.1 сохранены. Внешний анимационный сигнал использует ticket.Commit; Complete завершает уже выполняющееся действие. Добавлено событие Committed.

## 0.1.0

- Raycast/Focus, Door, Event, физический Grab, Input System, минимальный пример и нагрузочный стенд.
