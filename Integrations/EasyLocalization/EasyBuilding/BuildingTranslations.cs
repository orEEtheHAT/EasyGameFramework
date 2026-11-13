using EasyBuilding;
using UnityEngine;

namespace EasyLocalization.Integrations
{
    [AddComponentMenu("EasyGameFramework/Localization/Building Translations")]
    public sealed class BuildingTranslations : LocalizedCatalogAdapter, IBuildingMessageResolver
    {
        public string GetName(BuildingDefinition definition) => definition
            ? Translate("building." + definition.Id, definition.Name) : "";

        public string Resolve(in PlacementResult result) => Translate("building.result." + result.Failure, result.Reason);
    }
}
