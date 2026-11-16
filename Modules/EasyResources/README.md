# EasyResources — модуль EasyGameFramework

Ресурсы, кошельки, составные стоимости, резервы, предметный инвентарь и атомарные операции. Дополнительные сборки дают производство, торговлю, прогрессию, большие числа, проверку команд и UI Toolkit. Единая Resource Database хранит вложенные записи ResourceDefinition и все их параметры; SO описывают конфигурацию; количества и состояние операций живут в runtime-объектах.

Модуль входит в единый пакет `com.oreethehat.easy-game-framework`. Отдельно устанавливать прежний EasyResources или импортировать адаптеры из Samples не нужно. Короткие пространства имён и имена сборок сохранены.

## Первый запуск

1. Установите EasyGameFramework и откройте **Tools → EasyGameFramework → Setup** для общей настройки проекта.
2. Создайте **Create → EasyGameFramework → Resources → Resource Database**. В инспекторе базы нажмите **Add**, назовите запись Wood. Все параметры ресурсов редактируются внутри одного файла; по умолчанию ресурс целочисленный.
3. Добавьте **Resource Container** на пустой GameObject. В **Initial Contents** добавьте Wood с количеством 10. Лимиты по умолчанию выключены.
4. Нажмите Play: инспектор показывает **Available / Reserved / Total = 10 / 0 / 10**. Другой контейнер с тем же asset хранит отдельное количество.
5. Для предметов создайте **Resources → Inventory → Item** и добавьте **Inventory Container**. По умолчанию доступны 24 слота. Инспектор показывает заполненные слоты в Play и ошибки исходной вместимости до запуска.

Начальное содержимое, фильтры и лимиты проверяются вместе. Ошибка объясняет, какие Initial Contents / Slots / Storage Limits поправить. `ValidateConfiguration()` у обоих контейнеров позволяет выполнить ту же проверку из собственного инструмента; живое состояние и события не меняются. Это подготовительная проверка с временными коллекциями, не вызов для Update.

Готовые демо остаются Samples единого пакета: **Resources / Basics**, **Resources / Workflows**, **Resources / Benchmarks**. Basics даёт **13 PASS** (A = 6 Wood / 3 Stone; B = 3 Wood / 1 Stone; резервов нет), Workflows — **18 PASS**. Откройте соответствующую сцену из импортированной папки. После импорта команды **Tools → EasyGameFramework → Resources → Create Basics Scene / Create Workflows Scene** создают новую копию без перезаписи существующей. Пустая неизменённая сцена не требует предварительного сохранения. Демо выполняются автоматически, игрового ввода нет.

## Короткое API

Стоимость создаётся один раз или задаётся через **Resource Cost** asset. Повторяющиеся ресурсы суммируются, пустая стоимость означает бесплатную операцию. Неверные ссылки/отрицательные значения — ошибка конфигурации; недостаток средств — обычный `false` с `ResourceFailure`.

```csharp
using EasyResources;

var price = new ResourceCost(new ResourceAmount(wood, 4), new ResourceAmount(stone, 2));
// В игровом коде переиспользуйте price или ResourceCostAsset.Cost.
if (wallet.TrySpend(price, out var failure))
    CompleteOperation();
else
    ShowFailure(failure.ToString());
```

`ResourceContainer.Store` — тот же кошелёк, которым пользуются методы компонента. Для состояния без GameObject создайте `ResourceStore` и освободите его через Dispose. Store компонента освобождается самим компонентом при уничтожении; выключение сохраняет баланс.

Инвентарь и кошелёк участвуют в одной операции. Недостаток ресурсов, мест или допустимого веса оставляет оба состояния прежними:

```csharp
using EasyResources.Inventory;

bool crafted = inventory.Store.TryCraft(wallet.Store, price, outputItem, 1, out var failure);
// Несколько кошельков и выдач: переиспользуемый ResourceTransaction.
bool moved = plan.Clear().Transfer(source.Store, destination.Store, price).TryExecute(out failure);
```

`TryReserve(cost, out hold, out failure, options)` удерживает полную составную стоимость. `hold.TryCommit(out failure)` списывает остаток, `hold.Cancel()` освобождает его. Повторное завершение безопасно возвращает false. Для нескольких резервов и результата используйте `plan.Clear().Commit(a).Commit(b).Add(output, reward).TryExecute(out failure)`; отказ не тратит резервы. Одну транзакцию нельзя финансировать её же будущей выдачей.

