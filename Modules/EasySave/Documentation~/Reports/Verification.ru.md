# EasySave 0.1.0 — проверка от 13 ноября 2026

Реализован самостоятельный embedded UPM-пакет `com.oreethehat.easy-save`. Namespace EasySave, базовые Runtime/Editor сборки без зависимости от других Easy-модулей; отдельные импортируемые адаптеры. Локальный Git-репозиторий, без push, tag, лицензии от имени автора или публикации.

## Реализовано

- Save/Load именованных слотов, QuickSave/QuickLoad, список и метаданные, явное удаление, unscaled autosave.
- Раздельные Progress и Preferences; явные стабильные участники; deterministic order; payload versions и цепочки миграций; полная preflight в пределах validator владельца до первого Restore.
- Асинхронный worker I/O, bounded binary container v1/SHA256, pending/atomic replace/backup recovery; межэкземплярная файловая блокировка и атомарное скрытие удалённого слота.
- Главный поток для Unity callbacks, Busy/Cancelled/Committed/PartialRestore, lifecycle Disable/Destroy, defaults SO без runtime-изменений, Inspector diagnostics, короткий first-run.
- 8 owner adapters + отдельный EasySettings adapter, независимый First Run с New Input System и Windows verification Player.

## Фактические результаты

| Среда | Результат | Доказательство |
| --- | --- | --- |
| Games Factory / Unity 6000.3.22f1 | 43/43 EditMode (33 базовых + 10 адаптеров) | verified-editor.json |
| Games Factory / PlayMode | 5/5 lifetime/autosave/Inspector | verified-playmode.json |
| Чистая установка из tgz, без других Easy-модулей | 33/33 EditMode и 5/5 PlayMode | TestEdit.xml, TestPlay.xml |
| Windows x64 Mono Development Player чистого проекта | Build Succeeded, 0 errors; процесс exit=0; 30 checks, 9 записанных замеров | clean-build.json, Player-process.json, player-profile.json |
| Editor smoke/profile | 30 checks, 9 записанных замеров | editor-profile.json |
| First Run / реальные sample InputActions | Space/F5/F9/F6/F10 прошли через синтетическую Keyboard New Input System; 11 coins / text size24, Success | first-run-input.json |
| Визуальный осмотр | 1280×720, все инструкции/результат/путь видны; исходная EasySuiteLab восстановлена | first-run.png, first-run-scene-before/after.json |

Установочный sample действительно импортирован через Package Manager Sample API из `.tgz`, а не включён за счёт исходников Games Factory. Проверочный проект находится в `Audits/EasySave/CleanProject`; это отдельная install-проверка, не повседневный проект. Запуски Editor проверяли существующие PID/пути, второй Games Factory Editor не запускался. Все команды/импорт общего Editor выполнялись с атомарной общей блокировкой; чужие пакеты этим чатом не изменялись.

Предыдущие неуспешные диагностические прогоны сохранены в Audits/EasySave (отмена при общей compile error; первый Settings external-resize тест). Они не засчитаны в итог. Для First Run установленное Unity ограничение additive creation из Untitled обработано понятной инструкцией Save As; чистый harness сохраняет только свою bootstrap scene. Синтетический keyboard тест временно использовал копию InputSettings для unfocused Editor, затем восстановил настройки и сцену.

## Профиль

Медиана трёх измерений после одного прогрева, на каждом из трёх размеров. Малый payload: 32 characters, средний:1024, большой:8192 на участника. Milliseconds; локальный Windows запуск с другими рабочими процессами, это не обещание производительности на другом устройстве.

| Участники | Bytes | Capture | Encode | Storage write | Storage read | Decode | Validate | Restore |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 4 | 370 | 0.013 | 0.022 | 15.463 | 5.392 | 0.030 | 0.029 | 0.010 |
| 128 | 136420 | 2.271 | 4.418 | 19.588 | 5.779 | 3.644 | 3.557 | 2.818 |
| 512 | 4215780 | 56.866 | 89.053 | 187.163 | 6.292 | 90.207 | 80.322 | 58.395 |

Capture включает JsonUtility DTO на main thread; Encode/Decode — managed binary/SHA256 на worker. Storage включает планирование/ожидание Unity continuation, durable flush и проверку прежнего primary: это elapsed, не чистое время диска. Validate и Restore остаются main thread. Большой ~4 MiB снимок требует десятков milliseconds на main thread; для бесшовной игры следует уменьшать снимок/частоту и выбирать допустимую игровую границу. Zero-GC не заявляется, Jobs/Burst не добавлены без подходящего измеренного участка. Подробные per-iteration значения сохранены.

## Границы результата

- Проверены Windows desktop/local filesystem, Unity 6000.3.22f1, Mono. IL2CPP, mobile, Linux/macOS, облако/консоли и WebGL не заявлены. LocalFileStorage на WebGL отказывает сразу. Browser integration должна отдельно решить и backend, и стратегию выполнения сериализации.
- Тестировались повреждение/обрезание primary, backup, незакоммиченный pending, cancellation и lock contention; реального отключения питания/отказа диска не проводилось. SHA256 — целостность, не anti-cheat/шифрование. Полной power-loss durability произвольной файловой системы не обещаем.
- Rollback произвольных side effects отсутствует. PartialRestore честно указывает завершённых/неуспешного участника и возможность мутации. Игра владеет паузой мира/сценами и восстановлением после такого отказа.
- BuildingRecords поддерживает только resolved worlds без RedeemedParcels/missing/deferred state и явно отклоняет неподдерживаемое. Для полного local-world replacement нужен public API владельца.
- Combat/Input/Movement/Building adapters требуют полного read-only validator приложения: текущие владельцы не публикуют все internal prepare checks. Их wiring скомпилирован, но полная игровая интеграция со всеми активными casts/bindings/locomotion не объявляется проверенной. Native roundtrip-тесты проведены для Resources, Feedbacks, BuildingRecords и Settings; Interaction/Localization используют готовый owner API, но end-to-end мира здесь не добавлялся.
- Payload migrations не преобразуют автоматически полный набор участников или неизвестную версию контейнера. Эти случаи fail closed и требуют явной внешней конверсии/следующего API.
- Регрессия раннего EasySettings Restore(false) при внешнем resize выявлена здесь и исправлена владельцем EasySettings. Итоговый адаптер использует исправленный public API без дополнительного observer и сохраняет фактический экран; тест без Refresh прошёл.

Остались отдельным этапом: общая миграция EasyGameFramework, полные prepare/validate/replace API старых владельцев, дополнительные платформенные backend, release/push/tag. В проверенной самостоятельной области EasySave требуемая реализация и локальные проверки завершены.