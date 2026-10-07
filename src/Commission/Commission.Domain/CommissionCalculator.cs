using Commission.Domain.Schemes;
using PartnerCommission.Contracts;

namespace Commission.Domain;

/// <summary>
/// A partner above the event user.
/// </summary>
public sealed record Ancestor(int Level, string ExternalId);

public sealed record CalculatedCommission(
    string PartnerExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType);

public static class CommissionCalculator
{
    public static IReadOnlyList<CalculatedCommission> Calculate(
        ICommissionScheme scheme,
        decimal profit,
        IEnumerable<Ancestor> chain,
        int maxLevels)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLevels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLevels, CommissionLimits.MaxLevels);

        if (profit <= 0)
        {
            return [];
        }

        return chain
            .Where(x => x.Level >= 1 && x.Level <= maxLevels)
            .OrderBy(x => x.Level)
            .Select(x => new CalculatedCommission(
                x.ExternalId,
                x.Level,
                Math.Round(scheme.Multiplier(x.Level) * profit / 100m, MoneyFormat.Scale, MidpointRounding.AwayFromZero),
                scheme.Type))
            .Where(x => x.Amount > 0)
            .ToList();
    }
}
