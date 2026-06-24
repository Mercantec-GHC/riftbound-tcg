using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;

namespace riftbound_tcg.Engine.RulesEngine;

public static class ChainRules
{
    public static ChainWindow? Pass(ChainWindow current, int playerId, IReadOnlyList<int> turnOrder)
    {
        if (!turnOrder.Contains(playerId))
        {
            return current;
        }

        if (current.PriorityPlayerId is not null && current.PriorityPlayerId != playerId)
        {
            return current;
        }

        var passed = new Dictionary<int, bool>(current.PassedByPlayer) { [playerId] = true };
        if (turnOrder.All(id => passed.TryGetValue(id, out var hasPassed) && hasPassed))
        {
            return null;
        }

        return current with
        {
            PassedByPlayer = passed,
            PriorityPlayerId = NextPlayerId(turnOrder, playerId)
        };
    }

    public static ChainWindow Open(
        int? priorityPlayerId = null,
        int? startedByPlayerId = null,
        ChainItemSource source = ChainItemSource.PlayedCard)
    {
        return new ChainWindow(
            new Dictionary<int, bool>(),
            priorityPlayerId,
            startedByPlayerId ?? priorityPlayerId,
            source,
            PassesFocusOnClose: source == ChainItemSource.PlayedCard);
    }

    public static ChainPlayValidation ValidateChainPlay(
        CardDefinition card,
        int playerId,
        int turnPlayerId,
        ChainWindow? chainWindow)
    {
        _ = playerId;
        _ = turnPlayerId;
        var subtype = SpellClassifier.GetSpellSubtype(card);

        if (subtype == SpellSubtype.Reaction && chainWindow is null)
        {
            return ChainPlayValidation.Rejected($"{card.Name} is a [Reaction] - it can only be played in response to an effect on the chain.");
        }

        if (subtype == SpellSubtype.Action && chainWindow is not null)
        {
            return ChainPlayValidation.Rejected($"{card.Name} does not have [Reaction] and cannot be played in a Closed State.");
        }

        return ChainPlayValidation.Accepted(subtype);
    }

    public static StackItem Finalize(StackItem item) => item with { Status = ChainItemStatus.Finalized };

    private static int NextPlayerId(IReadOnlyList<int> turnOrder, int playerId)
    {
        var index = -1;
        for (var i = 0; i < turnOrder.Count; i++)
        {
            if (turnOrder[i] == playerId)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || turnOrder.Count == 0)
        {
            return playerId;
        }

        return turnOrder[(index + 1) % turnOrder.Count];
    }
}

public sealed record ChainPlayValidation(bool IsValid, SpellSubtype? Subtype, string? Error)
{
    public static ChainPlayValidation Accepted(SpellSubtype subtype) => new(true, subtype, null);
    public static ChainPlayValidation Rejected(string error) => new(false, null, error);
}
