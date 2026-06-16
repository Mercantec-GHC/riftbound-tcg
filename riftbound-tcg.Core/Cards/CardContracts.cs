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
    CardEffectDefinition Effect);

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
