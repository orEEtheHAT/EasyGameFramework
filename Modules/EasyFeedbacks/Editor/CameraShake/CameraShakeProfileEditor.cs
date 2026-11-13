using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(CameraShakeProfile))]
public sealed class CameraShakeProfileEditor : UnityEditor.Editor
{
    private static readonly GUIContent[] SpatialModeOptions =
    {
        new GUIContent("Uniform"),
        new GUIContent("Dissipating"),
        new GUIContent("Propagating")
    };

    private const string NoisePresetPath = "Packages/com.unity.cinemachine/Presets/Noise";
    private const string BuiltInPresetRoot =
        "Packages/com.oreethehat.easy-game-framework/Modules/EasyFeedbacks/Runtime/Presets/Camera Shake/";

    private SerializedProperty impulseDefinition;
    private SerializedProperty impulseChannel;
    private SerializedProperty impulseShape;
    private SerializedProperty customImpulseShape;
    private SerializedProperty impulseDuration;
    private SerializedProperty impulseType;
    private SerializedProperty dissipationRate;
    private SerializedProperty dissipationDistance;
    private SerializedProperty propagationSpeed;

    private SerializedProperty directionalKickEnabled;
    private SerializedProperty kickVector;
    private SerializedProperty noiseShakeEnabled;
    private SerializedProperty noiseProfile;
    private SerializedProperty noisePositionAmplitude;
    private SerializedProperty noiseRotationAmplitude;
    private SerializedProperty noiseFrequency;
    private SerializedProperty randomizeNoise;

    [MenuItem("Assets/Create/EasyGameFramework/Feedbacks/Camera Shake Profile", false, 100)]
    private static void CreateCameraShakeProfile()
    {
        var profile = ScriptableObject.CreateInstance<CameraShakeProfile>();
        var serializedProfile = new SerializedObject(profile);
        serializedProfile.FindProperty("noiseProfile").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<NoiseSettings>($"{NoisePresetPath}/6D Shake.asset");
        serializedProfile.ApplyModifiedPropertiesWithoutUndo();
        ProjectWindowUtil.CreateAsset(profile, "Camera Shake Profile.asset");
    }

