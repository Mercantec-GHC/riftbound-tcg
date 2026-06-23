namespace RiftboundTcg.Server.Api.Models;

public sealed record ApiResult<T>(
    T Data,
    ApiMeta? Meta = null);

public sealed record ApiMeta(
    string? RequestId = null,
    string? NextCursor = null,
    int? TotalCount = null);

public sealed record ApiErrorPayload(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

public sealed record CardDto(
    string Id,
    string Name,
    string Kind,
    IReadOnlyList<string> Tags,
    string Domain,
    IReadOnlyList<string> Domains,
    int Cost,
    int Might,
    string Text,
    string Image,
    string CardType,
    string? Supertype,
    CardEffectDto Effect);

public sealed record CardEffectDto(
    string Type,
    int Amount);

public sealed record CardUpsertResultDto(
    CardDto Card,
    bool Created);

public sealed record RiftCodexImportResultDto(
    int Imported,
    int Updated,
    int Skipped,
    int Pages,
    IReadOnlyList<string> Errors);

public sealed record DeckDto(
    string Id,
    string Name,
    string OwnerUserId,
    string Visibility,
    string? Description,
    IReadOnlyList<string> Tags,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SharedDeckDto(
    string Id,
    string Name,
    string OwnerUserId,
    string Visibility,
    string Author,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Domains,
    string LegendName,
    string ChampionName,
    DeckCardCountsDto CardCounts,
    string? Description,
    DateTimeOffset UpdatedAt);

public sealed record BrowseDeckDto(
    string Id,
    string Name,
    string OwnerUserId,
    string Visibility,
    string Author,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Domains,
    string LegendName,
    string ChampionName,
    DeckCardCountsDto CardCounts,
    string? Description,
    DateTimeOffset UpdatedAt,
    bool IsActive);

public sealed record AdminDeckDto(
    string Id,
    string Name,
    string OwnerUserId,
    string OwnerDisplayName,
    string Visibility,
    string LegendId,
    string ChampionId,
    string LegendName,
    string ChampionName,
    DeckCardCountsDto CardCounts,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Domains,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ActiveUsageCount,
    int QueuedTicketCount,
    int LobbySelectionCount);

public sealed record DeckCardCountsDto(
    int Main,
    int Runes,
    int Battlefields);

public sealed record CreateDeckRequest(
    string Name,
    string? Visibility,
    string? Description,
    IReadOnlyList<string>? Tags,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record UpdateDeckRequest(
    string? Name,
    string? Visibility,
    string? Description,
    IReadOnlyList<string>? Tags,
    string? LegendId,
    string? ChampionId,
    IReadOnlyList<string>? BattlefieldDeckIds,
    IReadOnlyList<string>? RuneDeckIds,
    IReadOnlyList<string>? MainDeckIds);

public sealed record UserDto(
    string Id,
    string Email,
    string DisplayName,
    string? AvatarImageHash,
    DateTimeOffset CreatedAt,
    UserStatsDto Stats,
    bool IsAdmin,
    bool IsDisabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DisabledAt);

public sealed record UserStatsDto(
    int GamesPlayed,
    int Wins,
    int Losses,
    int PointsScored,
    DateTimeOffset? LastPlayedAt);

public sealed record CreateUserRequest(
    string DisplayName);

public sealed record UpdateUserRequest(
    string? DisplayName);

public sealed record AdminUpdateUserRequest(
    string? Email,
    string? DisplayName,
    bool? IsAdmin,
    bool? IsDisabled);

public sealed record AdminUpdateDeckRequest(
    string? Visibility);

public sealed record RegisterRequest(
    string Email,
    string DisplayName,
    string Password);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record LogoutRequest(
    string RefreshToken);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string? CurrentRefreshToken);

public sealed record AuthSessionDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    UserDto User);

public sealed record MatchPlayerDto(
    int PlayerId,
    string UserId,
    string DisplayName,
    string? DeckId,
    int? TeamId);

public sealed record MatchSummaryDto(
    string Id,
    string Mode,
    string Status,
    IReadOnlyList<MatchPlayerDto> Players,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    int? WinnerPlayerId,
    int? WinningTeamId);

public sealed record CreateMatchPlayerRequest(
    string UserId,
    string DeckId,
    int? TeamId);

public sealed record CreateMatchRequest(
    string Mode,
    IReadOnlyList<CreateMatchPlayerRequest> Players,
    IReadOnlyList<string>? BattlefieldIds,
    int? FirstPlayerId);

public sealed record MatchSnapshotDto(
    string Id,
    string Mode,
    string Status,
    IReadOnlyList<MatchPlayerDto> Players,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    int? WinnerPlayerId,
    int? WinningTeamId,
    object State,
    int SequenceNumber);

public sealed record LegalActionTargetDto(
    string Type,
    string? CardId = null,
    string? Zone = null,
    string? UnitId = null,
    string? BattlefieldId = null,
    int? PlayerId = null);

public sealed record LegalActionDto(
    string Id,
    string Type,
    int PlayerId,
    string Label,
    LegalActionTargetDto? Source,
    IReadOnlyList<LegalActionTargetDto> Targets,
    IReadOnlyDictionary<string, object?>? PayloadSchema = null);

public sealed record MatchEventDto(
    string Id,
    string MatchId,
    int SequenceNumber,
    int? PlayerId,
    string ActionType,
    object ActionPayload,
    object ResultPayload,
    DateTimeOffset CreatedAt);

public sealed record SubmitMatchActionRequest(
    string? ActionId,
    string Type,
    int PlayerId,
    IReadOnlyDictionary<string, object?>? Payload,
    int? ExpectedSequenceNumber);

public sealed record SubmitActionResponseDto(
    bool Accepted,
    MatchEventDto Event,
    object State,
    IReadOnlyList<LegalActionDto>? LegalActions);

public sealed record QueueEntryDto(
    string UserId,
    string DeckId,
    string Mode,
    DateTimeOffset JoinedAt);

public sealed record MatchmakingTicketDto(
    string Id,
    string UserId,
    string DeckId,
    string Mode,
    string Status,
    DateTimeOffset CreatedAt,
    string? MatchId);

public sealed record JoinMatchmakingRequest(
    string DeckId,
    string Mode);

public sealed record LobbyDto(
    string Id,
    string HostUserId,
    string Name,
    string Status,
    IReadOnlyList<string> AllowedModes,
    string SelectedMode,
    int RequiredPlayerCount,
    int RequiredBattlefieldCount,
    IReadOnlyList<LobbyPlayerDto> Players,
    bool CanStart,
    string? MatchId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LobbyPlayerDto(
    string UserId,
    int SeatIndex,
    string DisplayName,
    string? DeckId,
    IReadOnlyList<string> SelectedBattlefieldIds,
    int? TeamId,
    bool IsReady);

public sealed record CreateLobbyRequest(
    string? Name,
    IReadOnlyList<string>? AllowedModes,
    string? SelectedMode,
    bool? IncludeReadyDummy);

public sealed record UpdateLobbySettingsRequest(
    string? Name,
    IReadOnlyList<string>? AllowedModes,
    string? SelectedMode);

public sealed record UpdateLobbyLoadoutRequest(
    string DeckId,
    IReadOnlyList<string> SelectedBattlefieldIds);
