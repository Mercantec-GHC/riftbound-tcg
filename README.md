# Riftlike Card Game App

A digital card-game application inspired by battlefield-control card game systems. The project is designed around a rules-engine-first architecture, with a React-based client, a server-authoritative game engine, realtime multiplayer, and PostgreSQL persistence.


## Goals

- Build a playable digital card game with Riftbound-style battlefield control, champions, units, spells, gear, scoring, and turn structure.
- Keep the game rules independent from the UI so the engine can be tested, simulated, and reused.
- Support multiplayer through a server-authoritative model.
- Store users, decks, matches, card definitions, and replay/event data in PostgreSQL.
- Make card behavior data-driven where possible, avoiding hardcoded one-off card logic.

## Core Design Principle

The rules engine owns the truth. The React UI only displays game state and lets players choose from legal actions provided by the server.

```text
React Client
  ↓
API / Realtime Transport
  ↓
Match Server
  ↓
Rules Engine
  ↓
Game State + Event Log
  ↓
PostgreSQL
```

## Suggested Tech Stack

### Frontend

- React
- TypeScript
- Vite
- CSS Modules, Tailwind, Mantine, MUI, or another component system
- WebSocket client for realtime match updates
- Client-side state management with Zustand, Redux Toolkit, Jotai, or React Query depending on preference

### Backend

- ASP.NET Core with C#

### Database

- PostgreSQL
- Use migrations from day one
- Store match event logs so games can be replayed and debugged
- Store cards as structured data, preferably JSON-backed records or normalized tables with JSON effect definitions

### Realtime

- WebSockets
- SignalR if using ASP.NET Core
- Socket.IO or native WebSocket if using Node/NestJS

### Testing

- Unit tests for the rules engine
- Scenario tests for card interactions
- Integration tests for API and database flows
- Playwright or Cypress for important frontend flows

## Repository Structure

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

## Major Systems

### 1. Game State

The game state should be serializable, deterministic, and independent from the UI.

Typical state objects:

- Players
- Decks
- Hands
- Discard piles
- Removed/exile zones
- Champions
- Units
- Gear or attachments
- Battlefields
- Turn state
- Priority/timing state
- Score state
- Pending effects

### 2. Actions

Every player decision should be represented as an action.

Examples:

- Start match
- Draw card
- Play card
- Move unit
- Attack or challenge, if applicable to the rules model
- Choose target
- Resolve ability
- Pass priority
- Score battlefield
- End phase

Actions should be validated by the engine before being applied.

### 3. Rules Engine

The engine should expose a small public API:

```ts
export interface RulesEngine {
  getLegalActions(state: GameState, playerId: PlayerId): GameAction[];
  applyAction(state: GameState, action: GameAction): ActionResult;
}
```

The engine should not know about React, HTML, WebSockets, HTTP, PostgreSQL, or authentication.

### 4. Card Definitions

Prefer structured card definitions over hardcoded logic.

Example shape:

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

Effect handlers can then resolve behavior by `effect.type`.

### 5. Event Log and Replays

Matches should be stored as an initial state plus an ordered list of events/actions.

Benefits:

- Replays
- Debugging
- Desync investigation
- Spectator mode
- AI simulation
- Easy bug reproduction

Recommended database tables:

- `users`
- `cards`
- `decks`
- `deck_cards`
- `matches`
- `match_players`
- `match_events`
- `match_snapshots`

### 6. PostgreSQL Persistence

Use PostgreSQL for durable state. Avoid storing the only source of an active match exclusively in process memory unless the app is explicitly prototype-only.

Good persistence pattern:

```text
Authoritative in-memory match state during active play
+ append-only event log written to PostgreSQL
+ periodic snapshots for fast recovery
```

The server should be able to rebuild a match by loading the latest snapshot and replaying subsequent events.

## Frontend Guidelines

The React client should:

- Render game state received from the server.
- Request legal actions from the server or receive them with match state updates.
- Highlight only legal targets and actions.
- Send selected actions to the server for validation.
- Never apply game rules locally as the source of truth.
- Use optimistic UI only for non-authoritative visual feedback.

Suggested major components:

```text
GamePage
├── OpponentArea
├── BattlefieldRow
├── PlayerBoard
├── Hand
├── CardView
├── ActionPanel
├── TargetingOverlay
└── MatchLog
```

## Backend Guidelines

The server should:

- Authenticate players.
- Own authoritative match state.
- Validate every submitted action.
- Broadcast state updates after valid actions.
- Reject illegal actions with clear error messages.
- Persist match events.
- Optionally persist snapshots after important turns or after a fixed number of events.

## Development Milestones

### Milestone 1: Local Engine Prototype

- Create game state model.
- Create placeholder cards.
- Implement match setup.
- Implement drawing, playing simple units, movement to battlefields, scoring, and win condition.
- Add engine scenario tests.

### Milestone 2: React Match UI

- Render a static match state.
- Display player hand and battlefields.
- Show legal actions.
- Submit actions to a mock engine or local dev server.

### Milestone 3: Server-Authoritative Multiplayer

- Create match rooms.
- Connect clients over WebSockets.
- Validate actions on the server.
- Broadcast updated state.
- Store match events in PostgreSQL.

### Milestone 4: Card Effects

- Add structured card effect definitions.
- Implement core effect handlers.
- Add targeting rules.
- Add triggered abilities.
- Add replacement or prevention effects if needed.

### Milestone 5: Decks and Accounts

- User accounts.
- Deck builder.
- Deck validation.
- Match history.
- Replays.

## Local Development

Example flow:

```bash
# Start everything up
aspire start
```

## Testing Strategy

Rules bugs are the most expensive bugs in a card game. Test the engine heavily.

Recommended test categories:

- Legal action generation
- Invalid action rejection
- Battlefield control
- Scoring
- Turn progression
- Cost payment
- Targeting
- Trigger timing
- Card-specific behavior
- Match replay determinism

Example scenario format:

```text
Given Player A controls one unit at Battlefield 1
And Player B controls no units at Battlefield 1
When the score step resolves
Then Player A gains 1 point
```


## Project Status

Early architecture/prototype phase.
