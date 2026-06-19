using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;

namespace riftbound_tcg.Engine.RulesEngine;

public static class ChainRules
{
    // Returns a new chain window with this player marked as passed.
    // Returns null if all players in the turn order have now passed (stack should resolve).
public static ChainWindow? Pass(ChainWindow current, int playerId, IReadOnlyList<int> turnOrder)
{
    if (!turnOrder.Contains(playerId))
        return current;

    var passed = new Dictionary<int, bool>(current.PassedByPlayer) { [playerId] = true };
    if (turnOrder.All(id => passed.ContainsKey(id) && passed[id]))
        return null; // All passed — caller should resolve top of stack
    return new ChainWindow(passed);
}

    // Opens a fresh chain window after a new card is added to the stack (resets all pass flags).
    public static ChainWindow Open() => new(new Dictionary<int, bool>());

    // Validates whether a given card can be added to the chain right now.
    public static ChainPlayValidation ValidateChainPlay(
        CardDefinition card,
        int playerId,
        int turnPlayerId,
        ChainWindow? chainWindow)
    {
        var subtype = SpellClassifier.GetSpellSubtype(card);

        if (subtype == SpellSubtype.Reaction && chainWindow is null)
            return ChainPlayValidation.Rejected($"{card.Name} is a [Reaction] — it can only be played in response to an effect on the chain.");

        if (subtype == SpellSubtype.Action && chainWindow is not null && playerId != turnPlayerId)
            return ChainPlayValidation.Rejected($"Only the turn player may chain an action spell.");

        return ChainPlayValidation.Accepted(subtype);
    }
}

public sealed record ChainPlayValidation(bool IsValid, SpellSubtype? Subtype, string? Error)
{
    public static ChainPlayValidation Accepted(SpellSubtype subtype) => new(true, subtype, null);
    public static ChainPlayValidation Rejected(string error) => new(false, null, error);
}
