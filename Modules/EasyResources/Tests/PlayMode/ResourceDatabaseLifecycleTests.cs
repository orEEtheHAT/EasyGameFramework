using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyResources.Tests
{
    public sealed class ResourceDatabaseLifecycleTests
    {
        [UnityTest]
        public IEnumerator SharedDatabaseKeepsDirectRefsAndDoesNotReceiveLiveWalletState()
        {
            var coin = ResourceDefinition.Create("Coin"); var crystal = ResourceDefinition.Create("Crystal");
            var database = ResourceDatabase.Create("Resources", coin, crystal);
            var rewards = ResourceRewardProfile.Create("Reward", database, new ResourceRewardRange(coin, 3, 3), new ResourceRewardRange(crystal, 2, 2));
            var owner = new GameObject("Database reward consumer");
            try
            {
                var wallet = owner.AddComponent<ResourceContainer>();
                var databaseJson = JsonUtility.ToJson(database); var rewardsJson = JsonUtility.ToJson(rewards);
                var coinJson = JsonUtility.ToJson(coin); var crystalJson = JsonUtility.ToJson(crystal);
                var events = 0;
                wallet.Changed += () =>
                {
                    Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(wallet.GetTotal(crystal), Is.EqualTo(2)); events++;
                };
                Assert.That(wallet.TryGrant(rewards.Roll(), out _), Is.True);
                Assert.That(events, Is.EqualTo(1));
                using (var restored = new ResourceStore())
                {
                    var snapshot = wallet.CaptureState();
                    restored.ValidateState(snapshot, database);
                    restored.RestoreState(snapshot, database);
                    Assert.That(restored.GetTotal(coin), Is.EqualTo(3)); Assert.That(restored.GetTotal(crystal), Is.EqualTo(2));
                }
                Assert.That(JsonUtility.ToJson(database), Is.EqualTo(databaseJson)); Assert.That(JsonUtility.ToJson(rewards), Is.EqualTo(rewardsJson));
                Assert.That(JsonUtility.ToJson(coin), Is.EqualTo(coinJson)); Assert.That(JsonUtility.ToJson(crystal), Is.EqualTo(crystalJson));
            }
            finally
            {
                UnityEngine.Object.Destroy(owner); UnityEngine.Object.Destroy(rewards); UnityEngine.Object.Destroy(database);
                UnityEngine.Object.Destroy(coin); UnityEngine.Object.Destroy(crystal);
            }
            yield return null; LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DatabaseRewardCapacityFailureLeavesEveryResourceAndConfigurationUntouched()
        {
            var coin = ResourceDefinition.Create("Coin"); var crystal = ResourceDefinition.Create("Crystal");
            var database = ResourceDatabase.Create("Resources", coin, crystal);
            var rewards = ResourceRewardProfile.Create("Reward", database, new ResourceRewardRange(coin, 3, 3), new ResourceRewardRange(crystal, 2, 2));
            try
            {
                using var wallet = new ResourceStore(limits: new ResourceLimits(new[] { new ResourceLimit(crystal, 1) }));
                var configuration = JsonUtility.ToJson(database); var revision = wallet.Revision; var events = 0;
                wallet.Changed += () => events++;
                var rolled = rewards.Roll();
                Assert.That(wallet.TryGrant(rolled, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(ResourceFailureCode.ResourceCapacityExceeded));
                Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(wallet.GetTotal(crystal), Is.Zero);
                Assert.That(wallet.Revision, Is.EqualTo(revision)); Assert.That(events, Is.Zero);
                Assert.That(JsonUtility.ToJson(database), Is.EqualTo(configuration));
            }
            finally
            {
                UnityEngine.Object.Destroy(rewards); UnityEngine.Object.Destroy(database);
                UnityEngine.Object.Destroy(coin); UnityEngine.Object.Destroy(crystal);
            }
            yield return null; LogAssert.NoUnexpectedReceived();
        }
    }
}
