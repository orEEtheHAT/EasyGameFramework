# Building + Resources

Штатная integration сборка EasyBuilding.EasyResources, без импорта Samples и старых package version guards. Добавьте BuildingResources, назначьте Resources и Containers (порядок списания), затем назначьте компонент в BuildingSystem > Cost Provider. Definition.Costs выбирает ResourceDefinition по stable ID; Amount — минимальные целые единицы.

BuildingResourceEconomy использует ResourceTransaction/ResourceReservation. Preview и Cancel не резервируют/не платят. Batch/delivery/refund/repair/upgrade используют тот же баланс. Для actor/team счетов реализуйте IBuildingResourceAccounts.GetStores(actor,output,out refundStore). При изменении конфигурации из кода вызовите RefreshConfiguration. Все резервы принадлежат конкретной операции и Cancel идемпотентен.

Building CaptureState сохраняет строительные квитанции, но не кошелёк. EasySave координирует полный снимок Building и Resources; повторный TryReplaceSnapshot никогда не оплачивает строительство заново. Tests находятся здесь и доступны общему testable пакету без импорта примера.