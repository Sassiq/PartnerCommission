using Partners.Domain.Entities;
using Shouldly;

namespace Partners.UnitTests;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_the_given_values_and_no_partner()
    {
        var user = User.Create("u-1", Now);

        user.Id.ShouldNotBe(Guid.Empty);
        user.ExternalId.ShouldBe("u-1");
        user.CreatedAt.ShouldBe(Now);
        user.PartnerId.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_external_id(string externalId) =>
        Should.Throw<ArgumentException>(() => User.Create(externalId, Now));

    [Fact]
    public void Create_rejects_a_null_external_id() =>
        Should.Throw<ArgumentNullException>(() => User.Create(null!, Now));
}
