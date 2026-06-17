using Microsoft.EntityFrameworkCore;

namespace RiftboundTcg.Server.Api.Data;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<DeckEntity> Decks => Set<DeckEntity>();
    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchPlayerEntity> MatchPlayers => Set<MatchPlayerEntity>();
    public DbSet<MatchEventEntity> MatchEvents => Set<MatchEventEntity>();
    public DbSet<MatchSnapshotEntity> MatchSnapshots => Set<MatchSnapshotEntity>();
    public DbSet<MatchmakingTicketEntity> MatchmakingTickets => Set<MatchmakingTicketEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<DeckEntity>(entity =>
        {
            entity.ToTable("decks");
            entity.HasKey(deck => deck.Id);
            entity.HasIndex(deck => deck.OwnerUserId);
            entity.HasIndex(deck => deck.DeletedAt);
            entity.Property(deck => deck.TagsJson).HasColumnType("jsonb");
            entity.Property(deck => deck.BattlefieldDeckIdsJson).HasColumnType("jsonb");
            entity.Property(deck => deck.RuneDeckIdsJson).HasColumnType("jsonb");
            entity.Property(deck => deck.MainDeckIdsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MatchEntity>(entity =>
        {
            entity.ToTable("matches");
            entity.HasKey(match => match.Id);
            entity.HasIndex(match => match.Status);
        });

        modelBuilder.Entity<MatchPlayerEntity>(entity =>
        {
            entity.ToTable("match_players");
            entity.HasKey(player => new { player.MatchId, player.PlayerId });
            entity.HasIndex(player => player.UserId);
            entity.HasIndex(player => player.DeckId);
        });

        modelBuilder.Entity<MatchEventEntity>(entity =>
        {
            entity.ToTable("match_events");
            entity.HasKey(matchEvent => matchEvent.Id);
            entity.HasIndex(matchEvent => new { matchEvent.MatchId, matchEvent.SequenceNumber }).IsUnique();
            entity.Property(matchEvent => matchEvent.ActionPayloadJson).HasColumnType("jsonb");
            entity.Property(matchEvent => matchEvent.ResultPayloadJson).HasColumnType("jsonb");
            entity.Property(matchEvent => matchEvent.StateAfterJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MatchSnapshotEntity>(entity =>
        {
            entity.ToTable("match_snapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.HasIndex(snapshot => new { snapshot.MatchId, snapshot.SequenceNumber }).IsUnique();
            entity.Property(snapshot => snapshot.StateJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MatchmakingTicketEntity>(entity =>
        {
            entity.ToTable("matchmaking_tickets");
            entity.HasKey(ticket => ticket.Id);
            entity.HasIndex(ticket => new { ticket.UserId, ticket.Mode });
            entity.HasIndex(ticket => ticket.Status);
        });

        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(token => token.Id);
            entity.HasIndex(token => token.UserId);
            entity.HasIndex(token => token.TokenHash).IsUnique();
        });
    }
}

public sealed class UserEntity
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PointsScored { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
}

public sealed class DeckEntity
{
    public string Id { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Visibility { get; set; } = "private";
    public string? Description { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string LegendId { get; set; } = string.Empty;
    public string ChampionId { get; set; } = string.Empty;
    public string BattlefieldDeckIdsJson { get; set; } = "[]";
    public string RuneDeckIdsJson { get; set; } = "[]";
    public string MainDeckIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class MatchEntity
{
    public string Id { get; set; } = string.Empty;
    public string Mode { get; set; } = "duel-1v1";
    public string Status { get; set; } = "mulligan";
    public int SequenceNumber { get; set; }
    public int? WinnerPlayerId { get; set; }
    public int? WinningTeamId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class MatchPlayerEntity
{
    public string MatchId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DeckId { get; set; } = string.Empty;
    public int? TeamId { get; set; }
}

public sealed class MatchEventEntity
{
    public string Id { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public int? PlayerId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string ActionPayloadJson { get; set; } = "{}";
    public string ResultPayloadJson { get; set; } = "{}";
    public string? StateAfterJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatchSnapshotEntity
{
    public string Id { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public string StateJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatchmakingTicketEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DeckId { get; set; } = string.Empty;
    public string Mode { get; set; } = "duel-1v1";
    public string Status { get; set; } = "queued";
    public string? MatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RefreshTokenEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
