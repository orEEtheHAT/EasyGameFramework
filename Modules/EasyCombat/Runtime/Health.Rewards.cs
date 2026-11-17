using System;
using EasyResources;
using UnityEngine;
using WalletCost = EasyResources.ResourceCost;

namespace EasyCombat
{
    public sealed partial class Health
    {
        [SerializeField, Tooltip("Grant the configured resource reward once on confirmed death. Disabled by default.")]
        private bool rewardEnabled;
        [SerializeField] private ResourceRewardProfile rewardProfile;
        [SerializeField, Tooltip("Explicit recipient wallet. No automatic killer or player lookup.")]
        private ResourceContainer rewardRecipient;
        private sealed class DeathRewardState
        {
            internal long HandledLife;
            internal bool Handling;
            internal System.Random Random;
        }
        private DeathRewardState deathRewardState;
        private CombatSignal<DeathResourceRewardResult> rewardCompleted;
        public bool RewardEnabled
        {
            get => rewardEnabled;
            set
            {
                if (rewardEnabled == value) return;
                rewardEnabled = value;
                if (value && Application.isPlaying) ObserveEnabledReward();
            }
        }
        public ResourceRewardProfile RewardProfile { get => rewardProfile; set => rewardProfile = value; }
        public ResourceContainer RewardRecipient { get => rewardRecipient; set => rewardRecipient = value; }
        public DeathResourceRewardResult LastRewardResult { get; private set; }
        public event Action<DeathResourceRewardResult> RewardCompleted
        {
            add { if (rewardCompleted == null) rewardCompleted = new CombatSignal<DeathResourceRewardResult>(); rewardCompleted.Add(value); }
            remove { rewardCompleted?.Remove(value); }
        }

        /// <summary>Assigns the explicit reward and destination, then enables death rewards. Null RNG uses Resources' default.</summary>
        public void ConfigureDeathReward(ResourceRewardProfile profile, ResourceContainer recipient, System.Random random = null)
        {
            if (deathRewardState != null && deathRewardState.Handling)
                throw new InvalidOperationException("Configure the death reward between death callbacks.");
            rewardProfile = profile; rewardRecipient = recipient;
            if (random != null || deathRewardState != null) EnsureDeathRewardState().Random = random;
            RewardEnabled = true;
        }
        private DeathRewardState EnsureDeathRewardState()
            => deathRewardState ?? (deathRewardState = new DeathRewardState());
        private void ObserveEnabledReward()
        {
            if (rewardEnabled && LifeState == CombatLifeState.Dead)
                EnsureDeathRewardState().HandledLife = Identity.Id;
        }
        // Called directly from both restore paths before external observers, including on disabled Health.
        private void RestoreRewardBoundary()
        {
            if (deathRewardState != null) deathRewardState.HandledLife = lifeState == CombatLifeState.Dead ? identity.Id : 0;
            else if (rewardEnabled && lifeState == CombatLifeState.Dead) EnsureDeathRewardState().HandledLife = identity.Id;
        }
        private readonly struct CapturedDeathReward
        {
            internal readonly DeathRewardState State;
            internal readonly long Life;
            internal readonly ResourceRewardProfile Profile;
            internal readonly ResourceContainer Destination;
            internal readonly System.Random Random;
            internal CapturedDeathReward(DeathRewardState state, long life, ResourceRewardProfile profile,
                ResourceContainer destination, System.Random random)
            { State = state; Life = life; Profile = profile; Destination = destination; Random = random; }
        }
        // Reserve the committed life and configuration before a feedback provider can mutate/pool this actor.
        private CapturedDeathReward CaptureDeathReward()
        {
            if (!this || !rewardEnabled || !isActiveAndEnabled || CombatSaveSystem.IsRestoring || lifeState != CombatLifeState.Dead) return default;
            var state = EnsureDeathRewardState();
            long life = identity.Id;
            if (state.Handling || state.HandledLife == life) return default;
            state.HandledLife = life;
            state.Handling = true;
            return new CapturedDeathReward(state, life, rewardProfile, rewardRecipient, state.Random);
        }
        // Retains the duplicate-source guard used by lifecycle regression tests.
        private void ProcessDeathReward() => ProcessCommittedDeathReward(CaptureDeathReward());
        private void ProcessCommittedDeathReward(in CapturedDeathReward captured)
        {
            var state = captured.State;
            if (state == null) return;
            var destination = captured.Destination;
            var life = captured.Life;
            WalletCost rolled = null;
            DeathResourceRewardResult result;
            try
            {
                if (!captured.Profile) throw new InvalidOperationException("Assign a Resource Reward profile.");
                if (!destination) throw new InvalidOperationException("Assign the recipient ResourceContainer.");
                rolled = captured.Profile.Roll(captured.Random);
                bool granted = destination.TryGrant(rolled, out var failure, new ResourceOperationContext("combat-death-reward"));
                result = new DeathResourceRewardResult(life, granted ? DeathResourceRewardStatus.Granted : DeathResourceRewardStatus.Rejected,
                    destination, rolled, failure, granted ? null : failure.ToString());
            }
            catch (ArgumentException error)
            { result = new DeathResourceRewardResult(life, DeathResourceRewardStatus.InvalidConfiguration, destination, rolled, error: error.Message); }
            catch (InvalidOperationException error)
            { result = new DeathResourceRewardResult(life, DeathResourceRewardStatus.InvalidConfiguration, destination, rolled, error: error.Message); }
            catch (OverflowException error)
            { result = new DeathResourceRewardResult(life, DeathResourceRewardStatus.InvalidConfiguration, destination, rolled, error: error.Message); }
            catch (Exception error)
            {
                // Configured RNG/extension code must not suppress Health's public death notification.
                result = new DeathResourceRewardResult(life, DeathResourceRewardStatus.InvalidConfiguration, destination, rolled, error: error.Message);
                Debug.LogException(error);
            }
            finally { state.Handling = false; }
            LastRewardResult = result;
            rewardCompleted?.Publish(result);
        }
    }
}
