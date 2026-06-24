using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Engine.RulesEngine;

public static class SpellClassifier
{
    // [Reaction] in card text marks a spell that can be played in response to any queued effect.
    public static SpellSubtype GetSpellSubtype(CardDefinition card)
    {
        if (card.Kind != CardKind.Spell) return SpellSubtype.Action;
        return KeywordCatalog.For(card).Any(keyword => keyword.Kind is KeywordKind.Reaction or KeywordKind.QuickDraw)
            || card.Text.Contains("[Reaction]", StringComparison.Ordinal)
            ? SpellSubtype.Reaction
            : SpellSubtype.Action;
    }

    // Units, champions, legends, and gear trigger the chain when "When you play me" or
    // "As you play me" appears in their text.
    public static bool HasOnPlayEffect(CardDefinition card)
    {
        if (card.Kind is CardKind.Spell or CardKind.Rune or CardKind.Battlefield or CardKind.Token)
            return false;

        return card.Text.Contains("When you play me", StringComparison.OrdinalIgnoreCase)
            || card.Text.Contains("As you play me", StringComparison.OrdinalIgnoreCase);
    }

    // A card may be played during a chain window only if it has Reaction timing.
    public static bool CanPlayDuringChainWindow(CardDefinition card, int playerId, int turnPlayerId)
    {
        _ = playerId;
        _ = turnPlayerId;
        return GetSpellSubtype(card) == SpellSubtype.Reaction;
    }
}
