# AGENTS.md

Guidance for AI coding agents and contributors working on this repository.

## Project Summary

Riftbound TCG is a digital card-game application with:

- A React/Vite frontend in `frontend/`.
- An ASP.NET Core backend/API in `riftbound-tcg.Server/`.
- Shared contracts and serializable domain types in `riftbound-tcg.Core/`.
- A deterministic rules engine in `riftbound-tcg.Engine/`.
- NUnit and scenario coverage in `riftbound-tcg.Tests/`.
- Aspire orchestration in `riftbound-tcg.AppHost/`.
- PostgreSQL persistence for users, decks, cards, matches, events, and snapshots.

The app is inspired by battlefield-control card game systems. Use original placeholder card names, text, and art unless the project owner explicitly provides licensed assets. Do not add proprietary card names, text, images, logos, or copied card data.

## Non-Negotiable Architecture Rules

1. The rules engine owns game truth.
2. The React client must never be authoritative for legal moves, scoring, card resolution, combat, targeting, turn flow, or win conditions.
3. All player actions must be validated server-side.
4. Game state must be serializable and deterministic.
5. Gameplay randomness must be seeded and owned by match state or match context.
6. Match events should be append-only and persisted to PostgreSQL.
7. Card behavior should be data-driven where practical.
8. Realtime messages are transport, not authority.
9. Frontend visual labs are for rendering and interaction verification only. They must not become gameplay authority.

## Current Repository Layout

