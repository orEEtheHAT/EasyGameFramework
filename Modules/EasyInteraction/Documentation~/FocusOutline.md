# Обводка фокуса: Target и камера наблюдателя

Предмет разрешает обводку одним флагом **Focus Outline** на **Interaction Target**. Его API — `HighlightWhenFocused`, default `true`. Цвет, толщина и render camera принадлежат одному **Focus Highlighter** на Actor-наблюдателе. Дополнительный компонент outline на каждом предмете не нужен.

Таким образом, в сцене может быть много обычных Targets и один Highlighter игрока. Каждый наблюдатель рисует только свою текущую цель и только в свою камеру; смена цели не меняет общие материалы предмета и не включает обводку для всех камер проекта.

## Первый запуск

1. У предмета уже должны быть **Interaction Target**, Collider для выбора и нужные Interaction Action. На Target оставьте основной флаг **Focus Outline** включённым. Новые Targets создаются с ним; существующее явно сохранённое значение не нужно заменять ради default.
2. Добавьте **EasyGameFramework > Interaction > Focus Highlighter** на Actor игрока. Поле **Actor** можно оставить пустым — используется Actor того же GameObject. Для одного render camera обычно достаточно одного Highlighter.
3. По умолчанию Highlighter ищет **Camera непосредственно на `Actor.View`**. Если View — только transform прицеливания, а изображение рисует другая камера, назначьте её в **Camera Override**. Для Cinemachine укажите реальную выходную Unity Camera, а не виртуальную камеру. Поиска `Camera.main`, Camera в произвольных children или глобального fallback нет.
4. Настройте **Outline Color** и **Width** на Highlighter. В Play выберите предмет фокусом Actor: силуэт появится только в resolved camera, пока предмет активен, разрешает обводку, имеет поддерживаемую геометрию и не удерживается через Grab.

Без Highlighter на наблюдателе один default-true флаг Target ничего не рисует. Выключение **Focus Outline** запрещает подсветку этого предмета всем наблюдателям, сохраняя сам Target и его игровые действия.

## Параметры предмета и наблюдателя

| Где | Поле Inspector / API | Default и смысл |
| --- | --- | --- |
| **Interaction Target** | **Focus Outline** / `HighlightWhenFocused` (`highlightWhenFocused`) | `true`. Разрешение подсветки именно этого Target. Не задаёт стиль, не отключает gameplay и не меняет режим выбора Actor. |
| Target, Advanced | **Outline Renderers** / `OutlineRenderers` (`outlineRenderers`) | Пустой массив — автоматический сбор дочерних статических MeshRenderer, принадлежащих ближайшему этому Target. Явный список нужен для исключений и отдельных составных частей. |
| **Focus Highlighter** | **Actor** / `Actor` | Пусто — Actor на этом GameObject; можно назначить другой явно. Его `CurrentTarget` определяет наблюдаемый предмет. |
| Highlighter | **Outline Color** / `OutlineColor` | Cyan `(0.1, 0.8, 1, 1)`. Общий unlit-цвет выбранной цели для этого наблюдателя. Не переписывает материалы Renderer. |
| Highlighter | **Width** / `Width` | `0.015` м, границы `0.001…0.1` м. Толщина extrusion в мировых метрах, а не в пикселях; дальний предмет визуально имеет более тонкий контур. |
| Highlighter | **Camera Override** / `CameraOverride` | Не назначена — точная Camera на Actor.View. Назначенная Camera имеет приоритет. Её GameObject должен существовать и быть активным. Компонент Camera может быть выключен для явного/manual render; это не заставляет такую камеру автоматически рендерить кадры. |

Два Actors с разными камерами могут одновременно выбирать разные Targets и разные стили. Их draws адресуются разным Camera. Не назначайте два независимых Highlighter с конфликтующими стилями одной render camera без явной игровой причины: оба будут рисовать в одни пиксели. Отдельного per-item цвета/толщины в этой схеме нет.

## Что скрывает обводку

- Нет активного Highlighter/Actor, текущего Target или пригодной resolved camera.
- Target выключен, уничтожен либо его **Focus Outline** выключен.
- Shader недоступен/неподдержан или активный pipeline не URP.
- Нет пригодных частей либо source Renderer не проходит обычные проверки: enabled/active, `forceRenderingOff`, ShadowsOnly и слой в culling mask resolved camera.
- Любой Grab Action этого Target удерживается хотя бы одним Actor.

Последнее правило действует **для всех наблюдателей** до последнего release. Подсветка не появляется кратковременно во время transfer или ухода одного из нескольких владельцев. После полного отпускания она снова определяется обычным Focus и разрешением Target. Внутренний список Actions Target уже закеширован; поиск Grab-компонентов на каждом кадре не нужен.

