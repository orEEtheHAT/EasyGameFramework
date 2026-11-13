using System;
using System.Collections.Generic;
using EasyBuilding.Cells;
using EasyBuilding.Paths;
using EasyBuilding.Voxels;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyBuilding.Samples
{
    /// <summary>Small Input System example of shared cell editing, undo/save, chunk meshes and editable paths.</summary>
    public sealed class BuildingCellPlayground : MonoBehaviour
    {
        public Camera View;
        public BuildingCellWorld Cells;
        public BuildingVoxelRenderer Voxels;
        public BuildingPathMesh Road, Pipe;
        private readonly BuildingCellEdit[] edit = new BuildingCellEdit[1];
        private readonly List<Vector3> path = new List<Vector3>();
        private BuildingCellStroke lastStroke;
        private int value = 1, mode;
        private string status = "Left click adds a block. Right click removes it.";
        private string snapshot;
        private GUIStyle label;
        private void Start()
        {
            var initial = new List<BuildingCellEdit>();
            for (int x = -5; x < 6; x++) for (int z = -4; z < 5; z++) initial.Add(new BuildingCellEdit(new Vector3Int(x, 0, z), 1));
            for (int y = 1; y < 4; y++) for (int x = -3; x <= 3; x++) if (x != 0 || y > 2) initial.Add(new BuildingCellEdit(new Vector3Int(x, y, 3), y == 3 ? 3 : 2));
            Cells.TryApply(initial, out _, out status);
            status = "Cell mode: left click adds; right click removes. 1 / 2 / 3 changes material.";
        }
        private void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current; if (keyboard == null || mouse == null || View == null) return;
            if (mouse.middleButton.isPressed) View.transform.RotateAround(Vector3.zero, Vector3.up, mouse.delta.ReadValue().x * 0.25f);
            View.fieldOfView = Mathf.Clamp(View.fieldOfView - mouse.scroll.ReadValue().y * 0.02f, 25, 75);
            if (keyboard.digit1Key.wasPressedThisFrame) value = 1;
            if (keyboard.digit2Key.wasPressedThisFrame) value = 2;
            if (keyboard.digit3Key.wasPressedThisFrame) value = 3;
            if (keyboard.tabKey.wasPressedThisFrame) { mode = (mode + 1) % 3; path.Clear(); status = mode == 0 ? "Cell mode" : mode == 1 ? "Road: click points, Enter applies" : "Pipe: click points, Enter applies"; }
            if (keyboard.f5Key.wasPressedThisFrame) { snapshot = JsonUtility.ToJson(Cells.Capture()); status = "Cell snapshot saved in memory. F9 restores it."; }
            if (keyboard.f9Key.wasPressedThisFrame && snapshot != null) { Cells.TryRestore(JsonUtility.FromJson<BuildingCellState>(snapshot), out var reason); status = reason ?? "Cell snapshot restored."; lastStroke = null; }
            if (keyboard.zKey.wasPressedThisFrame)
            {
                if (mode == 0) { if (Cells.TryUndo(lastStroke, out var reason)) { lastStroke = null; status = "Undone."; } else status = reason; }
                else if (path.Count > 0) { path.RemoveAt(path.Count - 1); status = "Path points: " + path.Count; }
            }
            var ray = View.ScreenPointToRay(mouse.position.ReadValue());
            if (mode == 0 && Physics.Raycast(ray, out var hit, 200, ~0, QueryTriggerInteraction.Ignore))
            {
                bool add = mouse.leftButton.wasPressedThisFrame, erase = mouse.rightButton.wasPressedThisFrame;
                if (add || erase)
                {
                    var position = Voxels.WorldToCell(hit.point + hit.normal * (erase ? -0.02f : 0.02f)); edit[0] = new BuildingCellEdit(position, erase ? 0 : value);
                    if (Cells.TryApply(edit, out lastStroke, out var reason)) status = "Cell " + position + (erase ? " removed" : " placed"); else status = reason;
                }
            }
            else if (mode != 0)
            {
                var target = mode == 1 ? Road : Pipe; var plane = new Plane(Vector3.up, target.transform.position);
                if (mouse.leftButton.wasPressedThisFrame && plane.Raycast(ray, out float distance))
                {
                    var point = target.transform.InverseTransformPoint(ray.GetPoint(distance));
                    if (path.Count == 0 || (point - path[path.Count - 1]).sqrMagnitude > 0.01f) path.Add(point);
                    status = "Path points: " + path.Count + ". Enter applies; Z removes last point.";
                }
                if (keyboard.enterKey.wasPressedThisFrame)
                { status = target.TrySetPoints(path.ToArray(), out var reason) ? "Path rebuilt: " + target.Length.ToString("F1") + " local units" : reason; }
            }
        }
        private void OnGUI()
        {
            if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            var previous = GUI.matrix; float scale = Mathf.Max(0.75f, Screen.height / 720f); float width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Box(new Rect(10, 10, Mathf.Min(1000, width - 20), 100), GUIContent.none);
            GUI.Label(new Rect(22, 18, width - 44, 26), "Cells and paths  |  Tab: cells / road / pipe  |  Middle drag: orbit  |  Wheel: zoom", label);
            GUI.Label(new Rect(22, 46, width - 44, 26), "1–3: material  |  Z: undo  |  F5 / F9: capture / restore cells  |  Enter: apply path", label);
            GUI.Label(new Rect(22, 74, width - 44, 26), status ?? "Ready", label); GUI.matrix = previous;
        }
    }
}
