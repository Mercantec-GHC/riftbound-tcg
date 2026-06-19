namespace riftbound_tcg.Core.Actions;

public enum GameActionType
{
    AdvancePhase,
    EndTurn,
    PlayCard,
    SummonChampion,
    MoveUnit,
    PassChainWindow,
    ConfirmMulligan,
    OpenShowdown,
    ResolveShowdown,
}

public sealed record GameAction(
    string Id,
    int PlayerId,
    GameActionType Type,
    string? CardHandIndex,
    string? TargetUnitId,
    string? TargetLaneId);

public sealed record ActionResult(bool Success, string? Error, GameState.GameStateSnapshot? NewState);
