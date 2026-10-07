namespace Wallet.Domain.Entities;

public sealed class Payout
{
    public Guid Id { get; set; }
    public string UserExternalId { get; set; } = "";
    public decimal Amount { get; set; }
    public int CommissionCount { get; set; }
    public DateTimeOffset PaidAt { get; set; }

    /// <summary>
    /// Pays out the given pending accruals of user.
    /// </summary>
    public static Payout Create(IReadOnlyCollection<Accrual> pending, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Count == 0)
        {
            throw new ArgumentException("Nothing to pay out.", nameof(pending));
        }

        var user = pending.First().UserExternalId;
        if (pending.Any(a => a.UserExternalId != user))
        {
            throw new ArgumentException("A payout covers one user's accruals only.", nameof(pending));
        }

        if (pending.Any(a => a.Status != AccrualStatus.Pending))
        {
            throw new InvalidOperationException("An accrual that is already paid cannot be paid again.");
        }

        var payout = new Payout
        {
            Id = Guid.CreateVersion7(),
            UserExternalId = user,
            Amount = pending.Sum(a => a.Amount),
            CommissionCount = pending.Count,
            PaidAt = now
        };

        foreach (var accrual in pending)
        {
            accrual.Status = AccrualStatus.Paid;
            accrual.PayoutId = payout.Id;
            accrual.PaidAt = now;
        }

        return payout;
    }
}
