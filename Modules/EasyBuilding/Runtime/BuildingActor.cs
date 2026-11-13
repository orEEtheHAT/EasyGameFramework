using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Actor")]
    public sealed class BuildingActor : MonoBehaviour, IBuildingActor, IBuildingSessionProvider
    {
        [Tooltip("Optional persistent player/profile ID. An empty value gets a unique ID for this runtime instance.")]
        [SerializeField] private string actorId;
        [SerializeField] private string teamId;
        [SerializeField] private bool administrator;
        [SerializeField] private string[] initialCapabilities = Array.Empty<string>();
        private string runtimeId;
        private HashSet<string> capabilities;
        private readonly Dictionary<BuildingSystem, BuildingSession> sessions = new Dictionary<BuildingSystem, BuildingSession>();
        private bool destroying;
        public string Id => string.IsNullOrEmpty(actorId) ? runtimeId ?? (runtimeId = Guid.NewGuid().ToString("N")) : actorId;
        public string TeamId { get => teamId ?? ""; set => teamId = value; }
        public Vector3 Position => transform.position;
        public bool IsAdministrator { get => administrator; set => administrator = value; }

        /// <summary>One actor-owned session per world. Independent tools can still call BuildingSystem.CreateSession explicitly.</summary>
        public BuildingSession GetSession(BuildingSystem system)
        {
            if (destroying) throw new ObjectDisposedException(nameof(BuildingActor));
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (sessions.TryGetValue(system, out var current) && !current.IsDisposed) return current;
            var session = system.CreateSession(this);
            sessions[system] = session; session.Disposed += ForgetSession;
            return session;
        }
        private void ForgetSession(BuildingSession session)
        {
            session.Disposed -= ForgetSession;
            if (sessions.TryGetValue(session.System, out var current) && ReferenceEquals(current, session)) sessions.Remove(session.System);
        }
        private void OnDisable()
        {
            // Keep borrowed references valid across player disable/re-enable, but leave building mode.
            foreach (var session in new List<BuildingSession>(sessions.Values)) session.Cancel();
        }
        private void OnDestroy()
        {
            destroying = true;
            foreach (var session in new List<BuildingSession>(sessions.Values)) session.Dispose();
            sessions.Clear();
        }

        public void SetIdentity(string id, string team = "")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A persistent actor ID cannot be empty.", nameof(id));
            actorId = id; teamId = team;
        }
        public bool HasCapability(string capability)
        {
            EnsureCapabilities();
            return string.IsNullOrEmpty(capability) || capabilities.Contains(capability);
        }
        public void Grant(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability)) throw new ArgumentException("Enter a capability name.", nameof(capability));
            EnsureCapabilities(); capabilities.Add(capability);
        }
        public void Revoke(string capability) { EnsureCapabilities(); if (capability != null) capabilities.Remove(capability); }
        public string[] CaptureCapabilities() { EnsureCapabilities(); var values = new string[capabilities.Count]; capabilities.CopyTo(values); Array.Sort(values, StringComparer.Ordinal); return values; }
        public void RestoreCapabilities(IEnumerable<string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var replacement = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) if (!string.IsNullOrWhiteSpace(value)) replacement.Add(value);
            capabilities = replacement;
        }
        private void EnsureCapabilities()
        {
            if (capabilities != null) return;
            capabilities = new HashSet<string>(StringComparer.Ordinal);
            if (initialCapabilities != null) foreach (var value in initialCapabilities) if (!string.IsNullOrWhiteSpace(value)) capabilities.Add(value);
        }
    }
}
