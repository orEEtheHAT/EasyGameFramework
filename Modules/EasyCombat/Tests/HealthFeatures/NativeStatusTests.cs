using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyCombat.Tests
{
    public sealed class DisableNativeStatusesTick : StatusTickEffect
    {
        [NonSerialized] public int Calls;
        public override void Execute(in StatusTickContext context)
        {
            Calls++;
            context.Target.StatusesEnabled = false;
        }
    }

    public sealed class DisableStatusesDuringRestoreTransient : MonoBehaviour, ICombatTransient
    {
        public Health Target;
        public int Calls;
        public void CancelForRestore()
        {
            Calls++;
            Target.StatusesEnabled = false;
        }
    }

    public sealed class NativeStatusTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private readonly List<ScriptableObject> definitions = new List<ScriptableObject>();
        private static readonly FieldInfo engineField = typeof(Health).GetField("statusEngine", BindingFlags.Instance | BindingFlags.NonPublic);
        private Health first, second;
        private CombatStatDefinition speed;
        private DamageTypeDefinition fire;
        private StatusDefinition slow;
        private CombatDefinitionCatalog catalog;
        private CombatReferenceRegistry references;

        private T Definition<T>() where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>();
            value.name = typeof(T).Name + definitions.Count;
            cleanup.Add(value); definitions.Add(value);
            return value;
        }
        private Health Actor(string id)
        {
            var go = new GameObject("Native status " + id); cleanup.Add(go);
            var health = go.AddComponent<Health>();
            Assert.That(references.RegisterActor(id, health), Is.True);
            return health;
        }
        [SetUp] public void Setup()
        {
            references = new CombatReferenceRegistry();
            first = Actor("a"); second = Actor("b");
            speed = Definition<CombatStatDefinition>(); fire = Definition<DamageTypeDefinition>();
            slow = Definition<StatusDefinition>(); slow.Duration = 2;
            slow.StatModifiers = new[] { new StatusStatModifier(speed, .5f) };
            slow.ResistanceModifiers = new[] { new DamageResistance(fire, .2f) };
            slow.Restrictions = CombatRestriction.Cast;
            catalog = ScriptableObject.CreateInstance<CombatDefinitionCatalog>(); cleanup.Add(catalog);
            var entries = new CombatCatalogEntry[definitions.Count];
            for (int i = 0; i < entries.Length; i++) entries[i] = new CombatCatalogEntry("native-" + i, definitions[i]);
            Assert.That(catalog.Configure(entries, "native-status-tests"), Is.True);
        }
        [TearDown] public void Cleanup()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] is GameObject go && go) Object.DestroyImmediate(go);
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i]) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear(); definitions.Clear(); references.Clear();
        }
        private static void AssertNoEngine(Health health)
        {
            Assert.That(engineField, Is.Not.Null, "Health lazy-allocation proof field is missing.");
            Assert.That(engineField.GetValue(health), Is.Null, "Read/query/subscription/preflight must not create the lazy status engine.");
        }
        private CombatWorldSnapshot Capture()
        {
            Assert.That(CombatSaveSystem.TryCapture(catalog, references, out var snapshot, out var error), Is.True, error);
            return snapshot;
        }

        [Test] public void DefaultsNeutralQueriesAndStableSubscriptionsDoNotCreateStatusStateOrComponents()
        {
            Assert.That(first.StatusesEnabled, Is.True); Assert.That(first.FeedbacksEnabled, Is.True);
            Assert.That(first.StatusCapacity, Is.EqualTo(32)); Assert.That(first.StatusCatchUpLimit, Is.EqualTo(8));
            Assert.That(first.StatusRecordCapacity, Is.EqualTo(512));
            AssertNoEngine(first);
            int changes = 0, lifecycle = 0;
            Action changed = () => changes++;
            Action<StatusChange> observed = _ => lifecycle++;
            first.StatusesChanged += changed; first.StatusLifecycle += observed;
            for (int i = 0; i < 100; i++)
            {
                Assert.That(first.StatusCount, Is.Zero); Assert.That(first.StatusRecordCount, Is.Zero);
                Assert.That(first.GetStatMultiplier(speed), Is.EqualTo(1));
                Assert.That(first.GetStatusResistanceModifier(fire), Is.Zero);
                Assert.That(first.StatusRestrictions, Is.EqualTo(CombatRestriction.None));
            }
            first.StatusesChanged -= changed; first.StatusLifecycle -= observed;
            AssertNoEngine(first); Assert.That(first.GetComponents<Component>().Length, Is.EqualTo(2));
            Assert.That(changes + lifecycle, Is.Zero);
        }

        [Test] public void LateApplicationReachesExistingHealthEventsAndMovementWithoutCacheRefresh()
        {
            var movement = first.gameObject.AddComponent<MovementInfluences>(); movement.MovementSpeedStat = speed;
            int changes = 0; var lifecycle = new List<StatusChange>();
            first.StatusesChanged += () => changes++;
            first.StatusLifecycle += lifecycle.Add;
            Assert.That(movement.Multiplier, Is.EqualTo(1)); AssertNoEngine(first);
            Assert.That(first.ApplyStatus(slow), Is.True);
            Assert.That(first.StatusCount, Is.EqualTo(1)); Assert.That(movement.Multiplier, Is.EqualTo(.5f));
            Assert.That(changes, Is.GreaterThan(0));
            Assert.That(lifecycle.FindAll(change => change.Kind == StatusChangeKind.Applied).Count, Is.EqualTo(1));
            Assert.That(first.RemoveStatus(slow), Is.True);
            Assert.That(movement.Multiplier, Is.EqualTo(1)); Assert.That(first.StatusCount, Is.Zero);
        }

        [Test] public void DisablingStatusesRemovesOnlyTheirContributionsAndReenableStartsEmpty()
        {
            var movement = first.gameObject.AddComponent<MovementInfluences>(); movement.MovementSpeedStat = speed;
            var source = new GameObject("Native status spatial source"); cleanup.Add(source);
            var spatial = ScriptableObject.CreateInstance<MovementInfluenceDefinition>(); cleanup.Add(spatial); spatial.SpeedMultiplier = .8f;
            movement.Set(source.transform, spatial);
            var stats = first.gameObject.AddComponent<CombatStats>(); stats.SetValue(speed, 100);
            Assert.That(stats.TryAdd(new StatModifier(speed, StatModifierOperation.Flat, 20), new object(), out var statHandle), Is.True);
            var control = first.gameObject.AddComponent<CombatControl>();
            Assert.That(control.TryRestrict(CombatRestriction.Move, out var manual), Is.True);
            Assert.That(first.ApplyStatus(slow), Is.True);
            Assert.That(movement.Multiplier, Is.EqualTo(.4f).Within(.0001f));
            Assert.That(stats.TryGetValue(speed, out var before), Is.True); Assert.That(before, Is.EqualTo(60));
            Assert.That(control.Allows(CombatRestriction.Cast), Is.False);
            first.StatusesEnabled = false;
            Assert.That(first.StatusCount, Is.Zero); Assert.That(first.StatusRecordCount, Is.Zero);
            Assert.That(first.GetStatMultiplier(speed), Is.EqualTo(1)); Assert.That(first.GetStatusResistanceModifier(fire), Is.Zero);
            Assert.That(first.StatusRestrictions, Is.EqualTo(CombatRestriction.None));
            Assert.That(movement.SourceCount, Is.EqualTo(1)); Assert.That(movement.Multiplier, Is.EqualTo(.8f));
            Assert.That(stats.ModifierCount, Is.EqualTo(1)); Assert.That(statHandle.IsActive, Is.True);
            Assert.That(stats.TryGetValue(speed, out var after), Is.True); Assert.That(after, Is.EqualTo(120));
            Assert.That(control.Allows(CombatRestriction.Move), Is.False); Assert.That(control.Allows(CombatRestriction.Cast), Is.True);
            Assert.That(first.FeedbacksEnabled, Is.True);
            Assert.That(first.ApplyStatus(slow), Is.False);
            first.StatusesEnabled = true;
            Assert.That(first.StatusCount, Is.Zero); Assert.That(movement.Multiplier, Is.EqualTo(.8f));
            Assert.That(manual.Remove(), Is.True); Assert.That(statHandle.Remove(), Is.True);
        }

        [Test] public void ZeroDamageOnContactAppliesNativeStatusWithoutControllerComponent()
        {
            AssertNoEngine(first);
            var result = first.ApplyDamage(new DamageInfo(0, onHitStatuses: new[] { new StatusApplication(slow) }, statusPolicy: StatusApplyPolicy.OnContact));
            Assert.That(result.AppliedAmount, Is.Zero); Assert.That(first.Current, Is.EqualTo(100));
            Assert.That(first.StatusCount, Is.EqualTo(1)); Assert.That(first.GetStatMultiplier(speed), Is.EqualTo(.5f));
            Assert.That(first.GetComponents<Component>().Length, Is.EqualTo(2));
        }

        [Test] public void EmptySnapshotShapeDoesNotDependOnWhetherStatusesWereEverAllocated()
        {
            AssertNoEngine(first); AssertNoEngine(second);
            var before = Capture();
            Assert.That(before.Actors[0].Statuses, Is.Not.Null); Assert.That(before.Actors[0].Statuses.Values, Is.Empty);
            Assert.That((before.Actors[0].Modules & SavedCombatModules.Statuses) != 0, Is.True);
            Assert.That(CombatSaveSystem.TryValidate(before, catalog, references, out var error), Is.True, error);
            AssertNoEngine(first); AssertNoEngine(second);
            Assert.That(first.ApplyStatus(slow), Is.True); first.ClearStatuses();
            var after = Capture();
            Assert.That(after.Actors[0].Modules, Is.EqualTo(before.Actors[0].Modules));
            Assert.That(JsonUtility.ToJson(after.Actors[0].Statuses), Is.EqualTo(JsonUtility.ToJson(before.Actors[0].Statuses)));
            Assert.That(CombatSaveSystem.TryValidate(after, catalog, references, out error), Is.True, error);
            AssertNoEngine(second);
        }

        [TestCase(false)] [TestCase(true)]
        public void RejectedRestoreNeverAttachesPreparedEngineOrMutatesEarlierActor(bool statusesOff)
        {
            first.ApplyDamage(new DamageInfo(5)); Assert.That(first.ApplyStatus(slow), Is.True);
            var saved = Capture();
            Assert.That(references.Unregister("a"), Is.True);
            Object.DestroyImmediate(first.gameObject);
            first = Actor("a");
            if (statusesOff) first.StatusesEnabled = false;
            else saved.Actors[1].Health.ManualInvulnerability = float.NaN;
            int changes = 0, lifecycle = 0, healthEvents = 0;
            first.StatusesChanged += () => changes++; first.StatusLifecycle += _ => lifecycle++;
            first.StateRestored += _ => healthEvents++;
            var firstIdentity = first.Identity.Id; var secondIdentity = second.Identity.Id;
            AssertNoEngine(first); AssertNoEngine(second);
            Assert.That(CombatSaveSystem.TryValidate(saved, catalog, references, out var validationError), Is.False);
            Assert.That(CombatSaveSystem.TryRestore(saved, catalog, references, out _, out var restoreError), Is.False);
            Assert.That(restoreError, Is.EqualTo(validationError));
            if (statusesOff) Assert.That(restoreError, Does.Contain("disabled"));
            AssertNoEngine(first); AssertNoEngine(second);
            Assert.That(first.Current, Is.EqualTo(100)); Assert.That(second.Current, Is.EqualTo(100));
            Assert.That(first.Identity.Id, Is.EqualTo(firstIdentity)); Assert.That(second.Identity.Id, Is.EqualTo(secondIdentity));
            Assert.That(changes + lifecycle + healthEvents, Is.Zero);
        }

        [Test] public void DisablingStatusSupportFromTickDefersClearAndStopsRemainingTicks()
        {
            var effect = ScriptableObject.CreateInstance<DisableNativeStatusesTick>(); cleanup.Add(effect);
            slow.TickEffects = new StatusTickEffect[] { effect }; slow.TickInterval = .1f; slow.Duration = 1;
            var removals = new List<StatusChange>(); first.StatusLifecycle += change =>
            { if (change.Kind == StatusChangeKind.Removed) removals.Add(change); };
            Assert.That(first.ApplyStatus(slow), Is.True); Assert.That(first.ApplyStatus(slow), Is.True);
            Assert.That(first.GetStatMultiplier(speed), Is.EqualTo(.25f));
            first.Statuses.Advance(.25f);
            Assert.That(effect.Calls, Is.EqualTo(1));
            Assert.That(first.StatusesEnabled, Is.False); Assert.That(first.StatusCount, Is.Zero); Assert.That(first.StatusRecordCount, Is.Zero);
            Assert.That(first.GetStatMultiplier(speed), Is.EqualTo(1)); Assert.That(first.StatusRestrictions, Is.EqualTo(CombatRestriction.None));
            Assert.That(removals.Count, Is.GreaterThan(0));
            Assert.That(removals.TrueForAll(change => change.RemovalReason == StatusRemovalReason.Disabled), Is.True);
        }

        [Test] public void TransientDisablingStatusesAfterPrepareRejectsBeforeAnyActorOrModuleCommit()
        {
            var mana = Definition<ResourceDefinition>(); mana.Maximum = 100; mana.RegenerationPerSecond = 0;
            var profile = Definition<ResourceProfile>(); profile.Resources = new[] { mana };
            var resources = first.gameObject.AddComponent<CombatResources>();
            Assert.That(resources.Configure(profile), Is.True);
            Assert.That(resources.TrySpend(new[] { new ResourceCost(mana, 30) }), Is.True);
            var control = first.gameObject.AddComponent<CombatControl>();
            Assert.That(control.TryRestrict(CombatRestriction.Cast, out var savedLease), Is.True);
            first.ApplyDamage(new DamageInfo(5)); second.ApplyDamage(new DamageInfo(20));
            Assert.That(second.ApplyStatus(slow), Is.True);
            var entries = new CombatCatalogEntry[definitions.Count];
            for (int i = 0; i < entries.Length; i++) entries[i] = new CombatCatalogEntry("native-" + i, definitions[i]);
            Assert.That(catalog.Configure(entries, "native-status-tests"), Is.True);
            var incoming = Capture();

            // Replace the recipient with an unused native Health and change the other actor's live modules.
            // A failed restore must retain these values and the current live handle, not the incoming ones.
            Assert.That(references.Unregister("b"), Is.True); Object.DestroyImmediate(second.gameObject); second = Actor("b");
            first.ResetToFull(); resources.ResetResources();
            Assert.That(resources.TrySpend(new[] { new ResourceCost(mana, 10) }), Is.True);
            Assert.That(savedLease.Remove(), Is.True);
            Assert.That(control.TryRestrict(CombatRestriction.Move, out var liveLease), Is.True);
            var firstIdentity = first.Identity.Id; var secondIdentity = second.Identity.Id;
            int restored = 0, statusChanges = 0;
            first.StateRestored += _ => restored++; second.StateRestored += _ => restored++;
            first.StatusesChanged += () => statusChanges++; second.StatusesChanged += () => statusChanges++;
            var transientObject = new GameObject("Status gate restore transient"); cleanup.Add(transientObject);
            var transient = transientObject.AddComponent<DisableStatusesDuringRestoreTransient>(); transient.Target = second;

            Assert.That(CombatSaveSystem.TryValidate(incoming, catalog, references, out var validationError), Is.True, validationError);
            Assert.That(transient.Calls, Is.Zero); Assert.That(second.StatusesEnabled, Is.True); AssertNoEngine(first); AssertNoEngine(second);
            Assert.That(CombatSaveSystem.TryRestore(incoming, catalog, references, out var result, out var error), Is.False);
            Assert.That(result, Is.Null); Assert.That(error, Does.Contain("disabled"));
            Assert.That(transient.Calls, Is.EqualTo(1));
            Assert.That(second.StatusesEnabled, Is.False, "The callback's deliberate configuration change persists; it is not a restore commit.");
            Assert.That(first.Current, Is.EqualTo(100)); Assert.That(second.Current, Is.EqualTo(100));
            Assert.That(first.Identity.Id, Is.EqualTo(firstIdentity)); Assert.That(second.Identity.Id, Is.EqualTo(secondIdentity));
            Assert.That(resources.TryGet(mana, out var retained), Is.True); Assert.That(retained.Current, Is.EqualTo(90));
            Assert.That(control.Allows(CombatRestriction.Move), Is.False); Assert.That(control.Allows(CombatRestriction.Cast), Is.True);
            Assert.That(liveLease.Remove(), Is.True); Assert.That(restored + statusChanges, Is.Zero);
            AssertNoEngine(first); AssertNoEngine(second);
            second.StatusesEnabled = true;
            Assert.That(second.StatusCount, Is.Zero); Assert.That(second.GetStatMultiplier(speed), Is.EqualTo(1)); AssertNoEngine(second);
        }
    }
}
