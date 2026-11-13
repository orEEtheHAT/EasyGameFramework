using System;
using UnityEngine;

namespace EasyBuilding.Cells
{
    [Serializable]
    public sealed class BuildingCellType
    {
        [Tooltip("Stable save ID. Do not change it after shipping saves.")] public string Id;
        public string Name;
        public Color Color = Color.white;
        public BuildingCost[] Costs = Array.Empty<BuildingCost>();
    }
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Cell Palette", fileName = "Cell Palette")]
    public sealed class BuildingCellPalette : ScriptableObject
    {
        [Tooltip("Zero means empty. Entry 0 is cell value 1, entry 1 is value 2, and so on.")]
        public BuildingCellType[] Types = Array.Empty<BuildingCellType>();
        public int Find(string id)
        {
            if (Types != null) for (int i = 0; i < Types.Length; i++) if (Types[i] != null && Types[i].Id == id) return i + 1;
            return 0;
        }
        public string ConfigurationError
        {
            get
            {
                if (Types == null || Types.Length > ushort.MaxValue) return "A cell palette supports at most 65535 nonempty cell types.";
                var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var type in Types)
                {
                    if (type == null || string.IsNullOrWhiteSpace(type.Id) || !ids.Add(type.Id)) return "Cell type IDs must be distinct and nonempty.";
                    if (type.Costs != null) foreach (var cost in type.Costs)
                        if (string.IsNullOrWhiteSpace(cost.ResourceId) || cost.Amount < 0) return "Cell costs need a resource ID and a nonnegative amount.";
                }
                return null;
            }
        }
    }
}
