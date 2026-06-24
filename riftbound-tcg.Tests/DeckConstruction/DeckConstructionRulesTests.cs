using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.DeckConstruction;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.DeckConstruction;

public sealed class DeckConstructionRulesTests
{
    [Test]
    public void legal_deck_with_40_main_12_runes_and_3_battlefields_is_valid()
    {
        var catalog = LegalCatalog();
        var result = DeckConstructionRules.Validate(LegalDeck(), catalog);

        Assert.That(result.IsValid, Is.True, string.Join(" ", result.Errors));
    }

    [Test]
    public void main_deck_must_have_at_least_40_cards()
    {
        var result = DeckConstructionRules.Validate(LegalDeck(mainDeckIds: MainDeckIds().Take(39).ToArray()), LegalCatalog());

        Assert.That(result.Errors, Has.Some.Contains("Main deck must contain at least 40 cards."));
    }

    [Test]
    public void rune_deck_must_have_exactly_12_cards()
    {
        var result = DeckConstructionRules.Validate(LegalDeck(runeDeckIds: Enumerable.Repeat("rune-a", 13).ToArray()), LegalCatalog());

        Assert.That(result.Errors, Has.Some.Contains("Rune deck must contain exactly 12 valid rune cards."));
    }

    [Test]
    public void battlefield_deck_must_have_exactly_3_unique_names()
    {
        var catalog = LegalCatalog();
        var result = DeckConstructionRules.Validate(LegalDeck(battlefieldDeckIds: ["field-a", "field-duplicate", "field-c"]), catalog);

        Assert.That(result.Errors, Has.Some.Contains("Battlefield deck cannot contain duplicate battlefield names."));
    }

    [Test]
    public void cards_must_match_legend_domain_identity()
    {
        var catalog = LegalCatalog();
        catalog["off-domain"] = CardBuilder.Unit().Id("off-domain").Name("Off Domain").Domain(Domain.Calm).Build();
        var result = DeckConstructionRules.Validate(LegalDeck(mainDeckIds: MainDeckIds().Append("off-domain").ToArray()), catalog);

        Assert.That(result.Errors, Has.Some.Contains("domain identity"));
    }

    [Test]
    public void champion_must_share_legend_tag()
    {
        var catalog = LegalCatalog();
        catalog["champion-a"] = CardBuilder.Champion().Id("champion-a").Name("Other Champion").Tags("Other").Build();

        var result = DeckConstructionRules.Validate(LegalDeck(), catalog);

        Assert.That(result.Errors, Has.Some.Contains("Chosen champion must share a champion tag"));
    }

    [Test]
    public void copy_limit_counts_chosen_champion()
    {
        var result = DeckConstructionRules.Validate(LegalDeck(mainDeckIds: MainDeckIds().Concat(["champion-a", "champion-a", "champion-a"]).ToArray()), LegalCatalog());

        Assert.That(result.Errors, Has.Some.Contains("counting the chosen champion"));
    }

    [Test]
    public void signature_cards_are_limited_and_must_match_legend_tag()
    {
        var catalog = LegalCatalog();
        catalog["sig-a"] = CardBuilder.Unit().Id("sig-a").Name("Sig A").Tags("Akali").Supertype("Signature").Build();
        catalog["sig-b"] = CardBuilder.Unit().Id("sig-b").Name("Sig B").Tags("Akali").Supertype("Signature").Build();
        catalog["sig-c"] = CardBuilder.Unit().Id("sig-c").Name("Sig C").Tags("Akali").Supertype("Signature").Build();
        catalog["sig-d"] = CardBuilder.Unit().Id("sig-d").Name("Sig D").Tags("Other").Supertype("Signature").Build();
        var main = MainDeckIds().Concat(["sig-a", "sig-b", "sig-c", "sig-d"]).ToArray();

        var result = DeckConstructionRules.Validate(LegalDeck(mainDeckIds: main), catalog);

        Assert.That(result.Errors, Has.Some.Contains("up to 3 total Signature cards"));
        Assert.That(result.Errors, Has.Some.Contains("Signature cards must share a champion tag"));
    }

    private static DeckConstructionRequest LegalDeck(
        IReadOnlyList<string>? battlefieldDeckIds = null,
        IReadOnlyList<string>? runeDeckIds = null,
        IReadOnlyList<string>? mainDeckIds = null) =>
        new(
            "legend-a",
            "champion-a",
            battlefieldDeckIds ?? ["field-a", "field-b", "field-c"],
            runeDeckIds ?? Enumerable.Repeat("rune-a", 12).ToArray(),
            mainDeckIds ?? MainDeckIds());

    private static string[] MainDeckIds()
    {
        return Enumerable.Range(0, 13)
            .SelectMany(index => Enumerable.Repeat($"main-{index}", 3))
            .Append("main-13")
            .ToArray();
    }

    private static Dictionary<string, CardDefinition> LegalCatalog()
    {
        var cards = new Dictionary<string, CardDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["legend-a"] = CardBuilder.Legend().Id("legend-a").Name("Akali Legend").Tags("Akali").Build(),
            ["champion-a"] = CardBuilder.Champion().Id("champion-a").Name("Akali Champion").Tags("Akali").Build(),
            ["field-a"] = CardBuilder.Gear().Kind(CardKind.Battlefield).Id("field-a").Name("Field A").Build(),
            ["field-b"] = CardBuilder.Gear().Kind(CardKind.Battlefield).Id("field-b").Name("Field B").Build(),
            ["field-c"] = CardBuilder.Gear().Kind(CardKind.Battlefield).Id("field-c").Name("Field C").Build(),
            ["field-duplicate"] = CardBuilder.Gear().Kind(CardKind.Battlefield).Id("field-duplicate").Name("Field A").Build(),
            ["rune-a"] = CardBuilder.Gear().Kind(CardKind.Rune).Id("rune-a").Name("Fury Rune").Build()
        };

        foreach (var index in Enumerable.Range(0, 14))
        {
            cards[$"main-{index}"] = CardBuilder.Unit().Id($"main-{index}").Name($"Main {index}").Build();
        }

        return cards;
    }
}
