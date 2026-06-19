namespace riftbound_tcg.Core.GameState;

public sealed record UserProfile(
    string Id,
    string DisplayName,
    long CreatedAtUnixTimeMilliseconds,
    UserStats Stats);

public sealed record UserStats(
    int GamesPlayed,
    int Wins,
    int Losses,
    int PointsScored,
    long? LastPlayedAtUnixTimeMilliseconds);
