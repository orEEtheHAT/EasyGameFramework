using EasyLocalization.Integrations;
using NUnit.Framework;
using UnityEngine;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class LocalizationConfigurationTests
    {
        [Test] public void StandardCatalogProviderUsesFallbackWithoutConfiguredTables()
        {
            var go = new GameObject("Unconfigured prompt localization");
            try
            {
                var provider = go.AddComponent<InteractionTranslations>();
                Assert.AreEqual("Release", provider.Translate("interaction.release", "Release"));
                Assert.DoesNotThrow(provider.Refresh);
                provider.enabled = false;
                Assert.DoesNotThrow(provider.Refresh);
                Assert.AreEqual("Fallback", provider.Translate("", "Fallback"));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
