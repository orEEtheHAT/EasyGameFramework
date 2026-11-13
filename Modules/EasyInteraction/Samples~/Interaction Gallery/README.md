# Interaction Gallery

Демонстрация модуля EasyInteraction в EasyGameFramework. Production-интеграции уже встроены; импортируется только сцена, prefabs, demo runtime и sample HUD.

После импорта откройте InteractionGallery.unity и нажмите Play. E выполняет основной Interact или отпускает свой предмет, Q отменяет. F1–F4 выбирают станции, 1–6 меняют способ поиска. C открывает меню, Z переключает список/круг. R+мышь вращает предмет, колесо меняет расстояние, G бросает. Дверь, carry item, socket, animation, sequence, analog handle и cooperation показывают отдельные behaviors.

Пересоздание: Tools > EasyGameFramework > Interaction > Create Interaction Gallery. Это стендовая камера с New Input System; для игрового FPS выберите готового EasyMovement player и выполните Set Up Selected Player — дополнительные samples для адаптеров не нужны.

Tests/GalleryHudTests проверяет список/круг/paging. GalleryFirstRunTests использует настоящую сцену и Keyboard events; добавьте импортированную сцену в Build Settings перед этим отдельным first-run прогоном. Sample UI не является prerequisite основного test suite.
