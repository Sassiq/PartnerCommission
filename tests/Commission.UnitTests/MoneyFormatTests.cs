using Commission.Domain;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

public class MoneyFormatTests
{
    [Fact]
    public void Format_is_numeric_28_8()
    {
        MoneyFormat.Precision.ShouldBe(28);
        MoneyFormat.Scale.ShouldBe(8);
    }

    [Fact]
    public void Largest_accepted_profit_fits_into_the_database_column()
    {
        // numeric(28,8) holds values below 10^20
        const decimal largestAcceptedProfit = 999_999_999_999_999_999.99999999m;
        var columnLimit = (decimal)Math.Pow(10, MoneyFormat.Precision - MoneyFormat.Scale);

        ProfitValidation.IsValid(largestAcceptedProfit).ShouldBeTrue();
        largestAcceptedProfit.ShouldBeLessThan(columnLimit);
    }

    [Fact]
    public void Calculated_amounts_are_rounded_to_the_stored_scale()
    {
        var result = CommissionCalculator.Calculate(
            new Commission.Domain.Schemes.LinearScheme(), 0.123456785m, [new Ancestor(1, "p")], maxLevels: 1);

        decimal.GetBits(result.Single().Amount)[3].ShouldBe(MoneyFormat.Scale << 16); // scale lives in bits 16-23
    }
}
