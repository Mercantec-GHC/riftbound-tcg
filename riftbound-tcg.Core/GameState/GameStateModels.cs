using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;

namespace riftbound_tcg.Core.GameState;

public enum TurnPhase { Awaken, Beginning, Channel, Draw, Main, Ending }
public enum GameStage { Setup, Mulligan, Playing, GameOver }

public sealed record UnitInstance(
    string Uid,
    CardDefinition Card,
    int Owner,
    UnitLocation Location,
    bool Exhausted,
    int Damage,
    int AttachedMight);

public abstract record UnitLocation;
public sealed record BaseLocation : UnitLocation;
public sealed record BattlefieldLocation(string BattlefieldId) : UnitLocation;

public sealed record PlayerState(
    int Id,
    string Name,
    int Points,
    IReadOnlyList<CardDefinition> ReadyRunes,
    IReadOnlyList<CardDefinition> ExhaustedRunes,
    IReadOnlyList<CardDefinition> RuneDeck,
    int PoolEnergy,
    IReadOnlyList<CardDefinition> Deck,
    IReadOnlyList<CardDefinition> Hand,
    IReadOnlyList<CardDefinition> Trash,
    IReadOnlyList<UnitInstance> Base,
    CardDefinition? Champion,
    CardDefinition? Legend,
    bool ChampionSummoned,
    string BattlefieldId);

public sealed record BattlefieldState(
    string Id,
    string Name,
    int Claim,
    int ChosenBy,
    int? ControllerId,
    int? ContestedByPlayerId,
    bool StagedShowdown,
    bool StagedCombat,
    IReadOnlyList<UnitInstance> Units);

public sealed record GameStateSnapshot(
    string Id,
    IReadOnlyList<int> TurnOrder,
    GameStage Stage,
    TurnPhase TurnPhase,
    int TurnNumber,
    int TurnPlayerId,
    int? PriorityPlayerId,
    int? Winner,
    IReadOnlyList<PlayerState> Players,
    IReadOnlyList<BattlefieldState> Battlefields,
    IReadOnlyList<StackItem> EffectStack,
    ChainWindow? ChainWindow,
    IReadOnlyList<LogEntry> Log);

public sealed record LogEntry(int Id, string Text);
