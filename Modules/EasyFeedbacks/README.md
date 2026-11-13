# EasyFeedbacks — EasyGameFramework module

Feedback playback and audio/haptics preferences within EasyGameFramework.

Compose gameplay feedback from pooled audio, Particle System or VFX Graph visuals, Cinemachine camera shake, gamepad vibration, and priority-based music playback.

## Requirements

- Unity 6.3 (6000.3) or newer
- Cinemachine 3.1.7 or a compatible newer version
- Input System 1.17.0 or a compatible newer version
- Visual Effect Graph 17.3.0 or a compatible version for the active render pipeline

The required Unity packages are declared in the framework root `package.json`.

## Installation

Install the single **EasyGameFramework** package using its root README. This module is included automatically; its assembly and namespace remain `EasyFeedbacks.Runtime` and `EasyFeedbacks`.

The standalone EasyFeedbacks product is maintained separately. Do not install both products in one project: they deliberately retain the same types and asset GUIDs. This module does not rewrite or publish the standalone repository.

See [First use](Documentation~/FirstUse.md) for a short setup recipe and three API examples.

## Scene setup

In your bootstrap scene choose **Tools > EasyGameFramework > Feedbacks > Add Playback Managers**. This explicit, undoable command adds missing managers to a root object and leaves existing managers unchanged. Enable any existing disabled managers. You can also add just the managers needed by the project:

- `AudioManager` for pooled `AudioClip` playback.
- `VisualFeedbackManager` for pooled Particle System and VFX Graph prefabs.
- `CameraShakeManager` for Easy Feedbacks camera shake ownership and global controls.
- `GamepadVibrationManager` for mixed dual-motor vibration playback.
- `MusicManager` for fallback, scene, zone, and event music selection.

Camera shake uses Cinemachine Impulse. It requires both a persistent `CameraShakeManager` and a Cinemachine Impulse Listener on every camera that should react. Keep **Use Camera Space** enabled for camera-oriented directional kicks.

Disabling a playback manager immediately releases the output it owns and rejects new playback until the manager is enabled again. Disabling `AudioManager` or `MusicManager` stops music; call `MusicManager.ResumeMusic()` after enabling the required managers when playback should continue.

Removing an AudioManager or VisualFeedbackManager component also destroys the pooled objects it created; the bootstrap object and unrelated children remain intact. Disabling and re-enabling these components retains their pools for reuse.

## Feedback workflow

1. Create **Assets > Create > EasyGameFramework > Feedbacks > Feedback Preset**.
2. Add any required Audio, Visual, Camera Shake, and Gamepad Vibration layers. Audio layers can hold multiple clips and randomize volume and pitch per playback.
3. Add `FeedbackEmitter` to the GameObject that owns the feedback origin.
4. Assign the preset and choose World Position or Follow Transform positioning.
5. Invoke `FeedbackEmitter.PlayFeedback()` and use the returned `FeedbackHandle` to stop, pause, or resume that launch.

The emitter's short control methods operate on its most recent launch:

```csharp
emitter.Play();
emitter.Pause();
emitter.Resume();
emitter.Stop();
```

FeedbackEmitter can also follow a GameObject's lifecycle. **Auto Play** supports Manual, On Start, and On Enable modes. **Stop On Disable** stops the emitter's most recent audio, visual, camera shake, and gamepad vibration launch using each layer's configured stop behavior.

Code can also start a preset directly:

```csharp
using EasyFeedbacks;

FeedbackHandle handle = FeedbackService.Play(preset, transform);
handle.Pause();
handle.Resume();
handle.Stop();
```

### Visual effects and pooling

Particle System one-shots return to the pool when Unity reports that their particles have finished. VFX Graph completion is authored explicitly because its particle-count readback is asynchronous: set **One Shot Duration** to the full effect time, including spawn delays, emission, and trails. With **Finish Current Particles**, **Stop Tail Duration** controls how long existing VFX particles may continue after an explicit stop. These are simulation durations: they respect the Visual Effect Play Rate and pause and resume with the feedback. Stopping a one-shot never extends it beyond its original One Shot Duration; the first applicable deadline returns it to the pool.

