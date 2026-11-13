using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Generation-safe reference to the pooled state produced by one feedback launch.
/// Copies remain safe when the backing slot is later reused.
/// </summary>
public readonly struct FeedbackHandle
{
    private readonly int slotId;
    private readonly uint generation;

    internal FeedbackHandle(int slotId, uint generation)
    {
        this.slotId = slotId;
        this.generation = generation;
    }

    internal int SlotId => slotId;
    internal uint Generation => generation;

    public bool IsValid => FeedbackHandleRegistry.IsValid(this);
    public bool CanPause => FeedbackHandleRegistry.CanPause(this);

    public void Stop()
    {
        FeedbackHandleRegistry.Stop(this, -1f);
    }

    public void Stop(float fadeOutDuration)
    {
        FeedbackHandleRegistry.Stop(this, Mathf.Max(0f, fadeOutDuration));
    }

    public void Pause()
    {
        FeedbackHandleRegistry.Pause(this);
    }

    public void Resume()
    {
        FeedbackHandleRegistry.Resume(this);
    }
}

/// <summary>
/// Reuses aggregate feedback state instead of allocating Lists and handle arrays
/// for every FeedbackService.Play call. Overflow Lists are allocated only when a
/// preset contains multiple active layers of the same module, then retained.
/// </summary>
internal static class FeedbackHandleRegistry
{
    private const int InitialCapacity = 32;

    private static State[] slots = new State[InitialCapacity];
    private static readonly List<int> ActiveSlots = new List<int>(InitialCapacity);
    private static readonly Stack<int> FreeSlots = new Stack<int>(InitialCapacity);
    private static int nextUnusedSlot;
    private static int lastSweepFrame = -1;

    internal static void Prewarm()
    {
        // Touching the registry during scene startup moves its one-time storage
        // initialization out of the first gameplay feedback.
        _ = slots.Length;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        for (var i = 0; i < nextUnusedSlot; i++)
        {
            slots[i].InUse = false;
            slots[i].ActiveListIndex = -1;
            slots[i].ResetHandles();
        }

        ActiveSlots.Clear();
        FreeSlots.Clear();
        nextUnusedSlot = 0;
        lastSweepFrame = -1;
    }

    internal static FeedbackHandle Rent()
    {
        SweepFinishedSlots();

        int slotId;
        if (FreeSlots.Count > 0)
        {
            slotId = FreeSlots.Pop();
        }
        else
        {
            if (nextUnusedSlot >= slots.Length)
            {
                Array.Resize(ref slots, slots.Length * 2);
            }

            slotId = nextUnusedSlot;
            nextUnusedSlot++;
        }

        ref var state = ref slots[slotId];
        state.ResetHandles();
        unchecked
        {
            state.Generation++;
            if (state.Generation == 0)
            {
                state.Generation = 1;
            }
        }

        state.InUse = true;
        state.ActiveListIndex = ActiveSlots.Count;
        ActiveSlots.Add(slotId);
        return new FeedbackHandle(slotId, state.Generation);
    }

