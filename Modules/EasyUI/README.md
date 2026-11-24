# EasyUI в EasyGameFramework

[Роли, шейдеры и параметры чтения](Documentation~/Typography.ru.md)

Окна, фокус, диалоги и круговые меню на uGUI и New Input System. Один WindowManager управляет UI игрока. Все интеграции с другими модулями уже поставляются в Integrations/EasyUI; импорт адаптеров не нужен.

## Первый запуск

1. Откройте Tools → EasyGameFramework → Setup и устраните диагностические ошибки.
2. В новом проекте один раз выполните Window → TextMeshPro → Import TMP Essential Resources. В Samples импортируйте UI Getting Started. Откройте GettingStarted.unity внутри импортированного примера и нажмите Play.
3. I / Start открывает инвентарь; Settings → Apply settings открывает подтверждение. Стрелки / D-pad / левый стик выбирают, Enter / A подтверждает, Esc / B возвращает на один уровень.
4. Tab / RB переключает круг; удержание R / LB открывает его до отпускания. Мышь или стик выбирают сектор. Центр и недоступный сектор ничего не выбирают.
5. Space увеличивает игровой счётчик только после закрытия UI и отпускания удерживаемого ввода. Закрытие ребёнка сохраняет запрос ввода родителя.

Команда Tools → EasyGameFramework → UI → Create Getting Started Scene доступна после импорта этого демонстрационного sample. Она создаёт отдельную копию примера.

## Свои окна

GameObject → EasyGameFramework → UI → Window Manager создаёт менеджер. Назначьте EasyInputWindowProvider с PlayerInputSettings и EventSystem игрока; один UI-модуль на систему событий. GameObject → EasyGameFramework → UI → Window создаёт панель. Добавьте обычные uGUI Selectable, сохраните prefab и создайте WindowDefinition. Policy определяет модальность, ограничение игры, курсор и Back. SO содержит конфигурацию; состояние и запросы принадлежат экземплярам.

```csharp
WindowHandle inventory = ui.Open(inventoryDefinition);
WindowHandle settings = ui.Open(settingsDefinition, inventory);
settings.Close();  // Освободит только ребёнка.
ui.Back();         // Один текущий получатель; операция/rebind отменяется раньше окна.
```

```csharp
var handle = ui.Open(confirmDefinition, inventory);
var result = await handle.Window.GetComponent<ConfirmationDialog>().Ask("Apply changes?");
// Confirmed либо Cancelled, включая отключение/уничтожение родителя.
radial.UpdateItem("build", "Build", available: true, icon: buildIcon);
```

Для стандартного набора используйте Tools → EasyGameFramework → UI → Create Integration Windows. Фабрика создаёт семь definitions: действия/круг действий, каталог/круг построек, общие настройки, аудио и графика. Это authoring-префабы, а не отдельная игровая сцена.

```csharp
var window = IntegrationWindowSet.InstantiateWindow(set.Settings, playerUiRoot);
var settingsView = window.GetComponent<IntegratedSettingsWindow>();
settingsView.Bind(translations);            // LocalizedCatalogCache, null допускает fallback.
settingsView.BindGraphics(settingsController);
window.GetComponent<WindowFeedbacks>().ContextProvider = playerFeedbackContext;
ui.Open(window);
```

InputSettingsWindow владеет только представлением и вызывает EasyInput; аудио вызывает FeedbackSettings, язык — Localization, графика — SettingsWindow.Bind(SettingsController). Графический controller живёт независимо от окна и сам управляет подтверждением/откатом. Capture/Restore и запись через EasySave остаются у соответствующих владельцев. Дочерние окна получают отдельные UI-контексты, обратную связь и переводы до открытия.

## Текст и шрифт

Штатные окна и шаблоны используют TextMeshProUGUI. Пакетный шрифт содержит латиницу, кириллицу и цифры. Его запасной атлас содержит распространённые знаки интерфейса, валюты и стрелки.

Чтобы поменять шрифт для набора окон, назначьте TMP Font Asset в поле Font у WindowManager перед открытием. Шрифт получат созданные в коде подписи и подписи префабов, использующие пакетный шрифт. У подписи со своим явно выбранным ассетом сохранится её шрифт. Добавьте Default Font из Modules/EasyUI/Runtime/Resources/EasyUI в список Fallback Font Assets своего шрифта, чтобы использовать пакетные глифы при отсутствии нужных символов.

Чтобы создать подпись из кода, после размещения объекта вызовите `UIText.Add(labelObject, "Ресурсы: 128 ₽")`. Для поля ввода и выпадающего списка используйте TMP_InputField и TMP_Dropdown. Back сначала снимает фокус редактирования или сворачивает открытый список. В ConfirmationDialog.Message назначьте TMP-подпись. Для адаптеров ResourceBalanceLabel и LocalizedWindowText используйте TextMeshProUGUI на том же объекте.

Пакетные атласы запечены из DejaVu Sans. Лицензия расположена в Runtime/Fonts.

## Интеграции и владение

- InteractionMenuWindow.Bind(presenter) показывает актуальные binding/glyph/toggle через InteractionPresenter. Select выбирает действие; ActivateSelected/ReleaseSelected предназначены для явных press/hold-кнопок. Back сначала отменяет активную операцию.
- BuildingCatalogWindow.Bind(placer,catalog) всегда использует текущую Placer.Session. UI не создаёт и не Dispose заимствованную сессию. Добавление RadialMenu на тот же объект переключает представление на круг. CloseOnSelection=true по умолчанию.
- BuildingCatalogPresentation.Bind(resources,cache) показывает авторскую базовую цену и доступные actor accounts. Итоговую цену размещения с модификаторами вычисляет Building. После замены источников вызвать RefreshSources().
- ResourceBalanceLabel.Presentation принимает LocalizedResourceLabelProvider. У одной TMP-подписи один писатель; не добавляйте LocalizedWindowText поверх того же ресурсного label.
- WindowFeedbacks.ContextProvider принимает IFeedbackContextProvider, например PlayerFeedbackContext. UI не выбирает gamepad самостоятельно.
- Для UI без PlayerInputSettings есть StandaloneInputProvider с GameplayActions/GameplayGates. Его контракт описан в Documentation~/InputProvider.md.

Состав кругового меню обновляется через SetItems, отдельные подписи/значки/доступность — UpdateItem без перестроения. Выбранный ID сохраняется при перестановке. Большие наборы используют список и WindowScrollIntoView. Скрытые окна сцены остаются у владельца; prefab-экземпляры менеджера освобождаются при закрытии. Быстрые Close/Open, callbacks с CloseAll/reopen, отключение и уничтожение защищены от чужих освобождений и повторного подтверждения.

## Проверки

Корневой testables: com.oreethehat.easy-game-framework. Fixtures находятся в Modules/EasyUI/Tests: стек/фокус/input fences, реальные Input/Interaction/Building/Resources/Localization/Feedbacks, death/restore/approach, reentry, cleanup вложенного аудио и графики. Production Samples для тестов не импортируются. Setup имеет отдельные Editor tests. Unity-прогон объединённого дерева выполняется после CUTOVER; исторические отчёты прежнего пакета не являются проверкой новой структуры.

Screen Space Overlay и uGUI остаются основным сценарием. VR/world-space UI и сохранение стека окон не предоставляются. У невыбранных Interaction actions без событий доступность проверяется с частотой 10 Hz; строки не пересоздаются без изменения состава. Jobs/Burst для работы с Unity UI не используются.
