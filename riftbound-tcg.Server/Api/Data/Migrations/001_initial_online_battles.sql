CREATE TABLE IF NOT EXISTS cards (
    "Id" text PRIMARY KEY,
    "Name" text NOT NULL,
    "Kind" text NOT NULL,
    "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "Domain" text NOT NULL,
    "DomainsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "Cost" integer NOT NULL,
    "Might" integer NOT NULL,
    "Text" text NOT NULL,
    "Image" text NOT NULL,
    "CardType" text NOT NULL,
    "Supertype" text NULL,
    "EffectType" text NOT NULL,
    "EffectAmount" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_cards_Kind" ON cards("Kind");
CREATE INDEX IF NOT EXISTS "IX_cards_Domain" ON cards("Domain");

DELETE FROM cards
WHERE "Id" IN (
    'ember-initiate',
    'glade-warden',
    'skyline-surge',
    'iron-vow',
    'ember-rune',
    'skybridge',
    'emberfield',
    'ember-legend',
    'ember-champion'
);

CREATE TABLE IF NOT EXISTS users (
    "Id" text PRIMARY KEY,
    "Email" text NOT NULL,
    "NormalizedEmail" text NOT NULL,
    "DisplayName" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "IsAdmin" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL,
    "LastLoginAt" timestamptz NULL,
    "AvatarImageHash" text NULL,
    "GamesPlayed" integer NOT NULL DEFAULT 0,
    "Wins" integer NOT NULL DEFAULT 0,
    "Losses" integer NOT NULL DEFAULT 0,
    "PointsScored" integer NOT NULL DEFAULT 0,
    "LastPlayedAt" timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_NormalizedEmail" ON users("NormalizedEmail");

CREATE TABLE IF NOT EXISTS refresh_tokens (
    "Id" text PRIMARY KEY,
    "UserId" text NOT NULL,
    "TokenHash" text NOT NULL,
    "ExpiresAt" timestamptz NOT NULL,
    "RevokedAt" timestamptz NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_refresh_tokens_UserId" ON refresh_tokens("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_refresh_tokens_TokenHash" ON refresh_tokens("TokenHash");

CREATE TABLE IF NOT EXISTS profile_images (
    "Hash" text PRIMARY KEY,
    "ContentType" text NOT NULL,
    "Bytes" bytea NOT NULL,
    "Length" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS decks (
    "Id" text PRIMARY KEY,
    "OwnerUserId" text NOT NULL,
    "Name" text NOT NULL,
    "Visibility" text NOT NULL,
    "Description" text NULL,
    "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "LegendId" text NOT NULL,
    "ChampionId" text NOT NULL,
    "BattlefieldDeckIdsJson" jsonb NOT NULL,
    "RuneDeckIdsJson" jsonb NOT NULL,
    "MainDeckIdsJson" jsonb NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL,
    "DeletedAt" timestamptz NULL
);

CREATE INDEX IF NOT EXISTS "IX_decks_OwnerUserId" ON decks("OwnerUserId");
CREATE INDEX IF NOT EXISTS "IX_decks_DeletedAt" ON decks("DeletedAt");

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
