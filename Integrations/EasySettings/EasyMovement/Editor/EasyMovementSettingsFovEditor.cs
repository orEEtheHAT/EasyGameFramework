using UnityEditor;
using UnityEngine;

namespace EasySettings.Integrations.Editor
{
    [CustomEditor(typeof(EasyMovementSettingsFov))]
    public sealed class EasyMovementSettingsFovEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var adapter = (EasyMovementSettingsFov)target;
            var settings = adapter.GetComponent<SettingsController>();
            if (settings.FieldOfViewProvider != adapter) {
                EditorGUILayout.HelpBox("Assign this component as Settings Controller > Field Of View Provider.", MessageType.Warning);
                if (GUILayout.Button("Use for Settings FOV")) { Undo.RecordObject(settings, "Bind settings FOV"); settings.BindFieldOfViewProvider(adapter); EditorUtility.SetDirty(settings); }
            }
            if (!adapter.PlayerFov) EditorGUILayout.HelpBox("Add EasyMovement > First Person FOV beside the player's FPS controller, then assign it here. No output-camera FOV is written.", MessageType.Warning);
            else if (!adapter.PlayerFov.isActiveAndEnabled) EditorGUILayout.HelpBox("Enable First Person FOV on the player.", MessageType.Warning);
            if (Application.isPlaying) {
                if (adapter.TryRead(out var value, out var reason)) EditorGUILayout.LabelField("Player base vertical FOV", value.ToString("0.##"));
                else EditorGUILayout.HelpBox(reason, MessageType.Warning);
            }
            EditorGUILayout.HelpBox("Saved FOV is the player's base. EasyMovement preserves gait-camera differences and live gameplay offsets. Other owners keep their own handles.", MessageType.Info);
        }
    }
}
