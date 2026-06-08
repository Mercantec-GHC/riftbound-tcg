using System.Text.Json.Serialization;

namespace riftbound_tcg.DomainModels.Models;

public enum CardType
{
    Unit,
    Spell,
    Gear,
    Champion
}

public enum TurnPhase
{
    Setup,
    Draw,
    Main,
    Combat,
    End
}

public enum BattlefieldLane
{
    Left,
    Center,
    Right
}

public enum GameActionType
{
    PlayCard,
    MoveUnit,
    PassPriority
}

public readonly record struct PlayerId(string Value);

public sealed record CardCost(int Generic, IReadOnlyList<string> Domains);

public sealed record CardEffectDefinition(string Type, IReadOnlyDictionary<string, string> Parameters);

public sealed record CardDefinition(
    string Id,
    string Name,
    CardType Type,
    CardCost Cost,
    IReadOnlyList<CardEffectDefinition> Effects);

public sealed record CardStats(int Power, int Health);

public sealed record CardInstance(
    Guid InstanceId,
    string DefinitionId,
    CardStats CurrentStats);

public sealed record BattlefieldZone(
    BattlefieldLane Lane,
    IReadOnlyList<CardInstance> Units);

public sealed record PlayerState(
    PlayerId PlayerId,
    IReadOnlyList<CardInstance> Hand,
    IReadOnlyList<CardInstance> Deck,
    IReadOnlyList<CardInstance> Discard,
    CardInstance? Champion,
    IReadOnlyList<BattlefieldZone> Battlefield,
    int Mana);

public sealed record TurnState(
    PlayerId ActivePlayerId,
    TurnPhase CurrentPhase,
    PlayerId PriorityHolderId);

public sealed record PlayerScore(PlayerId PlayerId, int Points);

public sealed record ScoreState(
    IReadOnlyList<PlayerScore> PointsByPlayer,
    int WinThreshold);

public sealed record PendingEffect(
    string EffectId,
    string Description,
    PlayerId? SourcePlayerId);

public sealed record GameState(
    IReadOnlyList<PlayerState> Players,
    TurnState Turn,
    ScoreState Score,
    IReadOnlyList<PendingEffect> PendingEffects);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PlayCardAction), "play-card")]
[JsonDerivedType(typeof(MoveUnitAction), "move-unit")]
[JsonDerivedType(typeof(PassPriorityAction), "pass-priority")]
public abstract record GameAction(PlayerId PlayerId, GameActionType ActionType);

public sealed record PlayCardAction(
    PlayerId PlayerId,
    Guid CardInstanceId) : GameAction(PlayerId, GameActionType.PlayCard);

public sealed record MoveUnitAction(
    PlayerId PlayerId,
    Guid UnitInstanceId,
    BattlefieldLane ToLane) : GameAction(PlayerId, GameActionType.MoveUnit);

public sealed record PassPriorityAction(
    PlayerId PlayerId) : GameAction(PlayerId, GameActionType.PassPriority);

public sealed record ActionResult(
    bool Success,
    GameState GameState,
    IReadOnlyList<string> Events,
    string? FailureReason);
