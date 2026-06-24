# Rules Implementation Audit

Reference source: `rules/`, generated from `Riftbound Core Rules.pdf` last updated 2026-03-30.

This audit tracks how much of the new numbered rules reference is represented in the current app. "Implemented" means there is server/engine behavior, not just UI affordance, unless explicitly noted.

## Status Legend

- **Implemented:** covered by the server-side C# rules engine or backend validation with tests or direct persistence support.
- **Partial:** some behavior exists, but the rule family is incomplete.
- **Improper:** behavior exists but contradicts the reference rules.
- **Missing:** no meaningful authoritative implementation found.
- **Frontend-only/prototype:** implemented in `frontend/src/features/game/rules/gameRules.ts` or manual UI controls, but not authoritative for online matches.

## Properly Implemented Or Mostly Correct

### Rules Engine Authority And Determinism

Status: **Implemented**

- `IRulesEngine` exposes authoritative create-state, legal-action, and apply-action operations.
- `DefaultRulesEngine` validates player seating, expected sequence number, and current legal action before accepting actions.
- Initial state is serializable JSON.
- Main deck and rune deck shuffles use a deterministic seeded algorithm, and tests cover deterministic initial state.
- Server online play routes actions through `IRulesEngine`; accepted actions are persisted as match events and snapshots.

Rule coverage:

- `000-099`: deterministic rules precedence is not deeply implemented, but the engine authority rule aligns with project architecture.
- `300-324`, `407-448`: action sequencing and server-side legality are partially enforced.

### Basic Setup, Zones, And Match State

Status: **Partial**

- Players get legend, champion, main deck, rune deck, hand, trash, base, runes, rune pool, points, and selected battlefield state.
- Players draw 4 during initial setup.
- Separate main deck and rune deck zones exist in state.
- Battlefields exist as shared board locations.
- Champion and legend are represented as distinct slots.

Rule coverage:

- `103`, `107`, `108`, `111-119`, `128`, `159-171`, `172-175`.

Gaps remain around exact deck construction, privacy, battlefield selection, champion-zone play semantics, domain identity, and public/private information enforcement.

### Mulligan

Status: **Mostly implemented**

- Each player may choose up to two cards, redraw the same number, then returned cards go back into the deck.
- Duplicate/out-of-range card indexes are ignored.
- Current server engine allows players to confirm mulligan independently rather than requiring strict turn-order confirmations.

Rule coverage:

- `118.1-118.3`.

Improper/incomplete details:

- Returned cards are appended to the deck, but rule `118.3` says they are Recycled, which delegates to rule `416`; simultaneous main-deck recycle should randomize the returned cards onto the bottom.
- The reference says mulligans are performed in turn order; the server currently allows any unconfirmed player to confirm.

### Turn Skeleton

Status: **Partial**

- The engine has an ordered phase progression: awaken, beginning, channel, draw, main, ending.
- Awaken readies the turn player's exhausted runes and units.
- Beginning scores held battlefields.
- Channel adds runes from the rune deck.
- Draw draws 1.
- Ending advances turn order and clears per-turn scored battlefield tracking.

Rule coverage:

- `301-306`, `314-317`, `315.1-315.4`, `316`, `317`.

Improper/incomplete details are listed below under "Improperly Implemented".

### Resource And Cost Basics

Status: **Partial**

- Ready runes can be exhausted to pay generic numeric costs.
- A simple energy pool exists and is emptied at draw and end-turn boundaries.
- Simple affordability checks prevent playing cards without enough generic resources.

Rule coverage:

- `131`, `161-166`, `356-357`, `429-430`.

Major gaps:

- Power costs and domains are not modeled in payment.
- Rune activated abilities and reaction resource abilities are not implemented.
- Cost replacement, increases, discounts, optional/mandatory additional costs, and non-standard costs are not implemented.

### Basic Card Play And Simple Effects

Status: **Partial**

- Units can be played from hand to base or a controlled battlefield.
- Units enter exhausted.
- Gear can be played and resolves to the owner's base.
- Spells/gear/effects can create stack items.
- Simple effect model supports only `draw`, `damage`, `buff`, and `rally`.
- Basic target checks exist for friendly buff/rally, enemy damage, and lane damage.

Rule coverage:

- `141-158`, `349-359`, `413`, `417`, `419`.

Major gaps:

- Card text is not parsed into general effects.
- Modes, additional choices, non-public target rules, mistargeting, linked instructions, triggered abilities, replacement effects, and most card categories are not implemented.

### Chain And Reaction Window

