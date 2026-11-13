using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EasySettings
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Settings/Settings Controller")]
    public sealed class SettingsController : MonoBehaviour
    {
        public SettingsDefaults Defaults;
        public Camera TargetCamera;
        [Tooltip("Optional dedicated global Volume. Source profile is never edited.")]
        public Volume TargetVolume;
        [Tooltip("Optional component implementing ISettingsFieldOfViewProvider. For an EasyMovement player, assign EasyMovement Settings FOV; leave empty for a plain Camera.")]
        public MonoBehaviour FieldOfViewProvider;
        [Tooltip("Bind Camera.main at startup and sceneLoaded. For respawns call BindTargets explicitly. No per-frame camera search.")]
        public bool FollowMainCamera = true;
        public SettingsSession Session { get; private set; }
        public string Error { get; private set; }
        public event Action SessionChanged;
        UrpSettingsBackend backend;
        int lifetime;
        bool refreshFov;

        void OnEnable()
        {
            int version = ++lifetime;
            UrpSettingsBackend createdBackend = null;
            SettingsSession createdSession = null;
            try {
                if (!Defaults) throw new InvalidOperationException("Create EasyGameFramework > Settings > Defaults and assign it to this controller.");
                if (FollowMainCamera && !TargetCamera) TargetCamera = Camera.main;
                createdBackend = new UrpSettingsBackend(TargetCamera, TargetVolume, Defaults.AllowPostProcessingChanges, Defaults.AllowVolumeChanges);
                if (FieldOfViewProvider && !(FieldOfViewProvider is ISettingsFieldOfViewProvider)) throw new InvalidOperationException("Assign an ISettingsFieldOfViewProvider component, such as EasyMovement Settings FOV.");
                createdBackend.BindFieldOfViewProvider(FieldOfViewProvider as ISettingsFieldOfViewProvider, this);
                createdSession = new SettingsSession(createdBackend, Defaults.Defaults, Defaults.Limits, Defaults.ConfirmationSeconds);
                if (version != lifetime || !isActiveAndEnabled) { createdSession.Dispose(); return; }
                backend = createdBackend; Session = createdSession;
                Error = null; SceneManager.sceneLoaded += SceneLoaded;
            } catch (Exception e) {
                if (createdSession != null) createdSession.Dispose(); else createdBackend?.Dispose();
                if (version != lifetime) return;
                Session = null; backend = null; Error = e.Message; Debug.LogError("EasySettings: " + Error, this);
            }
            NotifySessionChanged(version);
        }
        void Update()
        {
            var current = Session;
            if (current == null || current.IsDisposed || current.IsFaulted) return;
            if (refreshFov && !current.IsBusy) { refreshFov = false; current.Refresh(); }
            if (ReferenceEquals(Session, current) && !current.IsDisposed && current.HasPreview) current.Tick(Time.unscaledDeltaTime);
        }
        /// <summary>Event-driven invalidation for the bound FOV provider. The next update refreshes
        /// availability/readback without reentering a settings or owner notification.</summary>
        public void RefreshFieldOfViewProvider(MonoBehaviour provider, bool immediately = false)
        {
            if (!this || !isActiveAndEnabled || !ReferenceEquals(FieldOfViewProvider, provider)) return;
            var current = Session;
            if (immediately && current != null && !current.IsDisposed && !current.IsFaulted && !current.IsBusy) {
                refreshFov = false; current.Refresh();
            } else refreshFov = true;
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode) { if (FollowMainCamera && Session != null && Camera.main != TargetCamera) BindTargets(Camera.main, TargetVolume); }
        public void BindTargets(Camera camera, Volume volume = null)
        {
            var current = Session; var currentBackend = backend; int version = lifetime;
            current?.EnsureCanMutate();
            TargetCamera = camera; TargetVolume = volume;
            if (current == null) return;
            if (current.HasPreview) current.Revert();
            if (!Current(current, version)) return;
            var saved = current.Capture();
            currentBackend.BindTargets(camera, volume);
            if (!Current(current, version)) return;
            current.Refresh();
            if (Current(current, version)) current.Restore(saved);
        }
        public SettingsSnapshot Capture() => RequireSession().Capture();
        public SettingsState Draft { get => RequireSession().Draft; set => RequireSession().Draft = value; }
        public SettingsState Applied => RequireSession().Applied;
        public bool HasPreview => Session != null && Session.HasPreview;
        public SettingsResult Apply() => RequireSession().Apply();
        public bool Confirm() => RequireSession().Confirm();
        public SettingsResult Revert() => RequireSession().Revert();
        public void Cancel() => RequireSession().RequestCancel();
        public void Reset() => RequireSession().Reset();
        /// <summary>Copies a configured graphics preset into the draft; Apply is explicit.</summary>
        public void SelectProfile(int index)
        {
            if (!Defaults || Defaults.Profiles == null || index < 0 || index >= Defaults.Profiles.Length || Defaults.Profiles[index] == null)
                throw new ArgumentOutOfRangeException(nameof(index), "Select an existing profile from the assigned Settings Defaults.");
            RequireSession().SelectProfile(Defaults.Profiles[index]);
        }
        /// <summary>Refresh availability when opening UI; returns false if called during a callback or after shutdown.</summary>
        public bool Refresh()
        {
            var current = Session;
            if (current == null || current.IsDisposed || current.IsFaulted || current.IsBusy) return false;
            current.Refresh(); return ReferenceEquals(current, Session) && !current.IsDisposed;
        }
        /// <summary>Releases the previous FOV request and selects a new owner. Existing preferences
        /// can be reapplied explicitly with Restore; binding itself does not overwrite the new owner.</summary>
        public void BindFieldOfViewProvider(MonoBehaviour provider)
        {
            if (provider && !(provider is ISettingsFieldOfViewProvider)) throw new ArgumentException("Use a component implementing ISettingsFieldOfViewProvider.", nameof(provider));
            var current = Session; var currentBackend = backend; int version = lifetime;
            current?.EnsureCanMutate();
            if (current != null && current.HasPreview) current.Revert();
            if (current != null && !Current(current, version)) return;
            FieldOfViewProvider = provider;
            if (current == null) return;
            currentBackend.BindFieldOfViewProvider(provider as ISettingsFieldOfViewProvider, this);
            if (Current(current, version)) current.Refresh();
        }
        public SettingsResult Validate(SettingsSnapshot snapshot) => RequireSession().Validate(snapshot);
        public SettingsResult Validate(SettingsSnapshot snapshot, bool restoreDisplay) => RequireSession().Validate(snapshot, restoreDisplay);
        public SettingsResult Restore(SettingsSnapshot snapshot, bool restoreDisplay = false) => RequireSession().Restore(snapshot, restoreDisplay);
        SettingsSession RequireSession() => Session ?? throw new InvalidOperationException(Error ?? "Enable Settings Controller first.");
        bool Current(SettingsSession session, int version) => this && isActiveAndEnabled && version == lifetime &&
            ReferenceEquals(Session, session) && !session.IsDisposed;
        void NotifySessionChanged(int version)
        {
            var handlers = SessionChanged;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList()) {
                if (!this || version != lifetime) break;
                try { handler(); } catch (Exception e) { Debug.LogException(e, this); }
            }
        }
        void OnDisable() => ShutDown();
        void OnDestroy() => ShutDown();
        void ShutDown()
        {
            int version = ++lifetime;
            refreshFov = false;
            SceneManager.sceneLoaded -= SceneLoaded;
            var closingSession = Session; var closingBackend = backend;
            // Detach before calling user-facing cleanup. A nested enable owns a different generation.
            Session = null; backend = null;
            if (closingSession == null && closingBackend == null) return;
            try { if (closingSession != null) closingSession.Dispose(); else closingBackend?.Dispose(); }
            finally { NotifySessionChanged(version); }
        }
    }
}
