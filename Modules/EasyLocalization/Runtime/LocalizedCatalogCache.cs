using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyLocalization
{
    /// <summary>Publishes a complete snapshot. Consumers use Changed for both catalog and language updates.</summary>
    [AddComponentMenu("EasyGameFramework/Localization/Localized Catalog Cache")]
    public sealed class LocalizedCatalogCache : MonoBehaviour
    {
        public LocalizedCatalog catalog;
        private LocalizedCatalog subscribedCatalog;
        private Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        private int generation;
        private int lifetime;
        private long appliedRevision = -1;
        private string appliedLanguage;
        private CancellationTokenSource pending;
        private Task latestOperation;

        public event Action Changed;
        public LocalizedCatalog Catalog
        {
            get => catalog;
            set
            {
                if (ReferenceEquals(catalog, value)) return;
                catalog = value;
                Refresh();
            }
        }

        public bool IsReady => isActiveAndEnabled && Localization.IsCurrentRevision(appliedRevision) &&
            string.Equals(appliedLanguage, Localization.LanguageCode, StringComparison.Ordinal);
        public string Get(string id, string fallback) =>
            IsReady && id != null && values.TryGetValue(id, out var text) ? text : fallback;

        private void OnEnable()
        {
            Localization.Changed += Refresh;
            BindCatalog();
            Refresh();
        }

        private void OnDisable()
        {
            Localization.Changed -= Refresh;
            if (!ReferenceEquals(subscribedCatalog, null)) subscribedCatalog.Changed -= Refresh;
            subscribedCatalog = null;
            lifetime++;
            generation++;
            CancelPending();
            values.Clear();
            appliedRevision = -1;
            appliedLanguage = null;
        }

        private void BindCatalog()
        {
            if (ReferenceEquals(subscribedCatalog, catalog)) return;
            if (!ReferenceEquals(subscribedCatalog, null)) subscribedCatalog.Changed -= Refresh;
            subscribedCatalog = isActiveAndEnabled ? catalog : null;
            if (subscribedCatalog) subscribedCatalog.Changed += Refresh;
        }

        public async void Refresh()
        {
            var operation = StartRefresh();
            int version = generation;
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (this && isActiveAndEnabled && version == generation) Debug.LogException(exception, this); }
        }

        /// <summary>
        /// Requests a snapshot and follows refreshes that supersede it within this enabled lifetime/session.
        /// Delayed language/data notifications do not cancel callers; Disable or Localization.Reset still do.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (!isActiveAndEnabled) return;
            int enabledLifetime = lifetime;
            long sessionVersion = catalog ? Localization.GetSessionVersion() : Localization.LifecycleVersion;
            var operation = StartRefresh();
            while (true)
            {
                try { await operation; }
                catch (OperationCanceledException) when (CanFollow(operation, enabledLifetime, sessionVersion)) { }
                if (!this || !isActiveAndEnabled || lifetime != enabledLifetime || Localization.LifecycleVersion != sessionVersion)
                    throw new OperationCanceledException("The cache lifetime or localization session ended.");
                if (ReferenceEquals(operation, latestOperation)) return;
                operation = latestOperation;
            }
        }

        private bool CanFollow(Task operation, int enabledLifetime, long sessionVersion) =>
            this && isActiveAndEnabled && lifetime == enabledLifetime && Localization.LifecycleVersion == sessionVersion &&
            latestOperation != null && !ReferenceEquals(operation, latestOperation);

        private Task StartRefresh()
        {
            // Store before initialization/Changed can synchronously start another refresh.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            latestOperation = completion.Task;
            _ = CompleteRefreshAsync(completion);
            return completion.Task;
        }

        private async Task CompleteRefreshAsync(TaskCompletionSource<bool> completion)
        {
            try { await RefreshOnceAsync(); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        private async Task RefreshOnceAsync()
        {
            int version = ++generation;
            CancelPending();
            BindCatalog();
            if (!isActiveAndEnabled) return;
            var source = catalog;
            if (!source)
            {
                values.Clear();
                appliedRevision = -1;
                appliedLanguage = null;
                Publish();
                return;
            }
            var request = pending = new CancellationTokenSource();
            var token = request.Token;
            try
            {
                long sessionVersion = Localization.GetSessionVersion();
                await Localization.InitializeForRequestAsync(token);
                token.ThrowIfCancellationRequested();
                while (true)
                {
                    if (!Localization.IsCurrentSession(sessionVersion))
                        throw new OperationCanceledException("The localization session ended.");
                    long revision = Localization.Revision;
                    string language = Localization.LanguageCode;
                    var next = new Dictionary<string, string>(StringComparer.Ordinal);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in source.entries ?? Array.Empty<CatalogTranslation>())
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                            throw new InvalidOperationException("Catalog IDs must be nonempty and unique. Fix the Localized Catalog entries.");
                        var value = await Localization.TextOrNullAsync(entry.text, token);
                        if (value != null) next.Add(entry.id, value);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!this || !isActiveAndEnabled || version != generation || !ReferenceEquals(source, catalog) ||
                        !Localization.IsCurrentSession(sessionVersion))
                        throw new OperationCanceledException("The catalog refresh was superseded.");
                    // Native locale notification can arrive after the selection already committed.
                    // Never publish a mixed/old-language snapshot while waiting for that notification.
                    if (!Localization.IsCurrentRevision(revision) || !string.Equals(language, Localization.LanguageCode, StringComparison.Ordinal))
                        continue;
                    values = next;
                    appliedRevision = revision;
                    appliedLanguage = language;
                    Publish();
                    return;
                }
            }
            finally
            {
                if (ReferenceEquals(pending, request)) pending = null;
                request.Dispose();
            }
        }

        private void Publish()
        {
            var handlers = Changed;
            if (handlers == null) return;
            int version = generation;
            foreach (Action handler in handlers.GetInvocationList())
            {
                if (!this || !isActiveAndEnabled || version != generation) return;
                try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private void CancelPending()
        {
            var previous = pending;
            pending = null;
            previous?.Cancel();
            previous?.Dispose();
        }
    }
}
