# Building + Combat

Штатная integration сборка EasyBuilding.EasyCombat. На корень строительного prefab добавьте EasyGameFramework > Building > Integrations > Building Health. Health и PlacedBuilding добавляются автоматически; здоровье настраивается через Combat.

BuildingHealth сохраняет health state через IBuildingStatePart, удаляет разрушенную постройку, поддерживает оплачиваемый repair и перенос доли повреждения при upgrade. Repair не меняет здоровье внутри уже исполняющейся боевой транзакции. CanRestoreBuildingState проверяет данные/конфигурацию без запуска gameplay; оплата и награда не повторяются при snapshot restore. Остальные combat-состояния принадлежат модулю Combat.

Runtime и Tests включены в framework, дополнительных sample imports нет.