namespace Partners.Api.Models;

public sealed record UserResponse(
    string ExternalId,
    string? PartnerExternalId,
    DateTimeOffset CreatedAt);