`ObservedTarget` и `HighlightedTarget` могут различаться: наблюдатель всё ещё выбирает предмет, но не рисует его, например из-за held-suppression, false-флага или отсутствующей камеры. Диагностика готовности не доказывает, что на экране уже есть пиксели силуэта: GPU depth test, culling и сам факт render кадра проверяются отдельно.

## Геометрия и ограничения изображения

Используется URP inverted-hull shader `EasyGameFramework/Interaction/Focus Outline` из `Resources/EasyInteraction/FocusOutline.shader`. Исходные sharedMesh, материалы, слои, global settings и renderer features не изменяются. Shader включается как Resources asset; `IsShaderAvailable` проверяет активный URP и поддержку shader, но не заменяет проверку целевой платформы/Player при её выпуске.

Поддерживаются **статические MeshRenderer с MeshFilter и normals**. В автоматическом режиме самостоятельные вложенные Targets исключаются; дубли частей не рисуются дважды. SkinnedMeshRenderer не поддерживается этой runtime-обводкой. Если приложению нужна фиксированная запечённая модель, оно может предоставить её как обычную статическую геометрию; автоматически анимированный bake на каждый кадр здесь не создаётся.

Для readable meshes используется приватная копия геометрии со сглаженными extrusion normals в точно совпадающих вершинах. Это закрывает типичные разрывы между жёсткими гранями Cube, сохраняя исходное освещение и normals модели. Target-потребители одного source mesh разделяют внутреннюю snapshot-запись. У нечитаемого mesh без CPU data используется исходная геометрия/normals: Read/Write автоматически не включается, source не клонируется для недоступного сглаживания. `FallbackRendererCount` помогает увидеть такой случай.

Depth test — **LEqual**, поэтому контур скрывается непрозрачными стенами. Это не эффект просвечивания сквозь препятствия. Прозрачность/alpha clipping исходных материалов и автоматическое переключение LODGroup не повторяются. Открытые, nonmanifold meshes, несовпадающие вершины и raw normals могут оставлять швы или незамкнутый силуэт; предпочтительна закрытая opaque-геометрия. Для LOD и составных объектов задавайте понятный фиксированный набор Outline Renderers.

## Кто хранит ресурсы и когда освобождает

На Target геометрия создаётся **лениво**, при первом допустимом focus; default-true Targets не создают по материалу/mesh на старте сцены. Это обычный внутренний cache, не дополнительный MonoBehaviour или глобальный update-manager.

Target владеет renderer-частями и leases на сглаженные source snapshots. Они остаются прогретыми при потере фокуса, удержании, смене камеры и отключении наблюдателя. Это намеренное владение Target, позволяющее повторно выбирать/отпускать предмет без пересоздания geometry. False-флаг, disable/destroy Target и замена/удаление источника освобождают соответствующие leases; последняя lease освобождает принадлежащий общему cache приватный mesh. Уже созданный cache обслуживает изменения source даже когда Target сейчас не в фокусе.

Каждый Highlighter отдельно владеет одним лениво созданным Material своего стиля. Он переиспользуется при смене цели и grab/drop, а disable/destroy Highlighter уничтожает именно этот материал. Отключённый наблюдатель не уничтожает геометрию других Targets или материал другого наблюдателя. Материал на каждом предмете больше не хранится.

Изменения иерархии/явного списка в runtime можно согласовать через `target.RefreshOutlineRenderers()`; это обновляет уже созданный geometry cache и не вынуждает cold Target заранее выделять ресурсы. При замене camera/Actor/View или добавлении Camera на прежний View вызовите `highlighter.RefreshHighlight()`, чтобы немедленно обновить cached resolution. Публичные setters стиля/ссылок используются приложением на главном потоке; не меняйте source mesh и те же renderer-ссылки параллельно с их обслуживанием.

## Короткое API

```csharp
// Предмету достаточно Target. Он не нуждается в отдельном outline-компоненте.
target.HighlightWhenFocused = true;
// target.OutlineRenderers = chosenParts; // Пусто — auto.
target.RefreshOutlineRenderers();

// Один стиль и camera на наблюдателе.
highlighter.Actor = actor;
highlighter.OutlineColor = new Color(.1f, .8f, 1, 1);
highlighter.Width = .015f;
highlighter.CameraOverride = outputCamera; // Либо null для точной Camera на Actor.View.
highlighter.RefreshHighlight();
InteractionTarget shown = highlighter.HighlightedTarget;
```

