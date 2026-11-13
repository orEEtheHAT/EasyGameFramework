using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

internal sealed class VisualFeedbackListDrawer
{
    private const float Spacing = 2f;
    private const float HelpHeight = 36f;

    private readonly SerializedObject serializedObject;
    private readonly SerializedProperty entries;
    private readonly ReorderableList list;

    public VisualFeedbackListDrawer(SerializedObject serializedObject, SerializedProperty entries)
    {
        this.serializedObject = serializedObject;
        this.entries = entries;

        list = new ReorderableList(serializedObject, entries, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Visual Layers"),
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
        var type = element.FindPropertyRelative("type");
        var particlePrefab = element.FindPropertyRelative("particleSystemPrefab");
        var vfxPrefab = element.FindPropertyRelative("visualEffectPrefab");
        var lifetime = element.FindPropertyRelative("lifetime");
        var stopMode = element.FindPropertyRelative("stopMode");
        var vfxOneShotDuration = element.FindPropertyRelative("vfxOneShotDuration");
        var vfxStopTailDuration = element.FindPropertyRelative("vfxStopTailDuration");
        var prewarmCount = element.FindPropertyRelative("prewarmCount");
        var positionMode = element.FindPropertyRelative("positionMode");
        var positionOffset = element.FindPropertyRelative("positionOffset");
        var rotationOffset = element.FindPropertyRelative("rotationOffset");
        var scaleMultiplier = element.FindPropertyRelative("scaleMultiplier");
        var isParticle = type.enumValueIndex == (int)VisualFeedbackType.ParticleSystem;
        var resource = isParticle ? particlePrefab : vfxPrefab;

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var y = rect.y + Spacing;
        var enabledRect = new Rect(rect.x, y, 18f, lineHeight);
        var headerRect = new Rect(rect.x + 32f, y, rect.width - 108f, lineHeight);
        var resourceName = resource.objectReferenceValue != null
            ? resource.objectReferenceValue.name
            : isParticle ? "Missing Particle Prefab" : "Missing VFX Prefab";

        isEnabled.boolValue = EditorGUI.Toggle(
            enabledRect,
            new GUIContent(string.Empty, "Enable or disable this visual layer."),
            isEnabled.boolValue);
        var headerName = isEnabled.boolValue ? resourceName : $"{resourceName} (Disabled)";
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
        DrawProperty(ref contentRect, type, new GUIContent("Type"));
        DrawProperty(
            ref contentRect,
            resource,
            isParticle
                ? new GUIContent("Particle Prefab", "Prefab whose root contains a ParticleSystem.")
                : new GUIContent("VFX Prefab", "Prefab whose root contains a configured VisualEffect component."));

        if (resource.objectReferenceValue == null)
        {
            DrawHelpBox(
                ref contentRect,
                isParticle ? "Assign a Particle System prefab." : "Assign a prefab with a root VisualEffect component.",
                MessageType.Warning);
        }

        DrawSectionLabel(ref contentRect, "Playback");
        DrawProperty(
            ref contentRect,
            lifetime,
            new GUIContent(
                "Lifetime",
                "Pool lifetime policy. Loop Until Stopped requires Stop and does not make a non-looping resource loop automatically."));
        if (!isParticle && lifetime.enumValueIndex == (int)VisualFeedbackLifetime.OneShot)
        {
            DrawProperty(
                ref contentRect,
                vfxOneShotDuration,
                new GUIContent(
                    "One Shot Duration",
                    "Complete authored VFX lifetime in simulation seconds. Include spawn delays, emission, and the longest particle tail. Respects Play Rate and pauses with the feedback."));
        }

        DrawProperty(
            ref contentRect,
            stopMode,
            new GUIContent("Stop Mode", "Keep existing particles alive, or clear the instance immediately."));
        if (!isParticle && stopMode.enumValueIndex == (int)VisualFeedbackStopMode.FinishCurrentParticles)
        {
            DrawProperty(
                ref contentRect,
                vfxStopTailDuration,
                new GUIContent(
                    "Stop Tail Duration",
                    "Maximum simulation time after an explicit Stop for existing particles to finish. Respects Play Rate, pauses with the feedback, and is ignored by Clear Immediately."));
        }

        DrawProperty(
            ref contentRect,
            prewarmCount,
            new GUIContent(
                "Prewarm Count",
                "Instances created when an emitter using this preset starts. Use 1 for ordinary effects and a higher value for overlapping bursts."));

        if (isParticle && particlePrefab.objectReferenceValue != null)
        {
            var particleSystem = (ParticleSystem)particlePrefab.objectReferenceValue;
            var prefabLoops = HasLoopingSystem(particleSystem);
            var expectsLoop = lifetime.enumValueIndex == (int)VisualFeedbackLifetime.LoopUntilStopped;

            if (prefabLoops && !expectsLoop)
            {
                DrawHelpBox(
                    ref contentRect,
                    "This prefab loops, so a One Shot entry cannot return to the pool automatically.",
                    MessageType.Warning);
            }
            else if (!prefabLoops && expectsLoop)
            {
                DrawHelpBox(
                    ref contentRect,
                    "Lifetime requires Stop, but the prefab itself does not loop and may become visually empty.",
                    MessageType.Info);
            }
        }

        DrawSectionLabel(ref contentRect, "Transform");
        DrawPositioningOverride(ref contentRect, positionMode);
        DrawProperty(
            ref contentRect,
            positionOffset,
            new GUIContent("Position Offset", "Local offset rotated by the feedback context rotation."));
        DrawProperty(
            ref contentRect,
            rotationOffset,
            new GUIContent("Rotation Offset", "Local Euler correction applied on top of the context rotation."));

        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(
            contentRect,
            scaleMultiplier,
            new GUIContent("Scale Multiplier", "Multiplies the authored prefab scale and per-emitter Visual Scale."));
        if (EditorGUI.EndChangeCheck())
        {
            scaleMultiplier.vector3Value = ClampScale(scaleMultiplier.vector3Value);
        }

        NextLine(ref contentRect);
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

        // Source (3), Playback (4), Transform (5).
        height += 12f * (line + Spacing);

        var type = element.FindPropertyRelative("type");
        var isParticle = type.enumValueIndex == (int)VisualFeedbackType.ParticleSystem;
        if (!isParticle
            && element.FindPropertyRelative("lifetime").enumValueIndex == (int)VisualFeedbackLifetime.OneShot)
        {
            height += line + Spacing;
        }

        if (!isParticle
            && element.FindPropertyRelative("stopMode").enumValueIndex == (int)VisualFeedbackStopMode.FinishCurrentParticles)
        {
            height += line + Spacing;
        }

        var resource = isParticle
            ? element.FindPropertyRelative("particleSystemPrefab")
            : element.FindPropertyRelative("visualEffectPrefab");

        if (resource.objectReferenceValue == null)
        {
            height += HelpHeight + Spacing;
        }

        if (isParticle && resource.objectReferenceValue != null)
        {
            var particleSystem = (ParticleSystem)resource.objectReferenceValue;
            var prefabLoops = HasLoopingSystem(particleSystem);
            var expectsLoop = element.FindPropertyRelative("lifetime").enumValueIndex
                              == (int)VisualFeedbackLifetime.LoopUntilStopped;
            if (prefabLoops != expectsLoop)
            {
                height += HelpHeight + Spacing;
            }
        }

        return height + Spacing;
    }

    private void ShowAddMenu(Rect buttonRect, ReorderableList targetList)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Particle System"), false, () => AddEmptyElement(VisualFeedbackType.ParticleSystem));
        menu.AddItem(new GUIContent("VFX Graph"), false, () => AddEmptyElement(VisualFeedbackType.VFXGraph));

        if (targetList.index >= 0 && targetList.index < entries.arraySize)
        {
            var selectedIndex = targetList.index;
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Duplicate Selected"), false, () => DuplicateElement(selectedIndex));
        }

        menu.DropDown(buttonRect);
    }

