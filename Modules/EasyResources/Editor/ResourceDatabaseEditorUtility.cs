using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Editor
{
    /// <summary>Native subasset authoring. No runtime registry and no implicit migration of external assets.</summary>
    public static class ResourceDatabaseEditorUtility
    {
        public static ResourceDatabase FindDatabase(ResourceDefinition resource) => resource == null ? null :
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(resource)) as ResourceDatabase;

        /// <summary>
        /// Prove declared membership and native file ownership, including hidden records for which
        /// Unity's IsSubAsset may return false. This is an Editor authoring check, not a runtime lookup.
        /// </summary>
        public static bool IsOwnedResource(ResourceDatabase database, ResourceDefinition resource)
        {
            if (database == null || resource == null || !database.Contains(resource)) return false;
            var path = AssetDatabase.GetAssetPath(database);
            if (!TryDatabaseIdentity(database, path, out var guid, out var mainId)) return false;
            return NativeRecord(resource, path, guid, mainId, AssetDatabase.LoadAllAssetsAtPath(path));
        }

        private static bool TryDatabaseIdentity(ResourceDatabase database, string path, out string guid, out long mainId)
        {
            guid = null; mainId = 0;
            return !string.IsNullOrEmpty(path) && EditorUtility.IsPersistent(database) &&
                AssetDatabase.LoadMainAssetAtPath(path) == database &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(database, out guid, out mainId) &&
                !string.IsNullOrEmpty(guid) && mainId != 0;
        }

        private static bool NativeRecord(ResourceDefinition resource, string path, string databaseGuid, long mainId, UnityEngine.Object[] loaded)
        {
            if (!EditorUtility.IsPersistent(resource) || AssetDatabase.GetAssetPath(resource) != path ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resource, out string guid, out long fileId) ||
                guid != databaseGuid || fileId == 0 || fileId == mainId) return false;
            foreach (var asset in loaded)
                if (ReferenceEquals(asset, resource)) return true;
            return false;
        }

        public static void ValidateAsset(ResourceDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            database.ValidateConfiguration();
            var path = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Save the Resource Database as an asset before editing its records.");
            if (!TryDatabaseIdentity(database, path, out var guid, out var mainId))
                throw new ArgumentException("The Resource Database must be the persistent main asset of its own file.");
            var loaded = AssetDatabase.LoadAllAssetsAtPath(path);
            var fileIds = new HashSet<long> { mainId };
            foreach (var resource in database.Resources)
            {
                if (!NativeRecord(resource, path, guid, mainId, loaded))
                    throw new ArgumentException($"{resource.DisplayName} is an external asset. Database records must be created inside this file; use Add Resource.");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resource, out string _, out long fileId);
                if (!fileIds.Add(fileId)) throw new ArgumentException("Database records must have distinct native file IDs.");
            }
            foreach (var asset in loaded)
                if (asset is ResourceDefinition resource && !database.Contains(resource))
                    throw new ArgumentException($"{resource.DisplayName} is an unlisted resource record in this file. Restore its database membership before continuing.");
        }

        private static void CheckEditable(ResourceDatabase database)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit the resource database outside Play Mode.");
            ValidateAsset(database);
            if (!AssetDatabase.IsOpenForEdit(database)) throw new InvalidOperationException("The database is read-only. Make it writable before editing.");
        }

        public static ResourceDefinition AddResource(ResourceDatabase database, string displayName)
        {
            CheckEditable(database);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enter a resource name.");
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add resource record");
            ResourceDefinition resource = null;
            try
            {
                resource = ResourceDefinition.Create(displayName.Trim());
                resource.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(resource, database);
                Undo.RegisterCreatedObjectUndo(resource, "Add resource record");
                var rows = new List<ResourceDefinition>(database.Resources) { resource };
                SetResources(database, rows);
                EditorUtility.SetDirty(resource);
                AssetDatabase.SaveAssetIfDirty(database);
                ResourceIdValidator.Invalidate();
                return resource;
            }
            catch { Undo.RevertAllDownToGroup(group); if (resource != null && !EditorUtility.IsPersistent(resource)) UnityEngine.Object.DestroyImmediate(resource); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        /// <summary>
        /// Copy authored records into one database preserving domain IDs and all fields. Returns explicit
        /// old->new refs; caller remaps its agreed content scope, verifies reload, then deletes old files.
        /// No external files/references are modified here. Duplicate target/source IDs reject before copying.
        /// </summary>
        public static Dictionary<ResourceDefinition, ResourceDefinition> ImportDefinitions(ResourceDatabase database, IReadOnlyList<ResourceDefinition> sources)
        {
            CheckEditable(database);
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in database.Resources) ids.Add(resource.Id);
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source == null) throw new ArgumentException("Source resource is missing.");
                source.ValidateDefinition();
                if (!ids.Add(source.Id)) throw new ArgumentException("Import has a duplicate resource ID: " + source.DisplayName);
            }
            var remap = new Dictionary<ResourceDefinition, ResourceDefinition>();
            if (sources.Count == 0) return remap;
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Import resource records");
            try
            {
                var rows = new List<ResourceDefinition>(database.Resources);
                for (var i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    var resource = UnityEngine.Object.Instantiate(source);
                    resource.name = source.name;
                    resource.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(resource, database);
                    Undo.RegisterCreatedObjectUndo(resource, "Import resource record");
                    rows.Add(resource); remap.Add(source, resource);
                }
                SetResources(database, rows);
                AssetDatabase.SaveAssetIfDirty(database);
                ResourceIdValidator.Invalidate();
                return remap;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }

        public static bool TryRemoveResource(ResourceDatabase database, ResourceDefinition resource, out string error)
        {
            error = null;
            try
            {
                CheckEditable(database);
                if (!database.Contains(resource)) throw new ArgumentException("The selected resource does not belong to this database.");
                var references = FindReferences(database, resource);
                if (references.Count != 0)
                {
                    error = "Resource is in use. Remove or replace its references first:\n" + string.Join("\n", references);
                    return false;
                }
                Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Remove resource record");
                try
                {
                    var rows = new List<ResourceDefinition>(database.Resources); rows.Remove(resource);
                    SetResources(database, rows);
                    Undo.DestroyObjectImmediate(resource);
                    AssetDatabase.SaveAssetIfDirty(database);
                    ResourceIdValidator.Invalidate();
                    return true;
                }
                catch { Undo.RevertAllDownToGroup(group); throw; }
                finally { Undo.CollapseUndoOperations(group); }
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static void SetResources(ResourceDatabase database, List<ResourceDefinition> resources)
        {
            var serialized = new SerializedObject(database);
            serialized.Update();
            var rows = serialized.FindProperty("resources"); rows.arraySize = resources.Count;
            for (var i = 0; i < resources.Count; i++) rows.GetArrayElementAtIndex(i).objectReferenceValue = resources[i];
            serialized.ApplyModifiedProperties();
            database.OnAfterDeserialize();
            EditorUtility.SetDirty(database);
        }

        // Dependencies only locate candidate files; inspect exact object refs so another entry in the
        // same database does not falsely block removal. A failed/incomplete scan refuses removal.
        private static List<string> FindReferences(ResourceDatabase database, ResourceDefinition resource)
        {
            var result = new List<string>();
            var databasePath = AssetDatabase.GetAssetPath(database);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resource, out string guid, out long fileId))
                throw new InvalidOperationException("Could not resolve this resource's serialized identity; removal was cancelled.");
            var externalPattern = new Regex(@"\{[^}\r\n]*\bfileID:\s*" + fileId + @"\s*,[^}\r\n]*\bguid:\s*" + Regex.Escape(guid) + @"\b[^}\r\n]*\}");
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                var dependent = path == databasePath;
                if (!dependent)
                    foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                        if (dependency == databasePath) { dependent = true; break; }
                if (!dependent) continue;
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path)) throw new InvalidOperationException("Cannot inspect references in " + path + "; removal was cancelled.");
                    var text = File.ReadAllText(path);
                    if (!text.StartsWith("%YAML", StringComparison.Ordinal))
                        throw new InvalidOperationException("Save " + path + " using Force Text serialization before removing resource records.");
                    if (externalPattern.IsMatch(text)) result.Add(path);
                    continue;
                }
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                if (assets.Length == 0) throw new InvalidOperationException("Cannot fully inspect " + path + "; removal was cancelled.");
                foreach (var asset in assets)
                    if (asset != null && References(asset, database, resource)) { result.Add(path); break; }
            }
            // Includes current unsaved scenes and transient authored profiles. Never treats an Editor's
            // selection/inspector target as a gameplay reference.
            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
                if (component != null && !EditorUtility.IsPersistent(component) && component.gameObject.scene.IsValid() && References(component, database, resource))
                    result.Add("Loaded object: " + component.gameObject.name + " (" + component.GetType().Name + ")");
            foreach (var asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                if (asset != null && asset != database && asset != resource && !EditorUtility.IsPersistent(asset) && asset.hideFlags == HideFlags.None &&
                    !asset.GetType().Assembly.GetName().Name.Contains("Editor") && References(asset, database, resource))
                    result.Add("Loaded configuration: " + asset.name);
            return result;
        }

        private static bool References(UnityEngine.Object owner, ResourceDatabase database, ResourceDefinition resource)
        {
            try
            {
                using var serialized = new SerializedObject(owner);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == resource &&
                        !(owner == database && property.propertyPath.StartsWith("resources.Array.data[", StringComparison.Ordinal)))
                        return true;
                return false;
            }
            catch (Exception exception) { throw new InvalidOperationException("Could not inspect " + owner.name + "; removal was cancelled.", exception); }
        }
    }
}
