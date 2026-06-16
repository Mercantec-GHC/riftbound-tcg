CREATE TABLE IF NOT EXISTS users (
    "Id" text PRIMARY KEY,
    "DisplayName" text NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS decks (
    "Id" text PRIMARY KEY,
    "OwnerUserId" text NOT NULL,
    "Name" text NOT NULL,
    "Visibility" text NOT NULL,
    "LegendId" text NOT NULL,
    "ChampionId" text NOT NULL,
    "BattlefieldDeckIdsJson" jsonb NOT NULL,
    "RuneDeckIdsJson" jsonb NOT NULL,
    "MainDeckIdsJson" jsonb NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_decks_OwnerUserId" ON decks("OwnerUserId");

CREATE TABLE IF NOT EXISTS matches (
    "Id" text PRIMARY KEY,
    "Mode" text NOT NULL,
    "Status" text NOT NULL,
    "SequenceNumber" integer NOT NULL,
    "WinnerPlayerId" integer NULL,
    "WinningTeamId" integer NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL,
    "CompletedAt" timestamptz NULL
);

CREATE INDEX IF NOT EXISTS "IX_matches_Status" ON matches("Status");

CREATE TABLE IF NOT EXISTS match_players (
    "MatchId" text NOT NULL,
    "PlayerId" integer NOT NULL,
    "UserId" text NOT NULL,
    "DisplayName" text NOT NULL,
    "DeckId" text NOT NULL,
    "TeamId" integer NULL,
    PRIMARY KEY ("MatchId", "PlayerId")
);

CREATE INDEX IF NOT EXISTS "IX_match_players_UserId" ON match_players("UserId");
CREATE INDEX IF NOT EXISTS "IX_match_players_DeckId" ON match_players("DeckId");

CREATE TABLE IF NOT EXISTS match_events (
    "Id" text PRIMARY KEY,
    "MatchId" text NOT NULL,
    "SequenceNumber" integer NOT NULL,
    "PlayerId" integer NULL,
    "ActionType" text NOT NULL,
    "ActionPayloadJson" jsonb NOT NULL,
    "ResultPayloadJson" jsonb NOT NULL,
    "StateAfterJson" jsonb NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_match_events_MatchId_SequenceNumber" ON match_events("MatchId", "SequenceNumber");

CREATE TABLE IF NOT EXISTS match_snapshots (
    "Id" text PRIMARY KEY,
    "MatchId" text NOT NULL,
    "SequenceNumber" integer NOT NULL,
    "StateJson" jsonb NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_match_snapshots_MatchId_SequenceNumber" ON match_snapshots("MatchId", "SequenceNumber");

CREATE TABLE IF NOT EXISTS matchmaking_tickets (
    "Id" text PRIMARY KEY,
    "UserId" text NOT NULL,
    "DeckId" text NOT NULL,
    "Mode" text NOT NULL,
    "Status" text NOT NULL,
    "MatchId" text NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_matchmaking_tickets_UserId_Mode" ON matchmaking_tickets("UserId", "Mode");
CREATE INDEX IF NOT EXISTS "IX_matchmaking_tickets_Status" ON matchmaking_tickets("Status");
