using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyInput;
using EasyInteraction;
using EasyMovement.FirstPerson;
using EasyResources;
using EasySettings;
using UnityEngine;

namespace EasySave.Integrations
{
    /// <summary>Explicit common bindings. Add this component to SaveController's participant list.
    /// World catalogs/registries can additionally be registered through the production adapter factories.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Save/Framework Save Bindings")]
    public sealed class FrameworkSaveBindings : MonoBehaviour, ISaveParticipantSource
    {
        [Serializable] public sealed class PlayerBinding
        {
            [Tooltip("Persistent lowercase ID, independent of scene object name or instance ID.")] public string Id = "player";
            public FirstPersonCharacterController Movement;
            public PlayerInputSettings Input;
        }
        [Serializable] public sealed class WalletBinding { public string Id = "wallet"; public ResourceContainer Container; }
        [Serializable] public sealed class InteractionBinding { public string Id = "workbench"; public InteractionTarget Target; }
        public PlayerBinding[] Players = Array.Empty<PlayerBinding>();
        public WalletBinding[] Wallets = Array.Empty<WalletBinding>();
        public ResourceDefinition[] Resources = Array.Empty<ResourceDefinition>();
        public InteractionBinding[] Interactions = Array.Empty<InteractionBinding>();
        public bool SaveAudioPreferences = true;
        [Tooltip("Call InitializeAsync before preferences capture/load to wait for native locale initialization.")] public bool SaveLanguage;
        public SettingsController Graphics;

        public string ConfigurationError
        {
            get
            {
                if (Players == null || Wallets == null || Interactions == null || Resources == null) return "Binding lists cannot be null.";
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var player in Players)
                    if (player == null || !ValidId(player.Id) || !keys.Add("player/" + player.Id) || !player.Movement && !player.Input)
                        return "Each player needs a unique lowercase stable ID and a Movement or Input owner.";
                foreach (var wallet in Wallets)
                    if (wallet == null || !ValidId(wallet.Id) || !keys.Add("wallet/" + wallet.Id) || !wallet.Container)
                        return "Each wallet needs a unique lowercase stable ID and ResourceContainer.";
                foreach (var interaction in Interactions)
                    if (interaction == null || !ValidId(interaction.Id) || !keys.Add("interaction/" + interaction.Id) || !interaction.Target)
                        return "Each interaction needs a unique lowercase stable ID and InteractionTarget. Configure its own PersistentId/action IDs too.";
                foreach (var resource in Resources) if (!resource) return "The resource catalog has a missing definition.";
                return null;
            }
        }
        public async Task InitializeAsync(CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (SaveLanguage) await EasyLocalization.Localization.InitializeAsync();
            cancellation.ThrowIfCancellationRequested();
        }
        public ISaveParticipant[] CreateParticipants()
        {
            string error = ConfigurationError; if (error != null) throw new InvalidOperationException(error);
            var result = new List<ISaveParticipant>();
            foreach (var player in Players)
            {
                if (player.Movement) result.Add(EasySaveAdapters.Movement("player/" + player.Id + "/movement", player.Movement));
                if (player.Input) result.Add(EasySaveAdapters.Input("player/" + player.Id + "/input", player.Input));
            }
            if (Wallets.Length != 0)
            {
                var keys = new string[Wallets.Length]; var stores = new ResourceStore[Wallets.Length];
                for (int i = 0; i < keys.Length; i++) { keys[i] = Wallets[i].Id; stores[i] = Wallets[i].Container.Store; }
                result.Add(ResourceLedgerSave.Create("world/resources", keys, stores, Resources));
            }
            foreach (var interaction in Interactions) result.Add(EasySaveAdapters.Interaction("world/interaction/" + interaction.Id, interaction.Target));
            if (SaveAudioPreferences) result.Add(EasySaveAdapters.Feedbacks("global/audio"));
            if (SaveLanguage) result.Add(EasySaveAdapters.Language("global/language"));
            if (Graphics) result.Add(SettingsSaveAdapter.Create(Graphics, "global/graphics"));
            return result.ToArray();
        }
        private static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            return true;
        }
    }
}
