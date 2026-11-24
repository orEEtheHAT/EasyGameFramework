# EasyNarrative

Author quests, branching conversations and shared world facts in one module. A quest tracks progress for a save slot. A conversation presents lines and commits a consequence when the player confirms a reply. The journal supports concurrent orders, quest focus and scoped knowledge. Several players can read the same dialogue asset with independent cursors.

## Narrative Studio

Open **Tools → EasyGameFramework → Narrative → Narrative Studio**. The library indexes character profiles, inline speakers, player replies, quest definitions, dialogues and world fact databases. In **Library → World Facts**, search by a fact’s name or permanent ID to open its entry. Select a database row to manage its full list. Opening a graph asset in Project uses the same window. Choose **Graph** to edit nodes, **Settings** for the asset and its world facts, or **Connections** to navigate between participants, their lines, quest references and scene bindings. **Back** returns to the previous document or library. **Create** makes a quest, dialogue, speaker profile or fact database. Creating a dialogue from a speaker profile assigns that speaker to its greeting. Use **Save** to save the selected asset. Scene bindings belong to their scene.

**Library → Presets** groups dialogue checks, conditions, effects, quest rewards and icon libraries. Enable **Include rewards unused by quests** for other reward profiles. **Where used** opens the exact graph step or reply. For an icon library choose an icon to see its objective overrides and inherited defaults. For a world fact, the same tab shows readers and writers in graphs and loaded scenes. Scripted scene interactions can implement `INarrativeFactSource` to declare their keys. Each quest has a **Tests** tab for editing and running its scenarios in an isolated session.

## Author and verify a quest

Open a Quest Graph, select a step and choose **Inspect step**. In Play Mode it shows wallet totals, outstanding hand-ins, the current route and a bounded event history that you can start recording. Select a scene object and use **GameObject → EasyGameFramework → Narrative → Find quests using object** to jump back to its graph. Scene checks cover loaded objects. Code and other scenes may supply additional signals.

Use **Use as template** to create a quest with its own identity. Shift-select a branch to duplicate it with its internal links. The graph toolbar searches and frames nodes. **Tools → EasyGameFramework → Narrative → Dependencies, saves and benchmarks** checks serialized references, validates a saved slot against a catalog, replays test scenarios and measures signal dispatch.

## Make an NPC speak

1. Create **EasyGameFramework → Narrative → Dialogue**, open its graph and choose **Conversation template**. Edit the speaker, text and replies. Use the output port or destination picker to link lines. An empty destination ends the conversation. A line with no available replies offers Continue. An explicit reply can return to a previous topic.
2. Put **Dialogue Presenter** on the player with the Interaction Actor (or a parent). Assign the player's **Window Manager**. Add **Talk Action** to an interactive NPC and assign the Dialogue. The presenter creates a reusable TMP window on first use. EasyUI owns navigation, cursor and Back. A standalone conversation works without a quest or world.
3. For story logic, assign **Quest World** on the presenter. Conditions can check a fact, a quest's status or its current step. A reply can set a fact, start a quest, or send a gameplay event. Use the world's database on dialogues that reference facts. For localized writing assign the player's **Localized Catalog Cache** and fill the optional text keys.
4. The cursor belongs to the open conversation. Closing it, leaving through Back or restoring a save ends the conversation. Confirmed consequences live in the existing quest/world save. The interaction can optionally resume the interrupted line when the player returns to the same speaker. Reopening evaluates choices against the current world.

```csharp
presenter.Open(greeting);
world.Session.SetFlag("bridge.repaired", true, "Bridge interaction");
```

For a custom UI, construct a `DialogueRun`, show `Current` and `Replies`, subscribe to `Changed`, and pass the displayed reply ID and revision to `Choose`. Dispose the run when closing. Validation and node lookup are cached per dialogue asset; runtime edits to a definition require `Invalidate()` before the next conversation.

[Quest graphs, saves and screen markers](Quests/README.md) · [Dialogue graph, costs, skills, voices and interruptions](Dialogue/README.md) · [Repeatable orders and shared knowledge](Quests/Documentation~/RepeatableOrders.md)

**Playground.** Talk to the ferryman at the left crossing. Ask about him, revisit the topic, or take the order before visiting the board. With the delivery at its crossing step, selecting the ferry advances the existing quest. Repairing the bridge opens a different response. Switch English/Russian in the Tab menu.

**Dialogue and marker effects.** A new spoken line uses the reusable EasyUI Text Reveal effect. Set Reveal Duration on the player's Dialogue Presenter (0–3 seconds, default 0.85); click the line to finish it or use `ShowFullLine()`. Replies are selectable throughout. Changing language refreshes the current line immediately.

**Wayfinder** draws the tracked target as a diamond, the off-screen direction as a rotating chevron, and the search area as a compact icon at its approximate centre. The Quest Marker HUD owns Symbol Size, Target Tint, Search Tint, Pulse and the optional shared Wayfinder material. Call `RefreshAppearance()` after changing these fields from game code. Per-player reading preferences scale the badge, stop motion and give it a high-contrast silhouette. Search areas still use the designer's region centre. Captions are hidden by default. Quest Marker HUD → Show Quest Tracker shows the tracked quest and stage in the upper-left corner. With Quest Journal Shortcut installed, J toggles the journal.

**Preview conversation.** Open the dialogue graph and click Preview conversation. Pick a reply, inspect a disabled reply's requirements, or expand Test world to set facts, start a referenced quest and send objective events. Replay with current facts keeps the simulated progress; Reset preview rereads the authored graph and recreates its world. Preview uses the authored fallback text or its key. Scene objects, player preferences and saves are untouched.
