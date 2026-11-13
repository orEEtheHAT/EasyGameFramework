using UnityEditor;
using UnityEngine;

namespace EasyBuilding.Editor
{
    [CustomEditor(typeof(BuildingDefinition))]
    public sealed class BuildingDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("Prefab"); Field("Placement");
            var mode = (PlacementMode)serializedObject.FindProperty("Placement").enumValueIndex;
            if (mode == PlacementMode.Grid) { Field("GridSize"); Field("GridLayout"); Field("GridPlane"); Field("GridOrigin"); }
            if (mode == PlacementMode.Socket) { Field("SocketType"); Field("AttachmentSocketId"); Field("SocketRotationStep"); }
            Field("RequireZone");
            if (serializedObject.FindProperty("RequireZone").boolValue) Field("ZoneId");
            Field("BoundsCenter"); Field("BoundsSize");
            if (((BuildingDefinition)target).Prefab != null && GUILayout.Button("Fit bounds to prefab meshes"))
            {
                serializedObject.ApplyModifiedProperties();
                if (!BuildingAuthoring.FitBounds((BuildingDefinition)target, out var reason)) Debug.LogWarning(reason, target);
                serializedObject.Update();
            }
            Field("Payment");
            if (serializedObject.FindProperty("Payment").enumValueIndex == (int)BuildingPayment.Paid)
            {
                Field("Costs");
                EditorGUILayout.HelpBox("Add Building Resources to the world, assign its resource assets/containers, then assign it to Building System > Cost Provider. Preview and cancel never spend resources.", MessageType.Info);
            }
            if (Foldout("Shape", "Compound shape and clearance"))
            {
                Field("Volumes"); Field("Cells");
                if (serializedObject.FindProperty("Cells").arraySize != 0) Field("CellSize");
                Field("Clearances");
                EditorGUILayout.HelpBox("Empty Volumes and Cells use the basic bounds above. Clearances may overlap each other, but solid buildings cannot occupy them.", MessageType.Info);
            }
            if (Foldout("Surface", "Surfaces and obstacles"))
            {
                Field("CheckObstacles"); if (serializedObject.FindProperty("CheckObstacles").boolValue) Field("ObstacleLayers");
                Field("RequireSurface");
                if (serializedObject.FindProperty("RequireSurface").boolValue)
                {
                    Field("Surfaces"); Field("SurfaceLayers"); Field("MaximumSlope"); Field("SurfaceProbeDistance"); Field("SupportPoints");
                }
                Field("AlignToSurface");
            }
            if (Foldout("Access", "Rules and access")) { Field("Access"); Field("Rules"); }
            if (Foldout("Construction", "Construction and upgrades"))
            {
                Field("WorkRequired");
                if (serializedObject.FindProperty("WorkRequired").floatValue > 0)
                { Field("ConstructionPayment"); Field("ConstructionVisual"); Field("ConstructionPhases"); Field("CancellationRefund"); }
                Field("Upgrades");
            }
            if (Foldout("Pricing", "Pricing and refunds")) { Field("CostBasis"); Field("DemolitionRefund"); }
            if (Foldout("Presentation", "Catalog and persistence"))
            {
                Field("DisplayName"); Field("Icon"); Field("Category"); Field("Tags"); Field("SaveVersion");
                EditorGUILayout.LabelField("Stable ID", ((BuildingDefinition)target).Id);
            }
            serializedObject.ApplyModifiedProperties();
            var definition = (BuildingDefinition)target;
            if (definition.ConfigurationError != null) EditorGUILayout.HelpBox(definition.ConfigurationError, MessageType.Error);
            if (definition.Prefab != null)
            {
                if (definition.Prefab.GetComponentInChildren<BuildingSpace>(true) != null || definition.Prefab.GetComponentInChildren<BuildingSystem>(true) != null)
                    EditorGUILayout.HelpBox("Put Building System and Building Space on persistent scene/platform roots outside this prefab. This keeps contents safe when a building is replaced or removed.", MessageType.Error);
                var sockets = definition.Prefab.GetComponentsInChildren<BuildingSocket>(true);
                var ids = new System.Collections.Generic.HashSet<string>();
                foreach (var socket in sockets)
                    if (!ids.Add(socket.Id))
                    { EditorGUILayout.HelpBox("Duplicate socket ID in Prefab. Select the duplicate socket and use its context menu: Regenerate Socket ID.", MessageType.Error); break; }
            }
        }
        private void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
        private static bool Foldout(string key, string title)
        {
            string setting = "EasyBuilding.Definition." + key;
            bool open = EditorGUILayout.Foldout(SessionState.GetBool(setting, false), title, true);
            SessionState.SetBool(setting, open); return open;
        }
    }

    [CustomEditor(typeof(BuildingSystem))]
    public sealed class BuildingSystemEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var system = (BuildingSystem)target;
            var property = serializedObject.FindProperty("costProvider");
            if (property.objectReferenceValue != null && !(property.objectReferenceValue is IBuildingCostProvider) && !(property.objectReferenceValue is IBuildingEconomy))
                EditorGUILayout.HelpBox("Assign an IBuildingEconomy or IBuildingCostProvider component. The EasyResources sample provides a ready payment adapter.", MessageType.Error);
            var placer = system.GetComponent<BuildingPlacer>();
            var selected = Application.isPlaying && placer != null ? placer.Session?.Selected : system.Selected;
            var definition = selected != null ? selected : placer != null ? placer.StartingBuilding : null;
            if (definition != null && definition.Payment == BuildingPayment.Paid && system.CostProvider == null && system.Economy == null &&
                !(definition.WorkRequired > 0 && definition.ConstructionPayment == BuildingConstructionPayment.Delivery))
                EditorGUILayout.HelpBox("Paid placement is blocked. Assign a Cost Provider here, or set the definition's Payment to Free.", MessageType.Error);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Buildings / revision", system.Count + " / " + system.Revision);
                if (system.PendingConnectionCount != 0) EditorGUILayout.HelpBox(system.PendingConnectionCount + " connections await unloaded endpoints.", MessageType.Info);
                if (system.MissingBuildingCount != 0) EditorGUILayout.HelpBox(system.MissingBuildingCount + " preserved records await missing content.", MessageType.Warning);
                if (system.IsReadOnly) EditorGUILayout.HelpBox("Replica world. Send gameplay requests to the authority.", MessageType.Info);
            }
        }
    }

    [CustomEditor(typeof(BuildingPlacer))]
    public sealed class BuildingPlacerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var placer = (BuildingPlacer)target;
            if (placer.Actor != null && !(placer.Actor is IBuildingActor) && !(placer.Actor is IBuildingSessionProvider))
                EditorGUILayout.HelpBox("Actor must implement IBuildingActor or IBuildingSessionProvider. Assign the player's Building Actor for a shared selection.", MessageType.Error);
            else if (Application.isPlaying && placer.ConfigurationError != null)
                EditorGUILayout.HelpBox(placer.ConfigurationError, MessageType.Error);
        }
    }

    [CustomEditor(typeof(BuildingSocket))]
    public sealed class BuildingSocketEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Without an Attachment Socket Id, the building pivot snaps here. With one, the two authored sockets align. Type, size and roles must match. Regenerate IDs only when authoring duplicates.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying)) if (GUILayout.Button("Regenerate Socket ID"))
            {
                Undo.RecordObject(target, "Regenerate Socket ID");
                ((BuildingSocket)target).RegenerateId(); EditorUtility.SetDirty(target);
            }
        }
    }
}
