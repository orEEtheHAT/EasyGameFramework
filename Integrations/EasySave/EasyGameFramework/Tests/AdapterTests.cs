using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EasyBuilding;
using EasyFeedbacks;
using EasyResources;
using EasySave.Integrations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace EasySave.Integration.Tests
{
    public sealed class AdapterTests
    {
        private string root;
        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "EasySaveAdapters-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i]) UnityEngine.Object.DestroyImmediate(cleanup[i]); cleanup.Clear(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        private T Keep<T>(T item) where T : UnityEngine.Object { cleanup.Add(item); return item; }
        private static IEnumerator Run(Func<Task> body) { var task = body(); var deadline = DateTime.UtcNow.AddSeconds(20); while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null; Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult(); }
        private static void Ok(SaveResult result) => Assert.IsTrue(result.Success, result.ToString());
        [UnityTest] public IEnumerator ResourceRestoreAssignsBalanceWithoutDuplicateReward() => Run(async () =>
        {
            var wood = Keep(ResourceDefinition.Create("Wood", "wood")); using var store = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 10)));
            using var saves = new SaveService(new LocalFileStorage(root)); saves.Register(EasySaveAdapters.Resources("wallet", store, new[] { wood }));
            Ok(await saves.QuickSaveAsync()); store.Add(wood, 15); Ok(await saves.QuickLoadAsync()); Assert.AreEqual(10, store.GetTotal(wood));
            Ok(await saves.QuickLoadAsync()); Assert.AreEqual(10, store.GetTotal(wood));
        });
        [UnityTest] public IEnumerator ReservationsAreNotCancelledOrPaidByCaptureOrPreflight() => Run(async () =>
        {
            var wood = Keep(ResourceDefinition.Create("Wood", "wood")); using var store = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 10)));
            using var saves = new SaveService(new LocalFileStorage(root)); saves.Register(EasySaveAdapters.Resources("wallet", store, new[] { wood })); Ok(await saves.QuickSaveAsync());
            Assert.IsTrue(store.TryReserve(new ResourceCost(new ResourceAmount(wood, 3)), out var held));
            Assert.AreEqual(SaveError.CaptureFailed, (await saves.QuickSaveAsync()).Error);
            var loaded = await saves.QuickLoadAsync(); Assert.AreEqual(SaveError.ValidationFailed, loaded.Error); Assert.IsFalse(loaded.WorldMayHaveChanged);
            Assert.AreEqual(1, store.ActiveReservationCount); Assert.AreEqual(10, store.GetTotal(wood)); held.Cancel();
        });
        [UnityTest] public IEnumerator UnknownResourceFailsBeforeOtherParticipantMutation() => Run(async () =>
        {
            var wood = Keep(ResourceDefinition.Create("Wood", "wood")); using var store = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 10)));
            using (var writer = new SaveService(new LocalFileStorage(root))) { writer.Register(EasySaveAdapters.Resources("wallet", store, new[] { wood })); Ok(await writer.QuickSaveAsync()); }
            using var reader = new SaveService(new LocalFileStorage(root)); reader.Register(EasySaveAdapters.Resources("wallet", store, Array.Empty<ResourceDefinition>()));
            var result = await reader.QuickLoadAsync(); Assert.AreEqual(SaveError.ValidationFailed, result.Error); Assert.IsFalse(result.WorldMayHaveChanged); Assert.AreEqual(10, store.GetTotal(wood));
        });
        [UnityTest] public IEnumerator FeedbackPreferencesRemainSeparateFromProgress() => Run(async () =>
        {
            var before = FeedbackSettings.Capture();
            try
            {
                using var saves = new SaveService(new LocalFileStorage(root)); saves.Register(EasySaveAdapters.Feedbacks());
                var state = before; state.MasterVolume = .3f; FeedbackSettings.Restore(state); Ok(await saves.SavePreferencesAsync()); Ok(await saves.QuickSaveAsync());
                state.MasterVolume = .8f; FeedbackSettings.Restore(state); Ok(await saves.QuickLoadAsync()); Assert.AreEqual(.8f, FeedbackSettings.Current.MasterVolume);
                Ok(await saves.LoadPreferencesAsync()); Assert.AreEqual(.3f, FeedbackSettings.Current.MasterVolume);
            }
            finally { FeedbackSettings.Restore(before); }
        });
        [Test] public void FeedbackNonfinitePayloadRejected()
        {
            var participant = EasySaveAdapters.Feedbacks(); var state = FeedbackSettingsState.Default; state.MasterVolume = float.NaN;
            Assert.IsFalse(participant.Validate(JsonUtility.ToJson(state)).IsValid);
        }
        [UnityTest] public IEnumerator ProductionInputPreferencesAreSeparateFromWalletProgress() => Run(async () =>
        {
            var go = Keep(new GameObject("Player preferences")); go.SetActive(false);
            var actions = Keep(ScriptableObject.CreateInstance<UnityEngine.InputSystem.InputActionAsset>());
            actions.AddActionMap("Gameplay").AddAction("Use", UnityEngine.InputSystem.InputActionType.Button, "<Keyboard>/e");
            var player = go.AddComponent<EasyInput.PlayerInputSettings>(); player.ControlsCursor = false; player.Configure(actions);
            var wood = Keep(ResourceDefinition.Create("Wood", "wood")); using var wallet = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 10)));
            using var saves = new SaveService(new LocalFileStorage(root));
            saves.RegisterAll(EasySaveAdapters.Input("player/input", player), EasySaveAdapters.Resources("wallet", wallet, new[] { wood }));
            var original = player.CaptureSettings(); Ok(await saves.QuickSaveAsync()); Ok(await saves.SavePreferencesAsync());
            var settings = player.Settings; settings.MouseSensitivity *= 2; player.SetSettings(settings); wallet.Add(wood, 4);
            Ok(await saves.QuickLoadAsync()); Assert.AreEqual(10, wallet.GetTotal(wood)); Assert.AreEqual(settings.MouseSensitivity, player.Settings.MouseSensitivity);
            wallet.Add(wood, 1); Ok(await saves.LoadPreferencesAsync()); Assert.AreEqual(11, wallet.GetTotal(wood));
            Assert.AreEqual(JsonUtility.ToJson(original), JsonUtility.ToJson(player.CaptureSettings()));
        });
        [UnityTest] public IEnumerator NamedLedgerHoldRebindsAndChargesOnlyOnce() => Run(async () =>
        {
            var wood = Keep(ResourceDefinition.Create("Wood", "wood")); using var wallet = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 10)));
            Assert.IsTrue(wallet.TryReserve(new ResourceCost(new ResourceAmount(wood, 3)), out var original, out _, new ResourceReservationOptions("craft/1", 1000, 100)));
            long now = 100;
            using var saves = new SaveService(new LocalFileStorage(root));
            saves.Register(ResourceLedgerSave.Create("wallets", new[] { "player" }, new[] { wallet }, new[] { wood }, true, () => now));
            var snapshot = saves.CaptureSnapshot(); now = 500;
            Ok(await saves.RestoreSnapshotAsync(snapshot)); Assert.IsFalse(original.IsActive);
            Assert.IsTrue(wallet.TryGetReservation(original.Id, "craft/1", out var rebound));
            Assert.AreEqual(10, wallet.GetTotal(wood)); Assert.AreEqual(900, wallet.CaptureState(true, now).Reservations[0].RemainingMilliseconds);
            Assert.IsTrue(rebound.Commit()); Assert.IsFalse(rebound.Commit()); Assert.AreEqual(7, wallet.GetTotal(wood));
        });
        [UnityTest] public IEnumerator FullBuildingRestorePreservesIdsWithoutPayment() => Run(async () =>
        {
            var world = Keep(new GameObject("Building world")).AddComponent<BuildingSystem>();
            var prefab = Keep(new GameObject("Building prototype")); prefab.SetActive(false); prefab.layer = 31; prefab.AddComponent<BoxCollider>();
            var definition = Keep(ScriptableObject.CreateInstance<BuildingDefinition>()); definition.Prefab = prefab; definition.RequireSurface = false; definition.ObstacleLayers = 1 << 31;
            world.Select(definition); Assert.IsTrue(world.TryConfirm(new PlacementRequest(new Vector3(20000, 0, 20000), Quaternion.identity), out var building, out var placement), placement.Reason);
            string id = building.InstanceId;
            using var saves = new SaveService(new LocalFileStorage(root)); saves.Register(EasySaveAdapters.Buildings("world", world, new[] { definition }));
            Ok(await saves.QuickSaveAsync()); Ok(await saves.QuickLoadAsync()); Ok(await saves.QuickLoadAsync());
            Assert.AreEqual(1, world.Count); Assert.IsTrue(world.TryGet(id, out _)); Assert.IsFalse(world.IsReadOnly);
        });
        [UnityTest] public IEnumerator BuildingAdapterReplacesParcelHistoryExactly() => Run(async () =>
        {
            var world = Keep(new GameObject("Building world")).AddComponent<BuildingSystem>();
            var participant = EasySaveAdapters.Buildings("world", world, Array.Empty<BuildingDefinition>());
            var state = world.CaptureState(); state.RedeemedParcels.Add("already-redeemed"); var json = JsonUtility.ToJson(state);
            Assert.IsTrue(participant.Validate(json).IsValid);
            await participant.RestoreAsync(json, default); await participant.RestoreAsync(json, default);
            CollectionAssert.AreEqual(new[] { "already-redeemed" }, world.CaptureState().RedeemedParcels);
            state.RedeemedParcels.Clear(); await participant.RestoreAsync(JsonUtility.ToJson(state), default);
            Assert.IsEmpty(world.CaptureState().RedeemedParcels);
        });
    }
}
