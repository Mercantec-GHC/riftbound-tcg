namespace riftbound_tcg.Core.Effects;

public sealed record StackItem(
    string Id,
    string CardId,
    string CardName,
    int PlayerId,
    Cards.Effect Effect,
    string? TargetUnitId,
    string? TargetLaneId);

public sealed record ChainWindow(IReadOnlyDictionary<int, bool> PassedByPlayer);
