using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Tests
{
    public sealed class ResourceDatabaseTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        private ResourceDefinition Coin() => Own(ResourceDefinition.Create("Coin"));
        private ResourceDatabase Database(params ResourceDefinition[] entries) => Own(ResourceDatabase.Create("Database", entries));
        private ResourceRewardProfile Profile(ResourceDatabase database, params ResourceRewardRange[] ranges) =>
            Own(ResourceRewardProfile.Create("Reward", database, ranges));

        private sealed class CountingRandom : System.Random
        {
            public int Calls { get; private set; }
            public override int Next(int maxValue) { Calls++; return 0; }
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void DatabaseClonesInputAndExposesReadOnlyDirectRecords()
        {
            var coin = Coin(); var other = Coin();
            var source = new[] { coin };
            var database = Database(source);
            source[0] = other;
            Assert.That(database.Resources.Count, Is.EqualTo(1));
            Assert.That(database.Resources[0], Is.SameAs(coin));
            Assert.That(database.Contains(coin), Is.True);
            Assert.That(database.Contains(other), Is.False);
            Assert.That(database.Contains(null), Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<ResourceDefinition>)database.Resources)[0] = other);
            Assert.That(new List<ResourceDefinition>(database), Is.EqualTo(new[] { coin }));
        }

        [Test]
        public void LookupUsesStableIdAndCannotTreatSameIdForeignObjectAsMember()
        {
            var coin = Coin();
            var foreign = Own(ResourceDefinition.Create("Lookalike", stableId: coin.Id));
            var database = Database(coin);
            Assert.That(database.TryGet(coin.Id, out var found), Is.True);
            Assert.That(found, Is.SameAs(coin));
            Assert.That(database.Contains(foreign), Is.False);
            Assert.That(database.TryGet("missing", out found), Is.False); Assert.That(found, Is.Null);
            Assert.That(database.TryGet(null, out found), Is.False);
        }

        [Test]
        public void NullAndDuplicateRecordsAreRejectedAsConfigurationErrors()
        {
            var coin = Coin();
            var copy = Own(ResourceDefinition.Create("Copy", stableId: coin.Id));
            Assert.Throws<ArgumentNullException>(() => ResourceDatabase.Create("Missing", null));
            Assert.Throws<ArgumentException>(() => ResourceDatabase.Create("Null entry", new ResourceDefinition[] { null }));
            Assert.Throws<ArgumentException>(() => ResourceDatabase.Create("Repeated entry", coin, coin));
            Assert.Throws<ArgumentException>(() => ResourceDatabase.Create("Repeated ID", coin, copy));
        }

        [Test]
        public void RewardRejectsMissingDatabaseEvenWhenItWouldBeEmpty()
        {
            Assert.Throws<ArgumentException>(() => ResourceRewardProfile.Create("Missing", null));
            var database = Database();
            var profile = Profile(database);
            Assert.That(profile.Roll(new CountingRandom()).IsEmpty, Is.True);
        }

        [Test]
        public void ForeignDatabaseAssignmentKeepsOriginalBindingAndRewardRows()
        {
            var coin = Coin(); var foreign = Own(ResourceDefinition.Create("Copy", stableId: coin.Id));
            var database = Database(coin); var otherDatabase = Database(foreign);
            var profile = Profile(database, new ResourceRewardRange(coin, 3, 7));
            var json = JsonUtility.ToJson(profile);
            Assert.Throws<ArgumentException>(() => profile.Database = otherDatabase);
            Assert.Throws<ArgumentException>(() => profile.Database = null);
            Assert.That(profile.Database, Is.SameAs(database));
            Assert.That(profile.Ranges[0].Resource, Is.SameAs(coin));
            Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(json));
        }

        [Test]
        public void SerializedForeignSelectionFailsBeforeAnyRandomValueIsConsumed()
        {
            var coin = Coin(); var foreign = Coin();
            var database = Database(coin); var otherDatabase = Database(foreign);
            var profile = Profile(database, new ResourceRewardRange(coin, 3, 7));
            var data = new SerializedObject(profile);
            data.FindProperty("database").objectReferenceValue = otherDatabase;
            data.ApplyModifiedPropertiesWithoutUndo();
            var random = new CountingRandom();
            Assert.Throws<ArgumentException>(() => profile.Roll(random));
            Assert.That(profile.Ranges[0].Resource, Is.SameAs(coin), "Changing a serialized database must not clear or substitute the old row.");
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void MissingOrDuplicateSerializedRecordIsNotSilentlySkipped()
        {
            var coin = Coin(); var database = Database(coin);
            var profile = Profile(database, new ResourceRewardRange(coin, 3, 7));
            var data = new SerializedObject(database);
            var rows = data.FindProperty("resources");
            rows.arraySize = 2; rows.GetArrayElementAtIndex(1).objectReferenceValue = coin;
            data.ApplyModifiedPropertiesWithoutUndo(); database.OnAfterDeserialize();
            var random = new CountingRandom();
            Assert.Throws<ArgumentException>(() => profile.Roll(random));
            rows = data.FindProperty("resources"); rows.arraySize = 1; rows.GetArrayElementAtIndex(0).objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo(); database.OnAfterDeserialize();
            Assert.Throws<ArgumentException>(() => database.ValidateConfiguration());
            Assert.Throws<ArgumentException>(() => profile.Roll(random));
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void WholeDatabaseValidationDoesNotMutateDefinitionsWalletOrSavedDto()
        {
            var coin = Coin(); var database = Database(coin);
            var profile = Profile(database, new ResourceRewardRange(coin, 4, 4));
            var definition = JsonUtility.ToJson(coin); var config = JsonUtility.ToJson(database);
            using var store = new ResourceStore(new ResourceCost(new ResourceAmount(coin, 10)));
            var saved = store.CaptureState(); var snapshot = JsonUtility.ToJson(saved);
            database.ValidateConfiguration(); profile.ValidateConfiguration();
            store.ValidateState(saved, database);
            Assert.That(store.GetTotal(coin), Is.EqualTo(10)); Assert.That(store.Revision, Is.Zero);
            Assert.That(JsonUtility.ToJson(coin), Is.EqualTo(definition));
            Assert.That(JsonUtility.ToJson(database), Is.EqualTo(config));
            Assert.That(JsonUtility.ToJson(saved), Is.EqualTo(snapshot));
        }
    }
}
