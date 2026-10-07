using Commission.Domain;
using Commission.Domain.Entities;
using PartnerCommission.Contracts;
using Shouldly;

namespace Commission.UnitTests;

/// <summary>
/// These enums are stored in the database as integers, so their numbers are part of the data format.
/// Renumbering, reordering or inserting a member in the middle would silently change the meaning of stored rows.
/// If one of these tests fails, the change needs a data migration, not just a new test value.
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
