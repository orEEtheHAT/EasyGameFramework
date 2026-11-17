# Один Health: статусы и отклик

В Health находятся две независимые опции **Enable Statuses** и **Enable Health Feedbacks**, обе включены по умолчанию. Для типового врага достаточно Health и коллайдера; добавлять StatusController/HealthFeedbacks не нужно.

```csharp
health.ApplyStatus(poison, attacker);
health.StatusesChanged += RefreshStatusHud;
int count = health.StatusCount;
float speed = health.GetStatMultiplier(movementSpeedKey);
```

Эти queries и подписка не создают engine/pools на пустом Health. Advanced `health.Statuses` — стабильный managed API: ApplyFromHit, GetStacks/GetStack, Dispel/TryConsume/TryCombine/Advance и прежние операции. Это не MonoBehaviour: GetComponent/AddComponent<StatusController> не используются. Сама facade остаётся лёгкой; прежние коллекции/словари/буферы возникают по потребности. После активных статусов тёплые пулы сохраняются для повторного использования.

В Inspector доступны группы/тики/стэки и advanced limits32/8/512. StatusesEnabled=false очищает статусы, их множители/резисты/ограничения и снимает их регистрацию в StatusScheduler. Внешние MovementInfluences spatial sources, manual CombatControl leases и stat modifier handles остаются у своих владельцев. ON начинает пустым. Health disable/pooling, смерть, Reset и direct Restore очищают по прежним причинам. Revive сохраняет LifeRules.ClearStatusesOnRevive. Нулевой урон с OnContact может накладывать статус.

```csharp
health.ConfigureFeedbacks(damagePreset, deathPreset, healingPreset, playerContext);
health.FeedbacksEnabled = false; // Статусы и здоровье продолжают работать.
```

Пустые preset slots ничего не вызывают: нет provider, FeedbackService, auto-prewarm, handles или Update на Health. Отдельный FeedbackEmitter не создаётся. Context Provider необязателен и должен реализовать IFeedbackContextProvider. Его результат принимается целиком, сохраняя позицию/source/intensity и explicit gamepad/WithoutGamepad. Как прежде в Combat, назначенный provider вызывается и при выключенном MonoBehaviour. Provider/playback exception изолируется и не отменяет damage/reward/death.

При committed Killed запускается ровно один Death preset, либо Damage при отсутствии Death; это работает и при добивании Downed с нулевой потерей HP. Обычный Damage preset нужен реальной потере HP. Healing preset — положительному лечению HP, не одному лишь overheal shield. Reset/Revive/Restore не проигрывают отклики.

До вызова Context Provider резервируется выплата текущей жизни и фиксируются её настройки. Затем полностью подготавливается feedback context, включая explicit device, scale и audio multiplier, до любых callbacks выплаты награды. После этого выполняются зарезервированная награда и воспроизведение сохранённых preset/context. Pooling, перенастройка или Destroy провайдера из RewardCompleted не меняют уже подготовленный отклик. Сам FeedbackService получает intent перед обычными outward damage/death observers. Последующие изменения FeedbacksEnabled влияют на будущие запуски. Уже запущенные эффекты остаются у Feedbacks; глобальный StopAll не вызывается.

Снимки статусов содержат полное прежнее состояние. Пустой снимок одинаков до/после первого Apply+Clear и не создаёт live storage. Непустой restore при StatusesEnabled=false явно отклоняется. Gate и бюджеты повторно проверяются после CancelForRestore всех transient-объектов до первого Health/module commit; их собственные побочные эффекты не откатываются. Preflight не меняет actor state/identity, scheduler или подписки. World restore готовит данные всех актёров до commit и не очищает затем восстановленные записи. Подробности в Saving.ru.md.

Старые StatusController/HealthFeedbacks authoring компоненты заменены native возможностями. Для существующего стенда координатор переносит budgets и Damage/Death/Healing/ContextProvider ссылки в Health до удаления старых MonoBehaviour, затем обновляет prefab/scene refs атомарно. PresentationFeedbacks остаётся отдельным владельцем bounded presentation stream; не назначайте один и тот же damage output сразу в два сознательно включённых маршрута, если нужен один отклик.

Проверка выполняется Editor/PlayMode suites с реальным Health, статусами, scheduler и публичным FeedbackService. Никакие Player/Addressables/Clean validation builds автоматически не запускаются.