Количество — неотрицательный `long` в наименьших единицах. `resource.ToUnits(1.25m)` переводит точно; `DecimalPlaces = 2` означает 125 raw units. Потеря точности и переполнение отклоняются. `FromUnits` и `Format` выполняют обратное преобразование. Не обрезайте float приведением к long.

## Сохранение и возобновление

EasySave владеет слотами, форматом хранения и I/O; EasyResources предоставляет независимые DTO и проверку состояния. Следующий фрагмент предполагает единый снимок кошелька и фазы его длительной операции:

```csharp
var state = wallet.CaptureState(includeReservations: true, nowMilliseconds: now);
var options = new ResourceRestoreOptions(
    resumeReservations: true, replaceActiveReservations: true, nowMilliseconds: restoredNow);
wallet.ValidateState(state, definitions, options); // Полная немутирующая проверка.
wallet.RestoreState(state, definitions, options);
if (!wallet.Store.TryGetReservation(savedHoldId, savedOwnerId, out var payment))
    RejectInconsistentOperation(); // Не резервировать и не списывать повторно.
```

По умолчанию Capture/Restore требуют отсутствие активных резервов. Для сохранения длительных операций включите их явно и задайте постоянный непустой OwnerId каждому резерву. `ValidateState` использует те же проверки, что RestoreState, возвращает планируемые counts, не меняет handles/ревизию/события и не является обещанием, что изменённые позднее данные пройдут повторную проверку.

Для нескольких кошельков `ResourceSnapshots.RestoreAll` выполняет атомарное восстановление. Затем восстанавливаются владеющие резервами очереди/исследования/доставки. Это не единая атомарная загрузка всех механик мира; ошибки позднего этапа должны оставаться видимыми. Полные границы и точные API: [сохранения и операции](Documentation~/SaveAndOperations.md).

## События и ограничения

`Changed` и `DetailedChanged` публикуют согласованное состояние всех участников после успешной операции. Наблюдатель может читать, но не изменять участников во время уведомления; следующие команды ставьте после его завершения. Исключение наблюдателя не отменяет подтверждённую операцию. Уничтожение контейнера из уведомления откладывает его освобождение до конца публикации.

Короткий `store.DeferNotifications(context)` объединяет события вложенных действий в одну итоговую ревизию. Балансы и резервы внутри меняются сразу. Явно подтвердите/отмените свой hold перед закрытием scope; Dispose не выбирает исход. Scope нельзя переносить через кадр или await. Снимки во время открытого scope запрещены. Чистая отмена нового резерва без итогового изменения не создаёт уведомления. Компенсация уже потраченного — отдельная операция, которая тоже может не пройти лимиты.

API хранилищ выполняется на создающем главном потоке Unity. Здесь нет измеренной задачи для Jobs/Burst: транзакции и уведомления сохраняют этот порядок. `ResourceBatch` ограничивает количество целых транзакций за Process. История отключена по умолчанию; явная вместимость включает ограниченный журнал.

## Сборки и проверки

| Сборка | Назначение |
|---|---|
| EasyResources.Runtime | Кошелёк, стоимость, резервы, транзакции, точные количества, снимки |
| EasyResources.Inventory | Слоты, стаки, уникальные экземпляры, свойства, атомарное создание предметов |
| EasyResources.Production | Очереди, потоки, доставка, возобновление именованных операций |
| EasyResources.Trading | Магазин, котировки, комиссии и явный кредит |
| EasyResources.Progression | XP, ранги, условия, исследования и престиж |
| EasyResources.LargeNumbers | BigInteger-количества и мост к обычному кошельку |
| EasyResources.Networking | Независимые от транспорта команды/полномочия/повторы и DTO репликации; не Netcode SDK |
| EasyResources.UI | Resource Panel на UI Toolkit |

Cross-module adapters находятся у добавляющего функцию модуля в `Integrations`, например Combat, Building, Interaction, Localization и Save. Ресурсный модуль не создаёт второй боевой или сохраняющий сервис. Конкретные SDK транспорта остаются дополнениями.

Единственный testable root — `com.oreethehat.easy-game-framework`. Тесты: `EasyResources.Tests.EditMode`, `EasyResources.Tests.PlayMode`, `EasyResources.Tests.UI`. [План проверки объединения](Documentation~/Unification.md) отделяет статические проверки staging от результатов после CUTOVER. Старые отчёты в Documentation~/Reports — исторические измерения, а не подтверждение нового общего пакета. Benchmarks сохраняют прежний объём работы и калибровку GC для сравнения.


Единая база, поиск ресурсов в любых компонентах, безопасное удаление и явный выбор базы награды: [ResourceDatabase.md](Documentation~/ResourceDatabase.md).
