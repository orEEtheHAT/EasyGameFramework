using System;
using System.Collections.Generic;
using System.Reflection;
using EasyResources;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using WalletResource = EasyResources.ResourceDefinition;

namespace EasyCombat.Tests
{
    public sealed class HealthDeathRewardTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private Health enemy;
        private ResourceContainer wallet;
        private WalletResource coin, crystal;
        private ResourceRewardProfile profile;
        private int completions;
        private readonly MethodInfo duplicateCallback = typeof(Health).GetMethod("ProcessDeathReward", BindingFlags.Instance | BindingFlags.NonPublic);
        private sealed class FixedRandom : System.Random
        {
            private readonly byte sample;
            public int Calls;
            public FixedRandom(byte sample) { this.sample = sample; }
            public override int Next(int maxValue) { Calls++; return sample; }
        }
        private sealed class BrokenRandom : System.Random
        { public override int Next(int maxValue) => throw new NotSupportedException("Reward RNG failed"); }
        private T Keep<T>(T value) where T : Object { cleanup.Add(value); return value; }
        private GameObject Go(string name) => Keep(new GameObject(name));
        [SetUp] public void Setup()
        {
            enemy = Go("Reward enemy").AddComponent<Health>();
            wallet = Go("Reward player wallet").AddComponent<ResourceContainer>();
            coin = Keep(WalletResource.Create("Coin")); crystal = Keep(WalletResource.Create("Crystal"));
            var database = Keep(ResourceDatabase.Create("Reward database", coin, crystal));
            profile = Keep(ResourceRewardProfile.Create("Enemy reward", database, new ResourceRewardRange(coin, 3, 6), new ResourceRewardRange(crystal, 1, 4)));
            enemy.ConfigureDeathReward(profile, wallet, new FixedRandom(0));
            enemy.RewardCompleted += _ => completions++;
        }
        [TearDown] public void Cleanup()
        {
            // Dispose component-owned stores before their resource definitions.
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] is GameObject go && go) Object.DestroyImmediate(go);
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i]) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear(); completions = 0;
        }
        private void Kill() => enemy.ApplyDamage(new DamageInfo(1000));
        private void Duplicate() => duplicateCallback.Invoke(enemy, null); // Simulate duplicate source notification; no public payout shortcut.

        [Test] public void DefaultOffAllocatesNoRewardStateOrSignalAndAddsNoComponents()
        {
            var ordinary = Go("Ordinary Health").AddComponent<Health>();
            Assert.That(ordinary.RewardEnabled, Is.False);
            ordinary.RewardProfile = profile; ordinary.RewardRecipient = wallet;
            var randomBefore = UnityEngine.Random.state;
            ordinary.ApplyDamage(new DamageInfo(1000));
            Assert.That(ordinary.LastRewardResult.Status, Is.EqualTo(DeathResourceRewardStatus.None));
            Assert.That(wallet.GetTotal(coin), Is.Zero);
            Assert.That(typeof(Health).GetField("deathRewardState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ordinary), Is.Null);
            Assert.That(typeof(Health).GetField("rewardCompleted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ordinary), Is.Null);
            Assert.That(ordinary.GetComponents<Component>().Length, Is.EqualTo(2));
            Assert.That(UnityEngine.Random.state, Is.EqualTo(randomBefore));
        }

        [Test] public void ExplicitPropertiesEnableOneComponentRewardWithoutAdapter()
        {
            var ordinary = Go("Configured Health").AddComponent<Health>();
            ordinary.RewardProfile = profile; ordinary.RewardRecipient = wallet; ordinary.RewardEnabled = true;
            ordinary.ApplyDamage(new DamageInfo(1000));
            Assert.That(ordinary.LastRewardResult.Granted, Is.True);
            Assert.That(ordinary.GetComponents<Component>().Length, Is.EqualTo(2));
        }

        [Test] public void BrokenRewardRngCannotSuppressConfirmedDeathEvent()
        {
            enemy.ConfigureDeathReward(profile, wallet, new BrokenRandom()); int deaths = 0; enemy.Died += _ => deaths++;
            LogAssert.Expect(LogType.Exception, new Regex("NotSupportedException: Reward RNG failed"));
            var result = enemy.ApplyDamage(new DamageInfo(1000));
            Assert.That(result.Killed, Is.True); Assert.That(deaths, Is.EqualTo(1));
            Assert.That(enemy.LastRewardResult.Status, Is.EqualTo(DeathResourceRewardStatus.InvalidConfiguration));
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.EqualTo(1));
        }

        [Test] public void HealthDisabledInDamageObserverCannotCancelCommittedReward()
        {
            Action<DamageResult> disable = _ => enemy.enabled = false;
            enemy.Damaged += disable; Kill(); enemy.enabled = true; Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(completions, Is.EqualTo(1));
            enemy.Damaged -= disable; enemy.ResetToFull(); Kill();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(6)); Assert.That(completions, Is.EqualTo(2));
        }

        [TestCase("global")] [TestCase("resolved")] [TestCase("changed")] [TestCase("damaged")] [TestCase("died")]
        public void PoolingFromOutwardObserversCannotCancelCommittedRewardOrReorderSignals(string poolAt)
        {
            var order = new List<string>();
            void Observe(string phase)
            {
                order.Add(phase);
                Assert.That(wallet.GetTotal(coin), Is.EqualTo(3), phase);
                if (phase == poolAt) enemy.gameObject.SetActive(false);
            }
            enemy.RewardCompleted += _ => order.Add("reward");
            Action<CombatRecord> global = record => { if (record.TargetHealth == enemy) Observe("global"); };
            CombatEvents.DamageResolved += global;
            enemy.Resolved += _ => Observe("resolved");
            enemy.Changed += change => { if (change.Reason == HealthChangeReason.Damage) Observe("changed"); };
            enemy.Damaged += _ => Observe("damaged");
            enemy.Died += _ => Observe("died");
            try
            {
                Kill(); Duplicate();
                CollectionAssert.AreEqual(new[] { "reward", "global", "resolved", "changed", "damaged", "died" }, order);
                Assert.That(completions, Is.EqualTo(1));
                enemy.gameObject.SetActive(true); Duplicate(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
            }
            finally { CombatEvents.DamageResolved -= global; }
        }

        [Test] public void HealthDisabledBeforeDamageDoesNotDieOrReward()
        {
            enemy.enabled = false;
            var result = enemy.ApplyDamage(new DamageInfo(1000));
            Assert.That(result.Killed, Is.False); Assert.That(enemy.LifeState, Is.EqualTo(CombatLifeState.Alive));
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.Zero);
        }

        [TestCase(0, 3, 1)]
        [TestCase(3, 6, 4)]
        public void RealDeathGrantsBothInclusiveRangeEndpoints(byte randomSample, long expectedCoin, long expectedCrystal)
        {
            var random = new FixedRandom(randomSample); enemy.ConfigureDeathReward(profile, wallet, random);
            var before = JsonUtility.ToJson(profile); int observedDeaths = 0; enemy.Died += _ => observedDeaths++;
            bool coherent = false; wallet.Changed += () => coherent = wallet.GetTotal(coin) == expectedCoin && wallet.GetTotal(crystal) == expectedCrystal;
            var hit = enemy.ApplyDamage(new DamageInfo(1000));
            Assert.That(hit.Killed, Is.True); Assert.That(observedDeaths, Is.EqualTo(1));
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(expectedCoin)); Assert.That(wallet.GetTotal(crystal), Is.EqualTo(expectedCrystal));
            Assert.That(coherent, Is.True); Assert.That(completions, Is.EqualTo(1));
            Assert.That(enemy.LastRewardResult.Status, Is.EqualTo(DeathResourceRewardStatus.Granted));
            Assert.That(enemy.LastRewardResult.Reward.GetAmount(coin), Is.EqualTo(expectedCoin));
            Assert.That(enemy.LastRewardResult.LifeId, Is.EqualTo(enemy.Identity.Id));
            Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(before)); Assert.That(random.Calls, Is.GreaterThan(0));
        }

        [Test] public void DuplicateAndReentrantNotificationsNeverRerollOrPayTwice()
        {
            var random = new FixedRandom(0); enemy.ConfigureDeathReward(profile, wallet, random);
            wallet.Changed += Duplicate; enemy.RewardCompleted += _ => Duplicate();
            Kill(); var rolled = enemy.LastRewardResult.Reward; int calls = random.Calls;
            Duplicate(); Duplicate(); enemy.ApplyDamage(new DamageInfo(1000));
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(wallet.GetTotal(crystal), Is.EqualTo(1));
            Assert.That(completions, Is.EqualTo(1)); Assert.That(random.Calls, Is.EqualTo(calls));
            Assert.That(enemy.LastRewardResult.Reward, Is.SameAs(rolled));
        }

        [Test] public void ResetAndPoolToggleAllowExactlyOneRewardForNextLife()
        {
            Kill(); var firstLife = enemy.Identity.Id;
            enemy.RewardEnabled = false; enemy.RewardEnabled = true; enemy.gameObject.SetActive(false); enemy.gameObject.SetActive(true); Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(completions, Is.EqualTo(1));
            enemy.ResetToFull(); Assert.That(enemy.Identity.Id, Is.Not.EqualTo(firstLife)); Kill(); Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(6)); Assert.That(completions, Is.EqualTo(2));
        }

        [Test] public void DisabledAtDeathSkipsCatchUpAndResetAllowsNextKill()
        {
            enemy.RewardEnabled = false; Kill(); enemy.RewardEnabled = true; Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.Zero);
            enemy.ResetToFull(); Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
        }

        [Test] public void DeathObserversCanPoolOnlyAfterOwnedRewardHasCompleted()
        {
            enemy.RewardEnabled = false;
            enemy.Died += _ => enemy.RewardEnabled = false;
            enemy.RewardEnabled = true; Kill();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(completions, Is.EqualTo(1));
        }

        [Test] public void EnablingHealthRewardOverExistingCorpseDoesNotCreateRetroactiveReward()
        {
            enemy.RewardEnabled = false; Kill(); enemy.ConfigureDeathReward(profile, wallet);
            Duplicate(); Assert.That(wallet.GetTotal(coin), Is.Zero);
            enemy.ResetToFull(); Kill(); Assert.That(wallet.GetTotal(coin), Is.InRange(3L, 6L));
        }

        [Test] public void ReviveFromDeadAllowsOneRewardForNewLife()
        {
            Kill(); long previousLife = enemy.Identity.Id;
            Assert.That(enemy.Revive(), Is.True); Assert.That(enemy.Identity.Id, Is.Not.EqualTo(previousLife));
            Kill(); Duplicate(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(6)); Assert.That(completions, Is.EqualTo(2));
        }

        [Test] public void RestoringAliveRearmsSameIdentityWithCoordinatedWalletRollback()
        {
            var alive = enemy.CaptureState(); var balance = wallet.CaptureState(); var life = enemy.Identity.Id;
            Kill(); wallet.RestoreState(balance, new[] { coin, crystal });
            Assert.That(enemy.RestoreState(alive), Is.True); Assert.That(enemy.Identity.Id, Is.EqualTo(life));
            Kill(); Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3)); Assert.That(completions, Is.EqualTo(2));
        }

        [Test] public void AliveRestoreWhileDisabledRearmsWithoutPayingUntilRealDeath()
        {
            var alive = enemy.CaptureState(); Kill();
            enemy.RewardEnabled = false; Assert.That(enemy.RestoreState(alive), Is.True);
            enemy.RewardEnabled = true; Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
            Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(6));
        }

        [Test] public void DirectDeadRestoreNeverPaysEvenWhenRepeatedOrDisabled()
        {
            enemy.RewardEnabled = false; Assert.That(enemy.RestoreState(new HealthSnapshot(100, 0)), Is.True);
            enemy.RewardEnabled = true; Duplicate();
            Assert.That(enemy.RestoreState(new HealthSnapshot(100, 0)), Is.True); Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.Zero);
            enemy.ResetToFull(); Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
        }

        [Test] public void AliveRestoreWhileEntireEnemyInactiveRearmsOnReactivation()
        {
            var alive = enemy.CaptureState(); Kill(); enemy.gameObject.SetActive(false);
            Assert.That(enemy.RestoreState(alive), Is.True); enemy.gameObject.SetActive(true);
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
            Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(6));
        }

        [Test] public void WorldRestoreDeadConsumesNewIdentityAndAliveRestoreAllowsRealDeath()
        {
            var catalog = Keep(ScriptableObject.CreateInstance<CombatDefinitionCatalog>());
            Assert.That(catalog.Configure(Array.Empty<CombatCatalogEntry>(), "reward-test"), Is.True);
            var registry = new CombatReferenceRegistry(); registry.RegisterActor("enemy", enemy);
            Assert.That(CombatSaveSystem.TryCapture(catalog, registry, out var alive, out var error), Is.True, error);
            Kill(); Assert.That(CombatSaveSystem.TryCapture(catalog, registry, out var dead, out error), Is.True, error);
            long before = wallet.GetTotal(coin);
            Assert.That(CombatSaveSystem.TryRestore(dead, catalog, registry, out _, out error), Is.True, error); Duplicate();
            Assert.That(CombatSaveSystem.TryRestore(dead, catalog, registry, out _, out error), Is.True, error); Duplicate();
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(before)); Assert.That(completions, Is.EqualTo(1));
            Assert.That(CombatSaveSystem.TryRestore(alive, catalog, registry, out _, out error), Is.True, error);
            Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(before + 3)); Assert.That(completions, Is.EqualTo(2));
        }

        [Test] public void CapacityFailureRejectsWholeSnapshotAndDoesNotRetryAfterCapacityChanges()
        {
            Assert.That(wallet.Store.TrySetLimits(new ResourceLimits(new[] { new ResourceLimit(crystal, 0) }), out _), Is.True);
            var revision = wallet.Store.Revision; int changed = 0; wallet.Changed += () => changed++;
            var random = new FixedRandom(0); enemy.ConfigureDeathReward(profile, wallet, random); Kill();
            Assert.That(enemy.LastRewardResult.Status, Is.EqualTo(DeathResourceRewardStatus.Rejected));
            Assert.That(enemy.LastRewardResult.Failure.Code, Is.EqualTo(ResourceFailureCode.ResourceCapacityExceeded));
            Assert.That(enemy.LastRewardResult.Reward.GetAmount(coin), Is.EqualTo(3));
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(wallet.GetTotal(crystal), Is.Zero);
            Assert.That(changed, Is.Zero); Assert.That(wallet.Store.Revision, Is.EqualTo(revision));
            int calls = random.Calls; var rolled = enemy.LastRewardResult.Reward;
            wallet.Store.TrySetLimits(new ResourceLimits(), out _); Duplicate(); enemy.RewardEnabled = false; enemy.RewardEnabled = true; Duplicate();
            Assert.That(random.Calls, Is.EqualTo(calls)); Assert.That(enemy.LastRewardResult.Reward, Is.SameAs(rolled));
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.EqualTo(1));
        }

        [TestCase(true)] [TestCase(false)]
        public void MissingConfigurationReportsFailureAndDoesNotCatchUp(bool missingProfile)
        {
            enemy.ConfigureDeathReward(missingProfile ? null : profile, missingProfile ? wallet : null);
            Kill(); Assert.That(enemy.LastRewardResult.Status, Is.EqualTo(DeathResourceRewardStatus.InvalidConfiguration));
            Assert.That(enemy.LastRewardResult.Error, Is.Not.Empty); Assert.That(wallet.GetTotal(coin), Is.Zero);
            enemy.ConfigureDeathReward(profile, wallet); Duplicate(); Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.EqualTo(1));
        }

        [Test] public void DownedIsNotDeathAndFinishingHitPaysOnce()
        {
            var rules = Keep(ScriptableObject.CreateInstance<LifeRulesDefinition>()); rules.AllowDowned = true;
            var healthProfile = Keep(ScriptableObject.CreateInstance<HealthDefinition>()); healthProfile.LifeRules = rules;
            enemy.ApplyDefinition(healthProfile); Kill(); Assert.That(enemy.LifeState, Is.EqualTo(CombatLifeState.Downed));
            Assert.That(wallet.GetTotal(coin), Is.Zero); Assert.That(completions, Is.Zero);
            Kill(); Assert.That(enemy.LifeState, Is.EqualTo(CombatLifeState.Dead)); Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
        }

        [Test] public void ResetRequestedInDeathCallbackClosesOldLifeThenAllowsNewDeath()
        {
            enemy.Died += _ => enemy.ResetToFull(); Kill();
            Assert.That(enemy.IsStanding, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(3));
            Kill(); Assert.That(wallet.GetTotal(coin), Is.EqualTo(6)); Assert.That(completions, Is.EqualTo(2));
        }
    }
}
