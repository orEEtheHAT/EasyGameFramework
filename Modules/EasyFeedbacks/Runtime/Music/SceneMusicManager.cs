using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFeedbacks
{

public enum SceneMusicManagerActivationMode
{
    ActiveScene,
    WhileLoaded
}

/// <summary>
/// Local manager that declares the base playlist associated with one loaded scene.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Scene Music Manager")]
[DisallowMultipleComponent]
public sealed class SceneMusicManager : MonoBehaviour
{
    [SerializeField, Tooltip("Base playlist offered by this scene.")]
    private MusicPlaylist playlist;

    [SerializeField, Tooltip("Active Scene applies only when this object's scene is active. While Loaded applies for the entire loaded lifetime.")]
    private SceneMusicManagerActivationMode activationMode = SceneMusicManagerActivationMode.ActiveScene;

    [SerializeField, Tooltip("Higher priority wins when several eligible Scene Music Managers are loaded. The most recently registered manager wins ties.")]
    private int priority;

    [SerializeField, Min(-1f), Tooltip("Crossfade duration when this source becomes selected. -1 uses the playlist default.")]
    private float transitionDuration = -1f;

    private bool registered;

    public MusicPlaylist Playlist => playlist;
    public SceneMusicManagerActivationMode ActivationMode => activationMode;
    public int Priority => priority;
    public float TransitionDuration => Mathf.Max(-1f, transitionDuration);

    private void OnEnable()
    {
        TryRegister();
    }

    private void Start()
    {
        playlist?.PreloadAudioData();
        // Covers script execution orders where Scene Music Manager enabled before MusicManager.Awake.
        TryRegister();
    }

    private void OnDisable()
    {
        var manager = MusicManager.Instance;
        if (manager != null)
        {
            manager.UnregisterSceneMusicManager(this);
        }

        registered = false;
    }

    private void OnValidate()
    {
        transitionDuration = Mathf.Max(-1f, transitionDuration);
    }

    internal bool AppliesTo(Scene activeScene)
    {
        return activationMode == SceneMusicManagerActivationMode.WhileLoaded
               || gameObject.scene == activeScene;
    }

    private void TryRegister()
    {
        if (!Application.isPlaying || registered)
        {
            return;
        }

        var manager = MusicManager.Instance;
        if (manager == null)
        {
            return;
        }

        manager.RegisterSceneMusicManager(this);
        registered = true;
    }
}
}
