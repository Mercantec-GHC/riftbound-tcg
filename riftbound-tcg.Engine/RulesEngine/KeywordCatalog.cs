using System.Text.RegularExpressions;
using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Engine.RulesEngine;

public static partial class KeywordCatalog
{
    private static readonly IReadOnlyDictionary<KeywordKind, KeywordBehavior> Behaviors = new Dictionary<KeywordKind, KeywordBehavior>
    {
        [KeywordKind.Accelerate] = KeywordBehavior.OptionalAdditionalCost,
        [KeywordKind.Action] = KeywordBehavior.Permissive,
        [KeywordKind.Assault] = KeywordBehavior.Passive,
        [KeywordKind.Deathknell] = KeywordBehavior.Triggered,
        [KeywordKind.Deflect] = KeywordBehavior.MandatoryAdditionalCost,
        [KeywordKind.Ganking] = KeywordBehavior.Passive,
        [KeywordKind.Hidden] = KeywordBehavior.Permissive,
        [KeywordKind.Legion] = KeywordBehavior.Dependent,
        [KeywordKind.Reaction] = KeywordBehavior.Permissive,
        [KeywordKind.Shield] = KeywordBehavior.Passive,
        [KeywordKind.Tank] = KeywordBehavior.Passive,
        [KeywordKind.Temporary] = KeywordBehavior.Triggered,
        [KeywordKind.Vision] = KeywordBehavior.Triggered,
        [KeywordKind.Equip] = KeywordBehavior.Activated,
        [KeywordKind.QuickDraw] = KeywordBehavior.Triggered,
        [KeywordKind.Repeat] = KeywordBehavior.OptionalAdditionalCost,
        [KeywordKind.Weaponmaster] = KeywordBehavior.Triggered,
        [KeywordKind.Ambush] = KeywordBehavior.Passive,
        [KeywordKind.Hunt] = KeywordBehavior.Triggered,
        [KeywordKind.Level] = KeywordBehavior.Dependent,
        [KeywordKind.Unique] = KeywordBehavior.DeckConstraint,
        [KeywordKind.Backline] = KeywordBehavior.Passive
    };

    public static IReadOnlyList<CardKeywordDefinition> For(CardDefinition card)
    {
        return card.Keywords is { Count: > 0 } ? card.Keywords : Parse(card.Text);
    }

    public static IReadOnlyList<CardKeywordDefinition> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Behaviors.Keys
            .SelectMany(kind => ParseKeyword(text, kind))
            .ToArray();
    }

    private static IEnumerable<CardKeywordDefinition> ParseKeyword(string text, KeywordKind kind)
    {
        var name = KeywordText(kind);
        var matches = Regex.Matches(text, $@"(?<![A-Za-z])\[?{Regex.Escape(name)}(?:\s+(?<arg>\d+|\[[^\]]+\]))?\]?", RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            var definition = ToDefinition(name, match.Groups["arg"].Value);
            if (definition is not null)
            {
                yield return WithDependentText(definition, text);
            }
        }
    }

    private static CardKeywordDefinition WithDependentText(CardKeywordDefinition definition, string sourceText)
    {
        if (definition.Kind is not (KeywordKind.Deathknell or KeywordKind.Legion or KeywordKind.Level) || !string.IsNullOrWhiteSpace(definition.Text))
        {
            return definition;
        }

        var name = KeywordText(definition.Kind);
        var match = Regex.Match(sourceText, $@"\[?{Regex.Escape(name)}(?:\s+\d+)?\]?\s*(?:\[>\])?\s*(?<text>[^\n.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var dependentText = match.Success ? NullIfBlank(match.Groups["text"].Value) : null;
        return definition with { Text = dependentText };
    }

    private static string KeywordText(KeywordKind kind) =>
        kind == KeywordKind.QuickDraw ? "Quick-Draw" : kind.ToString();

    private static CardKeywordDefinition? ToDefinition(string name, string argument)
    {
        var normalized = Normalize(name);
        if (!Enum.TryParse<KeywordKind>(normalized, ignoreCase: true, out var kind))
        {
            return null;
        }

        var value = NumericValue(kind, argument);
        var cost = kind is KeywordKind.Equip or KeywordKind.Repeat ? NullIfBlank(argument) : null;
        var dependentText = kind is KeywordKind.Deathknell or KeywordKind.Legion or KeywordKind.Level && !argument.TrimStart().StartsWith("[>", StringComparison.Ordinal)
            ? NullIfBlank(argument)
            : null;
        return new CardKeywordDefinition(kind, Behaviors[kind], value, cost, dependentText);
    }

    private static int? NumericValue(KeywordKind kind, string argument)
    {
        if (kind is not (KeywordKind.Assault or KeywordKind.Deflect or KeywordKind.Shield or KeywordKind.Hunt or KeywordKind.Level))
        {
            return null;
        }

        var match = NumberRegex().Match(argument);
        if (match.Success && int.TryParse(match.Value, out var value))
        {
            return value;
        }

        return kind == KeywordKind.Level ? null : 1;
    }

    private static string Normalize(string name) =>
        name.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
