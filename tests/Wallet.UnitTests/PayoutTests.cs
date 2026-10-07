using PartnerCommission.Contracts;
using Shouldly;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace Wallet.UnitTests;

public class PayoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static Accrual Pending(decimal amount, string user = "u1") => new()
    {
        CommissionId = Guid.NewGuid(),
        UserExternalId = user,
        EventExternalId = "e",
        Level = 1,
        Amount = amount,
        SchemaType = SchemaType.Linear,
        AccruedAt = Now.AddMinutes(-5)
    };

    [Fact]
    public void Payout_amount_is_the_exact_sum_of_the_accruals()
    {
        var accruals = new[] { Pending(10.12345678m), Pending(0.00000001m), Pending(5m) };

        var payout = Payout.Create(accruals, Now);

        payout.Amount.ShouldBe(15.12345679m);
        payout.CommissionCount.ShouldBe(3);
        payout.UserExternalId.ShouldBe("u1");
        payout.PaidAt.ShouldBe(Now);
    }

    [Fact]
    public void Every_accrual_is_marked_paid_by_the_payout()
    {
        var accruals = new[] { Pending(1m), Pending(2m) };

        var payout = Payout.Create(accruals, Now);

        accruals.ShouldAllBe(a => a.Status == AccrualStatus.Paid && a.PayoutId == payout.Id && a.PaidAt == Now);
    }

    [Fact]
    public void Already_paid_accrual_cannot_be_paid_again()
    {
        var accruals = new[] { Pending(1m), Pending(2m) };
        Payout.Create(accruals, Now);

        Should.Throw<InvalidOperationException>(() => Payout.Create(accruals, Now.AddHours(1)));
    }

    [Fact]
    public void A_second_payout_does_not_touch_accruals_of_the_first()
    {
        var first = new[] { Pending(1m) };
        var firstPayout = Payout.Create(first, Now);

        var second = Payout.Create([Pending(3m)], Now.AddHours(1));

        second.Amount.ShouldBe(3m);
        first[0].PayoutId.ShouldBe(firstPayout.Id);
    }

    [Fact]
    public void AccrualStatus_numbers_are_stable_because_they_are_stored_in_the_database()
    {
        ((int)AccrualStatus.Pending).ShouldBe(1);
        ((int)AccrualStatus.Paid).ShouldBe(2);
        Enum.GetValues<AccrualStatus>().Length.ShouldBe(2);
    }

    [Fact]
    public void Nothing_to_pay_is_rejected() =>
        Should.Throw<ArgumentException>(() => Payout.Create([], Now));

    [Fact]
    public void Accruals_of_different_users_cannot_share_a_payout() =>
        Should.Throw<ArgumentException>(() => Payout.Create([Pending(1m, "u1"), Pending(1m, "u2")], Now));
}
