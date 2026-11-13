# EasySettings 0.1.0 — проверка 13 ноября 2026

Локальная реализация завершена в рамках URP-only. Публичной публикации, remote push и тегов не было.
Рабочая среда: Unity 6000.3.22f1, URP 17.3.0, Windows/D3D11, NVIDIA RTX 4070 Ti SUPER.

## Реализовано

- Embedded UPM `com.oreethehat.easy-settings` 0.1.0, отдельный локальный Git. Единственная авторская обязательная зависимость — EasyCore 0.1.0 (Numeric.IsFinite/IsNonNegative).
- SO defaults/ограничения/профили; value-copy Draft, Applied (confirmed), Preview, Observed. Apply/Cancel/Revert/Reset/SelectProfile, события Changed/Committed, schema-1 Capture/Validate/Restore.
- Поддерживаемые desktop window modes/resolutions/rational refresh, VSync/FPS, URP render scale/MSAA/shadow distance; perspective camera FOV и явно разрешённые camera AA/post processing/Volume exposure/bloom.
- Runtime clone URP asset; renderer assets только читаются. Глубокая копия VolumeProfile и его components, восстановление прежней internal-profile ссылки. Явное владение камерой/глобальными overrides, смена целей, disable/dispose и повторный запуск.
- Preview экрана с unscaled timeout, подтверждение только после наблюдений; сохранение фактически принятого размера/режима. Capture никогда не сохраняет неподтверждённый preview.
- Restore(snapshot,false) сохраняет АКТУАЛЬНЫЙ экран из backend.Read, включая внешнее resize без предварительного Refresh. Неполный snapshot и неизвестная версия отклоняются. Validate по умолчанию graphics-only; для экрана есть overload Validate(snapshot,true).
- Готовое scrollable окно EasyUI, отдельный дочерний confirmation dialog, клавиатурная/gamepad навигация и автопрокрутка к выбору, причины недоступности, runtime values, профили, Apply/Cancel/Reset. Отдельный EasyLocalization LocalizedCatalogCache adapter, slots для чужих окон без дублирования их состояния.
- Инспекторы с объяснениями; README, подробный Guide и меню создания first-run сцены.
- Jobs/Burst не используются для UI/URP apply. В idle нет backend read/write и повторного применения настроек.

## Автоматические результаты

| Контекст | Результат | Raw evidence |
| --- | --- | --- |
| Games Factory, compile | errors 0, compilationFailed=false | Raw/compile.json |
| Games Factory, EditMode | 33 / 33 | Raw/editmode.json |
| Games Factory, PlayMode | 9 / 9 | Raw/playmode.json |
| EasySave.Settings.Tests, опубликованный адаптер | 3 / 3, Editor assembly | Raw/save-adapter.json |
| Чистый URP проект, без UI/Save/Localization | 33 / 33 EditMode + 8 / 8 PlayMode | Raw/clean-EditMode.xml, Raw/clean-PlayMode.xml |
| Чистый Windows Development build | succeeded, 0 errors / 0 warnings | Raw/clean-build.json |
| Чистый Windows windowed Player | passed | Raw/clean-player.json |
| Games Factory first-run Windows Player с EasyUI | passed | Raw/ui-player.json |

Чистая установка: отдельный `Audits/EasySettings/CleanProject`, встроены только EasyCore/Settings с необходимыми Unity dependencies. Это install-проверка, не альтернативный повседневный проект. ECM2 и другие платные SDK не копировались. Сборка чистого проекта завершена и его Editor закрыт.

Проверено: validate/clamp/unsupported, VSync vs FPS, ошибки apply/rollback, preview capture/confirm/timeout/cancel/dispose, version/JSON round trip, внешнее resize, старый monitor mode, неполный snapshot, отсутствие backend work в idle, запрет worker-thread mutation, read-only Capture из Committed, profile/reset semantics. PlayMode дополнительно проверяет JSON source pipeline неизменным, глубокий Volume clone и его прежнюю ссылку, restore FOV, замену/уничтожение целей, duplicate owner, три enable/disable, повторное открытие окна без дублирования UI и переподключение локализованного view.

