using System;
using EasyLocalization;
using EasySettings.UI;
using UnityEngine;

namespace EasySettings.Integrations
{
    [RequireComponent(typeof(SettingsWindow)), AddComponentMenu("EasyGameFramework/Settings/Localized Settings Text")]
    public sealed class LocalizedSettingsText : MonoBehaviour, ISettingsTextProvider
    {
        public LocalizedCatalogCache Cache;
        LocalizedCatalogCache subscribed;
        SettingsWindow window;
        public event Action Changed;
        public string GetText(string key, string fallback) => Cache ? Cache.Get(key, fallback) : fallback;
        void OnEnable() { window = GetComponent<SettingsWindow>(); Bind(Cache); window.TextProvider = this; }
        void OnDisable() { if (subscribed) subscribed.Changed -= Refresh; subscribed = null; if (window && ReferenceEquals(window.TextProvider, this)) window.TextProvider = null; }
        public void Bind(LocalizedCatalogCache cache) { if (subscribed) subscribed.Changed -= Refresh; Cache = cache; subscribed = isActiveAndEnabled ? Cache : null; if (subscribed) subscribed.Changed += Refresh; Refresh(); }
        void Refresh() => Changed?.Invoke();
    }
}
