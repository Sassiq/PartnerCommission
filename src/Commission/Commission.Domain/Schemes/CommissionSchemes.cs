using PartnerCommission.Contracts;

namespace Commission.Domain.Schemes;

public static class CommissionSchemes
{
    private static readonly LinearScheme Linear = new();
    private static readonly FibonacciScheme Fibonacci = new();

    public static ICommissionScheme For(SchemaType type) => type switch
    {
        SchemaType.Linear => Linear,
        SchemaType.Fibonacci => Fibonacci,
        _ => throw new NotSupportedException($"Commission scheme '{type}' is not supported.")
    };
}
