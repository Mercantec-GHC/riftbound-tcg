# Rules Implementation Audit

Reference source: `rules/`, generated from `Riftbound Core Rules.pdf` last updated 2026-03-30.

Last audited against code: 2026-06-24.

This audit tracks how much of the numbered rules reference is represented in the current app. "Implemented" means there is server/engine behavior, not just UI affordance, unless explicitly noted.

## Status Legend

- **Implemented:** covered by the server-side C# rules engine or backend validation with tests or direct persistence/replay support.
- **Partial:** some behavior exists, but the rule family is incomplete.
- **Improper:** behavior exists but contradicts the reference rules.
- **Missing:** no meaningful authoritative implementation found.
- **Frontend-only/prototype:** implemented in `frontend/src/features/game/rules/gameRules.ts` or UI controls, but not authoritative for online matches.

## Properly Implemented Or Mostly Correct

### Rules Engine Authority, Persistence, And Determinism

Status: **Implemented**

- `IRulesEngine` exposes authoritative create-state, legal-action, and apply-action operations.
- `DefaultRulesEngine` validates player seating, expected sequence number, and current legal action before accepting actions.
- Initial state is serializable JSON.
- Main deck, rune deck, mulligan recycle, and Burn Out recycle use deterministic seeded randomization.
- Server online play routes actions through `IRulesEngine`; accepted actions are persisted as match events and snapshots.
- `MatchReplayService` can rebuild from the creation event and accepted event log, optionally starting from the latest snapshot, and verifies recorded post-state when present.
- Player-scoped snapshots and action responses can carry redacted views.

Rule coverage:

- `000-099`: engine authority and deterministic resolution align with the project architecture, although rule/card-text precedence is not a general system.
- `300-324`, `407-448`: server-side legality, sequence checks, and action application are partially enforced.

### Basic Setup, Zones, And Match State

Status: **Partial**

- Players get legend, champion, main deck, rune deck, hand, trash, banished, base, base gear, runes, rune pool, points, and selected battlefield state.
- Players draw 4 during initial setup.
- Separate main deck and rune deck zones exist in state.
- Battlefields exist as shared board locations with hidden-card slots.
- Champion and legend are represented as distinct slots.
- Facedown state exists, and player-view redaction hides opponent hand, deck order, and unrevealed facedown identity from non-viewers.

Rule coverage:

- `103`, `107`, `108`, `111-119`, `128`, `159-175`.

Gaps remain around the full setup/battlefield selection procedure, complete public/private information rules, top-most cards, ownership replacement, and complete champion-zone play semantics.

### Deck Construction Validation

Status: **Implemented for current online modes**

- Server deck create/update validation uses a shared Core deck-construction validator.
- Main decks must contain at least 40 cards.
- Rune decks must contain exactly 12 valid rune cards.
- Battlefield decks must contain exactly 3 valid battlefield cards with no duplicate battlefield names.
- Chosen Champion must be a champion card, cannot be Signature, and must share a champion tag with the selected Champion Legend.
- Main deck, rune deck, and battlefield cards must fit the selected Legend's domain identity.
- Main deck copy limits are enforced by card name, counting the chosen Champion.
- Signature cards are limited to 3 total and must share a champion tag with the selected Legend.
- Frontend deck-builder validation and add-card affordances mirror the authoritative server rules.
- RiftCodex import preserves signature metadata by marking imported signature cards with a `Signature` supertype.

Rule coverage:

- `103.1.b`, `103.2`, `103.2.a-b`, `103.2.d`, `103.3.a`, `103.3.a.1`, `103.4`, `103.4.c`, `133.7.a-b`.

Remaining future work:

- Deck construction is currently mode-agnostic for the supported online modes and requires three battlefield cards for all online decks. If casual modes with different battlefield counts are added, the validator should receive a mode-specific deck-construction policy.

### Mulligan

Status: **Implemented**

- Mulligan confirmations are enforced in turn order.
- Each player may choose up to two cards, redraw the same number, then returned cards are recycled to the bottom of the deck in deterministic randomized order.
- Duplicate/out-of-range card indexes are ignored.
- A player cannot confirm twice.

