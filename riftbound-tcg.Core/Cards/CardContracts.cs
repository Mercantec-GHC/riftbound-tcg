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
    public IReadOnlyList<CardContinuousEffectDefinition> ContinuousEffects { get; init; } = [];
    public IReadOnlyList<CardRuleModifierDefinition> RuleModifiers { get; init; } = [];
}

public sealed record CardContinuousEffectDefinition(
    string Id,
    string Layer,
    string Operation,
    string Property,
    int? Amount = null,
    string? TextValue = null,
    IReadOnlyList<string>? TextValues = null,
    string AppliesTo = "self",
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? RequiresTraits = null,
    IReadOnlyList<string>? RequiresAbilities = null,
    int? RequiresMinimumMight = null,
    int? Timestamp = null);

public enum RuleModifierPolarity
{
    Can,
    Cannot
}

public sealed record CardRuleModifierDefinition(
    string Id,
    RuleModifierPolarity Polarity,
    string ActionType,
    string AppliesTo = "self",
    string? Timing = null,
    string? Destination = null,
    string? EffectType = null,
    string? Target = null,
    string? CardKind = null);

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
    Rally,
    Kill,
    Banish,
    Stun
}

/// <summary>
/// One instruction in a card's effect sequence. Target-requiring steps (Damage, Buff, Kill,
/// Banish, Stun) consume the next not-yet-consumed entry from the stack item's chosen targets,
/// in step order; target-less steps (Draw) act on the controller only.
/// </summary>
public sealed record CardEffectStep(
    CardEffectType Type,
    int Amount);

public sealed record CardEffectDefinition(
    CardEffectType Type,
    int Amount)
{
    /// <summary>
    /// Ordered multi-instruction form, e.g. "Deal 4 to a unit. Draw 1." When non-empty, the
    /// resolver executes these in order instead of the single Type/Amount above.
    /// </summary>
    public IReadOnlyList<CardEffectStep> Steps { get; init; } = [];
}
