using PartnerCommission.Contracts;

namespace Wallet.Api.Models;

public sealed record PaidCommissionResponse(
    Guid CommissionId,
    string EventExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    DateTimeOffset AccruedAt);
