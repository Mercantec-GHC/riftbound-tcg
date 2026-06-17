using Microsoft.AspNetCore.Http.HttpResults;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Services;
using System.Security.Claims;

namespace RiftboundTcg.Server.Api;

public static class GameApiV1
{
    public static IEndpointRouteBuilder MapGameApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1")
            .WithTags("Riftbound API v1");

        MapAuth(api);
        MapCards(api);
        MapAdmin(api);
        MapDecks(api);
        MapUsers(api);
        MapMatches(api);
        MapMatchmaking(api);

        return app;
    }

    private static void MapAuth(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth")
            .WithTags("Auth");

        auth.MapPost("/register", async Task<Results<Ok<ApiResult<AuthSessionDto>>, BadRequest<ApiResult<ApiErrorPayload>>>> (RegisterRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(Envelope(await service.RegisterAsync(request, cancellationToken)));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("auth.invalid", ex.Message)));
            }
        });

        auth.MapPost("/login", async Task<Results<Ok<ApiResult<AuthSessionDto>>, BadRequest<ApiResult<ApiErrorPayload>>>> (LoginRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(Envelope(await service.LoginAsync(request, cancellationToken)));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("auth.invalid", ex.Message)));
            }
        });

        auth.MapPost("/refresh", async Task<Results<Ok<ApiResult<AuthSessionDto>>, BadRequest<ApiResult<ApiErrorPayload>>>> (RefreshTokenRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(Envelope(await service.RefreshAsync(request, cancellationToken)));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("auth.invalid", ex.Message)));
            }
        });

        auth.MapPost("/logout", async (LogoutRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            await service.LogoutAsync(request, cancellationToken);
            return TypedResults.NoContent();
        });

        api.MapGet("/me", async Task<Results<Ok<ApiResult<UserDto>>, UnauthorizedHttpResult, NotFound>> (ClaimsPrincipal user, AuthService service, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null)
            {
                return TypedResults.Unauthorized();
            }

            return await service.GetUserAsync(userId, cancellationToken) is { } profile
                ? TypedResults.Ok(Envelope(profile))
                : TypedResults.NotFound();
        }).RequireAuthorization();

        api.MapPatch("/me", async Task<Results<Ok<ApiResult<UserDto>>, UnauthorizedHttpResult, NotFound>> (ClaimsPrincipal user, UpdateUserRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null)
            {
                return TypedResults.Unauthorized();
            }

            return await service.UpdateUserAsync(userId, request, cancellationToken) is { } profile
                ? TypedResults.Ok(Envelope(profile))
                : TypedResults.NotFound();
        }).RequireAuthorization();

        api.MapGet("/me/stats", async Task<Results<Ok<ApiResult<UserStatsDto>>, UnauthorizedHttpResult, NotFound>> (ClaimsPrincipal user, AuthService service, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null)
            {
                return TypedResults.Unauthorized();
            }

            return await service.GetUserAsync(userId, cancellationToken) is { } profile
                ? TypedResults.Ok(Envelope(profile.Stats))
                : TypedResults.NotFound();
        }).RequireAuthorization();

        api.MapGet("/me/matches", async Task<Results<Ok<ApiResult<IReadOnlyList<MatchSummaryDto>>>, UnauthorizedHttpResult>> (ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            return userId is null ? TypedResults.Unauthorized() : TypedResults.Ok(Envelope(await store.ListMatchesForUserAsync(userId, cancellationToken)));
        }).RequireAuthorization();
    }

    private static void MapCards(RouteGroupBuilder api)
    {
        var cards = api.MapGroup("/cards")
            .WithTags("Cards");

        cards.MapGet("/", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListCardsAsync(cancellationToken)))
            .WithName("ListCards");

        cards.MapGet("/{cardId}", async Task<Results<Ok<ApiResult<CardDto>>, NotFound>> (string cardId, OnlineGameService store, CancellationToken cancellationToken) =>
            await store.GetCardAsync(cardId, cancellationToken) is { } card
                ? TypedResults.Ok(Envelope(card))
                : TypedResults.NotFound())
            .WithName("GetCard");
    }

    private static void MapAdmin(RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin")
            .WithTags("Admin")
            .RequireAuthorization("RequireAdmin");

        admin.MapPost("/cards", async Task<Ok<ApiResult<CardUpsertResultDto>>> (CardDto request, OnlineGameService store, CancellationToken cancellationToken) =>
            TypedResults.Ok(Envelope(await store.UpsertCardAsync(request, cancellationToken))))
            .WithName("AdminUpsertCard");

        admin.MapPost("/cards/import/riftcodex", async Task<Ok<ApiResult<RiftCodexImportResultDto>>> (OnlineGameService store, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            TypedResults.Ok(Envelope(await store.ImportRiftCodexAsync(httpClientFactory.CreateClient(), cancellationToken))))
            .WithName("AdminImportRiftCodexCards");
    }

    private static void MapDecks(RouteGroupBuilder api)
    {
        var decks = api.MapGroup("/decks")
            .WithTags("Decks");

        decks.MapGet("/", async Task<Results<Ok<ApiResult<IReadOnlyList<DeckDto>>>, UnauthorizedHttpResult>> (ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            return userId is null ? TypedResults.Unauthorized() : TypedResults.Ok(Envelope(await store.ListDecksAsync(userId, cancellationToken)));
        })
            .RequireAuthorization()
            .WithName("ListDecks");

        decks.MapGet("/public", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListPublicDecksAsync(cancellationToken)))
            .WithName("ListPublicDecks");

        decks.MapGet("/{deckId}", async Task<Results<Ok<ApiResult<DeckDto>>, UnauthorizedHttpResult, NotFound>> (string deckId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            return await store.GetDeckAsync(deckId, userId, cancellationToken) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetDeck");

        decks.MapPost("/", async Task<Results<Created<ApiResult<DeckDto>>, UnauthorizedHttpResult, BadRequest<ApiResult<ApiErrorPayload>>>> (CreateDeckRequest request, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            try
            {
                var deck = await store.CreateDeckAsync(userId, request, cancellationToken);
                return TypedResults.Created($"/api/v1/decks/{deck.Id}", Envelope(deck));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("deck.invalid", ex.Message)));
            }
        })
            .RequireAuthorization()
        .WithName("CreateDeck");

        decks.MapPatch("/{deckId}", async Task<Results<Ok<ApiResult<DeckDto>>, UnauthorizedHttpResult, NotFound, BadRequest<ApiResult<ApiErrorPayload>>>> (string deckId, UpdateDeckRequest request, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            try
            {
                return await store.UpdateDeckAsync(deckId, userId, request, cancellationToken) is { } deck
                ? TypedResults.Ok(Envelope(deck))
                : TypedResults.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("deck.invalid", ex.Message)));
            }
        })
            .RequireAuthorization()
            .WithName("UpdateDeck");

        decks.MapDelete("/{deckId}", async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> (string deckId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            return await store.DeleteDeckAsync(deckId, userId, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
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

        matches.MapGet("/", async Task<Results<Ok<ApiResult<IReadOnlyList<MatchSummaryDto>>>, UnauthorizedHttpResult>> (ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            return userId is null ? TypedResults.Unauthorized() : TypedResults.Ok(Envelope(await store.ListMatchesForUserAsync(userId, cancellationToken)));
        })
            .RequireAuthorization()
            .WithName("ListMatches");

        matches.MapPost("/", async Task<Results<Created<ApiResult<MatchSnapshotDto>>, UnauthorizedHttpResult, BadRequest<ApiResult<ApiErrorPayload>>>> (CreateMatchRequest request, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            try
            {
                var match = await store.CreateMatchAsync(userId, request, cancellationToken);
                return TypedResults.Created($"/api/v1/matches/{match.Id}", Envelope(match));
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Envelope(Error("match.invalid", ex.Message)));
            }
        })
        .RequireAuthorization()
        .WithName("CreateMatch");

        matches.MapGet("/{matchId}", async Task<Results<Ok<ApiResult<MatchSnapshotDto>>, UnauthorizedHttpResult, NotFound>> (string matchId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            if (!await store.IsUserSeatedAsync(matchId, userId, cancellationToken)) return TypedResults.NotFound();
            return await store.GetMatchAsync(matchId, cancellationToken) is { } match
                ? TypedResults.Ok(Envelope(match))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetMatch");

        matches.MapGet("/{matchId}/state", async Task<Results<Ok<ApiResult<MatchSnapshotDto>>, UnauthorizedHttpResult, NotFound>> (string matchId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            if (!await store.IsUserSeatedAsync(matchId, userId, cancellationToken)) return TypedResults.NotFound();
            return await store.GetMatchAsync(matchId, cancellationToken) is { } match
                ? TypedResults.Ok(Envelope(match))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetMatchState");

        matches.MapGet("/{matchId}/legal-actions", async Task<Results<Ok<ApiResult<IReadOnlyList<LegalActionDto>>>, UnauthorizedHttpResult, NotFound>> (string matchId, int playerId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            if (!await store.IsUserSeatedAsync(matchId, userId, cancellationToken)) return TypedResults.NotFound();
            return await store.GetLegalActionsAsync(matchId, playerId, cancellationToken) is { } actions
                ? TypedResults.Ok(Envelope(actions))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetLegalActions");

        matches.MapGet("/{matchId}/events", async Task<Results<Ok<ApiResult<IReadOnlyList<MatchEventDto>>>, UnauthorizedHttpResult, NotFound>> (string matchId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            if (!await store.IsUserSeatedAsync(matchId, userId, cancellationToken)) return TypedResults.NotFound();
            return await store.GetMatchEventsAsync(matchId, cancellationToken) is { } events
                ? TypedResults.Ok(Envelope(events))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetMatchEvents");

        matches.MapPost("/{matchId}/actions", async Task<Results<Accepted<ApiResult<SubmitActionResponseDto>>, UnauthorizedHttpResult, NotFound, BadRequest<ApiResult<ApiErrorPayload>>>> (string matchId, SubmitMatchActionRequest request, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            if (!await store.UserOwnsPlayerSeatAsync(matchId, userId, request.PlayerId, cancellationToken)) return TypedResults.BadRequest(Envelope(Error("match.forbidden", "Authenticated user does not own that player seat.")));
            return await store.SubmitActionAsync(matchId, request, cancellationToken) is { } result
                ? TypedResults.Accepted($"/api/v1/matches/{matchId}/events", Envelope(result))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("SubmitMatchAction");
    }

    private static void MapMatchmaking(RouteGroupBuilder api)
    {
        var matchmaking = api.MapGroup("/matchmaking")
            .WithTags("Matchmaking");

        matchmaking.MapGet("/queue", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListQueueAsync(cancellationToken)))
            .WithName("GetMatchmakingQueue");

        matchmaking.MapPost("/queue", JoinQueue)
            .RequireAuthorization()
            .WithName("JoinMatchmakingQueue");

        matchmaking.MapGet("/tickets", async (OnlineGameService store, CancellationToken cancellationToken) => Ok(await store.ListQueueAsync(cancellationToken)))
            .WithName("ListMatchmakingTickets");

        matchmaking.MapPost("/tickets", JoinQueue)
            .RequireAuthorization()
            .WithName("CreateMatchmakingTicket");

        matchmaking.MapGet("/tickets/{ticketId}", async Task<Results<Ok<ApiResult<MatchmakingTicketDto>>, UnauthorizedHttpResult, NotFound>> (string ticketId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            return await store.GetTicketAsync(ticketId, userId, cancellationToken) is { } ticket
                ? TypedResults.Ok(Envelope(ticket))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("GetMatchmakingTicket");

        matchmaking.MapDelete("/tickets/{ticketId}", async Task<Results<Ok<ApiResult<MatchmakingTicketDto>>, UnauthorizedHttpResult, NotFound>> (string ticketId, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            return await store.CancelTicketAsync(ticketId, userId, cancellationToken) is { } ticket
                ? TypedResults.Ok(Envelope(ticket))
                : TypedResults.NotFound();
        })
            .RequireAuthorization()
            .WithName("CancelMatchmakingTicket");

        matchmaking.MapDelete("/queue", async Task<Results<NoContent, UnauthorizedHttpResult>> (ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken) =>
        {
            var userId = AuthService.GetUserId(user);
            if (userId is null) return TypedResults.Unauthorized();
            await store.LeaveQueueAsync(userId, cancellationToken);
            return TypedResults.NoContent();
        })
        .RequireAuthorization()
        .WithName("LeaveMatchmakingQueue");
    }

    private static async Task<Results<Accepted<ApiResult<MatchmakingTicketDto>>, UnauthorizedHttpResult, BadRequest<ApiResult<ApiErrorPayload>>>> JoinQueue(JoinMatchmakingRequest request, ClaimsPrincipal user, OnlineGameService store, CancellationToken cancellationToken)
    {
        var userId = AuthService.GetUserId(user);
        if (userId is null) return TypedResults.Unauthorized();
        try
        {
            return TypedResults.Accepted("/api/v1/matchmaking/tickets", Envelope(await store.JoinQueueAsync(userId, request, cancellationToken)));
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
