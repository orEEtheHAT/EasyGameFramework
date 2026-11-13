using UnityEditor;
using UnityEngine;

namespace EasySave.Editor
{
    [CustomEditor(typeof(SaveConfiguration))]
    public sealed class SaveConfigurationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var configuration = (SaveConfiguration)target;
            string error = configuration.ConfigurationError;
            EditorGUILayout.HelpBox(error ?? "Defaults are copied into each session. Progress and Preferences use different folders. Autosave only writes Progress.", error == null ? MessageType.Info : MessageType.Error);
            EditorGUILayout.HelpBox("Local files: desktop backend. WebGL, cloud and platform SDK storage require a separately tested backend.", MessageType.Info);
        }
    }
    [CustomEditor(typeof(SaveController))]
    public sealed class SaveControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var controller = (SaveController)target; string error = controller.ConfigurationError;
            EditorGUILayout.HelpBox(error ?? "Register stable participant keys before use. Your game owns pausing simulation and finishing transient commands. Disable cancels this session; quitting does not start an unawaited save.", error == null ? MessageType.Info : MessageType.Error);
            if (Application.isPlaying && controller.LastResult != null) EditorGUILayout.HelpBox(controller.LastResult.ToString(), controller.LastResult.Success ? MessageType.Info : MessageType.Warning);
        }
    }
}
