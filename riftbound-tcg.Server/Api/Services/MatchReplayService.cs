using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RiftboundTcg.Server.Api.Data;
using riftbound_tcg.Engine.RulesEngine;

namespace RiftboundTcg.Server.Api.Services;

public sealed class MatchReplayService(GameDbContext db, IRulesEngine rulesEngine)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EngineMatchState?> RebuildAsync(string matchId, bool useLatestSnapshot, CancellationToken cancellationToken)
    {
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var players = await db.MatchPlayers
            .Where(player => player.MatchId == matchId)
            .OrderBy(player => player.PlayerId)
            .ToArrayAsync(cancellationToken);
        var acceptedActionEvents = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId
                && matchEvent.ActionType != "match.created"
                && matchEvent.ActionType != "action.rejected")
            .OrderBy(matchEvent => matchEvent.SequenceNumber)
            .ToArrayAsync(cancellationToken);

        var latestSnapshot = useLatestSnapshot
            ? await db.MatchSnapshots
                .Where(snapshot => snapshot.MatchId == matchId)
                .OrderByDescending(snapshot => snapshot.SequenceNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var state = latestSnapshot is not null
            ? FromStateJson(match, players, latestSnapshot.StateJson, latestSnapshot.SequenceNumber)
            : await FromCreationEventAsync(match, players, cancellationToken);

        if (state.SequenceNumber > acceptedActionEvents.Length)
        {
            throw new InvalidOperationException($"Match '{matchId}' snapshot sequence {state.SequenceNumber} is ahead of accepted event count {acceptedActionEvents.Length}.");
        }

        foreach (var matchEvent in acceptedActionEvents.Skip(state.SequenceNumber))
        {
            var playerId = matchEvent.PlayerId
                ?? throw new InvalidOperationException($"Accepted event '{matchEvent.Id}' is missing a player id.");
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(matchEvent.ActionPayloadJson, JsonOptions)
                ?? new Dictionary<string, object?>();
            var result = rulesEngine.ApplyAction(
                state,
                new EngineGameAction(playerId, matchEvent.ActionType, payload),
                state.SequenceNumber);

            if (!result.Accepted)
            {
                throw new InvalidOperationException($"Accepted event '{matchEvent.Id}' at append sequence {matchEvent.SequenceNumber} could not be replayed: {result.ResultMessage}");
            }

            if (result.State.SequenceNumber != state.SequenceNumber + 1)
            {
                throw new InvalidOperationException($"Accepted event '{matchEvent.Id}' advanced from state sequence {state.SequenceNumber} to {result.State.SequenceNumber}.");
            }

            if (!string.IsNullOrWhiteSpace(matchEvent.StateAfterJson)
                && !JsonNode.DeepEquals(JsonNode.Parse(matchEvent.StateAfterJson), result.State.State))
            {
                throw new InvalidOperationException($"Accepted event '{matchEvent.Id}' replay did not match its recorded state.");
            }

            state = result.State;
        }

        return state;
    }

    private async Task<EngineMatchState> FromCreationEventAsync(MatchEntity match, IReadOnlyList<MatchPlayerEntity> players, CancellationToken cancellationToken)
    {
        var creationEvent = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == match.Id && matchEvent.ActionType == "match.created")
            .OrderBy(matchEvent => matchEvent.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Match '{match.Id}' is missing its creation event.");

        if (string.IsNullOrWhiteSpace(creationEvent.StateAfterJson))
        {
            throw new InvalidOperationException($"Match '{match.Id}' creation event is missing state.");
        }

        return FromStateJson(match, players, creationEvent.StateAfterJson, 0);
    }

    private static EngineMatchState FromStateJson(MatchEntity match, IReadOnlyList<MatchPlayerEntity> players, string stateJson, int sequenceNumber)
    {
        var state = JsonNode.Parse(stateJson)?.AsObject()
            ?? throw new InvalidOperationException($"Match '{match.Id}' has invalid persisted state.");
        var stage = state["stage"]?.GetValue<string>() ?? match.Status;
        return new EngineMatchState(
            match.Id,
            match.Mode,
            stage,
            sequenceNumber,
            state,
            players.Select(player => new EnginePlayerState(player.PlayerId, player.UserId, ReadPlayerPoints(state, player.PlayerId), false)).ToArray());
    }

    private static int ReadPlayerPoints(JsonObject state, int playerId)
    {
        return state["players"]?.AsArray()
            .Select(node => node?.AsObject())
            .FirstOrDefault(player => player?["id"]?.GetValue<int>() == playerId)?["points"]?.GetValue<int>() ?? 0;
    }
}
