using UnityEditor;
using UnityEngine;

namespace EasyInteraction.Editor
{
    [CustomEditor(typeof(InteractionFocusHighlighter))]
    public sealed class InteractionFocusHighlighterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var highlighter = (InteractionFocusHighlighter)target;
            var actor = highlighter.Actor;
            var source = new SerializedObject(highlighter);
            var camera = source.FindProperty("cameraOverride").objectReferenceValue as Camera;
            if (!camera && actor)
            {
                // Diagnostics must not resolve Actor.View and write its serialized fallback in Edit Mode.
                var view = new SerializedObject(actor).FindProperty("view").objectReferenceValue as Transform;
                camera = view ? view.GetComponent<Camera>() : actor.GetComponentInChildren<Camera>();
            }
            EditorGUILayout.HelpBox("One highlighter styles this Actor's focused targets. Each Target has a Focus Outline checkbox (enabled by default); no per-item Outline component is required. Drawing goes only to Camera Override or the Camera on Actor.View. Held items stay suppressed for all observers. Width is in world metres.", MessageType.Info);
            if (!camera)
                EditorGUILayout.HelpBox("No render camera is configured. Assign Camera Override, or make Actor.View reference its Camera Transform. MainCamera and global-camera fallbacks are intentionally not used.", MessageType.Warning);
            if (!highlighter.IsShaderAvailable)
                EditorGUILayout.HelpBox("Focus outline requires an active URP asset and the bundled Resources/EasyInteraction/FocusOutline shader. This check does not create an effect material.", MessageType.Warning);
        }
    }
}
