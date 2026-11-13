using UnityEngine;
using UnityEngine.InputSystem;
using EasyResources;

namespace EasyBuilding.Samples
{
    public sealed class BuildingSample : MonoBehaviour
    {
        public BuildingSystem System;
        public ResourceContainer Wallet;
        public ResourceDefinition Wood;
        public BuildingDefinition Free;
        public BuildingDefinition FreeInZone;
        public BuildingDefinition GridInZone;
        public BuildingDefinition Socket;
        public BuildingDefinition Paid;
        private InputAction free, zone, grid, socket, paid;
        private GUIContent label = new GUIContent();
        private long lastWood = -1;
        private GUIStyle labelStyle;
        private void Awake()
        {
            free = new InputAction("Free", InputActionType.Button, "<Keyboard>/1");
            zone = new InputAction("Free in zone", InputActionType.Button, "<Keyboard>/2");
            grid = new InputAction("Grid", InputActionType.Button, "<Keyboard>/3");
            socket = new InputAction("Socket", InputActionType.Button, "<Keyboard>/4");
            paid = new InputAction("Paid", InputActionType.Button, "<Keyboard>/5");
        }
        private void OnEnable() { free?.Enable(); zone?.Enable(); grid?.Enable(); socket?.Enable(); paid?.Enable(); }
        private void OnDisable() { free?.Disable(); zone?.Disable(); grid?.Disable(); socket?.Disable(); paid?.Disable(); }
        private void Update()
        {
            if (free.WasPressedThisFrame()) System.Select(Free);
            if (zone.WasPressedThisFrame()) System.Select(FreeInZone);
            if (grid.WasPressedThisFrame()) System.Select(GridInZone);
            if (socket.WasPressedThisFrame()) System.Select(Socket);
            if (paid.WasPressedThisFrame()) System.Select(Paid);
            long wood = Wallet.GetTotal(Wood);
            if (wood != lastWood)
            { lastWood = wood; label.text = "EASY GAME FRAMEWORK — BUILDING\n1: Free   2: Free in blue plot   3: Grid in plot   4: Socket   5: Paid (3 wood)\nWood: " + lastWood + "    |    Select again after each placement."; }
        }
        private void OnDestroy() { free?.Dispose(); zone?.Dispose(); grid?.Dispose(); socket?.Dispose(); paid?.Dispose(); }
        private void OnGUI()
        {
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 8, 8) };
            var previous = GUI.matrix;
            float scale = Mathf.Max(0.6f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Box(new Rect(16, 16, Mathf.Min(850, Screen.width / scale - 32), 96), label, labelStyle);
            GUI.matrix = previous;
        }
    }
}