Rule coverage:

- `118.1-118.3`, `416`.

### Turn Skeleton

Status: **Partial**

- The engine has an ordered phase progression: awaken, beginning, channel, draw, main, ending.
- Awaken readies the turn player's exhausted runes and units.
- Beginning scores held battlefields.
- Channel adds runes from the rune deck.
- Draw draws 1.
- The first player skips their first draw in FFA3, FFA4, and 2v2; duel first player still draws.
- Ending advances turn order and clears per-turn scoring trackers.

Rule coverage:

- `301-306`, `314-317`, `315.1-315.4`, `316`, `317`, `482.7`, `483.7`, `484.7`.

Major gaps:

- Priority, open/closed state handling, cleanups, and "beginning of/ending of" triggered timing are still simplified compared with the full turn rules.

### Resource And Cost Basics

Status: **Partial**

- Card costs can be represented as energy plus domain Power and universal Power.
- Ready runes can be exhausted for energy.
- Matching rune resources can pay domain Power by being recycled.
- Universal Power can satisfy domain Power.
- Rune pool energy exists and clears after draw and at turn-end boundaries.
- Simple affordability checks prevent playing cards without enough resources.
- Deflect adds an extra targeting cost to enemy spells.

Rule coverage:

- `131`, `161-166`, `356-357`, `429-430`, `801`.

Major gaps:

- Rune activated/add abilities are not modeled as a full payment-time action system.
- Cost replacement, cost increases/discounts, optional/mandatory additional costs beyond currently modeled keyword cases, and non-standard costs are incomplete.

### Basic Card Play, Targets, And Effects

Status: **Partial**

- Units can be played from hand to base or to a controlled battlefield.
- Champion cards in hand can be played as units, and the champion-zone card can be summoned once from the champion slot when affordable.
- Units enter exhausted.
- Gear can be played and resolves to the owner's base.
- Spells/gear/effects can create stack items.
- Simple effect model supports `draw`, `damage`, `buff`, and `rally`.
- Target legality checks distinguish friendly, enemy, lane, optional/up-to, and still-legal-on-resolution targets.
- Effects do not follow objects that leave and return through a non-board zone.
- Lane-targeted effects can partially resolve remaining legal targets.

Rule coverage:

- `141-158`, `349-359`, `413`, `417`, `419`.

Major gaps:

- Card text is not parsed into a general instruction tree.
- Modes, linked instructions, full non-public target rules, "do as much as possible" as a general rule, prevention/replacement hooks for all actions, and most card categories are incomplete.

#### Card Effect Coverage Audit (2026-06-24)

A full pass against the live `cards` table (1,048 rows) was done to scope what it would take to wire real effects for every card from its `Text` field:

- 1,022 of 1,048 cards have non-trivial rules text; the rest are vanilla (mostly runes).
- Before this pass, every card defaulted to `EffectType = "rally"`, `EffectAmount = 0` regardless of its text — i.e. no card had a real effect wired, despite the simple single-instruction model (`Damage`/`Draw`/`Buff`/`Rally`) existing.
- Card text is overwhelmingly bespoke: even after stripping keyword reminder text (e.g. `[Action] (...)`), only ~12 of 1,048 cards reduce to an exact, mechanically-identical instruction (`"Draw N."`, `"Deal N to a unit."`, `"Kill a unit."`, `"Give a unit +N Might this turn."`, and a two-step `"Give a unit +N Might this turn. Draw 1."`). The remaining ~1,010 cards require individually reading and judging each card's wording — this is fundamentally a large hand-authoring effort, not a pattern-matching/scripting one.

What was added this pass:

