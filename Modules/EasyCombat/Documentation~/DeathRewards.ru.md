# Награда за уничтожение в Health

Настройка находится прямо в `Health`. По умолчанию **Enable Death Reward** выключен. Включите его, назначьте **Resource Reward** и **Recipient Wallet** (`ResourceContainer` игрока). Отдельный компонент или импорт Samples не требуется.

Создайте одну Resource Database и добавьте записи Coin/Crystal в её Inspector. В профиле Resource Reward выберите эту Database, затем нужные ресурсы из её списка и Min/Max. Оба края диапазона включены. Health использует готовый typed профиль; он не копирует определения и не хранит балансы. База/профиль — конфигурация, кошелёк — runtime состояние получателя.

```csharp
enemyHealth.ConfigureDeathReward(enemyRewardProfile, playerWallet);
// Или три явные настройки:
enemyHealth.RewardProfile = enemyRewardProfile;
enemyHealth.RewardRecipient = playerWallet;
enemyHealth.RewardEnabled = true;
```

```csharp
enemyHealth.RewardCompleted += result =>
{
    if (!result.Granted) ShowRewardError(result.Error);
};
var lastAttempt = enemyHealth.LastRewardResult;
```

`ConfigureDeathReward(profile, recipient, System.Random)` позволяет задать детерминированный генератор для тестов. Без него используется генератор Resources. Метод включает возможность, простое чтение свойств ничего не создаёт и не выдаёт.

Результат `EasyCombat.DeathResourceRewardResult`: Status (`None`, `Granted`, `Rejected`, `InvalidConfiguration`), Granted, LifeId, Recipient, Reward, Failure, Error. Reward — единственный сделанный бросок; при ошибке до броска он null. LastRewardResult хранит предыдущую попытку для диагностики, а не текущее здоровье. Ошибка подписчика результата логируется и не отменяет платёж или Died.

Health обрабатывает награду на подтверждённом переходе в Dead, до внешних Resolved/Changed/Damaged/Died callbacks. Поэтому Died-подписчик может вернуть врага в pool после выплаты. Downed ещё не смерть; finishing hit выдаёт награду. До броска жизнь помечается обработанной: повторное уведомление и reentry не бросают и не платят заново. Нет Update или глобального поиска убийцы/игрока.

Выдача всех ресурсов атомарна. Если хотя бы один не помещается, кошелёк сохраняется целиком, Status=Rejected и Failure объясняет причину. Попытка для этой смерти завершена: изменение лимитов/профиля, выключение и включение возможности не повторяют её. Автоматических retry/reroll нет. Неподходящая конфигурация даёт InvalidConfiguration. Неожиданная ошибка переданного RNG изолируется: результат сообщает ошибку, а Health всё равно публикует Died.

RewardEnabled=false не создаёт runtime-контейнер награды, подписки, списки или RNG. При выключенной возможности остаются сериализованные поля и короткая проверка в соответствующих lifecycle-точках. При включении нужны только ленивое состояние попытки и результат; новая награда может выделять память при создании rolled snapshot. Zero-GC на событие смерти не заявляется.

Health, выключенный до обработки подтверждённой смерти, не выдаёт награду; включение над трупом ничего не догоняет. Это также относится к RewardEnabled=false. Смерть уже зафиксирована до уведомления внешних наблюдателей: отключение или pooling из Resolved/Changed/Damaged/Died не отменяет завершённую выплату. Все эти callbacks вызываются после неё в прежнем порядке между собой.

ResetToFull и полноценный Revive из Dead создают новую жизнь и разрешают следующую настоящую смерть. Оба пути восстановления Health синхронизируют метку до уведомления внешних наблюдателей, даже у disabled Health/возможности. Восстановление Dead помечает жизнь обработанной без награды. Восстановление Alive/Downed разрешает следующую смерть, в том числе при сохранённом Identity в простом Health.RestoreState.

Save должен согласованно восстановить врага и кошелёк. Сценарий «сохранить живого и wallet → убить → восстановить оба → убить» снова выдаёт награду в восстановленной временной линии. Загрузка только Health при сохранении позднего кошелька не является согласованным восстановлением прогресса. Сами RewardProfile/Recipient/RewardEnabled — конфигурация Health, а не новый отдельный save ledger.

Компонент `DeathResourceReward` заменён этим API и удалён. Параллельного пути выплаты нет. Настройку прежнего локального примера переносит его builder: поля в Health и подписка RewardCompleted; готовая сцена не должна сохранять удалённый MonoBehaviour.
