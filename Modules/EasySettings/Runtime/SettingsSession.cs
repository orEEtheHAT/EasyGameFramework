using System;
using System.Threading;
using UnityEngine;

namespace EasySettings
{
    /// <summary>Main-thread boundary for Unity and deterministic display tests. Apply may throw;
    /// the session attempts to restore the preceding state. Dispose releases owned overrides.</summary>
    public interface ISettingsBackend : IDisposable
    {
        SettingsState Read();
        SettingsCapabilities Describe();
        void Apply(SettingsState state, bool applyDisplay);
    }

    public sealed class SettingsSession : IDisposable
    {
        readonly ISettingsBackend backend;
        readonly SettingsState defaults;
        readonly SettingsLimits limits;
        readonly float confirmationSeconds;
        readonly int thread = Thread.CurrentThread.ManagedThreadId;
        SettingsState draft, applied, observed, preview, checkpoint;
        bool disposed, notifying, pending, faulted, committed;
        float elapsed;
        int samples;
        int backendDepth;
        bool cancelRequested;

        public SettingsSession(ISettingsBackend backend, SettingsState defaults, SettingsLimits limits, float confirmationSeconds = 15)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.defaults = defaults; this.limits = limits.Sanitize();
            this.confirmationSeconds = SettingsValidation.FiniteClamp(confirmationSeconds, 5, 60, 15);
            applied = draft = observed = ReadBackend();
            Capabilities = DescribeBackend();
        }
        public SettingsState Draft { get => draft; set { Check(); if (pending) throw new InvalidOperationException("Confirm or revert the screen preview before editing."); draft = value; LastResult = null; Publish(); } }
        /// <summary>Last committed state; remains unchanged throughout a display preview.</summary>
        public SettingsState Applied => applied;
        /// <summary>Last backend observation. Screen resolution is sampled after frame boundaries, not inferred from SetResolution.</summary>
        public SettingsState Observed => observed;
        public SettingsState Preview => pending ? preview : applied;
        public SettingsCapabilities Capabilities { get; private set; }
        public SettingsResult LastResult { get; private set; }
        public bool HasPreview => pending;
        public bool IsFaulted => faulted;
        public bool IsDisposed => disposed;
        internal bool IsBusy => notifying || backendDepth != 0;
        internal void EnsureCanMutate() => Check();
        public bool CanConfirm => pending && samples >= 2 && elapsed >= .2f;
        public float RemainingSeconds => pending ? Mathf.Max(0, confirmationSeconds - elapsed) : 0;
        public event Action Changed;
        public event Action Committed;

        public SettingsResult Validate(SettingsSnapshot snapshot) => Validate(snapshot, false);
        public SettingsResult Validate(SettingsSnapshot snapshot, bool restoreDisplay)
        {
            Check(false);
            if (snapshot == null || snapshot.Version != SettingsSnapshot.CurrentVersion)
                return SettingsResult.Failure("Unsupported or missing settings snapshot version.");
            var state = snapshot.State;
            if (state.Display.Width <= 0 || state.Display.Height <= 0 || state.Display.RefreshDenominator == 0 || state.Display.RefreshNumerator == 0)
                return SettingsResult.Failure("Incomplete settings snapshot: captured display dimensions and refresh ratio are required.");
            if (!restoreDisplay) state.Display = applied.Display;
            return SettingsValidation.Validate(state, applied, limits, Capabilities);
        }
        public SettingsSnapshot Capture() { Check(false); return new SettingsSnapshot { State = applied }; }

        /// <summary>Display restore is opt-in and always requires preview confirmation.
        /// Preferences loaders normally use false, so loading data never blindly changes the screen.</summary>
        public SettingsResult Restore(SettingsSnapshot snapshot, bool restoreDisplay = false)
        {
            Check();
            if (pending) return Fail("Finish the display preview before restoring settings.");
            try {
                observed = ReadBackend(); if (disposed) return Interrupted();
                Capabilities = DescribeBackend(); if (disposed) return Interrupted();
            }
            catch (Exception e) { return Fail(e.Message); }
            var validation = Validate(snapshot, restoreDisplay);
            if (!validation.Success) { LastResult = validation; Publish(); return validation; }
            var state = snapshot.State;
            if (!restoreDisplay) state.Display = observed.Display;
            draft = state;
            return Apply();
        }

        public SettingsResult Apply()
        {
            Check();
            if (pending) return Fail("Confirm or revert the current screen preview first.");
            try {
                observed = ReadBackend(); if (disposed) return Interrupted();
                Capabilities = DescribeBackend(); if (disposed) return Interrupted();
            }
            catch (Exception e) { return Fail(e.Message); }
            var result = SettingsValidation.Validate(draft, observed, limits, Capabilities);
            if (!result.Success) { LastResult = result; Publish(); return result; }
            var next = result.State;
            bool display = !next.Display.Equals(observed.Display);
            checkpoint = observed;
            if (!next.Equals(observed)) {
                try { ApplyBackend(next, display); }
                catch (Exception e) {
                    if (disposed) return Interrupted();
                    try { ApplyBackend(checkpoint, display); }
                    catch (Exception rollback) { faulted = true; return Fail("Apply and rollback failed. Disable the controller to release overrides. " + e.Message + " / " + rollback.Message); }
                    return Fail("Apply failed and the previous settings were restored: " + e.Message);
                }
                if (disposed) return Interrupted();
            }
            draft = next;
            if (display) { pending = true; preview = next; elapsed = 0; samples = 0; }
            else { applied = next; observed = ReadBackend(); if (disposed) return Interrupted(); committed = true; }
            LastResult = result;
            Publish();
            if (!display) Publish(Committed);
            return result;
        }

