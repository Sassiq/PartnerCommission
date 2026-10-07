namespace Partners.Api.Models;

public sealed record TreeNodeResponse(
    int Level,
    string ExternalId,
    string? PartnerExternalId);
