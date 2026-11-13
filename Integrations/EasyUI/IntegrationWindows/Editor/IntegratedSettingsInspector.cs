using UnityEditor;

namespace EasyUI.Integrations.Editor
{
    [CustomEditor(typeof(IntegratedSettingsWindow))]
    public sealed class IntegratedSettingsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var view = (IntegratedSettingsWindow)target;
            EditorGUILayout.HelpBox("Language selection requires configured locales and an explicit await EasyLocalization.Localization.InitializeAsync(config) from your game bootstrap. Use Tools > EasyGameFramework > Localization > Translations > First setup to author the configuration. Controls, audio and graphics work before localization is ready.", MessageType.Info);
            if (EditorApplication.isPlaying && !string.IsNullOrEmpty(view.LanguageStatus))
                EditorGUILayout.HelpBox(view.LanguageStatus, MessageType.Info);
            if (!view.AudioWindow || !view.GraphicsWindow)
                EditorGUILayout.HelpBox("Use Tools > EasyGameFramework > UI > Create Integration Windows for a wired set, or assign the audio and graphics definitions here.", MessageType.Info);
            if (view.AudioWindow && (!view.AudioWindow.Prefab || !view.AudioWindow.Prefab.GetComponent<FeedbackSettingsWindow>()))
                EditorGUILayout.HelpBox("Audio Window needs a prefab with Feedback Settings Window.", MessageType.Error);
            if (view.GraphicsWindow && (!view.GraphicsWindow.Prefab || !view.GraphicsWindow.Prefab.GetComponent<EasySettings.UI.SettingsWindow>()))
                EditorGUILayout.HelpBox("Graphics Window needs a prefab with EasySettings Settings Window.", MessageType.Error);
            if (!view.GraphicsController)
                EditorGUILayout.HelpBox("Assign the player's Settings Controller, place this window below it, or call BindGraphics(controller) before Open. Keep the controller alive independently of the window.", MessageType.Info);
        }
    }
}
