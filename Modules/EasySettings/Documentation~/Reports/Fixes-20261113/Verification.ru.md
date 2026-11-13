# EasySettings — исправления аудита, 13 ноября 2026

Исправлены обе подтверждённые ошибки EasySettings и добавлена интеграция базового FOV через API EasyMovement. Версия Settings остаётся 0.1.0. Никакой консолидации пакетов, push, tag или публичного выпуска не выполнялось.

## Изменения

**Lifecycle.** `SettingsSession.Dispose()` теперь допустим как терминальное действие из Changed/Committed и backend callbacks. Он синхронно отмечает сессию закрытой, прекращает dispatch оставшихся подписчиков, откатывает pending preview и гарантированно освобождает backend. Повторное закрытие идемпотентно; запрет первого Dispose с чужого потока сохранён. Обычные реентерабельные мутации по-прежнему отклоняются, Capture внутри Committed остаётся допустимым.

`SettingsController` отсоединяет старые Session/backend до cleanup и использует поколение/идентичность сессии. Старые `BindTargets`/notifications не продолжаются после disable, destroy или повторного enable. Backend освобождает URP/камеру/Volume/глобальные параметры до вызова внешнего Dispose FOV lease, который может уведомлять пользовательский код. Callback закрытия во время Acquire не оставляет возвращённый позже handle.

**Честный FOV.** Прямая Camera считается недоступной для FOV, когда активный CinemachineBrain владеет её lens. Проверяются active Brain list, OutputCamera и ControlledObject; наличие Brain на другом GameObject тоже учтено. Одинаковая защита действует в Describe, Apply и ReleaseTargets. Откатывается только собственная фактическая запись FOV, если её не заменил новый писатель/Brain. UI и Inspector показывают конкретную причину недоступности.

**EasyMovement.** Новый встроенный компонент `EasySettings.Integrations.EasyMovementSettingsFov` обращается к `EasyMovement.FirstPerson.FirstPersonFov`; выходная Camera не переписывается. Адаптер читает BaseFieldOfView, получает AcquireBaseFovOverride, обновляет TrySet и снимает только свой lease. Gameplay offsets и другие base owners сохраняются. При authored walk60/sprint70 и user base80 результат80/90; zoom -15 даёт65/75, snapshot остаётся80.

Первый общий SetActive(true) тоже проверен: adapter.OnEnable обновляет уже назначенную сессию синхронно, когда это безопасно. Source Changed во время Apply/notification ставит одно отложенное обновление доступности/readback. Startup baseline обновляется до первого commit только при отсутствии пользовательских edits. Постоянной записи lens каждый кадр со стороны Settings нет.

Cinemachine3.1.7 — явная штатная зависимость Settings. Интеграционная assembly активна при Movement>=5.2.0; опубликованный5.1.0 не имеет нового API и не включается этим gate. Обратной зависимости Movement->Settings нет. Требуется FirstPersonFov на игроке; отсутствующий/disabled owner имеет диагностируемое состояние и не приводит к fallback-записи в Cinemachine output camera.

## API и настройка

Старые Capture/Validate/Restore и DTO version1 сохранены. Новые контракты:

- `ISettingsFieldOfViewProvider.TryRead(out float degrees, out string reason)` и `Acquire(UnityEngine.Object owner, float degrees)`.
- `ISettingsFieldOfViewOverride`: IsActive, TrySet(float), Dispose().
- `SettingsController.FieldOfViewProvider`, `BindFieldOfViewProvider(MonoBehaviour)`, `RefreshFieldOfViewProvider(MonoBehaviour, bool immediately=false)`.
- `UrpSettingsBackend.BindFieldOfViewProvider(ISettingsFieldOfViewProvider, UnityEngine.Object)` и диагностический `CanControlFieldOfView(Camera, out string)`.
- `EasyMovementSettingsFov.PlayerFov`, `Bind(FirstPersonFov)`, реализация provider/owned handles.

Для новичка: добавить First Person FOV возле FPS controller, затем EasyMovement Settings FOV возле Settings Controller и перетащить ссылку игрока. Provider подставляется при пустом поле, в инспекторе есть явная кнопка привязки. Обычная perspective Camera без Brain продолжает работать без адаптера. Подробности и пример80/90 с zoom находятся в README/Guide.

## Выполненные проверки

| Прогон | Результат |
| --- | --- |
| Компиляция Games Factory | failed=false, errors=[], compilationFailed=false |
| Собственные EditMode |42/42 passed,0 failed/skipped/inconclusive |
| Собственные strict PlayMode |20/32 passed,12 failed только по Missing Script warning в teardown |
| Общий прогон координатора |805/805 Edit;504/516 Play — те же12 warning failures; unexpectedAssertionFailures=0 |
| Независимая проверка координатора save/lifecycle |13/13 |
| Независимая проверка координатора FOV owner |12/12, реальный Brain/штатный FPS prefab/gait/offsets |

