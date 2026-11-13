# Контракты EasySave

`SaveService(ISaveStorage storage, SaveOptions options = null)` создаётся на главном потоке с Unity SynchronizationContext. Все public вызовы и callbacks принадлежат этому потоку. Запрос с другого потока возвращает `WrongThread`. Настройки копируются; backend принадлежит приложению, сервис не закрывает внешний backend при Dispose.

## Участник

`ISaveParticipant`: `string Key`, `SaveScope Scope`, `int Version`, `int RestoreOrder`, `string Capture()`, `SaveValidation Validate(string payload)`, `Task RestoreAsync(string payload, CancellationToken cancellation)`.

Key задаётся автором композиции, стабилен между запусками и не зависит от instance ID, имени GameObject или порядка объектов в сцене. Properties фиксируются при Register. Один key уникален внутри области. Не регистрируйте состояние одной механики одновременно через два адаптера. Порядок Capture/Validate/Restore: RestoreOrder по возрастанию, затем key ordinal. Порядок регистрации не влияет на результат.

Capture и Validate не меняют живое состояние и не отменяют игровые операции. Закончите/отмените необходимые команды снаружи перед снимком. Validate проверяет всё, что Restore собирается применить. Сначала весь документ читается и проверяется, затем все миграции и все Validate; только после этого начинается Restore. Неизвестные/отсутствующие участники отклоняются. Если нужна новая опциональная механика, выполните явную миграцию документа в приложении; автоматическое пропускание отсутствующих данных здесь намеренно отсутствует.

`JsonSaveParticipant<T>` вызывает JsonUtility на главном потоке. Поддерживаются его DTO-правила: сериализуемые fields, без UnityEngine.Object references, полиморфного произвольного графа и Dictionary. Владелец задаёт версию и проверяет свой DTO. Есть синхронный Action<T> и асинхронный Func<T,CancellationToken,Task> Restore. Асинхронный Restore должен уважать token и оставаться на Unity main thread для Unity API. Сервис не может отменить побочные эффекты чужого callback, игнорирующего token.

JSON Capture вызывает provider ровно один раз. Отсутствующий DTO (`null`), ошибка сериализации или неподходящий JSON object envelope отклоняются в фазе Capture до обращения к storage: `CaptureFailed`, `Committed=false`, ключ в `FailedParticipant`; прежние primary и backup сохраняются. Непустой marker DTO с `{}` и допустимый default serializable struct разрешены. Игровой Validate при Save не вызывается. Это правило JSON-адаптера: собственный строковый формат обычного `ISaveParticipant`, включая допустимый пустой payload, сохраняет прежнюю семантику.

## Явная композиция и memory checkpoints

`RegisterAll(params ISaveParticipant[])` проверяет весь набор stable keys/версий/бюджетов до регистрации. При отказе ни один новый участник не добавляется. Возвращённый IDisposable удаляет только принадлежащие ему записи, даже если такой же объект был позднее зарегистрирован заново. SaveController принимает в явном списке как ISaveParticipant, так и ISaveParticipantSource; source создаёт адаптеры один раз при включении. FrameworkSaveBindings предоставляет такую композицию для обычной сцены без сканирования мира.

`CaptureSnapshot(SaveScope=Progress)` синхронно захватывает immutable сериализованные значения без I/O; при ошибке Capture бросает исключение, не отдавая частичный snapshot. `RestoreSnapshotAsync(snapshot,token)` возвращает SaveResult и использует тот же preflight/migrations/order, что LoadAsync. Scope зафиксирован в snapshot. Это checkpoints/tests, а не сериализация live Unity object graph.

## Миграции payload

`RegisterMigration(scope, key, fromVersion, Func<string,string>)` задаёт чистый переход N → N+1. Все необходимые звенья должны существовать; предел 256 переходов на payload. Мигрированный payload снова ограничивается размером и проходит Validate. Более новая версия отвергается. Файл-источник не переписывается; новый формат сохраняется только отдельным Save после успешной загрузки.

## Конкуренция и lifetime

Одна операция на экземпляр сервиса. Save/Load/List/Delete во время другой возвращают `Busy`, очередь отсутствует. Регистрации и миграции добавляются между операциями. `Unregister` во время операции удаляет только переданного участника и запрашивает отмену до последующих callbacks. `CancelCurrent` отменяет текущий запрос. `Dispose` идемпотентен, запрашивает отмену и закрывает сессию для новых вызовов; не блокирует главный поток ожиданием I/O.

SaveController создаёт собственную сессию в OnEnable, отменяет/закрывает в OnDisable (и при Destroy через OnDisable). Повторное включение создаёт новую сессию из defaults. Его Completed не отправляется из старой завершившейся операции в новую сессию. Для программной настройки используйте `Configure(configuration, participants)` на выключенном компоненте. Конфигурация и массив participants копируются/читаются без изменения исходных assets.

Завершение приложения не гарантирует async save: сохраните и дождитесь результата **до** вашего Quit/смены сцены. OnApplicationQuit только отменяет запрос; finalizer, скрытого DontDestroyOnLoad и записи «на всякий случай» нет.

До commit отмена возвращает `Cancelled`, не публикует новый слот. После атомарной замены файла backend возвращает `Committed=true` и успешный результат даже при одновременно отменённом token. Это позволяет отличить совершившуюся запись от отменённой. Dispose после commit также не отменяет уже сохранённые данные.

## Частичное восстановление

Ранний отказ не вызывает Restore. После входа в любой Restore выставляется `WorldMayHaveChanged`, поскольку callback может изменить мир перед исключением. При отказе/отмене далее результат `PartialRestore`: RestoredParticipants содержит только callbacks, успешно вернувшие управление; FailedParticipant — текущий callback, если он не завершился. При отмене между callbacks FailedParticipant пуст.

Автоматического rollback нет. Игровые события, награды, внешние службы и уничтоженные объекты нельзя надёжно откатить общим кодом. Игра определяет паузу/блокировку ввода, порядок загрузки сцен и recovery при PartialRestore. Повторная загрузка безопасна только если API владельцев идемпотентны; адаптеры не должны выдавать награду или списывать оплату как побочный эффект Restore.

## Метрики

SaveResult.Timings отдельно измеряет Capture, сериализацию контейнера, storage, Validate/миграции и Restore. Capture включает сериализацию DTO JsonUtility; Serialization — бинарный контейнер и SHA256 на worker. Storage — elapsed ожидание backend, включая durable flush, проверку старой копии, планирование и системный I/O. Restore включает ожидания async владельцев. Это elapsed milliseconds, не CPU time и не FPS. Для List метрики всего обхода не агрегируются.

Workflow выделяет память под DTO/строки/буферы; zero-GC не заявляется. Jobs/Burst не используются: здесь managed строки и I/O, массового численного участка с измеренным выигрышем не найдено.
