using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative.UI;
using EasyQuests.Editor;
using UnityEditor;
using UnityEngine;

namespace EasyNarrative.Editor
{
    public sealed partial class DialogueEditor
    {
        private GUIStyle cardStyle;
        private string pressedPort;
        private Vector2 portDown;
        private bool portDragged, panning;
        private int capturedControl;
        private static readonly Vector2 Card = new(Width, Height);

        private void FrameSelection()
        {
            var positions = asset.lines.Where(l => selection.Contains(l.id) || l.id == selected).Select(l => l.position);
            NarrativeGraphEditing.Fit(positions, Card, viewport, ref pan, ref zoom);
            Repaint();
        }

        private void FocusActive()
        {
            var matches = UnityEngine.Object.FindObjectsByType<DialoguePresenter>(FindObjectsSortMode.None)
                .Where(p => p.Conversation?.Definition == asset && p.Conversation.Current != null).ToArray();
            if (matches.Length == 0) { ShowNotification(new GUIContent("No player is on this dialogue.")); return; }
            if (matches.Length == 1) { FocusLine(matches[0].Conversation.Current); return; }
            var menu = new GenericMenu();
            foreach (var presenter in matches)
            {
                var line = presenter.Conversation.Current;
                menu.AddItem(new GUIContent(presenter.name + " · " + line.name), false, () => FocusLine(line));
            }
            menu.ShowAsContext();
        }

        private Rect Box(DialogueLine line) => new((line.position - pan) * zoom, Card * zoom);
        private Rect Port(DialogueLine line)
        {
            var box = Box(line);
            return new Rect(box.xMax - 10, box.center.y - 10, 20, 20);
        }

        private void DrawCanvas(Rect canvas)
        {
            viewport = canvas.size;
            var ev = Event.current;
            bool inside = canvas.Contains(ev.mousePosition);
            GUI.BeginGroup(canvas);
            var mouse = ev.mousePosition;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (ev.type == EventType.ScrollWheel && inside)
            {
                NarrativeGraphEditing.ZoomAt(mouse, ev.delta.y, ref pan, ref zoom);
                ev.Use(); Repaint();
            }
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape && (wiring != null || capturedControl != 0))
            {
                CancelGesture(); ev.Use();
            }
            if (inside && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F && !EditorGUIUtility.editingTextField)
            {
                FrameSelection(); ev.Use();
            }
            if (inside && ev.type == EventType.MouseDown && ev.button == 2)
            {
                CancelGesture(); Capture(control); panning = true; ev.Use();
            }
            if (panning && ev.type == EventType.MouseDrag && ev.button == 2)
            {
                pan -= ev.delta / zoom; ev.Use(); Repaint();
            }

