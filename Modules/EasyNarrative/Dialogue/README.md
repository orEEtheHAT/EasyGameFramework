# Conversations

## Make a conversation

1. Create **EasyGameFramework → Narrative → Dialogue**. Open **Dialogue Graph**, choose **Conversation template**, then edit the greeting and its replies.
2. Drag the output port onto another line. Dropping on empty canvas creates a linked line. You can also click the port and then the destination. For a skill check, pick the reply's **Success** or **Failure** output. An empty destination ends the conversation.
3. Add **Talk Action** to an interaction target and assign the dialogue. Add **Dialogue Presenter** to the player's interaction actor and assign its Window Manager. Assign the Quest World for facts, quests or its player wallet. Assign its localized catalog cache to use translated strings.
4. Open **Preview conversation** to change test facts, grant test resources and exercise the branches. The preview uses an isolated quest session and wallet. Scene-specific conditions receive empty speaker/listener references in preview. In Play Mode, open **Active line** in the graph to locate a player's current line. **F** frames selected cards, the wheel zooms at the pointer, and the middle mouse button pans.

## Design a reply

Select a line, then expand the reply by its text in **Replies**. Open **Localization** to edit its translation key. For the spoken part, expand **Voices** on the line itself. Dialogue Presenter plays the matching clip through the active EasyFeedbacks Audio Manager using the default 2D clip settings.

- Set **Condition match** to **All**, **Any** or **None**. Each additional group has its own operator, and every group must pass. For example, a guard can require either a permit or a favourable reputation, plus a separate **None** group containing “alarm raised”. Empty groups impose no restriction. Groups have one level.
- Choose **Hidden**, **Disabled** or **Explain** for an unavailable answer. Give player-facing reasons a localized reason key. **Attempt price** can contain several resources. **Resource available** only checks a balance. Choosing a priced answer charges the complete price once. A failed skill check still consumes the price of the attempt.
- Set **On selection** and any **Success effects** to write facts, start quests, send events or apply a game-specific effect. A failed check follows **On failed check** and executes only **Failure consequences**. When all replies are unavailable, **Continue / fallback** supplies the next line.
- Assign a reusable **Speaker Profile** to share a localized name, portrait and subtitle colour. Add **Voices** to an individual spoken line. The player searches for an exact locale, then a language such as `en` for `en-US`, then an empty fallback locale. EasyFeedbacks plays the selected clip. The player advances replies manually and can skip speech. **History Limit** bounds the presenter's in-memory transcript.
- Under **Talk Action → Interruption**, choose what happens on distance, damage or loss of the speaker. **Resume** remembers the current line for this player and NPC until the save slot is restored. **Redirect** accepts a line ID. **Allow Movement** keeps gameplay input available while the window owns UI navigation.
- For short, non-modal lines attach **Ambient Dialogue Line** and bind its `Play()` to a scene event. Assign a presenter, dialogue and line ID. Set distance, cooldown and priority. A higher priority replaces an active bark, and a modal conversation takes precedence. For **Once** across saves, assign a world flag whose initial value is false.

## Extend from game code

Use `DialoguePresenter.OpenFrom(graph, npc, interruption)` for a player-facing conversation. For a custom view, construct `DialogueRun` with a `DialogueContext`, use `Displayed` for rows, `IsAvailable` and `Explain` for locked rows, and supply the revision shown on the button to `Choose(replyId, revision)`. Dispose the run when closing your view.

Derive a ScriptableObject from `DialogueCondition` for a requirement, `DialogueSkillCheck` for the roll or threshold, or `DialogueEffect` for a game-specific consequence. Keep conditions and checks free of side effects. Validate the consequence in `CanApply`. Implementations must complete `Apply` once accepted, as arbitrary external effects cannot be rolled back. Override `Preview` to simulate such an effect in the sandbox. For changes to game-specific state call `run.RefreshConditions()`; world facts, quest changes and wallet changes already refresh choices.

## Try the Ferryman

In FrameworkPlayground, ask the ferryman about the far bank. Pay two coins, ask for a favour after introducing yourself, or show the courier pass issued at the delivery board. Before visiting the board, the locked reply explains where to collect it. The pass is a saved fact. Showing it costs nothing and you keep it. All three solutions lead to the same landmark tip with English and Russian voice clips. Moving the player beyond talking range resumes the line on the next interaction. The bridge reaction can also deliver the tip as a once-per-save ambient subtitle.