        public bool Confirm()
        {
            Check();
            if (!CanConfirm) return false;
            observed = ReadBackend();
            if (disposed) return false;
            // Persist what the OS actually accepted, including a fallback mode or size.
            applied = preview; applied.Display = observed.Display; draft = applied;
            pending = false; committed = true;
            LastResult = new SettingsResult(true, applied, "Display confirmed using the observed window size/mode and desktop refresh rate.");
            Publish(); Publish(Committed); return true;
        }

        public SettingsResult Revert()
        {
            Check();
            if (!pending) { Cancel(); return new SettingsResult(true, applied); }
            try { ApplyBackend(checkpoint, true); }
            catch (Exception e) { faulted = true; return Fail("Display rollback failed. Disable the controller to release overrides. " + e.Message); }
            if (disposed) return Interrupted();
            pending = false; draft = applied;
            observed = ReadBackend();
            if (disposed) return Interrupted();
            LastResult = new SettingsResult(true, applied, "Display preview reverted. The OS applies window changes at the end of the frame.");
            Publish(); return LastResult;
        }
        public void Cancel() { Check(); if (pending) { Revert(); return; } draft = applied; LastResult = null; Publish(); }
        /// <summary>Closes an editing flow safely even from Changed/Committed. Ordinary mutations
        /// stay forbidden in callbacks; this cancellation is drained after the notification.</summary>
        public void RequestCancel()
        {
            if (disposed) return;
            Check(false);
            if (IsBusy) { cancelRequested = true; return; }
            Cancel();
        }
        void DrainCancellation()
        {
            if (disposed || IsBusy || !cancelRequested) return;
            cancelRequested = false;
            if (faulted) return;
            if (pending || !draft.Equals(applied)) Cancel();
        }
        public void Reset() { Check(); var state = defaults; state.Display = ReadBackend().Display; if (!disposed) Draft = state; }
        /// <summary>Quality presets affect graphics only; user display, sync and FPS choice remain unchanged.</summary>
        public void SelectProfile(GraphicsProfile profile)
        {
            Check(); if (profile == null) throw new ArgumentNullException(nameof(profile));
            var state = profile.State; state.Display = draft.Display; state.VSyncCount = draft.VSyncCount; state.FrameRateLimit = draft.FrameRateLimit; Draft = state;
        }
        public void Refresh()
        {
            Check(); observed = ReadBackend(); if (disposed) return;
            Capabilities = DescribeBackend(); if (disposed) return;
            // Startup dependencies may finish OnEnable after this controller. Adopt the live
            // initial baseline only until the first commit and only while there are no edits.
            if (!committed && !pending && draft.Equals(applied)) applied = draft = observed;
            Publish();
        }
        /// <summary>Call once per frame with unscaledDeltaTime. Idle sessions perform no Unity reads or writes.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            Check(); if (!pending) return;
            if (!EasyCore.Numeric.IsNonNegative(unscaledDeltaTime)) return;
            elapsed += unscaledDeltaTime;
            if (elapsed >= confirmationSeconds) { Revert(); return; }
            var previous = observed; observed = ReadBackend(); samples++;
            if (disposed) return;
            if (!previous.Equals(observed) || samples == 2) Publish();
        }
        SettingsResult Fail(string message) { LastResult = SettingsResult.Failure(message); Publish(); return LastResult; }
        static SettingsResult Interrupted() => SettingsResult.Failure("The settings session was closed during the operation.");
        SettingsState ReadBackend() { backendDepth++; try { return backend.Read(); } finally { backendDepth--; } }
        SettingsCapabilities DescribeBackend() { backendDepth++; try { return backend.Describe(); } finally { backendDepth--; } }
        void ApplyBackend(SettingsState value, bool display) { backendDepth++; try { backend.Apply(value, display); } finally { backendDepth--; } }
        void Check(bool mutation = true)
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("EasySettings must run on its creating Unity main thread.");
            if (disposed) throw new ObjectDisposedException(nameof(SettingsSession));
            if (mutation && IsBusy) throw new InvalidOperationException("Do not mutate a settings session from its notification or backend callback. Dispose is allowed as a terminal operation.");
            if (faulted) throw new InvalidOperationException("The settings backend is faulted. Dispose and recreate the controller.");
        }
        void Publish() => Publish(Changed);
        void Publish(Action handlers)
        {
            if (disposed) return;
            if (handlers == null) { DrainCancellation(); return; }
            notifying = true;
            try {
                foreach (Action handler in handlers.GetInvocationList()) {
                    if (disposed) break;
                    try { handler(); } catch (Exception e) { Debug.LogException(e); }
                }
            }
            finally { notifying = false; DrainCancellation(); }
        }
        /// <summary>Terminal main-thread operation. Safe inside Changed/Committed: resources are released
        /// synchronously and no remaining callbacks of this session are dispatched.</summary>
        public void Dispose()
        {
            if (disposed) return;
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Dispose on the owning main thread.");
            bool rollback = pending;
            disposed = true; pending = false; cancelRequested = false; Changed = null; Committed = null;
            try { if (rollback) ApplyBackend(checkpoint, true); }
            finally { backend.Dispose(); }
        }
    }
}
