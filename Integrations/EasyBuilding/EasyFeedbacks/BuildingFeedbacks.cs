using EasyFeedbacks;
using UnityEngine;

namespace EasyBuilding.Integrations
{
    [RequireComponent(typeof(BuildingSystem)), AddComponentMenu("EasyGameFramework/Building/Integrations/Building Feedbacks")]
    public sealed class BuildingFeedbacks : MonoBehaviour
    {
        public FeedbackPreset Placed;
        public FeedbackPreset Removed;
        [Tooltip("Optional active IFeedbackContextProvider from the owning player. Without it, feedback keeps its original context.")]
        public MonoBehaviour ContextProvider;
        public string ConfigurationError => ContextProvider != null && !(ContextProvider is IFeedbackContextProvider)
            ? "Context Provider must implement IFeedbackContextProvider. Assign the player's feedback provider or leave this field empty." : null;
        private BuildingSystem system;
        private void OnEnable() { system = GetComponent<BuildingSystem>(); system.Placed += OnPlaced; system.Removed += OnRemoved; }
        private void Start() { if (Placed != null) FeedbackService.Prewarm(Placed); if (Removed != null) FeedbackService.Prewarm(Removed); }
        private void OnDisable() { if (system == null) return; system.Placed -= OnPlaced; system.Removed -= OnRemoved; }
        private void OnPlaced(PlacedBuilding building) => Play(Placed, building);
        private void OnRemoved(PlacedBuilding building) => Play(Removed, building);
        /// <summary>Applies player defaults to an already authored context. Providers preserve explicitly targeted devices and other values.</summary>
        public FeedbackContext PrepareContext(FeedbackContext context)
        {
            if (ContextProvider != null && ContextProvider.isActiveAndEnabled && ContextProvider is IFeedbackContextProvider provider) return provider.Prepare(context);
            return context;
        }
        private void Play(FeedbackPreset preset, PlacedBuilding building)
        {
            if (preset != null && building != null) FeedbackService.Play(preset, PrepareContext(new FeedbackContext(building.transform.position, building.transform.rotation)));
        }
    }
}
