namespace Commission.Api.Models;

public sealed record PagedResult<T>(
    int PageNumber,
    int PageSize,
    int Total,
    IReadOnlyList<T> Items);
