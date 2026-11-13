using EasyFeedbacks;
using UnityEngine;

namespace EasyFootsteps
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Movement/Footstep Surface")]
    public sealed class FootstepSurface : MonoBehaviour
    {
        [SerializeField] private FeedbackPreset feedback;
        public FeedbackPreset Feedback => feedback;
    }
}
