using System.Collections.Concurrent;
using RiftboundTcg.Server.Api.Models;
using riftbound_tcg.Engine.RulesEngine;

namespace RiftboundTcg.Server.Api.Services;

public sealed class PlaceholderGameStore
{
    private readonly IRulesEngine _rulesEngine;
    private readonly ConcurrentDictionary<string, DeckDto> _decks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MatchDto> _matches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<MatchEventDto>> _matchEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, QueueEntryDto> _queue = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UserDto> _users = new(StringComparer.OrdinalIgnoreCase);

    public PlaceholderGameStore(IRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;

        foreach (var deck in SeedDecks)
        {
            _decks[deck.Id] = deck;
        }

        foreach (var user in SeedUsers)
        {
            _users[user.Id] = user;
        }

        var match = new MatchDto("match-demo-001", ["user-demo-001", "user-demo-002"], "mulligan", DateTimeOffset.UtcNow);
        _matches[match.Id] = match;
        _matchEvents[match.Id] =
        [
            new MatchEventDto("event-demo-001", match.Id, 1, null, "match.created", new { match.PlayerIds }, match.CreatedAt)
        ];
    }

    public IReadOnlyList<CardDto> Cards { get; } =
    [
        new("ember-initiate", "Ember Initiate", "Unit", 1, "When deployed, ready one battlefield slot."),
        new("glade-warden", "Glade Warden", "Unit", 2, "Guard a friendly battlefield until your next turn."),
        new("skyline-surge", "Skyline Surge", "Spell", 3, "Move a unit, then draw a card."),
        new("iron-vow", "Iron Vow", "Tactic", 2, "A friendly unit gets +2 power this combat.")
    ];

    public IReadOnlyList<UserDto> Users => _users.Values.OrderBy(user => user.DisplayName).ToArray();

    private static IReadOnlyList<UserDto> SeedUsers =>
    [
        new("user-demo-001", "Demo Player One"),
        new("user-demo-002", "Demo Player Two")
    ];

    public IReadOnlyList<DeckDto> Decks => _decks.Values.OrderBy(deck => deck.Name).ToArray();

    public IReadOnlyList<MatchDto> Matches => _matches.Values.OrderByDescending(match => match.CreatedAt).ToArray();

    public IReadOnlyList<QueueEntryDto> Queue => _queue.Values.OrderBy(entry => entry.JoinedAt).ToArray();

    private static IReadOnlyList<DeckDto> SeedDecks =>
    [
        new("deck-demo-001", "user-demo-001", "Ember Trial", ["ember-initiate", "skyline-surge", "iron-vow"]),
        new("deck-demo-002", "user-demo-002", "Glade Trial", ["glade-warden", "skyline-surge", "iron-vow"])
    ];

    public DeckDto CreateDeck(CreateDeckRequest request)
    {
        var deck = new DeckDto(
            $"deck-{Guid.NewGuid():N}",
            request.OwnerUserId,
            request.Name,
            request.CardIds);

        _decks[deck.Id] = deck;
        return deck;
    }

    public DeckDto? UpdateDeck(string deckId, UpdateDeckRequest request)
    {
        if (!_decks.TryGetValue(deckId, out var current))
        {
            return null;
        }

        var updated = current with
        {
            Name = request.Name ?? current.Name,
            CardIds = request.CardIds ?? current.CardIds
        };
        _decks[deckId] = updated;
        return updated;
    }

    public bool DeleteDeck(string deckId)
    {
        return _decks.TryRemove(deckId, out _);
    }

    public UserDto CreateUser(CreateUserRequest request)
    {
        var user = new UserDto($"user-{Guid.NewGuid():N}", request.DisplayName.Trim());
        _users[user.Id] = user;
        return user;
    }

    public UserDto? UpdateUser(string userId, UpdateUserRequest request)
    {
        if (!_users.TryGetValue(userId, out var current))
        {
            return null;
        }

        var updated = current with { DisplayName = request.DisplayName ?? current.DisplayName };
        _users[userId] = updated;
        return updated;
    }

    public MatchDto CreateMatch(CreateMatchRequest request)
    {
        var match = new MatchDto(
            $"match-{Guid.NewGuid():N}",
            request.PlayerIds,
            "configuring",
            DateTimeOffset.UtcNow);

        _matches[match.Id] = match;
        _matchEvents[match.Id] =
        [
            new MatchEventDto($"event-{Guid.NewGuid():N}", match.Id, 1, null, "match.created", new { match.PlayerIds }, match.CreatedAt)
        ];

        return match;
    }

