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

        cards.MapGet("/", (OnlineGameService store) => Ok(store.Cards))
            .WithName("ListCards");

        cards.MapGet("/{cardId}", Results<Ok<ApiResult<CardDto>>, NotFound> (string cardId, OnlineGameService store) =>
            store.Cards.FirstOrDefault(card => card.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase)) is { } card
                ? TypedResults.Ok(Envelope(card))
                : TypedResults.NotFound())
            .WithName("GetCard");
    }

    private static void MapDecks(RouteGroupBuilder api)
    {
        var decks = api.MapGroup("/decks")
            .WithTags("Decks");

        decks.MapGet("/", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListDecksAsync(cancellationToken)))
            .WithName("ListDecks");

        decks.MapGet("/public", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListDecksAsync(cancellationToken)))
            .WithName("ListPublicDecks");

        decks.MapGet("/{deckId}", async Task<Results<Ok<ApiResult<DeckDto>>, NotFound>> (string deckId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetDeckAsync(deckId, cancellationToken) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound())
            .WithName("GetDeck");

        decks.MapPost("/", async (CreateDeckRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var deck = await store.CreateDeckAsync(request, cancellationToken);
            return TypedResults.Created($"/api/v1/decks/{deck.Id}", Envelope(deck));
        })
        .WithName("CreateDeck");

        decks.MapPatch("/{deckId}", async Task<Results<Ok<ApiResult<DeckDto>>, NotFound>> (string deckId, UpdateDeckRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.UpdateDeckAsync(deckId, request, cancellationToken) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound())
            .WithName("UpdateDeck");

        decks.MapDelete("/{deckId}", async Task<Results<NoContent, NotFound>> (string deckId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.DeleteDeckAsync(deckId, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
            .WithName("DeleteDeck");
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users")
            .WithTags("Users");

        users.MapGet("/", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListUsersAsync(cancellationToken)))
            .WithName("ListUsers");

        users.MapGet("/{userId}", async Task<Results<Ok<ApiResult<UserDto>>, NotFound>> (string userId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetUserAsync(userId, cancellationToken) is { } user
                ? TypedResults.Ok(Envelope(user))
                : TypedResults.NotFound())
            .WithName("GetUser");

        users.MapPost("/", async (CreateUserRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var user = await store.CreateUserAsync(request, cancellationToken);
            return TypedResults.Created($"/api/v1/users/{user.Id}", Envelope(user));
        })
        .WithName("CreateUser");

        users.MapPatch("/{userId}", async Task<Results<Ok<ApiResult<UserDto>>, NotFound>> (string userId, UpdateUserRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.UpdateUserAsync(userId, request, cancellationToken) is { } user
                ? TypedResults.Ok(Envelope(user))
                : TypedResults.NotFound())
            .WithName("UpdateUser");
    }

    private static void MapMatches(RouteGroupBuilder api)
    {
        var matches = api.MapGroup("/matches")
            .WithTags("Matches");

        matches.MapGet("/", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListMatchesAsync(cancellationToken)))
            .WithName("ListMatches");

        matches.MapPost("/", async Task<Results<Created<ApiResult<MatchSnapshotDto>>, BadRequest<ApiResult<ApiErrorPayload>>>> (CreateMatchRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            try
            {
                var match = await store.CreateMatchAsync(request, cancellationToken);
                return TypedResults.Created($"/api/v1/matches/{match.Id}", Envelope(match));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("match.invalid", ex.Message)));
            }
        })
        .WithName("CreateMatch");

        matches.MapGet("/{matchId}", async Task<Results<Ok<ApiResult<MatchSnapshotDto>>, NotFound>> (string matchId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetMatchAsync(matchId, cancellationToken) is { } match
                ? TypedResults.Ok(Envelope(match))
                : TypedResults.NotFound())
            .WithName("GetMatch");

        matches.MapGet("/{matchId}/state", async Task<Results<Ok<ApiResult<MatchSnapshotDto>>, NotFound>> (string matchId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetMatchAsync(matchId, cancellationToken) is { } match
                ? TypedResults.Ok(Envelope(match))
                : TypedResults.NotFound())
            .WithName("GetMatchState");

        matches.MapGet("/{matchId}/legal-actions", async Task<Results<Ok<ApiResult<IReadOnlyList<LegalActionDto>>>, NotFound>> (string matchId, int playerId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetLegalActionsAsync(matchId, playerId, cancellationToken) is { } actions
                ? TypedResults.Ok(Envelope(actions))
                : TypedResults.NotFound())
            .WithName("GetLegalActions");

        matches.MapGet("/{matchId}/events", async Task<Results<Ok<ApiResult<IReadOnlyList<MatchEventDto>>>, NotFound>> (string matchId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetMatchEventsAsync(matchId, cancellationToken) is { } events
                ? TypedResults.Ok(Envelope(events))
                : TypedResults.NotFound())
            .WithName("GetMatchEvents");

        matches.MapPost("/{matchId}/actions", async Task<Results<Accepted<ApiResult<SubmitActionResponseDto>>, NotFound>> (string matchId, SubmitMatchActionRequest request, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.SubmitActionAsync(matchId, request, cancellationToken) is { } result
                ? TypedResults.Accepted($"/api/v1/matches/{matchId}/events", Envelope(result))
                : TypedResults.NotFound())
            .WithName("SubmitMatchAction");
    }

    private static void MapMatchmaking(RouteGroupBuilder api)
    {
        var matchmaking = api.MapGroup("/matchmaking")
            .WithTags("Matchmaking");

        matchmaking.MapGet("/queue", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListQueueAsync(cancellationToken)))
            .WithName("GetMatchmakingQueue");

        matchmaking.MapPost("/queue", JoinQueue)
            .WithName("JoinMatchmakingQueue");

        matchmaking.MapGet("/tickets", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListQueueAsync(cancellationToken)))
            .WithName("ListMatchmakingTickets");

        matchmaking.MapPost("/tickets", JoinQueue)
            .WithName("CreateMatchmakingTicket");

        matchmaking.MapGet("/tickets/{ticketId}", async Task<Results<Ok<ApiResult<MatchmakingTicketDto>>, NotFound>> (string ticketId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetTicketAsync(ticketId, cancellationToken) is { } ticket
                ? TypedResults.Ok(Envelope(ticket))
                : TypedResults.NotFound())
            .WithName("GetMatchmakingTicket");

        matchmaking.MapDelete("/tickets/{ticketId}", async Task<Results<Ok<ApiResult<MatchmakingTicketDto>>, NotFound>> (string ticketId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.CancelTicketAsync(ticketId, cancellationToken) is { } ticket
                ? TypedResults.Ok(Envelope(ticket))
                : TypedResults.NotFound())
            .WithName("CancelMatchmakingTicket");

        matchmaking.MapDelete("/queue/{userId}", async (string userId, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            await store.LeaveQueueAsync(userId, cancellationToken);
            return TypedResults.NoContent();
        })
        .WithName("LeaveMatchmakingQueue");
    }

    private static async Task<Results<Accepted<ApiResult<MatchmakingTicketDto>>, BadRequest<ApiResult<ApiErrorPayload>>>> JoinQueue(JoinMatchmakingRequest request, OnlineGameService store, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Accepted("/api/v1/matchmaking/tickets", Envelope(await store.JoinQueueAsync(request, cancellationToken)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(Envelope(Error("matchmaking.invalid", ex.Message)));
        }
    }

    private static Ok<ApiResult<T>> Ok<T>(T data)
    {
        return TypedResults.Ok(Envelope(data));
    }

    private static ApiResult<T> Envelope<T>(T data)
    {
        return new ApiResult<T>(data);
    }

    private static ApiErrorPayload Error(string code, string message)
    {
        return new ApiErrorPayload(code, message);
    }
}
