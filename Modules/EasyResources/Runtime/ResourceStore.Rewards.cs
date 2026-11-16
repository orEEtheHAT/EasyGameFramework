using System;

namespace EasyResources
{
    public sealed partial class ResourceStore
    {
        /// <summary>
        /// Grant a previously rolled reward atomically. Failure leaves every resource unchanged.
        /// Reuse the same snapshot on retry; a successful call is an intentional new grant each time.
        /// </summary>
        public bool TryGrant(ResourceCost reward, out ResourceFailure failure, ResourceOperationContext context = default)
        {
            CheckMutation();
            if (reward == null)
                throw new ArgumentNullException(nameof(reward));
            return Scratch.Clear().Add(this, reward).TryExecute(out failure, context, ResourceChangeKind.Add);
        }
    }
}
