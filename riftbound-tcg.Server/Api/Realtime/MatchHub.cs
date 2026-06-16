using Microsoft.AspNetCore.SignalR;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Services;

namespace RiftboundTcg.Server.Api.Realtime;

public sealed class MatchHub(OnlineGameService gameService) : Hub
{
    public async Task JoinMatch(string matchId, string userId)
    {
        if (!await gameService.IsUserSeatedAsync(matchId, userId, Context.ConnectionAborted))
        {
            await Clients.Caller.SendAsync("error", new ApiErrorPayload("match.forbidden", "User is not seated in this match."), Context.ConnectionAborted);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, MatchGroupName(matchId), Context.ConnectionAborted);
        var match = await gameService.GetMatchAsync(matchId, Context.ConnectionAborted);
        if (match is not null)
        {
            await Clients.Caller.SendAsync("match.joined", match, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("match.state", match.Id, match.State, match.SequenceNumber, Context.ConnectionAborted);
        }
    }

    public Task LeaveMatch(string matchId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, MatchGroupName(matchId), Context.ConnectionAborted);
    }

    public async Task SubmitAction(string matchId, SubmitMatchActionRequest action)
    {
        var result = await gameService.SubmitActionAsync(matchId, action, Context.ConnectionAborted);
        if (result is null)
        {
            await Clients.Caller.SendAsync("error", new ApiErrorPayload("match.not_found", "Match was not found."), Context.ConnectionAborted);
            return;
        }

        if (!result.Accepted)
        {
            await Clients.Caller.SendAsync("match.actionRejected", matchId, action.PlayerId, result.Event.ResultPayload, Context.ConnectionAborted);
            return;
        }

        await Clients.Group(MatchGroupName(matchId)).SendAsync("match.eventAppended", matchId, result.Event, Context.ConnectionAborted);
        await Clients.Group(MatchGroupName(matchId)).SendAsync("match.state", matchId, result.State, result.Event.SequenceNumber, Context.ConnectionAborted);

        var match = await gameService.GetMatchAsync(matchId, Context.ConnectionAborted);
        if (match?.Status == "completed")
        {
            await Clients.Group(MatchGroupName(matchId)).SendAsync("match.completed", matchId, result.State, match.WinnerPlayerId, match.WinningTeamId, Context.ConnectionAborted);
        }
    }

    public async Task RequestLegalActions(string matchId, int playerId)
    {
        var actions = await gameService.GetLegalActionsAsync(matchId, playerId, Context.ConnectionAborted);
        if (actions is null)
        {
            await Clients.Caller.SendAsync("error", new ApiErrorPayload("match.not_found", "Match was not found."), Context.ConnectionAborted);
            return;
        }

        await Clients.Caller.SendAsync("match.legalActions", matchId, playerId, actions, Context.ConnectionAborted);
    }

    public async Task SubscribeTicket(string ticketId, string userId)
    {
        var ticket = await gameService.GetTicketAsync(ticketId, Context.ConnectionAborted);
        if (ticket is null || !ticket.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
        {
            await Clients.Caller.SendAsync("error", new ApiErrorPayload("ticket.forbidden", "Ticket was not found for this user."), Context.ConnectionAborted);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroupName(ticketId), Context.ConnectionAborted);
        await Clients.Caller.SendAsync("matchmaking.ticketUpdated", ticket, Context.ConnectionAborted);
    }

    public static string MatchGroupName(string matchId)
    {
        return $"match:{matchId}";
    }

    public static string TicketGroupName(string ticketId)
    {
        return $"ticket:{ticketId}";
    }
}
