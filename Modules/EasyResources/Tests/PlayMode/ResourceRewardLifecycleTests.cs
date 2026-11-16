using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyResources.Tests
{
    public sealed class ResourceRewardLifecycleTests
    {
        [UnityTest]
        public IEnumerator ContainerReceivesCompleteRewardEvenWhenDestroyedByItsFinalEvent()
        {
            var coin = ResourceDefinition.Create("Coin"); var crystal = ResourceDefinition.Create("Crystal");
            var database = ResourceDatabase.Create("Resources", coin, crystal);
            var profile = ResourceRewardProfile.Create("Reward", database, new ResourceRewardRange(coin, 2, 2), new ResourceRewardRange(crystal, 3, 3));
            var go = new GameObject("Reward recipient"); var wallet = go.AddComponent<ResourceContainer>(); var store = wallet.Store;
            var observed = false;
            try
            {
                wallet.OnChanged.AddListener(() => UnityEngine.Object.DestroyImmediate(go));
                store.Changed += () => observed = store.GetTotal(coin) == 2 && store.GetTotal(crystal) == 3;
                Assert.That(wallet.TryGrant(profile.Roll(), out _), Is.True);
                Assert.That(observed, Is.True);
                Assert.Throws<ObjectDisposedException>(() => store.GetTotal(coin));
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(coin); UnityEngine.Object.Destroy(crystal); UnityEngine.Object.Destroy(profile); UnityEngine.Object.Destroy(database);
            }
            yield return null; LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SharedProfileDoesNotShareWalletsAndDeferredGrantKeepsOneFinalBatch()
        {
            var coin = ResourceDefinition.Create("Coin"); var crystal = ResourceDefinition.Create("Crystal");
            var database = ResourceDatabase.Create("Resources", coin, crystal);
            var profile = ResourceRewardProfile.Create("Reward", database, new ResourceRewardRange(coin, 2, 2), new ResourceRewardRange(crystal, 3, 3));
            var a = new GameObject("Wallet A"); var b = new GameObject("Wallet B");
            try
            {
                var first = a.AddComponent<ResourceContainer>(); var second = b.AddComponent<ResourceContainer>();
                var rolled = profile.Roll(); var events = 0; first.Changed += () => events++;
                using (first.Store.DeferNotifications())
                {
                    Assert.That(first.TryGrant(rolled, out _), Is.True);
                    Assert.That(first.TryGrant(rolled, out _), Is.True); // Each successful grant is a new explicit award.
                    Assert.That(events, Is.Zero);
                }
                Assert.That(events, Is.EqualTo(1));
                Assert.That(first.GetTotal(coin), Is.EqualTo(4)); Assert.That(first.GetTotal(crystal), Is.EqualTo(6));
                Assert.That(second.GetTotal(coin), Is.Zero); Assert.That(second.GetTotal(crystal), Is.Zero);
                Assert.That(second.TryGrant(rolled, out _), Is.True);
                Assert.That(second.GetTotal(coin), Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b);
                UnityEngine.Object.Destroy(coin); UnityEngine.Object.Destroy(crystal); UnityEngine.Object.Destroy(profile); UnityEngine.Object.Destroy(database);
            }
            yield return null; LogAssert.NoUnexpectedReceived();
        }
    }
}
