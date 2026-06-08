# AGENTS.md

Guidance for AI coding agents and contributors working on this repository.

## Project Summary

This is a digital card-game application with a React frontend, a server-authoritative backend, a pure rules engine, realtime multiplayer, and PostgreSQL persistence.

The app is inspired by battlefield-control card game systems. Use original placeholder card names, text, and art unless the project owner explicitly provides licensed assets.

## Non-Negotiable Architecture Rules

1. The rules engine owns game truth.
2. The React client must not be the authoritative source for legal moves, scoring, card resolution, or win conditions.
3. All player actions must be validated server-side.
4. Game state must be serializable and deterministic.
5. Match events should be append-only and persisted to PostgreSQL.
6. Card behavior should be data-driven where practical.
7. Do not hardcode proprietary card names, text, images, or logos.

## Preferred Repository Layout

```text
riftbound-tcg/
├── AGENTS.md
├── README.md
├── aspire.config.json
├── nuget.config
├── riftbound-tcg.slnx
├── frontend/                  # React/Vite client application
├── riftbound-tcg.AppHost/     # Aspire AppHost orchestration project
├── riftbound-tcg.Server/      # ASP.NET Core backend/API project
├── riftbound-tcg.Core/        # Shared domain models, contracts, and serialization types
├── riftbound-tcg.Engine/      # Pure deterministic rules engine
└── riftbound-tcg.Tests/       # NUnit and scenario tests, especially engine coverage
```

`riftbound-tcg.Core`, `riftbound-tcg.Engine`, and `riftbound-tcg.Tests` are expected project boundaries even if they are introduced incrementally. Keep frontend code in `frontend/`, server/API code in `riftbound-tcg.Server/`, Aspire orchestration in `riftbound-tcg.AppHost/`, shared domain contracts in `riftbound-tcg.Core/`, deterministic game rules in `riftbound-tcg.Engine/`, and test coverage in `riftbound-tcg.Tests/`.

Follow the existing repository structure if it already differs from this. Do not perform large reorganizations without a clear reason.

## Rules Engine Guidelines

The engine should be independent from:

- React
- DOM APIs
- HTTP
- WebSockets
- PostgreSQL
- Authentication
- File systems
- Environment variables

The engine may depend on shared type packages, validation libraries, or internal utilities, but keep it easy to test in isolation.

Expected engine API shape:

```ts
export interface RulesEngine {
  getLegalActions(state: GameState, playerId: PlayerId): GameAction[];
  applyAction(state: GameState, action: GameAction): ActionResult;
}
```

When adding rules, prefer these concepts:

- `GameState`
- `PlayerState`
- `CardDefinition`
- `CardInstance`
- `Zone`
- `BattlefieldState`
- `TurnState`
- `GameAction`
- `ActionValidator`
- `ActionResolver`
- `PendingEffect`
- `TriggeredAbility`
- `ContinuousEffect`

## Action Handling Pattern

Every action should follow this pipeline:

```text
Receive action
Validate action
Pay costs
Choose or verify targets
Apply state changes
Queue triggered effects
Resolve pending effects where appropriate
Check scoring and win conditions
Emit event
Persist event
Broadcast updated state
```

Do not mutate state from the React client. Client-side previews are allowed, but final state must come from the server.

## State Mutation Rules

Be deliberate with mutation.

Good options:

- Immutable state transitions, especially in TypeScript.
- Controlled mutation inside the engine if it is isolated and thoroughly tested.
- Event-sourced state transitions where each action produces a resulting event.

Avoid:

- UI components directly editing match state.
- Hidden global match state.
- Randomness without seeded RNG.
- Date/time calls inside deterministic rule resolution.

## Randomness

Any shuffle, random selection, or generated value that affects gameplay must use a deterministic seeded RNG owned by the match state or match context.

Do not use:

```ts
Math.random()
```

inside gameplay resolution.

## React Frontend Guidelines

The frontend should:

- Render server-provided game state.
- Show legal actions from the server or derived from server-approved data.
- Send player-selected actions to the backend.
- Treat backend responses as authoritative.
- Keep animation state separate from game state.
- Keep targeting UI separate from rules validation.

Suggested component boundaries:

```text
GamePage
OpponentArea
BattlefieldRow
BattlefieldSlot
PlayerBoard
Hand
CardView
ActionPanel
TargetingOverlay
MatchLog
```

Avoid putting rules like “can this card be played?” directly in React components. Use server-provided legal actions or shared display helpers that do not decide authority.

## Backend Guidelines

The backend should:

