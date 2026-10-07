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
        // b already refers to a (so a is b's ancestor); making a refer to b would close the loop.
        Should.Throw<InvalidPartnerLinkException>(() => PartnerLinkGuard.EnsureCanLink("a", "b", ["a"]));

    [Fact]
    public void Deep_cycle_is_rejected() =>
        // "d" is a descendant of "user" (d's ancestors are c, then user); making d the partner of user closes the loop.
        Should.Throw<InvalidPartnerLinkException>(() => PartnerLinkGuard.EnsureCanLink("user", "d", ["c", "user"]));

    [Fact]
    public void Unrelated_partner_is_allowed() =>
        Should.NotThrow(() => PartnerLinkGuard.EnsureCanLink("user", "other", ["p1", "p2"]));

    [Fact]
    public void Partner_without_ancestors_is_allowed() =>
        Should.NotThrow(() => PartnerLinkGuard.EnsureCanLink("user", "other", []));

    [Fact]
    public void Ids_are_compared_case_sensitively() =>
        Should.NotThrow(() => PartnerLinkGuard.EnsureCanLink("User", "user", []));
}
