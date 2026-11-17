using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Editor
{
    public sealed class FocusTargetConfigurationTests
    {
        [Test]
        public void FreshTargetAllowsHighlightButSavedOptOutAndRendererSelectionSurvivePrefabReload()
        {
            var root = new GameObject("Target-only outline configuration");
            var path = "Assets/EGF_OutlineTarget_" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                var target = root.AddComponent<InteractionTarget>();
                Assert.IsTrue(target.HighlightWhenFocused);
                Assert.IsTrue(new SerializedObject(target).FindProperty("highlightWhenFocused").boolValue);
                Assert.IsEmpty(target.OutlineRenderers);
                var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.SetParent(root.transform, false);
                var renderer = model.GetComponent<Renderer>();
                target.OutlineRenderers = new[] { renderer };
                target.HighlightWhenFocused = false;
                Assert.AreEqual(1, root.GetComponents<MonoBehaviour>().Length, "No per-item outline component is required.");
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<InteractionTarget>();
                Assert.IsFalse(saved.HighlightWhenFocused, "Explicitly serialized false must override the fresh default.");
                Assert.IsFalse(new SerializedObject(saved).FindProperty("highlightWhenFocused").boolValue);
                Assert.AreEqual(1, saved.OutlineRenderers.Length);
                Assert.AreEqual("Cube", saved.OutlineRenderers[0].name);
                Assert.AreEqual(saved.transform, saved.OutlineRenderers[0].transform.parent);
                Assert.AreEqual(1, saved.GetComponents<MonoBehaviour>().Length);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                Object.DestroyImmediate(root);
            }
        }
    }
}
