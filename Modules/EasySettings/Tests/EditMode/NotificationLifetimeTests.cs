using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace EasySettings.Tests
{
    public sealed class NotificationLifetimeTests
    {
        sealed class Backend : ISettingsBackend
        {
            public SettingsState State = Initial();
            public int Writes, Reads, Releases;
            public bool Closed, ThrowNextApply;
            public Action OnApply;
            void Live() { if (Closed) Assert.Fail("A closed backend was accessed."); }
            public SettingsState Read() { Live(); Reads++; return State; }
            public SettingsCapabilities Describe() { Live(); return new SettingsCapabilities(true, true, true, true, true, false, false,
                new[] { Display(800), Display(1280) }, new[] { FullScreenMode.Windowed }); }
            public void Apply(SettingsState value, bool display) {
                Live(); Writes++;
                if (ThrowNextApply) { ThrowNextApply = false; throw new InvalidOperationException("rollback failure"); }
                var old = State.Display; State = value; if (!display) State.Display = old;
                OnApply?.Invoke();
            }
            public void Dispose() { Releases++; Closed = true; }
        }
        static DisplaySettings Display(int width) => new DisplaySettings { Width = width, Height = 720, Mode = FullScreenMode.Windowed, RefreshNumerator = 60, RefreshDenominator = 1 };
        static SettingsState Initial() { var s = SettingsState.Balanced; s.Display = Display(800); return s; }
        static SettingsSession Create(Backend backend) => new SettingsSession(backend, Initial(), SettingsLimits.Default, 5);
        static void Edit(SettingsSession session, bool screen = false) { var s = session.Draft; s.RenderScale = .75f; if (screen) s.Display = Display(1280); session.Draft = s; }

        [Test] public void ChangedCanDisposeAndStopsRemainingEvents()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            int late = 0; session.Changed += session.Dispose; session.Changed += () => late++; session.Committed += () => late++;
            Assert.IsTrue(session.Apply().Success);
            Assert.IsTrue(session.IsDisposed); Assert.AreEqual(1, backend.Releases); Assert.Zero(late);
        }
        [Test] public void CommittedCanDisposeSynchronously()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            int late = 0; session.Committed += session.Dispose; session.Committed += () => late++;
            session.Apply(); session.Dispose();
            Assert.IsTrue(session.IsDisposed); Assert.AreEqual(1, backend.Releases); Assert.Zero(late);
        }
        [Test] public void DisposalFromPreviewChangedRollsBackBeforeRelease()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session, true);
            session.Changed += session.Dispose; session.Apply();
            Assert.IsTrue(session.IsDisposed); Assert.IsFalse(session.HasPreview);
            Assert.AreEqual(800, backend.State.Display.Width); Assert.AreEqual(1, backend.State.RenderScale);
            Assert.AreEqual(2, backend.Writes); Assert.AreEqual(1, backend.Releases);
        }
        [Test] public void DisposalFromConfirmChangedSuppressesCommitted()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session, true); session.Apply(); session.Tick(.1f); session.Tick(.1f);
            int late = 0; session.Changed += session.Dispose; session.Committed += () => late++;
            Assert.IsTrue(session.Confirm()); Assert.IsTrue(session.IsDisposed); Assert.Zero(late); Assert.AreEqual(1, backend.Releases);
        }
        [Test] public void ReentrantBackendApplyCannotContinueAfterCleanup()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            backend.OnApply = session.Dispose;
            Assert.IsFalse(session.Apply().Success); Assert.IsTrue(session.IsDisposed); Assert.AreEqual(1, backend.Releases);
        }
        [Test] public void BackendCallbackCannotReenterOrdinaryMutation()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            backend.OnApply = () => Assert.Throws<InvalidOperationException>(() => session.Apply());
            Assert.IsTrue(session.Apply().Success); Assert.AreEqual(1, backend.Writes); Assert.IsFalse(session.IsDisposed);
        }
        [Test] public void FailedPreviewRollbackStillReleasesExactlyOnce()
        {
            var backend = new Backend(); var session = Create(backend); Edit(session, true); session.Apply(); backend.ThrowNextApply = true;
            Assert.Throws<InvalidOperationException>(() => session.Dispose()); session.Dispose();
            Assert.IsTrue(session.IsDisposed); Assert.IsTrue(backend.Closed); Assert.AreEqual(1, backend.Releases);
        }
        [Test] public void CaptureRemainsAllowedButOrdinaryReentrantMutationIsRejected()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            session.Committed += () => {
                Assert.AreEqual(.75f, session.Capture().State.RenderScale);
                Assert.Throws<InvalidOperationException>(() => session.Apply());
                Assert.Throws<InvalidOperationException>(() => session.Draft = Initial());
                session.Dispose();
            };
            session.Apply(); Assert.IsTrue(session.IsDisposed);
        }
        [Test] public void WorkerCannotDisposeLiveSession()
        {
            var backend = new Backend(); using var session = Create(backend);
            bool rejected = Task.Run(() => { try { session.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
            Assert.IsTrue(rejected); Assert.IsFalse(session.IsDisposed); Assert.Zero(backend.Releases);
        }
        [Test] public void ClosingUiFromDraftChangedCancelsAfterDispatch()
        {
            var backend = new Backend(); using var session = Create(backend);
            session.Changed += session.RequestCancel;
            Edit(session);
            Assert.AreEqual(session.Applied, session.Draft); Assert.Zero(backend.Writes); Assert.IsFalse(session.IsDisposed);
        }
        [Test] public void ClosingUiFromPreviewChangedRollsBackWithoutClosingController()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session, true);
            session.Changed += session.RequestCancel; session.Apply();
            Assert.IsFalse(session.HasPreview); Assert.IsFalse(session.IsDisposed); Assert.AreEqual(800, backend.State.Display.Width); Assert.AreEqual(1, backend.State.RenderScale);
        }
        [Test] public void CloseFromCommittedKeepsAppliedGraphicsWithoutExtraBackendWrites()
        {
            var backend = new Backend(); using var session = Create(backend); Edit(session);
            session.Committed += session.RequestCancel; Assert.IsTrue(session.Apply().Success);
            Assert.AreEqual(.75f, session.Applied.RenderScale); Assert.AreEqual(.75f, backend.State.RenderScale); Assert.AreEqual(1, backend.Writes);
        }
    }
}
