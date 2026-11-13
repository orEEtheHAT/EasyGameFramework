# Первый запуск Building

Требуется установленный EasyGameFramework в Unity 6.3 URP с New Input System. Общие зависимости и установка — через единый Tools > EasyGameFramework > Setup.

## Рабочий пример за несколько минут

1. В Package Manager у EasyGameFramework импортируйте пример Building / Getting Started. Он содержит только демонстрацию; production-интеграции уже доступны.
2. Выполните Tools > EasyGameFramework > Building > Create Getting Started Scene. Будут созданы редактируемые ассеты и сцена в Assets/EasyGameFramework Examples/Building. Если папка уже содержит сцену, команда откроет её, не затерев правки.
3. Нажмите Play и наведите мышь на землю. Зелёный блок допустим, красный сопровождается причиной отказа. R/Q поворачивают, ЛКМ подтверждает, ПКМ/Esc отменяют, X удаляет объект под курсором.
4. 1 — свободно; 2 — свободно только в синем участке (положение не округляется); 3 — сетка внутри участка; 4 — бирюзовый сокет или верхний сокет поставленного блока; 5 — платная установка за 3 wood.
5. В Resource Container начинается 30 wood. Наведение и отмена не меняют баланс; подтверждение списывает ровно 3. После успешной установки выберите постройку снова. UI показывает тот же баланс EasyResources, отдельного demo-кошелька нет.

Ожидается: произвольная точка в зоне сохраняется в режиме 2; режим 3 округляет координаты; весь footprint должен помещаться в зоне. За пределами участка, в занятом сокете, при препятствии или нехватке wood подтверждение блокируется. Смещение зоны или появление препятствия после зелёного превью повторно проверяются в момент commit.

## Собственный префаб

1. У префаба сделайте корень со Scale (1,1,1), pivot снизу, дочернюю геометрию с коллайдером. Не помещайте BuildingSystem/BuildingSpace внутрь префаба.
2. Выберите префаб и Assets > Create > EasyGameFramework > Building > Definition From Selected Prefab. Bounds подгоняются по мешам; проверьте их в инспекторе.
3. Создайте Building World через GameObject > EasyGameFramework > Building. Назначьте определение в Starting Building у Building Placer и камеру в View Camera. У земли должен быть коллайдер; по умолчанию поверхность обязательна, препятствия проверяются, цена бесплатная.
4. Для участка добавьте Building Zone, задайте размер/позицию и Zone Id. На определении включите Require Zone и тот же ID. Placement оставьте Free, если округление не нужно.
5. Для цены добавьте Building Resources на мир, назначьте ResourceDefinition и ResourceContainer, сам адаптер — в Cost Provider у BuildingSystem. В Definition выберите Paid и ресурс в Costs. Количество — минимальные целые единицы ресурса.

Для FPS назначьте камеру EasyMovement/ECM2 и BuildingActor игрока в Placer, включите Aim From Screen Center, Reach около 6. Настройте встроенный EasyBuildingInput из модуля Input: UseFirstPersonPreset до Play, затем выбор определения и Enter(). Окна EasyUI используют ту же сессию. Не создавайте вторую сессию в собственном меню.

Ошибки настройки видны в инспекторах и Tools > EasyGameFramework > Building > Scene Diagnostics. Сложные правила и compound footprints раскрываются по мере необходимости.