- `CardEffectStep` and `CardEffectDefinition.Steps` (`riftbound-tcg.Core/Cards/CardContracts.cs`) support an ordered multi-instruction sequence (e.g. "Deal 4 to a unit. Draw 1." → two steps), in addition to the legacy single `Type`/`Amount`.
- Three new effect types: `Kill` (to trash, runs Deathknell), `Banish` (removed from the game), `Stun` (exhausts the unit).
- `DefaultRulesEngine.ResolveTopStackItem` executes `effect.steps` in order when present, with each target-requiring step consuming its own target from the chosen target list; falls back to the original single-effect path otherwise (existing lane-wide/legacy cards are unaffected and still covered by tests).
- `ValidateTargetSelection` now recognizes `kill`/`banish`/`stun`, and derives a multi-step card's target requirement from its first target-requiring step.
- New `cards.EffectsJson` column (jsonb, default `[]`) stores the step list; `CardEntity`/`CardDto`/`ToCardDefinition` round-trip it. See `riftbound-tcg.Server/Api/Data/Migrations/002_card_effect_steps.sql`.
- The 12 cards matching an exact, unambiguous pattern were authored and verified against the live DB (`Final Spark` x2, `Consult the Past` x2, `Premonition`, `Progress Day`, `Discipline` x2 multi-step, `Punch First`, `Primal Strength`, `Vengeance` x2).
- Tests: `EffectResolverTests` (Kill/Banish/Stun/multi-step) and `DefaultRulesEngineTests` (kill/banish/stun/multi-step end-to-end through play-card + chain resolution).
- Found and fixed a target-legality bug while authoring: `buff`-type effects ("Give a unit +/-N Might this turn") were restricted to friendly units only, but most of these cards' text has no friendly/enemy qualifier (unlike `rally`/"ready", which is always self-targeting). `UnitCanBeTargetedByEffect` now allows `buff` to target any unit.

#### Spell Category Pass (2026-06-24, session 2)

Went through all 202 Spell rows (192 distinct names) by hand, read every card's text, and classified each as either mechanically authorable now or blocked by a missing engine capability. Authored 16 more distinct spells this pass (28 of 192 distinct spells now have real effects, ~15%):

`Falling Comet`, `Hextech Ray`, `Incinerate`, `Blast of Power`, `Rune Prison`, `Firestorm` (lane-wide damage via the existing lane-target path), `Void Seeker` (2-step damage+draw), `Sky Splitter`, `Upstage Comedy`, `Concentrate`, `Downstage Dramatics`, `Feral Strength`, `Frigid Touch`, `Moonlight Affliction`, `Combat Experience`, `Back Off`.

