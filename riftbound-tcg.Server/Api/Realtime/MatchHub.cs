using Microsoft.AspNetCore.SignalR;

namespace RiftboundTcg.Server.Api.Realtime;

public sealed class MatchHub : Hub
{
    public Task JoinMatch(string matchId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, MatchGroupName(matchId));
    }

    public Task LeaveMatch(string matchId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, MatchGroupName(matchId));
    }

    public static string MatchGroupName(string matchId)
    {
        return $"match:{matchId}";
    }
}