    internal static void Add(FeedbackHandle handle, AudioHandle audioHandle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Add(audioHandle);
        }
    }

    internal static void Add(FeedbackHandle handle, VisualHandle visualHandle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Add(visualHandle);
        }
    }

    internal static void Add(FeedbackHandle handle, CameraShakeHandle cameraShakeHandle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Add(cameraShakeHandle);
        }
    }

    internal static void Add(FeedbackHandle handle, GamepadVibrationHandle gamepadVibrationHandle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Add(gamepadVibrationHandle);
        }
    }

    internal static bool IsEmpty(FeedbackHandle handle)
    {
        return !TryGetState(handle, out var slotId) || slots[slotId].IsEmpty;
    }

    internal static void Discard(FeedbackHandle handle)
    {
        if (TryGetState(handle, out var slotId))
        {
            ReleaseSlot(slotId);
        }
    }

    internal static bool IsValid(FeedbackHandle handle)
    {
        if (!TryGetState(handle, out var slotId))
        {
            return false;
        }

        if (slots[slotId].HasAnyValidHandle())
        {
            return true;
        }

        ReleaseSlot(slotId);
        return false;
    }

    internal static bool CanPause(FeedbackHandle handle)
    {
        return TryGetState(handle, out var slotId) && slots[slotId].HasAnyPausableHandle();
    }

    internal static void Stop(FeedbackHandle handle, float fadeOutDuration)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Stop(fadeOutDuration);
            // Immediate stops can be followed by another launch in the same frame.
            // Return completed slots now; the once-per-frame sweep otherwise grows
            // the registry for every launch even when no output overlaps.
            if (!slots[slotId].HasAnyValidHandle())
            {
                ReleaseSlot(slotId);
            }
        }
    }

    internal static void Pause(FeedbackHandle handle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Pause();
        }
    }

    internal static void Resume(FeedbackHandle handle)
    {
        if (TryGetState(handle, out var slotId))
        {
            slots[slotId].Resume();
        }
    }

    private static void SweepFinishedSlots()
    {
        var frame = Time.frameCount;
        if (lastSweepFrame == frame)
        {
            return;
        }

        lastSweepFrame = frame;
        for (var i = ActiveSlots.Count - 1; i >= 0; i--)
        {
            var slotId = ActiveSlots[i];
            if (!slots[slotId].HasAnyValidHandle())
            {
                ReleaseSlot(slotId);
            }
        }
    }

    private static bool TryGetState(FeedbackHandle handle, out int slotId)
    {
        slotId = handle.SlotId;
        return handle.Generation != 0
               && slotId >= 0
               && slotId < nextUnusedSlot
               && slots[slotId].InUse
               && slots[slotId].Generation == handle.Generation;
    }

    private static void ReleaseSlot(int slotId)
    {
        ref var state = ref slots[slotId];
        if (!state.InUse)
        {
            return;
        }

        var activeIndex = state.ActiveListIndex;
        var lastIndex = ActiveSlots.Count - 1;
        var lastSlotId = ActiveSlots[lastIndex];
        ActiveSlots[activeIndex] = lastSlotId;
        slots[lastSlotId].ActiveListIndex = activeIndex;
        ActiveSlots.RemoveAt(lastIndex);

        state.InUse = false;
        state.ActiveListIndex = -1;
        state.ResetHandles();
        FreeSlots.Push(slotId);
    }

    private struct State
    {
        public uint Generation;
        public bool InUse;
        public int ActiveListIndex;

        private bool hasAudio;
        private AudioHandle firstAudio;
        private List<AudioHandle> extraAudio;
        private bool hasVisual;
        private VisualHandle firstVisual;
        private List<VisualHandle> extraVisual;
        private bool hasCameraShake;
        private CameraShakeHandle firstCameraShake;
        private List<CameraShakeHandle> extraCameraShake;
        private bool hasGamepadVibration;
        private GamepadVibrationHandle firstGamepadVibration;
        private List<GamepadVibrationHandle> extraGamepadVibration;

        public bool IsEmpty => !hasAudio && !hasVisual && !hasCameraShake && !hasGamepadVibration;

        public void Add(AudioHandle handle)
        {
            if (!hasAudio)
            {
                firstAudio = handle;
                hasAudio = true;
                return;
            }

            extraAudio ??= new List<AudioHandle>(1);
            extraAudio.Add(handle);
        }

        public void Add(VisualHandle handle)
        {
            if (!hasVisual)
            {
                firstVisual = handle;
                hasVisual = true;
                return;
            }

            extraVisual ??= new List<VisualHandle>(1);
            extraVisual.Add(handle);
        }

        public void Add(CameraShakeHandle handle)
        {
            if (!hasCameraShake)
            {
                firstCameraShake = handle;
                hasCameraShake = true;
                return;
            }

            extraCameraShake ??= new List<CameraShakeHandle>(1);
            extraCameraShake.Add(handle);
        }

        public void Add(GamepadVibrationHandle handle)
        {
            if (!hasGamepadVibration)
            {
                firstGamepadVibration = handle;
                hasGamepadVibration = true;
                return;
            }

            extraGamepadVibration ??= new List<GamepadVibrationHandle>(1);
            extraGamepadVibration.Add(handle);
        }

        public bool HasAnyValidHandle()
        {
            if ((hasAudio && firstAudio.IsValid)
                || (hasVisual && firstVisual.IsValid)
                || (hasCameraShake && firstCameraShake.IsValid)
                || (hasGamepadVibration && firstGamepadVibration.IsValid))
            {
                return true;
            }

            if (HasValid(extraAudio)
                || HasValid(extraVisual)
                || HasValid(extraCameraShake)
                || HasValid(extraGamepadVibration))
            {
                return true;
            }

            return false;
        }

        public bool HasAnyPausableHandle()
        {
            if ((hasAudio && firstAudio.IsValid)
                || (hasVisual && firstVisual.IsValid)
                || (hasCameraShake && firstCameraShake.IsValid)
                || (hasGamepadVibration && firstGamepadVibration.IsValid))
            {
                return true;
            }

            return HasValid(extraAudio)
                   || HasValid(extraVisual)
                   || HasValid(extraCameraShake)
                   || HasValid(extraGamepadVibration);
        }

        public void Stop(float fadeOutDuration)
        {
            if (hasAudio)
            {
                Stop(firstAudio, fadeOutDuration);
            }

            if (extraAudio != null)
            {
                for (var i = 0; i < extraAudio.Count; i++)
                {
                    Stop(extraAudio[i], fadeOutDuration);
                }
            }

            if (hasVisual)
            {
                firstVisual.Stop();
            }

            if (extraVisual != null)
            {
                for (var i = 0; i < extraVisual.Count; i++)
                {
                    extraVisual[i].Stop();
                }
            }

            if (hasCameraShake)
            {
                firstCameraShake.Stop();
            }

            if (extraCameraShake != null)
            {
                for (var i = 0; i < extraCameraShake.Count; i++)
                {
                    extraCameraShake[i].Stop();
                }
            }

            if (hasGamepadVibration)
            {
                Stop(firstGamepadVibration, fadeOutDuration);
            }

            if (extraGamepadVibration != null)
            {
                for (var i = 0; i < extraGamepadVibration.Count; i++)
                {
                    Stop(extraGamepadVibration[i], fadeOutDuration);
                }
            }
        }

        public void Pause()
        {
            if (hasAudio)
            {
                firstAudio.Pause();
            }

            if (extraAudio != null)
            {
                for (var i = 0; i < extraAudio.Count; i++)
                {
                    extraAudio[i].Pause();
                }
            }

            if (hasVisual)
            {
                firstVisual.Pause();
            }

            if (extraVisual != null)
            {
                for (var i = 0; i < extraVisual.Count; i++)
                {
                    extraVisual[i].Pause();
                }
            }

            if (hasCameraShake)
            {
                firstCameraShake.Pause();
            }

            if (extraCameraShake != null)
            {
                for (var i = 0; i < extraCameraShake.Count; i++)
                {
                    extraCameraShake[i].Pause();
                }
            }

            if (hasGamepadVibration)
            {
                firstGamepadVibration.Pause();
            }

            if (extraGamepadVibration != null)
            {
                for (var i = 0; i < extraGamepadVibration.Count; i++)
                {
                    extraGamepadVibration[i].Pause();
                }
            }
        }

        public void Resume()
        {
            if (hasAudio)
            {
                firstAudio.Resume();
            }

            if (extraAudio != null)
            {
                for (var i = 0; i < extraAudio.Count; i++)
                {
                    extraAudio[i].Resume();
                }
            }

            if (hasVisual)
            {
                firstVisual.Resume();
            }

            if (extraVisual != null)
            {
                for (var i = 0; i < extraVisual.Count; i++)
                {
                    extraVisual[i].Resume();
                }
            }

            if (hasCameraShake)
            {
                firstCameraShake.Resume();
            }

            if (extraCameraShake != null)
            {
                for (var i = 0; i < extraCameraShake.Count; i++)
                {
                    extraCameraShake[i].Resume();
                }
            }

            if (hasGamepadVibration)
            {
                firstGamepadVibration.Resume();
            }

            if (extraGamepadVibration != null)
            {
                for (var i = 0; i < extraGamepadVibration.Count; i++)
                {
                    extraGamepadVibration[i].Resume();
                }
            }
        }

        public void ResetHandles()
        {
            hasAudio = false;
            firstAudio = default;
            extraAudio?.Clear();
            hasVisual = false;
            firstVisual = default;
            extraVisual?.Clear();
            hasCameraShake = false;
            firstCameraShake = default;
            extraCameraShake?.Clear();
            hasGamepadVibration = false;
            firstGamepadVibration = default;
            extraGamepadVibration?.Clear();
        }

        private static void Stop(AudioHandle handle, float fadeOutDuration)
        {
            if (fadeOutDuration >= 0f)
            {
                handle.Stop(fadeOutDuration);
            }
            else
            {
                handle.Stop();
            }
        }

        private static void Stop(GamepadVibrationHandle handle, float fadeOutDuration)
        {
            if (fadeOutDuration >= 0f)
            {
                handle.Stop(fadeOutDuration);
            }
            else
            {
                handle.Stop();
            }
        }

        private static bool HasValid(List<AudioHandle> handles)
        {
            if (handles == null)
            {
                return false;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValid(List<VisualHandle> handles)
        {
            if (handles == null)
            {
                return false;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValid(List<CameraShakeHandle> handles)
        {
            if (handles == null)
            {
                return false;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValid(List<GamepadVibrationHandle> handles)
        {
            if (handles == null)
            {
                return false;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
}
