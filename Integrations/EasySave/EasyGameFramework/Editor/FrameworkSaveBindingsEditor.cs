using EasySave.Integrations;
using UnityEditor;

namespace EasyGameFramework.Editor
{
    [CustomEditor(typeof(FrameworkSaveBindings))]
    public sealed class FrameworkSaveBindingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var source = (FrameworkSaveBindings)target;
            var error = source.ConfigurationError;
            EditorGUILayout.HelpBox(error ?? "Add this component to Save Controller's participant list. Keep stable IDs unchanged. Progress and Preferences are separate; prepare the game's snapshot boundary before saving/loading.", error == null ? MessageType.Info : MessageType.Error);
            if (source.SaveLanguage) EditorGUILayout.HelpBox("Await InitializeAsync() before preferences capture/load. Initialization does not automatically load preferences or write files.", MessageType.Info);
        }
    }
}
