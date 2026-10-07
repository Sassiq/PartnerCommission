namespace Wallet.Api.Models;

public sealed record PayoutResponse(
    Guid PayoutId,
    decimal Amount,
    int CommissionCount,
    DateTimeOffset PaidAt,
    IReadOnlyList<PaidCommissionResponse> Commissions);
