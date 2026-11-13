using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public interface IBuildingMessageResolver { string Resolve(in PlacementResult result); }
    [Serializable]
    public struct BuildingMessage { public PlacementFailure Failure; [TextArea] public string Text; }
    /// <summary>Optional localization asset. A game's Unity Localization adapter may implement IBuildingMessageResolver instead.</summary>
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Placement Messages", fileName = "Building Messages")]
    public sealed class BuildingMessages : ScriptableObject, IBuildingMessageResolver
    {
        public BuildingMessage[] Messages = Array.Empty<BuildingMessage>();
        private Dictionary<PlacementFailure, string> lookup;
        public string Resolve(in PlacementResult result)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<PlacementFailure, string>();
                if (Messages != null) foreach (var message in Messages) if (!string.IsNullOrWhiteSpace(message.Text)) lookup[message.Failure] = message.Text;
            }
            return lookup.TryGetValue(result.Failure, out var value) ? value : result.Reason;
        }
        public void Refresh() => lookup = null;
        private void OnValidate() => Refresh();
    }
}
