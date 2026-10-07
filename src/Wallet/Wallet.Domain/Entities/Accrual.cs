using PartnerCommission.Contracts;

namespace Wallet.Domain.Entities;

/// <summary>
/// A commission from the Commission service, waiting to be paid into the wallet.
/// </summary>
public sealed class Accrual
{
    public Guid CommissionId { get; set; }
    public string UserExternalId { get; set; } = "";
    public string EventExternalId { get; set; } = "";
    public int Level { get; set; }
    public decimal Amount { get; set; }
    public SchemaType SchemaType { get; set; }
    public DateTimeOffset AccruedAt { get; set; }
    public AccrualStatus Status { get; set; } = AccrualStatus.Pending;
    public Guid? PayoutId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}
