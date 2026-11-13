using System;
using EasyResources.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Tests
{
    public sealed class ResourceConfigurationTests
    {
        private GameObject owner;
        private ResourceContainer wallet;
        private InventoryContainer inventory;
        private ResourceDefinition wood;
        private ItemDefinition item;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Resource authoring validation");
            wallet = owner.AddComponent<ResourceContainer>();
            inventory = owner.AddComponent<InventoryContainer>();
            wood = ResourceDefinition.Create("Wood", weightPerUnit: 2, volumePerUnit: 3);
            item = ItemDefinition.Create("Item", maximumStack: 1, weightPerItem: 2, volumePerItem: 3);
            var data = new SerializedObject(wallet);
            var rows = data.FindProperty("initialContents").FindPropertyRelative("items");
            rows.arraySize = 1;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("resource").objectReferenceValue = wood;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("amount").longValue = 4;
            data.ApplyModifiedPropertiesWithoutUndo();
            data = new SerializedObject(inventory);
            rows = data.FindProperty("initialContents");
            rows.arraySize = 1;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = item;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("count").intValue = 2;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(wood);
            UnityEngine.Object.DestroyImmediate(item);
        }

        [Test]
        public void ValidationPreservesAuthoredContentsAndSharedDefinitions()
        {
            var walletJson = EditorJsonUtility.ToJson(wallet);
            var inventoryJson = EditorJsonUtility.ToJson(inventory);
            var woodJson = JsonUtility.ToJson(wood);
            var itemJson = JsonUtility.ToJson(item);
            Assert.DoesNotThrow(() => wallet.ValidateConfiguration());
            Assert.DoesNotThrow(() => inventory.ValidateConfiguration());
            Assert.That(EditorJsonUtility.ToJson(wallet), Is.EqualTo(walletJson));
            Assert.That(EditorJsonUtility.ToJson(inventory), Is.EqualTo(inventoryJson));
            Assert.That(JsonUtility.ToJson(wood), Is.EqualTo(woodJson));
            Assert.That(JsonUtility.ToJson(item), Is.EqualTo(itemJson));
        }

        [TestCase("resource", "container limit")]
        [TestCase("weight", "weight limit")]
        [TestCase("volume", "volume limit")]
        [TestCase("filter", "does not accept")]
        public void WalletReportsInitialContentsThatViolateStorage(string kind, string message)
        {
            var data = new SerializedObject(wallet);
            var storage = data.FindProperty("storage");
            if (kind == "resource")
            {
                var rows = storage.FindPropertyRelative("resourceLimits");
                rows.arraySize = 1;
                rows.GetArrayElementAtIndex(0).FindPropertyRelative("resource").objectReferenceValue = wood;
                rows.GetArrayElementAtIndex(0).FindPropertyRelative("maximum").longValue = 3;
            }
            else if (kind == "filter")
                storage.FindPropertyRelative("restrictResources").boolValue = true;
            else
            {
                storage.FindPropertyRelative(kind == "weight" ? "limitWeight" : "limitVolume").boolValue = true;
                storage.FindPropertyRelative(kind == "weight" ? "maximumWeight" : "maximumVolume").longValue = 1;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Assert.Throws<ArgumentException>(() => wallet.ValidateConfiguration()).Message,
                Does.Contain("Initial Contents").And.Contain(message));
        }

        [Test]
        public void ChangedAuthoredAmountsAreRevalidatedAfterAnEarlierSuccess()
        {
            wallet.ValidateConfiguration();
            var data = new SerializedObject(wallet);
            data.FindProperty("initialContents").FindPropertyRelative("items").GetArrayElementAtIndex(0)
                .FindPropertyRelative("amount").longValue = -1;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<ArgumentException>(() => wallet.ValidateConfiguration());
        }

        [TestCase("slots")]
        [TestCase("weight")]
        [TestCase("volume")]
        public void InventoryReportsInitialContentsThatCannotFit(string kind)
        {
            var data = new SerializedObject(inventory);
            if (kind == "slots")
                data.FindProperty("slots").intValue = 1;
            else
            {
                data.FindProperty(kind == "weight" ? "limitWeight" : "limitVolume").boolValue = true;
                data.FindProperty(kind == "weight" ? "maximumWeight" : "maximumVolume").longValue = 1;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Assert.Throws<ArgumentException>(() => inventory.ValidateConfiguration()).Message,
                Does.Contain("Initial Contents"));
        }

        [Test]
        public void MissingInitialDefinitionsAreReportedBeforePlay()
        {
            var data = new SerializedObject(wallet);
            data.FindProperty("initialContents").FindPropertyRelative("items").GetArrayElementAtIndex(0)
                .FindPropertyRelative("resource").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            data = new SerializedObject(inventory);
            data.FindProperty("initialContents").GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<ArgumentException>(() => wallet.ValidateConfiguration());
            Assert.Throws<ArgumentException>(() => inventory.ValidateConfiguration());
        }
    }
}
