namespace Commission.Api.Models;

public sealed record EventSummaryResponse(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt);
