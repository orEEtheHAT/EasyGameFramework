using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace EasyBuilding.Editor
{
    public static class BuildingAuthoring
    {
        [MenuItem("Assets/Create/EasyGameFramework/Building/Definition From Selected Prefab", priority = 201)]
        public static void CreateDefinitionFromSelection()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab)) throw new InvalidOperationException("Select a prefab asset in the Project window.");
            var source = AssetDatabase.GetAssetPath(prefab);
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(source).Replace('\\', '/') + "/" + prefab.name + " Building.asset");
            var definition = CreateDefinition(prefab, path); Selection.activeObject = definition; EditorGUIUtility.PingObject(definition);
        }
        [MenuItem("Assets/Create/EasyGameFramework/Building/Definition From Selected Prefab", true)]
        private static bool CanCreateFromSelection() => Selection.activeObject is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab);
        public static BuildingDefinition CreateDefinition(GameObject prefab, string assetPath)
        {
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab) || string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a prefab asset and an unused .asset path under Assets.");
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) throw new ArgumentException("Choose an unused definition path.");
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = prefab;
            definition.DisplayName = prefab.name; FitBounds(definition, out _);
            AssetDatabase.CreateAsset(definition, assetPath); AssetDatabase.SaveAssets(); return definition;
        }
        public static bool FitBounds(BuildingDefinition definition, out string reason)
        {
            reason = null;
            if (definition == null || definition.Prefab == null) { reason = "Assign a prefab first."; return false; }
            var root = definition.Prefab.transform; var bounds = new Bounds(); bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                var local = renderer.localBounds; var matrix = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = matrix.MultiplyPoint3x4(corner);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            if (!found) { reason = "No Mesh Renderer or Skinned Mesh Renderer was found. Set bounds manually for invisible or procedural prefabs."; return false; }
            Undo.RecordObject(definition, "Fit Building Bounds"); definition.BoundsCenter = bounds.center;
            definition.BoundsSize = Vector3.Max(bounds.size, Vector3.one * 0.01f); EditorUtility.SetDirty(definition); return true;
        }
        [MenuItem("GameObject/EasyGameFramework/Building/Building World", false, 10)]
        private static void CreateWorld(MenuCommand command)
        {
            var root = new GameObject("Building World"); Undo.RegisterCreatedObjectUndo(root, "Create Building World");
            GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
            root.AddComponent<BuildingSystem>(); var placer = root.AddComponent<BuildingPlacer>(); placer.ViewCamera = Camera.main;
            Selection.activeGameObject = root;
        }
        [MenuItem("GameObject/EasyGameFramework/Building/Socket", false, 11)]
        private static void CreateSocket(MenuCommand command)
        {
            var root = new GameObject("Building Socket"); Undo.RegisterCreatedObjectUndo(root, "Create Building Socket");
            GameObjectUtility.SetParentAndAlign(root, command.context as GameObject); root.AddComponent<BuildingSocket>(); Selection.activeGameObject = root;
        }
    }
    [CustomEditor(typeof(BuildingZone)), CanEditMultipleObjects]
    internal sealed class BuildingZoneHandles : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle handle = new BoxBoundsHandle();
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var zone = (BuildingZone)target;
            if (zone.Size.x <= 0 || zone.Size.y <= 0 || zone.Size.z <= 0 || zone.transform.lossyScale.sqrMagnitude < 0.000001f)
                EditorGUILayout.HelpBox("Zone size and transform scale must be nonzero. Drag the handles in the Scene view to resize the zone.", MessageType.Error);
        }
        private void OnSceneGUI()
        {
            var zone = (BuildingZone)target;
            using (new Handles.DrawingScope(zone.transform.localToWorldMatrix))
            {
                handle.center = zone.Center; handle.size = zone.Size;
                EditorGUI.BeginChangeCheck(); handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(zone, "Resize Building Zone"); zone.Center = handle.center; zone.Size = Vector3.Max(handle.size, Vector3.one * 0.001f); EditorUtility.SetDirty(zone); }
            }
        }
    }
    public sealed class BuildingDiagnostics : EditorWindow
    {
        private readonly List<string> messages = new List<string>();
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private Vector2 scroll;
        [MenuItem("Tools/EasyGameFramework/Building/Scene Diagnostics")]
        public static void Open() => GetWindow<BuildingDiagnostics>("EasyBuilding Diagnostics");
        private void OnGUI()
        {
            if (GUILayout.Button("Inspect loaded scenes")) Scan();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < messages.Count; i++)
            {
                EditorGUILayout.BeginHorizontal(); EditorGUILayout.HelpBox(messages[i], MessageType.Warning);
                if (objects[i] != null && GUILayout.Button("Select", GUILayout.Width(60))) Selection.activeObject = objects[i];
                EditorGUILayout.EndHorizontal();
            }
            if (messages.Count == 0) EditorGUILayout.HelpBox("No issues in the last scan. Inspect again after changing scene setup.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        public void Scan()
        {
            messages.Clear(); objects.Clear(); var sceneSockets = new Dictionary<string, BuildingSocket>(StringComparer.Ordinal);
            foreach (var socket in UnityEngine.Object.FindObjectsByType<BuildingSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (socket.GetComponentInParent<PlacedBuilding>() != null) continue;
                if (string.IsNullOrWhiteSpace(socket.Id) || sceneSockets.ContainsKey(socket.Id)) Add("Scene socket ID is missing or duplicated. Regenerate the duplicated socket's ID before making saves.", socket);
                else sceneSockets.Add(socket.Id, socket);
                if (socket.Size <= 0 || float.IsNaN(socket.Size) || float.IsInfinity(socket.Size)) Add("Socket size must be positive and finite.", socket);
            }
            foreach (var placer in UnityEngine.Object.FindObjectsByType<BuildingPlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!placer.ExternalTargeting && placer.ViewCamera == null && Camera.main == null)
                    Add("Assign View Camera to Building Placer or tag a camera MainCamera. Pose/XR/AR controllers use External Targeting.", placer);
                if (placer.StartingBuilding != null && placer.StartingBuilding.ConfigurationError != null) Add(placer.StartingBuilding.ConfigurationError, placer.StartingBuilding);
                if (placer.Actor != null && !(placer.Actor is IBuildingActor) && !(placer.Actor is IBuildingSessionProvider))
                    Add("Assign a Building Actor or another IBuildingSessionProvider to Actor, or clear it for standalone placement.", placer);
            }
            foreach (var space in UnityEngine.Object.FindObjectsByType<BuildingSpace>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!space.IsValid) Add("Building Space requires a positive uniform transform scale.", space);
            Repaint();
        }
        private void Add(string message, UnityEngine.Object owner) { messages.Add(message); objects.Add(owner); }
    }
}
