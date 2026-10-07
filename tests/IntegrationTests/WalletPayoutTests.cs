using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging;
using PartnerCommission.Messaging.Consuming;
using PartnerCommission.Messaging.Outbox;
using Shouldly;
using Wallet.Api.Data;
using Wallet.Api.Messaging;
using Wallet.Api.Payouts;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WalletPayoutTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Accrued = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private string _connectionString = "";
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<WalletDbContext>(o => o.UseNpgsql(_connectionString).UseSnakeCaseNamingConvention())
            .AddSingleton(Options.Create(new PayoutOptions { BatchSize = 1000 }))
            .AddSingleton<PayoutRunner>()
            .BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseNpgsql(_connectionString).UseSnakeCaseNamingConvention().Options);

    private static CommissionAccrued Accrual(string user, decimal amount, Guid? id = null, SchemaType schema = SchemaType.Linear) =>
        new(id ?? Guid.NewGuid(), "evt-" + Guid.NewGuid().ToString("N")[..6], user, Level: 1, amount, schema, Accrued);

    private async Task ReceiveAsync(params CommissionAccrued[] messages)
    {
        foreach (var message in messages)
        {
            await using var db = NewDb();
            await new CommissionAccruedHandler(db, NullLogger<CommissionAccruedHandler>.Instance)
                .HandleAsync(message, new MessageContext("t", 0, 0, "k", null, 1), default);
        }
    }

    private PayoutRunner Runner => _services.GetRequiredService<PayoutRunner>();

    [Fact]
    public async Task Redelivered_commission_is_stored_once()
    {
        var message = Accrual("u1", 10m);

        await ReceiveAsync(message, message, message);

        await using var db = NewDb();
        (await db.Accruals.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_commission_is_rejected_permanently()
    {
        await Should.ThrowAsync<PermanentMessageException>(() => ReceiveAsync(Accrual("u1", 0m)));
        await Should.ThrowAsync<PermanentMessageException>(() => ReceiveAsync(Accrual("u1", -5m)));
    }

    [Fact]
    public async Task Pending_commissions_do_not_change_the_balance_until_paid()
    {
        await ReceiveAsync(Accrual("u1", 10m), Accrual("u1", 5m));

        await using var db = NewDb();
        (await db.Wallets.CountAsync()).ShouldBe(0);
        (await db.Accruals.SumAsync(a => a.Amount)).ShouldBe(15m);
    }

    [Fact]
    public async Task Payout_credits_exactly_the_sum_and_marks_accruals_paid()
    {
        var first = Accrual("u1", 10.5m);
        var second = Accrual("u1", 4.25m, schema: SchemaType.Fibonacci);
        var other = Accrual("u2", 7m);
        await ReceiveAsync(first, second, other);

        var result = await Runner.RunAsync(default);

        result.Payouts.ShouldBe(2);
        result.TotalAmount.ShouldBe(21.75m);

        await using var db = NewDb();
        (await db.Wallets.SingleAsync(w => w.UserExternalId == "u1")).Balance.ShouldBe(14.75m);
        (await db.Wallets.SingleAsync(w => w.UserExternalId == "u2")).Balance.ShouldBe(7m);
        (await db.Accruals.AllAsync(a => a.Status == AccrualStatus.Paid && a.PayoutId != null && a.PaidAt != null)).ShouldBeTrue();

        var payout = await db.Payouts.SingleAsync(p => p.UserExternalId == "u1");
        payout.Amount.ShouldBe(14.75m);
        payout.CommissionCount.ShouldBe(2);
    }

    [Fact]
    public async Task Payout_queues_a_paid_notification_listing_the_commissions()
    {
        var first = Accrual("u1", 10m);
        var second = Accrual("u1", 5m);
        await ReceiveAsync(first, second);

        await Runner.RunAsync(default);

        await using var db = NewDb();
        var outbox = await db.Set<OutboxMessage>().SingleAsync();
        outbox.Topic.ShouldBe(Topics.CommissionsPaid);
        outbox.Key.ShouldBe("u1");

        var paid = MessageSerializer.Deserialize<CommissionsPaid>(outbox.Payload);
        paid.TotalAmount.ShouldBe(15m);
        paid.CommissionIds.ShouldBe([first.CommissionId, second.CommissionId], ignoreOrder: true);
        paid.PayoutId.ShouldBe((await db.Payouts.SingleAsync()).Id);
    }

    [Fact]
    public async Task Running_again_pays_nothing_twice()
    {
        await ReceiveAsync(Accrual("u1", 10m));

        await Runner.RunAsync(default);
        var second = await Runner.RunAsync(default);

        second.Payouts.ShouldBe(0);
        await using var db = NewDb();
        (await db.Wallets.SingleAsync()).Balance.ShouldBe(10m);
        (await db.Payouts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Balance_accumulates_over_several_payout_rounds()
    {
        await ReceiveAsync(Accrual("u1", 10m));
        await Runner.RunAsync(default);
        await ReceiveAsync(Accrual("u1", 2.5m), Accrual("u1", 1m));
        await Runner.RunAsync(default);

        await using var db = NewDb();
        (await db.Wallets.SingleAsync()).Balance.ShouldBe(13.5m);
        (await db.Payouts.OrderBy(p => p.PaidAt).Select(p => p.Amount).ToListAsync()).ShouldBe([10m, 3.5m]);
    }

    [Fact]
    public async Task Concurrent_payout_rounds_never_pay_a_commission_twice()
    {
        // 5 users x 20 commissions, paid by 4 runners at the same time (like 4 service instances plus a manual trigger).
        var users = Enumerable.Range(1, 5).Select(i => $"user{i}").ToList();
        var accruals = users.SelectMany(u => Enumerable.Range(1, 20).Select(n => Accrual(u, n))).ToArray();
        await ReceiveAsync(accruals);
        var expectedTotal = accruals.Sum(a => a.Amount);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Runner.RunAsync(default))));

        await using var db = NewDb();

        // Money: every wallet holds exactly its commissions, once.
        var balances = await db.Wallets.ToDictionaryAsync(w => w.UserExternalId, w => w.Balance);
        balances.Keys.Order().ShouldBe(users.Order());
        balances.Values.ShouldAllBe(b => b == 210m);
        balances.Values.Sum().ShouldBe(expectedTotal);

        // Bookkeeping: every accrual belongs to exactly one payout, and payouts add up to what was credited.
        (await db.Accruals.CountAsync(a => a.Status == AccrualStatus.Paid)).ShouldBe(accruals.Length);
        (await db.Payouts.SumAsync(p => p.Amount)).ShouldBe(expectedTotal);
        (await db.Payouts.SumAsync(p => p.CommissionCount)).ShouldBe(accruals.Length);

        // One notification per payout, together naming every commission exactly once.
        var notifications = (await db.Set<OutboxMessage>().Select(m => m.Payload).ToListAsync())
            .Select(MessageSerializer.Deserialize<CommissionsPaid>).ToList();
        notifications.Count.ShouldBe(await db.Payouts.CountAsync());
        notifications.SelectMany(n => n.CommissionIds).Count().ShouldBe(accruals.Length);
        notifications.SelectMany(n => n.CommissionIds).Distinct().Count().ShouldBe(accruals.Length);
    }
}
