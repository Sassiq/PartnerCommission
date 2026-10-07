using PartnerCommission.Contracts;

namespace Commission.Domain.Entities;

/// <summary>
/// Commission.
/// </summary>
public sealed class CommissionRecord
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string PartnerExternalId { get; set; } = "";
    public int Level { get; set; }
    public decimal Amount { get; set; }
    public SchemaType SchemaType { get; set; }
    public CommissionStatus Status { get; set; } = CommissionStatus.Pending;
    public DateTimeOffset AccruedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PayoutId { get; set; }

    public static CommissionRecord From(CalculatedCommission calculated, Guid eventId, DateTimeOffset accruedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = eventId,
        PartnerExternalId = calculated.PartnerExternalId,
        Level = calculated.Level,
        Amount = calculated.Amount,
        SchemaType = calculated.SchemaType,
        AccruedAt = accruedAt
    };
}
