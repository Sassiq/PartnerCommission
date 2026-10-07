using Commission.Domain.Entities;
using PartnerCommission.Contracts;

namespace Commission.Api.Models;

public sealed record CommissionResponse(
    Guid Id,
    string PartnerExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    CommissionStatus Status,
    DateTimeOffset AccruedAt,
    DateTimeOffset? PaidAt,
    Guid? PayoutId);
