using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyCombat;
using EasyInput;
using EasySave;
using EasySave.Integrations;
using UnityEngine;

namespace EasySuite.Example
{
    // The sample owns the gameplay pause/boundary; EasySave owns the storage and restore protocol.
    public sealed class EasySuiteSave : IDisposable
    {
        [Serializable] private sealed class DeliveryState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public EasyQuests.QuestSnapshot Quests;
        }
        [Serializable] private sealed class PresentationPreferences
        {
            public string graphics;
            public EasyUI.UIReadingPreferences reading;
        }
        private readonly EasySuiteLab lab;
        private readonly SaveService service;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool busy, disposed, paused;
        private float previousTimeScale;
        public SaveResult LastResult { get; private set; }
        public SaveService Service => service;
        public EasySuiteSave(EasySuiteLab lab)
        {
            this.lab = lab ? lab : throw new ArgumentNullException(nameof(lab));
            if (!lab.SaveWorkbench || !lab.GraphicsSettings || !lab.ResourceDatabase) throw new InvalidOperationException("Assign SaveWorkbench, GraphicsSettings and ResourceDatabase on the Playground Lab.");
            var references = new CombatReferenceRegistry();
            references.RegisterActor("player", lab.PlayerHealth); references.RegisterActor("target", lab.TargetHealth);
            service = new SaveService(new LocalFileStorage(Path.Combine(Application.persistentDataPath, "easygameframework-playground")));
            try
            {
                var questWorld = lab.GetComponent<EasyQuests.QuestWorld>();
                service.RegisterAll(
                    ResourceLedgerSave.Create("world/resources", new[] { "wallet", "stock" }, new[] { lab.Wallet.Store, lab.Stock.Store }, lab.ResourceDatabase.Resources),
                    EasySaveAdapters.Buildings("world/buildings", lab.Placer.System, lab.Buildings),
                    EasySaveAdapters.Combat("world/combat", lab.CombatCatalog, references),
                    EasySaveAdapters.Interaction("world/workbench", lab.SaveWorkbench),
                    EasySaveAdapters.Movement("player/movement", lab.Character),
                    DeliveryParticipant(questWorld),
                    EasySaveAdapters.Input("player/input", lab.Player),
                    EasySaveAdapters.Feedbacks("global/audio"), EasySaveAdapters.Language("global/language"),
                    PresentationParticipant());
                service.RegisterMigration(SaveScope.Preferences,"global/graphics",1, old =>
                    JsonUtility.ToJson(new PresentationPreferences { graphics=old, reading=EasyUI.UIReadingPreferences.Default }));
                if (questWorld)
                {
                    // v1 saved only the crate. Keep the stable key and initialize quests from authored defaults.
                    var fresh = new EasyQuests.QuestSession(questWorld.database, questWorld.quests);
                    fresh.StartAvailable();
                    var initial = fresh.Capture();
                    service.RegisterMigration(SaveScope.Progress, "world/crate", 1, old =>
                    {
                        var state = JsonUtility.FromJson<DeliveryState>(old);
                        state.Quests = initial;
                        return JsonUtility.ToJson(state);
                    });
                }
            }
            catch { service.Dispose(); lifetime.Dispose(); throw; }
        }
        // Keep the existing slot key so a pre-typography global preferences file still restores.
        private ISaveParticipant PresentationParticipant()
        {
            var graphics=SettingsSaveAdapter.Create(lab.GraphicsSettings,"global/graphics");
            return new JsonSaveParticipant<PresentationPreferences>(graphics.Key,SaveScope.Preferences,2,
                () => new PresentationPreferences { graphics=graphics.Capture(),reading=lab.Windows.ReadingPreferences },
                snapshot => snapshot.reading.IsValid && !string.IsNullOrEmpty(snapshot.graphics)
                    ? graphics.Validate(snapshot.graphics) : SaveValidation.Invalid("Invalid graphics or text preferences."),
                async (snapshot, token) => {
                    await graphics.RestoreAsync(snapshot.graphics,token);
                    lab.Windows.ReadingPreferences=snapshot.reading;
                },graphics.RestoreOrder);
        }

