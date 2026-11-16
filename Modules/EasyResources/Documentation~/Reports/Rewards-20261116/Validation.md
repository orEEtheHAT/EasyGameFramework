# Resource rewards — 16 ноября 2026

Новые API в EasyResources.Runtime:

- `ResourceRewardRange(ResourceDefinition, long min, long max)` и `FromQuantity(ResourceDefinition, decimal min, decimal max)`.
- SO `ResourceRewardProfile`: `Ranges`, `ValidateConfiguration()`, `Roll(System.Random random = null)`, `Create(string name, params ResourceRewardRange[])`.
- `ResourceStore.TryGrant(ResourceCost, out ResourceFailure, ResourceOperationContext = default)` и такой же фасад `ResourceContainer`.

Инструкция: [Rewards.md](../../Rewards.md). Контракт согласован с координатором до импорта в `Briefs/EasyGameFramework/InteractionUX-20261116/Resources/Contract.md`; его сигнатуры сохранены.

| Проверка | Итог | Источник |
|---|---|---|
| Общая компиляция | completed, ошибок нет | [compile.json](compile.json) |
| Новые EditMode-тесты награды | 22/22 | [reward-editmode-result.json](reward-editmode-result.json) |
| Полный EasyResources EditMode | 160/160 | [editmode.json](editmode.json) |
| Полный EasyResources PlayMode, включая UI | 13/13 | [playmode.json](playmode.json) |

Регрессии покрывают обе включительные границы, дробные raw units, фиксированные/нулевые/пустые профили, детерминированный System.Random и Unity.Random, [0,long.MaxValue] и края Int64, отбрасывание неравномерного хвоста, ошибочный пользовательский RNG, отсутствие расхода RNG при поздней неверной строке, дубликаты/переполнение суммы Max, неизменность SO и независимость результатов. Проверены полный отказ по вместимости/весу/объёму/фильтру/переполнению, readonly/disposed/wrong-thread/null, согласованные события и reentry, повтор той же награды после отказа и уничтожение контейнера из финального события.

`PreparedGrantHasNoManagedAllocationAfterWarmupWithPositiveControl` прошёл. После 64 прогревочных выдач измерены 256 атомарных выдач двух ресурсов; GC.Alloc current-thread recorder со суммированием зарегистрировал **0** событий. Положительный контроль `new byte[1024]` зарегистрирован. Roll выделяет память для независимого снимка при событии; нулевые выделения на его создание не заявляются. Update/polling отсутствует. Новые CPU/Player показатели этим прогоном не измерялись.

FrameworkPlayground после тестов осталась единственной активной сценой, без изменений. Editor ready/stopped, autoRefresh enabled, runInBackground true. Блокировка resources-rewards-api освобождена после завершения тестов; отчёты оформлены вне неё. Другие модули, Playground, package metadata/версия и Git-публикация этим владельцем не изменялись. Интеграция смерти — ответственность EasyCombat; общий sample и независимая приёмка — координатора.
