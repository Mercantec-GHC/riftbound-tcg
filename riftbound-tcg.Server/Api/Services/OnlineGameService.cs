using System.Text.Json;
using System.Text.Json.Nodes;
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

    public IReadOnlyList<CardDto> Cards { get; } =
    [
        Card("ember-initiate", "Ember Initiate", "unit", 1, 2, "When deployed, ready one battlefield slot."),
        Card("glade-warden", "Glade Warden", "unit", 2, 3, "Guard a friendly battlefield until your next turn."),
        Card("skyline-surge", "Skyline Surge", "spell", 3, 0, "Move a unit, then draw a card."),
        Card("iron-vow", "Iron Vow", "gear", 2, 1, "A friendly unit gets +2 power this combat."),
        Card("ember-rune", "Ember Rune", "rune", 0, 0, "Provides Fury energy."),
        Card("skybridge", "Skybridge Spire", "battlefield", 2, 0, "A contested bridge above the rift."),
        Card("emberfield", "Emberfield Crossing", "battlefield", 3, 0, "A hotly contested crossing."),
        Card("ember-legend", "Ember Legend", "legend", 0, 0, "Placeholder legend."),
        Card("ember-champion", "Ember Champion", "champion", 3, 4, "Placeholder champion.")
    ];

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (!await db.Users.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new UserEntity { Id = "user-demo-001", DisplayName = "Demo Player One", CreatedAt = now },
                new UserEntity { Id = "user-demo-002", DisplayName = "Demo Player Two", CreatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .OrderBy(user => user.DisplayName)
            .Select(user => new UserDto(user.Id, user.DisplayName))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .Where(user => user.Id == userId)
            .Select(user => new UserDto(user.Id, user.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var user = new UserEntity
        {
            Id = $"user-{Guid.NewGuid():N}",
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Online Player" : request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return new UserDto(user.Id, user.DisplayName);
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
        }

        await db.SaveChangesAsync(cancellationToken);
        return new UserDto(user.Id, user.DisplayName);
    }

    public async Task<IReadOnlyList<DeckDto>> ListDecksAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return (await db.Decks.OrderBy(deck => deck.Name).ToArrayAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<DeckDto?> GetDeckAsync(string deckId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        return deck is null ? null : ToDto(deck);
    }

    public async Task<DeckDto> CreateDeckAsync(CreateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (!await db.Users.AnyAsync(user => user.Id == request.OwnerUserId, cancellationToken))
        {
            db.Users.Add(new UserEntity { Id = request.OwnerUserId, DisplayName = request.OwnerUserId, CreatedAt = DateTimeOffset.UtcNow });
        }

        var now = DateTimeOffset.UtcNow;
        var deck = new DeckEntity
        {
            Id = string.IsNullOrWhiteSpace(request.Name) ? $"deck-{Guid.NewGuid():N}" : $"deck-{Guid.NewGuid():N}",
            OwnerUserId = request.OwnerUserId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Online deck" : request.Name.Trim(),
            Visibility = request.Visibility == "public" ? "public" : "private",
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

    public async Task<DeckDto?> UpdateDeckAsync(string deckId, UpdateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null)
        {
            return null;
        }

        deck.Name = request.Name ?? deck.Name;
        deck.Visibility = request.Visibility ?? deck.Visibility;
        deck.LegendId = request.LegendId ?? deck.LegendId;
        deck.ChampionId = request.ChampionId ?? deck.ChampionId;
        deck.BattlefieldDeckIdsJson = request.BattlefieldDeckIds is null ? deck.BattlefieldDeckIdsJson : Serialize(request.BattlefieldDeckIds);
        deck.RuneDeckIdsJson = request.RuneDeckIds is null ? deck.RuneDeckIdsJson : Serialize(request.RuneDeckIds);
        deck.MainDeckIdsJson = request.MainDeckIds is null ? deck.MainDeckIdsJson : Serialize(request.MainDeckIds);
        deck.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(deck);
    }

    public async Task<bool> DeleteDeckAsync(string deckId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var count = await db.Decks.Where(deck => deck.Id == deckId).ExecuteDeleteAsync(cancellationToken);
        return count > 0;
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

    public async Task<MatchSnapshotDto> CreateMatchAsync(CreateMatchRequest request, CancellationToken cancellationToken)
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
        var nextSequence = result.Accepted ? result.State.SequenceNumber : match.SequenceNumber + 1;
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
            db.MatchSnapshots.Add(new MatchSnapshotEntity
            {
                Id = $"snapshot-{Guid.NewGuid():N}",
                MatchId = matchId,
                SequenceNumber = result.State.SequenceNumber,
                StateJson = result.State.State.ToJsonString(JsonOptions),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SubmitActionResponseDto(result.Accepted, ToDto(entity), result.State.State, result.LegalActions.Select(ToDto).ToArray());
    }

    public async Task<MatchmakingTicketDto> JoinQueueAsync(JoinMatchmakingRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (request.Mode != "duel-1v1")
        {
            throw new InvalidOperationException("Only duel-1v1 matchmaking is supported.");
        }

        if (!await db.Decks.AnyAsync(deck => deck.Id == request.DeckId, cancellationToken))
        {
            throw new InvalidOperationException($"Deck '{request.DeckId}' was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var ticket = await db.MatchmakingTickets.FirstOrDefaultAsync(candidate => candidate.UserId == request.UserId && candidate.Mode == request.Mode && candidate.Status == "queued", cancellationToken);
        if (ticket is null)
        {
            ticket = new MatchmakingTicketEntity
            {
                Id = $"ticket-{Guid.NewGuid():N}",
                UserId = request.UserId,
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

    public async Task<MatchmakingTicketDto?> GetTicketAsync(string ticketId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        return ticket is null ? null : ToDto(ticket);
    }

    public async Task<MatchmakingTicketDto?> CancelTicketAsync(string ticketId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null)
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

    public async Task<bool> IsUserSeatedAsync(string matchId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchPlayers.AnyAsync(player => player.MatchId == matchId && player.UserId == userId, cancellationToken);
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

    private static CardDto Card(string id, string name, string kind, int cost, int might, string text)
    {
        return new CardDto(id, name, kind, [], "Fury", ["Fury"], cost, might, text, "*", kind, null, new CardEffectDto("rally", 0));
    }

    private static DeckDto ToDto(DeckEntity deck)
    {
        return new DeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, deck.LegendId, deck.ChampionId, Deserialize(deck.BattlefieldDeckIdsJson), Deserialize(deck.RuneDeckIdsJson), Deserialize(deck.MainDeckIdsJson));
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
}
