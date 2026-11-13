using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

internal sealed class GamepadVibrationFeedbackListDrawer
{
    private const float Spacing = 2f;
    private const float HelpHeight = 36f;
    private const string BuiltInPresetPath =
        "Packages/com.oreethehat.easy-game-framework/Modules/EasyFeedbacks/Runtime/Presets/Gamepad Vibration";

    private readonly SerializedObject serializedObject;
    private readonly SerializedProperty entries;
    private readonly ReorderableList list;

    public GamepadVibrationFeedbackListDrawer(SerializedObject serializedObject, SerializedProperty entries)
    {
        this.serializedObject = serializedObject;
        this.entries = entries;

        list = new ReorderableList(serializedObject, entries, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Gamepad Vibration Layers"),
            drawElementCallback = DrawElement,
            elementHeightCallback = GetElementHeight,
            onAddDropdownCallback = ShowAddMenu,
            onRemoveCallback = targetList => ReorderableList.defaultBehaviours.DoRemoveButton(targetList)
        };
    }

    public void DoLayoutList()
    {
        list.DoLayoutList();
    }

    private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < 0 || index >= entries.arraySize)
        {
            return;
        }

        var element = entries.GetArrayElementAtIndex(index);
        var isEnabled = element.FindPropertyRelative("isEnabled");
        var profile = element.FindPropertyRelative("profile");
        var strengthMultiplier = element.FindPropertyRelative("strengthMultiplier");
        var loop = element.FindPropertyRelative("loop");
        var duration = element.FindPropertyRelative("duration");
        var fadeIn = element.FindPropertyRelative("fadeIn");
        var fadeOut = element.FindPropertyRelative("fadeOut");

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var y = rect.y + Spacing;
        var enabledRect = new Rect(rect.x, y, 18f, lineHeight);
        var headerRect = new Rect(rect.x + 32f, y, rect.width - 108f, lineHeight);
        var profileName = profile.objectReferenceValue != null
            ? profile.objectReferenceValue.name
            : "Missing Vibration Profile";

        isEnabled.boolValue = EditorGUI.Toggle(
            enabledRect,
            new GUIContent(string.Empty, "Enable or disable this gamepad vibration layer."),
            isEnabled.boolValue);

        var headerName = isEnabled.boolValue ? profileName : $"{profileName} (Disabled)";
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

        var contentRect = new Rect(rect.x + 12f, y + lineHeight + Spacing, rect.width - 12f, lineHeight);

        DrawSectionLabel(ref contentRect, "Source");
        DrawProfileSelector(ref contentRect, profile);

        if (profile.objectReferenceValue == null)
        {
            DrawHelpBox(ref contentRect, "Assign a Gamepad Vibration Profile.", MessageType.Warning);
        }
        else if (profile.objectReferenceValue is GamepadVibrationProfile vibrationProfile
                 && !vibrationProfile.HasOutput)
        {
            DrawHelpBox(ref contentRect, "The selected profile has both motor strengths set to zero.", MessageType.Warning);
        }

        DrawSectionLabel(ref contentRect, "Invocation");
        DrawProperty(
            ref contentRect,
            strengthMultiplier,
            new GUIContent(
                "Strength Multiplier",
                "Multiplied by the common feedback Intensity. Values above 1 amplify the profile up to the motor limit."));

        DrawSectionLabel(ref contentRect, "Playback");
        DrawProperty(
            ref contentRect,
            loop,
            new GUIContent("Loop", "Keeps vibrating until FeedbackHandle.Stop is called."));
        using (new EditorGUI.DisabledScope(loop.boolValue))
        {
            DrawProperty(
                ref contentRect,
                duration,
                new GUIContent("Duration", "Total duration of a one-shot vibration in seconds."));
        }
        DrawProperty(ref contentRect, fadeIn, new GUIContent("Fade In", "Seconds used to fade the vibration in."));
        DrawProperty(
            ref contentRect,
            fadeOut,
            new GUIContent(
                "Fade Out",
                "Natural ending fade for a one-shot, and fade duration after Stop for any vibration."));

        if (!loop.boolValue && duration.floatValue <= 0f)
        {
            DrawHelpBox(ref contentRect, "A one-shot vibration needs a Duration greater than zero.", MessageType.Warning);
        }
        else if (!loop.boolValue && fadeIn.floatValue + fadeOut.floatValue > duration.floatValue)
        {
            DrawHelpBox(
                ref contentRect,
                "Fade In and Fade Out together exceed Duration. Runtime values will be scaled down proportionally.",
                MessageType.Warning);
        }
        else if (loop.boolValue)
        {
            DrawHelpBox(
                ref contentRect,
                "Loop ignores Duration and remains active until the feedback handle is stopped.",
                MessageType.Info);
        }
    }

    private float GetElementHeight(int index)
    {
        var line = EditorGUIUtility.singleLineHeight;
        var height = line + Spacing * 2f;
        if (index < 0 || index >= entries.arraySize)
        {
            return height;
        }

        var element = entries.GetArrayElementAtIndex(index);
        if (!element.isExpanded)
        {
            return height;
        }

        // Source (2), Invocation (2), Playback (5).
        height += 9f * (line + Spacing);

        var profile = element.FindPropertyRelative("profile").objectReferenceValue as GamepadVibrationProfile;
        if (profile == null || !profile.HasOutput)
        {
            height += HelpHeight + Spacing;
        }

        var loop = element.FindPropertyRelative("loop").boolValue;
        var duration = element.FindPropertyRelative("duration").floatValue;
        var fadeIn = element.FindPropertyRelative("fadeIn").floatValue;
        var fadeOut = element.FindPropertyRelative("fadeOut").floatValue;
        if ((!loop && duration <= 0f)
            || (!loop && fadeIn + fadeOut > duration)
            || loop)
        {
            height += HelpHeight + Spacing;
        }

        return height + Spacing;
    }

    private void DrawProfileSelector(ref Rect rect, SerializedProperty profile)
    {
        var menuWidth = rect.height + 4f;
        var fieldRect = new Rect(rect.x, rect.y, rect.width - menuWidth, rect.height);
        var menuRect = new Rect(fieldRect.xMax, rect.y - 2f, menuWidth, menuWidth);

        EditorGUI.PropertyField(
            fieldRect,
            profile,
            new GUIContent("Profile", "Reusable profile containing the motor balance."));

        if (GUI.Button(
                menuRect,
                new GUIContent(EditorGUIUtility.IconContent("_Popup").image, "Built-in Vibration Profiles"),
                GUI.skin.label))
        {
            ShowProfilePresetMenu(profile.propertyPath);
        }

        NextLine(ref rect);
    }

    private void ShowProfilePresetMenu(string propertyPath)
    {
        var menu = new GenericMenu();
        var current = serializedObject.FindProperty(propertyPath).objectReferenceValue;
        menu.AddItem(new GUIContent("None"), current == null, () => AssignProfile(propertyPath, null));
        menu.AddSeparator(string.Empty);

        var profiles = LoadBuiltInProfiles();
        for (var i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i];
            menu.AddItem(
                new GUIContent(profile.name),
                current == profile,
                () => AssignProfile(propertyPath, profile));
        }

        if (profiles.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("No Built-in Profiles Found"));
        }

        menu.ShowAsContext();
    }

    private void AssignProfile(string propertyPath, GamepadVibrationProfile profile)
    {
        serializedObject.Update();
        serializedObject.FindProperty(propertyPath).objectReferenceValue = profile;
        serializedObject.ApplyModifiedProperties();
    }

    private void ShowAddMenu(Rect buttonRect, ReorderableList targetList)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Empty"), false, () => AddElement(null));

        var profiles = LoadBuiltInProfiles();
        if (profiles.Length > 0)
        {
            menu.AddSeparator(string.Empty);
            for (var i = 0; i < profiles.Length; i++)
            {
                var profile = profiles[i];
                menu.AddItem(new GUIContent($"Built-in Profiles/{profile.name}"), false, () => AddElement(profile));
            }
        }

        menu.AddSeparator(string.Empty);
        if (targetList.index >= 0 && targetList.index < entries.arraySize)
        {
            var selectedIndex = targetList.index;
            menu.AddItem(new GUIContent("Duplicate Selected"), false, () => DuplicateElement(selectedIndex));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Duplicate Selected"));
        }

        menu.DropDown(buttonRect);
    }

    private void AddElement(GamepadVibrationProfile profile)
    {
        serializedObject.Update();
        var index = entries.arraySize;
        entries.arraySize++;
        var element = entries.GetArrayElementAtIndex(index);

        element.FindPropertyRelative("isEnabled").boolValue = true;
        element.FindPropertyRelative("profile").objectReferenceValue = profile;
        element.FindPropertyRelative("strengthMultiplier").floatValue = 1f;
        ApplyProfileDefaults(element, profile);
        element.isExpanded = true;

        serializedObject.ApplyModifiedProperties();
        list.index = index;
    }

    private static void ApplyProfileDefaults(SerializedProperty element, GamepadVibrationProfile profile)
    {
        var loop = false;
        var duration = 0.2f;
        var fadeIn = 0f;
        var fadeOut = 0.05f;

        switch (profile != null ? profile.name : string.Empty)
        {
            case "UI Click":
                duration = 0.06f;
                fadeOut = 0.015f;
                break;
            case "Light Impact":
                duration = 0.11f;
                fadeOut = 0.03f;
                break;
            case "Heavy Impact":
                duration = 0.32f;
                fadeIn = 0.01f;
                fadeOut = 0.08f;
                break;
            case "Weapon Recoil":
                duration = 0.13f;
                fadeOut = 0.03f;
                break;
            case "Explosion":
                duration = 0.65f;
                fadeIn = 0.01f;
                fadeOut = 0.18f;
                break;
            case "Landing":
                duration = 0.28f;
                fadeIn = 0.01f;
                fadeOut = 0.07f;
                break;
            case "Damage":
                duration = 0.24f;
                fadeOut = 0.05f;
                break;
            case "Low Rumble":
                loop = true;
                fadeIn = 0.2f;
                fadeOut = 0.25f;
                break;
            case "Earthquake":
                loop = true;
                fadeIn = 0.35f;
                fadeOut = 0.5f;
                break;
            case "Electric Magic":
                duration = 0.36f;
                fadeIn = 0.02f;
                fadeOut = 0.06f;
                break;
        }

        element.FindPropertyRelative("loop").boolValue = loop;
        element.FindPropertyRelative("duration").floatValue = duration;
        element.FindPropertyRelative("fadeIn").floatValue = fadeIn;
        element.FindPropertyRelative("fadeOut").floatValue = fadeOut;
    }

    private void DuplicateElement(int index)
    {
        serializedObject.Update();
        var newIndex = entries.arraySize;
        entries.arraySize++;
        var source = entries.GetArrayElementAtIndex(index);
        var destination = entries.GetArrayElementAtIndex(newIndex);

        CopyBool(source, destination, "isEnabled");
        CopyObjectReference(source, destination, "profile");
        CopyFloat(source, destination, "strengthMultiplier");
        CopyBool(source, destination, "loop");
        CopyFloat(source, destination, "duration");
        CopyFloat(source, destination, "fadeIn");
        CopyFloat(source, destination, "fadeOut");

        var duplicateIndex = index + 1;
        entries.MoveArrayElement(newIndex, duplicateIndex);
        entries.GetArrayElementAtIndex(duplicateIndex).isExpanded = true;
        serializedObject.ApplyModifiedProperties();
        list.index = duplicateIndex;
    }

    private static GamepadVibrationProfile[] LoadBuiltInProfiles()
    {
        var guids = AssetDatabase.FindAssets("t:GamepadVibrationProfile", new[] { BuiltInPresetPath });
        var profiles = new GamepadVibrationProfile[guids.Length];
        for (var i = 0; i < guids.Length; i++)
        {
            profiles[i] = AssetDatabase.LoadAssetAtPath<GamepadVibrationProfile>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        Array.Sort(profiles, (left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));
        return profiles;
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

    private static void DrawHelpBox(ref Rect rect, string message, MessageType type)
    {
        EditorGUI.HelpBox(new Rect(rect.x, rect.y, rect.width, HelpHeight), message, type);
        rect.y += HelpHeight + Spacing;
    }

    private static void NextLine(ref Rect rect)
    {
        rect.y += EditorGUIUtility.singleLineHeight + Spacing;
    }
}
}