```text
riftbound-tcg/
|-- AGENTS.md
|-- README.md
|-- aspire.config.json
|-- nuget.config
|-- riftbound-tcg.slnx
|-- frontend/                  # React/Vite client application
|-- riftbound-tcg.AppHost/     # Aspire AppHost orchestration
|-- riftbound-tcg.Server/      # ASP.NET Core backend/API
|-- riftbound-tcg.Core/        # Shared contracts, models, serialization types
|-- riftbound-tcg.Engine/      # Pure deterministic rules engine
|-- riftbound-tcg.Tests/       # NUnit, scenario, replay, and service tests
|-- riftbound-tcg.DomainModels/# Existing legacy/interop models; prefer Core for new contracts
|-- docs/                      # Architecture and implementation notes
|-- rules/                     # Local rules reference material
`-- archive/                   # Historical prototypes; reference only
```

Keep frontend code in `frontend/`, server/API code in `riftbound-tcg.Server/`, Aspire orchestration in `riftbound-tcg.AppHost/`, shared contracts in `riftbound-tcg.Core/`, deterministic rules in `riftbound-tcg.Engine/`, and tests in `riftbound-tcg.Tests/`.

Do not perform large reorganizations without a clear reason. Do not revive archived local-hotseat rule helpers as production authority.

## Runtime And Verification Rules

Use static checks directly, but use Aspire for full app runtime verification.

Allowed direct frontend commands:

```bash
npm run build
npm run lint
```

Do not start standalone Vite dev or preview servers for verification:

```bash
npm run dev
vite
npm run preview
```

The app is Aspire-orchestrated. For any full browser/API/realtime/manual verification, use Aspire so the frontend, backend, PostgreSQL, resource references, and generated environment are the same path the app expects.

Common Aspire flow:

```bash
aspire start --non-interactive
aspire wait server --non-interactive
aspire wait webfrontend --non-interactive
aspire ps --non-interactive
```

Current AppHost resources:

- `postgres`
- `riftbound`
- `server`
- `webfrontend`
- `devtunnel`

When backend API changes have been made and the app is actively running, rebuild the server resource so changes come through:

```bash
aspire resource server rebuild
```

Prefer Aspire resource commands for targeted restarts/rebuilds. Do not stop or restart the entire AppHost just because one resource changed unless the AppHost model itself changed.

## Frontend Visual Verification

Frontend visual changes should be verified with the dev-only visual lab when they affect the playmat, hand, card rendering, context actions, targeting affordances, drag/drop affordances, combat displays, board density, or popup behavior.

The visual lab lives in:

```text
frontend/src/features/dev/VisualStateLab.tsx
frontend/src/features/dev/VisualStateLab.css
```

It is reachable through the `Visual lab` nav item only in Vite dev mode. Because standalone Vite servers are not the verification path, reach it through the Aspire-managed `webfrontend` resource after `aspire start`.

Use the visual lab to verify scenarios such as:

- Spell context action popups.
- Gear attachment context action popups.
- Attachment and status-effect icons.
- Crowded battlefield and base layouts.
- Active combat and chain window displays.
- Viewer/opponent perspective switching.
- Custom serialized game states pasted into the JSON panel.

The visual lab may create synthetic `GameState` objects and synthetic action intents for rendering tests. Keep those states clearly local to `features/dev/`. Do not import visual lab fixtures into production game logic, the server, or the rules engine.

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
- Wall-clock time
- Unseeded randomness

The engine may depend on shared contracts from `riftbound-tcg.Core` and small internal utilities, but it must stay easy to test in isolation.

Expected engine shape:

```csharp
public interface IRulesEngine
{
    IReadOnlyList<GameAction> GetLegalActions(GameState state, PlayerId playerId);
    ActionResult ApplyAction(GameState state, GameAction action);
}
```

When adding rules, prefer these concepts and keep names aligned where possible:

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

Every player action should follow this pipeline:

```text
Receive action
Authorize player for match
Validate action
Pay costs
Choose or verify targets
Apply deterministic state changes
Queue triggered effects
Resolve pending effects where appropriate
Check scoring and win conditions
Emit event
Persist event
Broadcast updated state and legal actions
```

Do not mutate authoritative game state from the React client. Client-side previews and visual affordances are allowed, but final state must come from the server after rules-engine validation.

## State Mutation Rules

Be deliberate with mutation.

Good options:

- Immutable state transitions, especially in TypeScript.
- Controlled mutation inside the engine if it is isolated and thoroughly tested.
- Event-sourced state transitions where actions produce match events and replayable state.

Avoid:

- UI components directly editing match state.
- Hidden global match state.
- Randomness without a seed.
- Date/time calls inside deterministic rule resolution.
- Transport, database, or auth types leaking into engine logic.

## React Frontend Guidelines

The frontend should:

- Render server-provided game state.
- Show legal actions from the server or from server-approved action payloads.
- Send player-selected actions to the backend.
- Treat backend responses as authoritative.
- Keep animation/hover/drag state separate from game state.
- Keep targeting UI separate from rules validation.
- Use visual lab scenarios for playmat and popup regressions.

Current important frontend areas:

```text
frontend/src/app/                         # App shell and navigation
frontend/src/features/online/             # Online battle page, playmat, action guards
frontend/src/features/dev/                # Dev-only visual state lab
frontend/src/shared/api/                  # API clients and DTO types
frontend/src/shared/models/               # Frontend model types
frontend/src/shared/ui/                   # Shared cards, menus, previews, stacks
```

Avoid putting rules like "can this card be played?" directly in React components. Use server-provided legal actions, server-approved action intents, or shared display helpers that do not decide authority.

## Backend Guidelines

The backend should:

- Authenticate users.
- Create and manage matches.
- Keep active match state server-side.
- Validate every incoming action with the rules engine.
- Persist valid actions to PostgreSQL.
- Broadcast state and legal-action updates to connected clients.
- Reject illegal actions clearly.
- Rebuild matches from snapshots and events when needed.
- Check match membership before allowing a user to view or act in a match.

Keep API DTOs and persistence models from becoming rules-engine dependencies. Map at the application boundary.

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
- Match events should be append-only. Do not edit old events except during explicit development reset workflows.

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
6. Add or update visual lab scenarios if the effect changes frontend displays, targeting, status icons, card context menus, or board layout.

## Testing Requirements

When changing the engine, add or update tests.

Minimum expected tests for rules work:

- Legal action generation.
- Illegal action rejection.
- State transition correctness.
- Replay determinism, where relevant.
- Edge cases around timing, targeting, combat, scoring, and chain windows.

Test names should describe behavior:

```text
player_scores_when_they_control_a_battlefield
spell_cannot_target_a_friendly_unit_when_enemy_unit_is_required
unit_cannot_move_twice_when_exhausted
```

Do not rely only on snapshot tests for rules behavior.

Useful verification commands:

```bash
dotnet test
npm run build
npm run lint
```

For integrated verification, use Aspire as described in "Runtime And Verification Rules".

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

Use OpenAPI, generated TypeScript types, Zod schemas, or another shared contract approach to prevent frontend/backend drift. Keep route payloads explicit and versioned where practical.

## Code Style

Follow the style already present in the repository.

- Use TypeScript for frontend code.
- Use C# for backend, core, engine, and tests.
- Prefer strict type checking and nullable reference types.
- Avoid `any` unless there is a clear reason.
- Keep functions small and named by domain behavior.
- Avoid leaking transport or database types into the rules engine.
- Use linting and formatting through project scripts.
- Prefer focused patches over broad rewrites.

## Security Guidelines

- Never commit secrets.
- Do not log access tokens, refresh tokens, passwords, or session cookies.
- Validate all inbound payloads.
- Check match membership before allowing a user to view or act in a match.
- Rate-limit action submission if needed.
- Use server-side authorization for deck editing and match actions.
- Treat pasted visual lab JSON as local dev test data only; never execute embedded content from it.

## Documentation Expectations

When adding a major system, update documentation in `docs/` or the README.

Good docs to maintain:

- Architecture overview.
- Game state model.
- Action model.
- Card definition format.
- Effect handler list.
- Database schema notes.
- Realtime message contract.
- Visual lab scenario coverage for frontend game-state rendering.
- Aspire runtime and resource-operation notes.

## Before Opening A Pull Request

Check the following:

- Code compiles.
- Lint passes.
- Engine tests pass.
- Database migrations are included for schema changes.
- New rules have scenario tests.
- Frontend visual changes have been checked in the Visual lab where applicable.
- Integrated app behavior was verified through Aspire when runtime verification was needed.
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
- Add or update visual lab states alongside frontend visual gameplay changes.
- Keep generated code and hand-written code clearly separated.
- Use Aspire for full app verification; do not manually start standalone Vite dev servers.
