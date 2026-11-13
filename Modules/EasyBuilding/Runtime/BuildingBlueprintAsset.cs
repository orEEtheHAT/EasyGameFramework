using UnityEngine;
namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Blueprint", fileName = "New Blueprint")]
    public sealed class BuildingBlueprintAsset : ScriptableObject
    {
        public BuildingBlueprint Blueprint = new BuildingBlueprint();
    }
}
