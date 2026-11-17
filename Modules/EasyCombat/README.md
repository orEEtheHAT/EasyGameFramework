# Combat — EasyGameFramework

Combat is the health, damage, defense, status and ability module of **EasyGameFramework 0.1.0**. SOs describe reusable recipes; actors own HP, stacks, timers and modifier handles. Never modify shared definitions to implement temporary gameplay changes.

## First use

1. Install the EasyGameFramework package and its standard dependencies. ECM2 is supplied separately by its licensed owner.
2. Open **Tools > EasyGameFramework > Combat > Combat Studio**. SOs are also available under **Assets > Create > EasyGameFramework > Combat**.
3. For a ready firing range, import **Combat Attack Gallery** from EasyGameFramework's Samples, then use **Tools > EasyGameFramework > Combat > Samples > Open Attack Gallery**. Press Play, select **Pistol Shot**, then **Attack**: the front target loses HP. Fireball adds an explosion, a persistent field and Burn.
4. For a custom target, add `Health` and a Collider. A Health Profile is optional; the default is 100 HP. Status support and health feedback are enabled in Health by default; no extra components are required. Add Hurtboxes only for distinct contact zones.
5. Create a Weapon SO, assign it to `DamageEmitter.Attack`, then call `TryFire` from your Input System action or AI. Hitscan needs no projectile pool. Projectile attacks need `ProjectilePool`; impact fields also need a world-owned `DamageAreaPool`.
6. Movement, Resources and Feedbacks adapters ship with the framework and need no sample import. See the integration section below.

```csharp
// emitter: DamageEmitter; muzzle: Transform. Attack is configured in the Inspector.
bool fired = emitter.TryFire(muzzle.position, muzzle.forward);
```

```csharp
// target: Health; attacker: GameObject. Health applies defenses, hit rules and resistance.
var result = target.ApplyDamage(new DamageInfo(15, attacker));
target.Heal(5); // Healing does not resurrect a dead target.
```

```csharp
// target: Health; slow: StatusDefinition; attacker: GameObject.
target.ApplyStatus(slow, attacker);
// A motor reads MovementInfluences.Multiplier once; it already includes the timed speed stat.
```

## Health statuses and feedback

**Enable Statuses** and **Enable Health Feedbacks** are independent Health options, both on by default. A fresh Health with no active statuses has no old status collections/pools/pending buffers; empty feedback slots do no service or provider work. Turn statuses off to clear only their effects; spatial movement sources and manual control/stat modifiers remain. Set Damage/Death/Healing presets directly in Health. No StatusController or HealthFeedbacks component is needed.

Use `health.StatusCount`, `GetStatMultiplier`, `GetStatusResistanceModifier`, `StatusRestrictions` and `StatusesChanged` for UI/adapters without allocating the status engine. `health.Statuses` exposes the stable managed advanced API for stacks/combinations/manual simulation; it is not a MonoBehaviour. [Configuration, lifecycle and save semantics](Documentation~/HealthComposition.ru.md).

## Health death reward

In Health enable **Enable Death Reward** (off by default), then assign a Resource Reward profile and explicit recipient ResourceContainer. The profile selects entries from a Resource Database and gives inclusive Min/Max ranges. `health.ConfigureDeathReward(profile, wallet)` performs the same setup in code. Observe `RewardCompleted` / `LastRewardResult` for atomic admission failures. Confirmed death pays once before Died observers; loading a corpse never pays. [Setup, API and save/pooling lifecycle](Documentation~/DeathRewards.ru.md).

## Projectile execution

The framework Setup reads and explicitly saves the project choice. **Parallel is the default**; **Single Thread** uses the same flight calculation on the main thread. Opening or closing Setup does not change the choice; there is no Combat auto-popup. An explicit choice is a project Resources asset included in Player builds. The manual command is **Tools > EasyGameFramework > Combat > Projectile Settings**.

