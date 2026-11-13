using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyFeedbacks
{

/// <summary>
/// Persistent owner and mixer for dual-motor gamepad vibration feedback.
/// Concurrent vibrations are combined by taking the strongest value per motor.
/// Manager-wide and FeedbackService pauses are tracked independently.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Gamepad Vibration Manager")]
[DisallowMultipleComponent]
public sealed class GamepadVibrationManager : MonoBehaviour
{
    private const float MotorChangeEpsilon = 0.001f;
    private const float OutputRefreshInterval = 5f;

    private static int editorPreviewPlaybackDepth;

    [SerializeField, Min(0), Tooltip("Vibration slots allocated when this manager wakes up.")]
    private int initialCapacity = 8;

    [SerializeField, Min(1), Tooltip("Hard limit for simultaneous gamepad vibration layers.")]
    private int maximumCapacity = 32;

    private readonly List<Vibration> allVibrations = new List<Vibration>();
    private readonly List<Vibration> activeVibrations = new List<Vibration>();
    private readonly Stack<Vibration> availableVibrations = new Stack<Vibration>();
    private readonly List<DeviceOutput> deviceOutputs = new List<DeviceOutput>();

    private bool applicationHasFocus = true;
    private bool applicationPaused;
    private bool allPaused;
    private bool systemPaused;

    public static GamepadVibrationManager Instance { get; private set; }

    public int ActiveVibrations => activeVibrations.Count;
    public int AvailableVibrations => availableVibrations.Count;
    public int TotalVibrations => allVibrations.Count;
    public int MaximumCapacity => maximumCapacity;
    public Gamepad CurrentGamepad => Gamepad.current;
    public bool AreAllVibrationsPaused => allPaused;

    private bool IsGloballySuspended => applicationPaused || !isActiveAndEnabled || allPaused || systemPaused;

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
            Warn("GamepadVibrationManager must be a root object to persist between scenes. It was moved to the scene root at runtime.");
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);

        NormalizeCapacity();
        for (var i = 0; i < initialCapacity; i++)
        {
            availableVibrations.Push(CreateVibration());
        }

        SetSystemPaused(FeedbackService.AreAllPaused);
    }

    private void OnEnable()
    {
        if (Instance == this && Application.isPlaying)
        {
            ApplyOutputs();
        }
    }

    private void Update()
    {
        if (IsGloballySuspended)
        {
            return;
        }

        var deltaTime = Time.unscaledDeltaTime;
        for (var i = activeVibrations.Count - 1; i >= 0; i--)
        {
            var vibration = activeVibrations[i];
            if (vibration.Gamepad == null || !vibration.Gamepad.added)
            {
                ReleaseVibration(vibration);
                continue;
            }

            if (vibration.Paused || IsFocusSuspended(vibration))
            {
                continue;
            }

            if (UpdateVibration(vibration, deltaTime))
            {
                ReleaseVibration(vibration);
            }
        }

        ApplyOutputs();
        PruneDisconnectedOutputs();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationHasFocus = hasFocus;
        ApplyOutputs();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        applicationPaused = pauseStatus;
        ApplyOutputs();
    }

    private void OnDisable()
    {
        if (Instance != this)
        {
            return;
        }

        StopAll(true);
        SilenceOutputs();
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        ResetOutputs();
        Instance = null;
    }

    private void OnValidate()
    {
        NormalizeCapacity();
    }

    public GamepadVibrationHandle Play(GamepadVibrationFeedbackEntry entry, FeedbackContext context)
    {
        if (entry == null || !entry.IsEnabled || entry.Profile == null
            || context.HasExplicitGamepadTarget && context.TargetGamepad == null)
        {
            return default;
        }

        return PlayInternal(
            entry.Profile,
            entry.Profile.LowFrequencyMotor,
            entry.Profile.HighFrequencyMotor,
            entry.Duration,
            entry.Loop,
            entry.FadeIn,
            entry.FadeOut,
            context.Intensity * entry.StrengthMultiplier,
            context.TargetGamepad ?? Gamepad.current);
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void BeginEditorPreviewPlayback()
    {
        editorPreviewPlaybackDepth++;
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void EndEditorPreviewPlayback()
    {
        editorPreviewPlaybackDepth = Mathf.Max(0, editorPreviewPlaybackDepth - 1);
    }

    public GamepadVibrationHandle Play(
        float lowFrequencyMotor,
        float highFrequencyMotor,
        float duration,
        bool loop = false,
        float fadeIn = 0f,
        float fadeOut = 0f,
        float intensity = 1f,
        Gamepad gamepad = null)
    {
        return PlayInternal(
            null,
            lowFrequencyMotor,
            highFrequencyMotor,
            duration,
            loop,
            fadeIn,
            fadeOut,
            intensity,
            gamepad);
    }

    private GamepadVibrationHandle PlayInternal(
        GamepadVibrationProfile profile,
        float lowFrequencyMotor,
        float highFrequencyMotor,
        float duration,
        bool loop,
        float fadeIn,
        float fadeOut,
        float intensity,
        Gamepad gamepad)
    {
        if (!isActiveAndEnabled)
        {
            Warn("GamepadVibrationManager cannot play while it is disabled.");
            return default;
        }

        gamepad ??= Gamepad.current;
        if (gamepad == null || !gamepad.added || intensity <= 0f)
        {
            return default;
        }

        // Motor arguments remain normalized, while Intensity may amplify quieter
        // parts of a temporal pattern before the playback fade is applied.
        var resolvedIntensity = Mathf.Max(0f, intensity);
        var low = Mathf.Clamp01(lowFrequencyMotor) * resolvedIntensity;
        var high = Mathf.Clamp01(highFrequencyMotor) * resolvedIntensity;
        duration = Mathf.Max(0f, duration);
        if ((low <= 0f && high <= 0f) || (!loop && duration <= 0f))
        {
            return default;
        }

        var vibration = AcquireVibration();
        if (vibration == null)
        {
            Warn($"GamepadVibrationManager reached Maximum Capacity ({maximumCapacity}). The vibration was not played.");
            return default;
        }

        ConfigureVibration(vibration, profile, gamepad, low, high, duration, loop, fadeIn, fadeOut);
        GetOrCreateDeviceOutput(gamepad);
        ApplyOutputs();
        return new GamepadVibrationHandle(this, vibration.Id, vibration.Generation);
    }

    public void Stop(GamepadVibrationHandle handle)
    {
        if (TryGetVibration(handle, out var vibration))
        {
            RequestStop(vibration);
            ApplyOutputs();
        }
    }

    public void Stop(GamepadVibrationHandle handle, float fadeOutDuration)
    {
        if (TryGetVibration(handle, out var vibration))
        {
            RequestStop(vibration, Mathf.Max(0f, fadeOutDuration));
            ApplyOutputs();
        }
    }

    public void Pause(GamepadVibrationHandle handle)
    {
        if (TryGetVibration(handle, out var vibration) && !vibration.Paused)
        {
            vibration.Paused = true;
            ApplyOutputs();
        }
    }

    public void Resume(GamepadVibrationHandle handle)
    {
        if (TryGetVibration(handle, out var vibration) && vibration.Paused)
        {
            vibration.Paused = false;
            ApplyOutputs();
        }
    }

    public void StopAll(bool immediate = false)
    {
        for (var i = activeVibrations.Count - 1; i >= 0; i--)
        {
            var vibration = activeVibrations[i];
            if (immediate)
            {
                ReleaseVibration(vibration);
            }
            else
            {
                RequestStop(vibration);
            }
        }

        ApplyOutputs();
    }

    public void PauseAll()
    {
        if (allPaused)
        {
            return;
        }

        allPaused = true;
        ApplyOutputs();
    }

    public void ResumeAll()
    {
        if (!allPaused)
        {
            return;
        }

        allPaused = false;
        ApplyOutputs();
    }

    internal void SetSystemPaused(bool paused)
    {
        if (systemPaused == paused)
        {
            return;
        }

        systemPaused = paused;
        ApplyOutputs();
    }

    public void Prewarm(int targetCapacity)
    {
        targetCapacity = Mathf.Clamp(targetCapacity, 0, maximumCapacity);
        while (allVibrations.Count < targetCapacity)
        {
            availableVibrations.Push(CreateVibration());
        }
    }

    internal bool IsHandleValid(GamepadVibrationHandle handle)
    {
        return TryGetVibration(handle, out _);
    }

    internal bool IsHandlePlaying(GamepadVibrationHandle handle)
    {
        return TryGetVibration(handle, out var vibration)
               && !vibration.Paused
               && !IsVibrationSuspended(vibration);
    }

    private Vibration AcquireVibration()
    {
        if (activeVibrations.Count >= maximumCapacity)
        {
            return null;
        }

        Vibration vibration;
        if (availableVibrations.Count > 0)
        {
            vibration = availableVibrations.Pop();
        }
        else if (allVibrations.Count < maximumCapacity)
        {
            vibration = CreateVibration();
        }
        else
        {
            return null;
        }

        unchecked
        {
            vibration.Generation++;
            if (vibration.Generation == 0)
            {
                vibration.Generation = 1;
            }
        }

        vibration.InUse = true;
        activeVibrations.Add(vibration);
        return vibration;
    }

    private Vibration CreateVibration()
    {
        var vibration = new Vibration(allVibrations.Count);
        allVibrations.Add(vibration);
        return vibration;
    }

    private static void ConfigureVibration(
        Vibration vibration,
        GamepadVibrationProfile profile,
        Gamepad gamepad,
        float low,
        float high,
        float duration,
        bool loop,
        float fadeIn,
        float fadeOut)
    {
        vibration.Profile = profile;
        vibration.Gamepad = gamepad;
        vibration.LowFrequencyMotor = low;
        vibration.HighFrequencyMotor = high;
        vibration.Duration = duration;
        vibration.Loop = loop;
        vibration.FadeIn = Mathf.Max(0f, fadeIn);
        vibration.FadeOut = Mathf.Max(0f, fadeOut);

        if (!loop && vibration.FadeIn + vibration.FadeOut > duration)
        {
            var totalFade = vibration.FadeIn + vibration.FadeOut;
            var scale = totalFade > 0f ? duration / totalFade : 0f;
            vibration.FadeIn *= scale;
            vibration.FadeOut *= scale;
        }

        vibration.Elapsed = 0f;
        vibration.FadeMultiplier = vibration.FadeIn > 0f ? 0f : 1f;
        vibration.StopFadeElapsed = 0f;
        vibration.StopFadeDuration = 0f;
        vibration.StopStartMultiplier = 0f;
        vibration.Paused = false;
        vibration.Stopping = false;
        vibration.AllowOutputWhileUnfocused = editorPreviewPlaybackDepth > 0;
        UpdateMotorPattern(vibration);
    }

    private static bool UpdateVibration(Vibration vibration, float deltaTime)
    {
        vibration.Elapsed += deltaTime;
        UpdateMotorPattern(vibration);

        if (vibration.Stopping)
        {
            vibration.StopFadeElapsed += deltaTime;
            var progress = vibration.StopFadeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(vibration.StopFadeElapsed / vibration.StopFadeDuration);
            vibration.FadeMultiplier = Mathf.Lerp(vibration.StopStartMultiplier, 0f, progress);
            return progress >= 1f;
        }

        if (!vibration.Loop && vibration.Elapsed >= vibration.Duration)
        {
            return true;
        }

        var multiplier = 1f;
        if (vibration.FadeIn > 0f)
        {
            multiplier = Mathf.Min(multiplier, vibration.Elapsed / vibration.FadeIn);
        }

        if (!vibration.Loop && vibration.FadeOut > 0f)
        {
            multiplier = Mathf.Min(
                multiplier,
                (vibration.Duration - vibration.Elapsed) / vibration.FadeOut);
        }

        vibration.FadeMultiplier = Mathf.Clamp01(multiplier);
        return false;
    }

    private static void UpdateMotorPattern(Vibration vibration)
    {
        var profile = vibration.Profile;
        if (profile == null)
        {
            vibration.LowPatternMultiplier = 1f;
            vibration.HighPatternMultiplier = 1f;
            return;
        }

        float normalizedTime;
        if (vibration.Loop)
        {
            normalizedTime = Mathf.Repeat(
                vibration.Elapsed / profile.LoopCycleDuration,
                1f);
        }
        else
        {
            normalizedTime = vibration.Duration > Mathf.Epsilon
                ? Mathf.Clamp01(vibration.Elapsed / vibration.Duration)
                : 1f;
        }

        vibration.LowPatternMultiplier = profile.EvaluateLowFrequencyPattern(normalizedTime);
        vibration.HighPatternMultiplier = profile.EvaluateHighFrequencyPattern(normalizedTime);
    }

    private void RequestStop(Vibration vibration, float fadeOutDuration = -1f)
    {
        if (!vibration.InUse)
        {
            return;
        }

        if (vibration.Stopping)
        {
            if (vibration.Paused
                || IsVibrationSuspended(vibration)
                || fadeOutDuration == 0f)
            {
                ReleaseVibration(vibration);
                return;
            }

            if (fadeOutDuration > 0f)
            {
                var remainingFade = Mathf.Max(
                    0f,
                    vibration.StopFadeDuration - vibration.StopFadeElapsed);
                if (remainingFade <= 0f)
                {
                    ReleaseVibration(vibration);
                }
                else if (fadeOutDuration < remainingFade)
                {
                    vibration.StopFadeElapsed = 0f;
                    vibration.StopFadeDuration = fadeOutDuration;
                    vibration.StopStartMultiplier = vibration.FadeMultiplier;
                }
            }

            return;
        }

        var resolvedFadeOut = fadeOutDuration >= 0f ? fadeOutDuration : vibration.FadeOut;
        if (!vibration.Loop)
        {
            resolvedFadeOut = Mathf.Min(
                resolvedFadeOut,
                Mathf.Max(0f, vibration.Duration - vibration.Elapsed));
        }

        if (resolvedFadeOut <= 0f
            || vibration.FadeMultiplier <= 0f
            || vibration.Paused
            || IsVibrationSuspended(vibration))
        {
            ReleaseVibration(vibration);
            return;
        }

        vibration.Stopping = true;
        vibration.StopFadeElapsed = 0f;
        vibration.StopFadeDuration = resolvedFadeOut;
        vibration.StopStartMultiplier = vibration.FadeMultiplier;
    }

    private void ReleaseVibration(Vibration vibration)
    {
        if (!vibration.InUse)
        {
            return;
        }

        activeVibrations.Remove(vibration);
        vibration.Reset();
        availableVibrations.Push(vibration);
    }

    private bool TryGetVibration(GamepadVibrationHandle handle, out Vibration vibration)
    {
        if (handle.Owner != this)
        {
            vibration = null;
            return false;
        }

        var id = handle.VibrationId;
        if (id < 0 || id >= allVibrations.Count)
        {
            vibration = null;
            return false;
        }

        vibration = allVibrations[id];
        return vibration.InUse && vibration.Generation == handle.Generation;
    }

    private DeviceOutput GetOrCreateDeviceOutput(Gamepad gamepad)
    {
        for (var i = 0; i < deviceOutputs.Count; i++)
        {
            if (ReferenceEquals(deviceOutputs[i].Gamepad, gamepad))
            {
                return deviceOutputs[i];
            }
        }

        var output = new DeviceOutput(gamepad);
        deviceOutputs.Add(output);
        return output;
    }

    internal void ApplyUserSettings() => ApplyOutputs();

    private void ApplyOutputs()
    {
        var globallySuspended = IsGloballySuspended;
        for (var outputIndex = 0; outputIndex < deviceOutputs.Count; outputIndex++)
        {
            var output = deviceOutputs[outputIndex];
            var gamepad = output.Gamepad;
            if (gamepad == null || !gamepad.added)
            {
                output.AppliedLow = 0f;
                output.AppliedHigh = 0f;
                continue;
            }

            var low = 0f;
            var high = 0f;
            if (!globallySuspended)
            {
                for (var vibrationIndex = 0; vibrationIndex < activeVibrations.Count; vibrationIndex++)
                {
                    var vibration = activeVibrations[vibrationIndex];
                    if (vibration.Paused
                        || IsFocusSuspended(vibration)
                        || !ReferenceEquals(vibration.Gamepad, gamepad))
                    {
                        continue;
                    }

                    var vibrationLow = Mathf.Clamp01(
                        vibration.LowFrequencyMotor * vibration.LowPatternMultiplier)
                        * vibration.FadeMultiplier;
                    var vibrationHigh = Mathf.Clamp01(
                        vibration.HighFrequencyMotor * vibration.HighPatternMultiplier)
                        * vibration.FadeMultiplier;
                    low = Mathf.Max(low, vibrationLow);
                    high = Mathf.Max(high, vibrationHigh);
                }
            }

            var userMultiplier = FeedbackSettings.HapticsMultiplier;
            SetOutput(output, low * userMultiplier, high * userMultiplier);
        }
    }

    private bool IsVibrationSuspended(Vibration vibration)
    {
        return IsGloballySuspended || IsFocusSuspended(vibration);
    }

    private bool IsFocusSuspended(Vibration vibration)
    {
        return !applicationHasFocus && !vibration.AllowOutputWhileUnfocused;
    }

    private void SilenceOutputs()
    {
        for (var i = 0; i < deviceOutputs.Count; i++)
        {
            SetOutput(deviceOutputs[i], 0f, 0f);
        }
    }

    private void PruneDisconnectedOutputs()
    {
        for (var i = deviceOutputs.Count - 1; i >= 0; i--)
        {
            var gamepad = deviceOutputs[i].Gamepad;
            if (gamepad == null || !gamepad.added)
            {
                deviceOutputs.RemoveAt(i);
            }
        }
    }

    private void ResetOutputs()
    {
        for (var i = 0; i < deviceOutputs.Count; i++)
        {
            var output = deviceOutputs[i];
            if (output.Gamepad != null && output.Gamepad.added)
            {
                output.Gamepad.ResetHaptics();
            }

            output.AppliedLow = 0f;
            output.AppliedHigh = 0f;
        }
    }

    private static void SetOutput(DeviceOutput output, float low, float high)
    {
        var gamepad = output.Gamepad;
        if (gamepad == null || !gamepad.added)
        {
            output.AppliedLow = 0f;
            output.AppliedHigh = 0f;
            return;
        }

        low = Mathf.Clamp01(low);
        high = Mathf.Clamp01(high);
        var outputUnchanged = Mathf.Abs(output.AppliedLow - low) < MotorChangeEpsilon
                              && Mathf.Abs(output.AppliedHigh - high) < MotorChangeEpsilon;
        var mustSilenceExact = low <= 0f
                               && high <= 0f
                               && (output.AppliedLow > 0f || output.AppliedHigh > 0f);
        var refreshRequired = (low > 0f || high > 0f)
                              && Time.unscaledTime - output.LastCommandTime >= OutputRefreshInterval;
        if (outputUnchanged && !mustSilenceExact && !refreshRequired)
        {
            return;
        }

        gamepad.SetMotorSpeeds(low, high);
        output.AppliedLow = low;
        output.AppliedHigh = high;
        output.LastCommandTime = Time.unscaledTime;
    }

    private void NormalizeCapacity()
    {
        maximumCapacity = Mathf.Max(1, maximumCapacity);
        initialCapacity = Mathf.Clamp(initialCapacity, 0, maximumCapacity);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    private sealed class Vibration
    {
        public readonly int Id;
        public uint Generation;
        public bool InUse;
        public GamepadVibrationProfile Profile;
        public Gamepad Gamepad;
        public float LowFrequencyMotor;
        public float HighFrequencyMotor;
        public float LowPatternMultiplier;
        public float HighPatternMultiplier;
        public bool Loop;
        public float Duration;
        public float FadeIn;
        public float FadeOut;
        public float Elapsed;
        public float FadeMultiplier;
        public bool Paused;
        public bool Stopping;
        public bool AllowOutputWhileUnfocused;
        public float StopFadeElapsed;
        public float StopFadeDuration;
        public float StopStartMultiplier;

        public Vibration(int id)
        {
            Id = id;
        }

        public void Reset()
        {
            InUse = false;
            Profile = null;
            Gamepad = null;
            LowFrequencyMotor = 0f;
            HighFrequencyMotor = 0f;
            LowPatternMultiplier = 0f;
            HighPatternMultiplier = 0f;
            Loop = false;
            Duration = 0f;
            FadeIn = 0f;
            FadeOut = 0f;
            Elapsed = 0f;
            FadeMultiplier = 0f;
            Paused = false;
            Stopping = false;
            AllowOutputWhileUnfocused = false;
            StopFadeElapsed = 0f;
            StopFadeDuration = 0f;
            StopStartMultiplier = 0f;
        }
    }

    private sealed class DeviceOutput
    {
        public readonly Gamepad Gamepad;
        public float AppliedLow;
        public float AppliedHigh;
        public float LastCommandTime;

        public DeviceOutput(Gamepad gamepad)
        {
            Gamepad = gamepad;
        }
    }
}
}
