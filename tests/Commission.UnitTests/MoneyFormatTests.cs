using Commission.Domain;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

public class MoneyFormatTests
{
    [Fact]
    public void Format_is_numeric_28_8()
    {
        // Changing these changes the database columns of every service and needs a migration in each of them.
        MoneyFormat.Precision.ShouldBe(28);
        MoneyFormat.Scale.ShouldBe(8);
    }

    [Fact]
    public void Largest_accepted_profit_fits_into_the_database_column()
    {
        // numeric(28,8) holds 20 digits before the decimal point, i.e. values below 10^20. Accepted profit must stay below that.
        const decimal largestAcceptedProfit = 999_999_999_999_999_999.99999999m;
        var columnLimit = (decimal)Math.Pow(10, MoneyFormat.Precision - MoneyFormat.Scale);

        ProfitValidation.IsValid(largestAcceptedProfit).ShouldBeTrue();
        largestAcceptedProfit.ShouldBeLessThan(columnLimit);
    }

    [Fact]
    public void Calculated_amounts_are_rounded_to_the_stored_scale()
    {
        // 1 x 0.123456785 / 100 = 0.00123456785 -> must be cut to exactly MoneyFormat.Scale decimals.
        var result = CommissionCalculator.Calculate(
            new Commission.Domain.Schemes.LinearScheme(), 0.123456785m, [new Ancestor(1, "p")], maxLevels: 1);

        decimal.GetBits(result.Single().Amount)[3].ShouldBe(MoneyFormat.Scale << 16); // scale is stored in bits 16-23
    }
}
