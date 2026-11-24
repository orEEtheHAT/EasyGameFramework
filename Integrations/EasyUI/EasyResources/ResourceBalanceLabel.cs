using EasyResources;
using TMPro;
using UnityEngine;

namespace EasyUI.Integrations.Resources
{
    [RequireComponent(typeof(TextMeshProUGUI)), AddComponentMenu("EasyUI/Integrations/Resource Balance Label")]
    public sealed class ResourceBalanceLabel : MonoBehaviour
    {
        public ResourceContainer Container;
        public ResourceDefinition Resource;
        [SerializeField, Tooltip("Optional IResourceLabelProvider for translated names/templates. This label is the only component that writes this text.")]
        private MonoBehaviour presentation;
        public MonoBehaviour Presentation { get => presentation; set { presentation = value; BindPresentation(); dirty = true; } }
        private IResourceLabelProvider text;
        private ResourceContainer subscribed;
        private TMP_Text label;
        private bool dirty;
        public int RefreshCount { get; private set; }
        private void OnEnable()
        {
            label = GetComponent<TextMeshProUGUI>();
            if (!label.font || label.font == TMP_Settings.defaultFontAsset) label.font = UIText.ResolveFont(transform);
            BindPresentation();
            Bind(Container, Resource);
        }
        private void BindPresentation()
        {
            if (text != null) text.Changed -= Changed;
            text = isActiveAndEnabled ? presentation as IResourceLabelProvider : null;
            if (text != null) text.Changed += Changed;
        }
        public void Bind(ResourceContainer container, ResourceDefinition resource)
        {
            if (subscribed) subscribed.Changed -= Changed;
            Container = container; Resource = resource; subscribed = isActiveAndEnabled ? Container : null;
            if (subscribed) subscribed.Changed += Changed;
            dirty = true;
        }
        private void Changed() => dirty = true;
        private void LateUpdate()
        {
            if (!dirty) return; dirty = false;
            if (!label) label = GetComponent<TMP_Text>();
            label.text = Container && Resource ? text?.Format(Resource, Container.GetAvailable(Resource))
                ?? Resource.DisplayName + "  " + Resource.Format(Container.GetAvailable(Resource)) : "Assign Container and Resource";
            RefreshCount++;
        }
        private void OnDisable() { if (subscribed) subscribed.Changed -= Changed; subscribed = null; if (text != null) text.Changed -= Changed; text = null; }
    }
}
