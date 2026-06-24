using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Core.Effects;

public sealed record StackItem(
    string Id,
    string CardId,
    string CardName,
    int PlayerId,
    CardEffectDefinition Effect,
    string? TargetUnitId,
    string? TargetLaneId,
    ChainItemStatus Status = ChainItemStatus.Pending,
    ChainItemSource Source = ChainItemSource.PlayedCard);

public enum ChainItemStatus
{
    Pending,
    Finalized
}

public enum ChainItemSource
{
    PlayedCard,
    TriggeredAbility,
    AddCreated
}

public sealed record ChainWindow(
    IReadOnlyDictionary<int, bool> PassedByPlayer,
    int? PriorityPlayerId = null,
    int? StartedByPlayerId = null,
    ChainItemSource Source = ChainItemSource.PlayedCard,
    bool PassesFocusOnClose = true);
