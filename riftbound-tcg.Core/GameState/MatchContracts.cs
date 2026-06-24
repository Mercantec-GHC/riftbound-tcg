using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;

namespace riftbound_tcg.Core.GameState;

public enum GameMode
{
    Duel1v1,
    Ffa3,
    Ffa4,
    Teams2v2
}

public enum MatchStage
{
    Setup,
    Mulligan,
    Playing,
    GameOver
}

public enum TurnPhase
{
    Awaken,
    Beginning,
    Channel,
    Draw,
    Main,
    Ending
}

public enum ShowdownKind
{
    NonCombat,
    Combat
}

public enum CardLocationType
{
    Base,
    Battlefield,
    Hand,
    Deck,
    Trash,
    Banished,
    Attached,
    Stack,
    RuneDeck,
    RunePool
}

public sealed record CardLocation(
    CardLocationType Type,
    string? BattlefieldId,
    string? AttachedToUid = null);

public sealed record UnitState(
    string Uid,
    string CardId,
    int OwnerPlayerId,
    CardLocation Location,
    bool Exhausted,
    int Damage,
    int AttachedMight,
    bool Attacker,
    bool Defender,
    bool IsToken = false,
    bool IsFaceDown = false,
    bool RulesTextActive = true,
    IReadOnlyList<string>? AttachedCardIds = null,
    string? TopCardId = null);

public sealed record GearState(
    string Uid,
    string CardId,
    int OwnerPlayerId,
    CardLocation Location,
    bool Exhausted,
    string? AttachedUnitId,
    bool IsToken = false,
    bool IsFaceDown = false,
    bool RulesTextActive = true,
    IReadOnlyList<string>? AttachedCardIds = null,
    string? TopCardId = null);

public sealed record RunePools(
    IReadOnlyList<string> ReadyCardIds,
    IReadOnlyList<string> ExhaustedCardIds,
    int Energy)
{
    public IReadOnlyDictionary<Domain, int> Power { get; init; } = new Dictionary<Domain, int>();
    public int UniversalPower { get; init; }
}

public sealed record PlayerState(
    int Id,
    string Name,
    int Points,
    RunePools Runes,
    IReadOnlyList<string> RuneDeckCardIds,
    IReadOnlyList<string> DeckCardIds,
    IReadOnlyList<string> HandCardIds,
    IReadOnlyList<string> TrashCardIds,
    IReadOnlyList<UnitState> Base,
    IReadOnlyList<GearState> BaseGear,
    string? ChampionCardId,
    string? LegendCardId,
    bool ChampionSummoned,
    string BattlefieldId,
    IReadOnlyList<string>? BanishedCardIds = null);

public sealed record BattlefieldState(
    string Id,
    string Name,
    int Claim,
    int ChosenByPlayerId,
    int? ControllerPlayerId,
    int? ContestedByPlayerId,
    bool StagedShowdown,
    bool StagedCombat,
    IReadOnlyList<UnitState> Units);

public sealed record ActiveShowdown(
    string BattlefieldId,
    ShowdownKind Kind);

public sealed record ActiveCombat(
    string BattlefieldId,
    int AttackerPlayerId,
    int DefenderPlayerId);

public sealed record SelectedCardRef(
    int PlayerId,
    int HandIndex);

public sealed record SelectedUnitRef(
    int PlayerId,
    string UnitId);

public sealed record GameLogEntry(
    int Id,
    string Text);

public sealed record GameState(
    string Id,
    GameMode Mode,
    int VictoryScore,
    IReadOnlyList<PlayerState> Players,
    IReadOnlyList<BattlefieldState> Battlefields,
    MatchStage Stage,
    TurnPhase TurnPhase,
    int TurnNumber,
    int FirstPlayerId,
    int TurnPlayerId,
    int ActivePlayerId,
    int? PriorityPlayerId,
    int? FocusPlayerId,
    int? WinnerPlayerId,
    int? WinningTeamId,
    IReadOnlyList<int> TurnOrder,
    IReadOnlyList<int> TeamIds,
    IReadOnlyDictionary<int, bool> HasPassedFocusByPlayer,
    IReadOnlyDictionary<int, IReadOnlyList<string>> ScoredBattlefieldIdsThisTurn,
    IReadOnlyDictionary<int, bool> FirstTurnCompletedByPlayer,
    int MulliganPlayerIndex,
    ActiveShowdown? ActiveShowdown,
    ActiveCombat? ActiveCombat,
    SelectedCardRef? SelectedCard,
    SelectedUnitRef? SelectedUnit,
    int NextUid,
    int NextLogId,
    IReadOnlyList<GameLogEntry> Log,
    bool PassShield,
    IReadOnlyList<StackItem> EffectStack,
    ChainWindow? ChainWindow);

public sealed record MatchSummary(
    string Id,
    GameMode Mode,
    MatchStage Stage,
    int VictoryScore,
    IReadOnlyList<string> PlayerUserIds,
    int TurnNumber,
    int? WinnerPlayerId,
    int? WinningTeamId,
    long CreatedAtUnixTimeMilliseconds,
    long UpdatedAtUnixTimeMilliseconds);
