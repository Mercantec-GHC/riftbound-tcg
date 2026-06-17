using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Realtime;
using riftbound_tcg.Engine.RulesEngine;

namespace RiftboundTcg.Server.Api.Services;

public sealed class OnlineGameService(GameDbContext db, IRulesEngine rulesEngine, IHubContext<MatchHub> hubContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PlaceholderCardIds =
    [
        "ember-initiate",
        "glade-warden",
        "skyline-surge",
        "iron-vow",
        "ember-rune",
        "skybridge",
        "emberfield",
        "ember-legend",
        "ember-champion"
    ];

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await MigrateSchemaAsync(cancellationToken);
        await CleanupPlaceholderCardsAsync(cancellationToken);

        if (!await db.Users.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new UserEntity { Id = "user-demo-001", Email = "demo1@riftbound.local", NormalizedEmail = "DEMO1@RIFTBOUND.LOCAL", DisplayName = "Demo Player One", PasswordHash = string.Empty, CreatedAt = now, UpdatedAt = now },
                new UserEntity { Id = "user-demo-002", Email = "demo2@riftbound.local", NormalizedEmail = "DEMO2@RIFTBOUND.LOCAL", DisplayName = "Demo Player Two", PasswordHash = string.Empty, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CardDto>> ListCardsAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return (await db.Cards.OrderBy(card => card.Name).ToArrayAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<CardDto?> GetCardAsync(string cardId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Cards.FindAsync([cardId], cancellationToken) is { } card ? ToDto(card) : null;
    }

    public async Task<CardUpsertResultDto> UpsertCardAsync(CardDto request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var result = await UpsertCardCoreAsync(NormalizeCard(request), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RiftCodexImportResultDto> ImportRiftCodexAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var pagesProcessed = 0;
        var errors = new List<string>();
        const int size = 100;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            RiftCodexPage? response;
            try
            {
                response = await httpClient.GetFromJsonAsync<RiftCodexPage>($"https://api.riftcodex.com/cards?page={page}&size={size}", JsonOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Page {page}: {ex.Message}");
                break;
            }

            if (response is null)
            {
                errors.Add($"Page {page}: Empty response.");
                break;
            }

            totalPages = Math.Max(1, response.Pages);
            foreach (var item in response.Items ?? [])
            {
                var mapped = MapRiftCodexCard(item);
                if (mapped is null)
                {
                    skipped += 1;
                    continue;
                }

                var upsert = await UpsertCardCoreAsync(mapped, cancellationToken);
                if (upsert.Created)
                {
                    imported += 1;
                }
                else
                {
                    updated += 1;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            pagesProcessed += 1;
            page += 1;
        }

        return new RiftCodexImportResultDto(imported, updated, skipped, pagesProcessed, errors);
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .OrderBy(user => user.DisplayName)
            .Select(user => AuthService.ToDto(user))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .Where(user => user.Id == userId)
            .Select(user => AuthService.ToDto(user))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var email = $"{Guid.NewGuid():N}@legacy.riftbound.local";
        var user = new UserEntity
        {
            Id = $"user-{Guid.NewGuid():N}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Online Player" : request.DisplayName.Trim(),
            PasswordHash = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return AuthService.ToDto(user);
    }

    public async Task<UserDto?> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return AuthService.ToDto(user);
    }

    public async Task<IReadOnlyList<DeckDto>> ListDecksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return (await db.Decks
            .Where(deck => deck.DeletedAt == null && deck.OwnerUserId == userId)
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<SharedDeckDto>> ListPublicDecksAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null && deck.Visibility == "public")
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken);
        return await ToSharedDtosAsync(decks, cancellationToken);
    }

    public async Task<DeckDto?> GetDeckAsync(string deckId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        return deck is null || deck.DeletedAt is not null || (deck.OwnerUserId != userId && deck.Visibility != "public") ? null : ToDto(deck);
    }

    public async Task<DeckDto> CreateDeckAsync(string userId, CreateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        await ValidateDeckAsync(request.LegendId, request.ChampionId, request.BattlefieldDeckIds, request.RuneDeckIds, request.MainDeckIds, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var deck = new DeckEntity
        {
            Id = string.IsNullOrWhiteSpace(request.Name) ? $"deck-{Guid.NewGuid():N}" : $"deck-{Guid.NewGuid():N}",
            OwnerUserId = userId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Online deck" : request.Name.Trim(),
            Visibility = request.Visibility == "public" ? "public" : "private",
            Description = request.Description,
            TagsJson = Serialize(request.Tags ?? []),
            LegendId = request.LegendId,
            ChampionId = request.ChampionId,
            BattlefieldDeckIdsJson = Serialize(request.BattlefieldDeckIds),
            RuneDeckIdsJson = Serialize(request.RuneDeckIds),
            MainDeckIdsJson = Serialize(request.MainDeckIds),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Decks.Add(deck);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(deck);
    }

    public async Task<DeckDto?> UpdateDeckAsync(string deckId, string userId, UpdateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.DeletedAt is not null || deck.OwnerUserId != userId)
        {
            return null;
        }

        await ValidateDeckAsync(
            request.LegendId ?? deck.LegendId,
            request.ChampionId ?? deck.ChampionId,
            request.BattlefieldDeckIds ?? Deserialize(deck.BattlefieldDeckIdsJson),
            request.RuneDeckIds ?? Deserialize(deck.RuneDeckIdsJson),
            request.MainDeckIds ?? Deserialize(deck.MainDeckIdsJson),
            cancellationToken);

        deck.Name = request.Name ?? deck.Name;
        deck.Visibility = request.Visibility ?? deck.Visibility;
        deck.Description = request.Description ?? deck.Description;
        deck.TagsJson = request.Tags is null ? deck.TagsJson : Serialize(request.Tags);
        deck.LegendId = request.LegendId ?? deck.LegendId;
        deck.ChampionId = request.ChampionId ?? deck.ChampionId;
        deck.BattlefieldDeckIdsJson = request.BattlefieldDeckIds is null ? deck.BattlefieldDeckIdsJson : Serialize(request.BattlefieldDeckIds);
        deck.RuneDeckIdsJson = request.RuneDeckIds is null ? deck.RuneDeckIdsJson : Serialize(request.RuneDeckIds);
        deck.MainDeckIdsJson = request.MainDeckIds is null ? deck.MainDeckIdsJson : Serialize(request.MainDeckIds);
        deck.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(deck);
    }

    public async Task<bool> DeleteDeckAsync(string deckId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.OwnerUserId != userId || deck.DeletedAt is not null)
        {
            return false;
        }

        deck.DeletedAt = DateTimeOffset.UtcNow;
        deck.UpdatedAt = deck.DeletedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatchesAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var matches = await db.Matches.OrderByDescending(match => match.CreatedAt).ToArrayAsync(cancellationToken);
        var players = await db.MatchPlayers.ToArrayAsync(cancellationToken);
        return matches.Select(match => ToSummary(match, players.Where(player => player.MatchId == match.Id))).ToArray();
    }

    public async Task<MatchSnapshotDto?> GetMatchAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).OrderBy(player => player.PlayerId).ToArrayAsync(cancellationToken);
        var snapshot = await db.MatchSnapshots.Where(candidate => candidate.MatchId == matchId).OrderByDescending(candidate => candidate.SequenceNumber).FirstOrDefaultAsync(cancellationToken);
        return ToSnapshot(match, players, snapshot?.StateJson ?? "{}", snapshot?.SequenceNumber ?? match.SequenceNumber);
    }

    public async Task<MatchSnapshotDto> CreateMatchAsync(string userId, CreateMatchRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (request.Mode != "duel-1v1" || request.Players.Count != 2)
        {
            throw new InvalidOperationException("Only duel-1v1 matches with exactly two players are supported online.");
        }

        var decks = new List<DeckEntity>();
        foreach (var player in request.Players)
        {
            var deck = await db.Decks.FindAsync([player.DeckId], cancellationToken)
                ?? throw new InvalidOperationException($"Deck '{player.DeckId}' was not found.");
            if (deck.OwnerUserId != player.UserId || (player.UserId == userId && deck.OwnerUserId != userId))
            {
                throw new InvalidOperationException($"Deck '{player.DeckId}' is not owned by its seated player.");
            }
            decks.Add(deck);
        }

        return await CreateMatchFromDecksAsync(request.Mode, request.Players, decks, request.BattlefieldIds ?? [], request.FirstPlayerId ?? 0, cancellationToken);
    }

    public async Task<IReadOnlyList<LegalActionDto>?> GetLegalActionsAsync(string matchId, int playerId, CancellationToken cancellationToken)
    {
        var engineState = await LoadEngineStateAsync(matchId, cancellationToken);
        return engineState is null ? null : rulesEngine.GetLegalActions(engineState, playerId).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<MatchEventDto>?> GetMatchEventsAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (!await db.Matches.AnyAsync(match => match.Id == matchId, cancellationToken))
        {
            return null;
        }

        return (await db.MatchEvents.Where(matchEvent => matchEvent.MatchId == matchId).OrderBy(matchEvent => matchEvent.SequenceNumber).ToArrayAsync(cancellationToken))
            .Select(ToDto)
            .ToArray();
    }

    public async Task<SubmitActionResponseDto?> SubmitActionAsync(string matchId, SubmitMatchActionRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var engineState = await LoadEngineStateAsync(matchId, cancellationToken) ?? throw new InvalidOperationException("Match snapshot is missing.");
        var result = rulesEngine.ApplyAction(engineState, new EngineGameAction(request.PlayerId, request.Type, request.Payload), request.ExpectedSequenceNumber);
        var lastEventSequence = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId)
            .MaxAsync(matchEvent => (int?)matchEvent.SequenceNumber, cancellationToken) ?? match.SequenceNumber;
        var nextSequence = result.Accepted ? Math.Max(result.State.SequenceNumber, lastEventSequence + 1) : lastEventSequence + 1;
        var now = DateTimeOffset.UtcNow;
        var actionPayload = Serialize(request.Payload ?? new Dictionary<string, object?>());
        var resultPayload = Serialize(new { result.ResultMessage, result.Status });

        var entity = new MatchEventEntity
        {
            Id = $"event-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = nextSequence,
            PlayerId = request.PlayerId,
            ActionType = result.Accepted ? request.Type : "action.rejected",
            ActionPayloadJson = actionPayload,
            ResultPayloadJson = resultPayload,
            StateAfterJson = result.Accepted ? result.State.State.ToJsonString(JsonOptions) : null,
            CreatedAt = now
        };

        db.MatchEvents.Add(entity);
        if (result.Accepted)
        {
            match.SequenceNumber = result.State.SequenceNumber;
            match.Status = result.State.Stage == "game-over" ? "completed" : result.State.Stage;
            match.UpdatedAt = now;
            match.CompletedAt = result.State.Stage == "game-over" ? now : match.CompletedAt;
            match.WinnerPlayerId = result.State.State["winner"]?.GetValue<int?>();
            match.WinningTeamId = result.State.State["winningTeamId"]?.GetValue<int?>();
            if (match.CompletedAt == now)
            {
                await UpdateCompletedMatchStatsAsync(matchId, match.WinnerPlayerId, cancellationToken);
            }
            db.MatchSnapshots.Add(new MatchSnapshotEntity
            {
                Id = $"snapshot-{Guid.NewGuid():N}",
                MatchId = matchId,
                SequenceNumber = match.SequenceNumber,
                StateJson = result.State.State.ToJsonString(JsonOptions),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SubmitActionResponseDto(result.Accepted, ToDto(entity), result.State.State, result.LegalActions.Select(ToDto).ToArray());
    }

    private async Task UpdateCompletedMatchStatsAsync(string matchId, int? winnerPlayerId, CancellationToken cancellationToken)
    {
        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).ToArrayAsync(cancellationToken);
        foreach (var player in players)
        {
            var user = await db.Users.FindAsync([player.UserId], cancellationToken);
            if (user is null)
            {
                continue;
            }

            user.GamesPlayed += 1;
            if (winnerPlayerId is not null && player.PlayerId == winnerPlayerId)
            {
                user.Wins += 1;
            }
            else if (winnerPlayerId is not null)
            {
                user.Losses += 1;
            }
            user.LastPlayedAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = user.LastPlayedAt.Value;
        }
    }

    public async Task<MatchmakingTicketDto> JoinQueueAsync(string userId, JoinMatchmakingRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (request.Mode != "duel-1v1")
        {
            throw new InvalidOperationException("Only duel-1v1 matchmaking is supported.");
        }

        if (!await db.Decks.AnyAsync(deck => deck.Id == request.DeckId && deck.OwnerUserId == userId && deck.DeletedAt == null, cancellationToken))
        {
            throw new InvalidOperationException($"Deck '{request.DeckId}' was not found for the current user.");
        }

        var now = DateTimeOffset.UtcNow;
        var ticket = await db.MatchmakingTickets.FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Mode == request.Mode && candidate.Status == "queued", cancellationToken);
        if (ticket is null)
        {
            ticket = new MatchmakingTicketEntity
            {
                Id = $"ticket-{Guid.NewGuid():N}",
                UserId = userId,
                DeckId = request.DeckId,
                Mode = request.Mode,
                Status = "queued",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MatchmakingTickets.Add(ticket);
        }
        else
        {
            ticket.DeckId = request.DeckId;
            ticket.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await TryPairTicketAsync(ticket, cancellationToken);
        return ToDto(ticket);
    }

    public async Task<IReadOnlyList<QueueEntryDto>> ListQueueAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchmakingTickets
            .Where(ticket => ticket.Status == "queued")
            .OrderBy(ticket => ticket.CreatedAt)
            .Select(ticket => new QueueEntryDto(ticket.UserId, ticket.DeckId, ticket.Mode, ticket.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<MatchmakingTicketDto?> GetTicketAsync(string ticketId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        return ticket is null || ticket.UserId != userId ? null : ToDto(ticket);
    }

    public async Task<MatchmakingTicketDto?> CancelTicketAsync(string ticketId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null || ticket.UserId != userId)
        {
            return null;
        }

        ticket.Status = "cancelled";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(ticket);
    }

    public async Task LeaveQueueAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        await db.MatchmakingTickets
            .Where(ticket => ticket.UserId == userId && ticket.Status == "queued")
            .ExecuteUpdateAsync(updates => updates.SetProperty(ticket => ticket.Status, "cancelled"), cancellationToken);
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatchesForUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var matchIds = await db.MatchPlayers.Where(player => player.UserId == userId).Select(player => player.MatchId).ToArrayAsync(cancellationToken);
        var matches = await db.Matches.Where(match => matchIds.Contains(match.Id)).OrderByDescending(match => match.CreatedAt).ToArrayAsync(cancellationToken);
        var players = await db.MatchPlayers.Where(player => matchIds.Contains(player.MatchId)).ToArrayAsync(cancellationToken);
        return matches.Select(match => ToSummary(match, players.Where(player => player.MatchId == match.Id))).ToArray();
    }

    public async Task<bool> IsUserSeatedAsync(string matchId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchPlayers.AnyAsync(player => player.MatchId == matchId && player.UserId == userId, cancellationToken);
    }

    public async Task<bool> UserOwnsPlayerSeatAsync(string matchId, string userId, int playerId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchPlayers.AnyAsync(player => player.MatchId == matchId && player.UserId == userId && player.PlayerId == playerId, cancellationToken);
    }

    private async Task TryPairTicketAsync(MatchmakingTicketEntity ticket, CancellationToken cancellationToken)
    {
        var opponent = await db.MatchmakingTickets
            .Where(candidate => candidate.Id != ticket.Id && candidate.Mode == ticket.Mode && candidate.Status == "queued" && candidate.UserId != ticket.UserId)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (opponent is null)
        {
            return;
        }

        var firstDeck = await db.Decks.FindAsync([ticket.DeckId], cancellationToken);
        var secondDeck = await db.Decks.FindAsync([opponent.DeckId], cancellationToken);
        if (firstDeck is null || secondDeck is null)
        {
            return;
        }

        var players = new[]
        {
            new CreateMatchPlayerRequest(ticket.UserId, ticket.DeckId, 0),
            new CreateMatchPlayerRequest(opponent.UserId, opponent.DeckId, 1)
        };
        var match = await CreateMatchFromDecksAsync(ticket.Mode, players, [firstDeck, secondDeck], [], 0, cancellationToken);
        ticket.Status = "matched";
        ticket.MatchId = match.Id;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        opponent.Status = "matched";
        opponent.MatchId = match.Id;
        opponent.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await hubContext.Clients.Group(MatchHub.TicketGroupName(ticket.Id)).SendAsync("matchmaking.ticketUpdated", ToDto(ticket), cancellationToken);
        await hubContext.Clients.Group(MatchHub.TicketGroupName(opponent.Id)).SendAsync("matchmaking.ticketUpdated", ToDto(opponent), cancellationToken);
    }

    private async Task<MatchSnapshotDto> CreateMatchFromDecksAsync(string mode, IReadOnlyList<CreateMatchPlayerRequest> players, IReadOnlyList<DeckEntity> decks, IReadOnlyList<string> battlefieldIds, int firstPlayerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var matchId = $"match-{Guid.NewGuid():N}";
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var seats = players.Select((player, index) => new EngineSeatConfig(index, player.UserId, users.TryGetValue(player.UserId, out var user) ? user.DisplayName : player.UserId, player.TeamId)).ToArray();
        var engineDecks = decks.Select(deck => new EnginePlayerDeck(deck.Id, deck.LegendId, deck.ChampionId, Deserialize(deck.BattlefieldDeckIdsJson), Deserialize(deck.RuneDeckIdsJson), Deserialize(deck.MainDeckIdsJson))).ToArray();
        var selectedBattlefields = battlefieldIds.Count > 0 ? battlefieldIds : engineDecks.SelectMany(deck => deck.BattlefieldDeckIds).Take(2).ToArray();
        var engineState = rulesEngine.CreateInitialState(new EngineMatchConfig(matchId, mode, seats, selectedBattlefields, firstPlayerId), engineDecks, StableSeed(matchId));

        var match = new MatchEntity
        {
            Id = matchId,
            Mode = mode,
            Status = engineState.Stage,
            SequenceNumber = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Matches.Add(match);
        foreach (var seat in seats)
        {
            db.MatchPlayers.Add(new MatchPlayerEntity
            {
                MatchId = matchId,
                PlayerId = seat.PlayerId,
                UserId = seat.UserId,
                DisplayName = seat.DisplayName,
                DeckId = players[seat.PlayerId].DeckId,
                TeamId = seat.TeamId
            });
        }

        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = $"event-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = 0,
            ActionType = "match.created",
            ActionPayloadJson = "{}",
            ResultPayloadJson = "{}",
            StateAfterJson = engineState.State.ToJsonString(JsonOptions),
            CreatedAt = now
        });
        db.MatchSnapshots.Add(new MatchSnapshotEntity
        {
            Id = $"snapshot-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = 0,
            StateJson = engineState.State.ToJsonString(JsonOptions),
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(match, db.MatchPlayers.Local.Where(player => player.MatchId == matchId).ToArray(), engineState.State.ToJsonString(JsonOptions), 0);
    }

    private async Task<EngineMatchState?> LoadEngineStateAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).OrderBy(player => player.PlayerId).ToArrayAsync(cancellationToken);
        var snapshot = await db.MatchSnapshots.Where(candidate => candidate.MatchId == matchId).OrderByDescending(candidate => candidate.SequenceNumber).FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return new EngineMatchState(
            match.Id,
            match.Mode,
            match.Status,
            snapshot.SequenceNumber,
            JsonNode.Parse(snapshot.StateJson)!.AsObject(),
            players.Select(player => new EnginePlayerState(player.PlayerId, player.UserId, 0, false)).ToArray());
    }

    private async Task CleanupPlaceholderCardsAsync(CancellationToken cancellationToken)
    {
        await db.Cards
            .Where(card => PlaceholderCardIds.Contains(card.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static CardDto ToDto(CardEntity card)
    {
        return new CardDto(card.Id, card.Name, card.Kind, Deserialize(card.TagsJson), card.Domain, Deserialize(card.DomainsJson), card.Cost, card.Might, card.Text, card.Image, card.CardType, card.Supertype, new CardEffectDto(card.EffectType, card.EffectAmount));
    }

    private async Task<CardUpsertResultDto> UpsertCardCoreAsync(CardDto card, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = await db.Cards.FindAsync([card.Id], cancellationToken);
        var created = entity is null;
        if (entity is null)
        {
            entity = new CardEntity
            {
                Id = card.Id,
                CreatedAt = now
            };
            db.Cards.Add(entity);
        }

        entity.Name = card.Name;
        entity.Kind = card.Kind;
        entity.TagsJson = Serialize(card.Tags);
        entity.Domain = card.Domain;
        entity.DomainsJson = Serialize(card.Domains);
        entity.Cost = card.Cost;
        entity.Might = card.Might;
        entity.Text = card.Text;
        entity.Image = card.Image;
        entity.CardType = card.CardType;
        entity.Supertype = card.Supertype;
        entity.EffectType = card.Effect.Type;
        entity.EffectAmount = card.Effect.Amount;
        entity.UpdatedAt = now;
        return new CardUpsertResultDto(ToDto(entity), created);
    }

    private static CardDto NormalizeCard(CardDto card)
    {
        var kind = NormalizeKind(card.Kind);
        var domain = NormalizeDomain(card.Domain);
        var domains = card.Domains.Count > 0 ? card.Domains.Select(NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [domain];
        return card with
        {
            Id = card.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? card.Id.Trim() : card.Name.Trim(),
            Kind = kind,
            Tags = card.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Domain = domain,
            Domains = domains,
            Cost = Math.Max(0, card.Cost),
            Might = Math.Max(0, card.Might),
            Text = card.Text.Trim(),
            Image = string.IsNullOrWhiteSpace(card.Image) ? "*" : card.Image.Trim(),
            CardType = string.IsNullOrWhiteSpace(card.CardType) ? kind : card.CardType.Trim(),
            Effect = card.Effect with { Type = string.IsNullOrWhiteSpace(card.Effect.Type) ? "rally" : card.Effect.Type.Trim(), Amount = Math.Max(0, card.Effect.Amount) }
        };
    }

    private static CardDto? MapRiftCodexCard(RiftCodexCard item)
    {
        var id = !string.IsNullOrWhiteSpace(item.RiftboundId) ? item.RiftboundId : item.Id;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        var kind = NormalizeKind(item.Classification?.Type);
        var domains = item.Classification?.Domain?.Select(NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var domain = domains.FirstOrDefault() ?? "Fury";
        var richText = string.IsNullOrWhiteSpace(item.Text?.Rich) ? string.Empty : Regex.Replace(item.Text.Rich, "<.*?>", string.Empty);
        var plainText = !string.IsNullOrWhiteSpace(item.Text?.Plain) ? item.Text.Plain : richText;
        return new CardDto(
            id.Trim(),
            item.Name.Trim(),
            kind,
            item.Tags ?? [],
            domain,
            domains.Length > 0 ? domains : [domain],
            Math.Max(0, item.Attributes?.Energy ?? 0),
            Math.Max(0, item.Attributes?.Might ?? item.Attributes?.Power ?? 0),
            plainText ?? string.Empty,
            string.IsNullOrWhiteSpace(item.Media?.ImageUrl) ? "*" : item.Media.ImageUrl,
            string.IsNullOrWhiteSpace(item.Classification?.Type) ? kind : item.Classification.Type,
            item.Classification?.Supertype,
            new CardEffectDto("rally", 0));
    }

    private static string NormalizeKind(string? kind)
    {
        return (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "unit" => "unit",
            "spell" => "spell",
            "gear" => "gear",
            "champion" => "champion",
            "legend" => "legend",
            "battlefield" => "battlefield",
            "token" => "token",
            "rune" => "rune",
            _ => "unit"
        };
    }

    private static string NormalizeDomain(string? domain)
    {
        return (domain ?? string.Empty).Trim() switch
        {
            "Calm" => "Calm",
            "Mind" => "Mind",
            "Body" => "Body",
            "Chaos" => "Chaos",
            "Order" => "Order",
            _ => "Fury"
        };
    }

    private static DeckDto ToDto(DeckEntity deck)
    {
        return new DeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, deck.Description, Deserialize(deck.TagsJson), deck.LegendId, deck.ChampionId, Deserialize(deck.BattlefieldDeckIdsJson), Deserialize(deck.RuneDeckIdsJson), Deserialize(deck.MainDeckIdsJson), deck.CreatedAt, deck.UpdatedAt);
    }

    private async Task<IReadOnlyList<SharedDeckDto>> ToSharedDtosAsync(IReadOnlyList<DeckEntity> decks, CancellationToken cancellationToken)
    {
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var cards = await db.Cards.ToDictionaryAsync(card => card.Id, cancellationToken);
        return decks.Select(deck =>
        {
            var battlefieldIds = Deserialize(deck.BattlefieldDeckIdsJson);
            var runeIds = Deserialize(deck.RuneDeckIdsJson);
            var mainIds = Deserialize(deck.MainDeckIdsJson);
            var allCardIds = new[] { deck.LegendId, deck.ChampionId }.Concat(battlefieldIds).Concat(runeIds).Concat(mainIds);
            var deckCards = allCardIds.Select(id => cards.TryGetValue(id, out var card) ? card : null).Where(card => card is not null).Cast<CardEntity>().ToArray();
            var tags = deckCards.SelectMany(card => Deserialize(card.TagsJson)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToArray();
            var domains = deckCards.SelectMany(card => Deserialize(card.DomainsJson).Length > 0 ? Deserialize(card.DomainsJson) : [card.Domain]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(domain => domain).ToArray();
            var legendName = cards.TryGetValue(deck.LegendId, out var legend) ? legend.Name : string.Empty;
            var championName = cards.TryGetValue(deck.ChampionId, out var champion) ? champion.Name : string.Empty;
            var author = users.TryGetValue(deck.OwnerUserId, out var owner) ? owner.DisplayName : deck.OwnerUserId;
            return new SharedDeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, author, deck.LegendId, deck.ChampionId, battlefieldIds, runeIds, mainIds, tags, domains, legendName, championName, new DeckCardCountsDto(mainIds.Length, runeIds.Length, battlefieldIds.Length), deck.Description, deck.UpdatedAt);
        }).ToArray();
    }

    private static MatchSummaryDto ToSummary(MatchEntity match, IEnumerable<MatchPlayerEntity> players)
    {
        return new MatchSummaryDto(match.Id, match.Mode, match.Status, players.OrderBy(player => player.PlayerId).Select(ToDto).ToArray(), match.CreatedAt, match.UpdatedAt, match.CompletedAt, match.WinnerPlayerId, match.WinningTeamId);
    }

    private static MatchSnapshotDto ToSnapshot(MatchEntity match, IEnumerable<MatchPlayerEntity> players, string stateJson, int sequenceNumber)
    {
        return new MatchSnapshotDto(match.Id, match.Mode, match.Status, players.OrderBy(player => player.PlayerId).Select(ToDto).ToArray(), match.CreatedAt, match.UpdatedAt, match.CompletedAt, match.WinnerPlayerId, match.WinningTeamId, JsonNode.Parse(stateJson)!, sequenceNumber);
    }

    private static MatchPlayerDto ToDto(MatchPlayerEntity player)
    {
        return new MatchPlayerDto(player.PlayerId, player.UserId, player.DisplayName, player.DeckId, player.TeamId);
    }

    private static LegalActionDto ToDto(EngineLegalAction action)
    {
        return new LegalActionDto(action.Id, action.Type, action.PlayerId, action.Label, null, [], action.PayloadSchema?.Deserialize<IReadOnlyDictionary<string, object?>>(JsonOptions));
    }

    private static MatchEventDto ToDto(MatchEventEntity matchEvent)
    {
        return new MatchEventDto(
            matchEvent.Id,
            matchEvent.MatchId,
            matchEvent.SequenceNumber,
            matchEvent.PlayerId,
            matchEvent.ActionType,
            JsonNode.Parse(matchEvent.ActionPayloadJson) ?? new JsonObject(),
            JsonNode.Parse(matchEvent.ResultPayloadJson) ?? new JsonObject(),
            matchEvent.CreatedAt);
    }

    private static MatchmakingTicketDto ToDto(MatchmakingTicketEntity ticket)
    {
        return new MatchmakingTicketDto(ticket.Id, ticket.UserId, ticket.DeckId, ticket.Mode, ticket.Status, ticket.CreatedAt, ticket.MatchId);
    }

    private async Task MigrateSchemaAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "NormalizedEmail" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsAdmin" boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastLoginAt" timestamptz NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "GamesPlayed" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Wins" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Losses" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PointsScored" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastPlayedAt" timestamptz NULL;
            UPDATE users SET "Email" = lower("Id") || '@legacy.riftbound.local' WHERE "Email" = '';
            UPDATE users SET "NormalizedEmail" = upper("Email") WHERE "NormalizedEmail" = '';

            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "Description" text NULL;
            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "DeletedAt" timestamptz NULL;

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

            CREATE TABLE IF NOT EXISTS refresh_tokens (
                "Id" text PRIMARY KEY,
                "UserId" text NOT NULL,
                "TokenHash" text NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                "RevokedAt" timestamptz NULL,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_NormalizedEmail" ON users("NormalizedEmail");
            CREATE INDEX IF NOT EXISTS "IX_refresh_tokens_UserId" ON refresh_tokens("UserId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_refresh_tokens_TokenHash" ON refresh_tokens("TokenHash");
            CREATE INDEX IF NOT EXISTS "IX_decks_DeletedAt" ON decks("DeletedAt");
            """, cancellationToken);
    }

    private async Task ValidateDeckAsync(string legendId, string championId, IReadOnlyList<string> battlefieldDeckIds, IReadOnlyList<string> runeDeckIds, IReadOnlyList<string> mainDeckIds, CancellationToken cancellationToken)
    {
        var allIds = new[] { legendId, championId }.Concat(battlefieldDeckIds).Concat(runeDeckIds).Concat(mainDeckIds).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cards = await db.Cards.Where(card => allIds.Contains(card.Id)).ToDictionaryAsync(card => card.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (string.IsNullOrWhiteSpace(legendId) || !cards.TryGetValue(legendId, out var legend) || legend.Kind != "legend")
        {
            throw new InvalidOperationException("Deck must include a valid legend.");
        }

        if (string.IsNullOrWhiteSpace(championId) || !cards.TryGetValue(championId, out var champion) || champion.Kind != "champion")
        {
            throw new InvalidOperationException("Deck must include a valid champion.");
        }

        if (battlefieldDeckIds.Count is < 1 or > 3 || battlefieldDeckIds.Any(id => !cards.TryGetValue(id, out var card) || card.Kind != "battlefield"))
        {
            throw new InvalidOperationException("Battlefield deck must contain 1 to 3 valid battlefield cards.");
        }

        if (runeDeckIds.Count < 1 || runeDeckIds.Any(id => !cards.TryGetValue(id, out var card) || card.Kind != "rune"))
        {
            throw new InvalidOperationException("Rune deck must contain valid rune cards.");
        }

        if (mainDeckIds.Count > 40 || mainDeckIds.Any(id => !cards.TryGetValue(id, out var card) || card.Kind is "legend" or "champion" or "battlefield" or "rune" or "token"))
        {
            throw new InvalidOperationException("Main deck contains invalid cards.");
        }
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string[] Deserialize(string json)
    {
        return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
    }

    private static int StableSeed(string value)
    {
        var hash = 17;
        foreach (var character in value)
        {
            hash = unchecked(hash * 31 + character);
        }

        return Math.Abs(hash);
    }

    private sealed record RiftCodexPage(
        RiftCodexCard[]? Items,
        int Total,
        int Page,
        int Size,
        int Pages);

    private sealed record RiftCodexCard(
        string Id,
        string Name,
        [property: JsonPropertyName("riftbound_id")] string? RiftboundId,
        [property: JsonPropertyName("tcgplayer_id")] string? TcgplayerId,
        [property: JsonPropertyName("collector_number")] int? CollectorNumber,
        RiftCodexAttributes? Attributes,
        RiftCodexClassification? Classification,
        RiftCodexText? Text,
        RiftCodexSet? Set,
        RiftCodexMedia? Media,
        string[]? Tags,
        string? Orientation,
        RiftCodexMetadata? Metadata);

    private sealed record RiftCodexAttributes(int? Energy, int? Might, int? Power);

    private sealed record RiftCodexClassification(string? Type, string? Supertype, string? Rarity, string[]? Domain);

    private sealed record RiftCodexText(string? Rich, string? Plain, string? Flavour);

    private sealed record RiftCodexSet([property: JsonPropertyName("set_id")] string? SetId, string? Label);

    private sealed record RiftCodexMedia([property: JsonPropertyName("image_url")] string? ImageUrl, string? Artist, [property: JsonPropertyName("accessibility_text")] string? AccessibilityText);

    private sealed record RiftCodexMetadata([property: JsonPropertyName("clean_name")] string? CleanName, [property: JsonPropertyName("updated_on")] string? UpdatedOn, [property: JsonPropertyName("alternate_art")] bool? AlternateArt, bool? Overnumbered, bool? Signature);
}
