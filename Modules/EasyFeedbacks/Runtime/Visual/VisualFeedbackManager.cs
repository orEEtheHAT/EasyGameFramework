using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.VFX;

namespace EasyFeedbacks
{

/// <summary>
/// Persistent pool for Particle System and VFX prefabs.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Visual Feedback Manager")]
[DisallowMultipleComponent]
public sealed class VisualFeedbackManager : MonoBehaviour
{
    [System.Flags]
    private enum PauseReason
    {
        None = 0,
        Handle = 1 << 0,
        Manager = 1 << 1,
        System = 1 << 2
    }

    [SerializeField, Min(1), Tooltip("Hard limit for pooled visual instances across all resources. When full, the least recently used free instance is replaced; active instances are never evicted.")]
    private int maximumCapacity = 128;

    private readonly List<VisualVoice> allVoices = new List<VisualVoice>();
    private readonly List<VisualVoice> activeVoices = new List<VisualVoice>();
    private readonly Dictionary<UnityEngine.Object, ResourcePool> resourcePools =
        new Dictionary<UnityEngine.Object, ResourcePool>();

    private bool allPaused;
    private bool systemPaused;
    private ulong availabilitySequence;
    private int evictionCount;
    private int rejectedPlays;

    public static VisualFeedbackManager Instance { get; private set; }

    public int ActiveInstances => activeVoices.Count;
    public int AvailableInstances => allVoices.Count - activeVoices.Count;
    public int TotalInstances => allVoices.Count;
    public int MaximumCapacity => maximumCapacity;
    public int ResidentResources => resourcePools.Count;
    public int EvictionCount => evictionCount;
    public int RejectedPlays => rejectedPlays;
    public bool AreAllVisualsPaused => allPaused;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (transform.parent != null)
        {
            Warn("VisualFeedbackManager must be a root object to persist between scenes. It was moved to the scene root at runtime.");
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);
        maximumCapacity = Mathf.Max(1, maximumCapacity);
        SetSystemPaused(FeedbackService.AreAllPaused);
    }

    private void Update()
    {
        var deltaTime = Time.deltaTime;

        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            if (!HasValidInstance(voice))
            {
                RecoverDestroyedVoice(voice);
                continue;
            }

            if (voice.Follow != null)
            {
                ApplyPositionAndRotation(voice, voice.Follow.position, voice.Follow.rotation);
            }
            else if (voice.WasFollowing)
            {
                voice.Follow = null;
                voice.WasFollowing = false;
            }

            if (voice.IsPaused)
            {
                continue;
            }

            if (voice.Type == VisualFeedbackType.ParticleSystem)
            {
                UpdateParticleVoice(voice);
            }
            else
            {
                UpdateVfxVoice(voice, deltaTime);
            }
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            StopAll(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Dispose only instances created by this component. The bootstrap object
        // and unrelated children may still belong to other systems.
        for (var i = 0; i < allVoices.Count; i++)
        {
            var voice = allVoices[i];
            if (voice.GameObject != null)
            {
                voice.GameObject.SetActive(false);
                Destroy(voice.GameObject);
            }
        }

        activeVoices.Clear();
        resourcePools.Clear();
        allVoices.Clear();
    }

    private void OnValidate()
    {
        maximumCapacity = Mathf.Max(1, maximumCapacity);
    }

    public VisualHandle Play(VisualFeedbackEntry entry, FeedbackContext context)
    {
        if (entry == null || !entry.IsEnabled || entry.Resource == null)
        {
            return default;
        }

        var positionMode = FeedbackPositioning.Resolve(entry.PositionMode, context.Positioning);

        return PlayInternal(
            entry.Type,
            entry.Resource,
            entry.Lifetime,
            entry.StopMode,
            entry.VfxOneShotDuration,
            entry.VfxStopTailDuration,
            positionMode,
            entry.PositionOffset,
            entry.RotationOffset,
            entry.ScaleMultiplier,
            context);
    }

    public void Prewarm(VisualFeedbackEntry entry)
    {
        if (entry == null || !entry.IsEnabled || entry.Resource == null || entry.PrewarmCount <= 0)
        {
            return;
        }

        Prewarm(entry.Type, entry.Resource, entry.PrewarmCount);
    }

    public VisualHandle Play(
        ParticleSystem particleSystemPrefab,
        FeedbackContext context,
        VisualFeedbackLifetime lifetime = VisualFeedbackLifetime.OneShot,
        VisualFeedbackStopMode stopMode = VisualFeedbackStopMode.FinishCurrentParticles,
        FeedbackPositionMode positionMode = FeedbackPositionMode.WorldPosition)
    {
        return PlayInternal(
            VisualFeedbackType.ParticleSystem,
            particleSystemPrefab,
            lifetime,
            stopMode,
            0f,
            0f,
            positionMode,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            context);
    }

    public VisualHandle Play(
        VisualEffect visualEffectPrefab,
        FeedbackContext context,
        VisualFeedbackLifetime lifetime = VisualFeedbackLifetime.OneShot,
        float oneShotDuration = 2f,
        VisualFeedbackStopMode stopMode = VisualFeedbackStopMode.FinishCurrentParticles,
        float stopTailDuration = 1f,
        FeedbackPositionMode positionMode = FeedbackPositionMode.WorldPosition)
    {
        return PlayInternal(
            VisualFeedbackType.VFXGraph,
            visualEffectPrefab,
            lifetime,
            stopMode,
            Mathf.Max(0.01f, oneShotDuration),
            Mathf.Max(0f, stopTailDuration),
            positionMode,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            context);
    }

    public void Stop(VisualHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            StopVoice(voice);
        }
    }

