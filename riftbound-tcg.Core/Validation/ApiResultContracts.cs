namespace riftbound_tcg.Core.Validation;

public sealed record ApiResult<T>(
    bool Success,
    T? Data,
    ApiError? Error)
{
    public static ApiResult<T> Ok(T data) => new(true, data, null);

    public static ApiResult<T> Fail(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(false, default, new ApiError(code, message, details ?? new Dictionary<string, string>()));
}

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Details);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
