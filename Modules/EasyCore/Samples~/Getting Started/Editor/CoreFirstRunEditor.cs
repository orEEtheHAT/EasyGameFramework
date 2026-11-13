using EasyCore.Samples;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    [CustomEditor(typeof(CoreFirstRun))]
    internal sealed class CoreFirstRunEditor : UnityEditor.Editor
    {
        private const string MenuPath = "Tools/EasyGameFramework/Examples/Core/Create First Run";

        [MenuItem(MenuPath)]
        private static void CreateFirstRun()
        {
            var go = new GameObject("EasyCore First Run");
            Undo.RegisterCreatedObjectUndo(go, "Create EasyCore First Run");
            Undo.AddComponent<CoreFirstRun>(go);
            Selection.activeGameObject = go;
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreateFirstRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        public override void OnInspectorGUI()
        {
            var sample = (CoreFirstRun)target;
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode. This example checks numbers, manual deadlines and owned cleanup. Expected result: 8/8 in the Console. No fields need configuring.", MessageType.Info);
                return;
            }
            if (!sample.Completed)
            {
                EditorGUILayout.HelpBox("Waiting for Start. Enable this component and its GameObject to run the example.", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox($"{sample.Checks - sample.Failures}/{sample.Checks} checks passed.",
                sample.Failures == 0 ? MessageType.Info : MessageType.Error);
            if (sample.Failures > 0) EditorGUILayout.HelpBox("Open the Console for the failed check and its cause.", MessageType.Error);
        }
    }
}