    private void OnEnable()
    {
        impulseDefinition = serializedObject.FindProperty("impulseDefinition");
        impulseChannel = impulseDefinition.FindPropertyRelative("ImpulseChannel");
        impulseShape = impulseDefinition.FindPropertyRelative("ImpulseShape");
        customImpulseShape = impulseDefinition.FindPropertyRelative("CustomImpulseShape");
        impulseDuration = impulseDefinition.FindPropertyRelative("ImpulseDuration");
        impulseType = impulseDefinition.FindPropertyRelative("ImpulseType");
        dissipationRate = impulseDefinition.FindPropertyRelative("DissipationRate");
        dissipationDistance = impulseDefinition.FindPropertyRelative("DissipationDistance");
        propagationSpeed = impulseDefinition.FindPropertyRelative("PropagationSpeed");

        directionalKickEnabled = serializedObject.FindProperty("directionalKickEnabled");
        kickVector = serializedObject.FindProperty("kickVector");
        noiseShakeEnabled = serializedObject.FindProperty("noiseShakeEnabled");
        noiseProfile = serializedObject.FindProperty("noiseProfile");
        noisePositionAmplitude = serializedObject.FindProperty("noisePositionAmplitude");
        noiseRotationAmplitude = serializedObject.FindProperty("noiseRotationAmplitude");
        noiseFrequency = serializedObject.FindProperty("noiseFrequency");
        randomizeNoise = serializedObject.FindProperty("randomizeNoise");
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
            DrawDeliverySettings();

            EditorGUILayout.Space(10f);
            DrawDirectionalKickSettings();

            EditorGUILayout.Space(10f);
            DrawNoiseShakeSettings();

            if (!directionalKickEnabled.boolValue && !noiseShakeEnabled.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Both shake layers are disabled, so this profile will not generate anything.",
                    MessageType.Warning);
            }
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
            "Save Camera Shake Profile",
            $"{target.name} Copy",
            "asset",
            "Choose where to save the editable profile copy.",
            "Assets");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var copy = Instantiate((CameraShakeProfile)target);
        copy.name = target.name;
        AssetDatabase.CreateAsset(copy, path);
        Selection.activeObject = copy;
    }

    private void DrawDeliverySettings()
    {
        EditorGUILayout.LabelField("Delivery", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Channel and spatial propagation shared by Directional Kick and Noise Shake.",
            MessageType.Info);
        EditorGUILayout.PropertyField(impulseChannel, new GUIContent("Impulse Channel"));

        var mode = (CinemachineImpulseDefinition.ImpulseTypes)impulseType.intValue;
        var selectedMode = mode == CinemachineImpulseDefinition.ImpulseTypes.Dissipating
            ? 1
            : mode == CinemachineImpulseDefinition.ImpulseTypes.Propagating
                ? 2
                : 0;
        selectedMode = EditorGUILayout.Popup(new GUIContent("Spatial Mode"), selectedMode, SpatialModeOptions);
        impulseType.intValue = selectedMode == 1
            ? (int)CinemachineImpulseDefinition.ImpulseTypes.Dissipating
            : selectedMode == 2
                ? (int)CinemachineImpulseDefinition.ImpulseTypes.Propagating
                : (int)CinemachineImpulseDefinition.ImpulseTypes.Uniform;

        if (selectedMode == 2)
        {
            EditorGUILayout.PropertyField(propagationSpeed, new GUIContent("Propagation Speed"));
        }

        if (selectedMode > 0)
        {
            EditorGUILayout.PropertyField(dissipationDistance, new GUIContent("Dissipation Distance"));
            dissipationRate.floatValue = EditorGUILayout.Slider(
                new GUIContent("Dissipation Rate", "Controls how quickly the shake fades with distance."),
                dissipationRate.floatValue,
                0f,
                1f);
        }
    }

    private void DrawDirectionalKickSettings()
    {
        EditorGUILayout.LabelField("Directional Kick", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            directionalKickEnabled,
            new GUIContent("Enabled", "Generate a directed positional kick."));

        if (!directionalKickEnabled.boolValue)
        {
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(
            kickVector,
            new GUIContent(
                "Kick Vector",
                "Camera-oriented vector: direction sets the kick axis and magnitude sets relative strength. Cinemachine Impulse Listener should keep Use Camera Space enabled."));

        if (kickVector.vector3Value.sqrMagnitude <= Mathf.Epsilon)
        {
            EditorGUILayout.HelpBox(
                "Kick Vector is zero, so Directional Kick will not generate an impulse.",
                MessageType.Warning);
        }

        EditorGUILayout.PropertyField(impulseShape, new GUIContent("Shape"));
        if (impulseShape.intValue == (int)CinemachineImpulseDefinition.ImpulseShapes.Custom)
        {
            EditorGUILayout.PropertyField(customImpulseShape, new GUIContent("Custom Shape"));
        }

        EditorGUILayout.PropertyField(
            impulseDuration,
            new GUIContent("Kick Duration", "Duration of Directional Kick. Noise Duration is configured per Feedback layer."));
        EditorGUI.indentLevel--;
    }

    private void DrawNoiseShakeSettings()
    {
        EditorGUILayout.LabelField("Noise Shake", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            noiseShakeEnabled,
            new GUIContent("Enabled", "Generate a profile-specific positional and rotational noise shake."));

        if (!noiseShakeEnabled.boolValue)
        {
            return;
        }

        EditorGUI.indentLevel++;
        var noiseRect = EditorGUILayout.GetControlRect();
        var menuWidth = noiseRect.height + 4f;
        var fieldRect = new Rect(noiseRect.x, noiseRect.y, noiseRect.width - menuWidth, noiseRect.height);
        var menuRect = new Rect(fieldRect.xMax, noiseRect.y - 2f, menuWidth, menuWidth);

        EditorGUI.BeginChangeCheck();
        var selectedNoiseProfile = (NoiseSettings)EditorGUI.ObjectField(
            fieldRect,
            new GUIContent("Noise Profile", "Cinemachine NoiseSettings asset used by this layer."),
            noiseProfile.objectReferenceValue,
            typeof(NoiseSettings),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            noiseProfile.objectReferenceValue = selectedNoiseProfile;
        }

        if (GUI.Button(
                menuRect,
                new GUIContent(EditorGUIUtility.IconContent("_Popup").image, "Cinemachine Noise Presets"),
                GUI.skin.label))
        {
            ShowNoisePresetMenu();
        }

        if (noiseProfile.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a Cinemachine NoiseSettings asset, for example the built-in 6D Shake preset.",
                MessageType.Warning);
        }

        EditorGUILayout.PropertyField(
            noisePositionAmplitude,
            new GUIContent("Position Amplitude", "Set to 0 for rotation-only shake in place."));
        EditorGUILayout.PropertyField(
            noiseRotationAmplitude,
            new GUIContent("Rotation Amplitude", "Multiplier for pitch, yaw and roll noise."));
        EditorGUILayout.PropertyField(noiseFrequency, new GUIContent("Frequency"));
        EditorGUILayout.PropertyField(randomizeNoise, new GUIContent("Randomize Phase"));
        EditorGUI.indentLevel--;

        EditorGUILayout.HelpBox(
            "Use Additive signal combination on Cinemachine Impulse Listener. Keep its Secondary Noise empty unless you intentionally want another global shake layer.",
            MessageType.Info);
    }

    private void ShowNoisePresetMenu()
    {
        var menu = new GenericMenu();
        var current = noiseProfile.objectReferenceValue as NoiseSettings;
        menu.AddItem(new GUIContent("None"), current == null, () => AssignNoiseProfile(null));
        menu.AddSeparator(string.Empty);

        var presets = LoadNoisePresets();
        if (presets.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("No Cinemachine presets found"));
        }
        else
        {
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var capturedPreset = preset;
                menu.AddItem(
                    new GUIContent($"Presets/{preset.name}"),
                    current == preset,
                    () => AssignNoiseProfile(capturedPreset));
            }
        }

        menu.ShowAsContext();
    }

    private static List<NoiseSettings> LoadNoisePresets()
    {
        var presets = new List<NoiseSettings>();
        var presetGuids = AssetDatabase.FindAssets(
            "t:NoiseSettings",
            new[] { NoisePresetPath });

        for (var i = 0; i < presetGuids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(presetGuids[i]);
            var preset = AssetDatabase.LoadAssetAtPath<NoiseSettings>(assetPath);
            if (preset != null)
            {
                presets.Add(preset);
            }
        }

        presets.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        return presets;
    }

    private void AssignNoiseProfile(NoiseSettings preset)
    {
        serializedObject.Update();
        noiseProfile.objectReferenceValue = preset;
        serializedObject.ApplyModifiedProperties();
        Repaint();
    }
}
}
