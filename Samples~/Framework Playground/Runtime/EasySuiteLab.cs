using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using EasyBuilding;
using EasyCombat;
using EasyCombat.Integrations;
using EasyFeedbacks;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyLocalization;
using EasyResources;
using EasyUI;
using TMPro;
using EasyUI.Integrations;
using EasyUI.Integrations.Building;
using EasyUI.Integrations.Interaction;
using EasyUI.Integrations.Feedbacks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ResourceDefinition = EasyResources.ResourceDefinition;

namespace EasySuite.Example
{
    public enum EasySuiteCarryWalkingLag { All, NoForward, NoWalking }
    public enum EasySuiteCarryFollowPreset { Authored, Soft, Firm }

    // Project-local composition. Each mechanism remains implemented by its owning package.
    public sealed class EasySuiteLab : MonoBehaviour
    {
        [HideInInspector] public int ContentRevision;
        public PlayerInputSettings Player;
        public CombatFirstPersonController Character;
        public Health PlayerHealth, TargetHealth;
        public InteractionActor Actor;
        public InteractionPresenter Presenter;
        public BuildingPlacer Placer;
        public EasyBuildingInput BuildingInput;
        public BuildingDefinition[] Buildings;
        public BuildingCatalog BuildingCatalog;
        public EasyBuilding.Integrations.BuildingResources BuildingResources;
        public ResourceContainer Wallet, Stock;
        public ResourceDatabase ResourceDatabase;
        public ResourceDefinition Wood, Mana, Coin, Crystal;
        public ResourceRewardProfile EnemyRewardProfile;
        public WindowManager Windows;
        public WindowDefinition MenuWindow, ControlsWindow, BuildWindow, InteractionWindow;
        public IntegrationWindowSet IntegrationWindows;
        public LocalizedCatalogCache Texts;
        public DamageEmitter Weapon;
        public CombatAbilities Abilities;
        public AbilityDefinition PaidAbility;
        public MovementField SlowField;
        public CombatDefinitionCatalog CombatCatalog;
        public Transform Crate;
        public TMP_Text Hud, Prompt, Notice, Performance;
        public Image PromptIcon;
        public MonoBehaviour FeedbackContextProvider;
        public FeedbackPreset ShotFeedback, HitFeedback, UiFeedback;
        public long InitialWood = 24, InitialMana = 100, StockWood = 20;
        public string LastMessage { get; private set; } = "";
        public bool IsRestoring { get; internal set; }
        public int FiredCount { get; private set; }
        public int PlacedCount { get; private set; }
        public int SuccessfulLoads { get; internal set; }
        public bool Ready { get; private set; }
        public EasySuiteSave Saves { get; private set; }
        public InteractionTarget SaveWorkbench;
        public EasySettings.SettingsController GraphicsSettings;
        public event Action Changed;
        public event Action CarryWalkingLagChanged;
        public event Action CarryFollowSpeedChanged;
        public const string CarryFollowAuthoredKey = "menu.carryFollowSpeed.authored";
        public const string CarryFollowSoftKey = "menu.carryFollowSpeed.soft";
        public const string CarryFollowFirmKey = "menu.carryFollowSpeed.firm";
        public const string CarryFollowNoticeKey = "lab.carryFollowSpeed.changed";
        public EasySuiteCarryFollowPreset CarryFollowPreset { get; private set; }
        private float carryFollowSpeedBaseline = .5f;
        public EasySuiteCarryWalkingLag CarryWalkingLagMode { get; private set; } = EasySuiteCarryWalkingLag.All;
        public const string CarryWalkingLagAllKey = "menu.carryWalkingLag.all";
        public const string CarryWalkingLagNoForwardKey = "menu.carryWalkingLag.noForward";
        public const string CarryWalkingLagNoneKey = "menu.carryWalkingLag.none";
        public const string CarryWalkingLagNoticeKey = "lab.carryWalkingLag.changed";
        private GrabAction carryWalkingLagGrab;
        private Vector3 carryWalkingLagBaseline;
        private bool carryWalkingLagCaptured;
        private CancellationTokenSource lifetime;
        private bool worldInitialized, subscribed, closeAfterSelection;
        private InputAction menu, build, save, load, interactionMenu, journalAction;
        private bool hudDirty = true;
        private float nextTelemetry;
        private float frameTime;
        private int frames;
        private readonly List<PlacedBuilding> nearBuildings = new List<PlacedBuilding>();
        private readonly Dictionary<WindowDefinition, UIWindow> preparedWindows = new Dictionary<WindowDefinition, UIWindow>();
        private BuildingSession observedSession;
        private string noticeKey, noticeFallback;