| Диагностика Highlighter | Что означает |
| --- | --- |
| `ObservedTarget` | Target, за которым наблюдатель следит по выбору Actor; он может пока не иметь видимой обводки. |
| `HighlightedTarget` | Текущая допустимая к подсветке цель либо null при held/false/disabled/missing-camera/недопустимой render-конфигурации. Это замена прежней зависимости от per-item `CurrentOutline`. |
| `ResolvedCamera`, `HasRenderCamera` | Реально выбранная Camera и наличие пригодной ссылки; глобального fallback нет. |
| `IsHighlighted`, `IsSuppressedWhileHeld`, `IsShaderAvailable` | Готовность/причина held-suppression/поддержка URP shader. Depth-occlusion не превращается в отдельный gameplay state. |
| `RendererCount`, `UnsupportedRendererCount` | Число закешированных поддерживаемых частей и неподдерживаемых источников текущей геометрии. До lazy-инициализации значения могут быть нулевыми. |
| `SmoothedRendererCount`, `FallbackRendererCount` | Какие части используют private smoothed snapshot и какие raw source normals. |
| `RenderedPartCount` | Количество отправленных submesh draws последнего draw tick. Это число submissions, а не подсчёт видимых пикселей после depth test. |
| `RefreshHighlight()` | Явный refresh после изменения привязок/добавления камеры в runtime; не создаёт новый item-компонент. |

## Перенос прежней конфигурации

Прежний **InteractionFocusOutline на каждом предмете** и его Inspector удаляются при согласованном переходе. Совместимый пассивный компонент, `CurrentOutline`-wrapper или runtime-мигратор после удаления типа не предлагаются. Поэтому сначала сохраните исходные данные, затем обновляйте пакет.

1. **До обновления**, пока старый тип ещё доступен, архивируйте сцены/prefab и значения старых компонентов: enabled, Renderers, Outline Color, Width; сохраните соответствие предметам и ссылкам. Нельзя рассчитывать прочитать эти поля обычным API уже после удаления типа/meta.
2. Сопоставьте старое enabled с **Target.HighlightWhenFocused**, старый Renderers с **Target.OutlineRenderers**. Пустой список по-прежнему означает auto. Если нужна прежняя выборочная подсветка и предмет раньше не имел включённого outline, явно запишите false; новый default-true не является автоматической реконструкцией старого намерения.
3. Выберите общий стиль **явно на Highlighter наблюдателя**. Разные per-item цвета/толщины не могут одновременно превратиться в один общий стиль без изменения результата: решите конфликт, не полагаясь на first/last wins. Если сохранённый Width был `0.025`, а новый общий выбран `0.015`, это осознанная смена настройки, не автоматический перенос.
4. Удалите старые item-компоненты **до** обновления, имея их архив и план маппинга; после установки настройте новые поля Target и observer. Для контролируемого общего проекта допускается согласованная атомарная YAML-миграция записей компонентов/ссылок одновременно с заменой кода. Не оставляйте старые записи MonoBehaviour с уже удалённым script GUID как Missing Script.
5. Обновите свой код с `CurrentOutline`/AddComponent старого типа на Target-поля и диагностику **Highlighter.HighlightedTarget**. Назначьте Camera Override, если layout View/выходной камеры этого требует. Проверьте сцены и prefab variants отдельно.

Общий FrameworkPlayground переносится координатором вместе с canonical sample, импортированной копией и probe. Текущие два прежних предмета имели одинаковый cyan и Width `0.025`; их исходные данные архивированы, а новый observer явно настраивается на cyan/`0.015`. Это решение общей сцены, не скрытая авто-миграция пользовательских сцен. Модуль не пересохраняет сцены потребителя и не строит Player ради такого переноса.

## Проверка первого использования и камеры

Для ручной/CLI приёмки подготовьте два Targets с закрытыми static meshes и один Actor/View с Camera. После render настоящего кадра убедитесь, что выбранный предмет имеет видимый контур, а не только ненулевой диагностический счётчик. Поставьте непрозрачную стену между камерой и предметом: silhouette за ней должен исчезнуть по depth test.

Затем создайте второго наблюдателя с отдельной Camera и другим цветом/целью. Захватите кадр каждой камеры: в ней должна быть только её собственная focus-обводка. У первого наблюдателя очистите Camera Override и поставьте View без Camera, оставив в сцене другую tagged Main Camera: `HasRenderCamera=false`, draw не должен перейти в чужую камеру. Верните точную View Camera или явный Override, выполните RefreshHighlight и повторите render.

Проверьте переключение флага **Focus Outline** на Target false/true, grab/drop/transfer, отключение одного observer, Target disable/destroy, замену source mesh **вне фокуса**, readable/nonreadable mesh, Renderer.enabled/слой/culling mask и несколько частей. Геометрия остаётся тёплой при обычной смене focus/held, а Target false/disable/destroy освобождает её владение. Первое создание/refresh может выделять память; утверждение о прогретом пути должно сопровождаться отдельной GC-проверкой с положительным контролем. Новый Player/Addressables build этим описанием не подтверждён и для текущей задачи не выполняется.

Focus — существующий выбор Actor по конусу, default новых Actors/Setup. Raycast остаётся явным selectable режимом и не перенумеровывается. Обводка не заменяет выбор, стоимость, доступность action или input permissions. [Справочник Grab Action](GrabActionParameters.ru.md), [удержание и инерция](GrabComfort.md), [README](../README.md).
