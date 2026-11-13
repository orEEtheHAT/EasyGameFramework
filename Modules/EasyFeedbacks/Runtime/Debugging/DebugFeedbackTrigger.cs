using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyFeedbacks
{

/// <summary>
/// Development helper that launches a FeedbackEmitter when an Input System action is performed.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Debug/Debug Feedback Trigger")]
[DisallowMultipleComponent]
[RequireComponent(typeof(FeedbackEmitter))]
public sealed class DebugFeedbackTrigger : MonoBehaviour
{
    [SerializeField, Tooltip("FeedbackEmitter invoked when the input action is performed. Uses the emitter on this GameObject when empty.")]
    private FeedbackEmitter emitter;

    [SerializeField, Tooltip("New Input System action used to trigger the feedback.")]
    private InputActionReference inputAction = null;

    [SerializeField, Tooltip("Enables the input action when needed and disables it again only if this component enabled it.")]
    private bool enableActionAutomatically = true;

    [SerializeField, Tooltip("When enabled, the trigger runs only in the Editor or a development build.")]
    private bool developmentOnly = true;

    private InputAction subscribedAction;
    private bool enabledAction;

    public FeedbackEmitter Emitter => emitter;
    public InputActionReference InputAction => inputAction;
    public bool EnableActionAutomatically => enableActionAutomatically;
    public bool DevelopmentOnly => developmentOnly;

    private void Reset()
    {
        emitter = GetComponent<FeedbackEmitter>();
    }

    private void OnEnable()
    {
        if (developmentOnly && !Application.isEditor && !Debug.isDebugBuild)
        {
            return;
        }

        if (emitter == null)
        {
            emitter = GetComponent<FeedbackEmitter>();
        }

        subscribedAction = inputAction != null ? inputAction.action : null;
        if (subscribedAction == null)
        {
            return;
        }

        subscribedAction.performed += OnActionPerformed;
        if (enableActionAutomatically && !subscribedAction.enabled)
        {
            subscribedAction.Enable();
            enabledAction = true;
        }
    }

    private void OnDisable()
    {
        if (subscribedAction == null)
        {
            return;
        }

        subscribedAction.performed -= OnActionPerformed;
        if (enabledAction)
        {
            subscribedAction.Disable();
        }

        subscribedAction = null;
        enabledAction = false;
    }

    private void OnValidate()
    {
        if (emitter == null)
        {
            emitter = GetComponent<FeedbackEmitter>();
        }
    }

    private void OnActionPerformed(InputAction.CallbackContext context)
    {
        if (emitter != null)
        {
            emitter.Play();
        }
    }
}
}
