using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using Unity.Cinemachine;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(FeedbackPreset))]
public sealed class FeedbackPresetEditor : UnityEditor.Editor
{
    private const float VerticalSpacing = 2f;
    private const float HelpBoxHeight = 36f;

    private SerializedProperty audioEntries;
    private SerializedProperty visualEntries;
    private SerializedProperty cameraShakeEntries;
    private SerializedProperty gamepadVibrationEntries;
    private ReorderableList audioList;
    private readonly Dictionary<string, ReorderableList> audioClipLists =
        new Dictionary<string, ReorderableList>();
    private VisualFeedbackListDrawer visualList;
    private CameraShakeFeedbackListDrawer cameraShakeList;
    private GamepadVibrationFeedbackListDrawer gamepadVibrationList;

    private void OnEnable()
    {
        audioClipLists.Clear();
        audioEntries = serializedObject.FindProperty("audioEntries");
        visualEntries = serializedObject.FindProperty("visualEntries");
        cameraShakeEntries = serializedObject.FindProperty("cameraShakeEntries");
        gamepadVibrationEntries = serializedObject.FindProperty("gamepadVibrationEntries");
        audioList = new ReorderableList(serializedObject, audioEntries, true, true, true, true)
        {
            drawHeaderCallback = DrawHeader,
            drawElementCallback = DrawElement,
            elementHeightCallback = GetElementHeight,
            onAddDropdownCallback = ShowAddMenu,
            onRemoveCallback = list => ReorderableList.defaultBehaviours.DoRemoveButton(list)
        };

        visualList = new VisualFeedbackListDrawer(serializedObject, visualEntries);
        cameraShakeList = new CameraShakeFeedbackListDrawer(serializedObject, cameraShakeEntries);
        gamepadVibrationList = new GamepadVibrationFeedbackListDrawer(serializedObject, gamepadVibrationEntries);
    }

    private void OnDisable()
    {
        FeedbackPresetPreview.StopImmediate();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Every enabled audio entry is launched at the same time. Inspector preview is centered (2D); test spatial positioning through a Feedback Emitter in Play Mode.",
            MessageType.Info);

        audioList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Play Audio Preview"))
            {
                FeedbackPresetPreview.Play((FeedbackPreset)target);
            }

