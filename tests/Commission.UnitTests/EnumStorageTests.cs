using Commission.Domain;
using Commission.Domain.Entities;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

/// <summary>
/// Enums are stored as integers, so their numbers are part of the data format: changing them needs a data migration.
/// </summary>
public class EnumStorageTests
{
    [Fact]
    public void SchemaType_numbers_are_stable()
    {
        ((int)SchemaType.Linear).ShouldBe(1);
        ((int)SchemaType.Fibonacci).ShouldBe(2);
        Enum.GetValues<SchemaType>().Length.ShouldBe(2);
    }

    [Fact]
    public void CommissionStatus_numbers_are_stable()
    {
        ((int)CommissionStatus.Pending).ShouldBe(1);
        ((int)CommissionStatus.Paid).ShouldBe(2);
        Enum.GetValues<CommissionStatus>().Length.ShouldBe(2);
    }
}
