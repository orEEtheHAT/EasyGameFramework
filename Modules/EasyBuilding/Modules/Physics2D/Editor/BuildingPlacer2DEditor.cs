using UnityEditor;
using UnityEngine;

namespace EasyBuilding.TwoD.Editor
{
    [CustomEditor(typeof(BuildingPlacer2D))]
    public sealed class BuildingPlacer2DEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var placer = (BuildingPlacer2D)target;
            if (placer.Actor != null && !(placer.Actor is IBuildingActor) && !(placer.Actor is IBuildingSessionProvider))
                EditorGUILayout.HelpBox("Assign the player's Building Actor or another IBuildingSessionProvider to share selection.", MessageType.Error);
            else if (Application.isPlaying && placer.ConfigurationError != null)
                EditorGUILayout.HelpBox(placer.ConfigurationError, MessageType.Error);
        }
    }
}
