# Interaction UX — 2026-11-16

Реализованы визуальная настройка авторской позы, управляемая сила инерционного отставания, временный размер удерживаемого Rigidbody, Focus по умолчанию и opt-in URP обводка.

## First run

1. Выберите Grab Action в Authored Pose, нажмите **Edit Hold Pose in Scene View**. Укажите Preview Actor либо используйте фиксированный снимок камеры Scene View; он также работает в Prefab Mode. **W/E** перемещают/вращают cyan wire-preview, не сценический предмет. **Frame Preview** находит его, **Done** завершает редактирование. Записываются только LocalPosition/LocalEulerAngles через обычные Undo/prefab workflows.
2. HoldPoint имеет приоритет над HandAnchor, затем View/preview frame. View добавляет HoldDistance; anchor заменяет distance. Frame scale игнорируется, а root/GripPoint/parent geometry и HeldScaleMultiplier учитываются полным matrix-преобразованием. Preview — целевая поза, не симуляция контактов или multi-owner hand-axis motion. Skinned preview запекается один раз за сессию.
3. **Inertia Strength0.2** даёт пятую часть дополнительного позиционного/углового target lag относительно1 при одинаковом raw smoothing state. Inertia0.12 — отдельное время сглаживания. Это не гарантирует x0.2 движения физического тела: пружина, скорость и контакты остаются. Strength0 убирает добавленный target lag,1 восстанавливает прежнюю силу эффекта. При raw bounds0.35м/35° Strength0.2 ограничивает добавленное target lag0.07м/7°.
4. **Held Scale Multiplier0.1–1**, default1, равномерно умножает захваченный localScale Rigidbody root только на время общей hold-session. Модель, коллайдеры и GripPoint уменьшаются вместе. Scale, custom COM/tensor/rotation и automatic flags восстанавливаются после последнего выхода; transfer/multi-owner не умножают размер повторно. Масса не меняется. Восстановление размера происходит до освобождения owner collision exclusions.
5. Новый Actor использует **Focus**, конус60° и видимость; Raycast остаётся явным выбором enum0. Сохранённый режим и настройки существующего Actor не переписываются Setup.
6. Добавьте **Focus Outline** на Target и **Focus Highlighter** на Actor (кнопка в Inspector). Color/width настраиваются на outline. Static opaque MeshRenderer с normals, несколько дочерних частей, depth-tested hull без renderer feature/изменения source materials. Слой source определяет камеры; эффект общий для объекта. Resources shader включается в Player. Skinned, alpha clipping/transparency и automatic LOD switching не поддерживаются; hard/open meshes могут иметь щели.

## Контракты

`GrabAction.InertiaStrength`, `HeldScaleMultiplier`, `UnheldLocalScale`. Значения конфигурации не заменяют transient manual rotation/inspection/distance.

`GrabPoseUtility.GetRootPose(grab, Pose frame, bool usesViewDistance)`, `GetLocalPose(grab, Pose rootPose, Pose frame, bool usesViewDistance)`, `GetRootMatrix(grab, Pose rootPose)` — конкретные чистые преобразования без изменений объектов.

Editor-only `EasyInteraction.Editor.GrabPoseAuthoring.Begin(grab, actor=null)`, `End()`, `SetPreviewFrame(transform, bool isView=true)`, `SetSceneViewFrame(Pose)`, `TryGetPreviewPose(out Pose)`, `ApplyRootPose(Pose)`, `PreviewItem/IsActive`. ApplyRootPose использует тот же Undo/prefab путь, что handles. Session не сохраняет ссылки Actor/frame в prefab.

`InteractionFocusOutline.OutlineColor`, `Width`, `Renderers`, `RefreshRenderers()`, `RendererCount`, `UnsupportedRendererCount`, `IsShaderAvailable`, `IsHighlighted`, `RenderedPartCount`; `InteractionFocusHighlighter.Actor`, `CurrentOutline`, `RefreshHighlight()`. RenderedPartCount подтверждает draw submissions, не видимые пиксели. Shader asset: `Runtime/Resources/EasyInteraction/FocusOutline.shader`, load path `EasyInteraction/FocusOutline`, shader name `EasyGameFramework/Interaction/Focus Outline`.