The visual pool grows lazily up to **Maximum Capacity**. When the capacity is full, the least recently used free instance is replaced for a newly requested resource; active instances are never evicted. Playback is rejected only when every pooled instance is active. Set **Prewarm Count** to the expected overlap for frequently used effects so ordinary gameplay avoids Instantiate work. Eviction is a pressure fallback: every eviction performs Instantiate/Destroy work, so configure and prewarm the pool to keep the normal working set resident.

## Runtime user preferences

One settings window can control all Easy Feedbacks output without an Audio Mixer or an extra manager:

```csharp
FeedbackSettingsState beforeEditing = FeedbackSettings.Capture();
var settings = FeedbackSettings.Current; // A value copy, safe to edit.
settings.MasterVolume = 0.8f;
settings.MusicVolume = 0.5f;
settings.SoundEffectsVolume = 1f;
settings.HapticsEnabled = true;
settings.HapticsIntensity = 0.6f;
FeedbackSettings.Apply(settings);

// Cancel the settings window:
FeedbackSettings.Restore(beforeEditing);
```

Use these APIs on Unity's main thread. `Changed` notifies listeners after a different validated state is applied. `Capture` returns a serializable value for the game's own settings/save system; this package does not write PlayerPrefs or files. Finite multipliers are clamped to `0..1`; NaN and infinity fall back to `1`. Start new data from `FeedbackSettingsState.Default`: the C# zero-initialized struct instead means all output is muted and haptics are disabled.

Preferences affect both current and future managed output immediately. The master and category multipliers are applied after each audio voice's authored volume and fade. Existing `AudioHandle.SetVolume` and `MusicManager.SetVolume` still work independently. Preset audio and ordinary `AudioManager.Play` calls use **Sound Effects**; tracks started by `MusicManager` use **Music**. Direct playback can select `AudioPlaySettings.Category = FeedbackAudioCategory.Music`. Mixer routing, channel pauses, and global-pause exclusions remain independent of these categories. Ordinary AudioSources outside Easy Feedbacks are unaffected.

Zero volume or disabled haptics silences output while its timeline continues. One-shots return to their pools normally; loops remain owned by their handles and must still be stopped. Restoring preferences resumes the current phase of an active effect; it does not replay an expired one-shot. Haptic intensity preserves each layer's explicitly targeted gamepad.

An optional **Assets > Create > EasyGameFramework > Feedbacks > Feedback Settings** asset supplies initial values. Call `initialProfile.Apply()` once from the application's bootstrap, then restore any saved user preferences. Runtime changes never modify this asset. Each new play session starts with `FeedbackSettingsState.Default`, including when domain reload is disabled.

## Player-specific feedback context

Adapters can accept an optional component implementing `IFeedbackContextProvider` without depending on a player-input package. Pass the existing context to `Prepare` and preserve its authored source, position, rotation, follow transform, intensity, audio multiplier and visual scale:

```csharp
public FeedbackContext Prepare(FeedbackContext context)
{
    return context.WithDefaultGamepad(playerGamepad);
}
```

`WithDefaultGamepad` preserves an explicit device or an explicit choice of no device. When the player's gamepad is null, it creates an explicit no-gamepad context, so a keyboard-only player cannot vibrate another player's `Gamepad.current`. `HasExplicitGamepadTarget` distinguishes that choice from an unaddressed request. Use `WithoutGamepad()` to explicitly suppress vibration; other modules and context values are unchanged. `WithGamepad(nonNullDevice)` intentionally replaces the choice. For backward compatibility, `From`, `default(FeedbackContext)` and `WithGamepad(null)` retain the legacy current-gamepad fallback.

A `FeedbackEmitter` also accepts an optional **Context Provider**; it is used while that component is active and enabled. Without a provider the emitter retains its existing behavior. Providers should pass through the context and call `WithDefaultGamepad(playerGamepad)` instead of rebuilding a context or testing only `TargetGamepad != null`, which would lose the explicit no-gamepad choice.

## Global pause

Use the static feedback service to pause or resume managed output, except audio mixer groups explicitly configured to ignore the global pause:

```csharp
FeedbackService.PauseAll();
FeedbackService.ResumeAll();

// Uses each layer's configured fade or stop behaviour.
FeedbackService.StopAll();

// Immediately clears all managed output, for example before a loading screen.
FeedbackService.StopAll(immediate: true);
```

Calls can be nested, so every `PauseAll()` must be paired with one `ResumeAll()`. Manager-wide and individual handle pauses remain independent: a global resume never wakes an output that is still paused locally. Audio includes music because `MusicManager` delegates track playback to `AudioManager`.

For pause-menu sounds, assign an Audio Mixer to `AudioManager`, expand **Mixer Channel Controls**, and enable **Ignore Global Pause** on a dedicated group such as `UI`. The option applies to that exact mixer group and only bypasses `FeedbackService.PauseAll()`: handle pause, channel pause, `AudioManager.PauseAll()`, `StopAll()`, and `AudioListener.pause` still apply normally. Child mixer groups must be enabled separately.

Camera shake timing resumes from the same phase after a pause. While paused, its impulse contribution is muted so the camera can rest at its normal pose instead of remaining tilted or displaced behind a pause menu.

The global pause also applies to managed output launched while paused. Global controls do not affect ordinary AudioSources, third-party Particle Systems or VFX, external Cinemachine impulses, or gamepad motors controlled outside Easy Feedbacks.

`StopAll` does not change the nested pause state and does not prevent later feedback from playing. It stops music as well as feedback output; restart music explicitly with `MusicManager.ResumeMusic()` when the loading flow is ready.

## Gamepad vibration

Add a `GamepadVibrationManager` to the persistent bootstrap scene. A reusable `GamepadVibrationProfile` defines the balance and normalized temporal pattern of the low-frequency and high-frequency motors. Each vibration layer selects a profile and controls its strength, one-shot duration or looping, fade in, and fade out. Concurrent layers are mixed by keeping the strongest value for each motor.

Create a profile through **Assets > Create > EasyGameFramework > Feedbacks > Gamepad Vibration Profile**, or select one of the ten built-in starting profiles directly from a vibration layer:

- UI Click
- Light Impact
- Heavy Impact
- Explosion
- Low Rumble
- Weapon Recoil
- Landing
- Damage
- Earthquake
- Electric Magic

Adding a built-in profile through the layer's **+** menu also applies recommended duration, fades, and loop mode. Built-in package profiles are read-only. Select one in the Project window and use **Create Editable Copy** to save a customizable version under `Assets`.

Motor values are normalized speed commands, not perceptually linear force. Unity sends values in the `0..1` range to the controller, but the physical response, threshold, saturation, and perceived strength vary by controller and driver. The Low and High controls address two different motors; they do not change a single motor's frequency.

Route gameplay rumble through this manager instead of also calling `SetMotorSpeeds` directly, because a gamepad exposes one shared motor state and competing commands overwrite one another.

Unaddressed preset playback captures `Gamepad.current` when the vibration begins. Code driving local multiplayer can target a specific device explicitly:

```csharp
FeedbackContext context = FeedbackContext.From(transform).WithGamepad(playerGamepad);
FeedbackHandle handle = FeedbackService.Play(preset, context);
```

Actual rumble support depends on the controller, platform, operating system, and driver.

## Built-in camera shake profiles

Add a `CameraShakeManager` to the persistent bootstrap scene. Camera Shake layers can directly select ten built-in starting profiles: Light Impact, Heavy Impact, Explosion, Weapon Recoil, Low Rumble, Landing, Damage, Earthquake, Electric Magic, and Shockwave. They use Cinemachine's 6D Shake or 6D Wobble noise profiles and remain read-only inside the package. Adding one through the layer's **+** menu applies recommended playback settings. Select a built-in asset and use **Create Editable Copy** before customizing it.

The profile defines the character of a shake. Noise duration, fade in, fade out, loop mode, and per-layer strength remain in the Camera Shake layer so the same profile can be reused in different situations.

Noise signal sources are pooled with camera shake slots, so repeated noise-only recoil does not allocate a new signal per shot after the pool is prepared. For the usual Uniform noise shake, a practical `Initial Capacity` is approximately the shot rate multiplied by the shake duration; for example, `30 shots/s × 0.35 s` needs about 11 concurrent slots, so 12–16 is a sensible setting. Propagating shakes may need extra slots for their travel time.