        private ISaveParticipant DeliveryParticipant(EasyQuests.QuestWorld world)
        {
            return new JsonSaveParticipant<DeliveryState>("world/crate", SaveScope.Progress, world ? 2 : 1,
                () => new DeliveryState { Position = lab.Crate.position, Rotation = lab.Crate.rotation, Quests = world ? world.Session.Capture() : null },
                state =>
                {
                    if (!lab.Crate || !Finite(state.Position) || !Finite(state.Rotation))
                        return SaveValidation.Invalid("Missing crate or invalid crate pose.");
                    if (world)
                    {
                        try { world.Session.Validate(state.Quests); }
                        catch (ArgumentException e) { return SaveValidation.Invalid(e.Message); }
                    }
                    return SaveValidation.Valid;
                },
                state =>
                {
                    lab.Crate.SetPositionAndRotation(state.Position, state.Rotation);
                    if (lab.Crate.TryGetComponent<Rigidbody>(out var body))
                    {
                        body.position = state.Position;
                        body.rotation = state.Rotation;
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                    if (world) world.Session.Restore(state.Quests);
                }, 95);
        }

        private void Check()
        {
            if (disposed) throw new ObjectDisposedException(nameof(EasySuiteSave));
            if (busy || service.IsBusy) throw new InvalidOperationException("A Playground save operation is already running.");
        }
        private void FinishTransientCommands()
        {
            lab.Actor.Cancel("Snapshot boundary"); lab.BuildingInput.Exit(); lab.Placer.Session?.Cancel();
        }
        // Memory checkpoints use exactly the same production adapters and preflight as disk load.
        public SaveSnapshot Capture() { Check(); FinishTransientCommands(); return service.CaptureSnapshot(); }
        public SaveSnapshot CapturePreferences() { Check(); return service.CaptureSnapshot(SaveScope.Preferences); }
        public Task SaveFileAsync(CancellationToken cancellation = default) => WorldAsync(() => service.QuickSaveAsync(cancellation), false, cancellation);
        public Task LoadFileAsync(CancellationToken cancellation = default) => WorldAsync(() => service.QuickLoadAsync(cancellation), true, cancellation);
        public Task RestoreAsync(SaveSnapshot snapshot, CancellationToken cancellation = default)
        {
            if (snapshot == null || snapshot.Scope != SaveScope.Progress) throw new ArgumentException("Expected a progress checkpoint.");
            return WorldAsync(() => service.RestoreSnapshotAsync(snapshot, cancellation), true, cancellation);
        }
        public Task SavePreferencesAsync(CancellationToken cancellation = default) => PreferencesAsync(null, false, cancellation);
        public Task LoadPreferencesAsync(CancellationToken cancellation = default) => PreferencesAsync(null, true, cancellation);
        public Task RestorePreferencesAsync(SaveSnapshot snapshot, CancellationToken cancellation = default)
        {
            if (snapshot == null || snapshot.Scope != SaveScope.Preferences) throw new ArgumentException("Expected a preferences checkpoint.");
            return PreferencesAsync(snapshot, true, cancellation);
        }
        private async Task PreferencesAsync(SaveSnapshot snapshot, bool load, CancellationToken cancellation)
        {
            Check(); busy = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            try
            {
                // Await native language readiness; no automatic preference load or shared-session cancellation.
                await EasyLocalization.Localization.CaptureStateAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                LastResult = snapshot != null ? await service.RestoreSnapshotAsync(snapshot, linked.Token) :
                    load ? await service.LoadPreferencesAsync(linked.Token) : await service.SavePreferencesAsync(linked.Token);
                RequireSuccess(LastResult);
            }
            finally { busy = false; }
        }
        private async Task WorldAsync(Func<Task<SaveResult>> operation, bool restoring, CancellationToken cancellation)
        {
            Check(); cancellation.ThrowIfCancellationRequested(); busy = true;
            try
            {
                if (lab.Character.IsNoclipActive && !lab.Character.TrySetNoclip(false)) throw new InvalidOperationException("Exit noclip before saving/loading progress.");
                using var input = lab.Player.AcquireContext(lab, new InputContextRequest { BlockedMaps = new[] { "*" } });
                using var movement = lab.Character.AcquireInputBlock(lab);
                previousTimeScale = Time.timeScale; paused = true; Time.timeScale = 0;
                lab.IsRestoring = restoring;
                lab.Windows.CloseAll(); FinishTransientCommands();
                LastResult = await operation(); RequireSuccess(LastResult);
                if (restoring && lab && !disposed) { lab.SuccessfulLoads++; Physics.SyncTransforms(); }
            }
            finally
            {
                EndPause(); busy = false;
                if (lab && ReferenceEquals(lab.Saves, this))
                { lab.IsRestoring = false; if (!disposed && lab.SlowField) lab.SlowField.Fire(); }
            }
        }
        private static void RequireSuccess(SaveResult result)
        {
            if (!result.Success) throw new InvalidOperationException(result + (result.WorldMayHaveChanged ? " World may be partially restored; inspect LastResult.RestoredParticipants." : ""));
        }
        private void EndPause() { if (!paused) return; paused = false; Time.timeScale = previousTimeScale; }
        public void Dispose()
        {
            if (disposed) return; disposed = true; lifetime.Cancel(); service.Dispose(); EndPause();
            if (lab && ReferenceEquals(lab.Saves, this)) lab.IsRestoring = false;
            lifetime.Dispose();
        }
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && Finite(Quaternion.Dot(q, q)) && Quaternion.Dot(q, q) > .001f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
