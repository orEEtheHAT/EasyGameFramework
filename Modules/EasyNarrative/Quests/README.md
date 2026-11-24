# EasyNarrative · Quests

Create a **World Fact Database** and a **Quest Graph** from Create → EasyGameFramework → Narrative. Add a named fact in the database and keep its permanent key for calls from code. Flags use 0/1. Named states have explicit numeric codes so changing their display order leaves saves intact.

1. Assign the database on the quest. Double-click the graph, choose **Delivery template** and select a card. Click the **›** port on a card and then its destination, or choose **Next** in the inspector. A branch offers its conditions and default route. Drag a card to move it and use the middle mouse button to pan. **Validate** checks missing links, unreachable steps and cycles. The first version supports finite graphs.
2. On an **Objectives** step, use **World State** for something that may already be true, or **New Events** for actions after entry. **All** waits for every required goal, **Any** finishes on the first. Optional goals never block progress. A branch takes the first matching route, otherwise its default Next.
3. Add **Quest World** to a scene, assign the database and graphs. Use the API below or connect a Quest Event Binding (successful interaction, death, resources received) or Quest Zone. Configure an automatic start condition on the graph to make it available from a world fact.
4. Add **Quest Target** to a known location or NPC and assign a unique ID and world. Choose the target on its goal. A search area marks its independently authored approximate centre with a single compact icon. Add **Quest Marker HUD** to a stretched RectTransform on the player's Canvas and assign the camera. **Quest Journal** uses a TMP label and an optional localized catalog. Hidden quests and goals keep their information off the HUD.
5. Register `QuestSave.Create(world)` with EasySave alongside the wallet participant. Assign the player wallet on Quest World. On the End card choose Automatic or At Interaction and a Resource Reward Profile; for the latter, click Bind reward recipient. Existing externally managed Quest Reward components use the legacy Grant Reward checkbox. A refused payment remains claimable. Restoring a save reinstates state without replaying events.
6. Open the graph during Play Mode to see the active card and visited path. Select an objective to inspect progress and the source of a fact change.

```csharp
world.Session.SetFlag("bridge.repaired", true, "Repair interaction");
world.StartQuest(delivery);
world.Signal("parcel.delivered");
```

**Framework Playground.** Interact with the order board left of the entrance. Take the physical carry crate. Repair the bridge or use the ferry, bring the crate to the recipient and interact to collect five coins. Drop the crate near the station before interacting if the grab action owns your input. The board also opens the journal. F5/F9 save/load the lab. The English/Russian switch is in the Tab menu.

## React to the story in a scene

- Add **World State Reaction** to an object that stays active and assign its **Quest World**. Select a fact, active objective step, or completed quest. Drag the child objects into **Show When True** and **Show When False**. Additional actions accepts UnityEvents when you need other responses. Both enabling the listener and loading a slot apply the current state. Subsequent gameplay applies only changes of truth value.
- Add **Quest Event Reaction** for one-shot responses. Select the quest, then **Quest Started**, **Step Entered**, **Step Completed** or **Quest Completed**. Select the graph card for step events and connect **On Triggered** to a feedback emitter, animator or other scene callback. The listener receives new transitions while enabled. Restoring a slot reconstructs persistent state without replaying transitions.
- In code, subscribe to `world.Session.Transitioned`. Each notification carries `Kind`, `Quest` and the exact `Step` in traversal order, including cards crossed in a single action. Entering an End card emits Step Entered followed by Quest Completed. Step Completed is emitted when advancing past a card. Use the event payload to identify a transition; the session already holds the settled world.

## First quest from a scene object

Select a scene object and use GameObject → EasyGameFramework → Narrative → Create interaction quest. Choose a filename. The command creates a three-card graph, registers it with the scene world and connects the object, collider, interaction and marker. Repeating the command opens the existing graph. On a scene with several worlds, first assign the intended world on the object's Quest Target. In Play Mode interact with the object using an EasyInteraction player, or click Test interaction in its Quest Interaction Action inspector. This first interaction starts and completes the sample quest. To see its active marker before interacting, start the quest earlier from a dialogue or gameplay. Undo reverts scene wiring; the authored graph and database remain as assets.

Use the Quest event and Marker target selectors on goals, interaction bindings, zones, targets and dialogue reply events. Search and pick a key, or type a name and choose Create. Suggestions come from graphs and the currently loaded scene objects with the same world database. Game code can still send string keys.

## Optional quest-wide rules

In Quest Graph, click **Add quest rule**, choose a world fact, a comparison and **Go to**. Click the rule above the graph to edit it or see whether it fired in Play Mode. Each rule is checked at quest start and when its fact changes during the quest. If multiple eligible rules match the same evaluation, their displayed order wins. A rule can enter a separate branch and rejoin the normal graph later. It skips destinations whose route overlaps the visited path, so it cannot rewind or pay twice. An unfinished objective that loses its route emits **Step Interrupted**. Saving and loading preserves the taken route without replaying events. A quest with no rules uses its ordinary graph.