    public void Pause(VisualHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            PauseVoice(voice, PauseReason.Handle);
        }
    }

    public void Resume(VisualHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            ResumeVoice(voice, PauseReason.Handle);
        }
    }

    public void StopAll(bool immediate = false)
    {
        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            if (immediate)
            {
                ReleaseVoice(activeVoices[i]);
            }
            else
            {
                StopVoice(activeVoices[i]);
            }
        }
    }

    public void PauseAll()
    {
        if (allPaused)
        {
            return;
        }

        allPaused = true;
        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            if (!HasValidInstance(voice))
            {
                RecoverDestroyedVoice(voice);
                continue;
            }

            PauseVoice(voice, PauseReason.Manager);
        }
    }

    public void ResumeAll()
    {
        if (!allPaused)
        {
            return;
        }

        allPaused = false;
        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            if (!HasValidInstance(voice))
            {
                RecoverDestroyedVoice(voice);
                continue;
            }

            ResumeVoice(voice, PauseReason.Manager);
        }
    }

    internal void SetSystemPaused(bool paused)
    {
        if (systemPaused == paused)
        {
            return;
        }

        systemPaused = paused;
        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            if (!HasValidInstance(voice))
            {
                RecoverDestroyedVoice(voice);
                continue;
            }

            if (paused)
            {
                PauseVoice(voice, PauseReason.System);
            }
            else
            {
                ResumeVoice(voice, PauseReason.System);
            }
        }
    }

    internal bool IsHandleValid(VisualHandle handle)
    {
        return TryGetVoice(handle, out _);
    }

    internal bool IsHandlePlaying(VisualHandle handle)
    {
        return TryGetVoice(handle, out var voice) && !voice.IsPaused;
    }

    private VisualHandle PlayInternal(
        VisualFeedbackType type,
        UnityEngine.Object resource,
        VisualFeedbackLifetime lifetime,
        VisualFeedbackStopMode stopMode,
        float vfxOneShotDuration,
        float vfxStopTailDuration,
        FeedbackPositionMode positionMode,
        Vector3 positionOffset,
        Quaternion rotationOffset,
        Vector3 scaleMultiplier,
        FeedbackContext context)
    {
        if (!isActiveAndEnabled)
        {
            Warn("VisualFeedbackManager cannot play while it is disabled.");
            return default;
        }

        if (resource == null)
        {
            Warn("VisualFeedbackManager cannot play a null visual resource.");
            return default;
        }

        var voice = AcquireVoice(type, resource);
        if (voice == null)
        {
            rejectedPlays++;
            Warn($"VisualFeedbackManager reached Maximum Capacity ({maximumCapacity}). '{resource.name}' was not played.");
            return default;
        }

        voice.Lifetime = lifetime;
        voice.StopMode = stopMode;
        voice.VfxOneShotDuration = Mathf.Max(0.01f, vfxOneShotDuration);
        voice.VfxStopTailDuration = Mathf.Max(0f, vfxStopTailDuration);
        voice.Stopping = false;
        voice.PauseReasons = PauseReason.None;
        voice.PlaybackElapsed = 0f;
        voice.StopElapsed = 0f;
        voice.PlaybackStartFrame = Time.frameCount;
        voice.StopStartFrame = -1;
        voice.ResumeFrame = -1;
        voice.PositionOffset = positionOffset;
        voice.RotationOffset = NormalizeRotation(rotationOffset);
        voice.ScaleMultiplier = ClampScale(scaleMultiplier);
        voice.ContextVisualScale = ClampScale(context.VisualScale * context.Intensity);
        voice.Follow = positionMode == FeedbackPositionMode.FollowTransform
            ? (context.Follow != null ? context.Follow : context.Source)
            : null;
        voice.WasFollowing = voice.Follow != null;

        ApplyTransform(voice, context.WorldPosition, context.Rotation);
        if (voice.Follow != null)
        {
            ApplyPositionAndRotation(voice, voice.Follow.position, voice.Follow.rotation);
        }

        voice.GameObject.SetActive(true);

        if (voice.Type == VisualFeedbackType.ParticleSystem)
        {
            voice.ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            voice.ParticleSystem.Play(true);
        }
        else
        {
            voice.VisualEffect.pause = false;
            voice.VisualEffect.Reinit();
        }

        if (allPaused)
        {
            PauseVoice(voice, PauseReason.Manager);
        }

        if (systemPaused)
        {
            PauseVoice(voice, PauseReason.System);
        }

        return new VisualHandle(this, voice.Id, voice.Generation);
    }

    private VisualVoice AcquireVoice(VisualFeedbackType type, UnityEngine.Object resource)
    {
        resourcePools.TryGetValue(resource, out var resourcePool);
        var voice = TakeAvailableVoice(resourcePool);

        if (voice == null)
        {
            if (allVoices.Count < maximumCapacity)
            {
                voice = CreateVoice(type, resource);
            }
            else
            {
                voice = FindOldestAvailableVoice(resourcePool);
                if (voice == null)
                {
                    return null;
                }

                ReconfigureAvailableVoice(voice, type, resource);
                evictionCount++;
            }
        }
        else if (!HasValidInstance(voice))
        {
            ReconfigureAvailableVoice(voice, type, resource);
        }

        unchecked
        {
            voice.Generation++;
            if (voice.Generation == 0)
            {
                voice.Generation = 1;
            }
        }

        voice.InUse = true;
        activeVoices.Add(voice);
        return voice;
    }

    private void Prewarm(VisualFeedbackType type, UnityEngine.Object resource, int targetCount)
    {
        resourcePools.TryGetValue(resource, out var resourcePool);
        RepairInvalidAvailableVoices(resourcePool);
        resourcePools.TryGetValue(resource, out resourcePool);
        var existingCount = resourcePool != null ? resourcePool.LiveCount : 0;

        while (existingCount < targetCount)
        {
            VisualVoice voice;
            if (allVoices.Count < maximumCapacity)
            {
                voice = CreateVoice(type, resource);
                resourcePool = voice.Pool;
            }
            else
            {
                voice = FindOldestAvailableVoice(resourcePool);
                if (voice == null)
                {
                    break;
                }

                ReconfigureAvailableVoice(voice, type, resource);
                resourcePool = voice.Pool;
                evictionCount++;
            }

            AddAvailableVoice(voice);
            existingCount++;
        }

        if (existingCount < targetCount)
        {
            Warn($"VisualFeedbackManager could not prewarm {targetCount} instances of '{resource.name}' because Maximum Capacity ({maximumCapacity}) was reached.");
        }
    }

    private VisualVoice CreateVoice(VisualFeedbackType type, UnityEngine.Object resource)
    {
        var id = allVoices.Count;
        var voice = new VisualVoice(id);
        ConfigureVoiceInstance(voice, type, resource);
        allVoices.Add(voice);
        voice.GameObject.SetActive(false);
        return voice;
    }

    private void ReconfigureAvailableVoice(
        VisualVoice voice,
        VisualFeedbackType type,
        UnityEngine.Object resource)
    {
        if (voice.Pool != null)
        {
            voice.Pool.Available.Remove(voice);
            voice.Pool.LiveCount--;
            if (voice.Pool.LiveCount == 0)
            {
                resourcePools.Remove(voice.Pool.Resource);
            }
        }

        if (voice.GameObject != null)
        {
            voice.GameObject.SetActive(false);
            Destroy(voice.GameObject);
        }

        ConfigureVoiceInstance(voice, type, resource);
        voice.GameObject.SetActive(false);
    }

    private void ConfigureVoiceInstance(
        VisualVoice voice,
        VisualFeedbackType type,
        UnityEngine.Object resource)
    {
        GameObject gameObject;
        ParticleSystem particleSystem;
        VisualEffect visualEffect;

        if (type == VisualFeedbackType.ParticleSystem)
        {
            var prefab = (ParticleSystem)resource;
            var instance = Instantiate(prefab, transform);
            instance.name = $"Pooled Particle - {resource.name} ({voice.Id + 1})";
            ConfigureParticleInstance(instance);
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject = instance.gameObject;
            particleSystem = instance;
            visualEffect = null;
        }
        else
        {
            var prefab = (VisualEffect)resource;
            var instance = Instantiate(prefab, transform);
            instance.name = $"Pooled VFX - {resource.name} ({voice.Id + 1})";
            instance.Stop();
            instance.pause = true;
            gameObject = instance.gameObject;
            particleSystem = null;
            visualEffect = instance;
        }

        if (!resourcePools.TryGetValue(resource, out var resourcePool))
        {
            resourcePool = new ResourcePool(resource);
            resourcePools.Add(resource, resourcePool);
        }

        resourcePool.LiveCount++;
        voice.Configure(type, resourcePool, gameObject, particleSystem, visualEffect);
    }

    private VisualVoice TakeAvailableVoice(ResourcePool resourcePool)
    {
        if (resourcePool == null)
        {
            return null;
        }

        while (resourcePool.Available.Count > 0)
        {
            var lastIndex = resourcePool.Available.Count - 1;
            var voice = resourcePool.Available[lastIndex];
            resourcePool.Available.RemoveAt(lastIndex);
            if (!voice.InUse && voice.Pool == resourcePool)
            {
                return voice;
            }
        }

        return null;
    }

    private VisualVoice FindOldestAvailableVoice(ResourcePool excludedPool)
    {
        VisualVoice oldest = null;
        for (var i = 0; i < allVoices.Count; i++)
        {
            var candidate = allVoices[i];
            if (candidate.InUse || candidate.Pool == excludedPool)
            {
                continue;
            }

            if (oldest == null || candidate.LastAvailableSequence < oldest.LastAvailableSequence)
            {
                oldest = candidate;
            }
        }

        return oldest;
    }

    private void AddAvailableVoice(VisualVoice voice)
    {
        unchecked
        {
            availabilitySequence++;
            if (availabilitySequence == 0)
            {
                availabilitySequence = 1;
            }
        }

        voice.LastAvailableSequence = availabilitySequence;
        voice.Pool.Available.Add(voice);
    }

    private static bool HasValidInstance(VisualVoice voice)
    {
        return voice.GameObject != null
               && (voice.Type == VisualFeedbackType.ParticleSystem
                   ? voice.ParticleSystem != null
                   : voice.VisualEffect != null);
    }

    private void RepairInvalidAvailableVoices(ResourcePool resourcePool)
    {
        if (resourcePool == null)
        {
            return;
        }

        for (var i = resourcePool.Available.Count - 1; i >= 0; i--)
        {
            var voice = resourcePool.Available[i];
            if (!HasValidInstance(voice))
            {
                RecoverDestroyedVoice(voice);
            }
        }
    }

    private void RecoverDestroyedVoice(VisualVoice voice)
    {
        var type = voice.Type;
        var resource = voice.Resource;

        if (voice.InUse)
        {
            voice.InUse = false;
            activeVoices.Remove(voice);
        }

        voice.Follow = null;
        voice.WasFollowing = false;
        voice.PauseReasons = PauseReason.None;
        voice.Stopping = false;
        voice.PlaybackElapsed = 0f;
        voice.StopElapsed = 0f;
        voice.PlaybackStartFrame = -1;
        voice.StopStartFrame = -1;
        voice.ResumeFrame = -1;

        ReconfigureAvailableVoice(voice, type, resource);
        AddAvailableVoice(voice);
    }

    private void UpdateParticleVoice(VisualVoice voice)
    {
        if ((voice.Lifetime == VisualFeedbackLifetime.OneShot || voice.Stopping)
            && !voice.ParticleSystem.IsAlive(true))
        {
            ReleaseVoice(voice);
        }
    }

    private void UpdateVfxVoice(VisualVoice voice, float deltaTime)
    {
        if (voice.Stopping)
        {
            if (voice.StopElapsed >= voice.VfxStopTailDuration
                || (voice.Lifetime == VisualFeedbackLifetime.OneShot
                    && voice.PlaybackElapsed >= voice.VfxOneShotDuration))
            {
                ReleaseVoice(voice);
                return;
            }
        }
        else if (voice.Lifetime == VisualFeedbackLifetime.OneShot
                 && voice.PlaybackElapsed >= voice.VfxOneShotDuration)
        {
            ReleaseVoice(voice);
            return;
        }

        var clampedDelta = Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, VFXManager.maxDeltaTime));
        var simulationDelta = clampedDelta * Mathf.Max(0f, voice.VisualEffect.playRate);
        if (Time.frameCount != voice.PlaybackStartFrame
            && Time.frameCount != voice.ResumeFrame)
        {
            voice.PlaybackElapsed += simulationDelta;
        }

        if (voice.Stopping)
        {
            if (Time.frameCount != voice.StopStartFrame
                && Time.frameCount != voice.ResumeFrame)
            {
                voice.StopElapsed += simulationDelta;
            }
        }
    }

    private void StopVoice(VisualVoice voice)
    {
        if (!voice.InUse || voice.Stopping)
        {
            return;
        }

        if (!HasValidInstance(voice))
        {
            RecoverDestroyedVoice(voice);
            return;
        }

        if (voice.StopMode == VisualFeedbackStopMode.ClearImmediately || voice.IsPaused)
        {
            ReleaseVoice(voice);
            return;
        }

        voice.Stopping = true;

        if (voice.Type == VisualFeedbackType.ParticleSystem)
        {
            voice.ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (!voice.ParticleSystem.IsAlive(true))
            {
                ReleaseVoice(voice);
            }
        }
        else
        {
            voice.VisualEffect.Stop();
            voice.StopElapsed = 0f;
            voice.StopStartFrame = Time.frameCount;
            if (voice.VfxStopTailDuration <= 0f)
            {
                ReleaseVoice(voice);
            }
        }
    }

    private static void PauseVoice(VisualVoice voice, PauseReason reason)
    {
        if ((voice.PauseReasons & reason) != 0)
        {
            return;
        }

        var wasPaused = voice.IsPaused;

        if (!wasPaused && voice.Lifetime == VisualFeedbackLifetime.OneShot)
        {
            var alreadyFinished = voice.Type == VisualFeedbackType.ParticleSystem
                ? !voice.ParticleSystem.IsAlive(true)
                : voice.PlaybackElapsed >= voice.VfxOneShotDuration;
            if (alreadyFinished)
            {
                return;
            }
        }

        voice.PauseReasons |= reason;
        if (wasPaused)
        {
            return;
        }

        if (voice.Type == VisualFeedbackType.ParticleSystem)
        {
            voice.ParticleSystem.Pause(true);
        }
        else
        {
            voice.VisualEffect.pause = true;
        }

    }

    private static void ResumeVoice(VisualVoice voice, PauseReason reason)
    {
        if ((voice.PauseReasons & reason) == 0)
        {
            return;
        }

        voice.PauseReasons &= ~reason;
        if (voice.IsPaused)
        {
            return;
        }

        if (voice.Type == VisualFeedbackType.ParticleSystem)
        {
            voice.ParticleSystem.Play(true);
            if (voice.Stopping)
            {
                voice.ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
        else
        {
            voice.VisualEffect.pause = false;
            voice.ResumeFrame = Time.frameCount;
        }

    }

    private void ReleaseVoice(VisualVoice voice)
    {
        if (!voice.InUse)
        {
            return;
        }

        if (!HasValidInstance(voice))
        {
            RecoverDestroyedVoice(voice);
            return;
        }

        if (voice.Type == VisualFeedbackType.ParticleSystem)
        {
            voice.ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else
        {
            voice.VisualEffect.Stop();
            voice.VisualEffect.pause = true;
        }

        voice.Follow = null;
        voice.WasFollowing = false;
        voice.PauseReasons = PauseReason.None;
        voice.Stopping = false;
        voice.PlaybackElapsed = 0f;
        voice.StopElapsed = 0f;
        voice.PlaybackStartFrame = -1;
        voice.StopStartFrame = -1;
        voice.ResumeFrame = -1;
        voice.Transform.SetParent(transform, false);
        voice.Transform.SetLocalPositionAndRotation(Vector3.zero, voice.OriginalLocalRotation);
        voice.Transform.localScale = voice.OriginalLocalScale;
        voice.GameObject.SetActive(false);
        voice.InUse = false;
        activeVoices.Remove(voice);

        AddAvailableVoice(voice);
    }

    private bool TryGetVoice(VisualHandle handle, out VisualVoice voice)
    {
        if (handle.Owner != this)
        {
            voice = null;
            return false;
        }

        var instanceId = handle.InstanceId;
        if (instanceId >= 0 && instanceId < allVoices.Count)
        {
            voice = allVoices[instanceId];
            if (voice.InUse && voice.Generation == handle.Generation)
            {
                if (HasValidInstance(voice))
                {
                    return true;
                }

                RecoverDestroyedVoice(voice);
            }
        }

        voice = null;
        return false;
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        var isZero = Mathf.Approximately(rotation.x, 0f)
                     && Mathf.Approximately(rotation.y, 0f)
                     && Mathf.Approximately(rotation.z, 0f)
                     && Mathf.Approximately(rotation.w, 0f);
        return isZero ? Quaternion.identity : rotation;
    }

    private static Vector3 ClampScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0f, scale.x),
            Mathf.Max(0f, scale.y),
            Mathf.Max(0f, scale.z));
    }

    private static void ApplyTransform(VisualVoice voice, Vector3 worldPosition, Quaternion worldRotation)
    {
        ApplyPositionAndRotation(voice, worldPosition, worldRotation);
        voice.Transform.localScale = Vector3.Scale(
            Vector3.Scale(voice.OriginalLocalScale, voice.ScaleMultiplier),
            voice.ContextVisualScale);
    }

    private static void ApplyPositionAndRotation(
        VisualVoice voice,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        var contextRotation = NormalizeRotation(worldRotation);
        var position = worldPosition + contextRotation * voice.PositionOffset;
        var rotation = contextRotation * voice.RotationOffset * voice.OriginalLocalRotation;

        voice.Transform.SetPositionAndRotation(position, rotation);
    }

    private static void ConfigureParticleInstance(ParticleSystem root)
    {
        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (var i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.stopAction = ParticleSystemStopAction.None;
        }
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    private sealed class VisualVoice
    {
        public readonly int Id;
        public VisualFeedbackType Type;
        public ResourcePool Pool;
        public UnityEngine.Object Resource => Pool != null ? Pool.Resource : null;
        public GameObject GameObject;
        public Transform Transform;
        public ParticleSystem ParticleSystem;
        public VisualEffect VisualEffect;
        public Vector3 OriginalLocalScale;
        public Quaternion OriginalLocalRotation;

        public uint Generation;
        public bool InUse;
        public ulong LastAvailableSequence;
        public PauseReason PauseReasons;
        public bool IsPaused => PauseReasons != PauseReason.None;
        public bool Stopping;
        public float PlaybackElapsed;
        public float StopElapsed;
        public int PlaybackStartFrame = -1;
        public int StopStartFrame = -1;
        public int ResumeFrame = -1;
        public float VfxOneShotDuration;
        public float VfxStopTailDuration;
        public VisualFeedbackLifetime Lifetime;
        public VisualFeedbackStopMode StopMode;
        public Transform Follow;
        public bool WasFollowing;
        public Vector3 PositionOffset;
        public Quaternion RotationOffset;
        public Vector3 ScaleMultiplier;
        public Vector3 ContextVisualScale;

        public VisualVoice(int id)
        {
            Id = id;
        }

        public void Configure(
            VisualFeedbackType type,
            ResourcePool pool,
            GameObject gameObject,
            ParticleSystem particleSystem,
            VisualEffect visualEffect)
        {
            Type = type;
            Pool = pool;
            GameObject = gameObject;
            Transform = gameObject.transform;
            ParticleSystem = particleSystem;
            VisualEffect = visualEffect;
            OriginalLocalScale = gameObject.transform.localScale;
            OriginalLocalRotation = gameObject.transform.localRotation;
        }
    }

    private sealed class ResourcePool
    {
        public readonly UnityEngine.Object Resource;
        public readonly List<VisualVoice> Available = new List<VisualVoice>();
        public int LiveCount;

        public ResourcePool(UnityEngine.Object resource)
        {
            Resource = resource;
        }
    }
}
}
