using Partners.Domain;
using Shouldly;

namespace Partners.UnitTests;

public class PartnerLinkGuardTests
{
    [Fact]
    public void Self_reference_is_rejected() =>
        Should.Throw<InvalidPartnerLinkException>(() => PartnerLinkGuard.EnsureCanLink("a", "a", []));

    [Fact]
    public void Direct_cycle_is_rejected() =>
        Should.Throw<InvalidPartnerLinkException>(() => PartnerLinkGuard.EnsureCanLink("a", "b", ["a"]));

    [Fact]
    public void Deep_cycle_is_rejected() =>
        Should.Throw<InvalidPartnerLinkException>(() => PartnerLinkGuard.EnsureCanLink("user", "d", ["c", "user"]));

    [Fact]
    public void Unrelated_partner_is_allowed() =>
        Should.NotThrow(() => PartnerLinkGuard.EnsureCanLink("user", "other", ["p1", "p2"]));

    [Fact]
    public void Partner_without_ancestors_is_allowed() =>
        Should.NotThrow(() => PartnerLinkGuard.EnsureCanLink("user", "other", []));
}
