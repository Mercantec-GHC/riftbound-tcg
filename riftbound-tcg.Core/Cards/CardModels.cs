namespace riftbound_tcg.Core.Cards;

public enum CardKind
{
    Unit,
    Spell,
    Gear,
    Champion,
    Legend,
    Battlefield,
    Token,
    Rune,
}

public enum SpellSubtype
{
    Action,
    Reaction,
}

public enum EffectType
{
    Damage,
    Draw,
    Buff,
    Rally,
}

public sealed record Effect(EffectType Type, int Amount);

public sealed record CardDefinition(
    string Id,
    string Name,
    CardKind Kind,
    IReadOnlyList<string> Tags,
    string Domain,
    IReadOnlyList<string> Domains,
    int Cost,
    int Might,
    string Text,
    string Image,
    string CardType,
    string? Supertype,
    Effect Effect);