    public MatchSnapshotDto GetSnapshot(MatchDto match)
    {
        var sequenceNumber = GetMatchEvents(match.Id).Count;
        return new MatchSnapshotDto(match, CreatePlaceholderState(match, sequenceNumber), sequenceNumber);
    }

    public IReadOnlyList<LegalActionDto> GetLegalActions(string matchId, int playerId)
    {
        if (!_matches.TryGetValue(matchId, out var match))
        {
            return [];
        }

        return _rulesEngine
            .GetLegalActions(CreateEngineState(match, GetMatchEvents(matchId).Count), playerId)
            .Select(action => new LegalActionDto(action.Id, action.Type, action.Label, action.PlayerId, []))
            .ToArray();
    }

    public IReadOnlyList<MatchEventDto> GetMatchEvents(string matchId)
    {
        return _matchEvents.TryGetValue(matchId, out var events)
            ? events.OrderBy(matchEvent => matchEvent.SequenceNumber).ToArray()
            : [];
    }

    public SubmitActionResponseDto SubmitAction(string matchId, SubmitMatchActionRequest request)
    {
        if (!_matches.TryGetValue(matchId, out var match))
        {
            var missingState = new EngineMatchState(matchId, "missing", 0, []);
            var rejected = new MatchEventDto(
                $"event-{Guid.NewGuid():N}",
                matchId,
                0,
                request.PlayerId,
                "action.rejected",
                new { request.ActionType, reason = "match-not-found" },
                DateTimeOffset.UtcNow);
            return new SubmitActionResponseDto(false, new MatchActionReceiptDto(matchId, 0, "rejected"), rejected, missingState, []);
        }

        var events = _matchEvents.GetOrAdd(matchId, _ => []);
        MatchEventDto matchEvent;
        EngineActionResult engineResult;

        lock (events)
        {
            var engineState = CreateEngineState(match, events.Count);
            engineResult = _rulesEngine.ApplyAction(engineState, new EngineGameAction(request.PlayerId, request.ActionType, request.Payload));
            var sequenceNumber = events.Count + 1;
            matchEvent = new MatchEventDto(
                $"event-{Guid.NewGuid():N}",
                matchId,
                sequenceNumber,
                request.PlayerId,
                engineResult.Accepted ? request.ActionType : "action.rejected",
                new { request.Payload, engineResult.ResultMessage },
                DateTimeOffset.UtcNow);
            events.Add(matchEvent);
        }

        var receipt = new MatchActionReceiptDto(matchId, matchEvent.SequenceNumber, engineResult.Status);
        return new SubmitActionResponseDto(
            engineResult.Accepted,
            receipt,
            matchEvent,
            CreatePlaceholderState(match, matchEvent.SequenceNumber),
            engineResult.LegalActions.Select(action => new LegalActionDto(action.Id, action.Type, action.Label, action.PlayerId, [])).ToArray());
    }

    public MatchmakingTicketDto JoinQueue(JoinMatchmakingRequest request)
    {
        var entry = new QueueEntryDto(request.UserId, request.DeckId, DateTimeOffset.UtcNow);
        _queue[entry.UserId] = entry;
        return new MatchmakingTicketDto(
            $"ticket-{Guid.NewGuid():N}",
            request.UserId,
            request.DeckId,
            request.Mode ?? "duel-1v1",
            "queued",
            entry.JoinedAt,
            null);
    }

    public void LeaveQueue(string userId)
    {
        _queue.TryRemove(userId, out _);
    }

    private static object CreatePlaceholderState(MatchDto match, int sequenceNumber)
    {
        return new
        {
            matchId = match.Id,
            version = sequenceNumber,
            stage = match.Status,
            players = match.PlayerIds.Select((userId, index) => new
            {
                playerId = index,
                userId,
                points = 0,
                connected = false
            }).ToArray(),
            battlefields = Array.Empty<object>(),
            message = "Placeholder state until riftbound-tcg.Engine owns deterministic match resolution."
        };
    }

    private static EngineMatchState CreateEngineState(MatchDto match, int sequenceNumber)
    {
        return new EngineMatchState(
            match.Id,
            match.Status,
            sequenceNumber,
            match.PlayerIds.Select((userId, index) => new EnginePlayerState(index, userId, 0, false)).ToArray());
    }
}
