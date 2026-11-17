using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyInteraction.Editor
{
    /// <summary>Editor-only wire preview. Handles edit serialized pose values; scene geometry is never moved.</summary>
    [InitializeOnLoad]
    public static class GrabPoseAuthoring
    {
        private struct Part
        {
            public Mesh Mesh;
            public Matrix4x4 ItemLocal;
            public bool Owned;
        }

        private static GrabAction item;
        private static InteractionActor actor;
        private static Transform previewFrame;
        private static bool previewFrameIsView = true;
        private static Pose sceneViewFrame = new Pose(Vector3.zero, Quaternion.identity);
        private static readonly List<Part> parts = new List<Part>();
        private static Material wireMaterial;
        private static bool previousToolsHidden;
        private static bool sessionActive;
        private static int dragGroup = -1;

        static GrabPoseAuthoring()
        {
            SceneView.duringSceneGui += DuringSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += End;
            EditorApplication.playModeStateChanged += _ => End();
            Selection.selectionChanged += SelectionChanged;
            Undo.undoRedoPerformed += SceneView.RepaintAll;
            EditorApplication.quitting += End;
            EditorApplication.update += CleanupMissingItem;
        }

        public static GrabAction PreviewItem => item;
        public static bool IsActive => item;

        /// <summary>Starts a session. Actor/frame references stay in Editor memory and are not saved to a prefab.</summary>
        public static void Begin(GrabAction grab, InteractionActor previewActor = null)
        {
            End();
            if (!grab || grab.PoseMode != GrabPoseMode.AuthoredPose || EditorApplication.isPlayingOrWillChangePlaymode) return;
            item = grab;
            actor = previewActor;
            previewFrame = null;
            previewFrameIsView = true;
            SnapshotSceneView();
            previousToolsHidden = Tools.hidden;
            sessionActive = true;
            Tools.hidden = true;
            CacheGeometry();
            SceneView.RepaintAll();
        }

        public static void End()
        {
            if (sessionActive) Tools.hidden = previousToolsHidden;
            sessionActive = false;
            if (dragGroup >= 0)
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(dragGroup);
            }
            item = null;
            actor = null;
            previewFrame = null;
            dragGroup = -1;
            foreach (var part in parts)
                if (part.Owned && part.Mesh) Object.DestroyImmediate(part.Mesh);
            parts.Clear();
            if (wireMaterial) Object.DestroyImmediate(wireMaterial);
            wireMaterial = null;
            SceneView.RepaintAll();
        }

        /// <summary>Uses an explicit, unscaled preview frame after the item's own HoldPoint and actor HandAnchor.</summary>
        public static void SetPreviewFrame(Transform frame, bool isView = true)
        {
            previewFrame = frame;
            previewFrameIsView = isView;
            SceneView.RepaintAll();
        }

        /// <summary>A fixed fallback frame is also useful for deterministic Editor acceptance and prefab-stage authoring.</summary>
        public static void SetSceneViewFrame(Pose frame)
        {
            sceneViewFrame = frame;
            actor = null;
            previewFrame = null;
            SceneView.RepaintAll();
        }

        public static bool TryGetPreviewPose(out Pose rootPose)
        {
            if (!TryGetFrame(out var frame, out var isView, out _))
            {
                rootPose = default;
                return false;
            }
            rootPose = GrabPoseUtility.GetRootPose(item, frame, isView);
            return true;
        }

        /// <summary>Same write path as the Scene View handles, exposed for Editor integrations and acceptance.</summary>
        public static bool ApplyRootPose(Pose rootPose)
        {
            if (!TryGetFrame(out var frame, out var isView, out _)) return false;
            if (item.PoseMode != GrabPoseMode.AuthoredPose) return false;
            var local = GrabPoseUtility.GetLocalPose(item, rootPose, frame, isView);
            Undo.RecordObject(item, "Edit held item pose");
            item.LocalPosition = local.position;
            item.LocalEulerAngles = local.rotation.eulerAngles;
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            EditorUtility.SetDirty(item);
            SceneView.RepaintAll();
            return true;
        }

        public static void DrawInspector(GrabAction grab, SerializedObject serialized)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (serialized.FindProperty("poseMode").intValue != (int)GrabPoseMode.AuthoredPose)
            {
                if (item == grab) End();
                return;
            }
            if (item != grab)
            {
                if (GUILayout.Button("Edit Hold Pose in Scene View"))
                {
                    serialized.ApplyModifiedProperties();
                    Begin(grab);
                }
                return;
            }

            // Numeric fields above this action may still be pending in the custom inspector.
            // Flush them before evaluating the preview and before the final Update below.
            serialized.ApplyModifiedProperties();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hold Pose Preview", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            actor = (InteractionActor)EditorGUILayout.ObjectField(new GUIContent("Preview Actor", "Optional. Uses HoldPoint > Actor HandAnchor > Actor View. This reference is not saved on the prefab."), actor, typeof(InteractionActor), true);
            previewFrame = (Transform)EditorGUILayout.ObjectField(new GUIContent("Preview View / Frame", "Optional fallback when no HoldPoint or Actor HandAnchor exists. Frame scale is ignored."), previewFrame, typeof(Transform), true);
            if (previewFrame) previewFrameIsView = EditorGUILayout.Toggle(new GUIContent("Add Hold Distance", "On for a View frame; off for a direct hold anchor."), previewFrameIsView);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            if (TryGetFrame(out _, out _, out var label))
                EditorGUILayout.LabelField("Effective frame", label);
            else
                EditorGUILayout.HelpBox("Hold Point must be outside the held item and Grip Point must belong to its Rigidbody. Fix these references before editing the pose.", MessageType.Warning);
            EditorGUILayout.HelpBox("The cyan wire item is the held-size preview. W moves it; E rotates it. Pose changes save through normal Undo/prefab workflows. The real item stays in place. Without an Actor, a fixed Scene View snapshot is used, including in Prefab Mode.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Scene View as View"))
                {
                    actor = null;
                    previewFrame = null;
                    SnapshotSceneView();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Frame Preview") && TryGetPreviewPose(out var rootPose))
                {
                    SceneView.lastActiveSceneView?.Frame(new Bounds(rootPose.position, Vector3.one * 2), false);
                }
                if (GUILayout.Button("Done")) End();
            }
            serialized.Update();
        }

        private static bool TryGetFrame(out Pose frame, out bool isView, out string label)
        {
            frame = default;
            isView = false;
            label = null;
            if (!item || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (item.HoldPoint && item.HoldPoint.IsChildOf(item.transform)) return false;
            if (item.GripPoint && item.GripPoint.GetComponentInParent<Rigidbody>() != item.Body) return false;
            var chosen = item.HoldPoint;
            if (chosen) label = "Item Hold Point";
            else if (actor && actor.HandAnchor)
            {
                chosen = actor.HandAnchor;
                label = "Actor Hand Anchor";
            }
            else if (previewFrame)
            {
                chosen = previewFrame;
                isView = previewFrameIsView;
                label = isView ? "Preview View (+ Hold Distance)" : "Preview Frame";
            }
            else if (actor)
            {
                // Read authored data without invoking Actor.View's lazy resolver and mutating the scene.
                var property = new SerializedObject(actor).FindProperty("view");
                chosen = property.objectReferenceValue as Transform;
                if (!chosen)
                {
                    var camera = actor.GetComponentInChildren<Camera>();
                    chosen = camera ? camera.transform : actor.transform;
                }
                isView = true;
                label = "Actor View (+ Hold Distance)";
            }
            else
            {
                frame = sceneViewFrame;
                isView = true;
                label = "Fixed Scene View snapshot (+ Hold Distance)";
                return true;
            }
            frame = new Pose(chosen.position, chosen.rotation);
            return true;
        }

        private static void SnapshotSceneView()
        {
            var sceneView = SceneView.lastActiveSceneView;
            var camera = sceneView ? sceneView.camera : null;
            sceneViewFrame = camera ? new Pose(camera.transform.position, camera.transform.rotation) : new Pose(Vector3.zero, Quaternion.identity);
        }

        private static void SelectionChanged()
        {
            if (sessionActive && (!item || Selection.activeGameObject != item.gameObject)) End();
        }

        private static void CleanupMissingItem()
        {
            if (sessionActive && (!item || item.PoseMode != GrabPoseMode.AuthoredPose)) End();
        }

        private static void CacheGeometry()
        {
            var rootInverse = item.transform.worldToLocalMatrix;
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh && filter.GetComponent<MeshRenderer>())
                    parts.Add(new Part { Mesh = filter.sharedMesh, ItemLocal = rootInverse * filter.transform.localToWorldMatrix });
            foreach (var skin in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.sharedMesh) continue;
                var baked = new Mesh { name = "Held pose preview", hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(baked);
                parts.Add(new Part { Mesh = baked, ItemLocal = rootInverse * skin.transform.localToWorldMatrix, Owned = true });
            }
        }

        private static void DuringSceneGUI(SceneView sceneView)
        {
            if (!item)
            {
                if (sessionActive) End();
                return;
            }
            if (!TryGetFrame(out var frame, out var isView, out _)) return;
            if (item.PoseMode != GrabPoseMode.AuthoredPose) return;
            var pose = GrabPoseUtility.GetRootPose(item, frame, isView);
            if (Event.current.type == EventType.Repaint) DrawPreview(pose);
            using (new Handles.DrawingScope(Color.cyan))
            {
                Handles.DrawDottedLine(frame.position, pose.position, 4);
                Handles.Label(pose.position + Vector3.up * HandleUtility.GetHandleSize(pose.position) * .2f, "Held pose preview");
            }
            EditorGUI.BeginChangeCheck();
            var next = pose;
            if (Tools.current == Tool.Rotate)
                next.rotation = Handles.RotationHandle(pose.rotation, pose.position);
            else
                next.position = Handles.PositionHandle(pose.position, Tools.pivotRotation == PivotRotation.Local ? pose.rotation : Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                if (dragGroup < 0)
                {
                    Undo.IncrementCurrentGroup();
                    dragGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Edit held item pose");
                }
                ApplyRootPose(next);
            }
            if (Event.current.rawType == EventType.MouseUp && dragGroup >= 0)
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(dragGroup);
                dragGroup = -1;
            }
        }

        private static void DrawPreview(Pose pose)
        {
            var matrix = GrabPoseUtility.GetRootMatrix(item, pose);
            if (!wireMaterial)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (!shader) return;
                wireMaterial = new Material(shader) { name = "Held pose wire preview", hideFlags = HideFlags.HideAndDontSave };
                wireMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                wireMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                wireMaterial.SetInt("_Cull", (int)CullMode.Off);
                wireMaterial.SetInt("_ZWrite", 0);
                wireMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                wireMaterial.SetColor("_Color", new Color(.1f, 1, 1, .65f));
            }
            var previousWireframe = GL.wireframe;
            try
            {
                if (!wireMaterial.SetPass(0)) return;
                GL.wireframe = true;
                foreach (var part in parts)
                    if (part.Mesh)
                        for (int i = 0; i < part.Mesh.subMeshCount; i++) Graphics.DrawMeshNow(part.Mesh, matrix * part.ItemLocal, i);
            }
            finally { GL.wireframe = previousWireframe; }
            if (parts.Count == 0)
                using (new Handles.DrawingScope(Color.cyan, matrix)) Handles.DrawWireCube(Vector3.zero, Vector3.one * .25f);
        }
    }
}