- Authenticate users.
- Create and manage matches.
- Keep active match state server-side.
- Validate every incoming action with the rules engine.
- Persist valid actions to PostgreSQL.
- Broadcast state updates to connected clients.
- Reject illegal actions clearly.
- Rebuild matches from snapshots and events when needed.

Recommended active match persistence model:

```text
In-memory active state
+ PostgreSQL append-only event log
+ periodic match snapshots
```

## PostgreSQL Guidelines

Use migrations for all schema changes.

Recommended tables:

- `users`
- `cards`
- `decks`
- `deck_cards`
- `matches`
- `match_players`
- `match_events`
- `match_snapshots`

General database rules:

- Do not store passwords directly. Use a trusted auth provider or secure password hashing.
- Store card effect definitions as JSON only when the shape is validated at the application boundary.
- Use transactions when creating matches and appending events.
- Add indexes for common lookups such as `match_id`, `user_id`, and event sequence.
- Match events should be append-only; do not edit old events except during explicit development reset workflows.

Example event fields:

```text
id
match_id
sequence_number
player_id
action_type
action_payload
result_payload
created_at
```

## Card Data Guidelines

Use placeholder card content during development.

Example card definition:

```json
{
  "id": "test-firebolt",
  "name": "Firebolt",
  "type": "Spell",
  "cost": {
    "generic": 2,
    "domains": ["Fury"]
  },
  "timing": "Main",
  "effects": [
    {
      "type": "DealDamage",
      "amount": 3,
      "target": "EnemyUnit"
    }
  ]
}
```

When adding an effect type:

1. Define the schema.
2. Add validation.
3. Add an effect handler.
4. Add tests for valid and invalid targets.
5. Add at least one scenario test.

## Testing Requirements

When changing the engine, add or update tests.

Minimum expected tests for rules work:

- Legal action generation
- Illegal action rejection
- State transition correctness
- Replay determinism, where relevant
- Edge cases around timing, targeting, or scoring

Test names should describe behavior:

```text
player_scores_when_they_control_a_battlefield
spell_cannot_target_a_friendly_unit_when_enemy_unit_is_required
unit_cannot_move_twice_when_exhausted
```

Do not rely only on snapshot tests for rules behavior.

## Realtime Guidelines

Realtime messages should be explicit and versioned where practical.

Common message types:

- `match.joined`
- `match.state`
- `match.legalActions`
- `match.actionSubmitted`
- `match.actionRejected`
- `match.eventAppended`
- `match.completed`

Never trust a client-sent action because the UI hid illegal choices. Validate anyway.

## API Guidelines

Prefer clear contracts between client and server.

Example routes:

```text
POST /matches
GET /matches/:id
POST /matches/:id/actions
GET /matches/:id/events
GET /cards
GET /decks
POST /decks
```

Use OpenAPI, generated TypeScript types, Zod schemas, or another shared contract approach to prevent frontend/backend drift.

## Code Style

Follow the style already present in the repository. If no style exists yet:

- Use TypeScript for frontend code.
- Use C# for ASP.NET Core backend code.
- Prefer strict type checking (TypeScript) and nullable reference types (C#).
- Avoid `any` unless there is a clear reason.
- Keep functions small and named by domain behavior.
- Avoid leaking transport or database types into the rules engine.
- Use linting and formatting through project scripts.

Recommended commands, if available:

```bash
npm run lint
npm run typecheck
npm test
npm run test:e2e
```

## Security Guidelines

- Never commit secrets.
- Do not log access tokens, refresh tokens, passwords, or session cookies.
- Validate all inbound payloads.
- Check match membership before allowing a user to view or act in a match.
- Rate-limit action submission if needed.
- Use server-side authorization for deck editing and match actions.



## Documentation Expectations

When adding a major system, update documentation in `docs/` or the README.

Good docs to maintain:

- Architecture overview
- Game state model
- Action model
- Card definition format
- Effect handler list
- Database schema notes
- Realtime message contract

## Before Opening a Pull Request

Check the following:

- Code compiles.
- Lint passes.
- Engine tests pass.
- Database migrations are included for schema changes.
- New rules have scenario tests.
- React UI does not contain authoritative rule logic.
- No proprietary assets or copied card text were added.
- README or docs were updated if behavior or setup changed.

## Agent Behavior Notes

When working as an AI coding agent:

- Inspect existing files before making broad changes.
- Prefer small, focused patches.
- Do not invent dependencies if a suitable existing dependency is already present.
- Do not replace the architecture without user approval.
- Ask only when blocked by a real ambiguity; otherwise make a reasonable, reversible choice.
- Preserve deterministic gameplay behavior.
- Add tests alongside rules changes.
- Keep generated code and hand-written code clearly separated.
