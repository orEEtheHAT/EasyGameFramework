using EasyInteraction;

namespace EasySuite.Example
{
    public sealed class EasySuiteOpenAction : InteractionAction
    {
        public EasySuiteLab Lab;
        public bool BuildingCatalog = true;
        protected override bool OnExecute(InteractionActor actor)
        {
            if (!Lab) return false;
            if (BuildingCatalog) Lab.OpenBuildings(); else Lab.ToggleMenu();
            return false;
        }
    }
}
