using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EasyResources
{
    /// <summary>One authored asset containing resource records. Wallets keep direct refs; no runtime registry.</summary>
    [CreateAssetMenu(menuName = "EasyGameFramework/Resources/Resource Database", fileName = "Resource Database")]
    public sealed class ResourceDatabase : ScriptableObject, IEnumerable<ResourceDefinition>, ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private ResourceDefinition[] resources = Array.Empty<ResourceDefinition>();
        [NonSerialized] private IReadOnlyList<ResourceDefinition> view;
        [NonSerialized] private Dictionary<string, ResourceDefinition> byId;
        [NonSerialized] private HashSet<ResourceDefinition> members;
        [NonSerialized] private ResourceDefinition[] observed = Array.Empty<ResourceDefinition>();
        [NonSerialized] private bool configurationValid;
        public IReadOnlyList<ResourceDefinition> Resources => view ?? (view = Array.AsReadOnly(resources));

        public bool Contains(ResourceDefinition resource)
        {
            if (resource == null || resources == null) return false;
            ValidateConfiguration();
            return members.Contains(resource);
        }

        public bool TryGet(string id, out ResourceDefinition resource)
        {
            resource = null;
            if (string.IsNullOrWhiteSpace(id) || resources == null) return false;
            ValidateConfiguration();
            return byId.TryGetValue(id, out resource);
        }

        public void ValidateConfiguration()
        {
            if (configurationValid) return;
            if (resources == null) throw new ArgumentException("Database Resources is missing.");
            var nextById = new Dictionary<string, ResourceDefinition>(resources.Length, StringComparer.Ordinal);
            var nextMembers = new HashSet<ResourceDefinition>(ReferenceComparer<ResourceDefinition>.Instance);
            for (var i = 0; i < resources.Length; i++)
            {
                var resource = resources[i];
                if (resource == null) throw new ArgumentException($"Database entry {i + 1} is missing. Restore the record or remove the broken row.");
                resource.ValidateDefinition();
                if (nextById.ContainsKey(resource.Id))
                    throw new ArgumentException($"Duplicate resource ID in database: {resource.DisplayName}. Keep one record per ID.");
                nextById.Add(resource.Id, resource);
                nextMembers.Add(resource);
            }
            StopObserving();
            observed = (ResourceDefinition[])resources.Clone();
            foreach (var resource in observed) resource.ConfigurationChanged += InvalidateConfiguration;
            byId = nextById; members = nextMembers; configurationValid = true;
        }

        /// <summary>Invalidate local authoring caches after an explicit configuration change; no global registry.</summary>
        public void InvalidateConfiguration() { configurationValid = false; view = null; }

        private void StopObserving()
        {
            foreach (var resource in observed)
                if (!ReferenceEquals(resource, null)) resource.ConfigurationChanged -= InvalidateConfiguration;
            observed = Array.Empty<ResourceDefinition>();
        }

        /// <summary>Create transient configuration from code-owned definitions. Persisted databases author native subassets in Editor.</summary>
        public static ResourceDatabase Create(string name, params ResourceDefinition[] resources)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            var database = CreateInstance<ResourceDatabase>();
            database.name = name;
            database.resources = (ResourceDefinition[])resources.Clone();
            try { database.ValidateConfiguration(); return database; }
            catch { if (Application.isPlaying) Destroy(database); else DestroyImmediate(database); throw; }
        }

        public IEnumerator<ResourceDefinition> GetEnumerator() => ((IEnumerable<ResourceDefinition>)resources).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private void OnValidate() => InvalidateConfiguration();
        private void OnDisable() { StopObserving(); InvalidateConfiguration(); }
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => InvalidateConfiguration();
    }
}
