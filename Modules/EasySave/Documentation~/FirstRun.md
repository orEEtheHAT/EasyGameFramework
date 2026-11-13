# Первый запуск EasySave в EasyGameFramework

1. Установите единый EasyGameFramework и штатные зависимости через общий Setup координатора.
2. Для минимального изолированного примера импортируйте root sample EasySave First Run: `Modules/EasySave/Samples~/First Run`. Сохраните открытую Untitled сцену, затем Tools → EasyGameFramework → EasySave → Create First Run Scene.
3. Откройте созданную сцену и Play: F5 → Space → F9 возвращает монеты, не размер текста. F6/F10 отдельно сохраняют/загружают preferences. New Input System, новая временная папка на Play, working saves не используются.
4. Для полноценного межмодульного маршрута используйте root Framework Playground. Явные actor IDs/catalogs/SaveWorkbench/GraphicsSettings назначает builder; приложения вызывают lab.Save/Load и отдельно lab.SavePreferences/LoadPreferences.
5. Проверяйте SaveResult.Success/Error/Committed/RecoveredFromBackup. PartialRestore требует решения приложения; глобального rollback нет. Для конфигурации собственной сцены см. FrameworkSaveBindings и README.

После CUTOVER: EditMode EasySave.Tests + EasySave.Integration.Tests + EasySave.Settings.Tests, PlayMode EasySave.PlayMode.Tests. Production tests не требуют импортировать adapter samples. Root добавляет только единый com.oreethehat.easy-game-framework в manifest.testables, проводит общий clean-install/Player/perf сценарий. Исторический отдельный package archive не подтверждает объединённый пакет.