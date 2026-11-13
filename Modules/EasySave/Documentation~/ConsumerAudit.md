# Владение состоянием в объединённом комплекте

EasySave хранит сериализованные snapshots; живое состояние остаётся у owners. Штатные adapters в Integrations/EasySave используют новые немутирующие Validate APIs Combat/Input/Movement/Resources и полный Building local replacement. Точные сигнатуры и ограничения описаны в Integrations.md. Их исходники принадлежат профильным владельцам, Save не копирует их internal validation и не меняет чужие модули.

Input/audio/language/URP — Preferences. Combat/resources/buildings/interactions/player pose — Progress. В сохранении нет общего флага, который неявно переносит Preferences в игровой слот. Scene/menu lifecycle принадлежит приложению и EasyUI, не сериализуется автоматически.

EasyCore не нужен ради зависимости: собственные правила имени слота/формата/checksum и DateTime UTC имеют persistence-семантику. CancellationToken/IDisposable покрывают текущие операции; общий clock gameplay не подменяется UTC. Jobs/Burst не добавлены для managed сериализации/I/O.

Сохраняются реальные ограничения: внешние streamed archives и операции с named reservations нужно явно включать в композицию с их owners; arbitrary side effects не имеют гарантированного rollback. Стабильные keys задаёт приложение. Исторические Reports в модуле — архив прежних независимых пакетов, а не результаты Unity-проверки нового staging.