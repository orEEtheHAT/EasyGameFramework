using System;
using System.Collections.Generic;
using EasyBuilding;
using EasyCombat;
using EasyFeedbacks;
using EasyInput;
using EasyInteraction;
using EasyLocalization;
using EasyMovement.FirstPerson;
using EasyResources;

namespace EasySave.Integrations
{
    /// <summary>Production bridges to state owners. Callbacks and owner validation stay on Unity's main thread.</summary>
    public static class EasySaveAdapters
    {
        public static ISaveParticipant Feedbacks(string key = "feedbacks") => new JsonSaveParticipant<FeedbackSettingsState>(key, SaveScope.Preferences, 1,
            FeedbackSettings.Capture, state => Unit(state.MasterVolume) && Unit(state.MusicVolume) && Unit(state.SoundEffectsVolume) && Unit(state.HapticsIntensity)
                ? SaveValidation.Valid : SaveValidation.Invalid("Feedback multipliers must be finite and within 0..1."), FeedbackSettings.Restore);

        /// <summary>Initialize EasyLocalization before registering. Unknown language codes are rejected rather than falling back.</summary>
        public static ISaveParticipant Language(string key = "language") => new JsonSaveParticipant<LanguageState>(key, SaveScope.Preferences, 1,
            EasyLocalization.Localization.CaptureState, state =>
            {
                if (!EasyLocalization.Localization.IsReady) return SaveValidation.Invalid("Await Localization.InitializeAsync before loading language preferences.");
                if (!string.IsNullOrEmpty(state.languageCode)) foreach (var locale in EasyLocalization.Localization.Languages)
                    if (locale && locale.Identifier.Code == state.languageCode) return SaveValidation.Valid;
                return SaveValidation.Invalid("The saved language is unavailable. Initialize Localization and install its locale first.");
            }, async (state, token) => { await EasyLocalization.Localization.RestoreStateAsync(state, token); }, -100);

        /// <summary>Idle ledgers only. Active named reservations need a coordinated participant with their operation owners.</summary>
        public static ISaveParticipant Resources(string key, ResourceStore store, IEnumerable<EasyResources.ResourceDefinition> definitions, int order = -100)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var catalog = new List<EasyResources.ResourceDefinition>(definitions);
            return new JsonSaveParticipant<ResourceContainerState>(key, SaveScope.Progress, 1, () => store.CaptureState(), state =>
            {
                store.ValidateState(state, catalog);
                return SaveValidation.Valid;
            }, state => store.RestoreState(state, catalog), order);
        }

        public static ISaveParticipant Interaction(string key, InteractionTarget target, int order = 20)
        {
            if (!target) throw new ArgumentNullException(nameof(target));
            return new JsonSaveParticipant<InteractionTargetState>(key, SaveScope.Progress, 1, () => target.CaptureState(), state =>
            {
                if (!target) return SaveValidation.Invalid("Interaction target was destroyed.");
                var check = target.ValidateState(state); return check.IsAvailable ? SaveValidation.Valid : SaveValidation.Invalid(check.Reason);
            }, state => { if (!target || !target.TryRestoreState(state, out var result)) throw new InvalidOperationException("Interaction target rejected restore."); }, order);
        }

        public static ISaveParticipant Input(string key, PlayerInputSettings player)
        {
            if (!player) throw new ArgumentNullException(nameof(player));
            return new JsonSaveParticipant<InputSettingsSnapshot>(key, SaveScope.Preferences, 1, player.CaptureSettings,
                state => { if (!player) return SaveValidation.Invalid("Input player was destroyed."); return player.ValidateSettings(state, out var error) ? SaveValidation.Valid : SaveValidation.Invalid(error); },
                state => { if (!player) throw new InvalidOperationException("Input player was destroyed."); if (!player.RestoreSettings(state, out var error)) throw new InvalidOperationException(error); });
        }

        public static ISaveParticipant Movement(string key, FirstPersonCharacterController character, int order = 30)
        {
            if (!character) throw new ArgumentNullException(nameof(character));
            return new JsonSaveParticipant<FirstPersonCharacterState>(key, SaveScope.Progress, 1, character.CaptureState,
                state => { if (!character) return SaveValidation.Invalid("Character was destroyed."); return character.ValidateState(state, out var error) ? SaveValidation.Valid : SaveValidation.Invalid(error); },
                state => { if (!character) throw new InvalidOperationException("Character was destroyed."); if (!character.TryRestoreState(state, out var error)) throw new InvalidOperationException(error); }, order);
        }

        public static ISaveParticipant Combat(string key, CombatDefinitionCatalog catalog, CombatReferenceRegistry references,
            int order = 10)
        {
            if (!catalog || references == null) throw new ArgumentException("Provide a combat catalog and explicit actor registry.");
            return new JsonSaveParticipant<CombatWorldSnapshot>(key, SaveScope.Progress, 1, () =>
            {
                if (!CombatSaveSystem.TryCapture(catalog, references, out var state, out var error)) throw new InvalidOperationException(error); return state;
            }, state => CombatSaveSystem.TryValidate(state, catalog, references, out var error) ? SaveValidation.Valid : SaveValidation.Invalid(error),
                state => { if (!CombatSaveSystem.TryRestore(state, catalog, references, out _, out var error)) throw new InvalidOperationException(error); }, order);
        }

        /// <summary>Full local-world replacement through EasyBuilding, including private history and stable IDs.
        /// Unresolved content is strict by default; opt-in preservation requires an explicit report consumer.</summary>
        public static ISaveParticipant Buildings(string key, BuildingSystem system, IReadOnlyList<BuildingDefinition> definitions,
            bool preserveUnresolved = false, Action<BuildingSnapshotReport> restored = null, int order = 0)
        {
            if (!system || definitions == null) throw new ArgumentException("Provide a building world and definition catalog.");
            if (preserveUnresolved && restored == null) throw new ArgumentException("Preserving unresolved content requires a restore report handler.");
            var catalog = new List<BuildingDefinition>(definitions);
            var options = new BuildingRestoreOptions { PreserveUnknownDefinitions = preserveUnresolved, DeferMissingConnections = preserveUnresolved };
            return new JsonSaveParticipant<BuildingState>(key, SaveScope.Progress, 1, () => system.CaptureState(), state =>
            {
                if (!system || system.IsReadOnly) return SaveValidation.Invalid("Enable a writable local building world before loading.");
                return system.ValidateSnapshot(state, catalog, options, out _, out var error) ? SaveValidation.Valid : SaveValidation.Invalid(error);
            }, state =>
            {
                if (!system) throw new InvalidOperationException("Building world was destroyed.");
                if (!system.TryReplaceSnapshot(state, catalog, options, out var report, out var error)) throw new InvalidOperationException(error);
                restored?.Invoke(report);
            }, order);
        }
        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;

    }
}
