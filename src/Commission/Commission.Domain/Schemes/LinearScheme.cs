using PartnerCommission.Contracts;

namespace Commission.Domain.Schemes;

public sealed class LinearScheme : ICommissionScheme
{
    public SchemaType Type => SchemaType.Linear;

    public decimal Multiplier(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, CommissionLimits.MaxLevels);
        return level;
    }
}
