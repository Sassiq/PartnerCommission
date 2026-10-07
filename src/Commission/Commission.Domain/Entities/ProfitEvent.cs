namespace Commission.Domain.Entities;

/// <summary>
/// A profit/loss event of a user.
/// </summary>
public sealed class ProfitEvent
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string UserExternalId { get; set; } = "";
    public decimal Profit { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
