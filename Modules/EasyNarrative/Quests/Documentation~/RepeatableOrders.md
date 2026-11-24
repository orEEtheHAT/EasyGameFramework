# Repeatable orders, facts and the journal

## Build two orders from one quest graph

1. Add the graph to the Quest World's catalog.
2. Create a GameObject for each order and add **Quest Order**. Assign the same world and graph. Enter a permanent instance ID for each order, for example `market.amber` and `market.blue`.
3. In a goal's **Target**, enter `$recipient`. On each Quest Order add a role called `recipient` and set its target to that recipient's Quest Target ID. Use a group ID for a group marker. A character with no marker can also have an ID, for example `hero`.
4. Wire an interaction to **Quest Order → StartOrder()**. The next interaction can call **SendSignal(string)** to advance only that order. Starting an existing ID resumes its existing identity, bindings and progress. Generate or assign another stable ID for a new order.
5. Set a distinct display name for each order. **Title Key** supplies its translation. The graph's title is used when the order name is empty.

All instances share the read-only graph. Each instance keeps its own roles, objective counters, history and reward entitlement. In code use `session.Find(instanceId)`, `session.SignalFor(instanceId, eventName, count, origin)` and `world.TryCollect(instanceId, endingId)`. Ordinary `Signal` broadcasts to all listening orders.

## Choose the owner of a fact

Define each key once in the World Fact Database. Scope and owner belong to the read or write operation.

| Scope | Owner | Example |
| --- | --- | --- |
| World | None | The bridge is repaired for everyone. |
| Character | A fixed ID or a role such as `$recipient` | Alice is absent. Boris is at his counter. |
| Group | A fixed ID or role | Members of the guild share an agreement. |
| Quest Instance | Leave blank in the graph for the current order | Its parcel has been collected. |

For a clue, define a flag whose initial value is false. Call `session.Learn("hero", "redirect.known", "clues.letter")`. A Character condition on the same flag can unlock a branch, dialogue choice or objective. A world event and the player's knowledge can therefore occur at different times. Multiple sources can teach the same clue. The first effective write is kept as its source until the fact changes again. If the localization catalog contains the source string as a key, the journal translates it.

Use a **World State Reaction** on an active scene object to show one character and hide another. Choose **Fact**, the scope and owner. For a condition relative to an order, enter its instance ID.

## Journal and HUD

Add **Quest Journal Browser** to an existing Quest Journal with a TMP body. It uses the existing scroll area's bounds automatically. Search by the localized title, switch between all, main, side and completed quests, and select a row to inspect its history. **Track** changes the primary quest. An objective button focuses just that objective. **Pin** keeps another active quest on the HUD. There are three secondary pins. Finishing a quest removes its pin.

Enable **Cluster Markers** on Quest Marker HUD for distant targets of the same group objective. **Cluster Distance** is measured in world units, **Cluster Radius** in Canvas units. The badge counts represented targets. Separate objectives and nearby targets retain their own icons.

Add **Quest Notifications** to a Canvas element, assign its world, and optionally a TMP label, translation catalog and feedback preset. It displays the most recent visible objective or ending reached in a frame. Loading a save restores the journal without replaying notifications.

## Try FrameworkPlayground

Visit **Accept two orders** at the back of the arena. Pick up the amber parcel and announce Alice's departure. Ask the witness or read the letter and deliver the parcel to the assistant. The blue order still goes to Boris. Each successful order pays two coins once. Open the journal with **J**, search for an order and examine its history. The patrol exercise demonstrates the optional distant group markers.

The existing EasySave quest participant includes the instance ID, graph ID, bindings, local facts and their sources, goal progress, reward claims, primary objective and pins.
