using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyBuilding;
using EasyBuilding.Integrations;
using EasyCombat;
using EasyCombat.Integrations;
using EasyFeedbacks;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyInteraction.Integrations.Building;
using EasyInteraction.Integrations.Combat;
using EasyInteraction.Integrations.Feedbacks;
using EasyInteraction.Integrations.Movement;
using EasyInteraction.Integrations.Resources;
using EasyLocalization;
using EasyLocalization.Editor;
using EasyLocalization.Integrations;
using EasyMovement.FirstPerson;
using EasyResources;
using EasyUI;
using TMPro;
using EasyUI.Integrations;
using EasyUI.Integrations.Editor;
using EasyUI.Integrations.Feedbacks;
using EasyUI.Integrations.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using ResourceDefinition = EasyResources.ResourceDefinition;

namespace EasySuite.Example.Editor
{
    /// <summary>Explicit, repeatable first-run composition of the framework's modules.</summary>
    public static class EasySuiteSceneBuilder
    {
        public const string ScenePath = "Assets/EasyGameFramework/Playground/FrameworkPlayground.unity";
        public const string Content = "Assets/EasyGameFramework/Playground/Content";
        private const string MovementPlayer = "Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab";
        private const string MovementCamera = "Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_MainCamera_Sample.prefab";
        private const string CollectionName = "Framework Playground";
        public const int ContentRevision = 23;
        private const string CarryControlsSignKey = "sign.carry.controls9";
        private const string CarryControlsSignEnglish = "01 / CARRY\nWalking lag: authored values\nTab · walking lag / Follow Speed";
        private const string CarryControlsSignRussian = "01 / ПЕРЕНОС\nИнерция ходьбы: из настроек\nTab · ходьба / Follow Speed";
        private static readonly (string oldKey, string key)[] CarryKeyRenames =
        {
            ("menu.carryReturn.authored", EasySuiteLab.CarryFollowAuthoredKey),
            ("menu.carryReturn.faster", EasySuiteLab.CarryFollowFirmKey),
            ("lab.carryReturn.authored", EasySuiteLab.CarryFollowSoftKey),
            ("lab.carryReturn.faster", EasySuiteLab.CarryFollowNoticeKey),
            ("sign.carry.return7", CarryControlsSignKey)
        };
        private const string CarryInertiaSignKey = "sign.carry.inertia5";
        private const string CarryInertiaSignEnglish = "01 / CARRY CONTROLS\nTab · walking lag / Follow Speed\nE · take / release   G · throw";
        private const string CarryInertiaSignRussian = "01 / ПЕРЕНОС ПРЕДМЕТА\nTab · ходьба / Follow Speed\nE · взять / отпустить   G · бросить";
        private static readonly EasySuiteCommand[] OriginalMenuCommands =
        {
            EasySuiteCommand.Controls, EasySuiteCommand.Language, EasySuiteCommand.Buildings, EasySuiteCommand.InteractionActions,
            EasySuiteCommand.Save, EasySuiteCommand.Load, EasySuiteCommand.SavePreferences, EasySuiteCommand.LoadPreferences,
            EasySuiteCommand.AddResources, EasySuiteCommand.PaidAbility, EasySuiteCommand.DamagePlayer, EasySuiteCommand.Repair,
            EasySuiteCommand.KillPlayer, EasySuiteCommand.RevivePlayer, EasySuiteCommand.ResetTarget, EasySuiteCommand.Close
        };
        private const string CarryPoseKey = "sign.carry.pose";
        private const string CarryPoseEnglish = "01  /  CARRY POSE\nE · take / release   G · throw\nAuthored hold pose · physical contacts";
        private const string CarryPoseRussian = "01  /  ПОЗА В РУКАХ\nE · взять / отпустить   G · бросить\nЗаданная поза · физические контакты";
        private const string CarryUxKey = "sign.carry.ux";
        private const string CarryUxEnglish = "01  /  FOCUS + CARRY\nE · take / release   G · throw\nHeld scale 65% · adjustable Follow Speed";
        private const string CarryUxRussian = "01  /  ФОКУС И ПЕРЕНОСКА\nE · взять / отпустить   G · бросить\nРазмер в руках 65% · Follow Speed";
        private const string RewardSignKey = "sign.combat.rewards";
        private const string RewardSignEnglish = "03  /  COMBAT + REWARDS\nDefeat target: 3–7 coins + 1–3 crystals\nTab → Reset combat target · repeat";
        private const string RewardSignRussian = "03  /  БОЙ И НАГРАДЫ\nПобедите: 3–7 монет + 1–3 кристалла\nTab → Сбросить мишень · повторить";
        private static StringTableCollection strings;
        private static Font font;
        private static Material floor, dark, steel, gold, blue, coral, white;

        [MenuItem("Tools/EasyGameFramework/Examples/Create or Update Framework Playground")]
        public static void CreateAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var path = Create(false);
            // Play the example alone: an open default scene may already contain a camera/listener.
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        public static string Create(bool open = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before creating or updating the Framework Playground.");
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Choose Window > TextMeshPro > Import TMP Essential Resources, then create the Playground again.");
            if (File.Exists(ScenePath))
            {
                UpdateExisting();
                if (open) OpenExisting();
                return ScenePath;
            }
            RequireAsset<GameObject>(MovementPlayer);
            RequireAsset<GameObject>(MovementCamera);
            EnsureFolders();
            var combatAssets = EasyGameFramework.Editor.FrameworkCombatSampleFactory.Create(Content + "/Combat");
            var fireball = combatAssets.Fireball;
            var feedback = FrameworkFeedbackSampleFactory.Create(Content + "/Feedbacks");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateMaterials();

            // All scene objects are authored in a separate additive scene. The user's current scene is never saved or replaced.
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            bool saved = false;
            try
            {
                var labObject = new GameObject("EasyGameFramework Playground");
                labObject.SetActive(false);
                var lab = labObject.AddComponent<EasySuiteLab>();
                labObject.AddComponent<EasySuiteSmoke>().Lab = lab;
                FrameworkResourceDatabaseMigration.Prepare(lab);
                lab.InitialWood = 24; lab.InitialMana = 100; lab.StockWood = 20;
                lab.UiFeedback = lab.ShotFeedback = lab.HitFeedback = feedback;

                var playerHealth = Definition<HealthDefinition>("Player Health", value => value.MaximumHealth = 100);
                var targetHealth = Definition<HealthDefinition>("Target Health", value => value.MaximumHealth = 200);
                var buildingHealth = Definition<HealthDefinition>("Building Health", value => value.MaximumHealth = 60);
                var speed = combatAssets.MovementSpeed;
                lab.Buildings = CreateBuildings(lab.Wood, buildingHealth);
                lab.BuildingCatalog = Definition<BuildingCatalog>("Building Catalog", value => value.Buildings = lab.Buildings);
                var catalog = CreateTranslations(lab.Wood, lab.Mana, lab.Buildings);
                lab.Texts = labObject.AddComponent<LocalizedCatalogCache>(); lab.Texts.catalog = catalog;

                var cameraRoot = (GameObject)PrefabUtility.InstantiatePrefab(RequireAsset<GameObject>(MovementCamera), scene);
                cameraRoot.name = "Scene Camera";
                var outputCamera = cameraRoot.GetComponentInChildren<Camera>(true);
                if (!outputCamera) throw new InvalidOperationException("The EasyMovement camera prefab has no output Camera.");
                outputCamera.tag = "MainCamera"; outputCamera.backgroundColor = new Color(.035f, .055f, .09f);
                outputCamera.clearFlags = CameraClearFlags.SolidColor; outputCamera.farClipPlane = 150;

                var player = (GameObject)PrefabUtility.InstantiatePrefab(CreatePlayerPrefab(speed), scene);
                player.name = "Player"; player.SetActive(false); player.transform.position = new Vector3(0, 0, -8);
                lab.Character = player.GetComponentInChildren<CombatFirstPersonController>(true);
                if (!lab.Character || player.GetComponentsInChildren<FirstPersonCharacterController>(true).Length != 1)
                    throw new InvalidOperationException("The player must contain exactly one CombatFirstPersonController.");
                lab.GraphicsSettings = labObject.AddComponent<EasySettings.SettingsController>();
                lab.GraphicsSettings.Defaults = Definition<EasySettings.SettingsDefaults>("Graphics Defaults", _ => { });
                lab.GraphicsSettings.TargetCamera = outputCamera;
                lab.GraphicsSettings.FollowMainCamera = false;
                var settingsFov = labObject.AddComponent<EasySettings.Integrations.EasyMovementSettingsFov>();
                settingsFov.PlayerFov = lab.Character.GetComponent<FirstPersonFov>();
                lab.GraphicsSettings.FieldOfViewProvider = settingsFov;
                var body = lab.Character.gameObject;
                lab.Player = body.AddComponent<PlayerInputSettings>();
                lab.Player.Configure(CreateActions(), CreateInputProfile());
                lab.Player.ControlsCursor = true;
                lab.PlayerHealth = body.GetComponent<Health>() ?? body.AddComponent<Health>();
                Profile(lab.PlayerHealth, playerHealth);
                lab.PlayerHealth.StatusesEnabled = true;
                lab.PlayerHealth.FeedbacksEnabled = true;
                body.GetComponent<MovementInfluences>().MovementSpeedStat = speed;
                lab.Wallet = body.AddComponent<ResourceContainer>();
                var context = body.AddComponent<PlayerFeedbackContext>(); context.Player = lab.Player;
                lab.FeedbackContextProvider = context;
                foreach (var steps in player.GetComponentsInChildren<EasyFootsteps.CharacterStepController>(true))
                    steps.ContextProvider = context;

                var movement = body.AddComponent<EasyMovementInput>(); movement.Player = lab.Player; movement.Character = lab.Character;
                lab.Actor = body.AddComponent<InteractionActor>(); lab.Actor.PlayerRoot = body.transform;
                lab.Actor.View = lab.Character.LookPivot; lab.Actor.Range = 5; lab.Actor.SelectionMode = InteractionSelectionMode.Focus;
                lab.Actor.RequireVisibility = true;
                lab.Actor.HandAnchor = null; // Use centered View + HoldDistance; explicit anchors remain optional.
                lab.Presenter = body.AddComponent<InteractionPresenter>();
                var interaction = body.AddComponent<EasyInteractionInput>(); interaction.Player = lab.Player; interaction.Actor = lab.Actor;
                interaction.ToggleInsteadOfHold = true; lab.Presenter.InputSource = interaction;
                var translatedInteraction = labObject.AddComponent<InteractionTranslations>();
                translatedInteraction.Cache = lab.Texts;
                translatedInteraction.presenter = lab.Presenter;
                lab.Presenter.TextProvider = translatedInteraction;
                var approach = body.AddComponent<EasyMovementInteractionMotor>(); approach.character = lab.Character;
                lab.Actor.Motor = approach;
                var movementLock = body.AddComponent<EasyMovementInteractionLock>(); movementLock.character = lab.Character;
                movementLock.channel = "body"; movementLock.lockAtApproachPoint = true;
                body.AddComponent<CombatInteractionGuard>().health = lab.PlayerHealth;
                var interactionFeedback = body.AddComponent<InteractionFeedbacks>();
                interactionFeedback.ContextProvider = context; interactionFeedback.completed = feedback; interactionFeedback.failed = feedback;
                lab.PlayerHealth.ConfigureFeedbacks(feedback, feedback, feedback, context);

                var actor = body.AddComponent<BuildingActor>(); actor.SetIdentity("suite.player");
                body.AddComponent<BuildingInteractionContext>();
                var system = body.AddComponent<BuildingSystem>();
                lab.BuildingResources = body.AddComponent<BuildingResources>();
                lab.BuildingResources.Resources = new[] { lab.Wood, lab.Mana };
                lab.BuildingResources.Containers = new[] { lab.Wallet }; lab.BuildingResources.RefundContainer = lab.Wallet;
                system.Economy = lab.BuildingResources;
                lab.Placer = body.AddComponent<BuildingPlacer>(); lab.Placer.Actor = actor; lab.Placer.ViewCamera = outputCamera;
                lab.Placer.StartingBuilding = null; lab.Placer.AimFromScreenCenter = true; lab.Placer.ShowHelp = false;
                lab.Placer.Reach = 18; lab.Placer.RepeatPlacement = true; lab.Placer.ExternalTargeting = true;
                lab.BuildingInput = body.AddComponent<EasyBuildingInput>(); lab.BuildingInput.Player = lab.Player; lab.BuildingInput.Placer = lab.Placer;
                lab.BuildingInput.UseFirstPersonPreset(); lab.BuildingInput.AutoEnterOnEnable = false;
                var buildingFeedback = body.AddComponent<BuildingFeedbacks>();
                buildingFeedback.Placed = buildingFeedback.Removed = feedback; buildingFeedback.ContextProvider = context;

                var pools = new GameObject("Combat Pools");
                var projectiles = pools.AddComponent<ProjectilePool>(); projectiles.Prefab = fireball.ProjectilePrefab;
                var areas = pools.AddComponent<DamageAreaPool>();
                lab.Weapon = body.AddComponent<DamageEmitter>(); lab.Weapon.OwnerRoot = body.transform; lab.Weapon.Attack = fireball;
                lab.Weapon.Pool = projectiles; lab.Weapon.AreaPool = areas;
                var weaponInput = body.AddComponent<EasyCombatInput>(); weaponInput.Player = lab.Player; weaponInput.Emitter = lab.Weapon;
                weaponInput.Aim = lab.Character.LookPivot; weaponInput.Automatic = false;
                CreatePaidAbility(lab, body);

                CreateRoom(); CreateStations(lab, targetHealth, feedback);
                CreateFeedbackManagers(); CreateHudAndWindows(lab);
                CreateCombatCatalog(lab, playerHealth, targetHealth, buildingHealth, fireball, speed);
                ApplyContentUpdates(lab);
                player.SetActive(true); labObject.SetActive(true);
                EditorUtility.SetDirty(lab); AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                FrameworkResourceDatabaseMigration.FinalizeSavedScene(lab, true);
                saved = true;
            }
            finally
            {
                if (!open || !saved)
                {
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                }
            }
            return ScenePath;
        }

