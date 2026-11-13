using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EasySettings.Editor
{
    [CustomEditor(typeof(SettingsController))]
    public sealed class SettingsControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (SettingsController)target;
            if (!controller.Defaults) EditorGUILayout.HelpBox("Create Assets > Create > EasyGameFramework > Settings > Defaults, then assign that asset. Reset and Apply are explicit.", MessageType.Error);
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) EditorGUILayout.HelpBox("Assign a URP pipeline in Project Settings > Graphics / Quality. Built-in and HDRP are not supported.", MessageType.Error);
            if (!controller.TargetCamera && !controller.FollowMainCamera) EditorGUILayout.HelpBox("Assign a camera for FOV and post-processing controls. Pipeline and frame pacing settings work independently.", MessageType.Info);
            var camera = controller.TargetCamera ? controller.TargetCamera : controller.FollowMainCamera ? Camera.main : null;
            if (controller.FieldOfViewProvider) {
                if (!(controller.FieldOfViewProvider is ISettingsFieldOfViewProvider provider)) EditorGUILayout.HelpBox("Assign an ISettingsFieldOfViewProvider component, such as EasyMovement Settings FOV.", MessageType.Error);
                else if (Application.isPlaying && !provider.TryRead(out _, out var reason)) EditorGUILayout.HelpBox(reason, MessageType.Info);
            } else if (!UrpSettingsBackend.CanControlFieldOfView(camera, out var fovReason)) EditorGUILayout.HelpBox(fovReason, MessageType.Info);
            if (!string.IsNullOrEmpty(controller.Error)) EditorGUILayout.HelpBox(controller.Error, MessageType.Error);
            if (controller.Session != null) {
                var session = controller.Session;
                EditorGUILayout.LabelField("Observed screen", session.Observed.Display.ToString());
                EditorGUILayout.LabelField("Preview", session.HasPreview ? session.RemainingSeconds.ToString("0.0") + " s" : "None");
                if (!session.Capabilities.Camera) EditorGUILayout.HelpBox(session.Capabilities.CameraReason, MessageType.Info);
                EditorGUILayout.HelpBox(session.Capabilities.PostProcessingReason + "\n" + session.Capabilities.VolumeReason, MessageType.Info);
            }
        }
    }
    [CustomEditor(typeof(SettingsDefaults))]
    public sealed class SettingsDefaultsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var settings = (SettingsDefaults)target;
            var limits = settings.Limits;
            if (!EasyCore.Numeric.IsFinite(limits.MinRenderScale) || !EasyCore.Numeric.IsFinite(limits.MaxRenderScale) || limits.MinRenderScale < .1f || limits.MaxRenderScale > 2 || limits.MinRenderScale > limits.MaxRenderScale)
                EditorGUILayout.HelpBox("Render scale limits must be finite, between 0.1 and 2, with minimum <= maximum.", MessageType.Error);
            if (!EasyCore.Numeric.IsFinite(limits.MinFieldOfView) || !EasyCore.Numeric.IsFinite(limits.MaxFieldOfView) || limits.MinFieldOfView < 1 || limits.MaxFieldOfView > 179 || limits.MinFieldOfView > limits.MaxFieldOfView)
                EditorGUILayout.HelpBox("Base FOV limits must be finite, between 1 and 179, with minimum <= maximum.", MessageType.Error);
            if (!EasyCore.Numeric.IsNonNegative(limits.MaxShadowDistance) || limits.MaxFrameRate < 1)
                EditorGUILayout.HelpBox("Set a nonnegative finite shadow limit and a positive FPS limit.", MessageType.Error);
            var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var profile in settings.Profiles ?? System.Array.Empty<GraphicsProfile>()) {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || !names.Add(profile.Name))
                    EditorGUILayout.HelpBox("Every quality profile needs a unique nonempty name and a state.", MessageType.Warning);
            }
            EditorGUILayout.HelpBox("Profiles affect graphics only. Screen changes are previewed in desktop Players. VSync overrides FPS limit. Shader stripping, baked lighting and higher-priority Volumes cannot be repaired at runtime. Verify enabled post-processing variants in a Player build.", MessageType.Info);
        }
    }
    [CustomPropertyDrawer(typeof(GraphicsProfile))]
    public sealed class GraphicsProfileDrawer : PropertyDrawer
    {
        static readonly string[] Fields = { "RenderScale", "MsaaSamples", "ShadowDistance", "FieldOfView", "CameraAntialiasing", "PostProcessing", "PostExposure", "BloomIntensity" };
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => (property.isExpanded ? Fields.Length + 2 : 1) * (EditorGUIUtility.singleLineHeight + 2);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label, true);
            if (property.isExpanded) {
                EditorGUI.indentLevel++;
                position.y += position.height + 2; EditorGUI.PropertyField(position, property.FindPropertyRelative("Name"));
                var state = property.FindPropertyRelative("State");
                foreach (var field in Fields) { position.y += position.height + 2; EditorGUI.PropertyField(position, state.FindPropertyRelative(field)); }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
