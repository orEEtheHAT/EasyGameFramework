# EasySettings в едином пакете

## Основной сценарий и состояние

Defaults — исходная SO-конфигурация и ограничения. Session копирует их при создании. Draft редактируется как struct copy; Applied содержит последнее подтверждённое состояние; Preview — запрос экрана в ожидании решения, Observed — последнее чтение backend. Reset заполняет draft, Apply применяет его. SelectProfile берёт только графику и сохраняет выбранный экран/VSync/FPS. В инспекторе профиля поля экрана и frame pacing скрыты, поскольку профиль ими не владеет.

Короткий API находится на SettingsController: Draft, Applied, HasPreview, Apply/Confirm/Revert/Cancel/Reset/SelectProfile(int), Capture/Validate/Restore. Расширенный API доступен в Session. Методы работают на Unity main thread. Настройки не применяются автоматически при установке модуля или создании Defaults.

Apply нормализует поддерживаемые значения с Messages; непригодные разрешения/режимы отклоняет до записи. VSync имеет приоритет над FPS limit. Runtime URP asset клонируется при первом изменении render scale, MSAA или дальности теней; renderer assets только читаются. Создание controller, чтение и изменение только FOV, камеры, Volume, VSync/FPS или экрана не переключают pipeline. UrpSettingsBackend.RuntimePipeline возвращает null до первого изменения pipeline; последующие изменения используют ту же копию. VolumeProfile клонируется глубоко, включая компоненты и восстановление прежней internal-profile ссылки.

## Экран и lifecycle

Display preview наблюдает фактическое состояние после смены кадров, подтверждение становится доступным после двух наблюдений и 0.2 секунд. Timeout5–60 секунд использует unscaled time. Capture не сохраняет неподтверждённый запрос. Confirm сохраняет фактически принятые ОС размер/режим/частоту. В windowed/borderless частота остаётся частотой desktop. Это readback API, а не подтверждение физической точности монитора. При полной приостановке приложения код сможет откатить режим после возобновления кадров.

Один enabled controller владеет процессными graphics overrides. Disable/destroy восстанавливает стартовую конфигурацию. Поэтому UI и контроллер живут отдельно; открывать/закрывать нужно окно. BindTargets восстанавливает прежнюю камеру/Volume, затем переносит подтверждённую графику на новые targets. FollowMainCamera реагирует на sceneLoaded; respawn в той же сцене требует BindTargets.

Dispose разрешён внутри Changed/Committed как терминальное действие, прекращает последующие callbacks и освобождает backend синхронно. Capture внутри Committed разрешён. Обычные Apply/Restore/Draft mutations внутри callbacks отклоняются. RequestCancel специально предназначен для закрытия UI из события: отмена выполняется после завершения текущего dispatch, без реентерабельного Apply. SettingsWindow использует его при disable/закрытии confirmation. Поколение controller защищает BindTargets/notifications при синхронном disable+enable.

## Поддержка URP

| Настройка | Условия |
| --- | --- |
| Resolution/window/refresh | Desktop Player; список Screen.resolutions и текущий размер. Exclusive только Windows; maximized macOS. |
| VSync/FPS | QualitySettings.vSyncCount / Application.targetFrameRate; platform pacing может иметь приоритет. |
| Render scale | Runtime clone URP renderScale, границы0.1–2 и ограничения Defaults. |
| MSAA | Forward/Forward+ во всех renderer entries, hardware limit, Allow MSAA, без TAA. Число pipeline не доказывает sampling каждого custom render target. |
| Shadows | shadowDistance, включая0; source asset должен поддерживать realtime shadows. Baked lighting не меняется. |
| FOV | Независимая perspective Camera либо явно назначенный base-FOV owner. Active Brain, в том числе ControlledObject на другом объекте, запрещает прямую запись output lens. |
| Post/AA | Явный opt-in после проверки retained variants, URP base camera и post-process renderer data; None/FXAA/SMAA. TAA сохраняется, но не предлагается. |
| Exposure/bloom | Отдельный enabled global Volume weight1 в camera layer mask, существующие активные ColorAdjustments/Bloom, author opt-in. Более приоритетные volumes могут влиять на результат. |

