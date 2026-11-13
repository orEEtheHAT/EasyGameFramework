using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Trigger volume that offers a playlist while the tagged player is inside it.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Music Zone")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public sealed class MusicZone : MonoBehaviour
{
    [SerializeField, Tooltip("Playlist offered while the player is inside this trigger.")]
    private MusicPlaylist playlist;

    [SerializeField, Tooltip("Higher-priority zones win when several zones overlap. The most recently entered zone wins ties.")]
    private int priority;

    [SerializeField, Min(-1f), Tooltip("Crossfade duration when this zone becomes selected. -1 uses the playlist default.")]
    private float transitionDuration = -1f;

    [SerializeField, Tooltip("Only colliders belonging to an object with this tag activate the zone.")]
    private string playerTag = "Player";

    // Player characters commonly use several colliders. A small reusable list keeps
    // those colliders independent while allowing stale entries to be removed in
    // place without per-frame allocations.
    private readonly List<Collider> playerColliders = new List<Collider>(4);
    private MusicManager registeredManager;
    private bool registered;

    public MusicPlaylist Playlist => playlist;
    public int Priority => priority;
    public float TransitionDuration => Mathf.Max(-1f, transitionDuration);

    private void Reset()
    {
        var zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null)
        {
            zoneCollider.isTrigger = true;
        }

        var zoneBody = GetComponent<Rigidbody>();
        if (zoneBody != null)
        {
            zoneBody.isKinematic = true;
            zoneBody.useGravity = false;
        }
    }

    private void OnValidate()
    {
        transitionDuration = Mathf.Max(-1f, transitionDuration);
        if (string.IsNullOrWhiteSpace(playerTag))
        {
            playerTag = "Player";
        }
    }

    private void OnDisable()
    {
        Unregister();
        playerColliders.Clear();
    }

    private void Start()
    {
        // Prepare referenced clips when the scene starts instead of on the
        // first player entry, where decoder initialization would hitch gameplay.
        playlist?.PreloadAudioData();
    }

    private void Update()
    {
        RemoveInvalidPlayerColliders();
        if (playerColliders.Count == 0)
        {
            Unregister();
            return;
        }

        // A persistent zone can outlive the active manager during additive scene
        // changes. Move its registration to the replacement manager when needed.
        var manager = MusicManager.Instance;
        if (registered && (registeredManager == null || registeredManager != manager))
        {
            Unregister();
        }

        if (!registered && manager != null)
        {
            Register();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other) || playerColliders.Contains(other))
        {
            return;
        }

        playerColliders.Add(other);

        if (playerColliders.Count == 1)
        {
            Register();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!playerColliders.Remove(other) || playerColliders.Count > 0)
        {
            return;
        }

        Unregister();
    }

    private void Register()
    {
        if (registered)
        {
            return;
        }

        var manager = MusicManager.Instance;
        if (manager == null)
        {
            Warn($"Music Zone '{name}' needs an active MusicManager.");
            return;
        }

        manager.RegisterZone(this);
        registeredManager = manager;
        registered = true;
    }

    private void Unregister()
    {
        if (!registered)
        {
            return;
        }

        var manager = registeredManager;
        if (manager != null)
        {
            manager.UnregisterZone(this);
        }

        registeredManager = null;
        registered = false;
    }

    private void RemoveInvalidPlayerColliders()
    {
        for (var i = playerColliders.Count - 1; i >= 0; i--)
        {
            var candidate = playerColliders[i];
            if (candidate != null
                && candidate.enabled
                && candidate.gameObject.activeInHierarchy
                && IsPlayerCollider(candidate))
            {
                continue;
            }

            playerColliders.RemoveAt(i);
        }
    }

    private bool IsPlayerCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        if (candidate.CompareTag(playerTag))
        {
            return true;
        }

        var attachedBody = candidate.attachedRigidbody;
        if (attachedBody != null && attachedBody.CompareTag(playerTag))
        {
            return true;
        }

        var root = candidate.transform.root;
        return root != candidate.transform && root.CompareTag(playerTag);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }
}
}
