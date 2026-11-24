using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace EasyUI.Integrations.Localization
{
    [RequireComponent(typeof(TextMeshProUGUI)), AddComponentMenu("EasyUI/Integrations/Localized Window Text")]
    public sealed class LocalizedWindowText : MonoBehaviour
    {
        public LocalizedString Text = new LocalizedString();
        public string Fallback = "";
        private TextMeshProUGUI label;
        private int generation;
        private object[] arguments;
        private CancellationTokenSource pending;
        private void OnEnable()
        {
            label = GetComponent<TextMeshProUGUI>();
            if (!label.font || label.font == TMP_Settings.defaultFontAsset) label.font = UIText.ResolveFont(transform);
            EasyLocalization.Localization.Changed += Refresh;
            Text.StringChanged += StringChanged;
            Refresh();
        }
        private void OnDisable()
        {
            EasyLocalization.Localization.Changed -= Refresh; Text.StringChanged -= StringChanged;
            generation++; pending?.Cancel(); pending?.Dispose(); pending = null;
        }
        private void StringChanged(string _) => Refresh();
        public void SetArguments(params object[] values) { arguments = values; Refresh(); }
        public async void Refresh() => await RefreshAsync();
        public async Task RefreshAsync()
        {
            int version = ++generation;
            pending?.Cancel(); pending?.Dispose(); pending = null;
            if (!isActiveAndEnabled) return;
            var request = pending = new CancellationTokenSource();
            try
            {
                string value = await EasyLocalization.Localization.TextAsync(Text, arguments, Fallback, request.Token);
                if (this && isActiveAndEnabled && version == generation) label.text = value;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this && isActiveAndEnabled && version == generation) Debug.LogException(e, this); }
            finally { if (ReferenceEquals(pending, request)) pending = null; request.Dispose(); }
        }
    }
}
