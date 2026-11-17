using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyFeedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyCombat.Tests
{
    public sealed class HealthFeedbackRecordingContext : MonoBehaviour, IFeedbackContextProvider
    {
        public readonly List<FeedbackContext> Received = new List<FeedbackContext>();
        public FeedbackContext Prepared;
        public Gamepad DefaultGamepad;
        public bool SuppressGamepad;
        public bool CustomizeOutput;
        public Action DuringPrepare;
        public FeedbackContext Prepare(FeedbackContext context)
        {
            Received.Add(context);
            if (CustomizeOutput)
                context = new FeedbackContext(context.WorldPosition, context.Rotation, context.Source, context.Follow,
                    2.25f, .375f, new Vector3(2, 3, 4), context.Positioning);
            if (SuppressGamepad) context = context.WithoutGamepad();
            Prepared = context.WithDefaultGamepad(DefaultGamepad);
            DuringPrepare?.Invoke();
            return Prepared;
        }
    }

    public sealed class HealthFeedbackThrowingContext : MonoBehaviour, IFeedbackContextProvider
    {
        public int Calls;
        public FeedbackContext Prepare(FeedbackContext context)
        { Calls++; throw new InvalidOperationException("Health feedback context failed"); }
    }

    /// <summary>PlayMode routing tests plus one real visual-output fixture through the public FeedbackService.</summary>
    public sealed class HealthFeedbackTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private Health health;
        private HealthFeedbackRecordingContext provider;
        private T Asset<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); cleanup.Add(value); return value; }
        private GameObject Actor(string name)
        { var go = new GameObject(name); cleanup.Add(go); return go; }
        private FeedbackPreset Preset() => Asset<FeedbackPreset>();
        private EasyResources.ResourceContainer ConfigureDeathReward(out EasyResources.ResourceDefinition coin)
        {
            coin = EasyResources.ResourceDefinition.Create("Feedback reward coin"); cleanup.Add(coin);
            var database = EasyResources.ResourceDatabase.Create("Feedback reward database", coin); cleanup.Add(database);
            var reward = EasyResources.ResourceRewardProfile.Create("Feedback death reward", database,
                new EasyResources.ResourceRewardRange(coin, 1, 1)); cleanup.Add(reward);
            var wallet = Actor("Feedback reward recipient").AddComponent<EasyResources.ResourceContainer>();
            health.ConfigureDeathReward(reward, wallet);
            return wallet;
        }
        [SetUp] public void Setup()
        {
            var actor = Actor("Native Health feedback actor");
            actor.transform.position = new Vector3(4, 5, 6);
            actor.transform.rotation = Quaternion.Euler(0, 45, 0);
            health = actor.AddComponent<Health>();
            provider = actor.AddComponent<HealthFeedbackRecordingContext>();
            health.FeedbackContextProvider = provider;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var device in devices) if (device.added) InputSystem.RemoveDevice(device);
            devices.Clear();
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i]) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test] public void DefaultEnabledWithoutPresetsDoesNotRequestContextsOrAddComponents()
        {
            Assert.That(health.FeedbacksEnabled, Is.True);
            health.ApplyDamage(new DamageInfo(10)); health.Heal(5); health.ApplyDamage(new DamageInfo(500));
            Assert.That(provider.Received, Is.Empty);
            Assert.That(health.GetComponents<MonoBehaviour>().Length, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived(); // A null FeedbackService.Play would warn.
        }

        [Test] public void NativeDamageHealingAndDeathPreserveOriginalWorldContexts()
        {
            health.ConfigureFeedbacks(Preset(), Preset(), Preset(), provider);
            var point = new Vector3(1, 2, 3);
            health.ApplyDamage(new DamageInfo(10, point: point)); health.Heal(5); health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(provider.Received.Count, Is.EqualTo(3));
            Assert.That(provider.Received[0].WorldPosition, Is.EqualTo(point));
            Assert.That(provider.Received[0].Rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(provider.Received[1].WorldPosition, Is.EqualTo(health.transform.position));
            Assert.That(provider.Received[1].Rotation, Is.EqualTo(health.transform.rotation));
            Assert.That(provider.Received[2].WorldPosition, Is.EqualTo(point));
            foreach (var context in provider.Received)
            {
                Assert.That(context.Source, Is.SameAs(health.transform)); Assert.That(context.Follow, Is.Null);
                Assert.That(context.Intensity, Is.EqualTo(1)); Assert.That(context.AudioMultiplier, Is.EqualTo(1));
                Assert.That(context.VisualScale, Is.EqualTo(Vector3.one));
            }
            Assert.That(health.GetComponents<MonoBehaviour>().Length, Is.EqualTo(2));
        }

        [Test] public void FeedbackToggleIsIndependentAndConfigureDoesNotOverrideIt()
        {
            health.FeedbacksEnabled = false;
            health.ConfigureFeedbacks(Preset(), Preset(), Preset(), provider);
            Assert.That(health.FeedbacksEnabled, Is.False);
            health.ApplyDamage(new DamageInfo(10)); health.Heal(5);
            Assert.That(provider.Received, Is.Empty);
            health.FeedbacksEnabled = true; health.ApplyDamage(new DamageInfo(5));
            Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void FinishingDownedDeathUsesDeathPresetEvenWithZeroHealthLost()
        {
            var rules = Asset<LifeRulesDefinition>(); rules.AllowDowned = true;
            var definition = Asset<HealthDefinition>(); definition.LifeRules = rules;
            health.ApplyDefinition(definition);
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            var down = health.ApplyDamage(new DamageInfo(500));
            Assert.That(down.Downed, Is.True); Assert.That(provider.Received, Is.Empty);
            int damaged = 0; health.Damaged += _ => damaged++;
            var point = new Vector3(8, 9, 10);
            var finish = health.ApplyDamage(new DamageInfo(5, point: point));
            Assert.That(finish.Killed, Is.True); Assert.That(finish.AppliedAmount, Is.Zero); Assert.That(damaged, Is.Zero);
            Assert.That(provider.Received.Count, Is.EqualTo(1)); Assert.That(provider.Received[0].WorldPosition, Is.EqualTo(point));
            health.ApplyDamage(new DamageInfo(5)); Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void MissingDeathPresetFallsBackToDamageOnceOnLethalHit()
        {
            health.ConfigureFeedbacks(Preset(), null, null, provider);
            var hit = health.ApplyDamage(new DamageInfo(500));
            Assert.That(hit.Killed, Is.True); Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void DeathPresetOverridesDamageInsteadOfLaunchingBoth()
        {
            health.ConfigureFeedbacks(Preset(), Preset(), null, provider);
            health.ApplyDamage(new DamageInfo(500));
            Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void RejectedDamageShieldOnlyDamageAndOverhealDoNotLaunchHealthFeedback()
        {
            var shield = Asset<ShieldLayer>(); shield.Capacity = 50;
            var defense = Asset<DefenseProfile>(); defense.Layers = new[] { new DefenseSlot(shield) };
            var definition = Asset<HealthDefinition>(); definition.Defense = defense;
            health.ApplyDefinition(definition); health.ConfigureFeedbacks(Preset(), Preset(), Preset(), provider);
            health.Invulnerable = true; health.ApplyDamage(new DamageInfo(5)); health.Invulnerable = false;
            health.ApplyDamage(new DamageInfo(10));
            var result = health.ApplyHealing(new HealingInfo(5, overheal: OverhealPolicy.RestoreShield));
            Assert.That(result.AppliedAmount, Is.Zero); Assert.That(result.ShieldRestored, Is.EqualTo(5));
            health.Heal(0); Assert.That(provider.Received, Is.Empty);
        }

        [TestCase("global")] [TestCase("damaged")] [TestCase("died")]
        public void FeedbackCompletesBeforeExternalDeathObserversPoolActor(string phase)
        {
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            Action<CombatRecord> global = record =>
            {
                if (record.TargetHealth == health && phase == "global")
                { Assert.That(provider.Received.Count, Is.EqualTo(1)); health.gameObject.SetActive(false); }
            };
            CombatEvents.DamageResolved += global;
            health.Damaged += _ => { if (phase == "damaged") { Assert.That(provider.Received.Count, Is.EqualTo(1)); health.gameObject.SetActive(false); } };
            health.Died += _ => { if (phase == "died") { Assert.That(provider.Received.Count, Is.EqualTo(1)); health.gameObject.SetActive(false); } };
            try
            {
                var point = new Vector3(10, 11, 12); health.ApplyDamage(new DamageInfo(500, point: point));
                Assert.That(provider.Received.Count, Is.EqualTo(1)); Assert.That(provider.Received[0].WorldPosition, Is.EqualTo(point));
                health.gameObject.SetActive(true); Assert.That(provider.Received.Count, Is.EqualTo(1));
            }
            finally { CombatEvents.DamageResolved -= global; }
        }

        [Test] public void ResetRestoreAndReviveNeverReplayFeedback()
        {
            health.ConfigureFeedbacks(Preset(), Preset(), Preset(), provider);
            health.ApplyDamage(new DamageInfo(500)); var dead = health.CaptureState();
            provider.Received.Clear();
            health.ResetToFull(); Assert.That(health.RestoreState(dead), Is.True); Assert.That(health.Revive(), Is.True);
            Assert.That(provider.Received, Is.Empty);
        }

        [Test] public void RewardCallbackCannotCancelCapturedDeathFeedbackByPoolingOrChangingConfiguration()
        {
            var wallet = ConfigureDeathReward(out var coin);
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            var point = new Vector3(31, 32, 33); var source = health.transform;
            health.RewardCompleted += _ =>
            {
                health.FeedbacksEnabled = false;
                health.DeathFeedback = null;
                health.FeedbackContextProvider = null;
                health.transform.position = new Vector3(100, 200, 300);
                health.gameObject.SetActive(false);
            };
            var result = health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(result.Killed, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
            Assert.That(provider.Received.Count, Is.EqualTo(1));
            Assert.That(provider.Received[0].WorldPosition, Is.EqualTo(point));
            Assert.That(provider.Received[0].Source, Is.SameAs(source)); Assert.That(provider.Received[0].Follow, Is.Null);
        }

        [Test] public void DestroyingHealthFromRewardCallbackDoesNotRereadItsTransformForFeedback()
        {
            var wallet = ConfigureDeathReward(out var coin);
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            var point = new Vector3(41, 42, 43); var source = health.transform;
            health.RewardCompleted += _ => Object.DestroyImmediate(health);
            var result = health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(result.Killed, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
            Assert.That(provider.Received.Count, Is.EqualTo(1));
            Assert.That(provider.Received[0].WorldPosition, Is.EqualTo(point)); Assert.That(provider.Received[0].Source, Is.SameAs(source));
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void DestroyingActorFromRewardCallbackUsesCapturedContextAndExternalProvider()
        {
            var wallet = ConfigureDeathReward(out var coin);
            var external = Actor("Surviving player feedback provider").AddComponent<HealthFeedbackRecordingContext>();
            health.ConfigureFeedbacks(null, Preset(), null, external);
            var point = new Vector3(51, 52, 53);
            health.RewardCompleted += _ => Object.DestroyImmediate(health.gameObject);
            var result = health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(result.Killed, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
            Assert.That(external.Received.Count, Is.EqualTo(1)); Assert.That(external.Received[0].WorldPosition, Is.EqualTo(point));
            Assert.That(external.Received[0].Source == null, Is.True); Assert.That(external.Received[0].Follow, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void RewardCanDestroyProviderAfterItsDistinctivePreparedContextIsCapturedOnce()
        {
            var wallet = ConfigureDeathReward(out var coin);
            var gamepad = InputSystem.AddDevice<Gamepad>(); devices.Add(gamepad);
            provider.DefaultGamepad = gamepad; provider.CustomizeOutput = true;
            Assert.That(VisualFeedbackManager.Instance, Is.Null, "This output-level fixture owns its visual manager.");
            var visuals = Actor("Native Health output-level visual manager").AddComponent<VisualFeedbackManager>();
            var particleObject = Actor("Native Health recording particle resource"); particleObject.SetActive(false);
            var particles = particleObject.AddComponent<ParticleSystem>();
            var main = particles.main; main.playOnAwake = false; main.loop = true;
            var layer = new VisualFeedbackEntry();
            typeof(VisualFeedbackEntry).GetField("particleSystemPrefab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(layer, particles);
            typeof(VisualFeedbackEntry).GetField("lifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(layer, VisualFeedbackLifetime.LoopUntilStopped);
            var realPreset = Preset();
            typeof(FeedbackPreset).GetField("visualEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(realPreset, new List<VisualFeedbackEntry> { layer });
            health.ConfigureFeedbacks(null, realPreset, null, provider);
            var point = new Vector3(61, 62, 63); var source = health.transform;
            FeedbackContext preparedAtCommit = default;
            health.RewardCompleted += _ =>
            {
                Assert.That(provider.Received.Count, Is.EqualTo(1));
                preparedAtCommit = provider.Prepared;
                provider.DefaultGamepad = null; provider.CustomizeOutput = false;
                Object.DestroyImmediate(provider);
            };
            var result = health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(result.Killed, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
            Assert.That(provider == null, Is.True);
            Assert.That(provider.Received.Count, Is.EqualTo(1)); // Managed diagnostic history remains readable after native component destruction.
            Assert.That(preparedAtCommit.WorldPosition, Is.EqualTo(point)); Assert.That(preparedAtCommit.Source, Is.SameAs(source));
            Assert.That(preparedAtCommit.Intensity, Is.EqualTo(2.25f)); Assert.That(preparedAtCommit.AudioMultiplier, Is.EqualTo(.375f));
            Assert.That(preparedAtCommit.VisualScale, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(preparedAtCommit.TargetGamepad, Is.SameAs(gamepad)); Assert.That(preparedAtCommit.HasExplicitGamepadTarget, Is.True);
            // Observe actual FeedbackService dispatch, not only the provider's own diagnostic copy.
            Assert.That(visuals.ActiveInstances, Is.EqualTo(1)); Assert.That(visuals.TotalInstances, Is.EqualTo(1));
            var played = visuals.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(played.Length, Is.EqualTo(1));
            Assert.That(played[0].transform.position, Is.EqualTo(point));
            Assert.That(played[0].transform.localScale, Is.EqualTo(new Vector3(4.5f, 6.75f, 9f))); // context visual scale * intensity
            visuals.StopAll(true);
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void ProviderPrepareCannotCancelReservedDeathRewardByDisablingAndClearingHealthConfiguration()
        {
            var wallet = ConfigureDeathReward(out var coin);
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            provider.DuringPrepare = () =>
            {
                health.RewardEnabled = false;
                health.RewardProfile = null; health.RewardRecipient = null;
                health.FeedbacksEnabled = false; health.DeathFeedback = null; health.FeedbackContextProvider = null;
                health.enabled = false;
            };
            var point = new Vector3(71, 72, 73);
            var result = health.ApplyDamage(new DamageInfo(500, point: point));
            Assert.That(result.Killed, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
            Assert.That(health.LastRewardResult.Granted, Is.True);
            Assert.That(provider.Received.Count, Is.EqualTo(1)); Assert.That(provider.Prepared.WorldPosition, Is.EqualTo(point));
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void ProviderPrepareReentrantDamageCannotPayReservedDeathRewardTwice()
        {
            var wallet = ConfigureDeathReward(out var coin);
            health.ConfigureFeedbacks(null, Preset(), null, provider);
            DamageResult reentrant = default;
            provider.DuringPrepare = () => reentrant = health.ApplyDamage(new DamageInfo(500));
            var result = health.ApplyDamage(new DamageInfo(500));
            Assert.That(result.Killed, Is.True); Assert.That(reentrant.Rejection, Is.EqualTo(DamageRejection.Reentrant));
            Assert.That(wallet.GetTotal(coin), Is.EqualTo(1)); Assert.That(provider.Received.Count, Is.EqualTo(1));
            Assert.That(health.LastRewardResult.Granted, Is.True);
        }

        [Test] public void WorldRestoreDoesNotReplayAndNextActualHitStillPlays()
        {
            health.ConfigureFeedbacks(Preset(), Preset(), Preset(), provider);
            var catalog = Asset<CombatDefinitionCatalog>(); Assert.That(catalog.Configure(Array.Empty<CombatCatalogEntry>(), "feedback-test"), Is.True);
            var registry = new CombatReferenceRegistry(); registry.RegisterActor("actor", health);
            Assert.That(CombatSaveSystem.TryCapture(catalog, registry, out var alive, out var error), Is.True, error);
            health.ApplyDamage(new DamageInfo(500)); provider.Received.Clear();
            Assert.That(CombatSaveSystem.TryRestore(alive, catalog, registry, out _, out error), Is.True, error);
            Assert.That(CombatSaveSystem.TryRestore(alive, catalog, registry, out _, out error), Is.True, error);
            Assert.That(provider.Received, Is.Empty);
            health.ApplyDamage(new DamageInfo(5)); Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void DisabledAssignedProviderRetainsPreviousCombatRoutingPolicy()
        {
            health.ConfigureFeedbacks(Preset(), null, null, provider); provider.enabled = false;
            health.ApplyDamage(new DamageInfo(5)); Assert.That(provider.Received.Count, Is.EqualTo(1));
        }

        [Test] public void ProviderCanRouteExplicitPlayerDeviceAndPreserveVibrationSuppression()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>(); devices.Add(gamepad); provider.DefaultGamepad = gamepad;
            health.ConfigureFeedbacks(Preset(), null, null, provider);
            var point = new Vector3(21, 22, 23); health.ApplyDamage(new DamageInfo(5, point: point));
            Assert.That(provider.Prepared.TargetGamepad, Is.SameAs(gamepad)); Assert.That(provider.Prepared.HasExplicitGamepadTarget, Is.True);
            Assert.That(provider.Prepared.WorldPosition, Is.EqualTo(point)); Assert.That(provider.Prepared.Source, Is.SameAs(health.transform));
            provider.SuppressGamepad = true; health.ApplyDamage(new DamageInfo(5, point: point));
            Assert.That(provider.Prepared.TargetGamepad, Is.Null); Assert.That(provider.Prepared.HasExplicitGamepadTarget, Is.True);
            Assert.That(provider.Prepared.WorldPosition, Is.EqualTo(point));
        }

        [TestCase(false)] [TestCase(true)]
        public void ContextProviderFailureDoesNotSuppressHealthEvents(bool healing)
        {
            var throwing = health.gameObject.AddComponent<HealthFeedbackThrowingContext>();
            int notifications = 0;
            int rewardCompletions = 0;
            EasyResources.ResourceContainer wallet = null;
            EasyResources.ResourceDefinition coin = null;
            if (healing)
            {
                health.ApplyDamage(new DamageInfo(10)); health.Healed += _ => notifications++;
                health.ConfigureFeedbacks(null, null, Preset(), throwing);
            }
            else
            {
                wallet = ConfigureDeathReward(out coin); health.RewardCompleted += _ => rewardCompletions++;
                health.Died += _ => notifications++; health.ConfigureFeedbacks(null, Preset(), null, throwing);
            }
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Health feedback context failed"));
            if (healing) Assert.That(health.Heal(5), Is.EqualTo(5));
            else Assert.That(health.ApplyDamage(new DamageInfo(500)).Killed, Is.True);
            Assert.That(throwing.Calls, Is.EqualTo(1)); Assert.That(notifications, Is.EqualTo(1));
            if (!healing)
            {
                Assert.That(health.LastRewardResult.Granted, Is.True); Assert.That(wallet.GetTotal(coin), Is.EqualTo(1));
                Assert.That(rewardCompletions, Is.EqualTo(1));
                Assert.DoesNotThrow(() => health.ConfigureDeathReward(health.RewardProfile, wallet));
            }
        }
    }
}