Status: **Partial**

- The engine models an effect stack and chain window.
- Reactions can be played during an open chain window.
- Passing by all players resolves the newest stack item first.
- Tests cover simple LIFO reaction resolution.

Rule coverage:

- `327-340`, `806`, `813`.

Improper/incomplete details:

- `ChainRules.ValidateChainPlay` now rejects Action spells during an existing chain for all players unless a future rule/effect grants explicit Closed State timing.
- Priority order is simplified to all players passing rather than "controller of newest item, then next player in turn order" with exact FEPR behavior.
- Pending vs finalized chain items are simplified.
- Triggered abilities, activated abilities, add-resource chain behavior, and "chain opened by triggered/add ability does not pass Focus" are missing.

### Showdowns, Movement, And Battlefield Control

Status: **Partial**

- Moving units to a battlefield can create contested state, active showdown, and active combat.
- Non-combat showdown can pass focus and close.
- Control can be established after uncontested movement or showdown/combat resolution.
- A player may control multiple units at the same battlefield.

Rule coverage:

- `144`, `185-189`, `341-348`, `440-448`.

Improper/incomplete details:

- Standard move is implemented only for one unit at a time; rule `144.3` allows simultaneous standard moves to the same destination.
- Moving from battlefield back to base is not implemented in the server action, even though rule `144.4.b` allows it.
- Ganking battlefield-to-battlefield movement is missing.
- Multi-player and team destination restrictions are incomplete.

### Combat And Scoring

Status: **Partial, with solid basic tests**

- Combat is restricted to exactly two players.
- Attacker and defender assignments are represented.
- Both sides submit damage assignments before damage is applied.
- Combat damage is simultaneous.
- Damage assignment validates summed might, friendly/missing targets, lethal-before-spill, and over-assignment before all opposing units are lethal.
- Lethal units go to owners' trash.
- Surviving units are healed after combat cleanup.
- Winner establishes battlefield control and may score Conquer.
- Hold scoring occurs during beginning phase.
- "Only score each battlefield once per turn" is tracked.
- Final Conquer point draws instead unless every battlefield was scored that turn; Hold can gain the final point.

Rule coverage:

- `142-143`, `454-467`.

Missing details:

- Tank/last damage priority, Deflect, Shield, Stun, prevention, damage source attribution, triggered combat abilities, attacker recall when defenders remain, combat-result designation effects, and repeated combat staging are only partially represented or absent.

### Modes And Conceding

Status: **Partial**

- Supported modes are represented by player count, battlefield count, and victory score: `duel-1v1`, `ffa-3`, `ffa-4`, and `teams-2v2`.
- Duel second-player extra first-turn channel is implemented.
- FFA/2v2 last-player extra first-turn channel is implemented.
- Concede action exists.

Rule coverage:

- `476-484`, `649-652`.

Improper/incomplete details:

- FFA/2v2 first player should skip their first draw; the frontend prototype does this, but the server engine currently always draws.
- 2v2 turn order is not guaranteed to alternate teams; it is just a rotated numeric seat order.
- 2v2 team win/loss and teammate concession removal are incomplete.
- FFA concession/removal process is missing; current concede just awards the match to the first other player.

## Improperly Implemented Rules

These are places where the app has behavior, but the behavior currently contradicts the reference rules.

### Deck Construction Counts

Status: **Improper**

Reference:

- `103.2`: Main Deck must have at least 40 cards.
- `103.3.a`: Rune Deck has exactly 12 Rune cards.
- `103.4`: Battlefield count is dictated by mode; sanctioned modes expect each player to bring three battlefields, then select/remove according to mode.

Current behavior:

- Server `ValidateDeckAsync` rejects main decks with more than 40 cards, allowing decks below 40. This reverses the rule.
- Frontend validation also says "Main deck can contain at most 40 cards."
- Server and frontend allow rune decks with at least 12 cards, not exactly 12.
- Server allows battlefield decks with 1 to 3 cards, not the required three-card battlefield deck for sanctioned modes.

### Deck Construction Missing Restrictions

Status: **Improper/Incomplete**

Reference:

- `103.1.b`: Cards must match the Champion Legend's domain identity.
- `103.2.a`: Chosen Champion must be a champion unit with a champion tag matching the Champion Legend.
- `103.2.b`: Up to 3 copies of the same named main-deck card.
- `103.2.d`: Up to 3 total Signature cards matching the Champion Legend's champion tag.
- `103.3.a.1`: Rune cards must match domain identity.
- `103.4.c`: No duplicate battlefield names when more than one battlefield is required.

