using TMPro;
using UnityEngine;

namespace EasyUI
{
    [DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
    [AddComponentMenu("EasyGameFramework/UI/Styled Text")]
    public sealed class UIStyledText : MonoBehaviour
    {
        [SerializeField] private UITextRole role = UITextRole.Body;
        [Tooltip("Optional local theme. Reading preferences still come from the nearest Window Manager.")]
        public UITypography themeOverride;
        private TextMeshProUGUI label;
        private WindowManager manager;
        private Material leased;
        public UITextRole Role
        {
            get => role;
            set { role = value; Apply(); }
        }

        private void OnEnable() { Rebind(); }
        private void OnTransformParentChanged() { if (isActiveAndEnabled) Rebind(); }
        private void OnDisable()
        {
            if (manager) manager.TypographyChanged -= Apply;
            manager = null;
            ReleaseMaterial();
        }

        private void Rebind()
        {
            if (manager) manager.TypographyChanged -= Apply;
            manager = GetComponentInParent<WindowManager>(true);
            if (manager) manager.TypographyChanged += Apply;
            Apply();
        }

        /// <summary>Apply the role to this label without replacing its authored font, alignment or wrapping.</summary>
        public void Apply()
        {
            if (!label) label = GetComponent<TextMeshProUGUI>();
            var owner = manager ? manager : GetComponentInParent<WindowManager>(true);
            var theme = themeOverride ? themeOverride : owner && owner.Typography ? owner.Typography : UITypography.Default;
            var prefs = owner ? owner.ReadingPreferences : UIReadingPreferences.Default;
            var style = theme.Get(role);
            float size = Mathf.Max(1, style.size) * prefs.EffectiveScale;
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.fontStyle = style.emphasis;
            label.overrideColorTags = prefs.highContrast;
            label.enableVertexGradient = style.gradient && !prefs.highContrast;
            label.color = label.enableVertexGradient || prefs.highContrast ? Color.white : style.color;
            if (label.enableVertexGradient)
            {
                label.colorGradientPreset = null;
                label.colorGradient = new VertexGradient(style.color, style.color, style.bottomColor, style.bottomColor);
            }
            var finish = prefs.highContrast ? UITextFinish.Ink : style.finish;
            var material = !prefs.highContrast && style.customMaterial
                ? (prefs.reducedMotion ? style.customStillMaterial : style.customMaterial)
                : UITextMaterials.Preset(finish, prefs.reducedMotion);
            ApplyMaterial(material);
        }

        private void ApplyMaterial(Material preset)
        {
            var font = label.font;
            if (!font || !font.material) return;
            var original = font.material;
            ShaderUtilities.GetShaderPropertyIDs();
            Material desired = original, nextLease = null;
            if (preset && preset.shader.isSupported && preset.HasProperty(ShaderUtilities.ID_GradientScale) && original.HasProperty(ShaderUtilities.ID_GradientScale))
            {
                if (preset.mainTexture == original.mainTexture) desired = preset;
                else if (Application.isPlaying && isActiveAndEnabled && original.mainTexture)
                {
                    desired = TMP_MaterialManager.GetFallbackMaterial(preset, original);
                    nextLease = desired;
                }
            }
            if (nextLease != leased && nextLease) TMP_MaterialManager.AddFallbackMaterialReference(nextLease);
            if (label.fontSharedMaterial != desired)
            {
                label.fontSharedMaterial = desired;
                label.UpdateMeshPadding();
            }
            if (leased != nextLease && leased) TMP_MaterialManager.ReleaseFallbackMaterial(leased);
            leased = nextLease;
        }
        private void ReleaseMaterial()
        {
            if (!leased) return;
            if (label && label.font && label.fontSharedMaterial == leased) label.fontSharedMaterial = label.font.material;
            TMP_MaterialManager.ReleaseFallbackMaterial(leased);
            leased = null;
        }
    }

    internal static class UITextMaterials
    {
        private static Material ink, gold, stillGold, beacon;
        public static Material Preset(UITextFinish finish, bool still)
        {
            switch (finish)
            {
                case UITextFinish.Ink: return Load(ref ink, "Ink");
                case UITextFinish.Gilded: return still ? Load(ref stillGold, "Gilded Still") : Load(ref gold, "Gilded");
                case UITextFinish.Beacon: return Load(ref beacon, "Beacon");
                default: return null;
            }
        }
        private static Material Load(ref Material cached, string name)
        {
            if (!cached) cached = Resources.Load<Material>("EasyUI/Typography/" + name);
            return cached;
        }
    }
}
