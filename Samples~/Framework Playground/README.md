# Framework Playground

This sample connects the framework modules in one small playable scene. It uses original procedural feedback assets and creates editable definitions in `Assets/EasyGameFramework/Playground/Content`.

1. Install ECM2 and EasyGameFramework in a Unity 6.3 URP project with New Input System enabled.
2. Import TMP Essential Resources if your project has no TMP Settings, then import this sample from Package Manager.
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

Talk to the ferryman at the left crossing. His conversation remembers introductions, recognises a repaired bridge and offers passage when the delivery reaches its route choice. Confirm the reply to take the ferry.

The order board to the left of the entrance opens the delivery quest and journal. Pick up the carry crate, choose the bridge or ferry, then put the crate by the recipient and interact to receive five coins. The board reopens the journal; F5/F9 save and load the current route.

Open **Tab → Text** to compare TMP finishes, switch motion or high contrast, and try text at 100%, 125% and 150%. The reading settings also apply to the dialogue, quest journal and default quest markers. Save and Load Preferences include these settings.

The HUD and Lab Menu use TextMeshPro. In Play Mode open Tab, switch English/Russian and open Controls, Audio and Graphics to check the window labels and selection. The window set uses the Font on its Window Manager for generated controls.

The opt-in `EasySuiteSmoke` component exercises the scene and reports its results. It is dormant during ordinary play. Its checks can also be invoked in a standalone diagnostic Player with `-easySuiteSmoke <absolute-report-path>`; that report is a diagnostic, not a saved game.

**Marker and subtitle test.** Open Tab → Text → Replay subtitle, then compare with Motion off. Talk to the ferryman: click a spoken line to show it immediately, or select a reply while it is appearing. Accept the delivery and pick up the crate with the bridge still unrepaired to see both routes. Turn away from a tracked target to check the edge chevron; face the ferry search area to see its compact icon.

**Bridge reactions.** Take the delivery, pick up the crate, then save with F5 near the Bridge station. Repair the bridge. The red barrier gives way to a gold plank deck and the bridge route plays a confirmation effect. F9 restores the closed crossing. Save again after repairing and load: the deck stays open and the effect is silent. If you repair before accepting the quest, the deck opens immediately and the confirmation waits until the delivery takes the bridge route. Inspect Bridge → Bridge reactions (persistent condition) and Bridge → Bridge route confirmation (live graph event).

**First quest and conversation preview.** Inspect the small blue Survey post beside the order board. Reopen the journal at the board to see the completed survey. Its Quest Interaction Action was created with the same command available from GameObject → EasyGameFramework → Narrative → Create interaction quest. Open Parcel Delivery → Collect crate and click Quest event or Marker target to search existing keys. Select Bridge → Bridge reactions to see the two visibility slots. Open the Ferryman dialogue graph → Preview conversation, expand Test world and toggle Bridge repaired to compare the replies. The preview owns its own facts and progress.

**Quest-wide rule.** Take the delivery at the board. Use the coral Dispatch station beside it to send the recipient away before pickup, en route or before handover. The marker moves to the assistant and the original recipient leaves. Bring the physical crate to the blue assistant and hand it over for the usual reward. F5/F9 before and after departure compare both routes. Sending the recipient away before accepting the order also works. Open Parcel Delivery in Quest Graph and click the Recipient has left rule above the graph.

**Marker icon library.** Take the order at the board and turn away from the crate. Main uses a gold badge and zero edge padding. During route selection the ferry uses Side, with a separate turning arrow and a 32-unit margin. Open Content/Quests/Marker Icons to change the shared sprites or margins. The Enemy entry is available in the goal's Icon selector.

**Ten active markers.** At the entrance interact with the coral Patrol board (“Track ten targets”). Turn towards the two rows of training targets on the right. Each target has its own Enemy icon. Captions are hidden by default; enabling them on this goal uses its six-metre limit. Turn away and the markers disappear. Inspect a target to remove its marker and advance the journal (1/10). F5/F9 restores both the count and remaining markers. Edit Enemy in Content/Quests/Marker Icons to try pinning or a different caption mode. Main shows Shimmer on the delivery crate; Enemy uses Soft Pulse.

**Tracked quest.** Press J to open or close the journal; use Track next to switch quests. The upper-left tracker below the lab header shows the selected quest and its current stage. Disable Show Quest Tracker on Quest Markers to hide it. Rebind Journal in Tab → Controls. Turn the camera while watching a shimmering icon: the sweep stays attached to the sprite. The ferry search area is shown by a compact icon.

**Workshop order.** Visit the gold Workshop master near the entrance, accept the order and save with F5. The Combat Target drops crystals; use the existing reset target command if necessary. Hand over three crystals at the blue counter to receive five coins automatically. Load with F9, talk to the master again and choose calibration instead. Use the instrument and then the separate cash desk to claim eight coins. The calibration route leaves crystals in the wallet. Try saving before collection and loading after collection; an already collected reward stays collected when its own checkpoint is loaded.

## Narrative walkthroughs

- At the ferryman, compare the paid reply and the skill check. Switch the player language and interrupt a conversation by walking away.
- At **Accept two orders**, take the amber and blue deliveries. Announce Alice’s departure, learn the new route from a witness or a letter, and deliver the other parcel to Boris. Open **J** to find, track and pin individual orders.
- Choose **Tools → EasyGameFramework → Examples → Narrative → Inspect crystal hand-in**. **Inspect step** identifies the counter, wallet and reward. In Play Mode start recording, hand over three crystals and follow the transition.
- The two Workshop scenario assets in **Content/Editor/Narrative Scenarios** replay both rewards and their save checkpoints. The **LegacyDeliverySnapshot.json** Editor fixture can be opened in the narrative audit window against the Playground world.
- Open **Tools → EasyGameFramework → Examples → Preview Playground narrative translations** to inspect the table in context.

- During Play Mode, **Tools → EasyGameFramework → Examples → Narrative → Measure marker HUD** runs a temporary, isolated HUD with 10, 100 and 500 targets. The Console reports CPU time for both clustering settings.

In the Ferryman graph, find **River advice** and expand a reply to inspect its price, skill or pass requirement. Select **Landmark** and expand **Voices** for the English and Russian clips. In Workshop Order, select the hand-in step and expand **Screen marker** for its on-screen presentation.

Use **Tools → EasyGameFramework → Examples → Narrative → Open Ferryman in Narrative Studio**. Open **Connections** for the cast, quest and loaded NPCs. **Library** lists characters, dialogues and quests; select a speaker and click a line to edit it in the same window.

In **Narrative Studio → Library → World Facts**, search for `bridge.repaired` to open the existing **Bridge repaired** flag, or select **World Facts** to edit the entire Playground database. In Play Mode, open and close the journal with **J** to try the searchable quest list.

In Narrative Studio, try **Presets → Ferryman Trust → Where used**, **World Facts → bridge.repaired → Where used**, and **Workshop Order → Tests**. The bridge station declares its fact write, the ferryman checks the flag, and both Workshop scenario assets can be run directly. Under **Presets → Marker Icons → Where used**, choose an icon to inspect its sprite and objective usages.

The reusable **Quest Markers** preset, its four sprites and the shared marker materials are available in the package before importing this sample. The Playground keeps its own editable **Marker Icons** library.