Current behavior:

- Frontend filters runes by legend domain, but the server does not enforce full domain identity for main deck, rune deck, or battlefields.
- Server validates champion kind but not champion-tag match.
- Server and frontend do not enforce copy limits by card name.
- Signature total and tag restriction are not enforced.
- Duplicate battlefield-name limits are not enforced.

### First Draw In FFA And 2v2

Status: **Improper**

Reference:

- `482.7`, `483.7`, `484.7`: in FFA3, FFA4, and 2v2, the player going first does not draw during their first Draw Phase.

Current behavior:

- Server `DefaultRulesEngine.AdvancePhase` always draws 1 in the draw phase.
- The frontend prototype has a skip path, but online/server-authoritative play does not.

### Chain Timing For Action Spells In Closed State

Status: **Fixed**

Reference:

- `309.1.a`, `338.1.a.2`, `358.3`: Closed State cards generally need Reaction timing unless a rule/effect creates another exception.
- `158.2.a`: Action allows play during Open States during Showdowns, not ordinary chain response timing.

Fixed behavior:

- `DefaultRulesEngine.GetLegalActions` only offers Reactions during `chainWindow`, which is correct for current online behavior.
- `SpellClassifier.CanPlayDuringChainWindow` now only allows cards with Reaction timing.
- `ChainRules.ValidateChainPlay` now rejects Action spells during a Closed State chain window for both the turn player and non-turn players.
- Tests were updated in `SpellClassifierTests`, `ChainRulesTests`, and `ChainIntegrationTests` so Action spells cannot be chained by turn-player exception.

Remaining future work:

- If a future card effect or explicit exception allows an Action spell to be played in a Closed State, model that as a separate granted timing permission rather than a default turn-player exception.

### Movement Restrictions

Status: **Improper/Incomplete**

Reference:

- `144.4.a`: Units may move from base to a battlefield, subject to occupancy/combat restrictions.
- `144.4.b`: Units may move from a battlefield to their base.
- `144.3`: Multiple units may standard move simultaneously if they share a destination.
- `442.2.a-b`, `444.2`, `457.1-457.3`: multiplayer and team combat destination restrictions.

Current behavior:

- Server `move-unit` only moves a player's own unexhausted base unit to a battlefield.
- Battlefield-to-base standard movement is missing.
- Simultaneous multi-unit movement is missing.
- The error message says "battlefield you control," but the implementation can move into enemy or uncontrolled battlefields for contesting. The behavior is closer to the rules than the message, but the validation text is incorrect.
- Team destination restrictions are missing.

### Concede

Status: **Improper**

Reference:

- `650-652`: A player may concede any time; if more than one player remains, remove that player and continue. In team modes, teammates also lose. The removed player's objects, battlefield contribution, spells, and abilities are processed.

Current behavior:

- Any concede immediately sets `game-over` and picks the first other player as winner.
- This is only correct for a 1v1 game. It is wrong for FFA and incomplete for teams.

### Burn Out

Status: **Improper/Missing**

Reference:

- `413.4`, `431`: drawing beyond deck performs Burn Out: draw as much as possible, recycle trash into deck, choose opponent to gain 1 point, then continue drawing.

Current behavior:

- Draw simply stops when the deck is empty.
- No trash recycle, opponent point award, repeated Burn Out, or immediate win handling exists.

### Rune Pool And Payment

Status: **Improper/Incomplete**

Reference:

- `161-166`, `356-357`, `429`: costs include Energy and domain Power; resource Add abilities can be used during payment.

Current behavior:

- Costs are a single integer `cost`.
- Rune payment exhausts ready runes directly as generic energy.
- Domain Power, universal Power, rune Add abilities, and payment-time Reaction abilities are missing.

### Damage, Might, And Lethal Edge Cases

Status: **Improper/Incomplete**

Reference:

- `143.2.a`: a unit is killed if nonzero damage equals or exceeds Might.
- `143.2.b`: Might below 0 is treated as 0 for references and combat damage, but actual Might remains negative.
- `417.1.e`: valid damage is a positive integer.

Current behavior:

- Combat tests allow zero damage assignments when current combat might is zero. That is probably acceptable for assignment, but the engine does not clearly distinguish assigning 0 from dealing valid positive damage.
- Lethal threshold is clamped to at least 1 in `LethalDamage`; this helps avoid zero/negative instant death but does not fully model "nonzero damage equals/exceeds Might" for every edge case.
- Simple spell damage does not run the full Deal action model with prevention, sources, bonus damage, or trigger hooks.

