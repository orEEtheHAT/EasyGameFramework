using System.Collections;
using EasySettings.UI;
using EasySettings.Integrations;
using EasyUI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EasySettings.Tests
{
    public sealed class SettingsWindowTests
    {
        [UnityTest] public IEnumerator ReopenDoesNotDuplicateViewAndControllerRebinds()
        {
            var defaults = ScriptableObject.CreateInstance<SettingsDefaults>();
            var camera = new GameObject("Window test camera").AddComponent<Camera>(); camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var source = new GameObject("Window test settings"); source.SetActive(false); var controller = source.AddComponent<SettingsController>(); controller.Defaults = defaults; controller.TargetCamera = camera; source.SetActive(true);
            var canvas = new GameObject("Window test canvas", typeof(RectTransform), typeof(Canvas)); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; var manager = canvas.AddComponent<WindowManager>();
            var go = new GameObject("Settings test view", typeof(RectTransform)); go.SetActive(false); go.transform.SetParent(canvas.transform, false);
            var window = go.AddComponent<UIWindow>(); window.Policy.FadeIn = window.Policy.FadeOut = 0;
            var view = go.AddComponent<SettingsWindow>(); view.Controller = controller;
            var localization = go.AddComponent<LocalizedSettingsText>();
            manager.Open(window); yield return null;
            int children = go.GetComponentsInChildren<Transform>(true).Length;
            for (int i = 0; i < 3; i++) {
                var edited = controller.Session.Draft; edited.RenderScale = .7f; controller.Session.Draft = edited;
                window.Close(); yield return null;
                Assert.AreEqual(controller.Session.Applied.RenderScale, controller.Session.Draft.RenderScale);
                manager.Open(window); yield return null; Assert.AreEqual(children, go.GetComponentsInChildren<Transform>(true).Length);
                Assert.AreSame(localization, view.TextProvider);
            }
            controller.enabled = false; yield return null; controller.enabled = true; yield return null;
            var state = controller.Session.Draft; state.RenderScale = .8f; controller.Session.Draft = state; view.Apply(); Assert.AreEqual(.8f, controller.Session.Applied.RenderScale);
            Assert.IsNotEmpty(go.GetComponentsInChildren<Text>());
            // Closing only the UI from a settings notification must not throw, dispose the
            // persistent graphics owner, or leave an unapplied draft behind.
            controller.Session.Committed += window.Close;
            state = controller.Draft; state.RenderScale = .9f; controller.Draft = state; controller.Apply();
            Assert.IsFalse(window.IsActive); Assert.IsFalse(controller.Session.IsDisposed); Assert.AreEqual(.9f, controller.Applied.RenderScale);
            controller.Session.Committed -= window.Close;
            manager.Open(window); yield return null;
            controller.Session.Changed += window.Close;
            state = controller.Draft; state.RenderScale = .6f; controller.Draft = state;
            Assert.IsFalse(window.IsActive); Assert.AreEqual(controller.Applied, controller.Draft);
            controller.Session.Changed -= window.Close;
            Object.Destroy(canvas); Object.Destroy(source); Object.Destroy(camera.gameObject); Object.Destroy(defaults); yield return null;
        }
    }
}
