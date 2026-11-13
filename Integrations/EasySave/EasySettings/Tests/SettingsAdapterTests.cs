using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using EasySave.Integrations;
using EasySettings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasySave.Integration.Tests
{
    public sealed class SettingsAdapterTests
    {
        private sealed class Backend : ISettingsBackend
        {
            public SettingsState State = Initial();
            public int DisplayWrites, Writes;
            public SettingsState Read() => State;
            public SettingsCapabilities Describe() => new SettingsCapabilities(true, true, true, true, true, false, false,
                new[] { Display(800), Display(1280) }, new[] { FullScreenMode.Windowed });
            public void Apply(SettingsState state, bool display) { Writes++; if (display) DisplayWrites++; var old = State.Display; State = state; if (!display) State.Display = old; }
            public void Dispose() { }
        }
        private static DisplaySettings Display(int width) => new DisplaySettings { Width = width, Height = 720, Mode = FullScreenMode.Windowed, RefreshNumerator = 60, RefreshDenominator = 1 };
        private static SettingsState Initial() { var state = SettingsState.Balanced; state.Display = Display(800); return state; }
        private static IEnumerator Run(Func<Task> body) { var task = body(); var deadline = DateTime.UtcNow.AddSeconds(20); while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null; Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult(); }
        [UnityTest] public IEnumerator SavesConfirmedGraphicsAndNeverRestoresDisplay() => Run(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "EasySaveSettings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var backend = new Backend(); using var settings = new SettingsSession(backend, Initial(), SettingsLimits.Default);
                using var saves = new SaveService(new LocalFileStorage(root)); saves.Register(SettingsSaveAdapter.Create(settings));
                var draft = settings.Draft; draft.RenderScale = .7f; settings.Draft = draft; Assert.IsTrue(settings.Apply().Success);
                draft.RenderScale = .9f; settings.Draft = draft; Assert.IsTrue((await saves.SavePreferencesAsync()).Success); // Unapplied draft is not saved.
                Assert.IsTrue(settings.Apply().Success); backend.State.Display = Display(1280); // External resize without Refresh.
                var loaded = await saves.LoadPreferencesAsync(); Assert.IsTrue(loaded.Success, loaded.ToString());
                Assert.AreEqual(.7f, backend.State.RenderScale); Assert.AreEqual(1280, backend.State.Display.Width); Assert.Zero(backend.DisplayWrites);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        [Test] public void PendingPreviewAndFutureVersionRejectWithoutBackendMutation()
        {
            var backend = new Backend(); using var settings = new SettingsSession(backend, Initial(), SettingsLimits.Default);
            var participant = SettingsSaveAdapter.Create(settings); var snapshot = settings.Capture(); snapshot.Version = 2;
            Assert.IsFalse(participant.Validate(JsonUtility.ToJson(snapshot)).IsValid); Assert.Zero(backend.Writes);
            var draft = settings.Draft; draft.Display = Display(1280); settings.Draft = draft; settings.Apply();
            string committed = participant.Capture(); int writes = backend.Writes;
            Assert.IsFalse(participant.Validate(committed).IsValid); Assert.AreEqual(writes, backend.Writes);
            Assert.AreEqual(800, JsonUtility.FromJson<SettingsSnapshot>(committed).State.Display.Width);
        }
        [UnityTest] public IEnumerator ExternalDisplayChangePreservesActualWindowThroughOwnerApi() => Run(async () =>
        {
            var backend = new Backend(); using var settings = new SettingsSession(backend, Initial(), SettingsLimits.Default);
            var participant = SettingsSaveAdapter.Create(settings); string saved = participant.Capture();
            backend.State.Display = Display(1280); // No Refresh: the owner must preserve the actual display, not cached state.
            Assert.IsTrue(participant.Validate(saved).IsValid); Assert.Zero(backend.Writes);
            await participant.RestoreAsync(saved, default); Assert.Zero(backend.DisplayWrites); Assert.AreEqual(1280, backend.State.Display.Width);
        });
    }
}