`ProjectileExecutionSettings.RequestedMode` and `EffectiveMode` distinguish the saved choice from supported execution. Unity 6.3 Web Players use Single Thread. Automatic straight projectile flight batches Burst calculations; supported 3D volleys also batch broad collision probes when automatic physics transform syncing is disabled. Exact hits, statuses and callbacks resolve in launch order on the main thread. Guided substeps and explicit `Simulate(dt)` calls remain synchronous. Small volleys can cost more to schedule; measure your workload.

## Shipped integrations

- `Integrations/EasyCombat/Movement`: `CombatFirstPersonController` extends the standard EasyMovement FPS controller (ECM2), preserving gait, camera and input behavior. The optional **Combat FPS Player** sample contains only the local prefab builder. `NavMeshSpeedAdapter` is in the Combat module. Rigidbody integration belongs to Movement (`RigidbodyCombatSpeed`).
- `MovementSurface`, shown as **Slow Volume**, takes a `MovementInfluenceDefinition` coefficient in [0..1], with one trigger collider. The receiver needs `MovementInfluences` and its movement adapter. Overlapping zones use the strongest slow; `MovementSpeedStat` multiplies the timed status value once and can provide haste. Slow Volume is not a general arbitrary Status zone; advanced area entry/stay/exit delivery uses `CombatAreaTrigger` and `AreaTriggerDefinition`.
- `Integrations/EasyCombat/Resources`: `EasyResourcesCostProvider` + `CombatResourceMap` connect abilities, beam pulses and paid blocks to one existing wallet. Configure the provider on the actor, assign Container/Mapping; no mirrored balance is created. An explicit invalid/disabled provider rejects costs. Conversion/payment objects allocate; scopes are synchronous. Irreversible partial execution is not refunded.
- `Health` routes damage/death/healing to assigned Feedback presets and an optional `IFeedbackContextProvider`. `Integrations/EasyCombat/Feedbacks/PresentationFeedbacks` handles the separate bounded presentation stream. Standalone EasyFeedbacks is maintained separately; installing it alongside the framework would duplicate assemblies.
- `CombatSaveSystem.TryCapture/TryValidate/TryRestore` expose world snapshots to Save. Preflight is nonmutating and reuses restore validation; it is not a reservation or cross-module transaction. [Restore semantics](Documentation~/Saving.ru.md).

## Authoring helpers

`EasyGameFramework.Editor.FrameworkCombatSampleFactory.Create("Assets/MyGame/Combat")` creates a local Fireball recipe, projectile/field prefabs and `MovementSpeed_Sample` key in a dedicated empty output folder. The returned `FrameworkCombatSampleAssets` contains `Fireball`, `MovementSpeed` and `Folder`. No gallery import or hidden sample references are needed. A manifest makes repeated calls return the same valid pair and preserve designer changes. An incomplete manifest or existing assets without a manifest produce a diagnostic without duplicating or overwriting data; repair those assets or choose another empty folder.

## Current foundation

- Typed multi-part damage, shared formulas, per-term caps and critical participation.
- Health, resistances, hit rules, flat armor, reduction, directional block and rechargeable shields.
- Independent status stacks with captured strength, periodic formulas and stat/resistance modifiers.
- Hitscan, swept projectiles, penetration, AOE, persistent areas and overlap arbitration.
- Bounded damage journal and Russian-language EasyCombat Studio authoring UI.
- Shipped EasyMovement FPS, EasyResources wallet and EasyFeedbacks presentation integrations.
- Actor-owned stat modifier handles, atomic SO modifier sets, and calculation breakdowns.
- Bounded resources with atomic multi-resource costs, regeneration and stat-driven capacities.
- Healing formulas, criticals, restrictions, explicit overheal and lifesteal calculation bases.
- Injectable RNG/clock for reproducible rolls, cooldowns and journal timestamps.
- SO reactions with conditions, tags, cooldowns, deferred execution and bounded chains.
- Conditional damage modifiers, type conversion, resistance penetration and selective protection bypass.
- Status lifecycle notifications, category dispels, exact stack costs, status resistance and overlapping control restrictions.
- Atomic status combinations, optional downed/revive rules, nonlethal damage and bounded kill/assist attribution.
- Temporary/decaying shields, protection charges, damage caps, paid blocks and bounded guardian redirection.
- Linked body-part durability, grouped contacts and independent buildup meters with deferred threshold actions.
- SO abilities with targeting, atomic release costs, cast cancellation, cooldowns and sequential charges.
- Swept melee windows, sustained beams and sampled area enter/stay/exit payloads.
- Optional 3D homing, ricochet, chain and returning projectile profiles; optional Physics2D hitscan/projectile/AOE adapter.
- Versioned actor-state snapshots/JSON with stable catalogs, preserved captured status strength and restore without gameplay reward replay.
- Bounded presentation events, coalescing, per-key/global limits and an optional EasyFeedbacks bridge.
- Isolated battle calculator, recipe templates, dependency/cycle diagnostics and a live state/damage explanation window.

