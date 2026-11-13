# Building — EasyGameFramework

Строительство в Unity 6.3 URP: один Building Definition задаёт префаб и правила; Building Placer показывает превью и подтверждает установку через тот же валидатор. Модуль входит в единый пакет EasyGameFramework, отдельно устанавливать прежние Easy-пакеты и импортировать production-адаптеры не нужно.

| Сценарий | Настройка Definition |
|---|---|
| Свободно | Placement = Free |
| Только внутри участка, без сетки | Free + Require Zone, совпадающий Zone Id |
| По сетке | Grid + Grid Size; зона добавляется независимо |
| В сокет | Socket + совпадающий Socket Type |

Начните с [первого запуска для дизайнера](Documentation~/DesignerFirstRun.md). Полное [руководство](Documentation~/Guide.md) содержит API сессий, batch, строительства, истории и пространств. [Снимки мира](Documentation~/Snapshots.md) описывают публичный контракт для EasySave.

Включённые интеграции: [Resources](../../Integrations/EasyBuilding/EasyResources/README.md), [Combat](../../Integrations/EasyBuilding/EasyCombat/README.md), [Feedbacks](../../Integrations/EasyBuilding/EasyFeedbacks/README.md). Input/UI/Interaction и Save используют свои интеграции с Building. У каждого игрока один BuildingActor и общая сессия на мир; пользовательский runtime state не записывается в SO.

Расширенные геометрические инструменты (2D, ячейки, Tilemap, voxel rendering, paths, Terrain) остаются отдельными сборками модуля. XR, AR и TCP transport example расположены в Addons~ в корне пакета и не компилируются в базовом наборе. TCP — пример транспорта, не готовая интеграция Unity Netcode. Domain snapshots/change sets и transport-neutral authority API не требуют сетевого SDK.

Меню авторинга: Assets > Create > EasyGameFramework > Building; GameObject > EasyGameFramework > Building; Tools > EasyGameFramework > Building. Автоматических welcome popups нет.