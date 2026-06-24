using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Core.DeckConstruction;

public sealed record DeckConstructionRequest(
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record DeckConstructionResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class DeckConstructionRules
{
    public const int MinimumMainDeckCards = 40;
    public const int RequiredRuneDeckCards = 12;
    public const int RequiredBattlefieldCards = 3;
    public const int MaxMainDeckCopiesByName = 3;
    public const int MaxSignatureCards = 3;

    public static DeckConstructionResult Validate(
        DeckConstructionRequest deck,
        IReadOnlyDictionary<string, CardDefinition> cards)
    {
        var errors = new List<string>();

        TryGetCard(cards, deck.LegendId, out var legend);
        if (legend is null || legend.Kind != CardKind.Legend)
        {
            errors.Add("Deck must include a valid legend.");
        }

        TryGetCard(cards, deck.ChampionId, out var champion);
        if (champion is null || champion.Kind != CardKind.Champion)
        {
            errors.Add("Deck must include a valid champion.");
        }

        if (legend is not null && champion is not null)
        {
            if (IsSignature(champion))
            {
                errors.Add("Chosen champion cannot be a Signature card.");
            }

            if (!CardsShareTag(legend, champion))
            {
                errors.Add("Chosen champion must share a champion tag with the selected legend.");
            }
        }

        var battlefields = CardsFor(deck.BattlefieldDeckIds, cards).ToArray();
        if (deck.BattlefieldDeckIds.Count != RequiredBattlefieldCards ||
            battlefields.Length != deck.BattlefieldDeckIds.Count ||
            battlefields.Any(card => card.Kind != CardKind.Battlefield))
        {
            errors.Add("Battlefield deck must contain exactly 3 valid battlefield cards.");
        }

        if (battlefields.GroupBy(card => NormalizeName(card.Name)).Any(group => group.Key.Length > 0 && group.Count() > 1))
        {
            errors.Add("Battlefield deck cannot contain duplicate battlefield names.");
        }

        var runes = CardsFor(deck.RuneDeckIds, cards).ToArray();
        if (deck.RuneDeckIds.Count != RequiredRuneDeckCards ||
            runes.Length != deck.RuneDeckIds.Count ||
            runes.Any(card => card.Kind != CardKind.Rune))
        {
            errors.Add("Rune deck must contain exactly 12 valid rune cards.");
        }

        var mainDeckCards = CardsFor(deck.MainDeckIds, cards).ToArray();
        if (deck.MainDeckIds.Count < MinimumMainDeckCards)
        {
            errors.Add("Main deck must contain at least 40 cards.");
        }

        if (mainDeckCards.Length != deck.MainDeckIds.Count ||
            mainDeckCards.Any(card => card.Kind is CardKind.Legend or CardKind.Battlefield or CardKind.Rune or CardKind.Token))
        {
            errors.Add("Main deck contains invalid cards.");
        }

        if (legend is not null)
        {
            var offDomain = mainDeckCards.Concat(runes).Concat(battlefields)
                .Where(card => !FitsDomainIdentity(card, legend))
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (offDomain.Length > 0)
            {
                errors.Add($"Cards must match the selected legend's domain identity: {string.Join(", ", offDomain)}.");
            }
        }

        var copyCounts = mainDeckCards
            .Concat(champion is null ? [] : [champion])
            .GroupBy(card => NormalizeName(card.Name))
            .Where(group => group.Key.Length > 0 && group.Count() > MaxMainDeckCopiesByName)
            .Select(group => group.First().Name)
            .ToArray();
        if (copyCounts.Length > 0)
        {
            errors.Add($"Main deck can include up to 3 copies of the same named card, counting the chosen champion: {string.Join(", ", copyCounts)}.");
        }

        if (legend is not null)
        {
            var signatures = mainDeckCards.Where(IsSignature).ToArray();
            if (signatures.Length > MaxSignatureCards)
            {
                errors.Add("Main deck can include up to 3 total Signature cards.");
            }

            var offTagSignatures = signatures
                .Where(card => !CardsShareTag(legend, card))
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (offTagSignatures.Length > 0)
            {
                errors.Add($"Signature cards must share a champion tag with the selected legend: {string.Join(", ", offTagSignatures)}.");
            }
        }

        return new DeckConstructionResult(errors);
    }

    private static IEnumerable<CardDefinition> CardsFor(IEnumerable<string> ids, IReadOnlyDictionary<string, CardDefinition> cards)
    {
        foreach (var id in ids)
        {
            if (TryGetCard(cards, id, out var card) && card is not null)
            {
                yield return card;
            }
        }
    }

    private static bool TryGetCard(IReadOnlyDictionary<string, CardDefinition> cards, string id, out CardDefinition? card)
    {
        if (!string.IsNullOrWhiteSpace(id) && cards.TryGetValue(id, out var found))
        {
            card = found;
            return true;
        }

        card = null;
        return false;
    }

    private static bool FitsDomainIdentity(CardDefinition card, CardDefinition legend)
    {
        var identity = DomainsFor(legend).ToHashSet();
        return DomainsFor(card).All(identity.Contains);
    }

    private static IEnumerable<Domain> DomainsFor(CardDefinition card)
    {
        return card.Domains.Count > 0 ? card.Domains : [card.Domain];
    }

    private static bool CardsShareTag(CardDefinition first, CardDefinition second)
    {
        var firstTags = TagsFor(first).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TagsFor(second).Any(firstTags.Contains);
    }

    private static IEnumerable<string> TagsFor(CardDefinition card)
    {
        return card.Tags.Count > 0 ? card.Tags.Select(NormalizeTag).Where(tag => tag.Length > 0) : InferTagsFromName(card.Name);
    }

    private static IEnumerable<string> InferTagsFromName(string name)
    {
        var tag = name.Split(" - ", StringSplitOptions.None)[0]?.Trim();
        return string.IsNullOrWhiteSpace(tag) ? [] : [tag];
    }

    private static bool IsSignature(CardDefinition card)
    {
        return (card.Supertype ?? string.Empty).Contains("signature", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTag(string tag)
    {
        return tag.Trim();
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant();
    }
}
