using EasyResources;
using WalletCost = EasyResources.ResourceCost;

namespace EasyCombat
{
    public enum DeathResourceRewardStatus { None, Granted, Rejected, InvalidConfiguration }

    /// <summary>One completed Health death-reward attempt. Reward is null if configuration failed before the single roll.</summary>
    public readonly struct DeathResourceRewardResult
    {
        public long LifeId { get; }
        public DeathResourceRewardStatus Status { get; }
        public ResourceContainer Recipient { get; }
        public WalletCost Reward { get; }
        public ResourceFailure Failure { get; }
        public string Error { get; }
        public bool Granted => Status == DeathResourceRewardStatus.Granted;
        internal DeathResourceRewardResult(long lifeId, DeathResourceRewardStatus status, ResourceContainer recipient,
            WalletCost reward, ResourceFailure failure = default, string error = null)
        { LifeId = lifeId; Status = status; Recipient = recipient; Reward = reward; Failure = failure; Error = error; }
    }
}
