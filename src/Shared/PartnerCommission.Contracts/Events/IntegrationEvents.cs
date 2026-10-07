namespace PartnerCommission.Contracts.Events;

public static class Topics
{
    public const string ProfitEvents = "profit-events";
    public const string ProfitEventsDlt = "profit-events.dlt";
    public const string CommissionAccrued = "commission.accrued";
    public const string CommissionAccruedDlt = "commission.accrued.dlt";
    public const string CommissionsPaid = "wallet.commissions-paid";
    public const string CommissionsPaidDlt = "wallet.commissions-paid.dlt";
}

public sealed record ProfitEventReceived(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    DateTimeOffset OccurredAt);

public sealed record CommissionAccrued(
    Guid CommissionId,
    string EventExternalId,
    string PartnerExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    DateTimeOffset AccruedAt);

public sealed record CommissionsPaid(
    Guid PayoutId,
    string UserExternalId,
    IReadOnlyList<Guid> CommissionIds,
    decimal TotalAmount,
    DateTimeOffset PaidAt);
