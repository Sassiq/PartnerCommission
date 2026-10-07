using Commission.Api.Data;
using Commission.Api.Messaging;
using Commission.Api.Partners;
using Commission.Domain;
using Commission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging.Consuming;
using PartnerCommission.Messaging.Outbox;
using Shouldly;

namespace IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CommissionPipelineTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Occurred = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CommissionDbContext NewDb() => new(new DbContextOptionsBuilder<CommissionDbContext>()
        .UseNpgsql(_connectionString).UseSnakeCaseNamingConvention().Options);

    private static ProfitEventHandler Handler(CommissionDbContext db, FakePartners partners) =>
        new(db, partners, Options.Create(new CommissionOptions()), NullLogger<ProfitEventHandler>.Instance);

    private static MessageContext Context(Guid? messageId = null) => new("t", 0, 0, "k", messageId, 1);

    private static ProfitEventReceived Event(string id, decimal profit, string user = "d") => new(id, user, profit, Occurred);

    /// <summary>d is owned by c, c by b, b by a: levels 1, 2, 3 above d.</summary>
    private static FakePartners Chain() => new() { ["d"] = [new Ancestor(1, "c"), new Ancestor(2, "b"), new Ancestor(3, "a")] };

    private async Task SetSchemaAsync(SchemaType schema)
    {
        await using var db = NewDb();
        await db.Settings.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveSchema, schema));
    }

    [Fact]
    public async Task Linear_event_creates_commissions_for_the_whole_chain_with_schema_type()
    {
        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }

        await using var check = NewDb();
        var commissions = await check.Commissions.OrderBy(c => c.Level).ToListAsync();
        commissions.Select(c => (c.PartnerExternalId, c.Level, c.Amount, c.SchemaType, c.Status)).ShouldBe(
        [
            ("c", 1, 10m, SchemaType.Linear, CommissionStatus.Pending),
            ("b", 2, 20m, SchemaType.Linear, CommissionStatus.Pending),
            ("a", 3, 30m, SchemaType.Linear, CommissionStatus.Pending)
        ]);
    }

    [Fact]
    public async Task Fibonacci_event_uses_fibonacci_amounts()
    {
        await SetSchemaAsync(SchemaType.Fibonacci);

        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }

        await using var check = NewDb();
        (await check.Commissions.OrderBy(c => c.Level).Select(c => c.Amount).ToListAsync()).ShouldBe([10m, 10m, 20m]);
        (await check.Commissions.AllAsync(c => c.SchemaType == SchemaType.Fibonacci)).ShouldBeTrue();
    }

    [Fact]
    public async Task Every_commission_is_queued_for_the_wallet_in_the_same_transaction()
    {
        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }

        await using var check = NewDb();
        var outbox = await check.Set<OutboxMessage>().ToListAsync();
        outbox.Count.ShouldBe(3);
        outbox.ShouldAllBe(m => m.Topic == Topics.CommissionAccrued && m.PublishedAt == null);
        outbox.Select(m => m.Key).Order().ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task Redelivered_event_is_not_accrued_twice()
    {
        for (var i = 0; i < 3; i++)
        {
            await using var db = NewDb();
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }

        await using var check = NewDb();
        (await check.ProfitEvents.CountAsync()).ShouldBe(1);
        (await check.Commissions.CountAsync()).ShouldBe(3);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Simultaneous_delivery_of_the_same_event_is_accrued_once()
    {
        // Two consumers handle the same event at the same moment (e.g. the API accepted it twice onto different partitions).
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = NewDb();
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }));

        await using var check = NewDb();
        (await check.ProfitEvents.CountAsync()).ShouldBe(1);
        (await check.Commissions.CountAsync()).ShouldBe(3);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Switching_the_schema_does_not_change_existing_commissions()
    {
        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("old", 1000m), Context(), default);
        }

        await SetSchemaAsync(SchemaType.Fibonacci);

        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("new", 1000m), Context(), default);
        }

        await using var check = NewDb();
        var oldEvent = await check.ProfitEvents.SingleAsync(e => e.ExternalId == "old");
        var newEvent = await check.ProfitEvents.SingleAsync(e => e.ExternalId == "new");

        var oldCommissions = await check.Commissions.Where(c => c.EventId == oldEvent.Id).OrderBy(c => c.Level).ToListAsync();
        oldCommissions.Select(c => (c.SchemaType, c.Amount)).ShouldBe(
            [(SchemaType.Linear, 10m), (SchemaType.Linear, 20m), (SchemaType.Linear, 30m)]);

        var newCommissions = await check.Commissions.Where(c => c.EventId == newEvent.Id).OrderBy(c => c.Level).ToListAsync();
        newCommissions.Select(c => (c.SchemaType, c.Amount)).ShouldBe(
            [(SchemaType.Fibonacci, 10m), (SchemaType.Fibonacci, 10m), (SchemaType.Fibonacci, 20m)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-250)]
    public async Task Loss_or_zero_event_is_stored_without_commissions(int profit)
    {
        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("e1", profit), Context(), default);
        }

        await using var check = NewDb();
        var stored = await check.ProfitEvents.SingleAsync();
        stored.Profit.ShouldBe(profit);
        (await check.Commissions.CountAsync()).ShouldBe(0);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task User_without_partners_gets_the_event_stored_and_no_commissions()
    {
        await using (var db = NewDb())
        {
            await Handler(db, new FakePartners { ["solo"] = [] }).HandleAsync(Event("e1", 500m, "solo"), Context(), default);
        }

        await using var check = NewDb();
        (await check.ProfitEvents.CountAsync()).ShouldBe(1);
        (await check.Commissions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_user_stores_nothing_and_fails_permanently()
    {
        await using (var db = NewDb())
        {
            await Should.ThrowAsync<PermanentMessageException>(
                () => Handler(db, new FakePartners()).HandleAsync(Event("e1", 500m, "ghost"), Context(), default));
        }

        await using var check = NewDb();
        (await check.ProfitEvents.CountAsync()).ShouldBe(0);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Partners_outage_stores_nothing_and_is_reported_as_transient()
    {
        await using (var db = NewDb())
        {
            await Should.ThrowAsync<TransientMessageException>(
                () => Handler(db, FakePartners.Down).HandleAsync(Event("e1", 500m), Context(), default));
        }

        await using var check = NewDb();
        (await check.ProfitEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Chain_deeper_than_max_levels_is_cut()
    {
        var deep = Enumerable.Range(1, 12).Select(l => new Ancestor(l, $"p{l}")).ToArray();

        await using (var db = NewDb())
        {
            await Handler(db, new FakePartners { ["d"] = deep }).HandleAsync(Event("e1", 100m), Context(), default);
        }

        await using var check = NewDb();
        (await check.Commissions.CountAsync()).ShouldBe(10);
        (await check.Commissions.MaxAsync(c => c.Level)).ShouldBe(10);
    }

    [Fact]
    public async Task Settings_table_refuses_a_second_row()
    {
        await using var db = NewDb();
        db.Settings.Add(new CommissionSettings { Id = 2, ActiveSchema = SchemaType.Fibonacci });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // 23514 = check_violation: the database itself rejects the row, whatever the application does.
        (error.InnerException as Npgsql.PostgresException)?.SqlState.ShouldBe("23514");
        await using var check = NewDb();
        (await check.Settings.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Payout_notification_marks_commissions_paid_once()
    {
        await using (var db = NewDb())
        {
            await Handler(db, Chain()).HandleAsync(Event("e1", 1000m), Context(), default);
        }

        List<Guid> ids;
        await using (var db = NewDb())
        {
            ids = await db.Commissions.Where(c => c.PartnerExternalId == "a" || c.PartnerExternalId == "b")
                .Select(c => c.Id).ToListAsync();
        }

        var payoutId = Guid.NewGuid();
        var paidAt = Occurred.AddMinutes(30);
        var message = new CommissionsPaid(payoutId, "a", ids, 50m, paidAt);
        var messageId = Guid.NewGuid();

        for (var i = 0; i < 2; i++) // second delivery of the same message must be a no-op
        {
            await using var db = NewDb();
            await new CommissionsPaidHandler(db, NullLogger<CommissionsPaidHandler>.Instance)
                .HandleAsync(message, Context(messageId), default);
        }

        await using var check = NewDb();
        var all = await check.Commissions.OrderBy(c => c.Level).ToListAsync();
        all.Select(c => c.Status).ShouldBe([CommissionStatus.Pending, CommissionStatus.Paid, CommissionStatus.Paid]);
        all.Where(c => c.Status == CommissionStatus.Paid).ShouldAllBe(c => c.PayoutId == payoutId && c.PaidAt == paidAt);
        (await check.Set<InboxMessage>().CountAsync()).ShouldBe(1);
    }

    private sealed class FakePartners : Dictionary<string, Ancestor[]>, IAncestorsProvider
    {
        public static FakePartners Down => new() { IsDown = true };

        public bool IsDown { get; init; }

        public Task<IReadOnlyList<Ancestor>> GetAncestorsAsync(string userExternalId, int maxLevels, CancellationToken ct)
        {
            if (IsDown)
            {
                throw new TransientMessageException("Partners is unavailable");
            }

            if (!TryGetValue(userExternalId, out var chain))
            {
                throw new PermanentMessageException($"User '{userExternalId}' does not exist.");
            }

            return Task.FromResult<IReadOnlyList<Ancestor>>(chain.Where(a => a.Level <= maxLevels).ToList());
        }
    }
}
