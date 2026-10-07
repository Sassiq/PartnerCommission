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
    [InlineData(CommissionLimits.MaxLevels + 1)]
    public void Level_outside_the_supported_range_is_rejected(int level)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LinearScheme().Multiplier(level));
        Should.Throw<ArgumentOutOfRangeException>(() => new FibonacciScheme().Multiplier(level));
    }

    [Theory]
    [InlineData(SchemaType.Linear, typeof(LinearScheme))]
    [InlineData(SchemaType.Fibonacci, typeof(FibonacciScheme))]
    public void Factory_returns_scheme_for_type(SchemaType type, Type expected) =>
        CommissionSchemes.For(type).ShouldBeOfType(expected);

    [Fact]
    public void Factory_rejects_unknown_type() =>
        Should.Throw<NotSupportedException>(() => CommissionSchemes.For((SchemaType)99));
}
