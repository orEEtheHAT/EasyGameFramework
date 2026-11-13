using EasyFeedbacks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyMovement.IntegrationTests
{
    public sealed class FootstepContextProviderProbe : MonoBehaviour, IFeedbackContextProvider
    {
        public Gamepad DefaultGamepad, ExplicitGamepad;
        public bool ExplicitNone;
        public int Calls;
        public FeedbackContext Received, Returned;
        public FeedbackContext Prepare(FeedbackContext context)
        {
            Calls++; Received = context;
            if (ExplicitNone) context = context.WithoutGamepad();
            else if (ExplicitGamepad != null) context = context.WithGamepad(ExplicitGamepad);
            return Returned = context.WithDefaultGamepad(DefaultGamepad);
        }
    }

    public sealed class InvalidFootstepContextProvider : MonoBehaviour { }
}
