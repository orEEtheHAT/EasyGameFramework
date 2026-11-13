# Проверки модуля EasyInteraction

Тестовый root единого пакета: `com.oreethehat.easy-game-framework`. Production fixtures уже находятся в `Modules/EasyInteraction/Tests`; не копируйте их в Assets и не импортируйте адаптеры из Samples. Для Unity CLI используйте явный project path и атомарную общую блокировку из AGENTS.md. До CUTOVER Unity текущего проекта проверяет прежние пакеты, а не unified staging.

После CUTOVER: один scoped PlayMode filter `EasyInteraction`, затем EditMode filter `EasyInteraction.Tests.Editor`. Core, standard integrations и shared input тестируются без sample imports. Gallery HUD/first-run tests находятся в `Samples~/Interaction Gallery/Tests`; для scene test импортируйте Gallery и добавьте её сцену в Build Settings. Tests имеют TestAssemblies/UNITY_INCLUDE_TESTS guards; Editor tests ограничены Editor.

## Производительность

`Tools~/Performance/InteractionBenchmark.cs` — отдельный development-player стенд, не production assembly. Его можно скопировать в согласованную project-local профилировочную сцену; общие сборки и clean-project проверки координирует root. Нельзя выдавать старые raw results за замеры unified framework.

Стенд измеряет 16 selection scenarios, 3 API workloads и 9 сочетаний FPS/fixed step для физического удержания. Warmup/sample counts и конфигурация сохраняются в JSON. Основной GC meter — main-thread GC.Alloc ProfilerRecorder с положительным контролем new byte[512] и прогретым empty control. Schema2: allocationEvents >=0 означает измеренное число событий, -1 — unavailable/not measured. allocatedBytes=0 только при калиброванном нуле событий; для неизвестного размера ненулевых событий или unavailable записывается -1. Unavailable не считается passed. GC.Alloc sample.Value не используется как размер allocation.

Без --report создаётся новый файл EasyInteraction-benchmark-<UTC timestamp>.json в persistentDataPath; существующий путь --report не перезаписывается. CPU Stopwatch относится к отмеченным вызовам; `GC Allocated In Frame` для hold — весь процесс/кадр, не отдельный Grab. Профилирование влияет на CPU, поэтому сравнивайте одинаковую конфигурацию сборки.

## Дополнения

NGO smoke находится в `Addons~/Netcode/EasyInteraction/Tools~/NetworkSmoke`: server/client — разные процессы с loopback и отдельными JSON. XR regressions находятся в `Addons~/XR/EasyInteraction/Tests`. Они требуют реального SDK и отдельного подключения дополнения, не входят в baseline package tests. Дублирующая историческая XR-копия исключена из нового пакета.