Definitions are shared configuration. Health, resources, timers, shield charges and status instances are owned by actors. Treat definitions as immutable during combat.

Open **Tools > EasyGameFramework > Combat > Combat Studio**. Start with a Health Profile, a Weapon definition and a Damage Emitter. Assign collision geometry and a projectile pool when needed.

## Documentation

- [Detailed guide / Полное руководство](Documentation~/CombatGuide.ru.md)
- [Development roadmap](ROADMAP.md)
- [Migration from the Games Factory prototype](Documentation~/Migration.md)
- [Stats and modifiers / Характеристики](Documentation~/Stats.ru.md)
- [Resources and healing / Ресурсы и лечение](Documentation~/ResourcesHealing.ru.md)
- [Reactions / Реакции](Documentation~/Reactions.ru.md)
- [Damage modifiers / Модификаторы урона](Documentation~/DamageModifiers.ru.md)
- [Statuses and control / Статусы и контроль](Documentation~/StatusesControl.ru.md)
- [Life and attribution / Жизнь и учёт убийств](Documentation~/LifeAttribution.ru.md)
- [Advanced defense / Расширенная защита](Documentation~/AdvancedDefense.ru.md)
- [Body parts and buildup / Части тела и накопление](Documentation~/Composite.ru.md)
- [Abilities and delivery / Способности и доставка](Documentation~/AbilitiesDelivery.ru.md)
- [Projectile motion and Physics2D / Движение снарядов и 2D](Documentation~/ProjectileMotion2D.ru.md)
- [Save and restore / Снимки состояния](Documentation~/Saving.ru.md)
- [Authoring tools / Инструменты автора](Documentation~/AuthoringTools.ru.md)
- [Presentation / Звук, VFX и числа](Documentation~/Presentation.ru.md)
- [API entry points](Documentation~/API.ru.md)
- [Validation and limits](Documentation~/Validation.md)
- [Changelog](CHANGELOG.md)

## Package boundaries

`Runtime/` contains gameplay and opt-in presentation formatting. `Editor/` contains inspectors and Studio. `Tests/` contains regression and performance checks. `Samples~/` contains optional examples; production adapters live in the package-level `Integrations/EasyCombat/`; third-party implementations are not redistributed.

The pre-existing Games Factory sandbox stays in the development project, outside this package. It is an integration fixture rather than a dependency.

## License

MIT, matching Easy Feedbacks. Copyright 2026 orEEtheHAT. See [LICENSE.md](LICENSE.md).

## Examples and tests

Samples: **Combat Attack Gallery**, **Combat Basic Arena**, **Combat Lab**, **Combat FPS Player**. Each is independent; production adapters are already installed. Keep one imported copy of each sample assembly. Basic Arena and Combat Lab generate new editable assets through **Tools > EasyGameFramework > Combat > Samples**; FPS generation is under **Combat > Integrations**.

For tests, add only `com.oreethehat.easy-game-framework` to manifest `testables`. Run `EasyCombat` assembly filters in Edit and Play mode, including the shipped Movement/Resources/Feedbacks integration tests. Physics2D tests retain their built-in module guard. Editor/test assemblies do not enter normal Player builds.

Earlier validation reports describe the former standalone package. Framework acceptance, clean install, Player and comparative performance results belong to the coordinated unification report; earlier green results are not framework validation.
