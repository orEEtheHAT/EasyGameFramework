using System.Collections;
using EasyBuilding.TwoD;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyBuilding.PlayModeTests.TwoD
{
    public sealed class SharedSession2DTests
    {
        [UnityTest] public IEnumerator TwoDInputBlocksPruneInactiveOwnersWithoutClearingOtherRequests()
        {
            var root = new GameObject("2D controls"); var window = new GameObject("Window");
            try
            {
                var placer = root.AddComponent<BuildingPlacer2D>(); var owner = window.AddComponent<BuildingActor>(); object token = new object();
                placer.SetInputBlocked(owner, true); placer.SetInputBlocked(token, true); owner.enabled = false;
                Assert.That(placer.InputBlocked, Is.True); placer.SetInputBlocked(token, false); Assert.That(placer.InputBlocked, Is.False);
                owner.enabled = true; Assert.That(placer.InputBlocked, Is.False); placer.SetInputBlocked(owner, true);
                Object.Destroy(window); yield return null; Assert.That(placer.InputBlocked, Is.False);
            }
            finally { Object.DestroyImmediate(root); if (window != null) Object.DestroyImmediate(window); }
        }
        [UnityTest] public IEnumerator BorrowedActorSessionSurvivesDisableDestroyAndRejectsAnotherWorld()
        {
            var root = new GameObject("2D controls"); var actorObject = new GameObject("Actor"); var otherObject = new GameObject("Other world");
            try
            {
                var actor = actorObject.AddComponent<BuildingActor>(); var world = root.AddComponent<BuildingSystem>();
                var placer = root.AddComponent<BuildingPlacer2D>(); placer.Actor = actor;
                var shared = actor.GetSession(world); Assert.That(placer.Session, Is.SameAs(shared));
                placer.SetSession(shared); placer.Actor = null;
                Assert.That(placer.Session, Is.SameAs(shared), "An explicit binding must stay explicit even when it was already current.");
                using var foreign = otherObject.AddComponent<BuildingSystem>().CreateSession();
                Assert.Throws<System.ArgumentException>(() => placer.SetSession(foreign)); Assert.That(placer.Session, Is.SameAs(shared));
                placer.enabled = false; Assert.That(shared.IsDisposed, Is.False);
                placer.enabled = true; Assert.That(placer.Session, Is.SameAs(shared));
                using var explicitSession = world.CreateSession(); placer.SetSession(explicitSession);
                Object.Destroy(placer); yield return null; Assert.That(shared.IsDisposed, Is.False); Assert.That(explicitSession.IsDisposed, Is.False);
                Object.Destroy(actor); yield return null; Assert.That(shared.IsDisposed, Is.True); Assert.That(explicitSession.IsDisposed, Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(actorObject); Object.DestroyImmediate(otherObject); }
        }
    }
}
