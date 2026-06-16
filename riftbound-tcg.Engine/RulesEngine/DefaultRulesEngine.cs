namespace riftbound_tcg.Engine.RulesEngine;

public sealed class DefaultRulesEngine : IRulesEngine
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "advance-phase",
        "confirm-mulligan",
        "concede"
    };

    public IReadOnlyList<EngineLegalAction> GetLegalActions(EngineMatchState state, int playerId)
    {
        if (!state.Players.Any(player => player.PlayerId == playerId))
        {
            return [];
        }

        return
        [
            new("advance-phase", "advance-phase", "Advance phase", playerId),
            new("confirm-mulligan", "confirm-mulligan", "Confirm mulligan", playerId),
            new("concede", "concede", "Concede", playerId)
        ];
    }

    public EngineActionResult ApplyAction(EngineMatchState state, EngineGameAction action)
    {
        var actor = state.Players.FirstOrDefault(player => player.UserId.Equals(action.PlayerId, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
        {
            return Reject(state, $"Player '{action.PlayerId}' is not seated in match '{state.MatchId}'.");
        }

        if (!SupportedActions.Contains(action.ActionType))
        {
            return Reject(state, $"Action '{action.ActionType}' is not currently supported by the rules engine.");
        }

        var nextState = state with { SequenceNumber = state.SequenceNumber + 1 };
        return new EngineActionResult(
            true,
            "accepted",
            $"Accepted {action.ActionType}.",
            nextState,
            GetLegalActions(nextState, actor.PlayerId));
    }

    private EngineActionResult Reject(EngineMatchState state, string message)
    {
        return new EngineActionResult(false, "rejected", message, state, []);
    }
}
