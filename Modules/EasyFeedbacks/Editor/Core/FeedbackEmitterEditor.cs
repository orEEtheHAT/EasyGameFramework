using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(FeedbackEmitter))]
public sealed class FeedbackEmitterEditor : UnityEditor.Editor
{
    private SerializedProperty preset;
    private SerializedProperty contextProvider;
    private SerializedProperty positioning;
    private SerializedProperty follow;
    private SerializedProperty intensity;
    private SerializedProperty audioMultiplier;
    private SerializedProperty visualScale;
    private SerializedProperty autoPlay;
    private SerializedProperty stopOnDisable;
    private FeedbackHandle inspectorPreviewHandle;

    private void OnEnable()
    {
        preset = serializedObject.FindProperty("preset");
        contextProvider = serializedObject.FindProperty("contextProvider");
        positioning = serializedObject.FindProperty("positioning");
        follow = serializedObject.FindProperty("follow");
        intensity = serializedObject.FindProperty("intensity");
        audioMultiplier = serializedObject.FindProperty("audioMultiplier");
        visualScale = serializedObject.FindProperty("visualScale");
        autoPlay = serializedObject.FindProperty("autoPlay");
        stopOnDisable = serializedObject.FindProperty("stopOnDisable");
        EditorApplication.update -= MonitorInspectorPreview;
        EditorApplication.update += MonitorInspectorPreview;
        EditorApplication.focusChanged -= OnEditorFocusChanged;
        EditorApplication.focusChanged += OnEditorFocusChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= MonitorInspectorPreview;
        EditorApplication.focusChanged -= OnEditorFocusChanged;
        StopInspectorPreview(true);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(preset);
        if (preset.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Assign a FeedbackPreset before playing this emitter.", MessageType.Warning);
        }

        EasyGameFramework.Editor.FeedbackSetup.DrawRequirements(preset.objectReferenceValue as FeedbackPreset);

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Common", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField(new GUIContent("Source"), "This Transform");
        }

        EditorGUILayout.PropertyField(
            positioning,
            new GUIContent("Positioning", "Default mode used by preset layers whose Positioning is Inherited."));
        EditorGUILayout.PropertyField(
            follow,
            new GUIContent(
                "Follow Transform",
                "Optional target used by Follow Transform layers. The emitter transform is used when this is empty."));
        EditorGUILayout.PropertyField(intensity);
        EditorGUILayout.PropertyField(contextProvider);
        if (contextProvider.objectReferenceValue != null
            && !(contextProvider.objectReferenceValue is IFeedbackContextProvider))
        {
            EditorGUILayout.HelpBox(
                "Context Provider must implement IFeedbackContextProvider. Assign the owning player's provider component or leave it empty.",
                MessageType.Error);
        }
        else if (contextProvider.objectReferenceValue is MonoBehaviour provider && !provider.isActiveAndEnabled)
        {
            EditorGUILayout.HelpBox(
                "This Context Provider is disabled and will not be used. Enable it to preserve the owning player's gamepad selection.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            audioMultiplier,
            new GUIContent("Audio Multiplier", "Additional per-emitter multiplier applied only to audio volume."));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            visualScale,
            new GUIContent("Visual Scale", "Per-emitter scale multiplier applied only to Particle System and VFX entries."));

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Lifecycle", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            autoPlay,
            new GUIContent("Auto Play", "Manual, once on Start, or every time the emitter is enabled."));
        EditorGUILayout.PropertyField(
            stopOnDisable,
            new GUIContent("Stop On Disable", "Stops the most recent audio, visual, camera shake, and gamepad vibration launch when the emitter is disabled."));

        var autoPlayMode = (FeedbackAutoPlayMode)autoPlay.enumValueIndex;
        if (autoPlayMode == FeedbackAutoPlayMode.OnStart && stopOnDisable.boolValue)
        {
            EditorGUILayout.HelpBox(
                "On Start plays only once. After Stop On Disable, re-enabling this emitter will not play it again.",
                MessageType.Info);
        }
        else if (autoPlayMode == FeedbackAutoPlayMode.OnEnable && !stopOnDisable.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Without Stop On Disable, re-enabling can launch another feedback while the previous launch is still active.",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(4f);
        var emitter = (FeedbackEmitter)target;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play"))
                {
                    StopInspectorPreview(true);
                    AudioManager.BeginEditorPreviewPlayback();
                    GamepadVibrationManager.BeginEditorPreviewPlayback();
                    try
                    {
                        inspectorPreviewHandle = emitter.PlayFeedback();
                    }
                    finally
                    {
                        GamepadVibrationManager.EndEditorPreviewPlayback();
                        AudioManager.EndEditorPreviewPlayback();
                    }
                }

                var canStop = emitter.LastHandle.IsValid || inspectorPreviewHandle.IsValid;
                using (new EditorGUI.DisabledScope(!canStop))
                {
                    if (GUILayout.Button("Stop"))
                    {
                        emitter.Stop();
                        StopInspectorPreview(false);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!emitter.LastHandle.CanPause))
            {
                if (GUILayout.Button("Pause"))
                {
                    emitter.Pause();
                }

                if (GUILayout.Button("Resume"))
                {
                    emitter.Resume();
                }
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Runtime controls are available in Play Mode.", MessageType.Info);
        }
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    private void MonitorInspectorPreview()
    {
        if (!inspectorPreviewHandle.IsValid)
        {
            inspectorPreviewHandle = default;
            return;
        }

        if (!Application.isPlaying)
        {
            StopInspectorPreview(true);
        }
    }

    private void OnEditorFocusChanged(bool hasFocus)
    {
        if (!hasFocus)
        {
            StopInspectorPreview(true);
        }
    }

    private void StopInspectorPreview(bool immediate)
    {
        if (!inspectorPreviewHandle.IsValid)
        {
            inspectorPreviewHandle = default;
            return;
        }

        if (immediate)
        {
            inspectorPreviewHandle.Stop(0f);
        }
        else
        {
            inspectorPreviewHandle.Stop();
        }

        inspectorPreviewHandle = default;
    }
}
}
