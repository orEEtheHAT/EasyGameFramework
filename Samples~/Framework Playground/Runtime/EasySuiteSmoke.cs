using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyBuilding;
using EasyCombat;
using EasyCombat.Integrations;
using EasyFeedbacks;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyInteraction.Integrations.Building;
using EasyResources;
using EasyUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;
using ResourceDefinition = EasyResources.ResourceDefinition;

namespace EasySuite.Example
{
    /// <summary>
    /// Opt-in shared-scene smoke fixture, also usable in a standalone player.
    /// StartRun or -easySuiteSmoke <report.json> is required. No Update, device or event work otherwise.
    /// This deliberately exercises gameplay, then restores an in-memory snapshot; it never reads/writes the normal save file.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EasySuiteSmoke : MonoBehaviour
    {
        public EasySuiteLab Lab;
        [Range(30, 300)] public float MaximumRunSeconds = 120;
        [Range(30, 240)] public int WarmFrameCount = 90;
        public bool IsRunning { get; private set; }
        public EasySuiteSmokeReport Report { get; private set; }
        public string ReportJson => Report == null ? "" : JsonUtility.ToJson(Report, true);

        private Coroutine driver;
        private CancellationTokenSource cancellation;
        private Task pendingOperation;
        private bool finishing, abort, quit, inputCaptured, environmentCaptured;
        private string reportPath;
        private double began, deadline;
        private EasySuiteSmokeCheck current;
        private EasySave.SaveSnapshot originalWorld, originalPreferences;
        private EasySuiteSave saves;
        private PlayerInputSettings player;
        private InputActionAsset actions;
        private InputSettingsSnapshot originalInput;
        private InputDevice[] originalDevices;
        private bool originallyRestricted;
        private Keyboard keyboard, previousKeyboard;
        private Mouse mouse, previousMouse;
        private FeedbackSettingsState originalFeedback;
        private float originalTimeScale;
        private bool originalRunInBackground;
        private InputSettings.BackgroundBehavior originalBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInput;
#endif
        private PlacedBuilding placed;
        private string placedInstanceId;
        private readonly List<EasySuiteSmokeCheck> expected = new List<EasySuiteSmokeCheck>();

        private void Start()
        {
            if (IsRunning) return;
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], "-easySuiteSmoke", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i + 1]) || arguments[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    Debug.LogError("-easySuiteSmoke requires a JSON report path. No smoke checks were started.");
                    if (!Application.isEditor) Application.Quit(2);
                    return;
                }
                StartRun(arguments[i + 1], true);
                return;
            }
        }

        /// <summary>Call in Play Mode. With a null path the report stays in memory for the Editor caller.</summary>
        public bool StartRun(string outputPath = null, bool quitWhenComplete = false)
        {
            if (IsRunning) return false;
            if (!Application.isPlaying || !gameObject.activeInHierarchy)
                throw new InvalidOperationException("Start the smoke fixture on an active GameObject in Play Mode.");
            reportPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
            enabled = true;
            IsRunning = true; finishing = abort = inputCaptured = environmentCaptured = false;
            quit = quitWhenComplete; placed = null; placedInstanceId = null; originalWorld = null; originalPreferences = null; pendingOperation = null;
            began = Time.realtimeSinceStartupAsDouble;
            deadline = began + Mathf.Clamp(MaximumRunSeconds, 30, 300);
            cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(TimeSpan.FromSeconds(Mathf.Clamp(MaximumRunSeconds, 30, 300) + 5));
            Report = new EasySuiteSmokeReport
            {
                StartedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(), OperatingSystem = SystemInfo.operatingSystem,
                IsEditor = Application.isEditor, IsDevelopmentBuild = Debug.isDebugBuild,
                InputUpdateMode = InputSystem.settings.updateMode.ToString(), OutputPath = reportPath ?? "(memory only)"
            };
            expected.Clear();
            AddExpected("ready-and-fixture", "public API / scoped fixture");
            AddExpected("shared-input-and-context", "public link resolution");
            AddExpected("keyboard-movement", "native Input System state events");
            AddExpected("menu-child-and-close", "native keyboard open/cancel; child opened through public API");
            AddExpected("building-session-and-charge", "public API placement; authored zone/costs");
            AddExpected("paid-ability-release", "public API; actual scheduler delay and damage");
            AddExpected("active-paid-cast-restore", "composed snapshot API; actual cast completion and resumed delayed payment");
            AddExpected("grab-and-release", "public API; real Rigidbody fixture");
            AddExpected("memory-death-restore", "public snapshot API; no save file");
            AddExpected("language", "native Localization API");
            AddExpected("projectile-impact-burn", "public emitter API; real projectile/area/status simulation");
            AddExpected("warm-frames", "whole-player observation, not subsystem allocation profiling");
            Application.logMessageReceived += CaptureLog;
            driver = StartCoroutine(Run());
            return true;
        }

        private void AddExpected(string name, string method)
        {
            var item = new EasySuiteSmokeCheck { Name = name, Method = method, Status = "not-run" };
            expected.Add(item); Report.Checks.Add(item);
        }

        private IEnumerator Run()
        {
            yield return Execute(expected[0], Prepare(), 35);
            yield return Execute(expected[1], SharedInput(), 5);
            yield return Execute(expected[2], MoveByKeyboard(), 7);
            yield return Execute(expected[3], MenusByKeyboard(), 15);
            yield return Execute(expected[4], BuildAndPay(), 12);
            yield return Execute(expected[5], PaidAbility(), 10);
            yield return Execute(expected[6], ActivePaidCastRestore(), 14);
            yield return Execute(expected[7], Grab(), 8);
            yield return Execute(expected[8], MemoryRestore(), 20);
            yield return Execute(expected[9], Language(), 15);
            yield return Execute(expected[10], Projectile(), 12);
            yield return Execute(expected[11], WarmFrames(), 12);
            driver = null;
            _ = FinishAsync();
        }

        // Flatten nested enumerators so exceptions/deadlines from every check reach this report.
        private IEnumerator Execute(EasySuiteSmokeCheck check, IEnumerator body, float seconds)
        {
            if (abort) { check.Status = "skipped"; check.Details.Add("An earlier required check failed."); yield break; }
            current = check; check.Status = "running";
            double start = Time.realtimeSinceStartupAsDouble;
            double end = Math.Min(deadline, start + seconds);
            var stack = new Stack<IEnumerator>(); stack.Push(body);
            Exception failure = null;
            while (stack.Count > 0)
            {
                object yielded = null;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException("Check exceeded its " + seconds + " second wall-clock budget.");
                    var iterator = stack.Peek();
                    if (!iterator.MoveNext()) { (iterator as IDisposable)?.Dispose(); stack.Pop(); continue; }
                    yielded = iterator.Current;
                    if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) break;
                yield return yielded;
            }
            while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception exception) { failure ??= exception; } }
            ReleaseSyntheticControls();
            check.DurationSeconds = Time.realtimeSinceStartupAsDouble - start;
            if (failure != null)
            {
                check.Status = "failed"; check.Error = failure.ToString(); abort = true; cancellation.Cancel();
            }
            else if (check.Status == "running") check.Status = "passed";
            current = null;
        }

        private IEnumerator Prepare()
        {
            if (!Lab) Lab = GetComponent<EasySuiteLab>();
            if (!Lab) Lab = Object.FindFirstObjectByType<EasySuiteLab>();
            Require(Lab, "No EasySuiteLab is loaded.");
            yield return Until(() => Lab && Lab.Ready && !Lab.IsRestoring, 30, "Lab.Ready");
            Require(Object.FindObjectsByType<PlayerInputSettings>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Count(value => value.isActiveAndEnabled) == 1,
                "Smoke requires one effective player. It will not inject a keyboard into a separate local-player session.");
            Require(!Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Any(value => value.isActiveAndEnabled),
                "An extra Unity PlayerInput could consume the synthetic keyboard. Close that session before smoke.");
            Require(InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsManually,
                "Manual Input System updates are unsupported by this frame-driven smoke fixture.");
            player = Lab.Player; actions = player.Actions; saves = Lab.Saves;
            Require(player && actions && saves != null, "The lab player/actions/save composition is missing.");
            Require(player.Rebind == null || !player.Rebind.IsActive, "Finish the current interactive rebind before starting smoke.");
            originalTimeScale = Time.timeScale; originalRunInBackground = Application.runInBackground;
            originalFeedback = FeedbackSettings.Capture(); environmentCaptured = true;
            originalBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            originalEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            Application.runInBackground = true; Time.timeScale = 1;
            Lab.Windows.CloseAll(); Lab.Actor.Cancel("Smoke fixture"); Lab.BuildingInput.Exit();
            yield return Frames(2);
            originalWorld = saves.Capture();
            originalPreferences = saves.CapturePreferences();
            originalInput = player.CaptureSettings();
            originallyRestricted = actions.devices.HasValue;
            originalDevices = originallyRestricted ? actions.devices.Value.ToArray() : null;
            previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
            inputCaptured = true;
            var quiet = originalFeedback; quiet.HapticsEnabled = false; FeedbackSettings.Apply(quiet);
            keyboard = InputSystem.AddDevice<Keyboard>("EasySuiteSmokeKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("EasySuiteSmokeMouse");
            var preferences = player.Settings; preferences.DevicePreference = InputDevicePreference.KeyboardMouse; player.SetSettings(preferences);
            actions.devices = new InputDevice[] { keyboard, mouse };
            // PairDevices is deliberately not called: its private pairing policy must remain intact.
            ReleaseSyntheticControls(); yield return Frames(3);
            Require(actions.devices.HasValue && actions.devices.Value.Count == 2 && actions.devices.Value.Contains(keyboard) && actions.devices.Value.Contains(mouse),
                "The temporary device restriction was changed by another owner.");
            Require(Lab.PlayerHealth.IsStanding && Lab.Actor.isActiveAndEnabled, "Start smoke with a living, enabled lab player.");
            Note("Only this player's runtime Actions.devices is temporarily restricted. Existing pairing policy is untouched.");
            Note("Haptics are disabled for the fixture. No physical motor or audible-output assessment is performed.");
        }

        private IEnumerator SharedInput()
        {
            var movement = Lab.Character.GetComponent<EasyMovementInput>();
            var interaction = Lab.Actor.GetComponent<EasyInteractionInput>();
            var combat = Lab.Weapon.GetComponent<EasyCombatInput>();
            Require(movement && interaction && combat && Lab.BuildingInput, "A required input adapter is missing.");
            Require(movement.Player == player && interaction.Player == player && combat.Player == player && Lab.BuildingInput.Player == player,
                "All adapters must reference the same PlayerInputSettings.");
            Require(movement.Move.Resolve(player, true).actionMap.asset == actions && movement.Look.Resolve(player, true).actionMap.asset == actions
                && interaction.Interact.Resolve(player, true).actionMap.asset == actions && Lab.BuildingInput.Confirm.Resolve(player, true).actionMap.asset == actions,
                "Public adapter links did not resolve into the shared runtime asset.");
            int before = player.ContextRequestCount;
            using (var own = player.AcquireContext(this, new InputContextRequest { BlockedActions = new[] { "Gameplay/Fire" } }))
            {
                Require(player.ContextRequestCount == before + 1, "Context acquisition was not independent.");
                Require(!player.Action("Gameplay/Fire").enabled && player.Action("Gameplay/Move").enabled, "The owned action block leaked into movement.");
            }
            yield return Frames(2);
            Require(player.ContextRequestCount == before, "The fixture failed to release only its own context.");
            Note("Shared asset: " + actions.name + "; context count before/after: " + before + ".");
        }

        private IEnumerator MoveByKeyboard()
        {
            var move = player.Action("Gameplay/Move");
            var key = ForwardKey(move);
            var start = Lab.Character.transform.position;
            int frame = Time.frameCount;
            Press(key); yield return Frames(3);
            Require(move.activeControl?.device == keyboard && move.ReadValue<Vector2>().y > .25f, "The synthetic keyboard did not drive the player's Move action.");
            yield return Seconds(.35f);
            ReleaseSyntheticControls(); yield return Frames(3);
            float distance = Vector3.ProjectOnPlane(Lab.Character.transform.position - start, Vector3.up).magnitude;
            Require(Time.frameCount - frame >= 5 && distance > .03f, "Input was reported but the live FPS motor did not move.");
            Note("Keyboard " + key + "; observed planar movement " + distance.ToString("F3") + " m over " + (Time.frameCount - frame) + " advancing frames.");
        }

        private IEnumerator MenusByKeyboard()
        {
            Lab.Windows.CloseAll(); yield return Frames(2);
            int baseline = player.ContextRequestCount;
            yield return Tap(KeyboardKey(player.Action("Suite/Menu")));
            yield return Until(() => Lab.Windows.Count == 1 && Lab.Windows.InputReady, 4, "menu opened and release fence cleared");
            var parent = Lab.Windows.ActiveWindow;
            Require(parent != null && parent.IsOpen && player.ContextRequestCount == baseline + 1, "The menu did not own exactly one context.");
            Require(!player.Action("Gameplay/Move").enabled, "The blocking menu did not block gameplay movement.");
            Require(Lab.Windows.InputModule && Lab.Windows.InputModule.actionsAsset == actions, "UI does not use the same runtime action asset.");
            Lab.OpenControls(parent); // Child construction is an API step; closing below uses real input.
            yield return Until(() => Lab.Windows.Count == 2 && Lab.Windows.InputReady, 4, "child controls window");
            Require(Lab.Windows.ActiveWindow.Parent == parent && player.ContextRequestCount == baseline + 2, "Child context ownership is wrong.");
            yield return Tap(KeyboardKey(player.Action("UI/Cancel")));
            yield return Until(() => Lab.Windows.Count == 1 && Lab.Windows.InputReady, 4, "native child Back");
            Require(parent.IsOpen && Lab.Windows.ActiveWindow == parent && player.ContextRequestCount == baseline + 1,
                "Closing the child released or replaced the parent's context.");
            yield return Tap(KeyboardKey(player.Action("Suite/Menu")));
            yield return Until(() => Lab.Windows.Count == 0 && player.ContextRequestCount == baseline && player.Action("Gameplay/Move").enabled, 3, "menu close and gameplay return");
            Note("Native keyboard opened the menu, UI/Cancel closed the child, and the menu action closed the parent. Child creation used Lab.OpenControls.");
        }

        private IEnumerator BuildAndPay()
        {
            var actor = Lab.Actor.GetComponent<BuildingActor>();
            var interaction = Lab.Actor.GetComponent<BuildingInteractionContext>();
            Require(actor && interaction, "Building Actor/Interaction Context is missing.");
            var session = Lab.Placer.Session;
            Require(ReferenceEquals(session, actor.GetSession(Lab.Placer.System)) && ReferenceEquals(session, interaction.GetSession(Lab.Placer.System))
                && ReferenceEquals(session.Actor, actor), "Placement and interaction use different actor sessions.");
            var definition = Lab.Buildings.Where(value => value && value.ConfigurationError == null && value.Payment == BuildingPayment.Paid
                && value.CostBasis == BuildingCostBasis.Fixed && value.WorkRequired <= 0 && value.Placement != PlacementMode.Socket && value.Costs.Length > 0)
                .OrderBy(value => value.Costs.Sum(cost => (decimal)cost.Amount)).FirstOrDefault();
            Require(definition, "No supported fixed-price building in the lab catalog.");
            Require(Lab.Placer.System.PriceModifier == null, "This fixture expects the lab's unmodified authored fixed price.");
            var costs = BuildingCosts(definition);
            foreach (var row in costs)
            {
                long missing = row.Value - Lab.Wallet.GetAvailable(row.Key);
                if (missing > 0) { Lab.Wallet.Add(row.Key, missing); Note("Fixture topped up " + row.Key.name + " before measuring payment."); }
            }
            Require(session.Select(definition), "The canonical actor session rejected selection.");
            yield return Frames(3);
            Require(Lab.BuildingInput.IsBuilding && Lab.BuildingInput.Mode == BuildingInputMode.FirstPerson, "Selecting the catalog definition did not enter FPS building mode.");
            Require(player.Action("Gameplay/Move").enabled && player.Action("Gameplay/Look").enabled && !player.Action("Gameplay/Fire").enabled,
                "FPS building must preserve Move/Look while blocking Fire.");
            var request = FindPlacement(definition, session);
            var before = costs.ToDictionary(row => row.Key, row => Lab.Wallet.GetTotal(row.Key));
            int count = Lab.Placer.System.Count;
            Require(Lab.Placer.TryConfirm(request, out placed, out var result), "API placement failed: " + result.Reason);
            Require(placed && Lab.Placer.System.Count == count + 1, "Exactly one building was expected.");
            placedInstanceId = placed.InstanceId;
            yield return Frames(3);
            foreach (var row in costs) Require(before[row.Key] - Lab.Wallet.GetTotal(row.Key) == row.Value, "Building cost was omitted or charged more than once for " + row.Key.name + ".");
            Require(Lab.Placer.System.Count == count + 1, "A held input or confirmation callback placed an extra building.");
            Lab.BuildingInput.Exit(); yield return Frames(2);
            Require(!Lab.BuildingInput.IsBuilding && ReferenceEquals(session, Lab.Placer.Session) && !session.IsDisposed, "Exit disposed the borrowed actor session.");
            Note("Selected " + definition.Name + " through the actor session; confirmed with BuildingPlacer.TryConfirm at " + request.Position + ". Costs use authored raw units, not an assumed precision.");
        }

        private IEnumerator PaidAbility()
        {
            Lab.BuildingInput.Exit(); Lab.Windows.CloseAll(); Lab.ResetTarget();
            var provider = Lab.Abilities.GetComponent<EasyResourcesCostProvider>();
            Require(provider && provider.Container == Lab.Wallet && provider.Mapping, "Paid ability must spend the displayed shared wallet.");
            Require(provider.Mapping.TryConvert(Lab.PaidAbility.Costs, ReadOnlySpan<EasyCombat.ResourceCost>.Empty, out var cost, out var error), "Invalid ability mapping: " + error);
            long charge = Lab.Mana.ToUnits(2.5m);
            Require(cost.GetAmount(Lab.Mana) == charge, "The lab paid ability is not authored for exactly 2.5 whole energy.");
            var damage = Lab.PaidAbility.Actions.OfType<AbilityAttackAction>().SingleOrDefault();
            Require(damage && damage.DirectToTarget && Mathf.Approximately(damage.Attack.MinimumDamage, 15) && Mathf.Approximately(damage.Attack.MaximumDamage, 15), "The lab energy strike must apply a deterministic direct 15 damage.");
            long missing = charge - Lab.Wallet.GetAvailable(Lab.Mana); if (missing > 0) Lab.Wallet.Add(Lab.Mana, missing);
            yield return Until(() => !Lab.Abilities.IsCasting && AbilityCooldownReady(), 4, "paid ability cooldown");
            long balance = Lab.Wallet.GetTotal(Lab.Mana); float health = Lab.TargetHealth.Current;
            Require(Lab.PaidAbility.CastTime > 0, "The paid ability must have a real windup.");
            Require(Lab.Abilities.TryActivate(Lab.PaidAbility, Lab.TargetHealth.transform.position, Lab.TargetHealth, out var reason), "Ability activation failed: " + reason);
            Require(Lab.Abilities.IsCasting && Lab.Wallet.GetTotal(Lab.Mana) == balance && Mathf.Approximately(Lab.TargetHealth.Current, health),
                "The ability charged or damaged before its delayed release.");
            yield return Until(() => !Lab.Abilities.IsCasting, Mathf.Min(6, Lab.PaidAbility.CastTime + 3), "paid ability release");
            Require(balance - Lab.Wallet.GetTotal(Lab.Mana) == charge, "The ability did not charge exactly 2.5 whole energy once.");
            Require(Mathf.Abs(health - Lab.TargetHealth.Current - 15) < .001f, "The energy strike did not apply exactly 15 damage.");
            yield return Frames(3);
            Require(balance - Lab.Wallet.GetTotal(Lab.Mana) == charge && Lab.Wallet.ActiveReservationCount == 0, "A delayed callback charged again or left a reservation.");
            Note("Verified delayed payment of " + charge + " raw units = " + Lab.Mana.FromUnits(charge) + " whole energy and 15 target HP.");
        }

        private IEnumerator ActivePaidCastRestore()
        {
            Lab.ResetTarget();
            yield return Until(() => !Lab.Abilities.IsCasting && AbilityCooldownReady(), 4, "active-cast fixture cooldown");
            long balance = Lab.Wallet.GetTotal(Lab.Mana), charge = Lab.Mana.ToUnits(2.5m);
            float health = Lab.TargetHealth.Current;
            int contexts = player.ContextRequestCount;
            Require(balance >= charge, "The active-cast fixture needs its authored energy cost available.");
            Require(Lab.Abilities.TryActivate(Lab.PaidAbility, Lab.TargetHealth.transform.position, Lab.TargetHealth, out var reason), "Active-cast fixture activation failed: " + reason);
            Require(Lab.Abilities.IsCasting, "A live windup is required at the snapshot boundary.");
            var snapshot = saves.Capture();
            Require(Lab.Abilities.IsCasting && Lab.Wallet.GetTotal(Lab.Mana) == balance && Mathf.Abs(Lab.TargetHealth.Current - health) < .001f,
                "Capturing the active cast cancelled, charged, or delivered it early.");
            yield return Until(() => !Lab.Abilities.IsCasting, 4, "original captured cast completion");
            Require(balance - Lab.Wallet.GetTotal(Lab.Mana) == charge && Mathf.Abs(health - Lab.TargetHealth.Current - 15) < .001f,
                "The original captured cast did not complete with one payment and one hit.");
            yield return Await(saves.RestoreAsync(snapshot, cancellation.Token));
            actions.devices = new InputDevice[] { keyboard, mouse };
            Require(Lab.Abilities.IsCasting && Lab.Wallet.GetTotal(Lab.Mana) == balance && Mathf.Abs(Lab.TargetHealth.Current - health) < .001f,
                "Root restore did not recover the active windup, unspent wallet, and undamaged target together.");
            yield return Until(() => !Lab.Abilities.IsCasting, 4, "restored captured cast completion");
            Require(balance - Lab.Wallet.GetTotal(Lab.Mana) == charge && Mathf.Abs(health - Lab.TargetHealth.Current - 15) < .001f,
                "The restored cast charged or delivered damage more or less than once.");
            yield return Frames(3);
            Require(balance - Lab.Wallet.GetTotal(Lab.Mana) == charge && Lab.Wallet.ActiveReservationCount == 0 && !Lab.IsRestoring && !Lab.Character.InputBlocked
                && player.ContextRequestCount == contexts && player.Action("Gameplay/Move").enabled,
                "Resuming the captured cast left duplicate payment, an orphan hold, or a restore/input gate.");
            Note("Captured during live paid windup, allowed the original release, restored the whole snapshot, then observed exactly one resumed 250 raw / 2.5 energy charge and 15 HP hit. No file storage or allocation claim.");
        }

        private IEnumerator Grab()
        {
            var grab = Lab.Crate ? Lab.Crate.GetComponent<GrabAction>() : null;
            if (!grab) { current.Status = "skipped"; Note("Optional crate GrabAction is absent; pickup behavior was not measured."); yield break; }
            Lab.Actor.Cancel("Smoke grab");
            var body = grab.Body; Require(body && !body.isKinematic, "The crate must have a dynamic Rigidbody.");
            bool gravity = body.useGravity;
            var position = Lab.Actor.View.position + Lab.Actor.View.forward * Mathf.Min(1.5f, grab.GrabRange * .5f);
            // Rigidbody interpolation can leave the target's Transform/AimPosition at its old pose until a physics step.
            body.transform.position = position; body.position = position;
            body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            var availability = Lab.Actor.CheckDirect(grab);
            Require(availability.IsAvailable, "Fixture crate is not available: " + availability.Code + ": " + availability.Reason);
            Require(Lab.Actor.TryExecuteDirect(grab), "The public interaction API could not grab the nearby fixture crate: " + Lab.Actor.LastFailure.Reason);
            yield return Frames(3);
            Require(grab.IsHeld && grab.Owns(Lab.Actor) && !body.useGravity, "Grab did not own the Rigidbody and suspend gravity.");
            Require(Lab.Actor.Cancel(grab, "Smoke release"), "The actor could not release its grab.");
            yield return Frames(2);
            Require(!grab.IsHeld && body.useGravity == gravity, "Release did not restore the Rigidbody's gravity/ownership.");
            Note("Moved the fixture crate within authored grab range, then used Actor.TryExecuteDirect/Cancel. This is an API pickup check, not keyboard target acquisition.");
        }

        private IEnumerator MemoryRestore()
        {
            Lab.Windows.CloseAll(); Lab.Actor.Cancel("Smoke snapshot"); Lab.BuildingInput.Exit();
            var memory = saves.Capture();
            string inputBeforeLoad = JsonUtility.ToJson(player.CaptureSettings());
            string languageBeforeLoad = EasyLocalization.Localization.LanguageCode;
            string audioBeforeLoad = JsonUtility.ToJson(FeedbackSettings.Capture());
            long wood = Lab.Wallet.GetTotal(Lab.Wood), energy = Lab.Wallet.GetTotal(Lab.Mana);
            long stockWood = Lab.Stock.GetTotal(Lab.Wood), stockEnergy = Lab.Stock.GetTotal(Lab.Mana);
            int buildings = Lab.Placer.System.Count; float health = Lab.PlayerHealth.Current;
            int contexts = player.ContextRequestCount;
            Require(buildings > 0 && !string.IsNullOrEmpty(placedInstanceId) && Lab.Placer.System.TryGet(placedInstanceId, out placed),
                "The earlier placement's stable instance ID is required for the restore check.");
            Lab.KillPlayer(); yield return Frames(2);
            Require(!Lab.PlayerHealth.IsStanding && !Lab.Actor.isActiveAndEnabled, "Death did not disable the interaction actor.");
            Lab.AddResources(); Lab.Placer.System.Remove(placed);
            yield return Frames(2);
            yield return Await(saves.RestoreAsync(memory, cancellation.Token));
            Require(Mathf.Abs(Lab.PlayerHealth.Current - health) < .001f && Lab.Actor.isActiveAndEnabled, "Restore failed to revive health and interaction.");
            Require(Lab.Placer.System.Count == buildings && Lab.Wallet.GetTotal(Lab.Wood) == wood && Lab.Wallet.GetTotal(Lab.Mana) == energy
                && Lab.Stock.GetTotal(Lab.Wood) == stockWood && Lab.Stock.GetTotal(Lab.Mana) == stockEnergy, "Restore did not recover exact balances/buildings.");
            yield return Await(saves.RestoreAsync(memory, cancellation.Token));
            yield return Frames(2);
            Require(Lab.Placer.System.Count == buildings && Lab.Wallet.GetTotal(Lab.Wood) == wood && Lab.Wallet.GetTotal(Lab.Mana) == energy
                && Lab.Stock.GetTotal(Lab.Wood) == stockWood && Lab.Stock.GetTotal(Lab.Mana) == stockEnergy, "Repeating the same restore duplicated balances or buildings.");
            Require(!Lab.Character.InputBlocked && !Lab.IsRestoring && Lab.Wallet.ActiveReservationCount == 0
                && player.ContextRequestCount == contexts && player.Action("Gameplay/Move").enabled,
                "Restore left an owned input block, restore flag, or reservation.");
            Require(inputBeforeLoad == JsonUtility.ToJson(player.CaptureSettings()) && languageBeforeLoad == EasyLocalization.Localization.LanguageCode &&
                audioBeforeLoad == JsonUtility.ToJson(FeedbackSettings.Capture()), "Progress restore changed global preferences.");
            // Progress never restores input preferences; retain only the temporary device filter.
            actions.devices = new InputDevice[] { keyboard, mouse };
            Note("Memory-only capture, death, balance/building mutation, then two identical restores. No normal save file was opened.");
        }

        private IEnumerator Language()
        {
            string before = EasyLocalization.Localization.LanguageCode;
            var next = EasyLocalization.Localization.Languages.FirstOrDefault(locale => locale && locale.Identifier.Code != before);
            Require(next, "The lab needs a second native Localization locale.");
            yield return Await(EasyLocalization.Localization.SetLanguageAsync(next.Identifier.Code, cancellation.Token));
            Require(EasyLocalization.Localization.LanguageCode == next.Identifier.Code, "Native locale change did not commit.");
            if (Lab.Texts) yield return Await(Lab.Texts.RefreshAsync());
            Note("Native locale changed from " + before + " to " + next.Identifier.Code + "; translated cache refreshed.");
        }

        private IEnumerator Projectile()
        {
            var attack = Lab.Weapon.Attack;
            if (!attack || attack.Delivery != DamageDelivery.Projectile)
            { current.Status = "skipped"; Note("The configured weapon is not a projectile; no fireball claim is made."); yield break; }
            Lab.ResetTarget();
            var collider = Lab.TargetHealth.GetComponentsInChildren<Collider>().FirstOrDefault(value => value.enabled && !value.isTrigger && value.bounds.center.y > Lab.TargetHealth.transform.position.y + .4f);
            Require(collider, "No enabled target body collider was found.");
            var target = collider.bounds.center;
            var direction = (target - Lab.Character.LookPivot.position).normalized;
            float distance = Mathf.Min(3, attack.Range * .25f);
            var origin = target - direction * (distance + collider.bounds.extents.magnitude);
            Require(Lab.Weapon.Pool && Lab.Weapon.AreaPool, "Projectile and area pools are required for the fireball.");
            float before = Lab.TargetHealth.Current;
            int areasBefore = Lab.Weapon.AreaPool.ActiveCount;
            bool sawArea = false, sawStatus = false, sawProjectile = false;
            Require(Lab.Weapon.TryFire(origin, direction), "The public emitter rejected a controlled shot near the target.");
            double end = Time.realtimeSinceStartupAsDouble + 7;
            while (Time.realtimeSinceStartupAsDouble < end)
            {
                sawProjectile |= Lab.Weapon.Pool.ActiveCount > 0;
                sawArea |= Lab.Weapon.AreaPool.ActiveCount > areasBefore;
                sawStatus |= Lab.TargetHealth.StatusCount > 0;
                if (Lab.TargetHealth.Current < before && sawStatus && (!attack.ImpactArea || sawArea)) break;
                yield return null;
            }
            Require(sawProjectile && Lab.TargetHealth.Current < before, "A live projectile did not reach and damage the target.");
            Require(sawStatus, "The projectile did not apply a target status (burn).");
            if (attack.ImpactArea) Require(sawArea, "The authored impact-area/puddle did not appear in the live area pool.");
            float afterImpact = Lab.TargetHealth.Current;
            yield return Until(() => Lab.TargetHealth.Current < afterImpact - .001f, 3, "continued burn/puddle damage");
            Note("Real projectile observed; target HP " + before + " -> " + Lab.TargetHealth.Current + "; active status=" + sawStatus + ", impact area=" + sawArea + ". Emitter origin was positioned near the target for deterministic fixture geometry; aiming input was not tested.");
        }

        private IEnumerator WarmFrames()
        {
            // Remove transient combat work through the lab's own restore before measuring a stable loaded scene.
            yield return Await(saves.RestoreAsync(originalWorld, cancellation.Token));
            var quiet = FeedbackSettings.Current; quiet.HapticsEnabled = false; FeedbackSettings.Apply(quiet);
            actions.devices = new InputDevice[] { keyboard, mouse };
            yield return Frames(20);
            var samples = new double[Mathf.Clamp(WarmFrameCount, 30, 240)];
            int frames = Time.frameCount;
            long memory = GC.GetTotalMemory(false);
            int collections = GC.CollectionCount(0);
            double started = Time.realtimeSinceStartupAsDouble, previous = started;
            for (int i = 0; i < samples.Length; i++)
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                samples[i] = (now - previous) * 1000; previous = now;
            }
            var metric = Report.WarmFrames;
            metric.FrameCount = samples.Length; metric.ElapsedSeconds = previous - started;
            metric.AdvancingFrameCount = Time.frameCount - frames;
            metric.ValidTiming = metric.AdvancingFrameCount >= samples.Length && metric.ElapsedSeconds > 0;
            metric.MeanMilliseconds = samples.Average(); Array.Sort(samples);
            metric.P95Milliseconds = samples[Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * .95) - 1)];
            metric.MaximumMilliseconds = samples[samples.Length - 1];
            metric.ManagedHeapDeltaBytes = GC.GetTotalMemory(false) - memory;
            metric.GenerationZeroCollections = GC.CollectionCount(0) - collections;
            metric.ValidWholeProcessGcCounters = true;
            Require(metric.ValidTiming, "The player loop did not advance through the warm sample.");
            Note("Whole loaded scene, " + samples.Length + " frames; heap delta/collection counters are process observations, not allocated bytes or proof of subsystem zero-GC.");
        }

        private async Task FinishAsync()
        {
            if (finishing) return; finishing = true;
            var cleanup = new EasySuiteSmokeCheck { Name = "cleanup", Method = "scoped fixture restoration", Status = "running" };
            Report.Checks.Add(cleanup);
            double started = Time.realtimeSinceStartupAsDouble;
            try
            {
                ReleaseSyntheticControls();
                cancellation?.Cancel();
                if (pendingOperation != null && !pendingOperation.IsCompleted)
                {
                    if (await Task.WhenAny(pendingOperation, Task.Delay(8000)) != pendingOperation)
                        throw new TimeoutException("A cancelled operation did not finish within the cleanup budget.");
                }
                // Observe cancelled/faulted task completion before starting another restore.
                if (pendingOperation != null) { try { await pendingOperation; } catch { } pendingOperation = null; }
                if (Lab && Lab.isActiveAndEnabled && saves != null && originalWorld != null)
                {
                    Lab.Windows.CloseAll(); Lab.BuildingInput.Exit(); Lab.Actor.Cancel("Smoke cleanup");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    await saves.RestoreAsync(originalWorld, timeout.Token);
                    if (originalPreferences != null) await saves.RestorePreferencesAsync(originalPreferences, timeout.Token);
                    Report.WorldSnapshotRestored = true;
                }
                else if (originalWorld != null) throw new InvalidOperationException("The lab was disabled/destroyed; full world restore is unavailable. Input/environment cleanup still runs.");
                cleanup.Status = "passed";
            }
            catch (Exception exception) { cleanup.Status = "failed"; cleanup.Error = exception.ToString(); }
            finally
            {
                try { RestoreInputAndEnvironment(); }
                catch (Exception exception) { cleanup.Status = "failed"; cleanup.Error += "\nInput/environment cleanup: " + exception; }
                cleanup.DurationSeconds = Time.realtimeSinceStartupAsDouble - started;
                Application.logMessageReceived -= CaptureLog;
                cancellation?.Dispose(); cancellation = null;
                foreach (var item in Report.Checks) if (item.Status == "not-run" || item.Status == "running") item.Status = "skipped";
                Report.CompletedUtc = DateTime.UtcNow.ToString("O");
                Report.DurationSeconds = Time.realtimeSinceStartupAsDouble - began;
                Report.Passed = Report.Checks.All(item => item.Status != "failed") && Report.UnityErrors.Count == 0 && Report.InputScopeRestored;
                Report.ExitCode = Report.Passed ? 0 : 2;
                IsRunning = false; driver = null;
                try
                {
                    if (reportPath != null)
                    {
                        string folder = Path.GetDirectoryName(reportPath);
                        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                        File.WriteAllText(reportPath, ReportJson, new System.Text.UTF8Encoding(false));
                    }
                }
                catch (Exception exception)
                {
                    Report.Passed = false; Report.ExitCode = 2;
                    Report.ReportWriteError = exception.ToString();
                    Debug.LogError("EasySuite smoke could not write the requested report: " + exception.Message);
                }
                if (quit && !Application.isEditor) Application.Quit(Report.ExitCode);
            }
        }

        private void RestoreInputAndEnvironment()
        {
            Exception failure = null;
            void Attempt(Action action) { try { action(); } catch (Exception exception) { failure ??= exception; } }
            Attempt(ReleaseSyntheticControls);
            // Removing only our virtual devices allows each player's unchanged pairing policy to refresh itself.
            if (keyboard != null && keyboard.added) Attempt(() => InputSystem.RemoveDevice(keyboard));
            if (mouse != null && mouse.added) Attempt(() => InputSystem.RemoveDevice(mouse));
            keyboard = null; mouse = null;
            if (inputCaptured && player)
            {
                Attempt(() => Require(player.RestoreSettings(originalInput, out var error), "Could not restore original input settings: " + error));
                if (actions) Attempt(() => actions.devices = originallyRestricted ? originalDevices.Where(device => device != null && device.added).ToArray() : null);
            }
            if (previousKeyboard != null && previousKeyboard.added) Attempt(previousKeyboard.MakeCurrent);
            if (previousMouse != null && previousMouse.added) Attempt(previousMouse.MakeCurrent);
            if (environmentCaptured)
            {
                Attempt(() => FeedbackSettings.Restore(originalFeedback));
                Time.timeScale = originalTimeScale;
                Application.runInBackground = originalRunInBackground;
                Attempt(() => InputSystem.settings.backgroundBehavior = originalBackground);
#if UNITY_EDITOR
                Attempt(() => InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInput);
#endif
            }
            Report.InputScopeRestored = failure == null && (!inputCaptured || player && actions);
            if (failure != null) throw new InvalidOperationException("One or more scoped settings could not be restored.", failure);
        }

        private void OnDisable()
        {
            if (!IsRunning || finishing) return;
            abort = true; cancellation?.Cancel();
            if (driver != null) StopCoroutine(driver);
            driver = null;
            var failed = current ?? new EasySuiteSmokeCheck { Name = "interrupted", Method = "lifecycle" };
            if (!Report.Checks.Contains(failed)) Report.Checks.Add(failed);
            failed.Status = "failed"; failed.Error = "Smoke component was disabled or destroyed before completion.";
            _ = FinishAsync(); // Task cleanup continues after component disable while a player loop remains available.
        }
        private void OnDestroy() => OnDisable();

        private void CaptureLog(string message, string stack, LogType type)
        {
            if (Report == null || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (Report.UnityErrors.Count < 20) Report.UnityErrors.Add(message + "\n" + stack);
        }

        private void ReleaseSyntheticControls()
        {
            if (keyboard != null && keyboard.added) InputSystem.ResetDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.ResetDevice(mouse);
        }
        private void Press(Key key) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        private IEnumerator Tap(Key key) { Press(key); yield return Frames(2); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(3); }
        private static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        private static IEnumerator Seconds(float seconds)
        { double end = Time.realtimeSinceStartupAsDouble + seconds; while (Time.realtimeSinceStartupAsDouble < end) yield return null; }
        private static IEnumerator Until(Func<bool> condition, float timeout, string description)
        {
            double end = Time.realtimeSinceStartupAsDouble + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException("Timed out waiting for " + description + ".");
                yield return null;
            }
        }
        private IEnumerator Await(Task task)
        {
            pendingOperation = task;
            while (!task.IsCompleted) { cancellation.Token.ThrowIfCancellationRequested(); yield return null; }
            task.GetAwaiter().GetResult(); pendingOperation = null;
        }
        private Key KeyboardKey(InputAction action)
        {
            var control = action.controls.OfType<KeyControl>().FirstOrDefault(value => value.device == keyboard);
            Require(control != null, "No keyboard control is resolved for " + action.actionMap.name + "/" + action.name + ".");
            return control.keyCode;
        }
        private Key ForwardKey(InputAction action)
        {
            foreach (var binding in action.bindings)
                if (binding.isPartOfComposite && string.Equals(binding.name, "up", StringComparison.OrdinalIgnoreCase))
                    foreach (var key in keyboard.allKeys)
                        if (!string.IsNullOrEmpty(binding.effectivePath) && InputControlPath.Matches(binding.effectivePath, key)) return key.keyCode;
            throw new InvalidOperationException("Move needs a keyboard 'up' composite part for this native-input fixture; no binding IDs or fixed keys are assumed.");
        }
        private bool AbilityCooldownReady()
        {
            for (int i = 0; i < Lab.Abilities.Count; i++)
            {
                var view = Lab.Abilities.GetAbility(i);
                if (view.Definition == Lab.PaidAbility) return view.Cooldown <= 0 && Lab.Abilities.GlobalCooldownRemaining <= 0;
            }
            return false;
        }
        private Dictionary<ResourceDefinition, long> BuildingCosts(BuildingDefinition definition)
        {
            var result = new Dictionary<ResourceDefinition, long>();
            foreach (var cost in definition.Costs)
            {
                var resource = Lab.BuildingResources.Resources.FirstOrDefault(value => value && value.Id == cost.ResourceId);
                if (!resource) foreach (var alias in Lab.BuildingResources.Aliases) if (alias.Id == cost.ResourceId) { resource = alias.Resource; break; }
                Require(resource, "Building cost ID is not mapped: " + cost.ResourceId);
                result.TryGetValue(resource, out long amount); result[resource] = checked(amount + cost.Amount);
            }
            return result;
        }
        private PlacementRequest FindPlacement(BuildingDefinition definition, BuildingSession session)
        {
            var zones = Object.FindObjectsByType<BuildingZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(zone => zone.isActiveAndEnabled && (!definition.RequireZone || zone.ZoneId == definition.ZoneId));
            string last = "no matching zone";
            foreach (var zone in zones)
                for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
                {
                    var local = zone.Center + new Vector3(x * zone.Size.x * .15f, -zone.Size.y * .5f, z * zone.Size.z * .15f);
                    var origin = zone.transform.TransformPoint(local) + Vector3.up * 3;
                    var hits = Physics.RaycastAll(origin, Vector3.down, 6, definition.SurfaceLayers, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance);
                    foreach (var hit in hits)
                    {
                        if (hit.collider.transform.IsChildOf(Lab.Character.transform)) continue;
                        var request = new PlacementRequest(hit.point, Quaternion.identity, space: hit.collider.GetComponentInParent<BuildingSpace>(), surfaceNormal: hit.normal);
                        var check = session.Check(request); if (check.IsValid) return request; last = check.Reason;
                    }
                }
            throw new InvalidOperationException("No legal authored-zone placement was found: " + last);
        }
        private void Note(string detail) => current?.Details.Add(detail);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    [Serializable]
    public sealed class EasySuiteSmokeReport
    {
        public int Version = 1;
        public string StartedUtc, CompletedUtc, UnityVersion, Platform, OperatingSystem, InputUpdateMode, OutputPath, ReportWriteError;
        public bool IsEditor, IsDevelopmentBuild, Passed, WorldSnapshotRestored, InputScopeRestored;
        public int ExitCode;
        public double DurationSeconds;
        public List<EasySuiteSmokeCheck> Checks = new List<EasySuiteSmokeCheck>();
        public List<string> UnityErrors = new List<string>();
        public EasySuiteWarmMetrics WarmFrames = new EasySuiteWarmMetrics();
        public string Limits = "Native keyboard validates only movement/menu/Back. Placement, grab, ability and fire use public APIs and are labeled. Haptics are suppressed; no physical output claims. Whole-player timing/heap counters do not prove subsystem zero-GC. Deadlines require an advancing Unity player loop; no code can recover a hung native main thread. The ordinary save file is never used.";
    }
    [Serializable]
    public sealed class EasySuiteSmokeCheck
    {
        public string Name, Method, Status, Error;
        public double DurationSeconds;
        public List<string> Details = new List<string>();
    }
    [Serializable]
    public sealed class EasySuiteWarmMetrics
    {
        public bool ValidTiming, ValidWholeProcessGcCounters;
        public int FrameCount, AdvancingFrameCount, GenerationZeroCollections;
        public double ElapsedSeconds, MeanMilliseconds, P95Milliseconds, MaximumMilliseconds;
        public long ManagedHeapDeltaBytes;
        public string Scope = "Whole loaded scene plus smoke observation; managed heap delta is not allocated bytes or a subsystem allocation measure.";
    }
}
