using System;
using System.Collections.Generic;
using EasyBuilding.Cells;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace EasyBuilding.Tilemaps
{
    [Serializable] public struct BuildingTileBinding { public string CellId; public TileBase Tile; }
    /// <summary>Renders a Cell World into an owned Tilemap. Use a dedicated empty Tilemap; its Grid defines rectangular/hex/isometric layout.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Tilemap)), AddComponentMenu("EasyGameFramework/Building/2D/Tilemap Renderer")]
    public sealed class BuildingTilemapRenderer : MonoBehaviour
    {
        public BuildingCellWorld World;
        public Tilemap Target;
        public BuildingTileBinding[] Tiles = Array.Empty<BuildingTileBinding>();
        private BuildingCellWorld subscribed;
        private readonly Dictionary<int, TileBase> mapping = new Dictionary<int, TileBase>();
        private readonly HashSet<Vector3Int> owned = new HashSet<Vector3Int>();
        private readonly List<BuildingCellEdit> cells = new List<BuildingCellEdit>();
        private Vector3Int[] positions = Array.Empty<Vector3Int>();
        private TileBase[] values = Array.Empty<TileBase>();
        private void OnEnable()
        {
            if (Target == null) Target = GetComponent<Tilemap>();
            subscribed = World; if (subscribed == null) return;
            subscribed.Changed += Changed; subscribed.Restored += Rebuild; Rebuild();
        }
        private void OnDisable()
        {
            if (subscribed != null) { subscribed.Changed -= Changed; subscribed.Restored -= Rebuild; } subscribed = null; ClearOwned();
        }
        private void ClearOwned() { if (Target != null) foreach (var position in owned) Target.SetTile(position, null); owned.Clear(); }
        public void Rebuild()
        {
            ClearOwned(); mapping.Clear(); if (World == null || World.Palette == null || Target == null) return;
            foreach (var binding in Tiles) { int value = World.Palette.Find(binding.CellId); if (value != 0) mapping[value] = binding.Tile; }
            World.GetCells(cells); Changed(cells);
        }
        private void Changed(IReadOnlyList<BuildingCellEdit> edits)
        {
            if (Target == null || edits.Count == 0) return;
            if (positions.Length != edits.Count) { positions = new Vector3Int[edits.Count]; values = new TileBase[edits.Count]; }
            for (int i = 0; i < edits.Count; i++)
            {
                var edit = edits[i]; positions[i] = edit.Position; mapping.TryGetValue(edit.Value, out values[i]);
                if (values[i] != null) owned.Add(edit.Position); else owned.Remove(edit.Position);
            }
            Target.SetTiles(positions, values);
        }
        public Vector3Int WorldToCell(Vector3 point) => Target.WorldToCell(point);
    }
}
