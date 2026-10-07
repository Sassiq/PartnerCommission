using PartnerCommission.Contracts;

namespace Commission.Domain.Schemes;

public sealed class FibonacciScheme : ICommissionScheme
{
    public SchemaType Type => SchemaType.Fibonacci;

    public decimal Multiplier(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, CommissionLimits.MaxLevels);

        // F(1) = 1, F(2) = 1, (F3) = 2, ...
        decimal previous = 0, current = 1;
        for (var i = 1; i < level; i++)
        {
            var next = previous + current;
            previous = current;
            current = next;
        }

        return current;
    }
}
