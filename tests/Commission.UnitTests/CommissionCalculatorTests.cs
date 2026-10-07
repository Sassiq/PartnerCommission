using Commission.Domain;
using Commission.Domain.Schemes;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

public class CommissionCalculatorTests
{
    private const int MaxLevels = 10;

    private static List<Ancestor> Chain(int length) =>
        Enumerable.Range(1, length).Select(l => new Ancestor(l, $"partner-{l}")).ToList();

    [Fact]
    public void Linear_pays_level_times_profit_percent()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 200m, Chain(3), MaxLevels);

        result.Select(c => (c.Level, c.Amount)).ShouldBe([(1, 2m), (2, 4m), (3, 6m)]);
    }

    [Fact]
    public void Fibonacci_pays_fibonacci_of_level_times_profit_percent()
    {
        var result = CommissionCalculator.Calculate(new FibonacciScheme(), 1000m, Chain(5), MaxLevels);

        result.Select(c => (c.Level, c.Amount)).ShouldBe([(1, 10m), (2, 10m), (3, 20m), (4, 30m), (5, 50m)]);
    }

    [Fact]
    public void Full_ten_level_fibonacci_chain_pays_143_percent_of_profit()
    {
        var result = CommissionCalculator.Calculate(new FibonacciScheme(), 100m, Chain(10), MaxLevels);

        result.Sum(c => c.Amount).ShouldBe(143m);
    }

    [Fact]
    public void Commissions_go_to_the_matching_partner()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 100m, Chain(2), MaxLevels);

        result.Select(c => c.PartnerExternalId).ShouldBe(["partner-1", "partner-2"]);
    }

    [Theory]
    [InlineData(SchemaType.Linear)]
    [InlineData(SchemaType.Fibonacci)]
    public void Every_commission_carries_the_scheme_type(SchemaType type)
    {
        var result = CommissionCalculator.Calculate(CommissionSchemes.For(type), 100m, Chain(4), MaxLevels);

        result.ShouldNotBeEmpty();
        result.ShouldAllBe(c => c.SchemaType == type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-500)]
    public void Zero_or_negative_profit_gives_no_commissions(double profit)
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), (decimal)profit, Chain(3), MaxLevels);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void User_without_partners_gives_no_commissions()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 100m, [], MaxLevels);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Chain_is_cut_at_max_levels()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 100m, Chain(12), MaxLevels);

        result.Count.ShouldBe(MaxLevels);
        result.Max(c => c.Level).ShouldBe(MaxLevels);
    }

    [Fact]
    public void Amount_is_rounded_to_eight_decimals_away_from_zero()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 0.123456785m, Chain(1), MaxLevels);

        result.Single().Amount.ShouldBe(0.00123457m);
    }

    [Fact]
    public void Commission_that_rounds_to_zero_is_not_created()
    {
        var result = CommissionCalculator.Calculate(new LinearScheme(), 0.0000001m, Chain(1), MaxLevels);

        result.ShouldBeEmpty();
    }
}
