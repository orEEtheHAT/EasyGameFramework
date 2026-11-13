using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace EasySettings.Tests
{
    public sealed class SettingsSessionTests
    {
        sealed class Backend : ISettingsBackend
        {
            public SettingsState State = Initial;
            public int Writes, Reads;
            public bool ThrowOnce, AlwaysThrow, Disposed;
            public SettingsCapabilities Caps = new SettingsCapabilities(true, true, true, true, true, true, true,
                new[] { Display(800, 600), Display(1280, 720) }, new[] { FullScreenMode.Windowed, FullScreenMode.ExclusiveFullScreen });
            public SettingsState Read() { Reads++; return State; }
            public SettingsCapabilities Describe() => Caps;
            public void Apply(SettingsState state, bool display) {
                Writes++; if (AlwaysThrow || ThrowOnce) { ThrowOnce = false; throw new InvalidOperationException("simulated failure"); }
                var previous = State.Display; State = state; if (!display) State.Display = previous;
            }
            public void Dispose() => Disposed = true;
        }
        static DisplaySettings Display(int w, int h) => new DisplaySettings { Width = w, Height = h, Mode = FullScreenMode.Windowed, RefreshNumerator = 60, RefreshDenominator = 1 };
        static SettingsState Initial { get { var s = SettingsState.Balanced; s.Display = Display(800, 600); return s; } }
        Backend backend; SettingsSession session;
        [SetUp] public void SetUp() { backend = new Backend(); session = new SettingsSession(backend, Initial, SettingsLimits.Default, 5); }
        [TearDown] public void TearDown() { if (!session.IsDisposed) session.Dispose(); }
        void Edit(ActionRef edit) { var state = session.Draft; edit(ref state); session.Draft = state; }
        delegate void ActionRef(ref SettingsState state);
        void Preview() { Edit((ref SettingsState s) => s.Display = Display(1280, 720)); Assert.IsTrue(session.Apply().Success); }

        [Test] public void DraftDoesNotWriteAndCancelDoesNotApply() { Edit((ref SettingsState s) => s.RenderScale = .6f); session.Cancel(); Assert.AreEqual(1, session.Draft.RenderScale); Assert.Zero(backend.Writes); }
        [Test] public void ApplyAndCaptureCommitValueCopies() { Edit((ref SettingsState s) => s.RenderScale = .8f); session.Apply(); var capture = session.Capture(); capture.State.RenderScale = 2; Assert.AreEqual(.8f, session.Applied.RenderScale); Assert.AreEqual(.8f, backend.State.RenderScale); }
        [Test] public void ResetChangesOnlyDraftAndPreservesDisplay() { Edit((ref SettingsState s) => s.RenderScale = .8f); session.Apply(); session.Reset(); Assert.AreEqual(1, session.Draft.RenderScale); Assert.AreEqual(.8f, session.Applied.RenderScale); }
        [Test] public void VsyncDisablesFrameLimiterWithReason() { Edit((ref SettingsState s) => s.FrameRateLimit = 120); var r = session.Apply(); Assert.AreEqual(-1, r.State.FrameRateLimit); Assert.IsNotEmpty(r.Messages); }
        [TestCase(float.NaN)][TestCase(float.PositiveInfinity)][TestCase(float.NegativeInfinity)]
        public void NonfiniteValuesUseSafeFallback(float value) { Edit((ref SettingsState s) => { s.RenderScale = value; s.ShadowDistance = value; s.FieldOfView = value; }); var r = session.Apply(); Assert.IsTrue(r.Success); Assert.AreEqual(1, r.State.RenderScale); Assert.AreEqual(50, r.State.ShadowDistance); Assert.AreEqual(60, r.State.FieldOfView); }
        [Test] public void NumericLimitsClamp() { Edit((ref SettingsState s) => { s.RenderScale = 90; s.FieldOfView = -30; s.MsaaSamples = 3; s.VSyncCount = 0; s.FrameRateLimit = 999; }); var r = session.Apply(); Assert.AreEqual(2, r.State.RenderScale); Assert.AreEqual(40, r.State.FieldOfView); Assert.AreEqual(2, r.State.MsaaSamples); Assert.AreEqual(360, r.State.FrameRateLimit); }
        [Test] public void PreviewDoesNotLeakIntoSavedState() { Preview(); Assert.IsTrue(session.HasPreview); Assert.AreEqual(800, session.Capture().State.Display.Width); Assert.AreEqual(1280, session.Preview.Display.Width); }
        [Test] public void ConfirmationWaitsForObservation() { Preview(); Assert.IsFalse(session.Confirm()); session.Tick(.1f); Assert.IsFalse(session.Confirm()); session.Tick(.1f); Assert.IsTrue(session.Confirm()); Assert.AreEqual(1280, session.Capture().State.Display.Width); }
        [Test] public void ConfirmationSavesOsFallback() { Preview(); backend.State.Display = Display(800, 600); session.Tick(.1f); session.Tick(.1f); session.Confirm(); Assert.AreEqual(800, session.Capture().State.Display.Width); }
        [Test] public void TimeoutRevertsAllPreviewSettings() { Edit((ref SettingsState s) => s.RenderScale = .6f); Preview(); session.Tick(5); Assert.IsFalse(session.HasPreview); Assert.AreEqual(800, backend.State.Display.Width); Assert.AreEqual(1, backend.State.RenderScale); }
        [Test] public void CancelDuringPreviewReverts() { Preview(); session.Cancel(); Assert.AreEqual(800, backend.State.Display.Width); Assert.IsFalse(session.HasPreview); }
        [Test] public void CannotReplaceUnconfirmedPreview() { Preview(); Assert.IsFalse(session.Apply().Success); Assert.Throws<InvalidOperationException>(() => session.Draft = Initial); }
        [Test] public void DisposeRevertsPendingAndIsIdempotent() { Preview(); session.Dispose(); session.Dispose(); Assert.IsTrue(backend.Disposed); Assert.AreEqual(800, backend.State.Display.Width); }
        [Test] public void IdleTickPerformsNoBackendReadsOrWrites() { int reads = backend.Reads; for (int i = 0; i < 1000; i++) session.Tick(.02f); Assert.AreEqual(reads, backend.Reads); Assert.Zero(backend.Writes); }
        [Test] public void UnchangedApplyDoesNotWrite() { session.Apply(); Assert.Zero(backend.Writes); }
        [Test] public void InvalidDisplayIsRejectedBeforeSideEffects() { Edit((ref SettingsState s) => s.Display = Display(17, 31)); Assert.IsFalse(session.Apply().Success); Assert.Zero(backend.Writes); }
        [Test] public void UnsupportedFeaturesArePreservedWithReasons() {
            backend.Caps = new SettingsCapabilities(false, false, false, false, false, false, false, null, null, displayReason: "No display", msaaReason: "No MSAA", cameraReason: "No camera", postProcessingReason: "No post", volumeReason: "No volume");
            Edit((ref SettingsState s) => { s.Display = Display(1280, 720); s.MsaaSamples = 8; s.FieldOfView = 90; s.ShadowDistance = 100; s.PostExposure = 5; });
            var r = session.Apply(); Assert.IsTrue(r.Success); Assert.AreEqual(800, r.State.Display.Width); Assert.AreEqual(1, r.State.MsaaSamples); Assert.AreEqual(60, r.State.FieldOfView); Assert.IsNotEmpty(r.Messages);
        }
        [Test] public void FailedApplyRestoresPriorState() { backend.ThrowOnce = true; Edit((ref SettingsState s) => s.RenderScale = .5f); Assert.IsFalse(session.Apply().Success); Assert.AreEqual(1, backend.State.RenderScale); Assert.AreEqual(2, backend.Writes); Assert.IsFalse(session.IsFaulted); }
        [Test] public void FailedRollbackFaultsSession() { backend.AlwaysThrow = true; Edit((ref SettingsState s) => s.RenderScale = .5f); Assert.IsFalse(session.Apply().Success); Assert.IsTrue(session.IsFaulted); Assert.Throws<InvalidOperationException>(() => session.Apply()); }
        [Test] public void SnapshotJsonRoundTripAndDefaultRestoreDoNotChangeDisplay() { var snapshot = new SettingsSnapshot { State = Initial }; snapshot.State.RenderScale = .7f; snapshot.State.Display = Display(1280, 720); var copy = JsonUtility.FromJson<SettingsSnapshot>(JsonUtility.ToJson(snapshot)); Assert.IsTrue(session.Validate(copy).Success); Assert.IsTrue(session.Restore(copy).Success); Assert.AreEqual(.7f, session.Applied.RenderScale); Assert.AreEqual(800, session.Applied.Display.Width); }
        [Test] public void RestoreWithDisplayStillRequiresConfirmation() { var snapshot = new SettingsSnapshot { State = Initial }; snapshot.State.Display = Display(1280, 720); session.Restore(snapshot, true); Assert.IsTrue(session.HasPreview); }
        [Test] public void FutureVersionRejectedWithoutMutation() { Assert.IsFalse(session.Validate(new SettingsSnapshot { Version = 2 }).Success); Assert.IsFalse(session.Restore(new SettingsSnapshot { Version = 2 }).Success); Assert.Zero(backend.Writes); }
        [Test] public void IncompleteSnapshotRejectedBeforeApply() { Assert.IsFalse(session.Validate(new SettingsSnapshot()).Success); Assert.IsFalse(session.Restore(new SettingsSnapshot()).Success); Assert.Zero(backend.Writes); }
        [Test] public void GraphicsRestoreValidationIgnoresOldMonitorModes() { var old = new SettingsSnapshot { State = Initial }; old.State.Display = Display(3840, 2160); old.State.RenderScale = .8f; Assert.IsTrue(session.Validate(old).Success); Assert.IsFalse(session.Validate(old, true).Success); Assert.IsTrue(session.Restore(old).Success); Assert.AreEqual(800, backend.State.Display.Width); }
        [Test] public void GraphicsRestorePreservesExternallyResizedWindow() { var saved = session.Capture(); backend.State.Display = Display(1280, 720); Assert.IsTrue(session.Restore(saved).Success); Assert.IsFalse(session.HasPreview); Assert.AreEqual(1280, backend.State.Display.Width); }
        [Test] public void ResetPreservesExternallyResizedWindow() { backend.State.Display = Display(1280, 720); session.Reset(); session.Apply(); Assert.IsFalse(session.HasPreview); Assert.AreEqual(1280, backend.State.Display.Width); }
        [Test] public void ValidateNeverWrites() { var s = new SettingsSnapshot { State = Initial }; s.State.RenderScale = 90; var r = session.Validate(s); Assert.IsTrue(r.Success); Assert.AreEqual(90, s.State.RenderScale); Assert.Zero(backend.Writes); }
        [Test] public void GraphicsProfilePreservesSyncAndDisplay() { var p = new GraphicsProfile { State = SettingsState.Balanced }; p.State.VSyncCount = 0; p.State.Display = Display(1280, 720); session.SelectProfile(p); Assert.AreEqual(800, session.Draft.Display.Width); Assert.AreEqual(1, session.Draft.VSyncCount); }
        [Test] public void CommittedSubscriberCanCapture() { SettingsSnapshot saved = null; session.Committed += () => saved = session.Capture(); session.Apply(); Assert.NotNull(saved); }
        [Test] public void ThreadGuardRejectsWorkerMutation() { var failure = Task.Run(() => { try { session.Apply(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(); Assert.IsTrue(failure); }
        [Test] public void TaaIsNotOfferedAsASelectableMode() { Edit((ref SettingsState s) => s.CameraAntialiasing = AntialiasingMode.TemporalAntiAliasing); Assert.AreEqual(AntialiasingMode.None, session.Apply().State.CameraAntialiasing); }
    }
}