## Missing Or Essentially Not Implemented

### Golden And Silver Rules

Status: **Missing as a general system**

- Card text superseding rules (`002`) is not implemented as a general override model.
- "Can't beats can" (`054`) is not modeled.
- "Do as much as you can" (`055`) exists only in some simple effect behavior, not generally.
- Owner-zone replacement (`056`) is not implemented.

### Full Privacy And Information Rules

Status: **Missing**

- Secret/private/public information is not enforced as a rules system.
- Hand visibility, deck order secrecy, facedown card privacy, and reveal rules are not modeled authoritatively.

### Full Object Model

Status: **Missing/Partial**

- Permanents, runes, legends, battlefields, tokens, attached cards, top-most cards, facedown zones, banishment, and continuous modifications are not represented with the full rule semantics.
- Attach/detach, top-most card, effect text, inactive rules text, might bonuses as attached-card layer effects, and token lifecycle are missing.

### Abilities

Status: **Missing**

Reference `360-406` is almost entirely unimplemented.

Missing areas include:

- Passive abilities.
- Triggered abilities.
- Activated abilities.
- Replacement effects.
- Delayed triggered/replacement abilities.
- Continuous effects.
- Modal abilities.
- Ability source/controller changes.
- Ability timing beyond simple spell Action/Reaction text classification.

### Most Game Actions

Status: **Missing**

Only a small subset exists: draw, exhaust/ready via turn/move/payment, play, move, deal damage, channel, score, and concede.

Missing or non-authoritative actions include:

- Recycle.
- Hide.
- Discard.
- Stun.
- Reveal.
- Counter.
- Kill as a general action.
- Banish.
- Add Power/Energy as actual resource actions.
- Burn Out.
- Double.
- Swap.
- Attach/detach.
- Predict.
- Prevent.
- Replace.
- Create.
- Recall as a general permanent correction.

### Keywords

Status: **Mostly missing**

Only `Action` and `Reaction` are lightly recognized by text search. The keyword rules in `800-keywords.md` are otherwise missing, including Accelerate, Deathknell, Deflect, Ganking, Hidden, Shield, Tank-like assignment modifiers, Temporary, and others.

### Layers

Status: **Missing**

The rules in `468-475` are not implemented. Current `attachedMight` is a flat modifier, not a layer system with trait-altering effects, ability-altering effects, arithmetic ordering, dependencies, or timestamps.

### Multiplayer And Team Specific Rules

Status: **Partial/Missing**

- Mode counts and victory score exist.
- Team identity exists in state.
- Team scoring/winning, teammate priority invitation, teammate battlefield restrictions, teammate deck restrictions, friendly semantics, team concession, and final-point team exceptions are missing or incomplete.
- FFA player removal and continuing-game behavior are missing.

### Rebuild From Event Log

Status: **Persistence implemented, replay not found**

- Match events and snapshots are persisted.
- Current loading uses the latest snapshot.
- No authoritative replay-from-events path was found for rebuilding match state from the append-only event log.

## Frontend-Only Or Prototype Implementations To Watch

The local/hotseat rules module under `frontend/src/features/game/rules/gameRules.ts` contains client-side rule resolution for setup, turns, movement, combat, scoring, stack effects, and manual actions. This is useful for local play/prototyping, but it should not be treated as authoritative for online matches.

Important frontend-only/prototype concerns:

- It has a first-player draw skip for FFA/2v2 that the server engine lacks.
- It can manually adjust points, readiness, damage, kills, recalls, controller, showdown, and combat staging.
- It resolves combat automatically instead of using the same assignment flow as the server engine.
- It contains rule logic that should either be removed from online paths or kept strictly as display/local-hotseat behavior.

## Recommended Tracking Order

1. Fix improper deck construction validation: main deck minimum 40, rune deck exactly 12, three battlefield cards where required, champion-tag match, domain identity, copy limits, and signature limits.
2. Fix server first-turn draw skip for FFA/2v2.
3. Done: corrected `ChainRules.ValidateChainPlay` and `SpellClassifier.CanPlayDuringChainWindow` so Action spells are not valid in Closed State chain windows by default.
4. Implement battlefield-to-base movement and simultaneous standard moves.
5. Implement Burn Out.
6. Replace the generic integer resource model with Energy plus domain Power.
7. Add a real effect/action system for core actions before expanding card text and keywords.
8. Decide whether frontend local rules remain a local-hotseat feature or should be retired in favor of server-provided legal actions only.
