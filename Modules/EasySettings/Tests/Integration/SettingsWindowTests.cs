using System.Collections;
using EasySettings.UI;
using EasySettings.Integrations;
using EasyUI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
            Assert.AreSame(window.FirstSelected.gameObject, manager.EventSystem.currentSelectedGameObject);
            Assert.AreEqual("Apply", window.FirstSelected.GetComponentInChildren<TMP_Text>().text);
            var labels = go.GetComponentsInChildren<TMP_Text>();
            Assert.IsNotEmpty(labels);
            foreach (var label in labels) {
                Assert.AreSame(UIText.DefaultFont, label.font);
                Assert.IsFalse(label.raycastTarget);
            }
            var scaleButton = view.ExtensionsRoot.Find("scale").GetComponentInChildren<Button>();
            var draft = controller.Draft;
            draft.RenderScale = 1f;
            controller.Draft = draft;
            var applied = controller.Applied;
            manager.EventSystem.SetSelectedGameObject(scaleButton.gameObject);
            ExecuteEvents.Execute(scaleButton.gameObject, new BaseEventData(manager.EventSystem), ExecuteEvents.submitHandler);
            Assert.AreEqual(1.25f, controller.Draft.RenderScale);
            Assert.AreEqual(applied, controller.Applied);
            Assert.That(scaleButton.GetComponentInChildren<TMP_Text>().text, Does.StartWith(1.25f.ToString("0.00")));
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

        [UnityTest] public IEnumerator LocalizedLabelsRenderWithTheWindowManagersFont()
        {
            var defaultFont = UIText.DefaultFont;
            var customFont = Object.Instantiate(defaultFont);
            // TMP destroys its atlas textures and material with the font asset.
            customFont.atlasTextures = System.Array.ConvertAll(defaultFont.atlasTextures, atlas => atlas ? Object.Instantiate(atlas) : null);
            customFont.material = new Material(defaultFont.material) { mainTexture = customFont.atlasTextures[0] };
            var canvas = new GameObject("Settings font test canvas", typeof(RectTransform), typeof(Canvas));
            try {
                var manager = canvas.AddComponent<WindowManager>();
                manager.Font = customFont;
                var go = new GameObject("Settings font test view", typeof(RectTransform));
                go.SetActive(false);
                go.transform.SetParent(canvas.transform, false);
                var window = go.AddComponent<UIWindow>();
                window.Policy.FadeIn = window.Policy.FadeOut = 0;
                var view = go.AddComponent<SettingsWindow>();
                var translations = new TestTranslations();
                view.TextProvider = translations;
                manager.Open(window);
                yield return null;

                var labels = go.GetComponentsInChildren<TMP_Text>();
                var title = System.Array.Find(labels, label => label.text == "Graphics & display");
                Assert.IsNotNull(title);
                foreach (var label in labels) Assert.AreSame(customFont, label.font);

                translations.UseRussian();
                Assert.AreEqual("Графика и экран", title.text);
                var closeCaption = go.transform.Find("Close").GetComponentInChildren<TMP_Text>();
                Assert.AreEqual("Закрыть", closeCaption.text);
                foreach (var caption in new[] { title, closeCaption }) {
                    caption.ForceMeshUpdate();
                    Assert.AreEqual(caption.text.Length, caption.textInfo.characterCount);
                    for (int i = 0; i < caption.text.Length; i++) {
                        if (char.IsWhiteSpace(caption.text[i])) continue;
                        var glyph = caption.textInfo.characterInfo[i].textElement;
                        Assert.IsNotNull(glyph);
                        Assert.AreEqual((uint)caption.text[i], glyph.unicode, "Missing rendered glyph: " + caption.text[i]);
                    }
                }
            } finally {
                Object.Destroy(canvas);
                Object.Destroy(customFont);
            }
            yield return null;
        }

        sealed class TestTranslations : ISettingsTextProvider
        {
            bool russian;
            public event System.Action Changed;
            public void UseRussian() { russian = true; Changed?.Invoke(); }
            public string GetText(string key, string fallback)
            {
                if (!russian) return fallback;
                switch (key) {
                    case "settings.title": return "Графика и экран";
                    case "settings.close": return "Закрыть";
                    default: return fallback;
                }
            }
        }
    }
}
