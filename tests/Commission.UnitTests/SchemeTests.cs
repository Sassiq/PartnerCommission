using Commission.Domain;
using Commission.Domain.Schemes;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

public class SchemeTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    public void Linear_multiplier_equals_level(int level, decimal expected) =>
        new LinearScheme().Multiplier(level).ShouldBe(expected);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 8)]
    [InlineData(7, 13)]
    [InlineData(8, 21)]
    [InlineData(9, 34)]
    [InlineData(10, 55)]
    public void Fibonacci_multiplier_is_standard_sequence(int level, decimal expected) =>
        new FibonacciScheme().Multiplier(level).ShouldBe(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_level_is_rejected(int level)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LinearScheme().Multiplier(level));
        Should.Throw<ArgumentOutOfRangeException>(() => new FibonacciScheme().Multiplier(level));
    }

    [Fact]
    public void Level_above_the_supported_limit_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LinearScheme().Multiplier(CommissionLimits.MaxLevels + 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new FibonacciScheme().Multiplier(CommissionLimits.MaxLevels + 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new FibonacciScheme().Multiplier(200));
    }

    [Fact]
    public void Fibonacci_at_the_limit_is_exact() =>
        new FibonacciScheme().Multiplier(CommissionLimits.MaxLevels).ShouldBe(12_586_269_025m); // F(50)

    [Theory]
    [InlineData(SchemaType.Linear)]
    [InlineData(SchemaType.Fibonacci)]
    public void Highest_level_with_the_largest_accepted_profit_does_not_overflow(SchemaType type)
    {
        // 10^18 - 0.00000001 is the largest profit ProfitValidation accepts.
        const decimal largestProfit = 999_999_999_999_999_999.99999999m;
        ProfitValidation.IsValid(largestProfit).ShouldBeTrue();

        var result = CommissionCalculator.Calculate(
            CommissionSchemes.For(type), largestProfit, [new Ancestor(CommissionLimits.MaxLevels, "top")], CommissionLimits.MaxLevels);

        result.Single().Amount.ShouldBeGreaterThan(0m);
    }

    [Theory]
    [InlineData(SchemaType.Linear, typeof(LinearScheme))]
    [InlineData(SchemaType.Fibonacci, typeof(FibonacciScheme))]
    public void Factory_returns_scheme_for_type(SchemaType type, Type expected) =>
        CommissionSchemes.For(type).ShouldBeOfType(expected);

    [Fact]
    public void Factory_rejects_unknown_type() =>
        Should.Throw<NotSupportedException>(() => CommissionSchemes.For((SchemaType)99))
            .Message.ShouldContain("99");
}
