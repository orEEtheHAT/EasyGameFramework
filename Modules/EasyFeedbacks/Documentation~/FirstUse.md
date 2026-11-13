# First use

EasyFeedbacks is included in EasyGameFramework. Reference `EasyFeedbacks.Runtime` from your runtime assembly. Audio, visual effects, camera shake and haptics do not require references to Input, Combat, Save or Settings modules. Unity Input System, Cinemachine and VFX Graph are framework dependencies; the supported render pipeline is URP.

1. In a bootstrap scene, choose **Tools > EasyGameFramework > Feedbacks > Add Playback Managers**. The command is undoable and safe to repeat. Existing disabled managers are preserved; enable them before playback.
2. Create **Assets > Create > EasyGameFramework > Feedbacks > Feedback Preset**. Add one Audio layer and assign a clip. Keep its default one-shot mode. Ensure the scene has one active AudioListener, usually on the output camera.
3. Add `FeedbackEmitter` to the object that produces the feedback and assign the preset. Enter Play Mode and press **Play** in its inspector: the clip should play once and the Audio Manager's active voice count should return to zero afterward.
4. Add Visual, Camera Shake or Gamepad Vibration layers as needed. Camera shake also needs a Cinemachine Impulse Listener on each reacting camera. Inspectors identify missing or disabled playback managers and invalid context providers.

Manager creation is explicit; `Play` does not create a global hierarchy. Game-specific clips, presets and scenes belong under Assets. Built-in shake/haptic profiles are read-only; use **Create Editable Copy** to customize them.

## Play, pause, and cancel one launch

```csharp
using EasyFeedbacks;

FeedbackHandle handle = emitter.PlayFeedback();
handle.Pause();
handle.Resume();
handle.Stop(); // Each layer keeps its configured fade or stop behavior.
```

`emitter.Play()` is a void UnityEvent trigger. `emitter.Stop/Pause/Resume` address its most recent launch; keep individual handles when launches overlap. For pooled emitters choose **On Enable** and **Stop On Disable** when each activation owns one launch. A handle from a completed launch cannot control a later launch that reuses its slot.

`handle.Stop(0f)` bypasses audio and haptic fades; visuals and camera shakes retain their authored stop behavior. `FeedbackService.StopAll(immediate: true)` clears all managed output immediately.

## Target the owning player

```csharp
FeedbackContext context = FeedbackContext.From(transform)
    .WithDefaultGamepad(playerGamepad);
FeedbackHandle handle = FeedbackService.Play(preset, context);
```

If this player's device is null, `WithDefaultGamepad` explicitly suppresses haptics. It preserves an already explicit device or `WithoutGamepad()` choice. Unaddressed calls and `WithGamepad(null)` retain the standalone API's current-gamepad fallback. The EasyInput adapter supplies the device through `IFeedbackContextProvider.Prepare(context)`; Feedbacks itself has no EasyInput dependency.

## Apply or cancel user audio/haptic preferences

```csharp
FeedbackSettingsState before = FeedbackSettings.Capture();
var edited = FeedbackSettings.Current;
edited.MusicVolume = 0.5f;
edited.HapticsIntensity = 0.6f;
FeedbackSettings.Apply(edited);
// On Cancel:
FeedbackSettings.Restore(before);
// On Save: pass FeedbackSettings.Capture() to the global-preferences save layer.
```

Call on Unity's main thread. Preferences affect existing and future output without replacing handles or changing pause ownership, devices or playback time. Muting does not pause; loops still need Stop. The serializable snapshot has `MasterVolume`, `MusicVolume`, `SoundEffectsVolume`, `HapticsEnabled`, `HapticsIntensity`; initialize new data with `FeedbackSettingsState.Default`. `Changed` fires only for a different normalized value. An optional `FeedbackSettingsProfile` supplies initial values without becoming runtime state. Feedbacks does not write files or PlayerPrefs; save global preferences separately from game slots.

## Verification and limits

Package PlayMode tests cover preferences, explicit player targeting, pause/handle ownership, one-shot/loop completion, pool reuse, manager removal/replacement, subsystem state reset and a bounded warm managed-allocation check with a positive control. Normal Player builds exclude test assemblies. The reset test invokes subsystem callbacks; actual Editor restart/domain-reload combinations still require the shared Editor verification step.

The Framework Playground sample provides `EasySuite.Example.Editor.FrameworkFeedbackSampleFactory.Create(outputFolder, includeVibration)` for a generated 120 ms tone, cyan particle burst and optional rumble. It reuses existing generated assets, preserves edits, does not alter the open scene and needs no project-local demo assets. The sample builder owns the output folder and scene setup.

VFX Graph timing tests without a graph do not prove rendered GPU output. Check audible output, physical haptics and camera response on your target hardware. Set VFX one-shot/tail durations explicitly and size/prewarm pools for expected overlap; eviction and growth allocate. Jobs/Burst are not used for main-thread Unity playback.
