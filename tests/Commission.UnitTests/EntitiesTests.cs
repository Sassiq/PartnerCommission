using System.Globalization;
using Commission.Domain;
using Commission.Domain.Entities;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

public class EntitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Commission_is_created_from_calculation_with_its_schema_type()
    {
        var calculated = new CalculatedCommission("partner-1", 2, 4.5m, SchemaType.Fibonacci);
        var eventId = Guid.NewGuid();

        var commission = CommissionRecord.From(calculated, eventId, Now);

        commission.EventId.ShouldBe(eventId);
        commission.PartnerExternalId.ShouldBe("partner-1");
        commission.Level.ShouldBe(2);
        commission.Amount.ShouldBe(4.5m);
        commission.SchemaType.ShouldBe(SchemaType.Fibonacci);
        commission.Status.ShouldBe(CommissionStatus.Pending);
        commission.PaidAt.ShouldBeNull();
        commission.Id.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("100", true)]
    [InlineData("-100.12345678", true)]
    [InlineData("0", true)]
    [InlineData("0.123456789", false)]
    [InlineData("1000000000000000000", false)]
    public void Profit_must_fit_storage_precision(string value, bool expected) =>
        ProfitValidation.IsValid(decimal.Parse(value, CultureInfo.InvariantCulture)).ShouldBe(expected);
}
