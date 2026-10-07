using PartnerCommission.Contracts;

namespace Commission.Domain;

public static class ProfitValidation
{
    public static bool IsValid(decimal profit) =>
        Math.Round(profit, MoneyFormat.Scale) == profit &&
        Math.Abs(profit) < 1_000_000_000_000_000_000m;
}
