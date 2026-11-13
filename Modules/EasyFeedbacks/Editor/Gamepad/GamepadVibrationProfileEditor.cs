using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(GamepadVibrationProfile))]
public sealed class GamepadVibrationProfileEditor : UnityEditor.Editor
{
    private const string BuiltInPresetRoot =
        "Packages/com.oreethehat.easy-game-framework/Modules/EasyFeedbacks/Runtime/Presets/Gamepad Vibration/";

    private SerializedProperty lowFrequencyMotor;
    private SerializedProperty highFrequencyMotor;
    private SerializedProperty lowFrequencyPattern;
    private SerializedProperty highFrequencyPattern;
    private SerializedProperty loopCycleDuration;

    [MenuItem("Assets/Create/EasyGameFramework/Feedbacks/Gamepad Vibration Profile", false, 101)]
    private static void CreateGamepadVibrationProfile()
    {
        var profile = CreateInstance<GamepadVibrationProfile>();
        ProjectWindowUtil.CreateAsset(profile, "Gamepad Vibration Profile.asset");
    }

    private void OnEnable()
    {
        lowFrequencyMotor = serializedObject.FindProperty("lowFrequencyMotor");
        highFrequencyMotor = serializedObject.FindProperty("highFrequencyMotor");
        lowFrequencyPattern = serializedObject.FindProperty("lowFrequencyPattern");
        highFrequencyPattern = serializedObject.FindProperty("highFrequencyPattern");
        loopCycleDuration = serializedObject.FindProperty("loopCycleDuration");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var isBuiltIn = IsBuiltInPreset();
        if (isBuiltIn)
        {
            EditorGUILayout.HelpBox(
                "This built-in profile is read-only. Create an editable copy in Assets to customize it.",
                MessageType.Info);
            if (GUILayout.Button("Create Editable Copy"))
            {
                CreateEditableCopy();
            }

            EditorGUILayout.Space(6f);
        }

        using (new EditorGUI.DisabledScope(isBuiltIn))
        {
            EditorGUILayout.LabelField("Motor Maximums", EditorStyles.boldLabel);
            EditorGUILayout.Slider(
                lowFrequencyMotor,
                0f,
                1f,
                new GUIContent("Low-Frequency Motor", "Maximum speed of the heavy, usually left, motor. This does not set a frequency."));
            EditorGUILayout.Slider(
                highFrequencyMotor,
                0f,
                1f,
                new GUIContent("High-Frequency Motor", "Maximum speed of the sharp, usually right, motor. This does not set a frequency."));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Motor Pattern", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Curves use normalized one-shot time from 0 to 1. A looping layer repeats them using Loop Cycle Duration.",
                MessageType.Info);
            EditorGUILayout.PropertyField(
                lowFrequencyPattern,
                new GUIContent("Low Pattern", "Temporal multiplier for the low-frequency motor."));
            EditorGUILayout.PropertyField(
                highFrequencyPattern,
                new GUIContent("High Pattern", "Temporal multiplier for the high-frequency motor."));
            EditorGUILayout.PropertyField(
                loopCycleDuration,
                new GUIContent("Loop Cycle Duration", "Seconds per pattern repetition while a feedback layer is looping."));
        }

        if (lowFrequencyMotor.floatValue <= 0f && highFrequencyMotor.floatValue <= 0f)
        {
            EditorGUILayout.HelpBox(
                "Both motors are zero, so this profile will not produce vibration.",
                MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private bool IsBuiltInPreset()
    {
        return AssetDatabase.GetAssetPath(target)
            .StartsWith(BuiltInPresetRoot, System.StringComparison.Ordinal);
    }

    private void CreateEditableCopy()
    {
        var path = EditorUtility.SaveFilePanelInProject(
            "Save Gamepad Vibration Profile",
            $"{target.name} Copy",
            "asset",
            "Choose where to save the editable profile copy.",
            "Assets");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var copy = Instantiate((GamepadVibrationProfile)target);
        copy.name = target.name;
        AssetDatabase.CreateAsset(copy, path);
        Selection.activeObject = copy;
    }
}
}