            using (new EditorGUI.DisabledScope(!FeedbackPresetPreview.IsPlaying))
            {
                if (GUILayout.Button("Stop Audio Preview"))
                {
                    FeedbackPresetPreview.Stop();
                }
            }
        }

        EditorGUILayout.Space(10f);
        serializedObject.Update();
        EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Particle System and VFX prefab layers launch together with the Audio section.",
            MessageType.Info);
        visualList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        serializedObject.Update();
        EditorGUILayout.LabelField("Camera Shake", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each layer uses a reusable Camera Shake Profile. The + menu also applies that built-in profile's recommended duration, fades, and loop mode. Loop keeps Noise Shake active until the returned FeedbackHandle is stopped.",
            MessageType.Info);
        cameraShakeList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        if (HasEnabledCameraShakeEntry() && !HasCameraShakeManager())
        {
            EditorGUILayout.HelpBox(
                "No Camera Shake Manager was found in the loaded scenes. Add one to the persistent bootstrap scene.",
                MessageType.Warning);
        }

        if (HasEnabledCameraShakeEntry() && !HasImpulseListener())
        {
            EditorGUILayout.HelpBox(
                "No Cinemachine Impulse Listener was found in the loaded scenes. Add the extension to every Cinemachine Camera that should react to camera shake.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(10f);
        serializedObject.Update();
        EditorGUILayout.LabelField("Gamepad Vibration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each layer uses a reusable Vibration Profile with separate motor patterns. The + menu also applies recommended duration, fades, and loop mode. Playback targets FeedbackContext.TargetGamepad, or the current Gamepad when none is supplied.",
            MessageType.Info);
        gamepadVibrationList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();

        if (HasEnabledGamepadVibrationEntry() && !HasGamepadVibrationManager())
        {
            EditorGUILayout.HelpBox(
                "No Gamepad Vibration Manager was found in the loaded scenes. Add one to the persistent bootstrap scene.",
                MessageType.Warning);
        }
    }

    public override bool RequiresConstantRepaint()
    {
        return FeedbackPresetPreview.IsPlaying;
    }

    private static bool HasImpulseListener()
    {
        return Object.FindObjectsByType<CinemachineImpulseListener>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length > 0;
    }

    private static bool HasCameraShakeManager()
    {
        var managers = Object.FindObjectsByType<CameraShakeManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (var i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null && managers[i].isActiveAndEnabled)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasEnabledCameraShakeEntry()
    {
        for (var i = 0; i < cameraShakeEntries.arraySize; i++)
        {
            var entry = cameraShakeEntries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("isEnabled").boolValue
                && entry.FindPropertyRelative("profile").objectReferenceValue != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGamepadVibrationManager()
    {
        return Object.FindObjectsByType<GamepadVibrationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length > 0;
    }

    private bool HasEnabledGamepadVibrationEntry()
    {
        for (var i = 0; i < gamepadVibrationEntries.arraySize; i++)
        {
            if (gamepadVibrationEntries.GetArrayElementAtIndex(i)
                .FindPropertyRelative("isEnabled").boolValue)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawHeader(Rect rect)
    {
        EditorGUI.LabelField(rect, "Audio Layers");
    }

    private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < 0 || index >= audioEntries.arraySize)
        {
            return;
        }

        var element = audioEntries.GetArrayElementAtIndex(index);
        var isEnabled = element.FindPropertyRelative("isEnabled");
        var clips = element.FindPropertyRelative("clips");
        var mixer = element.FindPropertyRelative("mixerGroup");
        var minVolume = element.FindPropertyRelative("minVolume");
        var maxVolume = element.FindPropertyRelative("maxVolume");
        var minPitch = element.FindPropertyRelative("minPitch");
        var maxPitch = element.FindPropertyRelative("maxPitch");
        var loop = element.FindPropertyRelative("loop");
        var fadeIn = element.FindPropertyRelative("fadeInDuration");
        var fadeOut = element.FindPropertyRelative("fadeOutDuration");
        var spatialBlend = element.FindPropertyRelative("spatialBlend");
        var minDistance = element.FindPropertyRelative("minDistance");
        var maxDistance = element.FindPropertyRelative("maxDistance");
        var rolloff = element.FindPropertyRelative("rolloffMode");
        var doppler = element.FindPropertyRelative("dopplerLevel");
        var positionMode = element.FindPropertyRelative("positionMode");

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var y = rect.y + VerticalSpacing;
        var enabledRect = new Rect(rect.x, y, 18f, lineHeight);
        var headerRect = new Rect(rect.x + 32f, y, rect.width - 108f, lineHeight);
        var clipSummary = GetClipSummary(clips);
        isEnabled.boolValue = EditorGUI.Toggle(
            enabledRect,
            new GUIContent(string.Empty, "Enable or disable this audio layer."),
            isEnabled.boolValue);
        var headerName = isEnabled.boolValue ? clipSummary : $"{clipSummary} (Disabled)";
        element.isExpanded = EditorGUI.Foldout(headerRect, element.isExpanded, $"{index + 1}. {headerName}", true);

        if (GUI.Button(new Rect(rect.xMax - 72f, y, 72f, lineHeight), "Duplicate", EditorStyles.miniButton))
        {
            DuplicateElement(index);
            return;
        }

        if (!element.isExpanded)
        {
            return;
        }

        y += lineHeight + VerticalSpacing;
        var contentRect = new Rect(rect.x + 12f, y, rect.width - 12f, lineHeight);

        DrawSectionLabel(ref contentRect, "Basic");
        DrawAudioClips(ref contentRect, clips);

        if (!HasValidClip(clips))
        {
            DrawHelpBox(ref contentRect, "Add at least one AudioClip.", MessageType.Warning);
        }

        DrawProperty(ref contentRect, mixer, new GUIContent("Mixer Group"));

        DrawSectionLabel(ref contentRect, "Randomization");
        DrawOrderedSliders(
            ref contentRect,
            minVolume,
            maxVolume,
            0f,
            1f,
            new GUIContent("Min Volume", "Minimum volume selected for each playback."),
            new GUIContent("Max Volume", "Maximum volume selected for each playback."));
        DrawOrderedSliders(
            ref contentRect,
            minPitch,
            maxPitch,
            0.1f,
            3f,
            new GUIContent("Min Pitch", "Minimum AudioSource pitch selected for each playback."),
            new GUIContent("Max Pitch", "Maximum AudioSource pitch selected for each playback."));

        DrawSectionLabel(ref contentRect, "Playback");
        DrawProperty(ref contentRect, loop, new GUIContent("Loop"));
        DrawProperty(ref contentRect, fadeIn, new GUIContent("Fade In", "Fade-in duration in seconds."));
        DrawProperty(
            ref contentRect,
            fadeOut,
            new GUIContent(
                "Fade Out on Stop",
                "Fade-out duration after an explicit Stop command. Natural one-shot completion is not faded."));

        DrawSectionLabel(ref contentRect, "Spatial");
        EditorGUI.Slider(contentRect, spatialBlend, 0f, 1f, new GUIContent("Spatial Blend", "0 = 2D, 1 = 3D"));
        NextLine(ref contentRect);

        if (spatialBlend.floatValue > 0f)
        {
            DrawPositioningOverride(ref contentRect, positionMode);
            DrawProperty(ref contentRect, minDistance, new GUIContent("Min Distance"));
            DrawProperty(ref contentRect, maxDistance, new GUIContent("Max Distance"));

            if (maxDistance.floatValue < minDistance.floatValue)
            {
                DrawHelpBox(ref contentRect, "Max Distance must be greater than or equal to Min Distance.", MessageType.Warning);
            }

            DrawProperty(ref contentRect, rolloff, new GUIContent("Rolloff Mode"));
            DrawProperty(ref contentRect, doppler, new GUIContent("Doppler Level"));
        }
    }

    private float GetElementHeight(int index)
    {
        var lineHeight = EditorGUIUtility.singleLineHeight;
        if (index < 0 || index >= audioEntries.arraySize)
        {
            return lineHeight + VerticalSpacing * 2f;
        }

        var element = audioEntries.GetArrayElementAtIndex(index);
        var height = lineHeight + VerticalSpacing * 2f;
        if (!element.isExpanded)
        {
            return height;
        }

        var clips = element.FindPropertyRelative("clips");
        // Basic, mixer, randomization, playback and spatial sections.
        height += 13f * (lineHeight + VerticalSpacing);
        height += GetAudioClipList(clips).GetHeight() + VerticalSpacing;

        if (!HasValidClip(clips))
        {
            height += HelpBoxHeight + VerticalSpacing;
        }

        if (element.FindPropertyRelative("spatialBlend").floatValue > 0f)
        {
            // Positioning, min/max distance, rolloff and doppler.
            height += 5f * (lineHeight + VerticalSpacing);

            if (element.FindPropertyRelative("maxDistance").floatValue
                < element.FindPropertyRelative("minDistance").floatValue)
            {
                height += HelpBoxHeight + VerticalSpacing;
            }
        }

        return height + VerticalSpacing;
    }

    private void DrawAudioClips(ref Rect rect, SerializedProperty clips)
    {
        var clipList = GetAudioClipList(clips);
        var listHeight = clipList.GetHeight();
        clipList.DoList(new Rect(rect.x, rect.y, rect.width, listHeight));
        rect.y += listHeight + VerticalSpacing;
    }

    private ReorderableList GetAudioClipList(SerializedProperty clips)
    {
        var propertyPath = clips.propertyPath;
        if (audioClipLists.TryGetValue(propertyPath, out var existingList))
        {
            return existingList;
        }

        var clipList = new ReorderableList(serializedObject, clips, true, true, true, true)
        {
            drawHeaderCallback = rect => DrawAudioClipListHeader(rect, clips),
            drawElementCallback = (rect, index, isActive, isFocused) =>
                DrawAudioClipElement(rect, clips, index),
            elementHeight = EditorGUIUtility.singleLineHeight + 4f
        };

        clipList.onAddCallback = list =>
        {
            var newIndex = clips.arraySize;
            clips.InsertArrayElementAtIndex(newIndex);
            clips.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
            list.index = newIndex;
        };
        clipList.onRemoveCallback = list =>
        {
            if (list.index < 0 || list.index >= clips.arraySize)
            {
                return;
            }

            DeleteArrayElement(clips, list.index);
            list.index = Mathf.Min(list.index, clips.arraySize - 1);
        };

        audioClipLists.Add(propertyPath, clipList);
        return clipList;
    }

    private static void DrawAudioClipListHeader(Rect rect, SerializedProperty clips)
    {
        var currentEvent = Event.current;
        var acceptsDrop = rect.Contains(currentEvent.mousePosition) && HasDraggedAudioClips();
        if (acceptsDrop && currentEvent.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 0.9f, 0.2f));
        }

        EditorGUI.LabelField(rect, $"Audio Clips ({CountValidClips(clips)}) — drop AudioClips here");
        HandleAudioClipDrop(rect, clips);
    }

    private static void DrawAudioClipElement(Rect rect, SerializedProperty clips, int index)
    {
        if (index < 0 || index >= clips.arraySize)
        {
            return;
        }

        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(
            rect,
            clips.GetArrayElementAtIndex(index),
            new GUIContent($"Clip {index + 1}"));
    }

    private static void HandleAudioClipDrop(Rect rect, SerializedProperty clips)
    {
        var currentEvent = Event.current;
        if (!rect.Contains(currentEvent.mousePosition) || !HasDraggedAudioClips())
        {
            return;
        }

        if (currentEvent.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            currentEvent.Use();
            return;
        }

        if (currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        DragAndDrop.AcceptDrag();
        var draggedObjects = DragAndDrop.objectReferences;
        for (var i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is not AudioClip clip)
            {
                continue;
            }

            var newIndex = clips.arraySize;
            clips.InsertArrayElementAtIndex(newIndex);
            clips.GetArrayElementAtIndex(newIndex).objectReferenceValue = clip;
        }

        GUI.changed = true;
        currentEvent.Use();
    }

    private static bool HasDraggedAudioClips()
    {
        var draggedObjects = DragAndDrop.objectReferences;
        for (var i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is AudioClip)
            {
                return true;
            }
        }

        return false;
    }

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        var previousSize = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == previousSize)
        {
            array.DeleteArrayElementAtIndex(index);
        }
    }

    private static string GetClipSummary(SerializedProperty clips)
    {
        var validClipCount = CountValidClips(clips);
        if (validClipCount == 0)
        {
            return "Missing Audio Clips";
        }

        if (validClipCount == 1)
        {
            for (var i = 0; i < clips.arraySize; i++)
            {
                var clip = clips.GetArrayElementAtIndex(i).objectReferenceValue;
                if (clip != null)
                {
                    return clip.name;
                }
            }
        }

        return $"{validClipCount} Audio Clips";
    }

    private static bool HasValidClip(SerializedProperty clips)
    {
        return CountValidClips(clips) > 0;
    }

    private static int CountValidClips(SerializedProperty clips)
    {
        var count = 0;
        for (var i = 0; i < clips.arraySize; i++)
        {
            if (clips.GetArrayElementAtIndex(i).objectReferenceValue != null)
            {
                count++;
            }
        }

        return count;
    }

    private static void DrawOrderedSliders(
        ref Rect rect,
        SerializedProperty minProperty,
        SerializedProperty maxProperty,
        float allowedMin,
        float allowedMax,
        GUIContent minLabel,
        GUIContent maxLabel)
    {
        var minValue = Mathf.Clamp(minProperty.floatValue, allowedMin, allowedMax);
        var maxValue = Mathf.Clamp(maxProperty.floatValue, allowedMin, allowedMax);
        if (minValue > maxValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }

        minProperty.floatValue = minValue;
        maxProperty.floatValue = maxValue;

        EditorGUI.BeginChangeCheck();
        EditorGUI.Slider(rect, minProperty, allowedMin, allowedMax, minLabel);
        if (EditorGUI.EndChangeCheck() && minProperty.floatValue > maxProperty.floatValue)
        {
            maxProperty.floatValue = minProperty.floatValue;
        }

        NextLine(ref rect);

        EditorGUI.BeginChangeCheck();
        EditorGUI.Slider(rect, maxProperty, allowedMin, allowedMax, maxLabel);
        if (EditorGUI.EndChangeCheck() && maxProperty.floatValue < minProperty.floatValue)
        {
            minProperty.floatValue = maxProperty.floatValue;
        }

        NextLine(ref rect);
    }

    private void ShowAddMenu(Rect buttonRect, ReorderableList list)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Empty"), false, AddEmptyElement);

        if (list.index >= 0 && list.index < audioEntries.arraySize)
        {
            var selectedIndex = list.index;
            menu.AddItem(new GUIContent("Duplicate Selected"), false, () => DuplicateElement(selectedIndex));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Duplicate Selected"));
        }

        menu.DropDown(buttonRect);
    }

    private void AddEmptyElement()
    {
        serializedObject.Update();
        var index = audioEntries.arraySize;
        audioEntries.arraySize++;
        var element = audioEntries.GetArrayElementAtIndex(index);

        element.FindPropertyRelative("isEnabled").boolValue = true;
        element.FindPropertyRelative("clips").arraySize = 0;
        element.FindPropertyRelative("mixerGroup").objectReferenceValue = null;
        element.FindPropertyRelative("minVolume").floatValue = 1f;
        element.FindPropertyRelative("maxVolume").floatValue = 1f;
        element.FindPropertyRelative("minPitch").floatValue = 1f;
        element.FindPropertyRelative("maxPitch").floatValue = 1f;
        element.FindPropertyRelative("loop").boolValue = false;
        element.FindPropertyRelative("fadeInDuration").floatValue = 0f;
        element.FindPropertyRelative("fadeOutDuration").floatValue = 0f;
        element.FindPropertyRelative("spatialBlend").floatValue = 0f;
        element.FindPropertyRelative("minDistance").floatValue = 1f;
        element.FindPropertyRelative("maxDistance").floatValue = 500f;
        element.FindPropertyRelative("rolloffMode").enumValueIndex = (int)AudioRolloffMode.Logarithmic;
        element.FindPropertyRelative("dopplerLevel").floatValue = 1f;
        element.FindPropertyRelative("positionMode").intValue = (int)FeedbackPositionOverride.Inherited;
        element.isExpanded = true;

        serializedObject.ApplyModifiedProperties();
        audioList.index = index;
    }

    private void DuplicateElement(int index)
    {
        serializedObject.Update();
        var newElementIndex = audioEntries.arraySize;
        audioEntries.arraySize++;
        CopyEntry(
            audioEntries.GetArrayElementAtIndex(index),
            audioEntries.GetArrayElementAtIndex(newElementIndex));

        var duplicateIndex = index + 1;
        audioEntries.MoveArrayElement(newElementIndex, duplicateIndex);
        audioEntries.GetArrayElementAtIndex(duplicateIndex).isExpanded = true;
        serializedObject.ApplyModifiedProperties();
        audioList.index = duplicateIndex;
    }

    private static void CopyEntry(SerializedProperty source, SerializedProperty destination)
    {
        CopyBool(source, destination, "isEnabled");
        CopyObjectReferenceArray(source, destination, "clips");
        CopyObjectReference(source, destination, "mixerGroup");
        CopyFloat(source, destination, "minVolume");
        CopyFloat(source, destination, "maxVolume");
        CopyFloat(source, destination, "minPitch");
        CopyFloat(source, destination, "maxPitch");
        CopyBool(source, destination, "loop");
        CopyFloat(source, destination, "fadeInDuration");
        CopyFloat(source, destination, "fadeOutDuration");
        CopyFloat(source, destination, "spatialBlend");
        CopyFloat(source, destination, "minDistance");
        CopyFloat(source, destination, "maxDistance");
        CopyEnum(source, destination, "rolloffMode");
        CopyFloat(source, destination, "dopplerLevel");
        CopyEnum(source, destination, "positionMode");
    }

    private static void CopyObjectReferenceArray(
        SerializedProperty source,
        SerializedProperty destination,
        string propertyName)
    {
        var sourceArray = source.FindPropertyRelative(propertyName);
        var destinationArray = destination.FindPropertyRelative(propertyName);
        destinationArray.arraySize = sourceArray.arraySize;
        for (var i = 0; i < sourceArray.arraySize; i++)
        {
            destinationArray.GetArrayElementAtIndex(i).objectReferenceValue =
                sourceArray.GetArrayElementAtIndex(i).objectReferenceValue;
        }
    }

    private static void CopyObjectReference(SerializedProperty source, SerializedProperty destination, string propertyName)
    {
        destination.FindPropertyRelative(propertyName).objectReferenceValue =
            source.FindPropertyRelative(propertyName).objectReferenceValue;
    }

    private static void CopyFloat(SerializedProperty source, SerializedProperty destination, string propertyName)
    {
        destination.FindPropertyRelative(propertyName).floatValue = source.FindPropertyRelative(propertyName).floatValue;
    }

    private static void CopyBool(SerializedProperty source, SerializedProperty destination, string propertyName)
    {
        destination.FindPropertyRelative(propertyName).boolValue = source.FindPropertyRelative(propertyName).boolValue;
    }

    private static void CopyEnum(SerializedProperty source, SerializedProperty destination, string propertyName)
    {
        destination.FindPropertyRelative(propertyName).enumValueIndex = source.FindPropertyRelative(propertyName).enumValueIndex;
    }

    private static void DrawSectionLabel(ref Rect rect, string label)
    {
        EditorGUI.LabelField(rect, label, EditorStyles.miniBoldLabel);
        NextLine(ref rect);
    }

    private static void DrawProperty(ref Rect rect, SerializedProperty property, GUIContent label)
    {
        EditorGUI.PropertyField(rect, property, label);
        NextLine(ref rect);
    }

    private static void DrawPositioningOverride(ref Rect rect, SerializedProperty property)
    {
        var selectedIndex = property.intValue == (int)FeedbackPositionOverride.WorldPosition
            ? 1
            : property.intValue == (int)FeedbackPositionOverride.FollowTransform
                ? 2
                : 0;

        selectedIndex = EditorGUI.Popup(
            rect,
            new GUIContent("Positioning", "Use the emitter default, or force a mode for this layer."),
            selectedIndex,
            new[]
            {
                new GUIContent("Inherited"),
                new GUIContent("World Position"),
                new GUIContent("Follow Transform")
            });

        property.intValue = selectedIndex == 1
            ? (int)FeedbackPositionOverride.WorldPosition
            : selectedIndex == 2
                ? (int)FeedbackPositionOverride.FollowTransform
                : (int)FeedbackPositionOverride.Inherited;
        NextLine(ref rect);
    }

    private static void DrawHelpBox(ref Rect rect, string message, MessageType type)
    {
        var helpRect = new Rect(rect.x, rect.y, rect.width, HelpBoxHeight);
        EditorGUI.HelpBox(helpRect, message, type);
        rect.y += HelpBoxHeight + VerticalSpacing;
    }

    private static void NextLine(ref Rect rect)
    {
        rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
    }
}
}
