# EasyCore — модуль EasyGameFramework

Проверка чисел, явные источники времени и владение освобождаемыми ресурсами.
Доступен в едином пакете **EasyGameFramework**; отдельная установка Core не нужна.
Для собственного asmdef добавьте ссылку на `EasyCore.Runtime`, в коде — `using EasyCore;`.
Объект-менеджер, SO и регистрация сервисов не требуются.

## Три обычные задачи

Проверить входное значение, сохранив ограничения предметной области у вызывающего:

```csharp
bool validDamage = Numeric.IsPositive(damage);
bool validPosition = Numeric.IsFinite(position); // Vector3: все компоненты
bool validDuration = Numeric.IsNonNegative(seconds); // float или double
```

Выбрать часы для deadline и проверить тот же сценарий без ожидания:

```csharp
// Создать один источник для времени жизни операции, в Start/Update:
IClock clock = new UnscaledClock();
double deadline = clock.NowSeconds + 15;
// В последующих Update: bool expired = clock.NowSeconds >= deadline;

// В тесте вместо Unity-источника:
var manual = new ManualClock();
IClock testClock = manual;
double testDeadline = testClock.NowSeconds + 15;
manual.Advance(15);
bool expiredInTest = testClock.NowSeconds >= testDeadline;
```

Освободить свои ресурсы в обратном порядке:

```csharp
using (var lifetime = new DisposableScope())
{
    var stream = lifetime.Own(new System.IO.MemoryStream());
    lifetime.Defer(() => stream.Flush());
} // сначала Flush, затем stream.Dispose
```

Для подписки: сразу после `publisher.Changed += handler` зарегистрируйте
`lifetime.Defer(() => publisher.Changed -= handler)`. Создавайте новый scope
в каждом OnEnable и освобождайте в OnDisable. В Unity callback обработайте
AggregateException через Debug.LogException, если очистка может бросать исключения.

## Границы

| API | Семантика |
| --- | --- |
| Numeric.IsFinite(float/double/Vector3) | Отвергает NaN/Infinity; Vector3 проверяется по компонентам, не по длине |
| Numeric.IsPositive(float/double) | Конечное значение строго > 0 |
| Numeric.IsNonNegative(float/double) | Конечное значение >= 0, включая -0 |
| IClock.NowSeconds | Секунды одного явно выбранного источника |
| ScaledClock | Time.timeAsDouble, зависит от timeScale |
| UnscaledClock | Time.unscaledTimeAsDouble, работает при timeScale=0 |
| ManualClock(startSeconds=0), Advance(deltaSeconds) | Ручное неубывающее конечное время; invalid/overflow без мутации |
| DisposableScope.Own / Defer | Владение ссылочным IDisposable / однократная очистка |
| DisposableScope.Dispose | LIFO, попытка очистить каждый ресурс, затем AggregateException при ошибках |
| DisposableScope.IsDisposed / Count | Закрыт ли scope / число регистраций; Count=0 до callbacks |

Numeric не назначает clamp, fallback, единицы или epsilon. Конечные компоненты
не гарантируют конечную длину огромного вектора.

Unity-часы — main-thread PlayMode, scaled после Awake. В FixedUpdate читается
fixed time; один deadline нельзя считать попеременно в FixedUpdate и Update.
Unscaled frame time не идёт внутри кадра и во время Editor pause. Это не realtime,
UTC или timestamp сохранения. ManualClock не потокобезопасен; при очень большом
NowSeconds малое приращение может округлиться к прежнему значению.

DisposableScope используется на одном потоке. Null, self, duplicate reference и
регистрация после Dispose отклоняются без перехвата владения. Не создавайте циклы
и не освобождайте переданные ресурсы извне. Повторный/reentrant Dispose безопасен;
неудачная очистка не повторяется. Own/Defer и ошибки могут выделять память.
Числовые predicates, чтение часов и валидный ManualClock.Advance не создают
managed-объектов; новые замеры общего пакета выполняются после CUTOVER.

## Первый запуск

На странице **EasyGameFramework** импортируйте sample **Core / Getting Started**.
Команда **Tools > EasyGameFramework > Examples > Core > Create First Run** создаёт
пример в текущей сцене с поддержкой Undo. Включите Play: Console и Inspector
покажут **8/8 checks passed**. Камера и настройки ввода примеру не нужны.

- [Первое использование и тесты](Documentation~/FirstRun.ru.md)
- [Потребители и границы общего кода](Documentation~/Consumers.ru.md)
- [Статус проверок текущего этапа](Documentation~/Reports/Verification.ru.txt)

В Runtime нет зависимостей от игровых модулей или Editor, глобального контейнера,
игрового цикла и Jobs scheduler. Editor-помощник находится только в sample.
