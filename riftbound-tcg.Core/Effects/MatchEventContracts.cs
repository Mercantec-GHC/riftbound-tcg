using riftbound_tcg.Core.Actions;

namespace riftbound_tcg.Core.Effects;

public enum MatchEventType
{
    MatchCreated,
    PlayerJoined,
    ActionSubmitted,
    ActionRejected,
    ActionApplied,
    StateSnapshotCreated,
    MatchCompleted
}

public sealed record MatchEvent(
    string Id,
    string MatchId,
    long SequenceNumber,
    int? PlayerId,
    MatchEventType Type,
    GameAction? Action,
    IReadOnlyDictionary<string, string> Result,
    long CreatedAtUnixTimeMilliseconds);
