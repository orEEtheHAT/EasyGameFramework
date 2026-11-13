using UnityEditor;

namespace EasyBuilding.Integrations.Editor
{
    [CustomEditor(typeof(BuildingFeedbacks))]
    public sealed class BuildingFeedbacksEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var error = ((BuildingFeedbacks)target).ConfigurationError;
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
