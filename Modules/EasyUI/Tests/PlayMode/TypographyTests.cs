using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyUI.Tests
{
    public sealed class TypographyTests
    {
        [UnityTest]
        public IEnumerator NewLabelsUseRolesAndExplicitSizesArePreserved()
        {
            var parent = new GameObject("Typography test", typeof(RectTransform));
            try
            {
                var manager = parent.AddComponent<WindowManager>();
                GameObject Child() { var go = new GameObject("Label", typeof(RectTransform)); go.transform.SetParent(parent.transform, false); return go; }
                var body = UIText.Add(Child(), "Body");
                var customSize = UIText.Add(Child(), "Fixed size", 13f);
                var gold = UIText.Add(Child(), "Ёж №7", UITextRole.Title);
                var original = gold.fontSharedMaterial;
                var settings = manager.ReadingPreferences;
                settings.scale = 1.5f;
                settings.reducedMotion = true;
                manager.ReadingPreferences = settings;
                Assert.AreEqual(33, body.fontSize);
                Assert.AreEqual(13, customSize.fontSize);
                Assert.AreNotSame(original, gold.fontSharedMaterial);
                Assert.AreEqual(0, gold.fontSharedMaterial.GetFloat("_SheenStrength"));
                settings.highContrast = true;
                manager.ReadingPreferences = settings;
                Assert.IsFalse(gold.enableVertexGradient);
                Assert.IsTrue(gold.overrideColorTags);
                Assert.AreSame(gold.fontSharedMaterial, body.fontSharedMaterial);
                Assert.AreEqual(Color.white, gold.color);
                manager.ReadingPreferences = UIReadingPreferences.Default;
                Assert.AreSame(original, gold.fontSharedMaterial);
                Assert.AreEqual(22, body.fontSize);
                yield return null;
            }
            finally { Object.DestroyImmediate(parent); }
        }
    }
}
