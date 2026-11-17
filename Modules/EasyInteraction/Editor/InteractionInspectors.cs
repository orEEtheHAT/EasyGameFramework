using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyInteraction.Editor
{
    [CustomEditor(typeof(InteractionActor)), CanEditMultipleObjects]
    public sealed class InteractionActorEditor : UnityEditor.Editor
    {
        private bool advanced, diagnostics;
        private InteractionPreset preset;
        private void Field(string name, string label = null)
        {
            var property = serializedObject.FindProperty(name);
            if (property != null)
                EditorGUILayout.PropertyField(property, label == null ? null : new GUIContent(label), true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var actor = (InteractionActor)target;
            EditorGUILayout.HelpBox("Selection chooses an object; actions define behavior. For an EasyMovement player use Tools > EasyGameFramework > Interaction > Set Up Selected Player. Primary Interact releases your held item even while looking away; explicit menu commands remain addressable.", MessageType.Info);
            Field("view");
            Field("selectionMode");
            Field("range");
            switch ((InteractionSelectionMode)serializedObject.FindProperty("selectionMode").enumValueIndex)
            {
                case InteractionSelectionMode.Focus:
                    Field("focusAngle");
                    Field("directionWeight");
                    Field("switchThreshold");
                    break;
                case InteractionSelectionMode.SphereCast:
                    Field("castRadius");
                    break;
                case InteractionSelectionMode.Cursor:
                    Field("cursorCamera");
                    break;
                case InteractionSelectionMode.Contact:
                    Field("contactRadius");
                    break;
                case InteractionSelectionMode.Explicit:
                    EditorGUILayout.HelpBox("Use actor.SelectTarget(target), TryExecuteDirect(action), or an NPC/XR adapter. Reach and visibility are still checked.", MessageType.Info);
                    break;
            }

            advanced = EditorGUILayout.Foldout(advanced, "Advanced", true);
            if (advanced)
            {
                foreach (string name in new[]
                {
                    "targetLayers",
                    "obstacleLayers",
                    "requireVisibility",
                    "includeTriggers",
                    "playerRoot",
                    "handAnchor",
                    "motor",
                    "commandAdapter",
                    "automaticUpdate",
                    "useUnscaledTime",
                    "initialCapabilities",
                    "onTargetChanged"
                }

                )
                    Field(name);
                preset = (InteractionPreset)EditorGUILayout.ObjectField("Copy settings from preset", preset, typeof(InteractionPreset), false);
                if (preset && GUILayout.Button("Apply selection preset"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(actor, "Apply interaction preset");
                    preset.ApplyTo(actor);
                    EditorUtility.SetDirty(actor);
                    serializedObject.Update();
                }
            }

            diagnostics = EditorGUILayout.Foldout(diagnostics, "Diagnostics", true);
            if (diagnostics)
            {
                Field("debugSelection");
                if (Application.isPlaying)
                {
                    EditorGUILayout.LabelField("Target", actor.CurrentTarget ? actor.CurrentTarget.DisplayName : "None");
                    EditorGUILayout.LabelField("Selected action", actor.SelectedAction ? actor.SelectedAction.GetLabel(actor) : "None");
                    EditorGUILayout.LabelField("Active actions / queued", actor.ActiveCount + " / " + (actor.QueuedAction ? actor.QueuedAction.ActionId : "None"));
                    EditorGUILayout.LabelField("Candidates / visibility / queries", actor.LastCandidateCount + " / " + actor.LastVisibilityChecks + " / " + actor.LastPhysicsQueries);
                    if (!actor.LastFailure.IsAvailable)
                        EditorGUILayout.HelpBox(actor.LastFailure.Code + ": " + actor.LastFailure.Reason + "\n" + actor.LastFailure.Diagnostic, MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
            if (!actor.GetComponent<InteractionFocusHighlighter>() && GUILayout.Button("Add focus outline highlighter (opt-in)"))
                Undo.AddComponent<InteractionFocusHighlighter>(actor.gameObject);
            bool hasInput = false;
            foreach (var component in actor.GetComponents<MonoBehaviour>())
                if (component is IInteractionInputSource) { hasInput = true; break; }
            if (!hasInput && GUILayout.Button("Add standalone input (camera / prototype)"))
                Undo.AddComponent<InteractionInput>(actor.gameObject);
            if (!actor.GetComponent<InteractionPresenter>() && GUILayout.Button("Add UI presenter"))
                Undo.AddComponent<InteractionPresenter>(actor.gameObject);
        }
    }

    [CustomEditor(typeof(InteractionAction), true)]
    public sealed class InteractionActionEditor : UnityEditor.Editor
    {
        private bool advanced, feedback;
        private InteractionPreset preset;
        private static readonly HashSet<string> common = new HashSet<string>
        {
            "m_Script",
            "actionId",
            "actionLabel",
            "labelKey",
            "icon",
            "hideWhenUnavailable",
            "channel",
            "useDefaultChannel",
            "inputGesture",
            "useDefaultGesture",
            "confirmationSeconds",
            "execution",
            "duration",
            "timeout",
            "requireRangeWhileRunning",
            "requireVisibilityWhileRunning",
            "useScope",
            "useGroup",
            "maxParticipants",
            "useDefaultParticipants",
            "conditionMatch",
            "conditions",
            "costTiming",
            "approachPoint",
            "approachDistance",
            "approachAngle",
            "onStarted",
            "onCommitted",
            "onCompleted",
            "onCancelled",
            "onProgress"
        };
        private void Field(string name, string label = null, string tooltip = null)
        {
            var property = serializedObject.FindProperty(name);
            if (property != null)
                EditorGUILayout.PropertyField(property, label == null ? null : new GUIContent(label, tooltip), true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var action = (InteractionAction)target;
            if (action is EventAction)
                Field("actionLabel");
            var iterator = serializedObject.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (common.Contains(iterator.name))
                    continue;
                if (action is GrabAction && iterator.name != "grabRange" && iterator.name != "maximumMass" && iterator.name != "holdDistance")
                    continue;
                EditorGUILayout.PropertyField(iterator, true);
            }

            if (action is GrabAction)
            {
                Field("poseMode");
                bool authored = serializedObject.FindProperty("poseMode").intValue == (int)GrabPoseMode.AuthoredPose;
                if (authored)
                {
                    Field("localPosition");
                    Field("localEulerAngles");
                }
                else
                {
                    Field("rotationMode");
                    Field("holdOffset");
                }
                Field("followSpeed", "Follow Speed", "0 = soft, slower return; 1 = firm, faster return. Controls both position and driven rotation. Contacts can still block the dynamic Rigidbody.");
                Field("movementLag", "Movement Lag (XYZ)", "Walking lag per movement-reference axis: X side, Y up, Z forward. 0 compensates walking velocity damping on that axis; 1 keeps it. Contacts and body damping can still delay the body. Independent of camera turning.");
                Field("heldScaleMultiplier");
                if (authored) GrabPoseAuthoring.DrawInspector((GrabAction)action, serializedObject);
                EditorGUILayout.HelpBox(authored
                    ? "Authored Pose is the default. Edit Hold Pose opens Scene View handles without moving the scene item. Local Position uses Hold Point > Hand Anchor > View axes in metres, without frame scale. View adds Hold Distance; anchors replace distance. Rotation is for the Rigidbody root; Grip Point changes only the positioned point. Follow Speed controls the physical return; contacts can block the item. Movement Lag controls walking separately."
                    : "Hold Point overrides Actor Hand Anchor; either anchor overrides Hold Distance and inspection distance. Without an anchor, zero Hold Offset holds the item centred in front of View at Hold Distance. Offset uses the chosen point's local axes in metres. Grip Point selects the point on the item (otherwise centre of mass).", MessageType.Info);
            }

            EditorGUI.BeginChangeCheck();
            var gesture = (InteractionInputGesture)EditorGUILayout.EnumPopup("Input", action.InputGesture);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.FindProperty("inputGesture").enumValueIndex = (int)gesture;
                serializedObject.FindProperty("useDefaultGesture").boolValue = false;
            }

            if (gesture == InteractionInputGesture.HoldToConfirm)
                Field("confirmationSeconds");
            if (!(action is GrabAction) && !(action is CooperativeAction) && !(action is MechanicalAction) && !(action is InspectAction))
            {
                Field("execution");
                if (serializedObject.FindProperty("execution").enumValueIndex == (int)InteractionExecutionMode.Timed)
                    Field("duration");
            }

            advanced = EditorGUILayout.Foldout(advanced, "Advanced / rules and coordination", true);
            if (advanced)
            {
                if (!(action is EventAction))
                    Field("actionLabel");
                if (action is GrabAction || action is CooperativeAction || action is MechanicalAction || action is InspectAction)
                {
                    Field("execution");
                    if (serializedObject.FindProperty("execution").enumValueIndex == (int)InteractionExecutionMode.Timed)
                        Field("duration");
                }
                if (action is GrabAction)
                    foreach (string name in new[]
                    {
                        "gripPoint",
                        "holdPoint",
                        "movementReference",
                        "ignoreOwnerCollisions",
                        "breakDistance",
                        "minimumDistance",
                        "maximumDistance",
                        "throwSpeed",
                        "inspectionDistance"
                    }

                    )
                        Field(name);
                Field("actionId");
                Field("labelKey");
                Field("icon");
                Field("hideWhenUnavailable");
                EditorGUI.BeginChangeCheck();
                string channel = EditorGUILayout.TextField("Channel", action.Channel);
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.FindProperty("channel").stringValue = channel;
                    serializedObject.FindProperty("useDefaultChannel").boolValue = false;
                }

                EditorGUILayout.HelpBox("Actions on different channels can run together. Empty channel allows independent actions. Grab uses carry by default.", MessageType.Info);
                Field("useScope");
                if (serializedObject.FindProperty("useScope").enumValueIndex == (int)InteractionUseScope.Group)
                    Field("useGroup");
                EditorGUI.BeginChangeCheck();
                int count = EditorGUILayout.IntField("Max Participants", action.MaxParticipants);
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.FindProperty("maxParticipants").intValue = Mathf.Max(1, count);
                    serializedObject.FindProperty("useDefaultParticipants").boolValue = false;
                }

                Field("conditionMatch");
                Field("conditions");
                Field("costTiming");
                Field("requireRangeWhileRunning");
                Field("requireVisibilityWhileRunning");
                Field("timeout");
                Field("approachPoint");
                Field("approachDistance");
                Field("approachAngle");

                preset = (InteractionPreset)EditorGUILayout.ObjectField("Copy settings from preset", preset, typeof(InteractionPreset), false);
                if (preset && GUILayout.Button("Apply action preset"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(action, "Apply action preset");
                    preset.ApplyTo(action);
                    EditorUtility.SetDirty(action);
                    serializedObject.Update();
                }
            }

            feedback = EditorGUILayout.Foldout(feedback, "Feedback events", true);
            if (feedback)
            {
                Field("onStarted");
                Field("onCommitted");
                Field("onProgress");
                Field("onCompleted");
                Field("onCancelled");
            }

            serializedObject.ApplyModifiedProperties();
            if (action is GrabAction grab && grab.Body && grab.Body.isKinematic)
                EditorGUILayout.HelpBox("Grab requires a dynamic Rigidbody. Turn off Is Kinematic. A socket temporarily makes an inserted item kinematic.", MessageType.Warning);
            if (action is GrabAction configuredGrab && configuredGrab.GripPoint && configuredGrab.GripPoint.GetComponentInParent<Rigidbody>() != configuredGrab.Body)
                EditorGUILayout.HelpBox("Grip Point must belong to this item's Rigidbody. Move it under the held body or clear it to use the root (Authored Pose) or centre of mass (Preserve Pickup Pose).", MessageType.Error);
            if (action is GrabAction anchoredGrab && anchoredGrab.HoldPoint && anchoredGrab.HoldPoint.IsChildOf(anchoredGrab.transform))
                EditorGUILayout.HelpBox("Hold Point must be outside the held item. Clear it to use Actor Hand Anchor or View; use Grip Point for a point on the item.", MessageType.Error);
            if (action is GrabAction referencedGrab && referencedGrab.MovementReference &&
                (referencedGrab.MovementReference == referencedGrab.transform || referencedGrab.MovementReference.IsChildOf(referencedGrab.transform)))
                EditorGUILayout.HelpBox("Movement Reference must move the rig, not the held body. Do not assign this item or its children. Clear the reference to use automatic PlayerRoot / hold-frame resolution.", MessageType.Error);
            if (action is MechanicalAction mechanical && mechanical.Motion == MechanicalMotion.Linear && mechanical.Maximum <= mechanical.Minimum)
                EditorGUILayout.HelpBox("Set Maximum above Minimum to give the handle a visible drag distance. Axis is local to Moving Part; input uses the Analog gesture.", MessageType.Warning);
            if (action.Execution == InteractionExecutionMode.AnimationEvent)
                EditorGUILayout.HelpBox("Add Interaction Animation Bridge on the Animator object. Put CommitInteraction at the clip's contact frame. Set Timeout to handle a missing animation event.", MessageType.Info);
            if (action is CooperativeAction cooperative && cooperative.MaxParticipants < cooperative.MinimumParticipants)
                EditorGUILayout.HelpBox("Max Participants must be at least Minimum Participants.", MessageType.Error);
        }
    }

    [CustomEditor(typeof(InteractionTarget))]
    public sealed class InteractionTargetEditor : UnityEditor.Editor
    {
        private bool persistence, outlineSettings;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            foreach (string name in new[]
            {
                "displayName",
                "focusPoint",
                "focusPriority",
                "extraFocusPoints"
            }

            )
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("highlightWhenFocused"), new GUIContent("Focus Outline"));
            outlineSettings = EditorGUILayout.Foldout(outlineSettings, "Advanced outline geometry", true);
            if (outlineSettings)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("outlineRenderers"), new GUIContent("Outline Renderers (optional)"), true);
            var item = (InteractionTarget)target;
            bool usable = false;
            foreach (var collider in item.GetComponentsInChildren<Collider>())
                if (collider.enabled && collider.GetComponentInParent<InteractionTarget>() == item)
                    usable = true;
            if (!usable)
                EditorGUILayout.HelpBox("Add an enabled Collider to this object or a child. Enable Include Triggers on Actor only when trigger volumes should be selectable.", MessageType.Error);
            var actions = item.GetComponents<InteractionAction>();
            if (actions.Length == 0)
                EditorGUILayout.HelpBox("Add Door Action, Grab Action, Event Action or another Interaction Action to this object.", MessageType.Warning);
            var ids = new HashSet<string>();
            foreach (var action in actions)
                if (!ids.Add(action.ActionId))
                    EditorGUILayout.HelpBox("Duplicate Action ID: " + action.ActionId + ". Set distinct IDs under each action's Advanced settings before using ID-based calls or saving.", MessageType.Warning);
            persistence = EditorGUILayout.Foldout(persistence, "Persistence / network identity", true);
            if (persistence)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("persistentId"));
                if (GUILayout.Button("Assign a new unique ID"))
                    serializedObject.FindProperty("persistentId").stringValue = System.Guid.NewGuid().ToString("N");
                EditorGUILayout.HelpBox("Keep existing IDs when updating saved content. Give new prefab instances their own IDs. Runtime spawns receive IDs from your world/spawn data.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
