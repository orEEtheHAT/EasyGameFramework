# Транзакционная оплата строительства

В штатном сценарии используется BuildingResources из Integrations/EasyBuilding/EasyResources. Назначьте ResourceDefinition и ResourceContainer, затем этот компонент в Cost Provider на BuildingSystem. Контейнер хранит единственный runtime баланс, SO — только конфигурацию. В Costs у Definition выберите ресурс; Amount хранит минимальные целые единицы (long).

```csharp
// Для обычного одного контейнера используйте настроенный BuildingResources.
world.Economy = buildingResources;
var session = actor.GetSession(world);
session.Select(definition);
var preview = session.Check(request); // без резервов и списания
session.TryConfirm(request, out var placed, out var result);
```

Для явной конфигурации из кода:

```csharp
world.Economy = new BuildingResourceEconomy(
    new[] { playerContainer.Store, sharedContainer.Store },
    resourceDefinitions, refundStore: playerContainer.Store);
```

IBuildingResourceAccounts позволяет выбирать разрешённые кошельки по actor/team. Повторное указание того же Store не увеличивает баланс. Резерв захватывает всю сумму по доступным счетам; успешный commit публикует согласованные итоги. Ошибка отменяет резервы, повторный Cancel безопасен. Кредиты/возвраты учитывают capacity вместе со списанием. Существующие batch, доставка материалов, repair/upgrade и undo используют IBuildingEconomy.

Check и Cancel не создают резервов. Подтверждение ещё раз проверяет препятствия/зону/сокет и платёжные условия перед commit; успешное одиночное размещение потребляет выбор. Восстановление локального снимка через TryReplaceSnapshot не вызывает экономику и не повторяет награды. Snapshot Building и snapshot ResourceContainer координирует EasySave.

Расширение для собственного поставщика: IBuildingEconomy.CanApply(in BuildingBill,out reason), TryReserve(in BuildingBill,out IBuildingCostReservation,out reason); reservation.TryCommit(out reason) / Cancel(). False commit означает отсутствие списания; исключение после фактического необратимого списания нарушает контракт. IBuildingCostProvider остаётся компактным интерфейсом для специализированного одиночного размещения; основной путь фреймворка — IBuildingEconomy.