        private static void OpenExisting()
        {
            var existing = SceneManager.GetSceneByPath(ScenePath);
            if (!existing.IsValid() || !existing.isLoaded) existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(existing);
        }

        // Explicit, versioned sample updates. Never runs on import or overwrites a current revision.
        private static void UpdateExisting()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var labs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EasySuiteLab>(true)).ToArray();
                if (labs.Length != 1) throw new InvalidOperationException("Expected one Framework Playground lab. Restore the sample's lab before updating.");
                var lab = labs[0];
                if (lab.ContentRevision >= ContentRevision)
                {
                        FrameworkResourceDatabaseMigration.FinalizeSavedScene(lab);
                    return;
                }
                if (scene.isDirty) throw new InvalidOperationException("Save your Framework Playground edits before updating the example.");
                SceneManager.SetActiveScene(scene);
                ApplyContentUpdates(lab);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the updated Framework Playground.");
                FrameworkResourceDatabaseMigration.FinalizeSavedScene(lab, true);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void ApplyContentUpdates(EasySuiteLab lab)
        {
            if (lab.ContentRevision >= ContentRevision) return;
            if (lab.ContentRevision < 11) FrameworkPlaygroundTypography.Upgrade(lab);
            EnsureCarryControlsTranslations(lab);
            if (lab.ContentRevision < 1) ApplyCarryPoseRevision(lab);
            if (lab.ContentRevision < 2) ApplyInteractionUxRevision(lab);
            if (lab.ContentRevision < 3) ApplyResourceDatabaseRevision(lab);
            if (lab.ContentRevision < 4) ApplyHealthCompositionRevision(lab);
            if (lab.ContentRevision < 5) ApplyCarryInertiaRevision(lab);
            // Revision6's experimental carry path was withdrawn. Do not create it on a fresh sample.
            if (lab.ContentRevision < 6) { lab.ContentRevision = 6; EditorUtility.SetDirty(lab); }
            if (lab.ContentRevision < 7) ApplyCarryControlsRevision(lab);
            if (lab.ContentRevision < 8) ApplyWalkingDefaultRevision(lab);
            if (lab.ContentRevision < 9) ApplyCarrySimplifyRevision(lab);
            if (lab.ContentRevision < 10) ApplyHalfWalkingDefaultRevision(lab);
            if (lab.ContentRevision < 12) FrameworkQuestPlayground.Apply(lab);
            if (lab.ContentRevision < 13) FrameworkDialoguePlayground.Apply(lab);
            if (lab.ContentRevision < 14) FrameworkTypographyStudio.Apply(lab);
            if (lab.ContentRevision < 15) FrameworkTypographyStudio.AddRevealTranslations(lab);
            if (lab.ContentRevision < 16) FrameworkBridgeReactions.Apply(lab);
            if (lab.ContentRevision < 17) FrameworkBridgeReactions.UpgradeAuthoring(lab);
            if (lab.ContentRevision < 18) FrameworkDeliveryRule.Apply(lab);
            if (lab.ContentRevision < 19) FrameworkMarkerIcons.Apply(lab);
            if (lab.ContentRevision < 20) FrameworkPatrolMarkers.Apply(lab);
            if (lab.ContentRevision < 21) FrameworkQuestHud.Apply(lab);
            if (lab.ContentRevision < 22) FrameworkWorkshopQuest.Apply(lab);
            if (lab.ContentRevision < 23)
            {
                FrameworkNarrativeSuite.Apply(lab);
                FrameworkRoleOrders.Apply(lab);
                FrameworkConversationSuite.Apply(lab);
                FrameworkNarrativeText.Apply(lab);
            }
            lab.ContentRevision = ContentRevision;
            EditorUtility.SetDirty(lab);
        }

