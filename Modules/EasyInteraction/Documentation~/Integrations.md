# Штатные интеграции EasyInteraction

EasyInteraction — модуль единого EasyGameFramework. Адаптеры в `Integrations/EasyInteraction` компилируются вместе с комплектом. Импорт production Samples и проверки версий исчезнувших отдельных пакетов не требуются. Базовое runtime ядро не зависит от адаптеров.

## FPS и общий ввод

Выберите существующий EasyMovement Character и выполните **Tools > EasyGameFramework > Interaction > Set Up Selected Player**. Editor API общего Setup: `EasyGameFramework.Editor.InteractionPlayerSetup.Configure(character)` возвращает Actor. Команда сохраняет единственный существующий camera-hosted Actor и явный HandAnchor, связывает Character/View/PlayerRoot, Presenter и EasyInput.EasyInteractionInput, отключает конкурирующий standalone reader. При нескольких неоднозначных child Actors сначала объясняет ошибку.

Motor и Lock используют независимые `Character.AcquireInputBlock` для ручного ввода. Скриптовое движение сохраняется. Motor реализует `IInteractionCollisionFilter.AcquireCollisionExclusion(Rigidbody)`; Grab автоматически получает lease для ECM2 sweeps/overlaps/pushing. Парные Unity collisions и deferred release обслуживает Grab. [Семантика ownership и границы внешнего вмешательства](GrabComfort.md).

Основной Interact вызывает `Actor.PressPrimary(out action, toggle)`. Secondary, socket и строки меню вызывают explicit `Press(action)`/`Presenter.Activate(action)`. Общие rebind/sensitivity/device/context settings принадлежат EasyInput; адаптеры читателей находятся в `Integrations/EasyInput`. Локализованные glyphs/подписи принадлежат Input и Localization, не дублируются здесь.

## Resources, Building и Combat

`ResourceInteractionCost` резервирует ResourceCost в связанном ResourceContainer и освобождает только свои резервы. `ResourcePickupAction`/`ResourceTransferAction` перемещают количество через атомарный публичный API Resources. Запас и сохранение ресурсов остаются в Resources.

`BuildingInteractionContext : IBuildingSessionProvider` возвращает `BuildingActor.GetSession(system)`. Назначьте того же BuildingActor в контекст и Placer, чтобы выбор был общим. Контекст не владеет временем жизни заимствованной сессии. `BuildingAction` выбирает/подтверждает/отменяет через эту сессию.

`CombatInteractionGuard` получает Health, отменяет действия по выбранным событиям и слушает StateRestored/Revived. Возвращает только собственное отключение Actor. Перед общим Restore сначала отмените активные interaction tickets, затем восстанавливайте состояния модулей через EasySave.

## Feedbacks, Localization и UI

`InteractionFeedbacks.ContextProvider` получает IFeedbackContextProvider или находит его на PlayerRoot/родителях. Контекст передаётся целиком, включая explicit gamepad target/none. Обычно используется PlayerFeedbackContext из штатной интеграции EasyInput. Управление звуком/музыкой/вибрацией остаётся в Feedbacks; отдельный опубликованный продукт EasyFeedbacks этим модулем не изменяется.

Для текста используется один canonical provider: **EasyLocalization.Integrations.InteractionTranslations** (`Integrations/EasyLocalization/EasyInteraction`). Назначьте его в Presenter.TextProvider и Presenter в поле provider.presenter; подключите Localization Catalog через его cache. Fallback доступен до загрузки. Отдельный дублирующий direct-table LocalizedInteractionText в единый пакет не перенесён. Выбранная локаль, таблицы и cache принадлежат Localization.

Окна, фокус и круговое меню игрового интерфейса предоставляет EasyUI: `InteractionMenuWindow` в `Integrations/EasyUI/EasyInteraction` получает Presenter. Оно использует явные action-команды и собственный window context. Gallery InteractionHUD — только автономный демонстрационный UI; он остаётся в Sample и не является prerequisite production-компонентов.

## NPC и внешние SDK

NavMeshInteractionMotor входит в `Integrations/EasyInteraction/Navigation` и использует стандартный UnityEngine.AI.NavMeshAgent. Он реализует тот же контракт подхода, но не подменяет правила движения врагов.

Netcode и XR находятся в `Addons~`, а не среди базовых integration assemblies. Минимальные SDK для сохранённых примеров: NGO2.7.0 и XRI3.2.2. Их SDK-specific tests/smoke входят в дополнения; включение/упаковку дополнений координирует root. ECM2/Cinemachine/VFX Graph остаются штатным окружением, не дополнениями.
