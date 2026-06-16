using Microsoft.AspNetCore.Http.HttpResults;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Services;

namespace RiftboundTcg.Server.Api;

public static class GameApiV1
{
    public static IEndpointRouteBuilder MapGameApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1")
            .WithTags("Riftbound API v1");

        MapCards(api);
        MapDecks(api);
        MapUsers(api);
        MapMatches(api);
        MapMatchmaking(api);

        return app;
    }

    private static void MapCards(RouteGroupBuilder api)
    {
        var cards = api.MapGroup("/cards")
            .WithTags("Cards");

        cards.MapGet("/", (PlaceholderGameStore store) => Ok(store.Cards))
            .WithName("ListCards")
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(30)));

        cards.MapGet("/{cardId}", Results<Ok<ApiResult<CardDto>>, NotFound> (string cardId, PlaceholderGameStore store) =>
            store.Cards.FirstOrDefault(card => card.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase)) is { } card
                ? TypedResults.Ok(Envelope(card))
                : TypedResults.NotFound())
            .WithName("GetCard");
    }

    private static void MapDecks(RouteGroupBuilder api)
    {
        var decks = api.MapGroup("/decks")
            .WithTags("Decks");

        decks.MapGet("/", (PlaceholderGameStore store) => Ok(store.Decks))
            .WithName("ListDecks");

        decks.MapGet("/public", (PlaceholderGameStore store) => Ok(store.Decks))
            .WithName("ListPublicDecks");

        decks.MapGet("/{deckId}", Results<Ok<ApiResult<DeckDto>>, NotFound> (string deckId, PlaceholderGameStore store) =>
            store.Decks.FirstOrDefault(deck => deck.Id.Equals(deckId, StringComparison.OrdinalIgnoreCase)) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound())
            .WithName("GetDeck");

        decks.MapPost("/", (CreateDeckRequest request, PlaceholderGameStore store) =>
        {
            var deck = store.CreateDeck(request);
            return TypedResults.Created($"/api/v1/decks/{deck.Id}", Envelope(deck));
        })
        .WithName("CreateDeck");

        decks.MapPatch("/{deckId}", Results<Ok<ApiResult<DeckDto>>, NotFound> (string deckId, UpdateDeckRequest request, PlaceholderGameStore store) =>
            store.UpdateDeck(deckId, request) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound())
            .WithName("UpdateDeck");

        decks.MapDelete("/{deckId}", Results<NoContent, NotFound> (string deckId, PlaceholderGameStore store) =>
            store.DeleteDeck(deckId)
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
            .WithName("DeleteDeck");
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users")
            .WithTags("Users");

        users.MapGet("/", (PlaceholderGameStore store) => Ok(store.Users))
            .WithName("ListUsers");

        users.MapGet("/{userId}", Results<Ok<ApiResult<UserDto>>, NotFound> (string userId, PlaceholderGameStore store) =>
            store.Users.FirstOrDefault(user => user.Id.Equals(userId, StringComparison.OrdinalIgnoreCase)) is { } user
                ? TypedResults.Ok(Envelope(user))
                : TypedResults.NotFound())
            .WithName("GetUser");

        users.MapPost("/", (CreateUserRequest request, PlaceholderGameStore store) =>
        {
            var user = store.CreateUser(request);
            return TypedResults.Created($"/api/v1/users/{user.Id}", Envelope(user));
        })
        .WithName("CreateUser");

        users.MapPatch("/{userId}", Results<Ok<ApiResult<UserDto>>, NotFound> (string userId, UpdateUserRequest request, PlaceholderGameStore store) =>
            store.UpdateUser(userId, request) is { } user
                ? TypedResults.Ok(Envelope(user))
                : TypedResults.NotFound())
            .WithName("UpdateUser");
    }

    private static void MapMatches(RouteGroupBuilder api)
    {
        var matches = api.MapGroup("/matches")
            .WithTags("Matches");

        matches.MapGet("/", (PlaceholderGameStore store) => Ok(store.Matches))
            .WithName("ListMatches");

        matches.MapPost("/", (CreateMatchRequest request, PlaceholderGameStore store) =>
        {
            var match = store.CreateMatch(request);
            return TypedResults.Created($"/api/v1/matches/{match.Id}", Envelope(store.GetSnapshot(match)));
        })
        .WithName("CreateMatch");

        matches.MapGet("/{matchId}", Results<Ok<ApiResult<MatchSnapshotDto>>, NotFound> (string matchId, PlaceholderGameStore store) =>
            store.Matches.FirstOrDefault(match => match.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase)) is { } match
                ? TypedResults.Ok(Envelope(store.GetSnapshot(match)))
                : TypedResults.NotFound())
            .WithName("GetMatch");

        matches.MapGet("/{matchId}/state", Results<Ok<ApiResult<MatchSnapshotDto>>, NotFound> (string matchId, PlaceholderGameStore store) =>
            store.Matches.FirstOrDefault(match => match.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase)) is { } match
                ? TypedResults.Ok(Envelope(store.GetSnapshot(match)))
                : TypedResults.NotFound())
            .WithName("GetMatchState");

        matches.MapGet("/{matchId}/legal-actions", Results<Ok<ApiResult<IReadOnlyList<LegalActionDto>>>, NotFound> (string matchId, int playerId, PlaceholderGameStore store) =>
            store.Matches.Any(match => match.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase))
                ? TypedResults.Ok(Envelope(store.GetLegalActions(matchId, playerId)))
                : TypedResults.NotFound())
            .WithName("GetLegalActions");

        matches.MapGet("/{matchId}/events", Results<Ok<ApiResult<IReadOnlyList<MatchEventDto>>>, NotFound> (string matchId, PlaceholderGameStore store) =>
            store.Matches.Any(match => match.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase))
                ? TypedResults.Ok(Envelope(store.GetMatchEvents(matchId)))
                : TypedResults.NotFound())
            .WithName("GetMatchEvents");

        matches.MapPost("/{matchId}/actions", Results<Accepted<ApiResult<SubmitActionResponseDto>>, NotFound> (string matchId, SubmitMatchActionRequest request, PlaceholderGameStore store) =>
            store.Matches.Any(match => match.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase))
                ? TypedResults.Accepted($"/api/v1/matches/{matchId}/events", Envelope(store.SubmitAction(matchId, request)))
                : TypedResults.NotFound())
            .WithName("SubmitMatchAction");
    }

    private static void MapMatchmaking(RouteGroupBuilder api)
    {
        var matchmaking = api.MapGroup("/matchmaking")
            .WithTags("Matchmaking");

        matchmaking.MapGet("/queue", (PlaceholderGameStore store) => Ok(store.Queue))
            .WithName("GetMatchmakingQueue");

        matchmaking.MapPost("/queue", (JoinMatchmakingRequest request, PlaceholderGameStore store) =>
            TypedResults.Accepted("/api/v1/matchmaking/queue", Envelope(store.JoinQueue(request))))
            .WithName("JoinMatchmakingQueue");

        matchmaking.MapGet("/tickets", (PlaceholderGameStore store) => Ok(store.Queue))
            .WithName("ListMatchmakingTickets");

        matchmaking.MapPost("/tickets", (JoinMatchmakingRequest request, PlaceholderGameStore store) =>
            TypedResults.Accepted("/api/v1/matchmaking/tickets", Envelope(store.JoinQueue(request))))
            .WithName("CreateMatchmakingTicket");

        matchmaking.MapGet("/tickets/{ticketId}", Results<Ok<ApiResult<MatchmakingTicketDto>>, NotFound> (string ticketId, PlaceholderGameStore store) =>
        {
            var userId = ticketId.StartsWith("ticket-", StringComparison.OrdinalIgnoreCase)
                ? ticketId["ticket-".Length..]
                : ticketId;
            var entry = store.Queue.FirstOrDefault(candidate => candidate.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
            return entry is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(Envelope(new MatchmakingTicketDto(ticketId, entry.UserId, entry.DeckId, "duel-1v1", "queued", entry.JoinedAt, null)));
        })
        .WithName("GetMatchmakingTicket");

        matchmaking.MapDelete("/tickets/{ticketId}", Results<Ok<ApiResult<MatchmakingTicketDto>>, NotFound> (string ticketId, PlaceholderGameStore store) =>
        {
            var userId = ticketId.StartsWith("ticket-", StringComparison.OrdinalIgnoreCase)
                ? ticketId["ticket-".Length..]
                : ticketId;
            var entry = store.Queue.FirstOrDefault(candidate => candidate.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return TypedResults.NotFound();
            }

            store.LeaveQueue(entry.UserId);
            return TypedResults.Ok(Envelope(new MatchmakingTicketDto(ticketId, entry.UserId, entry.DeckId, "duel-1v1", "cancelled", entry.JoinedAt, null)));
        })
        .WithName("CancelMatchmakingTicket");

        matchmaking.MapDelete("/queue/{userId}", NoContent (string userId, PlaceholderGameStore store) =>
        {
            store.LeaveQueue(userId);
            return TypedResults.NoContent();
        })
        .WithName("LeaveMatchmakingQueue");
    }

    private static Ok<ApiResult<T>> Ok<T>(T data)
    {
        return TypedResults.Ok(Envelope(data));
    }

    private static ApiResult<T> Envelope<T>(T data)
    {
        return new ApiResult<T>(data);
    }
}
