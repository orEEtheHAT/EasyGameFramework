using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyBuilding.Tests
{
    public sealed class DefinitionIdTests
    {
        [Test]
        public void FirstSaveReimportCopyAndMoveKeepCorrectStableIds()
        {
            string folder = "Assets/PackageTests/EasyBuilding/DefinitionIds-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            try
            {
                string original = folder + "/Original.asset";
                var definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                AssetDatabase.CreateAsset(definition, original);
                string id = definition.Id;
                Assert.That(id, Is.EqualTo(AssetDatabase.AssetPathToGUID(original)));
                AssetDatabase.ImportAsset(original, ImportAssetOptions.ForceUpdate);
                Assert.That(definition.Id, Is.EqualTo(id));
                Assert.That(new SerializedObject(definition).FindProperty("stableId").stringValue, Is.EqualTo(id));
                Assert.That(AssetDatabase.CopyAsset(original, folder + "/Copy.asset"), Is.True);
                var copy = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(folder + "/Copy.asset");
                Assert.That(copy.Id, Is.Not.EqualTo(id));
                Assert.That(new SerializedObject(copy).FindProperty("stableId").stringValue, Is.EqualTo(copy.Id));
                Assert.That(AssetDatabase.MoveAsset(original, folder + "/Moved.asset"), Is.Empty);
                Assert.That(definition.Id, Is.EqualTo(id));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
    }
}