    private void AddEmptyElement(VisualFeedbackType type)
    {
        serializedObject.Update();
        var index = entries.arraySize;
        entries.arraySize++;
        var element = entries.GetArrayElementAtIndex(index);

        element.FindPropertyRelative("isEnabled").boolValue = true;
        element.FindPropertyRelative("type").enumValueIndex = (int)type;
        element.FindPropertyRelative("particleSystemPrefab").objectReferenceValue = null;
        element.FindPropertyRelative("visualEffectPrefab").objectReferenceValue = null;
        element.FindPropertyRelative("lifetime").enumValueIndex = (int)VisualFeedbackLifetime.OneShot;
        element.FindPropertyRelative("stopMode").enumValueIndex = (int)VisualFeedbackStopMode.FinishCurrentParticles;
        element.FindPropertyRelative("vfxOneShotDuration").floatValue = 2f;
        element.FindPropertyRelative("vfxStopTailDuration").floatValue = 1f;
        element.FindPropertyRelative("prewarmCount").intValue = 1;
        element.FindPropertyRelative("positionMode").intValue = (int)FeedbackPositionOverride.Inherited;
        element.FindPropertyRelative("positionOffset").vector3Value = Vector3.zero;
        element.FindPropertyRelative("rotationOffset").vector3Value = Vector3.zero;
        element.FindPropertyRelative("scaleMultiplier").vector3Value = Vector3.one;
        element.isExpanded = true;

        serializedObject.ApplyModifiedProperties();
        list.index = index;
    }

