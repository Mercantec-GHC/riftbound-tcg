namespace riftbound_tcg.Core.Cards;

public enum Domain
{
    Fury,
    Calm,
    Mind,
    Body,
    Chaos,
    Order
}

public enum CardKind
{
    Unit,
    Spell,
    Gear,
    Champion,
    Legend,
    Battlefield,
    Token,
    Rune
}

public enum KeywordKind
{
    Accelerate,
    Action,
    Assault,
    Deathknell,
    Deflect,
    Ganking,
    Hidden,
    Legion,
    Reaction,
    Shield,
    Tank,
    Temporary,
    Vision,
    Equip,
    QuickDraw,
    Repeat,
    Weaponmaster,
    Ambush,
    Hunt,
    Level,
    Unique,
    Backline
}

public enum KeywordBehavior
{
    Permissive,
    Passive,
    Triggered,
    Activated,
    OptionalAdditionalCost,
    MandatoryAdditionalCost,
    Dependent,
    DeckConstraint
}

public sealed record CardKeywordDefinition(
    KeywordKind Kind,
    KeywordBehavior Behavior,
    int? Value = null,
    string? Cost = null,
    string? Text = null);

public sealed record CardDefinition(
    string Id,
    string Name,
    CardKind Kind,
    IReadOnlyList<string> Tags,
    Domain Domain,
    IReadOnlyList<Domain> Domains,
    int Cost,
    int Might,
    string Text,
    string Image,
    string CardType,
    string? Supertype,
    CardEffectDefinition Effect)
{
    public IReadOnlyList<Domain> PowerCost { get; init; } = [];
    public IReadOnlyList<CardKeywordDefinition> Keywords { get; init; } = [];
}

public enum SpellSubtype
{
    Action,
    Reaction,
}

public enum CardEffectType
{
    Damage,
    Draw,
    Buff,
    Rally
}

public sealed record CardEffectDefinition(
    CardEffectType Type,
    int Amount);
