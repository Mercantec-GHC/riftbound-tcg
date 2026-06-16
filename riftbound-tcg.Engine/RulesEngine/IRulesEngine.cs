namespace riftbound_tcg.Engine.RulesEngine;

public interface IRulesEngine
{
    IReadOnlyList<EngineLegalAction> GetLegalActions(EngineMatchState state, int playerId);

    EngineActionResult ApplyAction(EngineMatchState state, EngineGameAction action);
}

public sealed record EngineMatchState(
    string MatchId,
    string Stage,
    int SequenceNumber,
    IReadOnlyList<EnginePlayerState> Players);

public sealed record EnginePlayerState(
    int PlayerId,
    string UserId,
    int Points,
    bool Connected);

public sealed record EngineLegalAction(
    string Id,
    string Type,
    string Label,
    int PlayerId);

public sealed record EngineGameAction(
    string PlayerId,
    string ActionType,
    object Payload);

public sealed record EngineActionResult(
    bool Accepted,
    string Status,
    string ResultMessage,
    EngineMatchState State,
    IReadOnlyList<EngineLegalAction> LegalActions);
