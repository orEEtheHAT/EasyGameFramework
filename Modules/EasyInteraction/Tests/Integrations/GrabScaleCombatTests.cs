using EasyCombat;
using EasyInteraction.Integrations.Combat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class GrabScaleCombatTests
    {
        [Test] public void CombatDeathRestoresTheHeldBodyScaleBeforeDisablingItsActor()
        {
            var player = new GameObject("Scale death actor");
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                player.transform.position = new Vector3(0, 1800, 0);
                var actor = player.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
                var health = player.AddComponent<Health>();
                var guard = player.AddComponent<CombatInteractionGuard>();
                guard.cancelOnDamage = false; // Exercise the death path, not the earlier damage interruption.
                item.transform.position = player.transform.position + Vector3.forward * 2;
                var original = new Vector3(1.2f, .8f, 1.1f);
                item.transform.localScale = original;
                var grab = item.AddComponent<GrabAction>();
                grab.HeldScaleMultiplier = .5f;
                grab.IgnoreOwnerCollisions = false;
                Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);
                Assert.AreEqual(original * .5f, item.transform.localScale);
                health.ApplyDamage(new DamageInfo(health.Maximum + 1));
                Assert.IsFalse(health.IsAlive);
                Assert.IsFalse(actor.enabled);
                Assert.IsFalse(grab.IsHeld);
                Assert.AreEqual(original, item.transform.localScale);
                Assert.IsTrue(grab.Body.useGravity);
            }
            finally { Object.DestroyImmediate(item); Object.DestroyImmediate(player); }
        }
    }
}
