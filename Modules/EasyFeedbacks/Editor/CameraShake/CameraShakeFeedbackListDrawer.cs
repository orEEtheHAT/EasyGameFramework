using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

internal sealed class CameraShakeFeedbackListDrawer
{
    private const float Spacing = 2f;
    private const float HelpHeight = 36f;
    private const string BuiltInPresetPath =
        "Packages/com.oreethehat.easy-game-framework/Modules/EasyFeedbacks/Runtime/Presets/Camera Shake";

    private readonly SerializedObject serializedObject;
    private readonly SerializedProperty entries;
    private readonly ReorderableList list;

    public CameraShakeFeedbackListDrawer(SerializedObject serializedObject, SerializedProperty entries)
    {
        this.serializedObject = serializedObject;
        this.entries = entries;

        list = new ReorderableList(serializedObject, entries, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Camera Shake Layers"),
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
        var noiseDuration = element.FindPropertyRelative("noiseDuration");
        var noiseFadeIn = element.FindPropertyRelative("noiseFadeIn");
        var noiseFadeOut = element.FindPropertyRelative("noiseFadeOut");

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var y = rect.y + Spacing;
        var enabledRect = new Rect(rect.x, y, 18f, lineHeight);
        var headerRect = new Rect(rect.x + 32f, y, rect.width - 108f, lineHeight);
        var profileName = profile.objectReferenceValue != null
            ? profile.objectReferenceValue.name
            : "Missing Camera Shake Profile";

        isEnabled.boolValue = EditorGUI.Toggle(
            enabledRect,
            new GUIContent(string.Empty, "Enable or disable this camera shake layer."),
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
            DrawHelpBox(ref contentRect, "Assign a Camera Shake Profile.", MessageType.Warning);
        }

        DrawSectionLabel(ref contentRect, "Invocation");
        DrawProperty(
            ref contentRect,
            strengthMultiplier,
            new GUIContent("Strength Multiplier", "Multiplied by the common feedback Intensity. Values above 1 amplify the shake."));

        DrawSectionLabel(ref contentRect, "Playback");
        DrawProperty(
            ref contentRect,
            loop,
            new GUIContent(
                "Loop",
                "Keeps Noise Shake active until FeedbackHandle.Stop. Directional Kick still plays once at the beginning."));
        using (new EditorGUI.DisabledScope(loop.boolValue))
        {
            DrawProperty(
                ref contentRect,
                noiseDuration,
                new GUIContent(
                    "Noise Duration",
                    "Total duration of a one-shot Noise Shake. Ignored while Loop is enabled."));
        }
        DrawProperty(
            ref contentRect,
            noiseFadeIn,
            new GUIContent("Noise Fade In", "Seconds used to fade Noise Shake in for this preset invocation."));
        DrawProperty(
            ref contentRect,
            noiseFadeOut,
            new GUIContent("Noise Fade Out", "Seconds used to fade Noise Shake out. A loop uses it after Stop."));

        if (loop.boolValue)
        {
            DrawHelpBox(
                ref contentRect,
                "Loop affects Noise Shake and ignores Noise Duration. Directional Kick is intentionally emitted only once.",
                MessageType.Info);
        }

        if (!loop.boolValue
            && noiseFadeIn.floatValue + noiseFadeOut.floatValue > noiseDuration.floatValue)
        {
            DrawHelpBox(
                ref contentRect,
                "For a one-shot shake, Noise Fade In and Fade Out together cannot be longer than Noise Duration. Runtime values will be scaled down proportionally.",
                MessageType.Warning);
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

        if (element.FindPropertyRelative("profile").objectReferenceValue == null)
        {
            height += HelpHeight + Spacing;
        }
        if (element.FindPropertyRelative("loop").boolValue)
        {
            height += HelpHeight + Spacing;
        }

        else if (element.FindPropertyRelative("noiseFadeIn").floatValue
                 + element.FindPropertyRelative("noiseFadeOut").floatValue
                 > element.FindPropertyRelative("noiseDuration").floatValue)
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
            new GUIContent("Profile", "Reusable Camera Shake Profile containing the Cinemachine impulse definition."));

        if (GUI.Button(
                menuRect,
                new GUIContent(EditorGUIUtility.IconContent("_Popup").image, "Built-in Camera Shake Profiles"),
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

    private void AssignProfile(string propertyPath, CameraShakeProfile profile)
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

    private void AddElement(CameraShakeProfile profile)
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

    private static void ApplyProfileDefaults(SerializedProperty element, CameraShakeProfile profile)
    {
        var loop = false;
        var duration = 0.35f;
        var fadeIn = 0f;
        var fadeOut = 0.25f;

        switch (profile != null ? profile.name : string.Empty)
        {
            case "Light Impact":
                duration = 0.16f;
                fadeOut = 0.1f;
                break;
            case "Heavy Impact":
                duration = 0.35f;
                fadeIn = 0.02f;
                fadeOut = 0.22f;
                break;
            case "Weapon Recoil":
                duration = 0.18f;
                fadeOut = 0.12f;
                break;
            case "Explosion":
                duration = 0.7f;
                fadeIn = 0.03f;
                fadeOut = 0.45f;
                break;
            case "Landing":
                duration = 0.45f;
                fadeIn = 0.02f;
                fadeOut = 0.25f;
                break;
            case "Damage":
                duration = 0.22f;
                fadeOut = 0.12f;
                break;
            case "Low Rumble":
                loop = true;
                fadeIn = 0.2f;
                fadeOut = 0.35f;
                break;
            case "Earthquake":
                loop = true;
                fadeIn = 0.35f;
                fadeOut = 0.7f;
                break;
            case "Electric Magic":
                duration = 0.45f;
                fadeIn = 0.02f;
                fadeOut = 0.25f;
                break;
            case "Shockwave":
                duration = 1f;
                fadeOut = 0.65f;
                break;
        }

        element.FindPropertyRelative("loop").boolValue = loop;
        element.FindPropertyRelative("noiseDuration").floatValue = duration;
        element.FindPropertyRelative("noiseFadeIn").floatValue = fadeIn;
        element.FindPropertyRelative("noiseFadeOut").floatValue = fadeOut;
    }

    private void DuplicateElement(int index)
    {
        serializedObject.Update();
        var newIndex = entries.arraySize;
        entries.arraySize++;
        var source = entries.GetArrayElementAtIndex(index);
        var destination = entries.GetArrayElementAtIndex(newIndex);

        destination.FindPropertyRelative("isEnabled").boolValue =
            source.FindPropertyRelative("isEnabled").boolValue;
        destination.FindPropertyRelative("profile").objectReferenceValue =
            source.FindPropertyRelative("profile").objectReferenceValue;
        destination.FindPropertyRelative("strengthMultiplier").floatValue =
            source.FindPropertyRelative("strengthMultiplier").floatValue;
        destination.FindPropertyRelative("loop").boolValue =
            source.FindPropertyRelative("loop").boolValue;
        destination.FindPropertyRelative("noiseDuration").floatValue =
            source.FindPropertyRelative("noiseDuration").floatValue;
        destination.FindPropertyRelative("noiseFadeIn").floatValue =
            source.FindPropertyRelative("noiseFadeIn").floatValue;
        destination.FindPropertyRelative("noiseFadeOut").floatValue =
            source.FindPropertyRelative("noiseFadeOut").floatValue;

        var duplicateIndex = index + 1;
        entries.MoveArrayElement(newIndex, duplicateIndex);
        entries.GetArrayElementAtIndex(duplicateIndex).isExpanded = true;
        serializedObject.ApplyModifiedProperties();
        list.index = duplicateIndex;
    }

    private static CameraShakeProfile[] LoadBuiltInProfiles()
    {
        var guids = AssetDatabase.FindAssets("t:CameraShakeProfile", new[] { BuiltInPresetPath });
        var profiles = new CameraShakeProfile[guids.Length];
        for (var i = 0; i < guids.Length; i++)
        {
            profiles[i] = AssetDatabase.LoadAssetAtPath<CameraShakeProfile>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
        }

        Array.Sort(profiles, (left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));
        return profiles;
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