        private void OnEnable()
        {
            lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            if (worldInitialized) { Saves = new EasySuiteSave(this); Connect(); InitializePresentation(lifetime.Token); }
        }
        private void Start()
        {
            try
            {
                if (Windows.Font)
                {
                    foreach (var label in Windows.GetComponentsInChildren<TextMeshProUGUI>(true))
                        if (label.font == UIText.DefaultFont) label.font = Windows.Font;
                }
                Wallet.Add(Wood, checked(InitialWood * Wood.UnitsPerWhole)); Wallet.Add(Mana, checked(InitialMana * Mana.UnitsPerWhole));
                Stock.Add(Wood, checked(StockWood * Wood.UnitsPerWhole)); Stock.Add(Mana, 40 * Mana.UnitsPerWhole);
                if (Abilities && PaidAbility) Abilities.Configure(new[] { PaidAbility });
                menu = Player.Action("Suite/Menu"); build = Player.Action("Suite/Build");
                save = Player.Action("Suite/Save"); load = Player.Action("Suite/Load"); interactionMenu = Player.Action("Suite/InteractionMenu");
                journalAction = Player.Actions.FindAction("Narrative/Journal", false);
                CaptureCarryWalkingLagBaseline();
                Saves = new EasySuiteSave(this);
                worldInitialized = true; Connect(); InitializePresentation(lifetime.Token);
            }
            catch (Exception exception) { Message(exception.Message); Debug.LogException(exception, this); }
        }
        private void Connect()
        {
            if (subscribed) return; subscribed = true;
                menu.performed += MenuPressed; build.performed += BuildPressed;
                save.performed += SavePressed; load.performed += LoadPressed; interactionMenu.performed += InteractionMenuPressed;
                Wallet.Changed += Dirty; Stock.Changed += Dirty; Player.PresentationChanged += Dirty;
                PlayerHealth.Changed += HealthChanged; TargetHealth.Changed += HealthChanged;
                TargetHealth.RewardCompleted += RewardCompleted;
                TargetHealth.StatusesChanged += Dirty;
                Presenter.Changed += PromptChanged; Texts.Changed += TextsChanged;
                Placer.System.Placed += BuildingPlaced; Placer.System.Removed += BuildingRemoved;
                Placer.SessionChanged += SessionChanged; SessionChanged(Placer.Session);
                Weapon.Fired += Shot; Weapon.Impacted += Impact;
                Windows.WindowOpened += WindowOpened; Windows.WindowClosed += WindowClosed;
        }
        private async void InitializePresentation(CancellationToken cancellation)
        {
            try
            {
                await EasyLocalization.Localization.InitializeAsync();
                cancellation.ThrowIfCancellationRequested();
                if (!this || !isActiveAndEnabled) return;
                Ready = true; Dirty();
                noticeKey = "lab.ready"; noticeFallback = "Explore the lab. Tab opens the menu; E interacts; T opens building.";
                RefreshLocalizedNotice();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Message(exception.Message); Debug.LogException(exception, this); }
        }
        public string T(string key, string fallback) => Texts ? Texts.Get(key, fallback) : fallback;
        private void Dirty() { hudDirty = true; Changed?.Invoke(); }
        private void TextsChanged() { RefreshLocalizedNotice(); Dirty(); }
        private void RefreshLocalizedNotice()
        {
            if (string.IsNullOrEmpty(noticeKey)) return;
            LastMessage = T(noticeKey, noticeFallback);
            if (Notice) Notice.text = LastMessage;
        }
        private void HealthChanged(HealthChange _) => Dirty();
        private void RewardCompleted(DeathResourceRewardResult result)
        {
            if (IsRestoring) return;
            Message(result.Granted
                ? T("lab.rewarded", "Target defeated. Coins and crystals were added to your wallet. Tab > Reset combat target starts another life.")
                : T("lab.rewardFailed.health", "The target reward was rejected; inspect Health > Enable Death Reward on Combat Target.") + " " + result.Failure + " " + result.Error);
            Dirty();
        }
        private void PromptChanged(InteractionPrompt _) => Dirty();
        private void BuildingPlaced(PlacedBuilding _) { if (!IsRestoring) PlacedCount++; Dirty(); }
        private void BuildingRemoved(PlacedBuilding _) => Dirty();
        private void SessionChanged(BuildingSession session)
        {
            if (observedSession != null) observedSession.SelectionChanged -= SelectionChanged;
            observedSession = session;
            if (observedSession != null) observedSession.SelectionChanged += SelectionChanged;
            if (observedSession?.Selected) SelectionChanged(observedSession.Selected);
        }
        private void SelectionChanged(BuildingDefinition selected)
        {
            if (selected && !IsRestoring)
            {
                BuildingInput.Enter(); closeAfterSelection = true;
                Message(T("lab.buildhelp", "Aim at the building pad. Click to place; R/Q rotate; Escape exits."));
            }
            Dirty();
        }
        private void Shot(ShotInfo shot) { FiredCount++; Play(ShotFeedback, shot.Origin); Dirty(); }
        private void Impact(DamageImpact impact) { Play(HitFeedback, impact.Point); Dirty(); }
        public void Play(FeedbackPreset preset, Vector3 position)
        {
            if (!preset || IsRestoring) return;
            var context = new FeedbackContext(position, Quaternion.identity);
            if (FeedbackContextProvider is IFeedbackContextProvider provider) context = provider.Prepare(context);
            FeedbackService.Play(preset, context);
        }
        private void MenuPressed(InputAction.CallbackContext _) => ToggleMenu();
        private void BuildPressed(InputAction.CallbackContext _) { if (Windows.Count == 0) OpenBuildings(); }
        private void InteractionMenuPressed(InputAction.CallbackContext _) { if (Windows.Count == 0) OpenInteractions(); }
        private void SavePressed(InputAction.CallbackContext _) => Save();
        private void LoadPressed(InputAction.CallbackContext _) => Load();
        public void ToggleMenu()
        {
            if (!Ready || !isActiveAndEnabled) return;
            if (Player.Rebind?.IsActive == true) { Player.CancelRebind(); return; }
            if (Windows.Count > 0) Windows.CloseAll();
            else OpenPrepared(MenuWindow, null, menu);
        }
        public void OpenControls(WindowHandle parent = null) => OpenPrepared(ControlsWindow, parent);
        public void OpenBuildings(WindowHandle parent = null)
        {
            if (!PlayerHealth.IsStanding) { Message(T("lab.dead", "Player defeated. Load or revive before building.")); return; }
            Actor.Cancel("Opening building catalog"); OpenPrepared(BuildWindow, parent, build);
        }
        public void OpenInteractions(WindowHandle parent = null) { Presenter.Refresh(); OpenPrepared(InteractionWindow, parent, interactionMenu); }
        private WindowHandle OpenPrepared(WindowDefinition definition, WindowHandle parent = null, InputAction opener = null)
        {
            if (!definition) throw new InvalidOperationException("Assign the lab window definitions in Easy Setup.");
            if (!preparedWindows.TryGetValue(definition, out var instance) || !instance)
            {
                instance = IntegrationWindowSet.InstantiateWindow(definition, Windows.transform);
                preparedWindows[definition] = instance;
                if (instance.TryGetComponent<InteractionMenuWindow>(out var interaction)) interaction.Bind(Presenter);
                if (instance.TryGetComponent<BuildingCatalogWindow>(out var building)) building.Bind(Placer, BuildingCatalog);
                if (instance.TryGetComponent<BuildingCatalogPresentation>(out var presentation)) presentation.Bind(BuildingResources, Texts);
                if (instance.TryGetComponent<IntegratedSettingsWindow>(out var settings)) { settings.Bind(Texts); settings.BindGraphics(GraphicsSettings); }
                if (instance.TryGetComponent<WindowFeedbacks>(out var feedback))
                { feedback.ContextProvider = FeedbackContextProvider; if (!feedback.Open) feedback.Open = UiFeedback; }
            }
            return Windows.Open(instance, parent, opener);
        }
        public void SelectBuilding(BuildingDefinition definition)
        {
            if (!definition || !Placer.Session.Select(definition)) return;
            Dirty();
            Message(T("lab.buildhelp", "Aim at the building pad. Click to place; R/Q rotate; Escape exits."));
        }
        public async void ChangeLanguage()
        {
            try { await EasyLocalization.Localization.SetLanguageAsync(EasyLocalization.Localization.LanguageCode == "ru" ? "en" : "ru", lifetime.Token); if (this) Dirty(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Message(exception.Message); Debug.LogException(exception, this); }
        }
        public async void Save()
        {
            if (!Ready || IsRestoring) return;
            var session = Saves;
            try { await session.SaveFileAsync(lifetime.Token); if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message(T("lab.saved", "Progress saved locally. Global preferences were not changed.")); }
            catch (Exception e) { if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message(e.Message); }
        }
        public async void SavePreferences()
        {
            if (!Ready) return;
            var session = Saves;
            try { await session.SavePreferencesAsync(lifetime.Token); if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message("Global input/audio/language/graphics preferences saved."); }
            catch (Exception e) { if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message(e.Message); }
        }
        public async void LoadPreferences()
        {
            if (!Ready) return;
            var session = Saves;
            try { await session.LoadPreferencesAsync(lifetime.Token); if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) { Message("Global preferences loaded; progress unchanged."); Dirty(); } }
            catch (Exception e) { if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message(e.Message); }
        }
        public async void Load()
        {
            if (!Ready || IsRestoring) return;
            var session = Saves;
            try { await session.LoadFileAsync(lifetime.Token); if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) { Message(T("lab.loaded", "State restored. Balances and rewards were not duplicated.")); Dirty(); } }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this && isActiveAndEnabled && ReferenceEquals(Saves, session)) Message(e.Message); }
        }
        public string CarryWalkingLagLabelKey => CarryWalkingLagMode == EasySuiteCarryWalkingLag.NoForward ? CarryWalkingLagNoForwardKey :
            CarryWalkingLagMode == EasySuiteCarryWalkingLag.NoWalking ? CarryWalkingLagNoneKey : CarryWalkingLagAllKey;
        public string CarryWalkingLagLabelFallback => CarryWalkingLagMode == EasySuiteCarryWalkingLag.NoForward ? "Carry walking lag: no forward / back" :
            CarryWalkingLagMode == EasySuiteCarryWalkingLag.NoWalking ? "Carry walking lag: off" : "Carry walking lag: all axes";

        private void CaptureCarryWalkingLagBaseline()
        {
            if (carryWalkingLagCaptured) return;
            carryWalkingLagGrab = Crate ? Crate.GetComponent<GrabAction>() : null;
            if (!carryWalkingLagGrab) throw new InvalidOperationException("Restore the Playground crate's Grab Action before comparing carry inertia.");
            carryWalkingLagBaseline = ReadMovementInertia(carryWalkingLagGrab);
            CarryWalkingLagMode = EasySuiteCarryWalkingLag.All;
            carryFollowSpeedBaseline = carryWalkingLagGrab.FollowSpeed;
            CarryFollowPreset = EasySuiteCarryFollowPreset.Authored;
            carryWalkingLagCaptured = true;
        }

        public void CycleCarryWalkingLag()
        {
            CaptureCarryWalkingLagBaseline();
            var next = (EasySuiteCarryWalkingLag)(((int)CarryWalkingLagMode + 1) % 3);
            var mask = next == EasySuiteCarryWalkingLag.NoForward ? new Vector3(1, 1, 0) :
                next == EasySuiteCarryWalkingLag.NoWalking ? Vector3.zero : Vector3.one;
            // All restores the exact authored vector captured at startup, including custom per-axis values.
            WriteMovementInertia(carryWalkingLagGrab, Vector3.Scale(carryWalkingLagBaseline, mask));
            CarryWalkingLagMode = next;
            CarryWalkingLagChanged?.Invoke();
            noticeKey = CarryWalkingLagNoticeKey;
            noticeFallback = "Close Tab: compare W/S and A/D. The walking-lag setting changes movement axes; Follow Speed changes physical response.";
            RefreshLocalizedNotice();
        }

        public float CarryFollowSpeedValue => carryWalkingLagGrab ? carryWalkingLagGrab.FollowSpeed : carryFollowSpeedBaseline;
        public string CarryFollowSpeedLabelKey => CarryFollowPreset == EasySuiteCarryFollowPreset.Soft ? CarryFollowSoftKey :
            CarryFollowPreset == EasySuiteCarryFollowPreset.Firm ? CarryFollowFirmKey : CarryFollowAuthoredKey;
        public string CarryFollowSpeedLabelFallback => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            CarryFollowPreset == EasySuiteCarryFollowPreset.Soft ? "Follow speed: soft · {0:0.##}" :
            CarryFollowPreset == EasySuiteCarryFollowPreset.Firm ? "Follow speed: firm · {0:0.##}" : "Follow speed: authored · {0:0.##}", CarryFollowSpeedValue);

        public void CycleCarryFollowSpeed()
        {
            CaptureCarryWalkingLagBaseline();
            CarryFollowPreset = (EasySuiteCarryFollowPreset)(((int)CarryFollowPreset + 1) % 3);
            carryWalkingLagGrab.FollowSpeed = CarryFollowPreset == EasySuiteCarryFollowPreset.Soft ? 0 :
                CarryFollowPreset == EasySuiteCarryFollowPreset.Firm ? 1 : carryFollowSpeedBaseline;
            CarryFollowSpeedChanged?.Invoke();
            noticeKey = CarryFollowNoticeKey;
            noticeFallback = "Close Tab: compare soft (0) and firm (1) follow. Walking lag is separate. E release · G throw.";
            RefreshLocalizedNotice();
        }

        private static Vector3 ReadMovementInertia(GrabAction grab) => grab.MovementLag;
        private static void WriteMovementInertia(GrabAction grab, Vector3 value) => grab.MovementLag = value;

        public void AddResources() { Wallet.Add(Wood, 20 * Wood.UnitsPerWhole); Wallet.Add(Mana, 20 * Mana.UnitsPerWhole); Message(T("lab.added", "Added 20 wood and 20 energy.")); }
        public void DamagePlayer() { PlayerHealth.ApplyDamage(new DamageInfo(25)); Dirty(); }
        public void KillPlayer() { PlayerHealth.ApplyDamage(new DamageInfo(PlayerHealth.Maximum + 1)); Message(T("lab.dead", "Player defeated. F9 loads your save; Tab still opens the lab menu.")); }
        public void RevivePlayer() { PlayerHealth.ResetToFull(); Dirty(); }
        public void ResetTarget() { TargetHealth.ResetToFull(); TargetHealth.ClearStatuses(); Dirty(); }
        public void RepairNearest()
        {
            Placer.System.GetBuildings(nearBuildings);
            PlacedBuilding closest = null; float distance = float.PositiveInfinity;
            foreach (var candidate in nearBuildings)
            {
                float value = (candidate.transform.position - Character.transform.position).sqrMagnitude;
                if (value < distance) { closest = candidate; distance = value; }
            }
            if (!closest) { Message(T("lab.nobuilding", "Place a building first.")); return; }
            if (!Placer.System.TryRepair(closest, 10, out var result, Placer.Session.Actor)) Message(result.Reason);
            else Message(T("lab.repaired", "Repaired up to 10 HP using wood."));
            Dirty();
        }
        public void CastPaidAbility()
        {
            if (!Abilities || !PaidAbility) { Message("The paid ability is not configured."); return; }
            if (!Abilities.TryActivate(PaidAbility, TargetHealth.transform.position, TargetHealth, out var reason)) Message(reason.ToString());
            else Message(T("lab.paidcast", "Casting: energy is charged from the shared wallet at release."));
            Dirty();
        }
        private void WindowOpened(WindowHandle handle)
        {
            var view = handle.Window.GetComponent<EasySuiteWindow>(); if (view) view.Bind(this, handle);
            var feedback = handle.Window.GetComponent<WindowFeedbacks>();
            if (!feedback || !feedback.Open) Play(UiFeedback, Character.transform.position); Dirty();
        }
        private void WindowClosed(WindowHandle _) => Dirty();
        public void Message(string text) { noticeKey = noticeFallback = null; LastMessage = text ?? ""; if (Notice) Notice.text = LastMessage; }
        private void Update()
        {
            frameTime += Time.unscaledDeltaTime; frames++;
            if (Time.unscaledTime >= nextTelemetry)
            {
                nextTelemetry = Time.unscaledTime + .5f;
                hudDirty = true;
                if (Performance && frameTime > 0) Performance.text = (frames / frameTime).ToString("F0") + " FPS  ·  " + (frameTime * 1000 / frames).ToString("F1") + " ms";
                frameTime = 0; frames = 0;
            }
        }
        private void LateUpdate()
        {
            if (closeAfterSelection) { closeAfterSelection = false; Windows.CloseAll(); }
            if (!Ready || !hudDirty) return; hudDirty = false;
            string statusText = "";
            if (TargetHealth.StatusCount > 0)
            {
                var active = TargetHealth.Statuses.GetStatus(0);
                statusText = "  ·  " + (string.IsNullOrWhiteSpace(active.Definition.DisplayName) ? active.Definition.name : active.Definition.DisplayName) + " ×" + active.Stacks;
            }
            Hud.text = T("lab.hp", "HEALTH") + " " + PlayerHealth.Current.ToString("F0") + " / " + PlayerHealth.Maximum.ToString("F0") +
                "     " + T("lab.wood", "WOOD") + " " + Wood.Format(Wallet.GetAvailable(Wood)) + "     " + T("lab.energy", "ENERGY") + " " + Mana.Format(Wallet.GetAvailable(Mana)) +
                "\n" + T("lab.target", "TARGET") + " " + TargetHealth.Current.ToString("F0") + statusText + "     " + T("lab.built", "BUILT") + " " + Placer.System.Count + "     " + Character.GetMaxSpeed().ToString("F1") + " m/s" +
                (Coin && Crystal ? "\n" + T("lab.coins", "COINS") + " " + Coin.Format(Wallet.GetAvailable(Coin)) + "     " + T("lab.crystals", "CRYSTALS") + " " + Crystal.Format(Wallet.GetAvailable(Crystal)) : "");
            var prompt = Presenter.Prompt;
            Prompt.text = prompt.Action ? (prompt.Available ? "[" + prompt.Binding + "] " : "") + prompt.Label + (prompt.Running ? "  " + (prompt.Progress * 100).ToString("F0") + "%" : "") +
                (prompt.Available ? "" : "\n" + prompt.Reason) : T("lab.hint", "E · interact   H · actions   T · build   Tab · menu   F5 / F9 · save / load");
            if (!prompt.Action && journalAction != null && Player.DeviceFamily == InputDeviceFamily.KeyboardMouse)
                Prompt.text += "   " + Player.GetPresentation(journalAction).Text + " · " + T("quest.journal.shortHint", "journal");
            if (PromptIcon) { PromptIcon.sprite = prompt.BindingIcon; PromptIcon.enabled = prompt.Action && prompt.BindingIcon; }
        }
        private void OnDisable()
        {
            Ready = false; closeAfterSelection = false; lifetime?.Cancel();
            Saves?.Dispose(); Saves = null;
            Disconnect();
            foreach (var window in preparedWindows.Values) if (window) window.Handle?.Close();
            if (worldInitialized && BuildingInput) BuildingInput.Exit();
            if (worldInitialized && Actor) Actor.Cancel("Lab disabled");
        }
        private void Disconnect()
        {
            if (!subscribed) return; subscribed = false;
            if (menu != null) menu.performed -= MenuPressed; if (build != null) build.performed -= BuildPressed;
            if (save != null) save.performed -= SavePressed; if (load != null) load.performed -= LoadPressed;
            if (interactionMenu != null) interactionMenu.performed -= InteractionMenuPressed;
            if (Wallet) Wallet.Changed -= Dirty; if (Stock) Stock.Changed -= Dirty;
            if (Player) Player.PresentationChanged -= Dirty;
            if (PlayerHealth) PlayerHealth.Changed -= HealthChanged; if (TargetHealth) TargetHealth.Changed -= HealthChanged;
            if (TargetHealth) TargetHealth.RewardCompleted -= RewardCompleted;
            if (TargetHealth) TargetHealth.StatusesChanged -= Dirty;
            if (Presenter) Presenter.Changed -= PromptChanged; if (Texts) Texts.Changed -= TextsChanged;
            if (Placer && Placer.System) { Placer.System.Placed -= BuildingPlaced; Placer.System.Removed -= BuildingRemoved; }
            if (Placer) Placer.SessionChanged -= SessionChanged;
            if (observedSession != null) observedSession.SelectionChanged -= SelectionChanged;
            observedSession = null;
            if (Weapon) { Weapon.Fired -= Shot; Weapon.Impacted -= Impact; }
            if (Windows) { Windows.WindowOpened -= WindowOpened; Windows.WindowClosed -= WindowClosed; }
        }
        private void OnDestroy()
        {
            OnDisable(); lifetime?.Dispose();
            foreach (var window in preparedWindows.Values) if (window) Destroy(window.gameObject);
            preparedWindows.Clear();
        }
    }
}
