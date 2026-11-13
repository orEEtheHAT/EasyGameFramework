using System.Collections;
using System.Collections.Generic;
using EasyInteraction.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyInteraction.Tests.Gallery
{
    public sealed class GalleryHudTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        [SetUp] public void SetUp()
        {
            var go = new GameObject("Gallery HUD actor"); objects.Add(go);
            go.transform.position = new Vector3(0, 90, 0);
            actor = go.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
        }
        [TearDown] public void TearDown()
        {
            if (actor) actor.Cancel();
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
        private EventAction Action()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(go);
            go.transform.position = new Vector3(0, 90, 2);
            var action = go.AddComponent<EventAction>(); action.Execution = InteractionExecutionMode.Timed; action.Duration = 4;
            Physics.SyncTransforms(); return action;
        }
        [UnityTest]
        public IEnumerator HudCreatesListAndRadialWithoutManualCanvasSetup()
        {
            var action = Action();
            action.gameObject.AddComponent<EventAction>().ActionId = "second";
            actor.RefreshTarget();
            var hud = actor.gameObject.AddComponent<InteractionHUD>();
            yield return null;
            hud.SetMenuOpen(true);
            Assert.IsTrue(hud.MenuOpen);
            Assert.AreEqual(2, hud.VisibleActionCount);
            hud.Layout = InteractionMenuLayout.Radial;
            yield return null;
            Assert.AreEqual(2, hud.VisibleActionCount);
            hud.SetMenuOpen(false);
            Object.Destroy(hud);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManyActionsRemainReachableThroughMenuPages()
        {
            var action = Action();
            for (int i = 1; i < 13; i++)
                action.gameObject.AddComponent<EventAction>().ActionId = "action-" + i;
            actor.RefreshTarget();
            var hud = actor.gameObject.AddComponent<InteractionHUD>();
            yield return null;
            hud.SetMenuOpen(true);
            Assert.AreEqual(13, hud.ActionCount);
            hud.SetPage(hud.PageCount - 1);
            Assert.IsTrue(hud.VisibleActionCount > 0);
            Assert.AreEqual("action-10", actor.SelectedAction.ActionId);
            hud.Layout = InteractionMenuLayout.Radial;
            hud.SetPage(hud.PageCount - 1);
            Assert.AreEqual("action-12", actor.SelectedAction.ActionId);
            Object.Destroy(hud);
            yield return null;
        }
    }
}