Settings не восстанавливает stripped shader variants, не включает фиктивные internal-only shadow switches и не меняет HDRP/Built-in. В VFX baseline есть отдельный Editor warning штатных templates с отсутствующим HDRP sub-output; он не фильтруется и cache не патчится. Численные assertions и warnings учитываются отдельно в приёмке.

## Штатные интеграции

Production assemblies лежат в `Integrations/EasySettings`:

- EasyUI: `EasySettings.UI`, SettingsWindow.Bind(controller), TextProvider. Зависит от EasyUI.Runtime, не от combined menu assembly.
- EasyLocalization: `EasySettings.Localization`, LocalizedSettingsText.Bind(LocalizedCatalogCache). Использует owner Get/Changed; fallback labels остаются читаемыми.
- EasyMovement: `EasySettings.EasyMovement`, EasyMovementSettingsFov. Все модули теперь штатны, старых versionDefines/package-ID guards нет.

Общее меню и typed child windows принадлежат EasyUI: Input сохраняет бинды/чувствительность; Feedbacks — аудио/вибрацию; Localization — язык. Каждое блокирующее child window получает собственный EasyInput request через EasyUI. Закрытие графики не откатывает настройки чужих модулей. В Settings нет второй копии их runtime state, глобального меню-менеджера или общего фиктивного Apply.

FOV adapter получает FirstPersonFov.AcquireBaseFovOverride, читает BaseFieldOfView и обновляет TrySet. Снимает только свой handle; gait deltas и gameplay AcquireOffset остаются у Movement. При отключении provider его запрос освобождается; после pooling Apply получает новый handle. Bind(playerFov) выбирает другого игрока; сохранённый snapshot применяйте явно. Missing/disabled owner не вызывает fallback в output Camera.

Для другого конкретного FOV owner доступны ISettingsFieldOfViewProvider (TryRead, Acquire) и ISettingsFieldOfViewOverride (IsActive, TrySet, Dispose). Это граница владения lens, не общий service framework. Изменения availability/readback идут через RefreshFieldOfViewProvider, без постоянной записи lens каждый кадр.

## Сохранение

SettingsSaveAdapter принадлежит Save и сохраняет SettingsSnapshot в Preferences. Controller должен быть доступен; pending preview не загружается поверх другого preview. Capture сохраняет только confirmed state, Validate немутирующий, неизвестная версия/неполный snapshot отклоняются. Restore(snapshot,false) сохраняет АКТУАЛЬНЫЙ display из backend.Read, включая внешнее изменение размера окна; Validate по умолчанию проверяет graphics-only, чтобы старый monitor mode не блокировал графику. Restore(...,true) требует нового подтверждения и не должен подтверждаться автоматически при загрузке.

Не добавлены PlayerPrefs/файловое хранилище, Jobs/Burst или глобальный игровой цикл. Idle Tick не читает/не пишет backend; UI обновляется по событиям и countdown preview. CPU Apply timing не равен FPS/GPU cost; GC проверки требуют положительного контроля.

## После CUTOVER

Проверить Edit/Play регрессии Settings, UI-close из Changed/Committed, Display timeout/capture, source assets/lifetime, FOV gait/zoom/other owners, SettingsSaveAdapter. Тесты UI/FOV находятся в Tests и не требуют импорта production samples. Демо-сцена создаётся из опционального Settings Demo командой Tools > EasyGameFramework > Settings > Create first-run scene.

Чистую установку, общую сборку и platform/physical-display QA координирует root. Сохранённые Reports относятся к прежнему этапу; STAGING source compilation не заменяет Unity validation нового пакета.