            var byId = new Dictionary<string, DialogueLine>();
            foreach (var line in asset.lines)
                if (line != null && !string.IsNullOrEmpty(line.id)) byId[line.id] = line;
            var captions = new Dictionary<string, List<string>>();
            Handles.BeginGUI();
            foreach (var source in asset.lines)
            {
                if (source == null) continue;
                void Edge(string id, string caption)
                {
                    if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var dest)) return;
                    var a = Port(source).center;
                    var b = new Vector2(Box(dest).xMin, Box(dest).center.y);
                    Handles.DrawBezier(a, b, a + Vector2.right * 55, b + Vector2.left * 55, Color.gray, null, 2);
                    if (source.id == selected && zoom > .5f)
                    {
                        if (!captions.TryGetValue(id, out var labels)) captions.Add(id, labels = new List<string>());
                        labels.Add(caption);
                    }
                }
                Edge(source.next, "Continue");
                foreach (var reply in source.replies)
                {
                    Edge(reply.next, reply.text);
                    if (reply.skill) Edge(reply.failedNext, "Failed · " + reply.text);
                }
            }
            if (byId.TryGetValue(selected ?? "", out var active))
            {
                foreach (var pair in captions)
                {
                    var dest = Box(byId[pair.Key]);
                    var anchor = Vector2.Lerp(Port(active).center, new Vector2(dest.xMin, dest.center.y), .5f);
                    var labels = pair.Value;
                    var caption = labels.Count == 1 ? NarrativeGraphEditing.Short(labels[0]) : labels.Count + " routes";
                    GUI.Label(new Rect(anchor - Vector2.up * 17, new Vector2(150, 18)),
                        new GUIContent(caption, string.Join("\n", labels)), EditorStyles.miniLabel);
                }
            }
            string preview = pressedPort ?? wiring;
            if (preview != null && byId.TryGetValue(preview, out var start))
            {
                Handles.DrawLine(Port(start).center, mouse);
                if (ev.type == EventType.MouseMove || ev.type == EventType.MouseDrag) Repaint();
            }
            Handles.EndGUI();
            foreach (var line in asset.lines)
            {
                if (line == null) continue;
                var color = GUI.backgroundColor;
                if (line.id == selected || selection.Contains(line.id)) GUI.backgroundColor = Color.cyan;
                var box = Box(line);
                string title = (line.id == asset.entry ? "Start · " : "") + line.name;
                var content = new GUIContent(title, line.name + "\n" + line.text);
                if (box.height >= 42f) content.text += "\n" + line.SpeakerName;
                if (box.height >= 57f) content.text += line.replies.Length == 1 ? "\n1 reply" : "\n" + line.replies.Length + " replies";
                cardStyle ??= new GUIStyle(GUI.skin.box) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8,8,3,3) };
                GUI.Box(box, content, cardStyle);
                GUI.backgroundColor = color;
                if (!Application.isPlaying) GUI.Box(Port(line), "›", EditorStyles.miniButton);
            }

            if (inside && ev.type == EventType.MouseDown && ev.button == 0)
            {
                var node = asset.lines.LastOrDefault(l => l != null && Box(l).Contains(mouse));
                var port = asset.lines.LastOrDefault(l => l != null && Port(l).Contains(mouse));
                if (!Application.isPlaying && wiring != null)
                {
                    Connect(wiring, replyPort, failurePort, node?.id ?? port?.id, pan + mouse / zoom);
                    wiring = null; ev.Use();
                }
                else if (!Application.isPlaying && port != null)
                {
                    Capture(control); pressedPort = port.id; portDown = mouse; portDragged = false; ev.Use();
                }
                else if (node != null)
                {
                    if (ev.shift && !selection.Add(node.id)) selection.Remove(node.id);
                    if (!ev.shift && !selection.Contains(node.id)) { selection.Clear(); selection.Add(node.id); }
                    selected = selection.Contains(node.id) ? node.id : selection.FirstOrDefault();
                    if (!Application.isPlaying && selected == node.id)
                    {
                        Capture(control); dragging = node.id; Undo.IncrementCurrentGroup(); dragGroup = Undo.GetCurrentGroup();
                    }
                    ev.Use();
                }
                else { selected = null; selection.Clear(); ev.Use(); }
                Repaint();
            }
            if (capturedControl != 0 && ev.type == EventType.MouseDrag && ev.button == 0)
            {
                if (pressedPort != null) portDragged |= (mouse - portDown).sqrMagnitude > 9;
                else if (dragging != null)
                {
                    Undo.RecordObject(asset, "Move dialogue lines");
                    foreach (var line in asset.lines)
                        if (selection.Contains(line.id)) line.position += ev.delta / zoom;
                    EditorUtility.SetDirty(asset);
                }
                ev.Use(); Repaint();
            }
            if (capturedControl != 0 && ev.rawType == EventType.MouseUp)
            {
                var port = pressedPort;
                bool dropped = portDragged;
                ReleaseMouse();
                if (inside && port != null && byId.TryGetValue(port, out var origin))
                {
                    var node = asset.lines.LastOrDefault(l => l != null && (Box(l).Contains(mouse) || Port(l).Contains(mouse)));
                    string destination = node?.id;
                    Vector2 point = pan + mouse / zoom;
                    PickOutput(origin, (reply, failed) =>
                    {
                        if (dropped) Connect(origin.id, reply, failed, destination, point);
                        else { wiring = origin.id; replyPort = reply; failurePort = failed; }
                        Repaint();
                    });
                }
                ev.Use(); Repaint();
            }
            GUI.EndGroup();
        }

        private static void PickOutput(DialogueLine line, Action<string,bool> pick)
        {
            if (line.replies.Length == 0) { pick(null, false); return; }
            bool fallback = Array.TrueForAll(line.replies, r => r.CanBeLocked);
            if (line.replies.Length == 1 && !line.replies[0].skill && !fallback) { pick(line.replies[0].id, false); return; }
            var menu = new GenericMenu();
            for (int i = 0; i < line.replies.Length; i++)
            {
                var reply = line.replies[i];
                string label = (i + 1) + ". " + (reply.text ?? reply.id).Replace('/', '∕');
                menu.AddItem(new GUIContent(label + (reply.skill ? "/Success" : "")), false, () => pick(reply.id, false));
                if (reply.skill) menu.AddItem(new GUIContent(label + "/Failure"), false, () => pick(reply.id, true));
            }
            if (fallback) menu.AddItem(new GUIContent("When no reply is available"), false, () => pick(null, false));
            menu.ShowAsContext();
        }

        private void Connect(string sourceId, string replyId, bool failed, string destinationId, Vector2 point)
        {
            if (Application.isPlaying || !asset) return;
            var source = Array.Find(asset.lines, l => l.id == sourceId);
            if (source == null) return;
            var reply = replyId == null ? null : Array.Find(source.replies, r => r.id == replyId);
            if (replyId != null && (reply == null || failed && !reply.skill)) return;
            if (destinationId != null && !Array.Exists(asset.lines, l => l.id == destinationId)) return;
            Edit(destinationId == null ? "Connect new dialogue line" : "Connect dialogue lines", () =>
            {
                if (destinationId == null)
                {
                    var line = new DialogueLine { position = point };
                    asset.lines = asset.lines.Append(line).ToArray();
                    destinationId = line.id;
                    selected = line.id; selection.Clear(); selection.Add(selected);
                }
                if (reply == null) source.next = destinationId;
                else if (failed) reply.failedNext = destinationId;
                else reply.next = destinationId;
            });
        }

        private void Capture(int control) { GUIUtility.hotControl = control; capturedControl = control; }
        private void ReleaseMouse()
        {
            if (dragging != null) Undo.CollapseUndoOperations(dragGroup);
            if (capturedControl != 0 && GUIUtility.hotControl == capturedControl) GUIUtility.hotControl = 0;
            capturedControl = 0; dragging = pressedPort = null; portDragged = panning = false;
        }
        private void CancelGesture() { ReleaseMouse(); wiring = null; Repaint(); }
        public override void LostFocus() { if (capturedControl != 0) ReleaseMouse(); }
    }
}