## Input debug trigger

Add `DebugFeedbackTrigger` next to a `FeedbackEmitter`, then assign an Input Action Reference. The trigger invokes the emitter whenever the action is performed. It can enable a disabled action automatically and, by default, runs only in the Editor or a development build.

## Music selection

Music is selected in this order:

```text
Event Requests > Music Zones > Scene Music Managers > Fallback
```

Event requests return a `MusicRequestHandle`. Release that handle when the event ends so the next eligible music source can resume control.

Assign one or more Music Event Catalogs to the persistent `MusicManager`. It merges them into one case-sensitive Event ID registry and rejects duplicate IDs instead of choosing a catalog by list order. Gameplay code never needs a catalog reference:

```csharp
MusicRequestHandle handle = MusicManager.Instance.RequestEvent("Combat");
```

Use one catalog for a small project or split larger projects into focused catalogs such as Core, Combat, World, or DLC. Event IDs must remain unique across the complete manager list. The Music Manager inspector shows the resolved source catalog and every validation issue. By default, a playlist prepares lazily on its first request. Enable **Preload On Start** only on catalogs whose first event must start with the lowest possible latency.

Each Music Event has an **Interruption** mode:

- **Replace** selects the normal winning playlist and discards lower playback.
- **Suspend Previous** starts a fresh temporary playback session while preserving every lower session at its exact track position and playlist order. Releasing its handle stops the temporary session and resumes the previous one. Multiple suspend events can be nested; priority decides their order.

### Pause-menu music

Create separate `Music` and `Menu Music` mixer groups. In `AudioManager > Mixer Channel Controls`, leave **Ignore Global Pause** disabled for `Music` and enable it for `Menu Music`. Assign the pause-menu playlist to `Menu Music`, add a `Pause Menu` entry to a catalog registered in `MusicManager`, give it a high priority, and set **Interruption** to **Suspend Previous**.

```csharp
private MusicRequestHandle pauseMenuMusic;
private bool pauseMenuOpen;

public void OpenPause()
{
    var musicManager = MusicManager.Instance;
    if (pauseMenuOpen || musicManager == null)
    {
        return;
    }

    pauseMenuOpen = true;
    FeedbackService.PauseAll();
    pauseMenuMusic = musicManager.RequestEvent("Pause Menu");
}

public void ClosePause()
{
    ReleasePause();
}

private void OnDisable()
{
    ReleasePause();
}

private void ReleasePause()
{
    if (!pauseMenuOpen)
    {
        return;
    }

    pauseMenuOpen = false;
    pauseMenuMusic.Release();
    pauseMenuMusic = default;
    FeedbackService.ResumeAll();
}
```

The gameplay track is frozen by the global pause and by the suspend session. The menu track can still start because its mixer group ignores only the global pause. On close—or if the controller is disabled or destroyed—the menu request is released and gameplay music resumes from the same playback position after the matching `ResumeAll()` call. `pauseMenuMusic = default` only clears the local token after release, preventing stale state from being mistaken for an owned request.

Use a short or zero event transition for a crisp pause-menu switch. Set the menu playlist to **Sequential** with its intended opening track first (or use a one-track playlist) when it must restart with the same song every time. Do not use `AudioListener.pause` for this flow because it pauses excluded mixer groups too.

A suspended music session normally keeps its current pooled AudioSource while paused and can temporarily retain additional transition tails. `AudioManager.Maximum Capacity` must therefore allow the gameplay track, the temporary menu track, any nested music events, transition tails, and concurrent sound effects; the default capacity is normally ample.

## Runtime assemblies

- `EasyFeedbacks.Runtime`
- `EasyFeedbacks.Editor`

Scripts in another custom assembly definition must reference `EasyFeedbacks.Runtime`.

## Content ownership

The package includes framework code and a small set of general-purpose camera shake and gamepad vibration starting profiles. Game-specific presets, audio clips, playlists, particle prefabs, VFX prefabs, customized profiles, and scenes belong to the consuming project.

## License

Easy Feedbacks is available under the [MIT License](LICENSE.md).