## Физические границы

Уменьшается физический root, а не отдельная display-модель. Дополнительные helper GameObject для scale/pose не создаются. Custom COM масштабируется вручную, поскольку Unity задаёт его без Transform scale; custom inertia tensor умножается на multiplier², automatic mass properties пересчитываются. См. [Unity Rigidbody.centerOfMass](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rigidbody/centerofmass). Default1 не пишет scale/mass properties. Контакты при восстановлении размера рядом со стеной сохраняют обычное физическое поведение, расширение не телепортирует предмет. Не изменяйте параллельно исходный scale другого адаптера во время владения GrabAction.

При pooling неактивный native Rigidbody может не раскрывать сохранённый COM через getter. Регрессия сравнивает inactive getter с обычным не удерживаемым Rigidbody, затем проверяет полное исходное активное состояние после enable **до нового захвата**; дополнительный сценарий удаляет GrabAction в inactive-состоянии до enable. Так подтверждено восстановление без скрытого OnEnable repair. Исходный неверный момент измерения сохранён в первом raw report, assert активного состояния сохранён/усилен.

## Сценарий общего Playground

Координатор обновляет канонический sample, импортированную копию и сцену точечно: Actor.Focus, opt-in highlighter/outline на составном предмете, HeldScaleMultiplier0.65 и InertiaStrength0.2. Проверить W/E Editor ghost и Undo в scene/prefab; в Play сравнить Strength0.2/1 без утверждения x5 физической амплитуды, измерить scale0.65/drop restore/transfer, выбрать off-axis target Focus и сравнить с Raycast. Снять URP screenshots с outline, сменой фокуса и частичным/полным opaque-wall occlusion; повторить реальное GPU rendering в Player, проверив shader delivery. Проверить E+mouse/hover loss, modal input блокировку, R+mouse/regrab/G throw и очистку world/input.

Это owner-реализация и целевая валидация. Независимые scene visual/Player проверки и публикация принадлежат координатору; их результаты не подменяются счётчиками unit-тестов.

## Owner validation

- Final PlayMode: **193/193 PASS**, including native shared input, Focus/default and persisted Raycast, held geometry/mass properties/lifecycle/death, exact inertia force/torque ratio, static outline routing/resources/material cleanup and calibrated warm GC.
- Final Editor: **16/16 PASS**, including scaled parent/grip pose math, actual Undo/Redo, prefab save/reload without moving the scene root, new Actor/Setup defaults and preview cleanup on mode change.
- Targeted final-exit regression: **7/7 PASS**. An earlier inactive-native COM assertion was replaced with a plain Rigidbody control and stronger active-state checks before regrab, also after deleting GrabAction while inactive. First raw report remains preserved; no runtime workaround or hidden OnEnable repair was introduced.
- No failed/skipped/inconclusive cases in final runs. Run counts overlap and are not a unique-test total. Compilation completed without errors. Positive GC controls detected;128 warmed steps produced zero main-thread allocation events in each measured hold/collision mode and static outline routing/draw scope. Initial allocation/new leases/material/renderer discovery are outside scope.
- Same Games Factory Editor PID24668 returned after final tests at **2026-11-16 11:37:06 UTC /14:37:06 Moscow**, stopped, runner inactive, not compiling/updating, original Playground clean. Own atomic lease was released; free lock observed at **11:38:15 UTC /14:38:15 Moscow**. No second Editor, restart, world save/discard or publication.

The final Editor-only guard/test changed after the193-case runtime run; runtime/shader/PlayMode code remained unchanged. Actual common-scene GPU screenshots, URP Player rendering and broader framework acceptance are coordinator work, not results claimed by this owner report.
