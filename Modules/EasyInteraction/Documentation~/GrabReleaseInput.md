# Основной Interact при удержании предмета

В модуле EasyInteraction единого EasyGameFramework основная кнопка Interact надёжно отпускает собственный active Grab даже во время движения мыши, потери raycast-фокуса или наведения на другой объект. Это исправление маршрутизации ввода; коэффициенты удержания, инерция и Break Distance не меняются.

Штатный InteractionInput вызывает единый `Actor.PressPrimary(out action, toggleInsteadOfHold)`. Приоритет: свой активный Grab, затем выбранная action; без выбранной цели сохраняется fallback активной операции. Нажатие не запускает другую world action после выбора Grab, даже если команда была отклонена. Условие «active Grab» учитывает сессию Actor, а не первый произвольный ActiveAction: Inspect не заслоняет удерживаемый предмет.

```csharp
// В обработчике input action после проверки разрешений reader:
if (actor.PressPrimary(out var action, toggleInsteadOfHold))
{
    pressedAction = action;
    pressedTicket = action ? actor.GetTicket(action) : default;
}
```

Для key-up используйте захваченные action/ticket и существующую политику Release/Cancel. Не выбирайте новую SelectedAction на отпускании. Смена контекста, rebind/device lifecycle и запреты модальных окон остаются ответственностью input reader. Сам primary command проверяет активность Actor и защиту от повторного входа, но не заменяет эти разрешения. При отсутствии локальной цели асинхронный CommandAdapter по-прежнему получает null press и может выбрать собственную удалённую операцию; в этом случае возвращённая action может быть null.

`actor.PressPrimary()` — короткий вызов без tracking. `actor.Press()` без action имеет ту же семантику. Для UI: `presenter.Activate()` или `presenter.ActivatePrimary(out action)`. Основная Prompt показывает Release удерживаемого предмета. Для явно выбранной строки списка, socket, use или secondary-команды сохраняются `actor.Press(action)` и `presenter.Activate(action)` — они не подменяются отпусканием Grab.

Штатный EasyInput adapter использует тот же command и находится в Integrations/EasyInput/EasyInteraction. Он принадлежит модулю EasyInput и сохраняет CanRead/context/ownership guards. Дополнительный импорт Samples или отдельная версия модуля не требуются.
