namespace riftbound_tcg.Core.Actions;

public enum GameActionType
{
    ConfirmMulligan,
    AdvancePhase,
    EndTurn,
    PlayCard,
    SummonChampion,
    MoveUnit,
    PassFocus,
    OpenShowdown,
    ResolveShowdown,
    ResolveCombat,
    PassChainWindow
}

public enum ActionTargetType
{
    CardInHand,
    Champion,
    Unit,
    Battlefield,
    Base
}

public sealed record GameAction(
    string Id,
    string MatchId,
    int PlayerId,
    GameActionType Type,
    IReadOnlyList<ActionTarget> Targets,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ActionTarget(
    ActionTargetType Type,
    string? Id,
    int? Index);

public sealed record LegalAction(
    GameActionType Type,
    string Label,
    IReadOnlyList<LegalActionTarget> Targets,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record LegalActionTarget(
    ActionTargetType Type,
    string Id,
    string Label,
    bool Required);

public sealed record LegalActionsResult(
    string MatchId,
    int PlayerId,
    IReadOnlyList<LegalAction> Actions);
