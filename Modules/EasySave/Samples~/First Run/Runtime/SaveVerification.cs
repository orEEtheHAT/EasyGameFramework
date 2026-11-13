using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasySave.Samples
{
    [Serializable] public sealed class SaveVerificationReport
    {
        public bool Success;
        public string UnityVersion, Platform, StorageRoot, Error;
        public int Checks;
        public List<SaveBenchmarkRow> Measurements = new List<SaveBenchmarkRow>();
    }
    [Serializable] public sealed class SaveBenchmarkRow
    {
        public int Participants, PayloadCharacters, Iteration;
        public SaveTimings Save, Load;
    }
    public static class SaveVerification
    {
        [Serializable] private sealed class State { public int Value; public string Text; }
        public static async Task<SaveVerificationReport> RunAsync(string storageRoot, string reportPath)
        {
            // These paths are explicitly owned test directories, never the game's persistent save directory.
            var report = new SaveVerificationReport { UnityVersion = Application.unityVersion, Platform = Application.platform.ToString(), StorageRoot = storageRoot };
            int ownerThread = Thread.CurrentThread.ManagedThreadId, value = 10, preference = 20;
            void Need(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); report.Checks++; }
            try
            {
                using (var save = new SaveService(new LocalFileStorage(Path.Combine(storageRoot, Guid.NewGuid().ToString("N")))))
                {
                    save.Register(new JsonSaveParticipant<State>("progress", SaveScope.Progress, 1, () => new State { Value = value }, s => s.Value >= 0 ? SaveValidation.Valid : SaveValidation.Invalid("negative"),
                        s => { Need(Thread.CurrentThread.ManagedThreadId == ownerThread, "Restore left the main thread"); value = s.Value; }));
                    save.Register(new JsonSaveParticipant<State>("preferences", SaveScope.Preferences, 1, () => new State { Value = preference }, _ => SaveValidation.Valid, s => preference = s.Value));
                    Need((await save.QuickSaveAsync()).Success, "Save failed"); Need((await save.SavePreferencesAsync()).Success, "Preferences save failed");
                    value = 88; preference = 32;
                    Need((await save.QuickLoadAsync()).Success && value == 10 && preference == 32, "Progress crossed preference boundary");
                    Need((await save.QuickLoadAsync()).Success && value == 10, "Repeated restore failed");
                    Need((await save.LoadPreferencesAsync()).Success && preference == 20, "Preference restore failed");
                    var slots = await save.ListAsync(); Need(slots.Success && slots.Slots.Count == 1 && slots.Slots[0].ParticipantCount == 1, "Wrong slot metadata");
                    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                    Need((await save.SaveAsync("cancelled", cancellation: cancellation.Token)).Error == SaveError.Cancelled, "Cancellation failed");
                    Need((await Task.Run(() => save.QuickLoadAsync())).Error == SaveError.WrongThread, "Wrong-thread request reached participants");
                    Need((await save.DeleteAsync("quick")).Committed, "Delete not committed"); Need((await save.QuickLoadAsync()).Error == SaveError.NotFound, "Deleted slot resurrected");
                }
                string recoveryRoot = Path.Combine(storageRoot, Guid.NewGuid().ToString("N"));
                using (var save = new SaveService(new LocalFileStorage(recoveryRoot)))
                {
                    save.Register(new JsonSaveParticipant<State>("p", SaveScope.Progress, 1, () => new State { Value = value }, _ => SaveValidation.Valid, s => value = s.Value));
                    value = 1; Need((await save.QuickSaveAsync()).Success, "Initial recovery save failed"); value = 2; Need((await save.QuickSaveAsync()).Success, "Backup rotation failed");
                    await Task.Run(() => File.WriteAllBytes(Path.Combine(recoveryRoot, "progress", "quick", "save.bin"), new byte[] { 1, 2 }));
                    var recovered = await save.QuickLoadAsync(); Need(recovered.Success && recovered.RecoveredFromBackup && value == 1, "Corrupt-primary recovery failed");
                }
                int[] counts = { 4, 128, 512 }, sizes = { 32, 1024, 8192 };
                for (int size = 0; size < counts.Length; size++)
                {
                    using var save = new SaveService(new LocalFileStorage(Path.Combine(storageRoot, "benchmark-" + size + "-" + Guid.NewGuid().ToString("N"))));
                    string text = new string('x', sizes[size]); int restored = 0;
                    for (int i = 0; i < counts[size]; i++) save.Register(new JsonSaveParticipant<State>("item/" + i, SaveScope.Progress, 1,
                        () => new State { Value = 42, Text = text }, s => s.Value == 42 && s.Text == text ? SaveValidation.Valid : SaveValidation.Invalid("payload mismatch"), _ => restored++));
                    for (int iteration = 0; iteration < 4; iteration++)
                    {
                        var written = await save.QuickSaveAsync(); var loaded = await save.QuickLoadAsync();
                        Need(written.Success && loaded.Success, "Benchmark roundtrip failed: " + written + " / " + loaded);
                        if (iteration > 0) report.Measurements.Add(new SaveBenchmarkRow { Participants = counts[size], PayloadCharacters = sizes[size], Iteration = iteration, Save = written.Timings, Load = loaded.Timings });
                    }
                    Need(restored == counts[size] * 4, "Lost participant restoration");
                }
                report.Success = true;
            }
            catch (Exception e) { report.Error = e.ToString(); }
            string json = JsonUtility.ToJson(report, true);
            await Task.Run(() => { Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); File.WriteAllText(reportPath, json); });
            return report;
        }
    }
}
