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

public sealed record DeckDto(
    string Id,
    string Name,
    string OwnerUserId,
    string Visibility,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record CreateDeckRequest(
    string OwnerUserId,
    string Name,
    string? Visibility,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record UpdateDeckRequest(
    string? Name,
    string? Visibility,
    string? LegendId,
    string? ChampionId,
    IReadOnlyList<string>? BattlefieldDeckIds,
    IReadOnlyList<string>? RuneDeckIds,
    IReadOnlyList<string>? MainDeckIds);

public sealed record UserDto(
    string Id,
    string DisplayName);

public sealed record CreateUserRequest(
    string DisplayName);

public sealed record UpdateUserRequest(
    string? DisplayName);

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
    string UserId,
    string DeckId,
    string Mode);
