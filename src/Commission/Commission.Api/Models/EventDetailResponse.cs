namespace Commission.Api.Models;

public sealed record EventDetailResponse(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt,
    IReadOnlyList<CommissionResponse> Commissions);