Two judgment calls worth knowing about for future passes:
- Cards with a `[Repeat]` additional-cost clause, or a `[Level N]` clause that only changes cost/amount, were authored using their **base, unpaid/non-leveled** resolution (e.g. "Ready a unit." for a `[Repeat]` card, "+1 Might" for a `[Level 6] +3 Might instead" card). This only ever under-delivers relative to the full card text — it never grants something the card couldn't otherwise do — so it was treated as a safe partial implementation rather than skipped entirely.
- Cards whose text has a genuine `if/then` conditional that changes the *outcome* (extra damage, an extra draw only on a kill, a banish-instead-of-trash branch, etc.) were **not** authored, even partially, because dropping the condition would make the unconditional portion fire every time — that's not under-delivering, it's incorrect/exploitable behavior (e.g. `Disintegrate`'s "if this kills it, draw 1" would otherwise always draw).

What's still blocking most of the remaining ~164 distinct spells, roughly by frequency:
- **Multiple distinct targets in one instruction** (e.g. "Choose a friendly unit and an enemy unit. They deal damage to each other.") — the play-card API only accepts one `targetUnitId`/`targetLaneId` per action.
- **"All units"/global effects** with no target selection at all (e.g. "Kill all units.", "Ready your units.") — not just "no target," but a different resolution shape than "consume a target."
- **Variable amounts** ("equal to its Might", "equal to its Energy cost") — `CardEffectStep.Amount` is a fixed integer.
- **Choose-one / modal effects** ("Choose one — ...") — no mode-selection concept exists yet.
- **Non-effect-resolver mechanics**: token creation, move, return-to-hand/recall, attach/detach, control-change, channel-rune, look-at/reveal-and-choose, counter-a-spell. These have some support as low-level `InternalGameActionExecutor` building blocks but aren't wired into the card-effect/stack pipeline.
- **Triggered/replacement abilities embedded in spell text** ("When any unit takes damage this turn, kill it.") — these need the ability framework, not the effect resolver.

#### Full Re-Audit Pass + Multi-Target Support (2026-06-24, session 3)

A player reported that "Back to Back" ("Give two friendly units each +2 Might this turn.") had no effect when played. Root cause: it was never authored — it needs **two distinct unit targets**, which the engine and the play-card action payload had no way to express (only a single `targetUnitId`). This was exactly the "multiple distinct targets" gap called out above.

Fixed generally, not just for this one card:

- `DefaultRulesEngine.RequiredTargetCount(card)` reads phrases like "two friendly units" or "a friendly unit and an enemy unit" out of card text and returns how many distinct unit targets are needed (defaults to 1, the existing behavior).
- The `play-card` action now also accepts `targetUnitIds: string[]` alongside the existing single `targetUnitId`/`targetLaneId`. `ValidateMultiUnitTargetSelection` checks the count matches, every id is distinct, and every id is independently legal for the effect.
- No resolver changes were needed: the existing single-effect path already applies the effect to every target in the list (it was already doing this for lane-wide damage), so once multiple legal targets are validated, all of them get the effect.
- Frontend ([OnlineBattlePage.tsx](../frontend/src/features/online/OnlineBattlePage.tsx)) mirrors `RequiredTargetCount` client-side and the targeting flow now accumulates picks (1-of-N, 2-of-N, ...) before submitting, showing progress in the prompt banner.

Then did a full re-pass over all 192 distinct spells (not just the ones touched by this bug) to check for other cards wrongly left at the default no-op that should now be authorable. Found and fixed:

- **Newly unlocked by multi-target**: `Back to Back` (+2/+2, 2 targets), `Bonds of Strength` (+1/+1, 2 targets, ignoring its `[Repeat]` clause), `Facebreaker` (stun + stun, 2 targets — required extending the phrase-matcher to also catch "a friendly unit and an enemy unit", not just "two units").
- **Missed in the first pass**: cards with an "as you play this, you may spend X as an additional cost; if you do, ignore this spell's cost" clause that only modifies cost, not effect — same safe-to-omit-cost-text reasoning as `[Repeat]`/`[Level N]` cards. `Wallop` (Rally), `Call to Glory` (Buff +3). Also `Meditation` ("as an additional cost, you may exhaust a friendly unit; if you do, draw 2, otherwise draw 1") — authored as the floor value, Draw 1, since the bonus draw is gated on a cost we don't model paying.
- `Zenith Blade` ("Stun an enemy unit at a battlefield. You may move a friendly unit...") — authored the required Stun half; the optional move is dropped (same omission-only-under-delivers reasoning).

**Total now authored: 30 of 192 distinct spells (~16%).** Re-confirmed the remaining ~162 all still require one of the previously-identified missing capabilities (multiple *different*-effect targets, "all units" with no target selection, variable Might-based amounts, choose-one modes, token/move/recall/control/counter mechanics, or triggered abilities) — none were mechanically authorable with the current engine without further capability work.

Remaining work (large, multi-session):

- The other ~1,010 cards each need individual reading and a hand-written effect/ability mapping — there is no mechanical shortcut. Reasonable next slices: read and author one card category at a time (e.g. all Spells, then Units' on-play triggers, then Gear), starting with single/double-instruction cards before tackling modes, conditionals, and choose-one branches.
- Multi-step target selection currently supports at most one target per target-requiring step, consumed in step order; cards needing two independent targets in one instruction (e.g. "Choose a friendly unit and an enemy unit...") are not yet supported.
- Triggered abilities (on-play, hold/conquer, deathknell-beyond-kill, keyword-driven effects like Legion/Vision/Equip/Repeat/Weaponmaster/Ambush/Hunt) are a separate, larger body of work tracked under Abilities and Keywords below.

### Chain And Reaction Window

Status: **Partial**

- The engine models an effect stack and chain window.
- Reactions and Quick-Draw cards can be played during an open chain window.
- Action spells are rejected during Closed State chain windows unless future card-specific permission is added.
- Chain priority is tracked; non-priority passes are rejected.
- Passing by all active players resolves the newest stack item first.
- New reaction items give priority to their controller and resolve LIFO.
- Triggered/add-created chain sources do not pass showdown focus on close.
- Tests cover simple LIFO reaction resolution and priority movement.

Rule coverage:

- `327-340`, `806`, `813`, `820`.

Remaining gaps:

- The full FEPR priority model is still simplified.
- Pending vs finalized chain items are represented but not a complete model of every chain timing edge.
- Add-resource chain behavior, advanced triggered ability ordering, and card-granted timing exceptions are incomplete.

### Showdowns, Movement, And Battlefield Control

Status: **Partial**

- Moving units to a battlefield can create contested state, active showdown, and active combat.
- Non-combat showdown can pass focus and close.
- Control can be established after uncontested movement or showdown/combat resolution.
- Moving into an undefended enemy-controlled battlefield can flip control without scoring.
- A player may control multiple units at the same battlefield.
- Server movement supports base-to-battlefield, battlefield-to-base, and simultaneous standard moves to a shared destination.
- Team movement rejects teammate-controlled battlefields.
- Multiplayer movement rejects battlefields already occupied by two other players.
- Ganking is recognized for battlefield-to-battlefield movement.

Rule coverage:

- `144`, `185-189`, `341-348`, `440-448`.

Remaining gaps:

- Destination and combat restrictions for every FFA/team edge case are not complete.
- Full simultaneous movement ordering and all showdown timing details remain simplified.

### Combat And Scoring

Status: **Partial, with substantial basic coverage**

- Combat participants are represented, including team-side grouping for 2v2.
- Both sides submit damage assignments before damage is applied.
- Combat damage is simultaneous.
- Damage assignment validates summed might, friendly/missing targets, lethal-before-spill, positive lethal assignment before spreading, Tank priority, Backline/last-damage ordering, Deflect costs, over-assignment before all opposing units are lethal, and non-participant submissions.
- Current might, attached might, Shield, stunned units, prevention entries, and source attribution are considered by combat resolution.
- Lethal units go to owners' trash.
- Surviving units are healed after combat cleanup.
- Surviving attackers are recalled when defenders remain.
- Combat designation outcome and combat-open designation triggers are recorded/queued.
- Winner establishes battlefield control and may score Conquer.
- Hold scoring occurs during beginning phase.
- "Only score each battlefield once per turn" is tracked.
- Final Conquer point draws instead unless every battlefield was scored this turn, with a 2v2 teammate-occupied battlefield exception; Hold can gain the final point.
- Team totals are used for 2v2 winning.

Rule coverage:

- `142-143`, `454-467`, parts of `468-475`, `480-484`.

Remaining gaps:

- Damage source attribution, prevention, repeated combat staging, and trigger hooks exist only for modeled cases and are not a full Deal/Kill/Prevent/Replace action system.
- Combat result designation effects and all keyword interactions are still incomplete.

### Modes And Conceding

Status: **Partial**

- Supported modes are represented by player count, battlefield count, and victory score: `duel-1v1`, `ffa-3`, `ffa-4`, and `teams-2v2`.
- 2v2 turn order alternates teams when team data is available.
- Duel second-player extra first-turn channel is implemented.
- FFA/2v2 last-player extra first-turn channel is implemented.
- FFA/2v2 first-player first-draw skip is implemented.
- Duel concession completes the match.
- FFA concession removes the conceding player and continues while more than one active player remains.
- 2v2 concession causes the conceding team to lose.

Rule coverage:

- `476-484`, `649-652`.

Remaining gaps:

- Full FFA removal cleanup is incomplete: the removed player's objects, active chain/showdown contributions, pending abilities, and battlefield contribution are only partially cleared.
- Full team shared-loss cleanup and teammate object handling are incomplete.

## Improperly Implemented Or Incomplete Rules

These are places where the app has behavior, but the behavior still contradicts or under-models the reference rules.

### Golden And Silver Rules

Status: **Missing as a general system**

- Card text superseding rules (`002`) is not implemented as a general override model.
- "Can't beats can" (`054`) is not modeled.
- "Do as much as you can" (`055`) exists only in some simple effect behavior, not generally.
- Owner-zone replacement (`056`) is not implemented as a general rule.

### Full Privacy And Information Rules

Status: **Partial**

- Player-scoped redaction hides opponent hand, deck order, and unrevealed facedown card identity in API views.
- Hidden cards and facedown state exist.

Missing areas:

- A full public/private/secret information rules system is not present.
- Reveal, facedown visibility history, hidden card ownership visibility, deck inspection, and information-sharing restrictions are only lightly represented.

### Full Object Model

Status: **Partial**

- Units, gear, tokens, runes, legends, champions, battlefields, attached cards, hidden cards, facedown cards, banished cards, and continuous effects are represented in some form.
- Attach/detach and token creation have server actions.
- Temporary and Deathknell have limited lifecycle behavior.

Missing areas:

- Full permanent/card/object distinction, top-most cards, facedown zones, token lifecycle, ownership replacement, text boxes, inactive rules text, and controller/source semantics are incomplete.

### Abilities

Status: **Partial**

- Triggered abilities can be collected and queued after matching events.
- Delayed triggered abilities can fire once.
- Replacement abilities can intercept modeled score events.
- Activated and modal abilities can be legal/paid/resolved in simple cases.
- Passive abilities can contribute to unit state.

Missing areas:

- Full `360-406` semantics are not implemented.
- Ability source/controller changes, advanced trigger ordering, nested/repeated triggers, delayed replacements beyond simple cases, modal targeting across all effect shapes, and continuous effect generation from arbitrary card text are incomplete.

### Internal Game Actions

Status: **Partial framework**

`InternalGameActionExecutor` supports modeled internal actions for:

- Recycle.
- Discard.
- Reveal.
- Kill.
- Banish.
- Stun.
- Counter.
- Prevent.
- Create.
- Predict.
- Attach.
- Detach.
- Swap.
- Double.
- Recall.

Current caveat:

- These are low-level executor operations and tests, not all exposed as player-legal actions or wired into every card/effect path. Treat them as implementation building blocks, not full rule-family completion.

### Burn Out

Status: **Implemented for draw effects**

- Drawing beyond the remaining main deck draws as much as possible, recycles the player's trash into the main deck in deterministic randomized order, creates a pending opponent-choice action, awards the chosen opponent 1 point, and continues the remaining draw.
- Repeated Burn Out with an empty deck/trash is supported through repeated pending choices.
- Points gained from Burn Out can immediately end the game when they meet the victory condition.
- Engine tests cover trash recycle, pending opponent choice, repeated empty-deck Burn Out, immediate win, and deterministic recycle order.

Remaining future work:

- Rule `431` also applies when moving cards from the Main Deck to non-hand zones in excess of the remaining deck. Current fixed behavior covers draws, which are the currently modeled online engine path.

### Damage, Might, And Lethal Edge Cases

Status: **Partial**

- Combat now accounts for current might, negative might contributing zero combat damage, positive lethal assignment before spreading, Shield, Stun, prevention, Tank, Backline/last-damage ordering, and source attribution in modeled cases.

Remaining gaps:

- Simple spell damage does not run a full universal Deal action model with every prevention, source, bonus damage, replacement, and trigger hook.
- Kill/lethal handling is still implemented in specific resolution paths rather than as one generalized rules action.

### Keywords

Status: **Partial**

- `KeywordCatalog` parses and classifies: Accelerate, Action, Assault, Deathknell, Deflect, Ganking, Hidden, Legion, Reaction, Shield, Tank, Temporary, Vision, Equip, Quick-Draw, Repeat, Weaponmaster, Ambush, Hunt, Level, Unique, and Backline.
- Implemented behavior exists for several keywords, including Accelerate, Reaction, Action, Deflect, Ganking, Hidden, Shield, Tank, Temporary, Deathknell, Backline, and Quick-Draw.

Missing or incomplete areas:

- Many keyword behaviors are classified but not fully executed.
- Unique is still primarily a deck/card constraint concept, not a complete in-game uniqueness rule.
- Legion, Level, Vision, Equip, Repeat, Weaponmaster, Ambush, and Hunt need complete card-effect behavior.

### Layers

Status: **Partial**

- `ContinuousEffectLayerResolver` evaluates trait, ability, and arithmetic layers.
- It supports add/remove/set operations, dependencies, timestamps, source order, arithmetic increases before decreases, and JSON-defined effects.
- Legacy `attachedMight` is folded into arithmetic might evaluation.

Missing areas:

- This is a focused characteristic resolver, not the full rules `468-475` system.
- Full object applicability, timestamps from actual game events, dependencies across all characteristic types, continuous effects generated by card text, and layer interaction with every rules query are incomplete.

### Multiplayer And Team Specific Rules

Status: **Partial**

- Mode counts and victory score exist.
- Team identity exists in state.
- 2v2 turn order alternates teams when possible.
- Team scoring/winning uses team totals.
- Teammate spell priority and friendly/enemy target semantics exist for modeled cases.
- Team movement and team combat grouping have coverage.
- Lobby loadout selection prevents teammates in team modes from saving the same selected battlefield; saving a teammate-conflicting loadout clears the teammate's duplicate battlefield selection and unreadies them.
- FFA and team concession behavior exists for basic outcomes.

Missing areas:

- Full FFA player-removal cleanup, teammate battlefield contribution rules, all final-point exceptions, team shared-object cleanup, and every multiplayer destination restriction are incomplete.

## Frontend-Only Or Prototype Implementations To Watch

The local/hotseat rules module under `frontend/src/features/game/rules/gameRules.ts` contains client-side rule resolution for setup, turns, movement, combat, scoring, stack effects, and manual actions. This is useful for local play/prototyping, but it should not be treated as authoritative for online matches.

The online frontend path uses server-provided legal actions and `onlineActionGuards.ts` to check legal-action type and payload schema before submitting. That is appropriate as a UI guard only; final legality remains server-side.

Important frontend-only/prototype concerns:

- It can manually adjust points, readiness, damage, kills, recalls, controller, showdown, and combat staging.
- It resolves some combat and local actions independently of the server engine.
- Any online path should continue to use server-provided legal actions and backend responses as authoritative.

## Recommended Tracking Order

1. Done: fixed deck construction validation: main deck minimum 40, rune deck exactly 12, three battlefield cards, champion-tag match, domain identity, copy limits, signature limits, and duplicate battlefield names.
2. Done: implemented ordered mulligan confirmations with deterministic recycle to bottom.
3. Done: implemented draw-based Burn Out with deterministic trash recycle, pending opponent choice, point award, repeated Burn Out, and immediate win handling.
4. Done: corrected chain timing so Action spells are not valid in Closed State chain windows by default.
5. Done: fixed first-turn draw skip for FFA/2v2.
6. Done: implemented battlefield-to-base movement, simultaneous standard moves, and several team/multiplayer movement restrictions.
7. Done: added domain Power and universal Power payment basics.
8. Done: added replay-from-events support with snapshot tail replay.
9. Done: added a multi-step card effect model (`CardEffectStep`/`Steps`, `Kill`/`Banish`/`Stun` effect types, `EffectsJson` column) and authored the 12 cards whose text is an exact, unambiguous instruction match. See "Card Effect Coverage Audit (2026-06-24)" above for what remains.
10. Next: hand-author the remaining ~1,010 cards' effects/abilities one category at a time — this is the largest remaining body of work and has no scripting shortcut.
11. Next: turn internal game actions into a consistently used general action/effect pipeline across card effects.
12. Next: complete ability and keyword behavior for the parsed keyword catalog.
13. Next: expand privacy/reveal/facedown rules from redaction support into a full information model.
14. Next: finish FFA/team player-removal cleanup and remaining multiplayer destination/combat restrictions.
15. Next: decide whether frontend local rules remain a local-hotseat feature or should be retired in favor of server-provided legal actions only.
