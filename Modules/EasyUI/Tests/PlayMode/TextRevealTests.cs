using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyUI.Tests
{
    public sealed class TextRevealTests
    {
        [UnityTest]
        public IEnumerator LatestSubtitleWinsAndPooledLabelsRespectReadingPreferences()
        {
            var root = new GameObject("Reveal test", typeof(RectTransform));
            try
            {
                var player = root.AddComponent<WindowManager>();
                var child = new GameObject("Subtitle", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                child.SetActive(false);
                var label = UIText.Add(child, "", UITextRole.Body);
                var original = label.fontSharedMaterial;
                var reveal = child.AddComponent<UITextReveal>();
                reveal.playOnEnable = false;
                reveal.duration = 3;
                reveal.Play("Первый субтитр");
                Assert.False(reveal.IsRevealing);
                child.SetActive(true);
                Assert.True(reveal.IsRevealing);
                reveal.Play("Цена: 5 ₽");
                Assert.AreEqual("Цена: 5 ₽", label.text);
                Assert.True(reveal.IsRevealing);
                var prefs = player.ReadingPreferences;
                prefs.reducedMotion = true;
                player.ReadingPreferences = prefs;
                Assert.False(reveal.IsRevealing);
                Assert.AreSame(original, label.fontSharedMaterial);
                child.SetActive(false);
                reveal.Play("Следующая строка");
                child.SetActive(true);
                Assert.False(reveal.IsRevealing);
                Assert.AreEqual("Следующая строка", label.text);
                player.ReadingPreferences = UIReadingPreferences.Default;
                reveal.Replay();
                reveal.Complete();
                Assert.False(reveal.IsRevealing);
                Assert.AreSame(original, label.fontSharedMaterial);
                yield return null;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