        private static void ApplyCarryPoseRevision(EasySuiteLab lab)
        {
            // Revision 1: EGF-001. Keep the crate identity, world transform and all unrelated content.
            var grab = lab.Crate ? lab.Crate.GetComponent<GrabAction>() : null;
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            if (!collection) throw new InvalidOperationException("Restore the Framework Playground localization collection before updating.");
            var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
            var russian = collection.GetTable(new LocaleIdentifier("ru")) as StringTable;
            var oldEntry = collection.SharedData.GetEntry("sign.carry");
            var poseEntry = collection.SharedData.GetEntry(CarryPoseKey);
            var sign = lab.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<LocalizedText>(true))
                .FirstOrDefault(text => text.text.TableReference.TableCollectionNameGuid == collection.SharedData.TableCollectionNameGuid
                    && (text.text.TableEntryReference.KeyId == oldEntry?.Id || text.text.TableEntryReference.KeyId == poseEntry?.Id));
            if (!grab || !english || !russian || !sign)
                throw new InvalidOperationException("The carry crate, its sign and English/Russian localization are required to update this Playground.");
            var markerMaterial = RequireAsset<Material>(Content + "/Materials/Blue.mat");
            if (english.GetEntry(CarryPoseKey) == null) english.AddEntry(CarryPoseKey, CarryPoseEnglish);
            if (russian.GetEntry(CarryPoseKey) == null) russian.AddEntry(CarryPoseKey, CarryPoseRussian);
            EditorUtility.SetDirty(english); EditorUtility.SetDirty(russian); EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(english); AssetDatabase.SaveAssetIfDirty(russian); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            grab.PoseMode = GrabPoseMode.AuthoredPose;
            grab.LocalPosition = new Vector3(.25f, -.25f, 0);
            grab.LocalEulerAngles = new Vector3(8, -20, 5);
            if (!lab.Crate.Find("Carry Pose Front Marker"))
                RemoveCollider(Primitive("Carry Pose Front Marker", PrimitiveType.Cube, lab.Crate,
                    new Vector3(.2f, .15f, -.505f), new Vector3(.25f, .45f, .025f), markerMaterial));
            if (!lab.Crate.Find("Carry Pose Top Marker"))
                RemoveCollider(Primitive("Carry Pose Top Marker", PrimitiveType.Cube, lab.Crate,
                    new Vector3(.2f, .505f, -.2f), new Vector3(.25f, .025f, .45f), markerMaterial));
            sign.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(CarryPoseKey).Id);
            sign.fallback = CarryPoseEnglish;
            if (sign.TryGetComponent<TextMesh>(out var mesh)) { mesh.text = CarryPoseEnglish; EditorUtility.SetDirty(mesh); }
            EditorUtility.SetDirty(grab); EditorUtility.SetDirty(sign);
            lab.ContentRevision = 1;
            EditorUtility.SetDirty(lab);
        }

        private static void ApplyInteractionUxRevision(EasySuiteLab lab)
        {
            var grab = lab.Crate ? lab.Crate.GetComponent<GrabAction>() : null;
            if (!grab || !lab.Actor || !lab.TargetHealth || !lab.Wallet || !lab.SaveWorkbench || !lab.Texts || !lab.Texts.catalog || !lab.Hud)
                throw new InvalidOperationException("Restore the Playground actor, crate, target, wallet, workbench, localization and HUD references before updating.");
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            if (!collection) throw new InvalidOperationException("Restore the Framework Playground localization collection before updating.");
            var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
            var russian = collection.GetTable(new LocaleIdentifier("ru")) as StringTable;
            if (!english || !russian) throw new InvalidOperationException("The Playground English and Russian tables are required.");
            var carrySign = FindSign(lab, collection, CarryPoseKey, CarryUxKey);
            var rewardSign = FindSign(lab, collection, "sign.combat", RewardSignKey);
            if (!carrySign || !rewardSign) throw new InvalidOperationException("Restore the carry and combat signs before updating the Playground.");

            EnsureFolders();
            FrameworkResourceDatabaseMigration.Prepare(lab);
            string rewardPath = Content + "/Definitions/Rewards/EnemyRewards_Sample.asset";
            lab.EnemyRewardProfile = AssetDatabase.LoadAssetAtPath<ResourceRewardProfile>(rewardPath);
            if (!lab.EnemyRewardProfile)
            {
                lab.EnemyRewardProfile = ResourceRewardProfile.Create("EnemyRewards_Sample", lab.ResourceDatabase,
                    new ResourceRewardRange(lab.Coin, 3, 7), new ResourceRewardRange(lab.Crystal, 1, 3));
                AssetDatabase.CreateAsset(lab.EnemyRewardProfile, rewardPath);
            }
            ConfigureHealthReward(lab);

            // Revision 1's authored position/rotation, object identities and world transforms stay intact.
            lab.Actor.SelectionMode = InteractionSelectionMode.Focus;
            var highlighter = lab.Actor.GetComponent<InteractionFocusHighlighter>() ?? lab.Actor.gameObject.AddComponent<InteractionFocusHighlighter>();
            highlighter.Actor = lab.Actor;
            highlighter.OutlineColor = new Color(.1f, .8f, 1f, 1f);
            highlighter.Width = .015f;
            highlighter.CameraOverride = lab.Placer.ViewCamera;
            ConfigureOutline(grab.Target);
            ConfigureOutline(lab.SaveWorkbench);
            grab.HeldScaleMultiplier = .65f;
            EditorUtility.SetDirty(lab.Actor); EditorUtility.SetDirty(highlighter); EditorUtility.SetDirty(grab);

            var rows = new (string key, string en, string ru)[]
            {
                (CarryUxKey, CarryUxEnglish, CarryUxRussian),
                (RewardSignKey, RewardSignEnglish, RewardSignRussian),
                ("lab.coins", "COINS", "МОНЕТЫ"),
                ("lab.crystals", "CRYSTALS", "КРИСТАЛЛЫ"),
                ("lab.rewarded", "Target defeated. Coins and crystals added. Tab > Reset combat target starts another life.", "Мишень побеждена. Монеты и кристаллы получены. Tab > Сбросить мишень — новая жизнь."),
                ("lab.rewardFailed", "The target reward was rejected; inspect Death Resource Reward on Combat Target.", "Награда отклонена; проверьте Death Resource Reward на Combat Target."),
                ("resource.framework.coin.sample", "Coins", "Монеты"),
                ("resource.framework.crystal.sample", "Crystals", "Кристаллы")
            };
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries ?? Array.Empty<CatalogTranslation>());
            foreach (var row in rows)
            {
                if (english.GetEntry(row.key) == null) english.AddEntry(row.key, row.en);
                if (russian.GetEntry(row.key) == null) russian.AddEntry(row.key, row.ru);
                if (!entries.Any(entry => entry != null && entry.id == row.key))
                    entries.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id) });
            }
            lab.Texts.catalog.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(lab.Texts.catalog);
            EditorUtility.SetDirty(english); EditorUtility.SetDirty(russian); EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(lab.Texts.catalog); AssetDatabase.SaveAssetIfDirty(english);
            AssetDatabase.SaveAssetIfDirty(russian); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            BindSign(carrySign, collection, CarryUxKey, CarryUxEnglish);
            BindSign(rewardSign, collection, RewardSignKey, RewardSignEnglish);

            var header = lab.Hud.rectTransform.parent as RectTransform;
            if (header)
            {
                header.sizeDelta = new Vector2(header.sizeDelta.x, 128);
                header.anchoredPosition = new Vector2(header.anchoredPosition.x, -64);
                var title = header.Find("Title") as RectTransform;
                if (title) { title.anchoredPosition = new Vector2(title.anchoredPosition.x, 43); EditorUtility.SetDirty(title); }
                if (lab.Performance) { var performance = lab.Performance.rectTransform; performance.anchoredPosition = new Vector2(performance.anchoredPosition.x, 43); EditorUtility.SetDirty(performance); }
                EditorUtility.SetDirty(header);
            }
            lab.Hud.rectTransform.sizeDelta = new Vector2(890, 72);
            lab.Hud.rectTransform.anchoredPosition = new Vector2(24, -15);
            EditorUtility.SetDirty(lab.Hud.rectTransform);
            lab.ContentRevision = 2;
            EditorUtility.SetDirty(lab);
        }

        private static void ApplyResourceDatabaseRevision(EasySuiteLab lab)
        {
            FrameworkResourceDatabaseMigration.Prepare(lab);
            ConfigureHealthReward(lab);
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            if (!collection || !lab.Texts || !lab.Texts.catalog) throw new InvalidOperationException("Restore the sample localization before updating.");
            var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
            var russian = collection.GetTable(new LocaleIdentifier("ru")) as StringTable;
            if (!english || !russian) throw new InvalidOperationException("The sample English and Russian tables are required.");
            const string key = "lab.rewardFailed.health";
            if (english.GetEntry(key) == null) english.AddEntry(key, "The target reward was rejected; inspect Health > Enable Death Reward on Combat Target.");
            if (russian.GetEntry(key) == null) russian.AddEntry(key, "Награда отклонена; проверьте Health > Enable Death Reward на Combat Target.");
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries ?? Array.Empty<CatalogTranslation>());
            if (!entries.Any(entry => entry != null && entry.id == key))
            {
                entries.Add(new CatalogTranslation { id = key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(key).Id) });
                lab.Texts.catalog.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(lab.Texts.catalog); AssetDatabase.SaveAssetIfDirty(lab.Texts.catalog);
            }
            EditorUtility.SetDirty(english); EditorUtility.SetDirty(russian); EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(english); AssetDatabase.SaveAssetIfDirty(russian); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            lab.ContentRevision = 3; EditorUtility.SetDirty(lab);
        }

        private static void ApplyHealthCompositionRevision(EasySuiteLab lab)
        {
            if (!lab.PlayerHealth || !lab.TargetHealth)
                throw new InvalidOperationException("Restore the Playground player and target Health references before updating.");

            // Old component settings were transferred to native Health fields before their script types were removed.
            // Keep those feature switches, status budgets, exact player presets and context references.
            lab.PlayerHealth.ConfigureFeedbacks(lab.PlayerHealth.DamageFeedback, lab.PlayerHealth.DeathFeedback,
                lab.PlayerHealth.HealingFeedback, lab.PlayerHealth.FeedbackContextProvider);

            // The training target deliberately demonstrates its Damage feedback as well as Death/Healing.
            var targetDamage = lab.TargetHealth.DamageFeedback ? lab.TargetHealth.DamageFeedback :
                lab.TargetHealth.DeathFeedback ? lab.TargetHealth.DeathFeedback : lab.HitFeedback;
            lab.TargetHealth.ConfigureFeedbacks(targetDamage, lab.TargetHealth.DeathFeedback,
                lab.TargetHealth.HealingFeedback, lab.TargetHealth.FeedbackContextProvider);
            EditorUtility.SetDirty(lab.PlayerHealth); EditorUtility.SetDirty(lab.TargetHealth);

            if (!lab.Actor || !lab.Placer || !lab.Placer.ViewCamera || !lab.Crate || !lab.SaveWorkbench)
                throw new InvalidOperationException("Restore the Playground Actor, output camera and interactive targets before updating.");
            var highlighter = lab.Actor.GetComponent<InteractionFocusHighlighter>() ?? lab.Actor.gameObject.AddComponent<InteractionFocusHighlighter>();
            highlighter.Actor = lab.Actor;
            highlighter.CameraOverride = lab.Placer.ViewCamera;
            highlighter.OutlineColor = new Color(.1f, .8f, 1f, 1f);
            highlighter.Width = .015f;
            // The explicit pre-import migration copied each target's old enabled state and renderer list.
            // Updating the demo must not re-enable an intentionally disabled target outline.
            var crateTarget = lab.Crate.GetComponent<InteractionTarget>();
            crateTarget.RefreshOutlineRenderers();
            lab.SaveWorkbench.RefreshOutlineRenderers();
            EditorUtility.SetDirty(crateTarget); EditorUtility.SetDirty(lab.SaveWorkbench);
            EditorUtility.SetDirty(highlighter);
            UpdateCompositionSign(lab, "sign.carry.health4",
                "01  /  FOCUS + CARRY\nE · take / release   G · throw\nNo outline while held · size 65%",
                "01  /  ФОКУС И ПЕРЕНОСКА\nE · взять / отпустить   G · бросить\nВ руках без обводки · размер 65%", CarryUxKey, CarryPoseKey);
            UpdateCompositionSign(lab, "sign.combat.health4",
                "03  /  HEALTH + REWARDS\nClick · hit feedback + burn\n3–7 coins + 1–3 crystals · Tab resets",
                "03  /  ЗДОРОВЬЕ И НАГРАДЫ\nЩелчок · отклик попадания + ожог\n3–7 монет + 1–3 кристалла · Tab сброс", RewardSignKey, "sign.combat");
            lab.ContentRevision = 4; EditorUtility.SetDirty(lab);
        }

        private static void ApplyCarryInertiaRevision(EasySuiteLab lab)
        {
            if (!lab.Crate || !lab.Crate.GetComponent<GrabAction>() || !lab.MenuWindow || !lab.MenuWindow.Prefab)
                throw new InvalidOperationException("Restore the Playground crate and lab menu before updating the carry-inertia demonstration.");
            UpdateLegacyWalkingDefault(lab.Crate.GetComponent<GrabAction>());
            EnsureCarryInertiaTranslations(lab);
            UpdateCarryInertiaMenu(lab.MenuWindow);
            UpdateCompositionSign(lab, CarryInertiaSignKey, CarryInertiaSignEnglish, CarryInertiaSignRussian,
                "sign.carry.health4", CarryUxKey, CarryPoseKey, "sign.carry");
            // Update only the owned sample's legacy all-one default. Custom authored vectors remain intact.
            // The A/B switch changes a transient copy of that captured vector during play.
            lab.ContentRevision = 5;
            EditorUtility.SetDirty(lab);
        }

        private static void UpdateLegacyWalkingDefault(GrabAction grab)
        {
            if (!grab || !grab.MovementLag.Equals(Vector3.one)) return;
            grab.MovementLag = Vector3.one * .5f;
            EditorUtility.SetDirty(grab);
        }

        private static void ApplyHalfWalkingDefaultRevision(EasySuiteLab lab)
        {
            var grab = lab.Crate ? lab.Crate.GetComponent<GrabAction>() : null;
            if (!grab || !lab.MenuWindow || !lab.MenuWindow.Prefab)
                throw new InvalidOperationException("Restore the Playground crate and menu before updating its walking-lag default.");
            UpdateLegacyWalkingDefault(grab);
            EnsureCarryControlsTranslations(lab);
            UpdateCarryInertiaMenu(lab.MenuWindow, updateWalkingDefault: true);
            UpdateCompositionSign(lab, CarryControlsSignKey, CarryControlsSignEnglish, CarryControlsSignRussian,
                CarryInertiaSignKey, "sign.carry.health4", CarryUxKey, CarryPoseKey, "sign.carry");
            lab.ContentRevision = 10; EditorUtility.SetDirty(lab);
        }

        private static void ApplyWalkingDefaultRevision(EasySuiteLab lab)
        {
            EnsureCarryControlsTranslations(lab);
            UpdateCarryInertiaMenu(lab.MenuWindow, updateWalkingDefault: true);
            UpdateCompositionSign(lab, CarryControlsSignKey, CarryControlsSignEnglish, CarryControlsSignRussian,
                CarryInertiaSignKey, "sign.carry.health4", CarryUxKey, CarryPoseKey, "sign.carry");
            lab.ContentRevision = 8; EditorUtility.SetDirty(lab);
        }

        private static void ApplyCarryControlsRevision(EasySuiteLab lab)
        {
            EnsureCarryControlsTranslations(lab);
            UpdateCarryInertiaMenu(lab.MenuWindow, includeFollow: true);
            UpdateCompositionSign(lab, CarryControlsSignKey, CarryControlsSignEnglish, CarryControlsSignRussian,
                CarryInertiaSignKey, "sign.carry.health4", CarryUxKey, CarryPoseKey, "sign.carry");
            lab.ContentRevision = 7; EditorUtility.SetDirty(lab);
        }

        private static void ApplyCarrySimplifyRevision(EasySuiteLab lab)
        {
            if (!lab.Crate || !lab.Crate.GetComponent<GrabAction>() || !lab.MenuWindow || !lab.MenuWindow.Prefab)
                throw new InvalidOperationException("Restore the Playground crate and menu before updating carry controls.");
            var grab = lab.Crate.GetComponent<GrabAction>();
            var authoredWalking = grab.MovementLag;
            float authoredFollow = grab.FollowSpeed;
            EnsureCarryControlsTranslations(lab);
            // Existing FollowSpeed values belong to the author; this migration changes presentation only.
            UpdateCarryInertiaMenu(lab.MenuWindow, updateFollowDefault: true);
            UpdateCompositionSign(lab, CarryControlsSignKey, CarryControlsSignEnglish, CarryControlsSignRussian,
                CarryInertiaSignKey, "sign.carry.health4", CarryUxKey, CarryPoseKey, "sign.carry");
            if (!grab.MovementLag.Equals(authoredWalking) || !grab.FollowSpeed.Equals(authoredFollow))
                throw new InvalidOperationException("The carry-controls migration must retain the authored walking vector and Follow Speed.");
            // Serialize the same component with its current API, retiring obsolete owned fields without changing its values.
            EditorUtility.SetDirty(grab);
            lab.ContentRevision = 9; EditorUtility.SetDirty(lab);
        }

        private static (string key, string en, string ru)[] CarryControlsTranslations() => new[]
        {
            (EasySuiteLab.CarryFollowAuthoredKey, "Follow speed: authored · {0:0.##}", "Скорость следования: исходная · {0:0.##}"),
            (EasySuiteLab.CarryFollowSoftKey, "Follow speed: soft · {0:0.##}", "Скорость следования: мягкая · {0:0.##}"),
            (EasySuiteLab.CarryFollowFirmKey, "Follow speed: firm · {0:0.##}", "Скорость следования: плотная · {0:0.##}"),
            (EasySuiteLab.CarryFollowNoticeKey, "Close Tab: compare soft (0) and firm (1) follow. Walking lag is separate. E release · G throw.", "Закройте Tab: сравните мягкое (0) и плотное (1) следование. Инерция ходьбы отдельно. E отпустить · G бросить."),
            (CarryControlsSignKey, CarryControlsSignEnglish, CarryControlsSignRussian)
        };

        private static void RenameCarryTableKeys(StringTableCollection collection)
        {
            foreach (var rename in CarryKeyRenames)
            {
                var oldEntry = collection.SharedData.GetEntry(rename.oldKey);
                var newEntry = collection.SharedData.GetEntry(rename.key);
                if (oldEntry != null && newEntry != null && oldEntry.Id != newEntry.Id)
                    throw new InvalidOperationException("Duplicate carry translation keys '" + rename.oldKey + "' and '" + rename.key + "'. Restore the original shared entry before updating.");
            }
            foreach (var rename in CarryKeyRenames)
            {
                var oldEntry = collection.SharedData.GetEntry(rename.oldKey);
                if (oldEntry == null) continue;
                // Native IDs are unchanged, so saved ID-based scene/prefab references remain valid even on retry.
                collection.SharedData.RenameKey(oldEntry.Id, rename.key);
                EditorUtility.SetDirty(collection.SharedData);
            }
        }

        private static void EnsureCarryControlsTranslations(EasySuiteLab lab)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            var english = collection ? collection.GetTable(new LocaleIdentifier("en")) as StringTable : null;
            var russian = collection ? collection.GetTable(new LocaleIdentifier("ru")) as StringTable : null;
            if (!collection || !english || !russian || !lab.Texts || !lab.Texts.catalog)
                throw new InvalidOperationException("Restore the Playground localization before updating carry controls.");
            var renames = CarryKeyRenames.ToDictionary(row => row.oldKey, row => row.key);
            bool catalogChanged = false;
            var entries = new List<CatalogTranslation>();
            foreach (var entry in lab.Texts.catalog.entries ?? Array.Empty<CatalogTranslation>())
            {
                if (entry != null && renames.TryGetValue(entry.id, out string key))
                { entries.Add(new CatalogTranslation { id = key, text = entry.text }); catalogChanged = true; }
                else entries.Add(entry);
            }
            foreach (var key in CarryKeyRenames.Select(row => row.key))
                if (entries.Count(entry => entry != null && entry.id == key) > 1)
                    throw new InvalidOperationException("Duplicate carry catalog entry '" + key + "'. Restore its original entry before updating.");
            RenameCarryTableKeys(collection);
            var walking = CarryInertiaTranslations().Single(row => row.key == EasySuiteLab.CarryWalkingLagNoticeKey);
            var rows = CarryControlsTranslations().Concat(new[]
            {
                walking, (key: CarryPoseKey, en: CarryPoseEnglish, ru: CarryPoseRussian),
                (key: CarryUxKey, en: CarryUxEnglish, ru: CarryUxRussian),
                (key: CarryInertiaSignKey, en: CarryInertiaSignEnglish, ru: CarryInertiaSignRussian)
            });
            foreach (var row in rows)
            {
                if (collection.SharedData.GetEntry(row.key) == null) EditorUtility.SetDirty(collection.SharedData);
                var en = english.GetEntry(row.key); var ru = russian.GetEntry(row.key);
                if (en == null) { en = english.AddEntry(row.key, row.en); EditorUtility.SetDirty(english); }
                if (ru == null) { ru = russian.AddEntry(row.key, row.ru); EditorUtility.SetDirty(russian); }
                if (en.Value != row.en) { en.Value = row.en; EditorUtility.SetDirty(english); }
                if (ru.Value != row.ru) { ru.Value = row.ru; EditorUtility.SetDirty(russian); }
                if (!entries.Any(entry => entry != null && entry.id == row.key))
                {
                    entries.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id) });
                    catalogChanged = true;
                }
            }
            if (catalogChanged) { lab.Texts.catalog.SetEntries(entries.ToArray()); EditorUtility.SetDirty(lab.Texts.catalog); }
            foreach (var asset in new Object[] { english, russian, collection.SharedData, lab.Texts.catalog }) AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static (string key, string en, string ru)[] CarryInertiaTranslations() => new[]
        {
            (EasySuiteLab.CarryWalkingLagAllKey, "Carry walking lag: all axes", "Инерция ходьбы: все оси"),
            (EasySuiteLab.CarryWalkingLagNoForwardKey, "Carry walking lag: no forward / back", "Инерция ходьбы: без вперёд / назад"),
            (EasySuiteLab.CarryWalkingLagNoneKey, "Carry walking lag: off", "Инерция ходьбы: выключена"),
            (EasySuiteLab.CarryWalkingLagNoticeKey, "Close Tab: compare W/S and A/D. The walking-lag setting changes movement axes; Follow Speed changes physical response.", "Закройте Tab: сравните W/S и A/D. Инерция ходьбы меняет оси перемещения; Follow Speed меняет физическое следование."),
            (CarryInertiaSignKey, CarryInertiaSignEnglish, CarryInertiaSignRussian)
        };

        private static void EnsureCarryInertiaTranslations(EasySuiteLab lab)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            var english = collection ? collection.GetTable(new LocaleIdentifier("en")) as StringTable : null;
            var russian = collection ? collection.GetTable(new LocaleIdentifier("ru")) as StringTable : null;
            if (!collection || !english || !russian || !lab.Texts || !lab.Texts.catalog)
                throw new InvalidOperationException("Restore the Playground localization before updating carry-inertia labels.");
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries ?? Array.Empty<CatalogTranslation>());
            foreach (var row in CarryInertiaTranslations())
            {
                if (english.GetEntry(row.key) == null) english.AddEntry(row.key, row.en);
                if (russian.GetEntry(row.key) == null) russian.AddEntry(row.key, row.ru);
                if (!entries.Any(entry => entry != null && entry.id == row.key))
                    entries.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id) });
            }
            lab.Texts.catalog.SetEntries(entries.ToArray());
            foreach (var asset in new Object[] { english, russian, collection.SharedData, lab.Texts.catalog })
            { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        }

        private static void UpdateCompositionSign(EasySuiteLab lab, string key, string englishText, string russianText, params string[] previousKeys)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            var english = collection ? collection.GetTable(new LocaleIdentifier("en")) as StringTable : null;
            var russian = collection ? collection.GetTable(new LocaleIdentifier("ru")) as StringTable : null;
            if (!collection || !english || !russian || !lab.Texts || !lab.Texts.catalog)
                throw new InvalidOperationException("Restore the Playground localization collection before updating its signs.");
            var sign = FindSign(lab, collection, previousKeys.Concat(new[] { key }).ToArray());
            if (!sign) throw new InvalidOperationException("Restore the Playground sign for " + key + ".");
            if (english.GetEntry(key) == null) english.AddEntry(key, englishText);
            if (russian.GetEntry(key) == null) russian.AddEntry(key, russianText);
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries ?? Array.Empty<CatalogTranslation>());
            if (!entries.Any(entry => entry != null && entry.id == key))
                entries.Add(new CatalogTranslation { id = key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(key).Id) });
            lab.Texts.catalog.SetEntries(entries.ToArray());
            BindSign(sign, collection, key, englishText);
            foreach (var asset in new Object[] { english, russian, collection.SharedData, lab.Texts.catalog })
            { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        }

        private static void ConfigureHealthReward(EasySuiteLab lab)
        {
            if (!lab.TargetHealth || !lab.EnemyRewardProfile || !lab.Wallet || !lab.ResourceDatabase)
                throw new InvalidOperationException("Restore the sample target, reward profile, wallet and resource database before updating.");
            lab.EnemyRewardProfile.Database = lab.ResourceDatabase;
            EditorUtility.SetDirty(lab.EnemyRewardProfile); AssetDatabase.SaveAssetIfDirty(lab.EnemyRewardProfile);
            lab.TargetHealth.RewardProfile = lab.EnemyRewardProfile;
            lab.TargetHealth.RewardRecipient = lab.Wallet;
            lab.TargetHealth.RewardEnabled = true;
            EditorUtility.SetDirty(lab.TargetHealth);
        }

        private static void ConfigureOutline(InteractionTarget target)
        {
            target.HighlightWhenFocused = true;
            target.RefreshOutlineRenderers();
            EditorUtility.SetDirty(target);
        }

        private static LocalizedText FindSign(EasySuiteLab lab, StringTableCollection collection, params string[] keys)
        {
            var ids = keys.Select(key => collection.SharedData.GetEntry(key)?.Id).Where(id => id.HasValue).Select(id => id.Value).ToArray();
            return lab.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LocalizedText>(true))
                .FirstOrDefault(text => text.text.TableReference.TableCollectionNameGuid == collection.SharedData.TableCollectionNameGuid && ids.Contains(text.text.TableEntryReference.KeyId));
        }

        private static void BindSign(LocalizedText sign, StringTableCollection collection, string key, string fallback)
        {
            sign.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(key).Id);
            sign.fallback = fallback;
            if (sign.TryGetComponent<TextMesh>(out var mesh)) { mesh.text = fallback; EditorUtility.SetDirty(mesh); }
            EditorUtility.SetDirty(sign);
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { "", "/Materials", "/Definitions", "/Definitions/Resources", "/Definitions/Rewards", "/Prefabs", "/Input", "/Localization", "/UI" })
                Directory.CreateDirectory(Content + folder);
            AssetDatabase.Refresh();
        }

        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Required local asset is missing: " + path);

        private static T Definition<T>(string name, Action<T> configure) where T : ScriptableObject
        {
            string path = Content + "/Definitions/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>(); asset.name = name; configure(asset); AssetDatabase.CreateAsset(asset, path); return asset;
        }

        private static void Profile(Health health, HealthDefinition definition)
        {
            // The serialized authoring profile persists to scene/prefab; runtime health is initialized by Health itself.
            var serialized = new SerializedObject(health); serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreatePlayerPrefab(CombatStatDefinition speed)
        {
            string path = Content + "/Prefabs/Combat FPS Player.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var source = RequireAsset<GameObject>(MovementPlayer).GetComponentInChildren<FirstPersonCharacterController>(true);
            string originalGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(source)));
            string replacementGuid = AssetDatabase.FindAssets("CombatFirstPersonController t:MonoScript")
                .FirstOrDefault(guid => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)).GetClass() == typeof(CombatFirstPersonController));
            if (string.IsNullOrEmpty(replacementGuid)) throw new InvalidOperationException("The built-in Combat first-person integration is unavailable. Resolve framework compilation errors first.");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(MovementPlayer);
            string yaml = File.ReadAllText(Path.Combine(package.resolvedPath, MovementPlayer.Substring(package.assetPath.Length + 1))).Replace("\r\n", "\n");
            string marker = "m_Script: {fileID: 11500000, guid: " + originalGuid + ", type: 3}";
            if (yaml.IndexOf(marker, StringComparison.Ordinal) < 0 || yaml.IndexOf(marker, StringComparison.Ordinal) != yaml.LastIndexOf(marker, StringComparison.Ordinal))
                throw new InvalidOperationException("Expected exactly one first-person controller in the EasyMovement template.");
            // Only replace the script identity. Local YAML file IDs, camera references and serialized movement defaults stay intact.
            yaml = yaml.Replace(marker, marker.Replace(originalGuid, replacementGuid))
                .Replace("EasyMovement.FirstPerson::EasyMovement.FirstPerson.FirstPersonCharacterController\n", "EasyCombat.EasyMovement::EasyCombat.Integrations.CombatFirstPersonController\n");
            File.WriteAllText(path, yaml); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var controller = contents.GetComponentInChildren<CombatFirstPersonController>(true);
                if (!controller || !controller.LookPivot || !controller.defaultCamera || contents.GetComponentsInChildren<FirstPersonCharacterController>(true).Length != 1)
                    throw new InvalidOperationException("Combat player conversion did not preserve the original controller and view links.");
                var influences = controller.GetComponent<MovementInfluences>() ?? controller.gameObject.AddComponent<MovementInfluences>();
                influences.MovementSpeedStat = speed;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return RequireAsset<GameObject>(path);
        }

        private static InputActionAsset CreateActions()
        {
            string path = Content + "/Input/EasySuite Controls.inputactions";
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path); if (existing) return existing;
            const string sourcePath = "Packages/com.oreethehat.easy-game-framework/Modules/EasyInput/Runtime/Resources/EasyInputDefaults.inputactions";
            var source = RequireAsset<InputActionAsset>(sourcePath);
            var document = JObject.Parse(source.ToJson()); document["name"] = "EasySuite Controls";
            var maps = (JArray)document["maps"];
            var gameplay = maps.OfType<JObject>().Single(map => (string)map["name"] == "Gameplay");
            var actions = (JArray)gameplay["actions"]; var bindings = (JArray)gameplay["bindings"];
            var settings = actions.OfType<JObject>().Single(action => (string)action["name"] == "Settings");
            settings.Remove(); settings["name"] = "Menu";
            var suiteActions = new JArray(settings); var suiteBindings = new JArray();
            foreach (var binding in bindings.OfType<JObject>().Where(binding => (string)binding["action"] == "Settings").ToArray())
            { binding.Remove(); binding["action"] = "Menu"; suiteBindings.Add(binding); }
            // Action and binding IDs from the source are retained, including the moved Settings/Menu action.
            AddSuiteAction(suiteActions, suiteBindings, "Build", "<Keyboard>/t");
            AddSuiteAction(suiteActions, suiteBindings, "InteractionMenu", "<Keyboard>/h");
            AddSuiteAction(suiteActions, suiteBindings, "Save", "<Keyboard>/f5");
            AddSuiteAction(suiteActions, suiteBindings, "Load", "<Keyboard>/f9");
            maps.Add(new JObject { ["name"] = "Suite", ["id"] = Guid.NewGuid().ToString(), ["actions"] = suiteActions, ["bindings"] = suiteBindings });
            File.WriteAllText(path, document.ToString(Formatting.Indented)); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var result = RequireAsset<InputActionAsset>(path);
            if (result.FindAction("Gameplay/Settings", false) != null || result.FindAction("Suite/Menu", true).id != source.FindAction("Gameplay/Settings", true).id)
                throw new InvalidOperationException("Suite/Menu must be the original Settings action, with no duplicate Gameplay consumer.");
            return result;
        }

        private static void AddSuiteAction(JArray actions, JArray bindings, string name, string path)
        {
            actions.Add(new JObject { ["name"] = name, ["type"] = "Button", ["id"] = Guid.NewGuid().ToString(), ["expectedControlType"] = "Button", ["processors"] = "", ["interactions"] = "", ["initialStateCheck"] = false });
            bindings.Add(new JObject { ["name"] = "", ["id"] = Guid.NewGuid().ToString(), ["path"] = path, ["interactions"] = "", ["processors"] = "", ["groups"] = "KeyboardMouse", ["action"] = name, ["isComposite"] = false, ["isPartOfComposite"] = false });
        }

        private static InputProfile CreateInputProfile()
        {
            string path = Content + "/Input/EasySuite Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<InputProfile>(path); if (profile) return profile;
            profile = Object.Instantiate(RequireAsset<InputProfile>("Packages/com.oreethehat.easy-game-framework/Modules/EasyInput/Runtime/Resources/EasyInputStandard.asset"));
            profile.name = "EasySuite Profile"; profile.DefaultMaps = new[] { "Gameplay", "Suite" };
            AssetDatabase.CreateAsset(profile, path); return profile;
        }

        private static BuildingDefinition[] CreateBuildings(ResourceDefinition wood, HealthDefinition health)
        {
            return new[]
            {
                Building("Barrier", new Vector3(2, 1.2f, .6f), steel, 4, wood, health),
                Building("Pillar", new Vector3(1, 2.4f, 1), gold, 6, wood, health),
                Building("Block", new Vector3(1, .75f, 1), blue, 2, wood, health)
            };
        }

        private static BuildingDefinition Building(string name, Vector3 size, Material material, long price, ResourceDefinition wood, HealthDefinition health)
        {
            string prefabPath = Content + "/Prefabs/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                var root = new GameObject(name); root.SetActive(false);
                try
                {
                    Primitive(name + " Body", PrimitiveType.Cube, root.transform, new Vector3(0, size.y * .5f, 0), size, material);
                    var accent = Primitive("Top trim", PrimitiveType.Cube, root.transform, new Vector3(0, size.y - .06f, 0), new Vector3(size.x + .03f, .06f, size.z + .03f), white);
                    RemoveCollider(accent);
                    Profile(root.AddComponent<Health>(), health);
                    var repair = root.AddComponent<BuildingHealth>(); repair.RepairCostPerHealth = new[] { new BuildingCost(wood.Id, 1) };
                    root.SetActive(true); prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            return Definition<BuildingDefinition>(name, definition =>
            {
                definition.Prefab = prefab; definition.DisplayName = name; definition.Placement = PlacementMode.Grid;
                definition.GridSize = 1; definition.GridOrigin = Vector3.zero;
                definition.BoundsCenter = new Vector3(0, size.y * .5f, 0); definition.BoundsSize = size;
                definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost(wood.Id, price) };
                definition.RequireZone = true; definition.ZoneId = "suite.pad"; definition.RequireSurface = true;
                definition.CheckObstacles = true; definition.DemolitionRefund = .5f; definition.SurfaceProbeDistance = .15f;
            });
        }

        private static void CreatePaidAbility(EasySuiteLab lab, GameObject body)
        {
            var energy = Definition<EasyCombat.ResourceDefinition>("Combat Energy Cost", _ => { });
            var mapping = Definition<CombatResourceMap>("Shared Energy Mapping", value =>
                value.Entries = new[] { new CombatResourceMap.Entry(energy, lab.Mana) });
            var provider = body.AddComponent<EasyResourcesCostProvider>(); provider.Container = lab.Wallet; provider.Mapping = mapping;
            var attack = Definition<AttackDefinition>("Energy Strike Damage", value => { value.DisplayName = "Energy Strike"; value.Damage = 15; });
            var action = Definition<AbilityAttackAction>("Energy Strike Action", value => { value.Attack = attack; value.DirectToTarget = true; });
            lab.PaidAbility = Definition<AbilityDefinition>("Energy Strike", value =>
            {
                value.DisplayName = "Energy Strike"; value.TargetMode = AbilityTargetMode.Unit; value.UnitTargets = AreaTargetFilter.All;
                value.Range = 40; value.CastTime = .35f; value.Cooldown = .5f; value.MaximumCharges = 0;
                value.Costs = new[] { new EasyCombat.ResourceCost(energy, 2.5f) }; value.Actions = new CombatAbilityAction[] { action };
            });
            lab.Abilities = body.AddComponent<CombatAbilities>(); lab.Abilities.Muzzle = lab.Character.LookPivot;
            lab.Abilities.Configure(new[] { lab.PaidAbility });
        }

        private static void CreateCombatCatalog(EasySuiteLab lab, params ScriptableObject[] primary)
        {
            AssetDatabase.SaveAssets();
            var roots = new List<string>(primary.Select(AssetDatabase.GetAssetPath))
            {
                AssetDatabase.GetAssetPath(lab.PaidAbility),
                AssetDatabase.GetAssetPath(lab.SlowField.Effect),
                AssetDatabase.GetAssetPath(lab.SlowField.SensorProfile),
                Content + "/Definitions/Shared Energy Mapping.asset"
            };
            var entries = new List<CombatCatalogEntry>();
            foreach (string path in AssetDatabase.GetDependencies(roots.ToArray(), true).Distinct().OrderBy(value => value, StringComparer.Ordinal))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!definition || definition is CombatDefinitionCatalog || definition.GetType().Namespace?.StartsWith("EasyCombat", StringComparison.Ordinal) != true) continue;
                entries.Add(new CombatCatalogEntry(AssetDatabase.AssetPathToGUID(path), definition));
            }
            lab.CombatCatalog = Definition<CombatDefinitionCatalog>("Combat Save Catalog", value =>
            {
                if (!value.Configure(entries.ToArray(), "EasySuiteLab/1")) throw new InvalidOperationException("Could not create a stable Combat save catalog.");
            });
        }

        private static void CreateMaterials()
        {
            floor = Material("Floor", new Color(.09f, .115f, .15f));
            dark = Material("Ink", new Color(.035f, .055f, .085f));
            steel = Material("Slate", new Color(.28f, .38f, .46f));
            gold = Material("Gold", new Color(.96f, .64f, .22f));
            blue = Material("Blue", new Color(.12f, .52f, .85f));
            coral = Material("Coral", new Color(.88f, .28f, .24f));
            white = Material("Ivory", new Color(.84f, .9f, .92f));
        }

        private static Material Material(string name, Color color)
        {
            string path = Content + "/Materials/" + name + ".mat";
            var value = AssetDatabase.LoadAssetAtPath<Material>(path); if (value) return value;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("The lab requires the project's installed URP Lit shader.");
            value = new Material(shader) { name = name }; value.SetColor("_BaseColor", color); value.SetFloat("_Smoothness", .3f);
            AssetDatabase.CreateAsset(value, path); return value;
        }

        private static void CreateRoom()
        {
            var room = new GameObject("Lab Architecture").transform;
            Primitive("Floor", PrimitiveType.Cube, room, new Vector3(0, -.2f, 0), new Vector3(26, .4f, 24), floor);
            Primitive("Rear wall", PrimitiveType.Cube, room, new Vector3(0, 2, 11.7f), new Vector3(26, 4, .35f), dark);
            Primitive("Left rail", PrimitiveType.Cube, room, new Vector3(-12.8f, .65f, 0), new Vector3(.3f, 1.3f, 24), steel);
            Primitive("Right rail", PrimitiveType.Cube, room, new Vector3(12.8f, .65f, 0), new Vector3(.3f, 1.3f, 24), steel);
            Primitive("Entry rail", PrimitiveType.Cube, room, new Vector3(0, .65f, -11.7f), new Vector3(26, 1.3f, .3f), steel);
            for (int i = 0; i < 4; i++)
            {
                var guide = Primitive("Entry guide " + i, PrimitiveType.Cube, room, new Vector3(0, .008f, -7 + i), new Vector3(.12f, .016f, .55f), gold);
                RemoveCollider(guide);
            }
            var pad = new GameObject("Building Pad"); pad.transform.SetParent(room, false); pad.transform.position = new Vector3(0, 0, 2);
            var zone = pad.AddComponent<BuildingZone>(); zone.ZoneId = "suite.pad";
            zone.Center = new Vector3(0, 2.5f, 0); zone.Size = new Vector3(8, 5, 10);
            Primitive("Build surface", PrimitiveType.Cube, pad.transform, new Vector3(0, .01f, 0), new Vector3(8, .02f, 10), steel);
            for (int i = -4; i <= 4; i++)
                RemoveCollider(Primitive("Grid X " + i, PrimitiveType.Cube, pad.transform, new Vector3(i, .024f, 0), new Vector3(.018f, .008f, 10), gold));
            for (int i = -5; i <= 5; i++)
                RemoveCollider(Primitive("Grid Z " + i, PrimitiveType.Cube, pad.transform, new Vector3(0, .024f, i), new Vector3(8, .008f, .018f), gold));
            Sign(room, new Vector3(0, 3, 10.9f), "sign.title", "EASY SUITE\nINTERACTIVE LAB", 6.5f, gold);
            Sign(room, new Vector3(0, 1.6f, 7.7f), "sign.build", "02  /  BUILD\nT · choose  /  click · place", 5.2f, gold);

            var light = new GameObject("Key Light").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48, -25, 0); light.intensity = 1.4f; light.color = new Color(1, .94f, .84f);
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.43f, .5f, .62f);
            RenderSettings.skybox = null; RenderSettings.fog = false;
        }

        private static void CreateStations(EasySuiteLab lab, HealthDefinition targetProfile, FeedbackPreset feedback)
        {
            var stations = new GameObject("Lab Stations").transform;
            var crate = Primitive("Physical Crate", PrimitiveType.Cube, stations, new Vector3(-6, .6f, -3), new Vector3(1.1f, 1.1f, 1.1f), gold);
            var body = crate.AddComponent<Rigidbody>(); body.mass = 3; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var grab = crate.AddComponent<GrabAction>(); grab.ActionId = "suite.grab"; grab.GrabRange = 3.5f; grab.ThrowSpeed = 8;
            grab.RotationMode = GrabRotation.Stabilized; grab.IgnoreOwnerCollisions = true;
            grab.HoldDistance = 2f; grab.HoldOffset = Vector3.zero; grab.FollowSpeed = .5f; grab.MovementLag = Vector3.one * .5f;
            grab.Target.DisplayName = "Carry crate"; grab.Target.FocusPoint = crate.transform;
            lab.Crate = crate.transform;
            Sign(stations, new Vector3(-4.7f, 1.6f, 1.4f), CarryPoseKey, CarryPoseEnglish, 5.2f, gold);

            var workbench = new GameObject("Workbench"); workbench.SetActive(false); workbench.transform.SetParent(stations, false);
            workbench.transform.position = new Vector3(-7, 0, 4);
            Primitive("Bench", PrimitiveType.Cube, workbench.transform, new Vector3(0, .65f, 0), new Vector3(3.6f, 1.3f, 1.5f), steel);
            Primitive("Console", PrimitiveType.Cube, workbench.transform, new Vector3(0, 1.45f, .25f), new Vector3(1.5f, .65f, .3f), blue);
            var focus = Child(workbench.transform, "Console focus", new Vector3(0, 1.6f, -.1f));
            var point = Child(workbench.transform, "Approach point", new Vector3(0, 0, -1.8f));
            var work = workbench.AddComponent<EasySuiteWorkAction>(); work.Lab = lab;
            work.ActionId = "suite.calibrate"; work.ActionLabel = "Calibrate workbench"; work.LabelKey = "action.work";
            work.Channel = "body"; work.Execution = InteractionExecutionMode.Timed; work.Duration = 2.5f;
            work.ApproachPoint = point; work.ApproachDistance = .12f; work.ApproachAngle = 10; work.Timeout = 15;
            work.Target.DisplayName = "Workbench"; work.Target.FocusPoint = focus;
            work.Target.PersistentId = "framework.workbench";
            lab.SaveWorkbench = work.Target;
            var terminal = workbench.AddComponent<EasySuiteOpenAction>(); terminal.Lab = lab; terminal.BuildingCatalog = false;
            terminal.ActionId = "suite.terminal"; terminal.ActionLabel = "Open lab terminal"; terminal.LabelKey = "action.terminal"; terminal.Channel = "";
            var catalog = workbench.AddComponent<EasySuiteOpenAction>(); catalog.Lab = lab; catalog.BuildingCatalog = true;
            catalog.ActionId = "suite.catalog"; catalog.ActionLabel = "Open building catalog"; catalog.LabelKey = "action.catalog"; catalog.Channel = "";
            lab.Stock = workbench.AddComponent<ResourceContainer>();
            var wood = workbench.AddComponent<ResourceTransferAction>(); wood.Resource = lab.Wood; wood.Amount = 4; wood.ActorContainer = lab.Wallet;
            wood.ActionId = "suite.takewood"; wood.LabelKey = "action.takewood"; wood.Channel = "";
            var energy = workbench.AddComponent<ResourceTransferAction>(); energy.Resource = lab.Mana; energy.Amount = (int)(10 * lab.Mana.UnitsPerWhole); energy.ActorContainer = lab.Wallet;
            energy.ActionId = "suite.takeenergy"; energy.LabelKey = "action.takeenergy"; energy.Channel = "";
            workbench.SetActive(true);
            Sign(stations, new Vector3(-7, 3, 5.1f), "sign.work", "WORKBENCH\nH · actions  /  E · calibrate", 5.4f, blue);

            var target = new GameObject("Combat Target"); target.SetActive(false); target.transform.SetParent(stations, false);
            target.transform.position = new Vector3(7, 0, 4);
            Primitive("Target base", PrimitiveType.Cylinder, target.transform, new Vector3(0, .15f, 0), new Vector3(2, .15f, 2), dark);
            Primitive("Target body", PrimitiveType.Capsule, target.transform, new Vector3(0, 1.45f, 0), new Vector3(1, 1.15f, 1), coral);
            lab.TargetHealth = target.AddComponent<Health>(); Profile(lab.TargetHealth, targetProfile);
            lab.TargetHealth.StatusesEnabled = true; lab.TargetHealth.FeedbacksEnabled = true;
            lab.TargetHealth.ConfigureFeedbacks(feedback, feedback, feedback, lab.FeedbackContextProvider);
            target.SetActive(true);
            Sign(stations, new Vector3(7, 3.4f, 5.4f), "sign.combat", "03  /  COMBAT\nClick · fireball + burn", 5.4f, coral);

            var field = new GameObject("Slow Field"); field.transform.SetParent(stations, false); field.transform.position = new Vector3(6.5f, .6f, -2.3f);
            var disk = Primitive("Blue field surface", PrimitiveType.Cylinder, field.transform, new Vector3(0, -.57f, 0), new Vector3(4.4f, .025f, 4.4f), blue);
            RemoveCollider(disk);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                RemoveCollider(Primitive("Field marker " + i, PrimitiveType.Cube, field.transform, new Vector3(Mathf.Sin(angle) * 2.2f, -.5f, Mathf.Cos(angle) * 2.2f), new Vector3(.09f, .16f, .09f), white));
            }
            var area = Definition<AreaDefinition>("Slow Field Area", value => { value.Shape = AreaShape.Sphere; value.Radius = 2.35f; value.AffectOwner = true; value.Targets = AreaTargetFilter.All; });
            var sensor = Definition<AreaTriggerDefinition>("Slow Field Sensor", value => { value.Area = area; value.Duration = 0; value.StayInterval = .1f; value.StayImmediately = true; });
            var effect = Definition<MovementInfluenceDefinition>("Slow Field Effect", value => value.SpeedMultiplier = .45f);
            lab.SlowField = field.AddComponent<MovementField>(); lab.SlowField.SensorProfile = sensor; lab.SlowField.Effect = effect;
            Sign(stations, new Vector3(4.8f, 1.4f, 1.5f), "sign.slow", "SLOW FIELD\nMove through the blue zone", 4.7f, blue);
        }

        private static void CreateFeedbackManagers()
        {
            var managers = new GameObject("Feedback Services");
            managers.AddComponent<AudioManager>(); managers.AddComponent<VisualFeedbackManager>();
            managers.AddComponent<GamepadVibrationManager>(); managers.AddComponent<CameraShakeManager>(); managers.AddComponent<MusicManager>();
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = position; return value;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var value = GameObject.CreatePrimitive(type); value.name = name; value.transform.SetParent(parent, false);
            value.transform.localPosition = position; value.transform.localScale = scale; value.GetComponent<Renderer>().sharedMaterial = material; return value;
        }

        private static void RemoveCollider(GameObject value)
        { var collider = value.GetComponent<Collider>(); if (collider) Object.DestroyImmediate(collider); }

        private static void Sign(Transform parent, Vector3 position, string key, string fallback, float width, Material accent)
        {
            var root = Child(parent, "Sign " + key, position);
            RemoveCollider(Primitive("Plaque", PrimitiveType.Cube, root, Vector3.zero, new Vector3(width, 1.15f, .08f), dark));
            RemoveCollider(Primitive("Accent", PrimitiveType.Cube, root, new Vector3(-width * .5f + .04f, 0, -.055f), new Vector3(.08f, 1.15f, .04f), accent));
            var textObject = Child(root, "Label", new Vector3(0, 0, -.065f)).gameObject;
            var text = textObject.AddComponent<TextMesh>(); text.font = font; text.fontSize = 64; text.characterSize = .075f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = Color.white; text.text = fallback;
            textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            Localize(textObject, key, fallback);
            textObject.GetComponent<EasySuiteLabel>().MeshBounds = new Vector2(width - .4f, .85f);
        }

        private static void CreateHudAndWindows(EasySuiteLab lab)
        {
            var canvasObject = new GameObject("EasySuite UI", typeof(RectTransform)); canvasObject.SetActive(false);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1; // At 800x600 the logical canvas is 960x720: all authored panels still fit.
            canvasObject.AddComponent<GraphicRaycaster>();
            var provider = canvasObject.AddComponent<EasyInputWindowProvider>(); provider.Player = lab.Player;
            provider.GameplayMaps = new[] { "Gameplay", "Building" }; // Suite/Menu remains available over nested windows and after death.
            var eventObject = new GameObject("EasySuite EventSystem"); eventObject.SetActive(false);
            var events = eventObject.AddComponent<EventSystem>(); eventObject.AddComponent<EasyUIInputModule>();
            lab.Windows = canvasObject.AddComponent<WindowManager>(); lab.Windows.InputProvider = provider; lab.Windows.EventSystem = events;
            lab.Windows.ModalShade = new Color(.01f, .02f, .04f, .76f);

            var header = Rect("HUD Header", canvasObject.transform, new Vector2(.5f, 1), new Vector2(0, -52), new Vector2(0, 104));
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one; header.sizeDelta = new Vector2(0, 104);
            header.gameObject.AddComponent<Image>().color = new Color(.025f, .045f, .075f, .93f);
            var title = Text("Title", header, new Vector2(0, .5f), new Vector2(24, 31), new Vector2(500, 32), 22, TextAnchor.MiddleLeft);
            title.rectTransform.pivot = new Vector2(0, .5f); Localize(title.gameObject, "lab.title", "EASY SUITE  /  INTERACTIVE LAB");
            lab.Performance = Text("Performance", header, new Vector2(1, .5f), new Vector2(-24, 31), new Vector2(260, 26), 15, TextAnchor.MiddleRight);
            lab.Performance.rectTransform.pivot = new Vector2(1, .5f); lab.Performance.color = new Color(.5f, .8f, .92f);
            lab.Hud = Text("Player and world", header, new Vector2(0, .5f), new Vector2(24, -18), new Vector2(890, 52), 17, TextAnchor.MiddleLeft);
            lab.Hud.rectTransform.pivot = new Vector2(0, .5f); lab.Hud.text = "Loading player, resources and language…";

            var footer = Rect("HUD Prompts", canvasObject.transform, new Vector2(.5f, 0), new Vector2(0, 61), new Vector2(0, 122));
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.sizeDelta = new Vector2(0, 122);
            footer.gameObject.AddComponent<Image>().color = new Color(.025f, .045f, .075f, .9f);
            lab.Prompt = Text("Interaction Prompt", footer, new Vector2(.5f, .5f), new Vector2(15, 19), new Vector2(840, 57), 19, TextAnchor.MiddleCenter);
            lab.Notice = Text("Notice", footer, new Vector2(.5f, .5f), new Vector2(0, -35), new Vector2(880, 30), 15, TextAnchor.MiddleCenter);
            lab.Notice.color = new Color(.97f, .75f, .39f);
            var icon = Rect("Binding Icon", footer, new Vector2(.5f, .5f), new Vector2(-435, 20), new Vector2(28, 28));
            lab.PromptIcon = icon.gameObject.AddComponent<Image>(); lab.PromptIcon.enabled = false; lab.PromptIcon.preserveAspect = true;
            var crosshair = Text("Crosshair", canvasObject.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(22, 22), 22, TextAnchor.MiddleCenter);
            crosshair.text = "+"; crosshair.color = new Color(.9f, .96f, 1, .8f);

            bool createdWindows = !File.Exists(Content + "/UI/Integration Windows/EasyUI Windows.asset");
            lab.IntegrationWindows = IntegrationWindowBuilder.CreateAt(Content + "/UI/Integration Windows");
            if (createdWindows)
            {
                string settingsPath = AssetDatabase.GetAssetPath(lab.IntegrationWindows.Settings.Prefab);
                var settings = PrefabUtility.LoadPrefabContents(settingsPath);
                try
                {
                    settings.GetComponent<InputSettingsWindow>().Actions = new[] { "Gameplay/Interact", "Gameplay/Jump", "Gameplay/Move", "Gameplay/Fire",
                        "Gameplay/Look", "Gameplay/Sprint", "Gameplay/Crouch", "Gameplay/ThrowItem", "Gameplay/RotateItem",
                        "Suite/Menu", "Suite/Build", "Suite/InteractionMenu", "Suite/Save", "Suite/Load", "Narrative/Journal" };
                    PrefabUtility.SaveAsPrefabAsset(settings, settingsPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(settings); }
            }
            lab.ControlsWindow = lab.IntegrationWindows.Settings;
            lab.BuildWindow = lab.IntegrationWindows.BuildingRadial;
            lab.InteractionWindow = lab.IntegrationWindows.Interaction;
            lab.MenuWindow = CreateMenu(feedback: lab.UiFeedback);
            // Runtime EasySuiteLab creates inactive instances, binds per-player references/context, then opens them.
            eventObject.SetActive(true); canvasObject.SetActive(true);
        }

        private static WindowDefinition CreateMenu(FeedbackPreset feedback)
        {
            string definitionPath = Content + "/UI/Lab Menu.asset";
            var existing = AssetDatabase.LoadAssetAtPath<WindowDefinition>(definitionPath); if (existing) return existing;
            var root = new GameObject("Lab Menu", typeof(RectTransform)); root.SetActive(false);
            try
            {
                var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(730, 660);
                root.AddComponent<Image>().color = new Color(.035f, .06f, .095f, .98f);
                var window = root.AddComponent<UIWindow>(); window.Policy = new WindowPolicy { BlockGameplay = true, Cursor = WindowCursor.Visible, FadeIn = .08f, FadeOut = .08f };
                root.AddComponent<EasySuiteWindow>();
                var response = root.AddComponent<WindowFeedbacks>(); response.Confirm = feedback;
                var title = Text("Title", rect, new Vector2(.5f, 1), new Vector2(0, -38), new Vector2(680, 44), 29, TextAnchor.MiddleCenter);
                Localize(title.gameObject, "menu.title", "LAB CONTROL");
                var subtitle = Text("Subtitle", rect, new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(660, 30), 16, TextAnchor.MiddleCenter);
                Localize(subtitle.gameObject, "menu.subtitle", "Experiment, save, restore. Each window owns its input request.");
                var commands = OriginalMenuCommands;
                var labels = new[] { "Settings", "Change language", "Building catalog", "Interaction actions", "Save progress  ·  F5", "Load progress  ·  F9", "Save preferences", "Load preferences",
                    "+20 wood / energy", "Energy strike  ·  2.50", "Damage player  ·  25", "Repair nearest building", "Defeat player", "Revive player", "Reset combat target", "Return to lab" };
                for (int i = 0; i < commands.Length; i++)
                {
                    var buttonRect = Rect(commands[i].ToString(), rect, new Vector2(.5f, 1), new Vector2(i % 2 == 0 ? -174 : 174, -140 - i / 2 * 65), new Vector2(326, 49));
                    var image = buttonRect.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .2f, .27f);
                    var button = buttonRect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                    var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.55f);
                    colors.selectedColor = new Color(1.4f, 1.65f, 1.75f); colors.pressedColor = new Color(.7f, .9f, 1); button.colors = colors;
                    buttonRect.gameObject.AddComponent<EasySuiteButton>().Command = commands[i];
                    var label = Text("Label", buttonRect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(308, 43), 18, TextAnchor.MiddleCenter);
                    Localize(label.gameObject, "menu." + commands[i], labels[i]);
                    if (i == 0) window.FirstSelected = button;
                }
                ConfigureCarryInertiaMenu(root, strings);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Content + "/UI/Lab Menu.prefab");
                var definition = ScriptableObject.CreateInstance<WindowDefinition>(); definition.Prefab = prefab.GetComponent<UIWindow>(); definition.Policy = window.Policy.Copy();
                AssetDatabase.CreateAsset(definition, definitionPath); return definition;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void UpdateCarryInertiaMenu(WindowDefinition definition, bool includeFollow = false, bool updateWalkingDefault = false, bool updateFollowDefault = false)
        {
            string path = Content + "/UI/Lab Menu.prefab";
            if (AssetDatabase.GetAssetPath(definition.Prefab) != path)
                throw new InvalidOperationException("Restore the original Lab Menu prefab reference before updating the example.");
            var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
            var persistent = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var beforeButtons = SnapshotMenuButtons(persistent, updateWalkingDefault);
            var beforeBindings = updateWalkingDefault || updateFollowDefault ? SnapshotOtherMenuBindings(persistent, updateFollowDefault ? EasySuiteCommand.CycleCarryFollowSpeed : EasySuiteCommand.CycleCarryWalkingLag) : null;
            var beforeIds = SnapshotMenuObjectIds(persistent);
            string beforeWindow = PersistentMenuId(definition.Prefab);
            string beforeSelection = PersistentMenuId(definition.Prefab.FirstSelected);
            byte[] beforeBytes = File.ReadAllBytes(path);
            GameObject contents = null;
            bool saveAttempted = false;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                bool changed = updateWalkingDefault ? ConfigureWalkingDefault(contents, collection) : updateFollowDefault ? ConfigureCarryFollowButton(contents, collection, contents.GetComponentsInChildren<EasySuiteButton>(true)) : ConfigureCarryInertiaMenu(contents, collection, includeFollow);
                if (changed)
                {
                    saveAttempted = true;
                    if (!PrefabUtility.SaveAsPrefabAsset(contents, path))
                        throw new IOException("Could not save the updated Lab Menu prefab.");
                }
                PrefabUtility.UnloadPrefabContents(contents); contents = null;
                if (saveAttempted) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var afterButtons = SnapshotMenuButtons(reloaded, updateWalkingDefault);
                foreach (var pair in beforeButtons)
                    if (!afterButtons.TryGetValue(pair.Key, out var value) || value != pair.Value)
                        throw new InvalidOperationException("The Lab Menu update changed an existing button identity or translation: " + pair.Key + ".");
                if (updateWalkingDefault || updateFollowDefault)
                {
                    var afterBindings = SnapshotOtherMenuBindings(reloaded, updateFollowDefault ? EasySuiteCommand.CycleCarryFollowSpeed : EasySuiteCommand.CycleCarryWalkingLag);
                    foreach (var pair in beforeBindings)
                        if (!afterBindings.TryGetValue(pair.Key, out var value) || value != pair.Value)
                            throw new InvalidOperationException("The carry-label update changed another menu translation: " + pair.Key + ".");
                    if (updateWalkingDefault) ValidateWalkingDefault(reloaded, collection);
                    if (updateFollowDefault) ValidateFollowDefault(reloaded, collection);
                }
                if (!beforeIds.IsSubsetOf(SnapshotMenuObjectIds(reloaded)) ||
                    PersistentMenuId(reloaded.GetComponent<UIWindow>()) != beforeWindow ||
                    PersistentMenuId(reloaded.GetComponent<UIWindow>().FirstSelected) != beforeSelection)
                    throw new InvalidOperationException("The Lab Menu update changed existing prefab object identities or its initial selection.");
            }
            catch
            {
                if (contents) { PrefabUtility.UnloadPrefabContents(contents); contents = null; }
                if (saveAttempted)
                {
                    // The source asset and its meta stay at the same path. Restore the bytes if Unity's
                    // name matching ever changes an old fileID rather than committing a broken update.
                    File.WriteAllBytes(path, beforeBytes);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                throw;
            }
            finally { if (contents) PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static Dictionary<EasySuiteCommand, string> SnapshotMenuButtons(GameObject prefab, bool allowWalkingDefaultRemap = false)
        {
            if (!prefab) throw new InvalidOperationException("Restore the Lab Menu prefab before updating its buttons.");
            var all = prefab.GetComponentsInChildren<EasySuiteButton>(true);
            var snapshot = new Dictionary<EasySuiteCommand, string>();
            foreach (var command in OriginalMenuCommands)
                if (all.Count(button => button.Command == command) != 1)
                    throw new InvalidOperationException("Expected one existing Lab Menu button for " + command + ".");
            foreach (var command in all.Select(button => button.Command).Distinct())
            {
                var matching = all.Where(button => button.Command == command).ToArray();
                if (matching.Length != 1) throw new InvalidOperationException("Expected one existing Lab Menu button for " + command + ".");
                var button = matching[0];
                var label = button.transform.Find("Label")?.GetComponent<LocalizedText>();
                if (!label) throw new InvalidOperationException("Restore the localized Label of the " + command + " button.");
                string componentIds = string.Join(",", button.GetComponentsInChildren<Component>(true).Select(PersistentMenuId).OrderBy(id => id));
                string identity = ((int)command) + "|" + PersistentMenuId(button.gameObject) + "|" + componentIds;
                // Ordinary migrations retain the existing guards. Revision8 additionally owns one walking-label remap.
                if (command != EasySuiteCommand.CycleCarryFollowSpeed && (!allowWalkingDefaultRemap || command != EasySuiteCommand.CycleCarryWalkingLag))
                    identity += "|" + label.text.TableReference.TableCollectionNameGuid + "|" + label.text.TableEntryReference.KeyId + "|" + label.fallback;
                snapshot.Add(command, identity);
            }
            return snapshot;
        }

        private static Dictionary<EasySuiteCommand, string> SnapshotOtherMenuBindings(GameObject prefab, EasySuiteCommand ignored = EasySuiteCommand.CycleCarryWalkingLag)
        {
            var result = new Dictionary<EasySuiteCommand, string>();
            foreach (var button in prefab.GetComponentsInChildren<EasySuiteButton>(true))
            {
                if (button.Command == ignored) continue;
                var label = button.transform.Find("Label")?.GetComponent<LocalizedText>();
                if (!label) throw new InvalidOperationException("Restore the localized Label of the " + button.Command + " button.");
                result.Add(button.Command, label.text.TableReference.TableCollectionNameGuid + "|" + label.text.TableEntryReference.KeyId + "|" + label.fallback);
            }
            return result;
        }

        private static LocalizedText WalkingDefaultLabel(GameObject root)
        {
            var buttons = root.GetComponentsInChildren<EasySuiteButton>(true).Where(button => button.Command == EasySuiteCommand.CycleCarryWalkingLag).ToArray();
            if (buttons.Length != 1) throw new InvalidOperationException("Restore the single existing walking-lag button before updating its default.");
            var label = buttons[0].transform.Find("Label")?.GetComponent<LocalizedText>();
            if (!label) throw new InvalidOperationException("Restore the localized Label of the walking-lag button.");
            return label;
        }

        private static bool ConfigureWalkingDefault(GameObject root, StringTableCollection collection)
        {
            var entry = collection ? collection.SharedData.GetEntry(EasySuiteLab.CarryWalkingLagAllKey) : null;
            if (entry == null) throw new InvalidOperationException("Restore the existing walking-lag All translation before updating its button.");
            var label = WalkingDefaultLabel(root);
            bool changed = false;
            if (label.text.TableReference.TableCollectionNameGuid != collection.SharedData.TableCollectionNameGuid || label.text.TableEntryReference.KeyId != entry.Id || label.fallback != "Carry walking lag: all axes")
            {
                label.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, entry.Id);
                label.fallback = "Carry walking lag: all axes";
                changed = true;
            }
            var preview = label.GetComponent<TMP_Text>();
            if (preview && preview.text != label.fallback) { preview.text = label.fallback; changed = true; }
            return changed;
        }

        private static void ValidateWalkingDefault(GameObject root, StringTableCollection collection)
        {
            var label = WalkingDefaultLabel(root);
            var entry = collection ? collection.SharedData.GetEntry(EasySuiteLab.CarryWalkingLagAllKey) : null;
            var preview = label.GetComponent<TMP_Text>();
            if (entry == null || label.text.TableReference.TableCollectionNameGuid != collection.SharedData.TableCollectionNameGuid ||
                label.text.TableEntryReference.KeyId != entry.Id || label.fallback != "Carry walking lag: all axes" || (preview && preview.text != label.fallback))
                throw new InvalidOperationException("The saved walking-lag button must retain its identity and reference the existing All translation.");
        }

        private static HashSet<string> SnapshotMenuObjectIds(GameObject prefab)
        {
            var ids = new HashSet<string>();
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                ids.Add(PersistentMenuId(transform.gameObject));
                foreach (var component in transform.GetComponents<Component>()) ids.Add(PersistentMenuId(component));
            }
            return ids;
        }

        private static string PersistentMenuId(Object value)
        {
            if (!value) return "null";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long fileId) || string.IsNullOrEmpty(guid) || fileId == 0)
                throw new InvalidOperationException("Expected a persistent prefab object identity for " + value.name + ".");
            return guid + ":" + fileId;
        }

        private static bool ConfigureCarryInertiaMenu(GameObject root, StringTableCollection collection, bool includeFollow = false)
        {
            if (!collection || collection.SharedData.GetEntry(EasySuiteLab.CarryWalkingLagAllKey) == null || collection.SharedData.GetEntry(EasySuiteLab.CarryWalkingLagNoneKey) == null)
                throw new InvalidOperationException("Create the carry-inertia translations before configuring the Lab Menu.");
            var buttons = root.GetComponentsInChildren<EasySuiteButton>(true);
            // A previous attempt may have saved the shared prefab before the scene revision.
            // Preserve its known new command when resuming an earlier migration.
            bool hasFollowButton = buttons.Any(button => button.Command == EasySuiteCommand.CycleCarryFollowSpeed);
            bool twoColumns = includeFollow || hasFollowButton;
            if (includeFollow && collection.SharedData.GetEntry(EasySuiteLab.CarryFollowAuthoredKey) == null)
                throw new InvalidOperationException("Create the follow-speed translations before updating its menu button.");
            if (buttons.Any(button => !OriginalMenuCommands.Contains(button.Command) && button.Command != EasySuiteCommand.CycleCarryWalkingLag &&
                button.Command != EasySuiteCommand.CycleCarryFollowSpeed))
                throw new InvalidOperationException("The Lab Menu has custom commands; adjust its layout explicitly before updating the example.");
            bool changed = false;
            for (int i = 0; i < OriginalMenuCommands.Length; i++)
            {
                var matching = buttons.Where(button => button.Command == OriginalMenuCommands[i]).ToArray();
                if (matching.Length != 1) throw new InvalidOperationException("Expected one Lab Menu button for " + OriginalMenuCommands[i] + ".");
                var rect = matching[0].transform as RectTransform;
                changed |= SetMenuRect(rect, new Vector2(i % 2 == 0 ? -174 : 174, -126 - i / 2 * 57), new Vector2(326, 44));
                var label = rect.Find("Label") as RectTransform;
                if (!label) throw new InvalidOperationException("Restore the Label of the " + OriginalMenuCommands[i] + " button.");
                changed |= SetMenuRect(label, Vector2.zero, new Vector2(308, 38), true);
            }
            var cycleButtons = buttons.Where(button => button.Command == EasySuiteCommand.CycleCarryWalkingLag).ToArray();
            if (cycleButtons.Length > 1) throw new InvalidOperationException("Remove duplicate carry-inertia buttons before updating the example.");
            RectTransform cycleRect;
            if (cycleButtons.Length == 0)
            {
                cycleRect = Rect(EasySuiteCommand.CycleCarryWalkingLag.ToString(), root.transform, new Vector2(.5f, 1), new Vector2(0, -582), new Vector2(674, 44));
                var image = cycleRect.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .2f, .27f);
                var button = cycleRect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.55f);
                colors.selectedColor = new Color(1.4f, 1.65f, 1.75f); colors.pressedColor = new Color(.7f, .9f, 1); button.colors = colors;
                cycleRect.gameObject.AddComponent<EasySuiteButton>().Command = EasySuiteCommand.CycleCarryWalkingLag;
                var label = Text("Label", cycleRect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(656, 38), 18, TextAnchor.MiddleCenter);
                var source = label.gameObject.AddComponent<LocalizedText>();
                source.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(EasySuiteLab.CarryWalkingLagAllKey).Id);
                source.fallback = "Carry walking lag: all axes";
                label.gameObject.AddComponent<EasySuiteLabel>();
                changed = true;
            }
            else cycleRect = cycleButtons[0].transform as RectTransform;
            changed |= SetMenuRect(cycleRect, new Vector2(twoColumns ? -174 : 0, -582), new Vector2(twoColumns ? 326 : 674, 44));
            var cycleLabel = cycleRect.Find("Label") as RectTransform;
            if (!cycleLabel) throw new InvalidOperationException("Restore the Label of the carry-inertia button.");
            changed |= SetMenuRect(cycleLabel, Vector2.zero, new Vector2(twoColumns ? 308 : 656, 38), true);
            if (twoColumns)
            {
                var cycleText = cycleLabel.GetComponent<TMP_Text>();
                if (cycleText && cycleText.fontSize != 16) { cycleText.fontSize = 16; changed = true; }
            }
            if (includeFollow) changed |= ConfigureCarryFollowButton(root, collection, buttons);
            return changed;
        }

        private static bool ConfigureCarryFollowButton(GameObject root, StringTableCollection collection, EasySuiteButton[] buttons)
        {
            var matching = buttons.Where(button => button.Command == EasySuiteCommand.CycleCarryFollowSpeed).ToArray();
            if (matching.Length > 1) throw new InvalidOperationException("Remove duplicate follow-speed buttons before updating the example.");
            bool changed = false;
            RectTransform rect;
            if (matching.Length == 0)
            {
                rect = Rect(EasySuiteCommand.CycleCarryFollowSpeed.ToString(), root.transform, new Vector2(.5f, 1), new Vector2(174, -582), new Vector2(326, 44));
                var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .2f, .27f);
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.3f, 1.5f, 1.55f);
                colors.selectedColor = new Color(1.4f, 1.65f, 1.75f); colors.pressedColor = new Color(.7f, .9f, 1); button.colors = colors;
                rect.gameObject.AddComponent<EasySuiteButton>().Command = EasySuiteCommand.CycleCarryFollowSpeed;
                var label = Text("Label", rect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(308, 38), 16, TextAnchor.MiddleCenter);
                var source = label.gameObject.AddComponent<LocalizedText>();
                source.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(EasySuiteLab.CarryFollowAuthoredKey).Id);
                source.fallback = "Follow speed: authored";
                label.gameObject.AddComponent<EasySuiteLabel>();
                changed = true;
            }
            else rect = matching[0].transform as RectTransform;
            if (rect.gameObject.name != nameof(EasySuiteCommand.CycleCarryFollowSpeed)) { rect.gameObject.name = nameof(EasySuiteCommand.CycleCarryFollowSpeed); changed = true; }
            var translated = rect.Find("Label")?.GetComponent<LocalizedText>();
            if (!translated) throw new InvalidOperationException("Restore the localized Label of the follow-speed button.");
            long entryId = collection.SharedData.GetEntry(EasySuiteLab.CarryFollowAuthoredKey).Id;
            if (translated.text.TableReference.TableCollectionNameGuid != collection.SharedData.TableCollectionNameGuid || translated.text.TableEntryReference.KeyId != entryId || translated.fallback != "Follow speed: authored")
            {
                translated.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, entryId);
                translated.fallback = "Follow speed: authored";
                changed = true;
            }
            var previewText = translated.GetComponent<TMP_Text>();
            if (previewText && previewText.text != translated.fallback) { previewText.text = translated.fallback; changed = true; }
            changed |= SetMenuRect(rect, new Vector2(174, -582), new Vector2(326, 44));
            var text = rect.Find("Label") as RectTransform;
            if (!text) throw new InvalidOperationException("Restore the Label of the follow-speed button.");
            changed |= SetMenuRect(text, Vector2.zero, new Vector2(308, 38), true);
            return changed;
        }

        private static void ValidateFollowDefault(GameObject root, StringTableCollection collection)
        {
            var button = root.GetComponentsInChildren<EasySuiteButton>(true).SingleOrDefault(item => item.Command == EasySuiteCommand.CycleCarryFollowSpeed);
            var label = button ? button.transform.Find("Label")?.GetComponent<LocalizedText>() : null;
            var entry = collection ? collection.SharedData.GetEntry(EasySuiteLab.CarryFollowAuthoredKey) : null;
            var preview = label ? label.GetComponent<TMP_Text>() : null;
            if (!label || entry == null || label.text.TableReference.TableCollectionNameGuid != collection.SharedData.TableCollectionNameGuid || label.text.TableEntryReference.KeyId != entry.Id ||
                label.fallback != "Follow speed: authored" || (preview && preview.text != label.fallback))
                throw new InvalidOperationException("The saved Follow Speed button must retain its identity and reference the authored follow translation.");
        }

        private static bool SetMenuRect(RectTransform rect, Vector2 position, Vector2 size, bool centered = false)
        {
            if (!rect) throw new InvalidOperationException("Restore the Lab Menu RectTransforms before updating their layout.");
            Vector2 anchor = new Vector2(.5f, centered ? .5f : 1);
            if (rect.anchoredPosition == position && rect.sizeDelta == size && rect.anchorMin == anchor && rect.anchorMax == anchor && rect.pivot == new Vector2(.5f, .5f)) return false;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return true;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var value = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); value.SetParent(parent, false);
            value.anchorMin = value.anchorMax = anchor; value.pivot = new Vector2(.5f, .5f); value.anchoredPosition = position; value.sizeDelta = size; return value;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int pointSize, TextAnchor alignment)
        {
            var rect = Rect(name, parent, anchor, position, size);
            var text = UIText.Add(rect.gameObject, string.Empty, pointSize, FrameworkPlaygroundTypography.Alignment(alignment));
            text.color = new Color(.9f, .94f, .98f);
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void Localize(GameObject target, string key, string fallback)
        {
            var entry = strings.SharedData.GetEntry(key);
            if (entry == null) throw new InvalidOperationException("Missing authored localization key: " + key);
            var source = target.AddComponent<LocalizedText>(); source.text = new LocalizedString(strings.SharedData.TableCollectionNameGuid, entry.Id); source.fallback = fallback;
            target.AddComponent<EasySuiteLabel>();
        }

        private static LocalizedCatalog CreateTranslations(ResourceDefinition wood, ResourceDefinition energy, BuildingDefinition[] buildings)
        {
            var mapping = LocalizationSetup.Create(Content + "/Localization", "en", new[] { "ru" }, CollectionName);
            strings = mapping.collection;
            RenameCarryTableKeys(strings);
            var rows = new List<(string key, string en, string ru)>();
            void Add(string key, string en, string ru) => rows.Add((key, en, ru));
            rows.AddRange(CarryInertiaTranslations());
            rows.AddRange(CarryControlsTranslations());
            Add(CarryPoseKey, CarryPoseEnglish, CarryPoseRussian);
            Add("lab.title", "EASY GAME FRAMEWORK  /  PLAYGROUND", "EASY GAME FRAMEWORK  /  ЛАБОРАТОРИЯ");
            Add("lab.ready", "Explore the lab. Tab opens the menu; E interacts; T opens building.", "Исследуйте стенд. Tab — меню; E — действие; T — строительство.");
            Add("lab.hp", "HEALTH", "ЗДОРОВЬЕ"); Add("lab.wood", "WOOD", "ДЕРЕВО"); Add("lab.energy", "ENERGY", "ЭНЕРГИЯ");
            Add("lab.target", "TARGET", "МИШЕНЬ"); Add("lab.built", "BUILT", "ПОСТРОЕНО");
            Add("lab.hint", "E · interact   H · actions   T · build   Tab · menu   F5 / F9 · save / load", "E · действие   H · список   T · стройка   Tab · меню   F5 / F9 · сохранить / загрузить");
            Add("lab.buildhelp", "Aim at the pad. Click to place; R/Q rotate; Escape exits.", "Наведитесь на площадку. Щелчок — поставить; R/Q — поворот; Esc — выход.");
            Add("lab.saved", "Saved locally. Move, build or take damage, then press F9.", "Сохранено. Передвиньтесь, постройте или получите урон, затем нажмите F9.");
            Add("lab.loaded", "State restored. Balances and rewards were not duplicated.", "Состояние восстановлено. Ресурсы и награды не дублировались.");
            Add("lab.nosave", "Press F5 or Save before loading.", "Сначала нажмите F5 или «Сохранить».");
            Add("lab.added", "Added 20 wood and 20 energy.", "Добавлено 20 дерева и 20 энергии.");
            Add("lab.dead", "Player defeated. F9 loads your save; Tab opens the lab menu.", "Игрок побеждён. F9 — загрузить; Tab — открыть меню стенда.");
            Add("lab.nobuilding", "Place a building first.", "Сначала разместите постройку.");
            Add("lab.repaired", "Repaired up to 10 HP using wood.", "Восстановлено до 10 HP за дерево.");
            Add("lab.workdone", "Calibration complete. Movement control returned to the player.", "Калибровка завершена. Управление движением возвращено игроку.");
            Add("lab.paidcast", "Casting: energy is charged from the shared wallet at release.", "Подготовка: энергия спишется из общего кошелька при выполнении.");
            Add("sign.title", "EASY SUITE\nINTERACTIVE LAB", "EASY SUITE\nЛАБОРАТОРИЯ МЕХАНИК");
            Add("sign.build", "02  /  BUILD\nT · choose  /  click · place", "02  /  СТРОИТЕЛЬСТВО\nT · выбрать  /  щелчок · поставить");
            Add("sign.carry", "01  /  INTERACT\nE · carry  /  R · rotate", "01  /  ВЗАИМОДЕЙСТВИЕ\nE · взять  /  R · вращать");
            Add("sign.work", "WORKBENCH\nH · actions  /  E · calibrate", "ВЕРСТАК\nH · действия  /  E · калибровка");
            Add("sign.combat", "03  /  COMBAT\nClick · fireball + burn", "03  /  БОЙ\nЩелчок · огненный шар + ожог");
            Add("sign.slow", "SLOW FIELD\nMove through the blue zone", "ЗОНА ЗАМЕДЛЕНИЯ\nПройдите через синюю область");
            Add("menu.title", "LAB CONTROL", "УПРАВЛЕНИЕ СТЕНДОМ");
            Add("menu.subtitle", "Experiment, save, restore. Each window owns its input request.", "Экспериментируйте, сохраняйте и восстанавливайте состояние.");
            Add("menu.Controls", "Controls / audio", "Управление / звук"); Add("menu.Language", "Change language", "Сменить язык");
            Add("menu.Buildings", "Building catalog", "Каталог построек"); Add("menu.InteractionActions", "Interaction actions", "Действия с объектом");
            Add("menu.Save", "Save  ·  F5", "Сохранить  ·  F5"); Add("menu.Load", "Load  ·  F9", "Загрузить  ·  F9");
            Add("menu.SavePreferences", "Save preferences", "Сохранить настройки");
            Add("menu.LoadPreferences", "Load preferences", "Загрузить настройки");
            Add("menu.AddResources", "+20 wood / energy", "+20 дерева / энергии"); Add("menu.PaidAbility", "Energy strike  ·  2.50", "Удар энергией  ·  2.50");
            Add("menu.DamagePlayer", "Damage player  ·  25", "Урон игроку  ·  25"); Add("menu.Repair", "Repair nearest building", "Починить ближнюю постройку");
            Add("menu.KillPlayer", "Defeat player", "Победить игрока"); Add("menu.RevivePlayer", "Revive player", "Возродить игрока");
            Add("menu.ResetTarget", "Reset combat target", "Восстановить мишень"); Add("menu.Close", "Return to lab", "Вернуться на стенд");
            Add("action.work", "Calibrate workbench", "Калибровать верстак"); Add("action.terminal", "Open lab terminal", "Открыть терминал");
            Add("action.catalog", "Open building catalog", "Открыть каталог построек"); Add("action.takewood", "Take 4 wood", "Взять 4 дерева");
            Add("action.takeenergy", "Take 10 energy", "Взять 10 энергии");
            Add("Grab", "Grab", "Взять"); Add("Release", "Release", "Отпустить"); Add("Workbench", "Workbench", "Верстак"); Add("Carry crate", "Carry crate", "Переносимый ящик");
            Add("interaction.resources", "Not enough resources.", "Недостаточно ресурсов."); Add("interaction.full", "Container cannot accept this resource.", "Контейнер не принимает ресурс.");
            Add("interaction.outOfRange", "Move closer.", "Подойдите ближе."); Add("interaction.tooHeavy", "Too heavy.", "Слишком тяжело.");
            Add("ui.interactions", "Actions", "Действия"); Add("ui.toggle", "Toggle", "Нажать повторно"); Add("ui.hold", "Hold", "Удерживать");
            Add("ui.buildings", "Buildings", "Постройки"); Add("ui.building.choose", "Choose a building", "Выберите постройку");
            Add("ui.building.free", "Free", "Бесплатно"); Add("ui.building.baseCosts", "Base cost (available / required): ", "Стоимость (доступно / нужно): ");
            Add("resource." + wood.Id, "Wood", "Дерево"); Add("resource." + energy.Id, "Energy", "Энергия");
            var buildingNames = new[] { "Барьер", "Колонна", "Блок" };
            for (int i = 0; i < buildings.Length; i++) Add("building." + buildings[i].Id, buildings[i].Name, buildingNames[i]);
            Add("language.en", "English", "Английский"); Add("language.ru", "Russian", "Русский");
            Add("ui.settings.title", "Controls", "Управление"); Add("ui.settings.language", "Language", "Язык");
            Add("ui.settings.mouseSensitivity", "Mouse sensitivity  {0:0.00}", "Мышь  {0:0.00}");
            Add("ui.settings.gamepadSensitivity", "Gamepad sensitivity  {0:0.00}", "Геймпад  {0:0.00}");
            Add("ui.settings.deadzone", "Stick deadzone", "Мёртвая зона стика"); Add("ui.settings.invertY", "Invert Y: {0}", "Инверсия Y: {0}");
            Add("ui.settings.devices", "Devices: {0}", "Ввод: {0}"); Add("ui.settings.device.Automatic", "Automatic", "Автоматически");
            Add("ui.settings.device.KeyboardMouse", "Keyboard / mouse", "Клавиатура / мышь"); Add("ui.settings.device.Gamepad", "Gamepad", "Геймпад");
            Add("ui.settings.on", "On", "Вкл."); Add("ui.settings.off", "Off", "Выкл."); Add("ui.settings.capture", "Capture", "Запомнить");
            Add("ui.settings.restore", "Restore", "Вернуть"); Add("ui.settings.defaults", "Defaults", "По умолчанию"); Add("ui.settings.back", "Back", "Назад"); Add("ui.settings.reset", "Reset", "Сброс");
            Add("ui.settings.status.help", "Select a binding. Esc / B cancels rebinding before closing this window.", "Выберите кнопку. Esc / B сначала отменяет переназначение, затем закрывает окно.");
            Add("ui.settings.status.captured", "Settings captured in memory.", "Настройки запомнены."); Add("ui.settings.status.captureFirst", "Capture settings first.", "Сначала запомните настройки.");
            Add("ui.settings.status.restored", "Settings restored.", "Настройки восстановлены."); Add("ui.settings.status.connectGamepad", "Connect a gamepad first.", "Сначала подключите геймпад.");
            Add("ui.settings.audio.title", "Audio & vibration", "Звук и вибрация"); Add("ui.settings.audio.master", "Master volume", "Общая громкость");
            Add("ui.settings.audio.music", "Music", "Музыка"); Add("ui.settings.audio.sfx", "Sound effects", "Звуковые эффекты");
            Add("ui.settings.audio.hapticsIntensity", "Vibration strength", "Сила вибрации"); Add("ui.settings.audio.haptics", "Vibration", "Вибрация");
            var actions = new[] { ("Gameplay/Interact", "Interact", "Взаимодействие"), ("Gameplay/Jump", "Jump", "Прыжок"), ("Gameplay/Move", "Move", "Движение"),
                ("Gameplay/Fire", "Fire", "Огонь"), ("Gameplay/Look", "Look", "Взгляд"), ("Gameplay/Sprint", "Sprint", "Бег"), ("Gameplay/Crouch", "Crouch", "Присесть"),
                ("Gameplay/ThrowItem", "Throw item", "Бросить предмет"), ("Gameplay/RotateItem", "Rotate item", "Вращать предмет"),
                ("Suite/Menu", "Lab menu", "Меню стенда"), ("Suite/Build", "Building catalog", "Каталог построек"), ("Suite/InteractionMenu", "Action menu", "Список действий"),
                ("Suite/Save", "Save", "Сохранить"), ("Suite/Load", "Load", "Загрузить") };
            foreach (var action in actions) Add("ui.settings.action." + action.Item1, action.Item2, action.Item3);
            Add("ui.settings.part.up", "forward", "вперёд"); Add("ui.settings.part.down", "back", "назад");
            Add("ui.settings.part.left", "left", "влево"); Add("ui.settings.part.right", "right", "вправо");

            var english = (StringTable)strings.GetTable(new LocaleIdentifier("en"));
            var russian = (StringTable)strings.GetTable(new LocaleIdentifier("ru"));
            foreach (var row in rows)
            {
                // Preserve any existing authored translation if generation is resumed after an interrupted first run.
                if (english.GetEntry(row.key) == null) english.AddEntry(row.key, row.en);
                if (russian.GetEntry(row.key) == null) russian.AddEntry(row.key, row.ru);
            }
            EditorUtility.SetDirty(english); EditorUtility.SetDirty(russian); EditorUtility.SetDirty(strings.SharedData);
            var translations = rows.Select(row => new CatalogTranslation { id = row.key,
                text = new LocalizedString(strings.SharedData.TableCollectionNameGuid, strings.SharedData.GetEntry(row.key).Id) }).ToArray();
            var catalog = Definition<LocalizedCatalog>("Localized Lab Catalog", value => value.entries = translations);
            AssetDatabase.SaveAssets(); return catalog;
        }
    }
}
