using UnityEditor;
using UnityEngine;

namespace EasyCombat.Editor
{
    [CustomEditor(typeof(MovementSurface))]
    public sealed class MovementSurfaceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var volume=(MovementSurface)target;
            var colliders=volume.GetComponents<Collider>();
            if(colliders.Length!=1 || !colliders[0].isTrigger || !colliders[0].enabled)
                EditorGUILayout.HelpBox("Slow Volume needs one enabled Collider with Is Trigger on this GameObject. Add or configure a Box/Sphere/Capsule Collider. Trigger contacts also require a Rigidbody on one side.",MessageType.Error);
            if(!volume.Effect || !volume.Effect.IsValid)
                EditorGUILayout.HelpBox("Create EasyGameFramework > Combat > Environment > Movement Influence and assign it to Effect. Speed multiplier must be between 0 (stop) and 1 (unchanged).",MessageType.Error);
            EditorGUILayout.HelpBox("Targets need MovementInfluences and a movement adapter (Combat FPS, NavMesh or RigidbodyCombatSpeed). Overlaps use the strongest slow. This volume applies a spatial movement coefficient; timed statuses use StatusController.",MessageType.Info);
        }
    }
}