## Marker icons and screen edges

Create → EasyGameFramework → Narrative → Quest Icon Library. Add a named entry, assign a Sprite (2D and UI) and optionally an upward-facing Edge Sprite. On the quest asset select the shared Icon Library and Default icon. An individual objective can override it in Quest Graph → Objectives → Icon. Renaming an entry preserves its references. Missing entries fall back to the quest default, then to Wayfinder.

Quest Marker HUD → Edge Padding sets the margin in Canvas units. Enable Override Edge Padding on an entry to give it its own margin. Zero places its rectangular bounds flush against the HUD/camera viewport. Transparent borders in the sprite add visible space. The label moves inward independently. With no edge sprite the category icon stays upright off screen. An assigned edge sprite rotates towards the target. Wayfinder supplies the high-contrast silhouette and fallback for absent sprites. Search areas use the same compact icon at their approximate centre.

## Captions, edge behaviour and grouped markers

In **Quest Graph → Objectives** select **Mark → All targets in group**. Pick the same **Marker group** on the objective and on every scene or prefab **Quest Target**. Group-only objects can leave the unique ID empty. Each active component gets its own pooled marker for the tracked quest. Disabling or despawning an anchor removes that marker. Use QuestTarget.Configure to bind a spawned object or change its group. Gameplay sends the objective's progress signal separately.

In the icon library choose **Caption** (always, on screen only, off screen only, hidden) and **Outside view** (pin or hide). Objectives inherit these choices, or override them under Label Mode and Outside view. Label Max Distance hides only the caption beyond a camera distance in world units; zero removes the distance limit. Existing objectives keep their previous edge setting and their captions by default.

Assign **Soft Pulse**, **Shimmer** or a custom sprite material to the library entry's **Sprite Material**. The shared presets live under Modules/EasyUI/Runtime/IconEffects and can also be dropped onto a uGUI Image or SpriteRenderer. Create a material with shader EasyGameFramework/UI/Icon Finish to combine pulse and shimmer, change their speed, strength and tint. The quest HUD uses plain sprites with reduced motion and the Wayfinder silhouette in high contrast.

**Quest HUD.** Marker captions are hidden by default. For an individual icon or goal, explicitly select the desired caption mode. Quest Marker HUD → Show Quest Tracker is on by default and displays the tracked quest title, current stage and a counter for single counted goals. Tracker Inset positions it relative to the top-left; the stage's optional Name Key localizes its title. Finished, hidden and untracked quests have no tracker.

**Journal shortcut.** Add Quest Journal Shortcut to an active player, assign Player Input Settings, Window Manager and Quest Journal. The bundled Narrative/Journal action opens and closes the journal with J. Its owned input context keeps this map available while the journal blocks movement. A custom action asset can supply a different binding. Other modal windows retain their focus.

## Author an objective without copying IDs

Select an Objectives card → **Add objective…**. Pick the scene's Quest World and the source. The wizard registers the quest, creates scene bindings with Undo and supplies a marker for a selected object. Talk counts opening the chosen NPC conversation. Interact uses the selected action's successful completion. Enter Zone uses a trigger collider and the selected player's root. Defeat takes a Health component; Defeat Group listens to Health on active Quest Targets in a named group, including spawned targets. Custom Event keeps the normal signal workflow.

Resource objectives share the **wallet** on Quest World. Quantities are entered in ordinary resource units and stored at the resource's precision. **Receive Resource** counts positive incoming amounts while the step is active. **Have Resource** checks current available stock immediately, including pre-owned stock, and leaves it in the wallet. **Give Resource** creates a hand-in interaction which deducts the full amount once, after checking the current quest and objective. Use SetWallet to switch a running world's wallet.

Each End can have its own payout profile and mode. **Automatic** settles at the end of the frame so a quest can finish inside a wallet notification. **At Interaction** becomes available on the bound NPC or object after that specific outcome. If the wallet refuses the grant, the entitlement remains pending; automatic payouts retry on wallet changes and after restoring a pending ending. The seeded roll and claimed flag are saved. Register the wallet and quest participants in the same EasySave slot.

## Built-in marker artwork

Assign **Quest Markers** from **Packages → EasyGameFramework → Modules → EasyNarrative → Quests → Marker Icons** to a quest’s Icon Library. Select **Main** for a shimmering main objective, **Side** for a side objective with a directional edge sprite, or **Enemy** for a pulsing target. These assets are installed with the package. Duplicate the library into Assets to customize it. The procedural Wayfinder is the HUD fallback.
