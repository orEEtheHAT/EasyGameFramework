using System;
using System.Collections.Generic;
using EasyResources;
using UnityEngine;

namespace EasyQuests
{
    public sealed partial class QuestWorld
    {
        [Tooltip("Player wallet for resource objectives and graph rewards.")] public ResourceContainer wallet;
        private ResourceStore observed;
        private bool retryPayouts;
        private readonly HashSet<string> pending = new();
        private readonly List<string> payoutBatch = new();

        private QuestSession CreateSession()
        {
            var created = new QuestSession(database, quests, r => wallet ? wallet.GetAvailable(r) : 0);
            if (isActiveAndEnabled && Application.isPlaying) ConnectWallet();
            created.Transitioned += ObserveEnding;
            created.Restored += RebuildPayouts;
            return created;
        }

        public void SetWallet(ResourceContainer value)
        {
            wallet = value;
            if (isActiveAndEnabled && session != null) SynchronizeWallet();
        }
        private void OnEnable() { if (session != null) SynchronizeWallet(); }
        private void SynchronizeWallet()
        {
            ConnectWallet();
            session.RefreshWalletBalances();
            RebuildPayouts();
        }
        private void OnDisable() => DisconnectWallet();
        private void DisconnectWallet()
        {
            if (observed == null) return;
            observed.DetailedChanged -= WalletChanged;
            observed.Changed -= SchedulePendingPayouts;
            observed = null;
        }

        // Limit-only wallet changes have no per-resource rows.
        private void SchedulePendingPayouts()
        {
            if (pending.Count > 0) retryPayouts = true;
        }
        private void ConnectWallet()
        {
            var store = wallet ? wallet.Store : null;
            if (observed == store) return;
            DisconnectWallet();
            observed = store;
            if (observed != null)
            {
                observed.DetailedChanged += WalletChanged;
                observed.Changed += SchedulePendingPayouts;
            }
        }

        private void WalletChanged(ResourceChange change)
        {
            if (change.Kind == ResourceChangeKind.Restore || change.Kind == ResourceChangeKind.Replica) return;
            Session.ObserveWalletChange(change);
        }

        private void ObserveEnding(QuestTransition t)
        {
            if (t.Kind != QuestTransitionKind.QuestCompleted || t.Step.rewardMode != QuestRewardMode.Automatic) return;
            pending.Add(t.Run.Id);
            retryPayouts = true;
        }
        private void RebuildPayouts()
        {
            pending.Clear();
            foreach (var run in Session.Journal)
                if (run.RewardPending && run.Current.rewardMode == QuestRewardMode.Automatic) pending.Add(run.Id);
            retryPayouts = pending.Count > 0;
        }
        private void LateUpdate()
        {
            if (!retryPayouts) return;
            retryPayouts = false;
            payoutBatch.Clear(); payoutBatch.AddRange(pending);
            foreach (var quest in payoutBatch)
            {
                var run = Session.Find(quest);
                if (run == null || !run.RewardPending || Grant(run)) pending.Remove(quest);
            }
        }

        public bool TryCollect(QuestDefinition quest, string endingId)
        {
            if (!Session.Contains(quest)) return false;
            return TryCollect(quest.id, endingId);
        }

        public bool TryCollect(string instanceId, string endingId)
        {
            var run = Session.Find(instanceId);
            return run != null && run.RewardPending && run.Current.id == endingId &&
                run.Current.rewardMode == QuestRewardMode.AtInteraction && Grant(run);
        }
        private bool Grant(QuestRun run)
        {
            var profile = run.Current.rewardProfile;
            if (!wallet || !profile)
            {
                run.LastRewardFailure = !wallet ? "Assign the quest world's wallet." : "Assign the ending's reward profile.";
                return false;
            }
            return Session.TryClaimReward(run.Id, () =>
            {
                bool accepted = wallet.TryGrant(profile.Roll(new System.Random(run.RewardSeed)), out var failure);
                run.LastRewardFailure = accepted ? null : failure.ToString();
                return accepted;
            });
        }

        public bool TryTurnIn(string instanceId, QuestGoal goal) =>
            wallet && Session.TryTurnIn(Session.Find(instanceId),goal,wallet.Store);

        public bool TryTurnIn(QuestDefinition quest, QuestGoal goal)
        {
            if (!wallet || !Session.Contains(quest) || goal == null || !goal.resource) return false;
            return Session.TryTurnIn(quest, goal, wallet.Store);
        }
    }
}