Это НЕ объявляется полностью зелёным строгим Editor PlayMode прогоном. Исходные failed attempts сохранены. В runtime и тестах не добавлялись IgnoreFailingMessages, blanket/узкие Expect, выключение logger или ослабление FOV assertions.

Собственные регрессии охватывают: Changed/Committed Dispose; прекращение последующих callbacks; preview rollback при закрытии; cleanup при ошибке rollback; backend callback reentry; disable+enable в одном callback; Destroy/DestroyImmediate; SessionChanged reenable; BindTargets, прерванный Refresh callback; обычные Camera; Brain на другой ControlledObject; включение Brain после редактирования; смену virtual cameras; release при новом lens owner; startup availability адаптера; base/gait deltas; zoom и другие base owners; disable/re-enable provider; pooling FirstPersonFov; отсутствующий owner; возврат lease после закрывшего controller callback; настоящий SettingsSaveAdapter round trip базового FOV.

Две первоначальные проверки output camera ошибочно вызывали повторный Brain.ManualUpdate в том же кадре. Они исправлены ожиданием следующего настоящего кадра. Численные ожидания не менялись. Первоначальные9 setup failures обнаружили и привели к исправлению реального порядка OnEnable; assertion доступности оставлен сразу после SetActive, перед yield.

## Отдельное Editor/VFX предупреждение

Все12 текущих failures вызваны строгим `LogAssert.NoUnexpectedReceived` для предупреждения `The referenced script (Unknown) on this Behaviour is missing!`. Полный сохранённый стек:

`VisualEffectResource.get_graph -> VFXGraphPreprocessor.OnPostprocessAllAssets -> VFXAssetManager.Build -> VFXLibrary.SRPChanged -> RenderPipelineManager.OnActiveRenderPipelineAssetChanged -> GameView.OnGUI`.

Read-only snapshot обнаружил0 missing MonoBehaviours среди всех загруженных scene/asset/hidden GameObjects; selection=null. Из41 script GUID в штатных VFX17.3 templates разрешаются40. Единственная отсутствующая ссылка —081ffb0090424ba4cb05370a42ead6b9, встречается, например, в `01_Minimal_System.vfx:640`. Она принадлежит HDRP-специфичному VFXHDRPSubOutput, что подтверждают [официальные метаданные Unity Graphics](https://raw.githubusercontent.com/Unity-Technologies/Graphics/master/Packages/com.unity.render-pipelines.high-definition/Editor/VFXGraph/VFXHDRPSubOutput.cs.meta). HDRP в URP-only проекте не установлен.

Это не потеря GUID/скрипта EasySettings. При этом независимый минимальный Unity-only контроль вне Unity Test Runner **не воспроизвёл** warning: пустая собственная сцена, Instantiate URP asset, QualitySettings swap,4 подтверждённых camera renders, restore+Destroy,0 logs. Он не создаёт объекты/API EasySettings/Movement/Save. Поэтому не заявляется, что любой swap URP обязательно вызывает warning; воспроизводимость зависит от Editor/test-runner/import context. Шаблоны Unity, Library cache и внешние пакеты не правились, HDRP не добавлялся. Независимый контроль не служит оправданием для скрытия красных результатов.

При первом восстановлении диагностической сцены обнаружилось ограничение JsonUtility с динамическим eval-классом. Запись восстановлена по ближайшему coordinator inventory и Editor.log, исходная сохранённая EasySuiteLab возвращена. Helper переведён на native SessionState primitives; повторный контроль и восстановление завершены успешно. Чужая сцена не сохранялась и не редактировалась; собственный Editor lease освобождён, PlayMode остановлен.

## Артефакты и границы

Raw: `Audits/EasySettings/Fixes-20261113`, копии — соседняя Raw. Полный source file manifest с SHA256 — source-hashes.json;92 code/assets/metadata files, canonical digest381D1AC8D97A712C1725DC839BF2DC3470A3642376311EE71A5A5EF4B4EE78D3. Существующие GUID не менялись. Отдельный docs-only.diff фиксирует изменения инструкции.

Runtime/metadata/samples заморожены и переданы координатору для clean install/Player. Эти новые acceptance-прогоны выполняет отдельный проверочный чат; до поступления их raw результата они здесь не засчитываются. Предыдущие Player результаты базовой0.1.0 не выдаются за проверку нового FOV bridge. Физические display modes, другие платформы, shader stripping и lens writers вне owner API не получили новых гарантий.

Не выполнялись изменения соседних модулей, общей архитектурной миграции, новых публичных репозиториев или публикация. Movement5.2.0 принадлежит своему владельцу и разрешён отдельно координатором; Settings версия остаётся0.1.0.
