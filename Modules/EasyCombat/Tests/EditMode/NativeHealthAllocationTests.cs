using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyCombat.Tests
{
    public sealed class RejectedNativeCapture : StatusTickEffect
    {
        public int Calls;
        public override bool TryCapture(in StatusCaptureContext context, out float value)
        { Calls++; value = 0; return false; }
        public override void Execute(in StatusTickContext context) { }
    }

    public sealed class NativeHealthAllocationTests
    {
        [Test] public void NeutralQueriesOfWarmDefaultActorsAllocateNoEventsWithPositiveControl()
        {
            var actors = new Health[32];
            var stat = ScriptableObject.CreateInstance<CombatStatDefinition>();
            var type = ScriptableObject.CreateInstance<DamageTypeDefinition>();
            double checksum = 0;
            try
            {
                for (int i = 0; i < actors.Length; i++) actors[i] = new GameObject("Native lazy health").AddComponent<Health>();
                Action work = () =>
                {
                    double sum = 0;
                    for (int step = 0; step < 1000; step++)
                        for (int i = 0; i < actors.Length; i++)
                        {
                            var actor = actors[i];
                            sum += actor.StatusCount + actor.StatusRecordCount + actor.GetStatMultiplier(stat)
                                + actor.GetStatusResistanceModifier(type) + (int)actor.StatusRestrictions;
                        }
                    checksum = sum;
                };
                work();
                Assert.That(CombatAllocationMeter.TryMeasure(work, out var events), Is.True, "Profiler empty/positive controls must validate.");
                Assert.That(events, Is.Zero); Assert.That(checksum, Is.EqualTo(32000));
                foreach (var actor in actors)
                {
                    Assert.That(actor.ExistingStatuses, Is.Null);
                    Assert.That(actor.HasAllocatedStatusStorage, Is.False);
                }
            }
            finally
            {
                foreach (var actor in actors) if (actor) Object.DestroyImmediate(actor.gameObject);
                Object.DestroyImmediate(stat); Object.DestroyImmediate(type);
            }
        }

        [Test] public void AdvancedFacadeAndRejectedFirstCaptureRetainNoOldPoolsOrDictionaries()
        {
            var actor = new GameObject("Native rejected capture").AddComponent<Health>();
            var status = ScriptableObject.CreateInstance<StatusDefinition>();
            var rejected = ScriptableObject.CreateInstance<RejectedNativeCapture>();
            status.TickEffects = new StatusTickEffect[] { rejected };
            try
            {
                var facade = actor.Statuses;
                Assert.That(actor.HasAllocatedStatusStorage, Is.False);
                Assert.That(facade.Count, Is.Zero); Assert.That(facade.Restrictions, Is.EqualTo(CombatRestriction.None));
                Assert.That(actor.ApplyStatus(status), Is.False); Assert.That(rejected.Calls, Is.EqualTo(1));
                Assert.That(actor.Statuses, Is.SameAs(facade)); Assert.That(actor.StatusCount, Is.Zero);
                Assert.That(actor.HasAllocatedStatusStorage, Is.False);
            }
            finally { Object.DestroyImmediate(actor.gameObject); Object.DestroyImmediate(status); Object.DestroyImmediate(rejected); }
        }
    }
}
