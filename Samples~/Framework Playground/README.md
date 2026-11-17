# Framework Playground

This sample connects the framework modules in one small playable scene. It uses original procedural feedback assets and creates editable definitions in `Assets/EasyGameFramework/Playground/Content`.

1. Install ECM2 and EasyGameFramework in a Unity 6.3 URP project with New Input System enabled.
2. Import this sample from Package Manager.
3. Choose **Tools > EasyGameFramework > Examples > Create or Update Framework Playground**.
4. Enter Play Mode. The command creates its own localization setup when needed. Repeating it applies new, versioned sample additions to the existing scene, then opens it. Save scene edits first. Current revisions preserve your customized pose and other edits; importing the package never rewrites your scene.

| Input | Action |
| --- | --- |
| WASD / mouse | Move and look |
| E | Interact; release the held object even while turning |
| G | Throw the held object |
| R + mouse / mouse wheel | Rotate the held item / adjust its distance |
| Left click | Fire the sample fireball; place a selected building in building mode |
| H | Open the available interaction actions |
| T | Open the building catalog |
| R / Q | Rotate the building preview |
| Escape | Exit the current mode / close a window |
| Tab | Open the lab menu |
| F5 / F9 | Save / load gameplay progress |

The settings menu demonstrates rebinding, audio, language and a graphics child window. Window input restrictions are owned independently. Progress loading uses EasySave and leaves user preferences separate.

New Grab Action components use Authored Pose by default, with zero Local Position and Local Euler Angles. Choose Pose Mode on each item's Grab Action; this sample intentionally demonstrates a custom offset/rotation. The held object is the same world Rigidbody: save its root with the model, colliders and Grab Action as a regular prefab. No separate in-hand display prefab is needed.

The carried crate demonstrates EGF-001: Authored Pose with Local Position (0.25, -0.25, 0) and Local Euler Angles (8, -20, 5), dynamic physical follow. Blue front/top markers make its orientation visible. Pick up, release, push it into a different world rotation, and pick up again: it returns to the same pose relative to your view. E also releases while turning; G throws. It excludes collisions with its holder. The combat target demonstrates a fireball and its ground effect. The workbench and building area share resource costs through production integrations.

To edit a hold pose visually, leave Play Mode, select **Physical Crate**, and choose **Grab Action > Edit Hold Pose in Scene View**. Assign the player's Interaction Actor to **Preview Actor** to use its view, or use the fixed Scene View snapshot (also available in Prefab Mode). **W** moves the cyan preview; **E** rotates it. Standard handles update Local Position / Local Euler Angles with Undo and normal prefab overrides. The world item stays in place. **Done** closes the preview.

The sample actor uses **Selection Mode = Focus**. **Interaction Target > Highlight When Focused** enables the outline and defaults to true; the item needs no separate outline component. The Actor's **Interaction Focus Highlighter** owns the shared cyan **Outline Color**, **Width = 0.015 metres** and explicit output **Camera Override**. Held items suppress their outline for every actor; after release, normal focused highlighting returns. This URP silhouette uses opaque static MeshRenderers, follows the normal depth buffer, and needs no project renderer-feature or source-material changes. Skinned/open/transparent meshes have separate limitations described by the highlighter.

On **Physical Crate > Grab Action**, **Follow Speed** is the main response control: 0 is soft and slow, 1 is firm and fast; the component default is 0.5. It changes position and driven rotation together through one physical spring. **Held Scale Multiplier = 0.65** temporarily scales the Rigidbody root, model and colliders to 65%; releasing or throwing restores the original size. The authored Local Position and Local Euler Angles set the hold pose.

A new Play Mode session starts with the crate's authored **Movement Lag** vector. New components and the sample default to **(0.5, 0.5, 0.5)**. Open **Tab**: the lower-left **Carry walking lag** button cycles **All axes → No forward / back → Off → All axes**. All uses the exact captured authored vector, No forward / back masks it by (1,1,0), and Off applies zero. The comparison retains custom per-axis values and restores them on the third click. This demo state is separate from saved progress and preferences.

**Movement Lag X / Y / Z** controls lateral, vertical and forward walking lag in the resolved movement reference's local axes. Zero compensates constant walking-source velocity on that axis; one retains its physical damping lag. Camera turning is not walking translation. Contacts, starts/stops and finite physics timing still affect the real dynamic Rigidbody. **Movement Reference** is an optional advanced override; the normal player setup resolves it automatically. After an explicit teleport of both actor and body, call ResetMotionTracking().

The lower-right **Follow speed** button compares **Authored → Soft (0) → Firm (1) → Authored** using the existing **GrabAction.FollowSpeed** property. It captures the exact saved authored value, changes only FollowSpeed during Play, and restores it on the third click. It does not rewrite the walking vector, hold pose or scale; a new Play session starts from the authored value. Compare walking and turning in free space, then press the crate into a wall and pull it clear. E releases even while turning; G throws.

All four resources (Wood, Energy, Coins and Crystals) are editable in one database asset: `Assets/EasyGameFramework/Playground/Content/Definitions/Resources/Resources_Sample.asset`. Select the database to search, add and edit records in one Inspector. Its native nested records retain stable resource IDs; runtime wallets use direct references. The former separate resource files are backed up, their sample references are remapped and reload-verified, then those four files are retired during the explicit sample update. Repeating an up-to-date update preserves edits.

The profile `Assets/EasyGameFramework/Playground/Content/Definitions/Rewards/EnemyRewards_Sample.asset` references this database. Its resource dropdown selects each reward record, with inclusive Min / Max quantities. The example contains **3–7 coins** and **1–3 crystals**; add more rows for additional resources. The Inspector displays amounts in the chosen resource's units.

Defeat **Combat Target** with fireballs to receive both rewards in the player's wallet; the third HUD row shows the balances. On the target's **Health**, **Enable Death Reward** is on and **Resource Reward / Recipient** reference this profile and the player's Resource Container. Health awards once per committed death; an extra death-reward component is not needed. Open **Tab > Reset combat target** for another life. Progress saving uses the whole resource database, so its catalog also covers newly added database records. Restoring a dead target never pays again; restoring an alive checkpoint rewinds its wallet and allows the subsequent real death to earn that life’s reward.

A complete reference for the Grab Action fields, mode-specific options and advanced interaction rules is [Grab Action parameters](../../Modules/EasyInteraction/Documentation~/GrabActionParameters.ru.md) in the package's module documentation. Health now also owns **Enable Statuses** and **Enable Feedbacks**, both on by default. Status storage is created only when effects need it; the HUD reads neutral Health queries and receives Health status notifications. Damage / Death / Healing presets and their context provider are configured on Health, without separate Status Controller or Health Feedbacks components. The player retains its existing three presets; the training target also has its Damage preset assigned so the example visibly demonstrates hit feedback. Turning Health feedbacks off stops that Health event stream; separate weapon-impact feedback remains a weapon event.

The opt-in `EasySuiteSmoke` component exercises the scene and reports its results. It is dormant during ordinary play. Its checks can also be invoked in a standalone diagnostic Player with `-easySuiteSmoke <absolute-report-path>`; that report is a diagnostic, not a saved game.