Оба native Player запускались с `-screen-fullscreen 0 -screen-width 1280 -screen-height 720`. Автоматизация меняла только размеры собственного окна. Подтверждение и timeout при timeScale=0 проверены через реальные Screen API. Отдельно моделировалось внешнее изменение размера окна; Restore графики оставил этот размер и не открыл preview. Завершение вернуло исходные 1280×720.

Локальная сцена: `Assets/PackageTests/EasySettings/FirstRun.unity`.
Скриншот итогового окна: `Audits/EasySettings/ui-player.png` (визуально просмотрен).
Полные build reports, логи и вспомогательные скрипты: `Audits/EasySettings` и `Library/EasySettingsWork`.

## Замеры и пределы выводов

| Измерение | Чистый Player | Player с EasyUI |
| --- | ---: | ---: |
| Среднее CPU-время Apply, 12 изменений | 0.0383 ms | 0.0817 ms |
| Idle Session.Tick, цикл 100000 вызовов | 7.04 ns/call | 8.36 ns/call |
| Максимум whole-frame GC в idle sample | 1342 bytes | 5236 bytes |
| Максимум с контрольным выделением 256 KiB | 262280 bytes | 262280 bytes |

GC recorder positive control сработал в обоих Player. Whole-frame числа включают все системы сцены/UI: это не заявление о нулевых аллокациях пакета. Время Apply измеряет CPU API, а не стоимость рендера/GPU или настоящий FPS. Никакой скрытый Editor cadence не выдаётся за FPS. У idle отдельно проверено ноль backend reads/writes за 1000 Tick.

В общей сборке один warning от Unity `Hidden/Core/DebugOccluder` (D3D11 implicit vector truncation); собственных compile/build errors нет. В native UI log один диагностический `Ran out of Graphics Ring Buffer space` при одновременной нагрузке разработки; тесты и снимок прошли. В чистой сборке таких build warnings нет. Эти сообщения сохранены в raw logs, не замаскированы.

## Границы и интеграция

- Физические exclusive fullscreen/частоты монитора, macOS/Linux, HDR-дисплеи и другие платформы не проверены. Их нельзя считать подтверждёнными по Windows windowed smoke-test.
- Camera/Volume post-processing изменения требуют author opt-in после сохранения/проверки shader variants и ресурсов в Player. Эти flags выключены по умолчанию. Runtime API не может восстановить stripped variants или изменить baked lighting. Native демонстрация не утверждает визуальную проверку всех post effects.
- FOV относится к явно назначенной perspective Camera. Если lens/FOV принадлежит Cinemachine/EasyMovement/другому rig, подключать через API владельца; два писателя на один lens не поддерживаются.
- Поддержка MSAA консервативная: все renderer entries должны быть Forward/Forward+; TAA/camera stack/custom render targets могут иметь собственные ограничения. Показанные числа — параметры pipeline/owned overrides, не доказательство конечного framebuffer для всех камер.
- Единственный enabled settings owner на процесс. Disable восстанавливает начальную конфигурацию; закрывать нужно окно, а не контроллер сессии. Для смены сцен есть BindTargets и FollowMainCamera на sceneLoaded; respawn внутри сцены требует BindTargets.
- EasySave adapter принадлежит EasySave, не дублирован здесь. На момент прогона его 3 тестов сохранён дополнительный guard, отказывающий при live display != Applied после внешнего resize. Owner API Settings уже исправлен и проверен; избыточный guard может убрать только владелец EasySave. Запрос и доказательства опубликованы в Status/EasySettings.txt. Прямой Settings.Restore(false) этот сценарий поддерживает.
- Продуктовые настройки input/audio/language и чужие окна не переносятся в этот модуль. Общая миграция/упаковка EasyGameFramework и публичный релиз остаются этапом координатора.

## Источники API

Реализация сверена с исходниками установленных Unity пакетов и официальной документацией:
[URP 17.3 UniversalRenderPipelineAsset](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal%4017.3/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html),
[Screen.SetResolution](https://docs.unity3d.com/cn/6000.0/ScriptReference/Screen.SetResolution.html).
Для API доступности определяющим был исходный код установленной версии, включая internal setters shadow support и публичные rendererDataList/Volume.profile.
