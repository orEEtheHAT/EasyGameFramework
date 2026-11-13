using EasyInteraction;
using UnityEngine;

namespace EasyLocalization.Integrations
{
    [AddComponentMenu("EasyGameFramework/Localization/Interaction Translations")]
    public sealed class InteractionTranslations : LocalizedCatalogAdapter, IInteractionTextProvider
    {
        [Tooltip("Assign this component to the presenter's Text Provider. This optional reference refreshes its visible prompt after translations change.")]
        public InteractionPresenter presenter;

        protected override void OnTranslationsChanged()
        {
            base.OnTranslationsChanged();
            if (this && isActiveAndEnabled && presenter) presenter.Refresh();
        }
    }
}
