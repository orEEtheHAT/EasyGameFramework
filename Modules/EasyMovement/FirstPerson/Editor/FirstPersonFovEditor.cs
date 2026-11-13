using UnityEditor;

namespace EasyMovement.FirstPerson.Editor
{
    [CustomEditor(typeof(FirstPersonFov))]
    public sealed class FirstPersonFovEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Owns vertical FOV of runtime gait-camera instances. User base FOV preserves authored differences; temporary zoom uses AcquireOffset. Changes apply only when ownership/settings/camera membership changes. Perspective non-physical lenses only.", MessageType.Info);
            var fov = (FirstPersonFov)target;
            if (EditorApplication.isPlaying && !fov.IsAvailable) EditorGUILayout.HelpBox(fov.UnavailableReason, MessageType.Warning);
        }
    }
}