    private void DuplicateElement(int index)
    {
        serializedObject.Update();
        var newIndex = entries.arraySize;
        entries.arraySize++;
        CopyEntry(entries.GetArrayElementAtIndex(index), entries.GetArrayElementAtIndex(newIndex));

        var duplicateIndex = index + 1;
        entries.MoveArrayElement(newIndex, duplicateIndex);
        entries.GetArrayElementAtIndex(duplicateIndex).isExpanded = true;
        serializedObject.ApplyModifiedProperties();
        list.index = duplicateIndex;
    }

    private static void CopyEntry(SerializedProperty source, SerializedProperty destination)
    {
        CopyBool(source, destination, "isEnabled");
        CopyEnum(source, destination, "type");
        CopyObjectReference(source, destination, "particleSystemPrefab");
        CopyObjectReference(source, destination, "visualEffectPrefab");
        CopyEnum(source, destination, "lifetime");
        CopyEnum(source, destination, "stopMode");
        CopyFloat(source, destination, "vfxOneShotDuration");
        CopyFloat(source, destination, "vfxStopTailDuration");
        CopyInt(source, destination, "prewarmCount");
        CopyEnum(source, destination, "positionMode");
        CopyVector3(source, destination, "positionOffset");
        CopyVector3(source, destination, "rotationOffset");
        CopyVector3(source, destination, "scaleMultiplier");
    }

    private static bool HasLoopingSystem(ParticleSystem root)
    {
        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (var i = 0; i < systems.Length; i++)
        {
            if (systems[i].main.loop)
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyObjectReference(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).objectReferenceValue = source.FindPropertyRelative(name).objectReferenceValue;
    }

    private static void CopyBool(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).boolValue = source.FindPropertyRelative(name).boolValue;
    }

    private static void CopyEnum(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).enumValueIndex = source.FindPropertyRelative(name).enumValueIndex;
    }

    private static void CopyInt(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).intValue = source.FindPropertyRelative(name).intValue;
    }

    private static void CopyFloat(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).floatValue = source.FindPropertyRelative(name).floatValue;
    }

    private static void CopyVector3(SerializedProperty source, SerializedProperty destination, string name)
    {
        destination.FindPropertyRelative(name).vector3Value = source.FindPropertyRelative(name).vector3Value;
    }

    private static Vector3 ClampScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0f, scale.x),
            Mathf.Max(0f, scale.y),
            Mathf.Max(0f, scale.z));
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
        EditorGUI.HelpBox(new Rect(rect.x, rect.y, rect.width, HelpHeight), message, type);
        rect.y += HelpHeight + Spacing;
    }

    private static void NextLine(ref Rect rect)
    {
        rect.y += EditorGUIUtility.singleLineHeight + Spacing;
    }
}
}
