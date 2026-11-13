using UnityEditor;
using UnityEngine;
using EasySettings.UI;

namespace EasyGameFramework.Editor
{
    [CustomEditor(typeof(SettingsWindow))]
    public sealed class SettingsWindowEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var view = (SettingsWindow)target;
            if (!view.Controller) EditorGUILayout.HelpBox("Assign the scene Settings Controller. Keep it outside this window so closing UI does not end the graphics session.", MessageType.Error);
            else if (view.Controller.transform.IsChildOf(view.transform)) EditorGUILayout.HelpBox("Move Settings Controller outside this window. Disabling the controller restores startup graphics.", MessageType.Warning);
            EditorGUILayout.HelpBox("Open through EasyUI WindowManager. For gameplay blocking/cursor requirements, configure its EasyInput provider. Every child window owns its own request.", MessageType.Info);
        }
    }
}
