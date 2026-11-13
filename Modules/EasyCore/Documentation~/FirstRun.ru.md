# Первое использование в EasyGameFramework

1. Установите единый пакет **EasyGameFramework** и выполните его общую инструкцию
   установки зависимостей. Core — встроенный модуль, не отдельный package.json.
2. Для своего asmdef добавьте `EasyCore.Runtime`; используйте `using EasyCore;`.
   После компиляции доступны Numeric, IClock, ScaledClock, UnscaledClock,
   ManualClock и DisposableScope без дополнительной настройки.
3. Для запуска примера импортируйте **Core / Getting Started** из Samples
   на странице EasyGameFramework в Package Manager.
4. Выполните **Tools > EasyGameFramework > Examples > Core > Create First Run**.
   Команда добавляет один GameObject в текущую сцену, действие поддерживает Undo.
5. Включите Play Mode. Console: **EasyCore first run: 8/8 checks passed.**
   Inspector компонента показывает тот же результат и объясняет ошибки.
6. Остановите и снова включите Play Mode. Результат должен повториться.

Пример не требует полей настройки, камеры или игрового ввода. Сам Core не обращается
к ECM2/URP/Cinemachine/VFX Graph/Input System; гарантированные зависимости
единого комплекта настраиваются общей инструкцией.

Если namespace не найден, дождитесь компиляции и проверьте ссылку asmdef.
Если меню отсутствует, импортируйте sample с его Editor-папкой.
Ручной вариант: пустой GameObject, **Add Component > EasyGameFramework > Examples >
Core First Run**. Для неактивного компонента Inspector объяснит, почему Start
ещё не запущен. Не импортируйте вторую копию EasyCore.Samples.

## Тесты после CUTOVER

Общий manifest содержит один testable root `com.oreethehat.easy-game-framework`.
В **Window > General > Test Runner** запустите `EasyCore.Tests.EditMode`
(41 test cases) и `EasyCore.Tests.PlayMode` (3). Import Samples для этих тестов
не нужен. Тестовые сборки используют TestAssemblies; EditMode ограничена Editor.

Проверки охватывают IEEE-границы, векторы, invalid/overflow без мутации ручных часов,
LIFO, повторный/reentrant Dispose, nested cleanup failures, rejected ownership,
отписки, disable/re-enable/destroy и timeScale=0. TimeScale восстанавливается в finally.

До CUTOVER staging не импортирован; здесь указан план, а не успешный Unity-прогон.
Координатор проводит общие clean-install/Player/perf проверки. Helper в
`Modules/EasyCore/Tools~/Verification` подготовлен для включения в эту проверку.
Результаты старого отдельного пакета лежат в `Reports/StandaloneBaseline`
только как исторические данные.
