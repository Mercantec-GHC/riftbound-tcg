namespace RiftboundTcg.Server.Api.Models;

public sealed record ApiResult<T>(
    T Data,
    ApiMeta? Meta = null);

public sealed record ApiMeta(
    string? RequestId = null,
    string? NextCursor = null,
    int? TotalCount = null);

public sealed record CardDto(
    string Id,
    string Name,
    string Type,
    int Cost,
    string Text);

public sealed record DeckDto(
    string Id,
    string OwnerUserId,
    string Name,
    IReadOnlyList<string> CardIds);

public sealed record UpdateDeckRequest(
    string? Name,
    IReadOnlyList<string>? CardIds);

public sealed record UserDto(
    string Id,
    string DisplayName);

public sealed record CreateUserRequest(
    string DisplayName);

public sealed record UpdateUserRequest(
    string? DisplayName);

public sealed record MatchDto(
    string Id,
    IReadOnlyList<string> PlayerIds,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record MatchSnapshotDto(
    MatchDto Match,
    object State,
    int SequenceNumber);

public sealed record LegalActionDto(
    string Id,
    string Type,
    string Label,
    int PlayerId,
    IReadOnlyList<object> Targets);

public sealed record MatchEventDto(
    string Id,
    string MatchId,
    int SequenceNumber,
    string? PlayerId,
    string ActionType,
    object Payload,
    DateTimeOffset CreatedAt);

public sealed record QueueEntryDto(
    string UserId,
    string? DeckId,
    DateTimeOffset JoinedAt);

public sealed record MatchmakingTicketDto(
    string Id,
    string UserId,
    string? DeckId,
    string Mode,
    string Status,
    DateTimeOffset CreatedAt,
    string? MatchId);

public sealed record CreateDeckRequest(
    string OwnerUserId,
    string Name,
    IReadOnlyList<string> CardIds);

public sealed record CreateMatchRequest(
    string? Mode,
    IReadOnlyList<string> PlayerIds);

public sealed record SubmitMatchActionRequest(
    string PlayerId,
    string ActionType,
    object Payload);

public sealed record MatchActionReceiptDto(
    string MatchId,
    int SequenceNumber,
    string Status);

public sealed record SubmitActionResponseDto(
    bool Accepted,
    MatchActionReceiptDto Receipt,
    MatchEventDto Event,
    object State,
    IReadOnlyList<LegalActionDto> LegalActions);

public sealed record JoinMatchmakingRequest(
    string UserId,
    string? DeckId,
    string? Mode);
