using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasySave
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Save/Save Controller")]
    public sealed class SaveController : MonoBehaviour
    {
        [SerializeField] private SaveConfiguration configuration;
        [Tooltip("Explicit ISaveParticipant components or ISaveParticipantSource bindings. No scene scanning.")]
        [SerializeField] private MonoBehaviour[] participants = Array.Empty<MonoBehaviour>();
        private double nextAutoSave;
        private float autoSaveSeconds;
        private string autoSaveSlot;
        public SaveService Service { get; private set; }
        public SaveResult LastResult { get; private set; }
        public event Action<SaveResult> Completed;

        /// <summary>Configure an inactive component before enabling it. The participant array is copied.</summary>
        public void Configure(SaveConfiguration defaults, params MonoBehaviour[] explicitParticipants)
        {
            if (isActiveAndEnabled) throw new InvalidOperationException("Disable Save Controller before configuring it.");
            configuration = defaults ? defaults : throw new ArgumentNullException(nameof(defaults));
            participants = explicitParticipants == null ? Array.Empty<MonoBehaviour>() : (MonoBehaviour[])explicitParticipants.Clone();
        }

        public string ConfigurationError
        {
            get
            {
                if (!configuration) return "Create a Save Configuration asset and assign it here.";
                if (configuration.ConfigurationError != null) return configuration.ConfigurationError;
                if (participants == null) return "Assign an explicit participant list (it may be empty).";
                for (int i = 0; i < participants.Length; i++)
                {
                    if (!participants[i]) return "Participant " + i + " is missing. Assign an owner or bindings component.";
                    if (participants[i] is ISaveParticipantSource source)
                    {
                        if (source.ConfigurationError != null) return source.ConfigurationError;
                        continue;
                    }
                    if (!(participants[i] is ISaveParticipant current)) return "Participant " + i + " must implement ISaveParticipant or ISaveParticipantSource.";
                    for (int j = 0; j < i; j++)
                        if (participants[j] is ISaveParticipant previous && previous.Scope == current.Scope && previous.Key == current.Key)
                            return "Duplicate participant key in " + current.Scope + ": " + current.Key;
                }
                return null;
            }
        }
        private void OnEnable()
        {
            try
            {
                var error = ConfigurationError; if (error != null) throw new InvalidOperationException(error);
                var entries = new List<ISaveParticipant>();
                foreach (var component in participants)
                    if (component is ISaveParticipantSource source) entries.AddRange(source.CreateParticipants());
                    else entries.Add((ISaveParticipant)component);
                Service = configuration.CreateService();
                Service.RegisterAll(entries.ToArray());
                autoSaveSeconds = configuration.AutoSaveSeconds; autoSaveSlot = configuration.AutoSaveSlot;
                nextAutoSave = Time.unscaledTimeAsDouble + autoSaveSeconds;
            }
            catch (Exception e) { Service?.Dispose(); Service = null; Debug.LogError("EasySave: " + e.Message, this); }
        }
        private void Update()
        {
            if (Service == null || autoSaveSeconds <= 0 || Time.unscaledTimeAsDouble < nextAutoSave) return;
            nextAutoSave = Time.unscaledTimeAsDouble + autoSaveSeconds;
            if (!Service.IsBusy) _ = Track(Service.SaveAsync(autoSaveSlot, displayName: "Autosave"));
        }
        public Task<SaveResult> SaveAsync(string slot, CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.SaveAsync(slot, cancellation: cancellation));
        public Task<SaveResult> LoadAsync(string slot, CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.LoadAsync(slot, cancellation: cancellation));
        public Task<SaveResult> QuickSaveAsync(CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.QuickSaveAsync(cancellation));
        public Task<SaveResult> QuickLoadAsync(CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.QuickLoadAsync(cancellation));
        public Task<SaveResult> SavePreferencesAsync(CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.SavePreferencesAsync(cancellation));
        public Task<SaveResult> LoadPreferencesAsync(CancellationToken cancellation = default) => Service == null ? Inactive() : Track(Service.LoadPreferencesAsync(cancellation));
        private static Task<SaveResult> Inactive() => Task.FromResult(SaveResult.Fail(SaveError.Disposed, "Enable a correctly configured Save Controller first."));
        private async Task<SaveResult> Track(Task<SaveResult> task)
        {
            var session = Service; var result = await task;
            if (this && isActiveAndEnabled && ReferenceEquals(Service, session))
            {
                LastResult = result;
                if (Completed != null) foreach (Action<SaveResult> observer in Completed.GetInvocationList())
                    try { observer(result); } catch (Exception e) { Debug.LogException(e, this); }
            }
            return result;
        }
        private void OnDisable() { Service?.Dispose(); Service = null; }
        // Quitting is too late to guarantee asynchronous persistence. Save and await before the application's quit flow.
        private void OnApplicationQuit() { Service?.CancelCurrent(); }
    }
}
