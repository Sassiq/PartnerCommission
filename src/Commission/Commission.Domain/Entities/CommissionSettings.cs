using PartnerCommission.Contracts;

namespace Commission.Domain.Entities;

/// <summary>
/// Single-row table holding the scheme used for new calculations.
/// </summary>
public sealed class CommissionSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public SchemaType ActiveSchema { get; set; } = SchemaType.Linear;
}
