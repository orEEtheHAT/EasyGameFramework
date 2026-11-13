using System;
using System.Collections.Generic;
using EasyMovement.FirstPerson;
using UnityEngine;

namespace EasySettings.Integrations
{
    /// <summary>Settings owns only a base-FOV request. EasyMovement owns gait deltas, gameplay offsets and lens writes.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(SettingsController)), AddComponentMenu("EasyGameFramework/Settings/EasyMovement Settings FOV")]
    public sealed class EasyMovementSettingsFov : MonoBehaviour, ISettingsFieldOfViewProvider
    {
        [Tooltip("First Person FOV on the player's EasyMovement controller. Its base value is captured; gameplay offsets are not saved.")]
        public FirstPersonFov PlayerFov;
        readonly List<Lease> leases = new List<Lease>();
        FirstPersonFov subscribed;

        void Reset()
        {
            PlayerFov = GetComponent<FirstPersonFov>();
            var settings = GetComponent<SettingsController>();
            if (settings && !settings.FieldOfViewProvider) settings.FieldOfViewProvider = this;
        }
        void OnEnable()
        {
            var settings = GetComponent<SettingsController>();
            if (settings && !settings.FieldOfViewProvider) settings.BindFieldOfViewProvider(this);
            Subscribe();
            if (settings) settings.RefreshFieldOfViewProvider(this, immediately: true);
        }
        /// <summary>Call when swapping players at runtime. Removes only the previous player's settings handle.</summary>
        public void Bind(FirstPersonFov player)
        {
            ReleaseOverrides();
            if (!this) return;
            PlayerFov = player;
            Subscribe(); Invalidate();
        }
        void Subscribe()
        {
            if (subscribed) subscribed.Changed -= Invalidate;
            subscribed = isActiveAndEnabled ? PlayerFov : null;
            if (subscribed) subscribed.Changed += Invalidate;
        }
        void Invalidate() { var settings = GetComponent<SettingsController>(); if (settings) settings.RefreshFieldOfViewProvider(this); }
        void OnDisable() { if (subscribed) subscribed.Changed -= Invalidate; subscribed = null; ReleaseOverrides(); Invalidate(); }
        void OnDestroy() => ReleaseOverrides();
        void ReleaseOverrides() { var previous = leases.ToArray(); leases.Clear(); foreach (var lease in previous) lease.Dispose(); }
        public bool TryRead(out float degrees, out string reason)
        {
            degrees = 60;
            if (!this || !isActiveAndEnabled) { reason = "Enable EasyMovement Settings FOV."; return false; }
            if (!PlayerFov) { reason = "Assign the player's First Person FOV component to EasyMovement Settings FOV."; return false; }
            if (!PlayerFov.isActiveAndEnabled) { reason = "Enable First Person FOV on the EasyMovement player."; return false; }
            if (!PlayerFov.IsAvailable) { reason = PlayerFov.UnavailableReason; return false; }
            degrees = PlayerFov.BaseFieldOfView; reason = ""; return true;
        }
        public ISettingsFieldOfViewOverride Acquire(UnityEngine.Object owner, float degrees)
        {
            if (!TryRead(out _, out var reason)) throw new InvalidOperationException(reason);
            var source = PlayerFov;
            var lease = new Lease(this, source, source.AcquireBaseFovOverride(owner, degrees));
            if (!this || !isActiveAndEnabled || PlayerFov != source) lease.Dispose(); else leases.Add(lease);
            return lease;
        }
        sealed class Lease : ISettingsFieldOfViewOverride
        {
            readonly EasyMovementSettingsFov adapter;
            readonly FirstPersonFov source;
            FirstPersonFov.FovOverride handle;
            public Lease(EasyMovementSettingsFov adapter, FirstPersonFov source, FirstPersonFov.FovOverride handle) { this.adapter = adapter; this.source = source; this.handle = handle; }
            public bool IsActive => adapter && adapter.isActiveAndEnabled && source && adapter.PlayerFov == source && handle != null && handle.IsActive;
            public bool TrySet(float degrees) => IsActive && handle.TrySet(degrees);
            public void Dispose() { var previous = handle; handle = null; if (adapter) adapter.leases.Remove(this); previous?.Dispose(); }
        }
    }
}
