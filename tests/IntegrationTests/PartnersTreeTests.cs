using Microsoft.EntityFrameworkCore;
using Partners.Api.Data;
using Partners.Domain;
using Partners.Domain.Entities;
using Shouldly;

namespace IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PartnersTreeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PartnersDbContext NewDb() => new(new DbContextOptionsBuilder<PartnersDbContext>()
        .UseNpgsql(_connectionString).UseSnakeCaseNamingConvention().Options);

    /// <summary>
    /// root
    /// ├─ a ── a1 ── a2
    /// └─ b
    /// loner (no links)
    /// </summary>
    private async Task SeedAsync()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var users = new[] { "root", "a", "a1", "a2", "b", "loner" }.ToDictionary(id => id, id => User.Create(id, now));
        db.Users.AddRange(users.Values);
        await db.SaveChangesAsync();

        users["a"].PartnerId = users["root"].Id;
        users["b"].PartnerId = users["root"].Id;
        users["a1"].PartnerId = users["a"].Id;
        users["a2"].PartnerId = users["a1"].Id;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ancestors_are_returned_nearest_first()
    {
        await SeedAsync();
        await using var db = NewDb();

        var chain = await new TreeQueries(db).GetAncestorsAsync("a2", 10, default);

        chain.Select(a => (a.Level, a.ExternalId)).ShouldBe([(1, "a1"), (2, "a"), (3, "root")]);
    }

    [Fact]
    public async Task Ancestor_depth_limit_is_respected()
    {
        await SeedAsync();
        await using var db = NewDb();

        var chain = await new TreeQueries(db).GetAncestorsAsync("a2", 2, default);

        chain.Select(a => a.ExternalId).ShouldBe(["a1", "a"]);
    }

    [Fact]
    public async Task User_without_partner_has_no_ancestors()
    {
        await SeedAsync();
        await using var db = NewDb();

        (await new TreeQueries(db).GetAncestorsAsync("root", 10, default)).ShouldBeEmpty();
        (await new TreeQueries(db).GetAncestorsAsync("loner", 10, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Descendants_are_returned_level_by_level()
    {
        await SeedAsync();
        await using var db = NewDb();

        var branch = await new TreeQueries(db).GetDescendantsAsync("root", 10, default);

        branch.Select(d => (d.Level, d.ExternalId, d.PartnerExternalId)).ShouldBe(
            [(1, "a", "root"), (1, "b", "root"), (2, "a1", "a"), (3, "a2", "a1")]);
    }

    [Fact]
    public async Task Descendant_depth_limit_is_respected()
    {
        await SeedAsync();
        await using var db = NewDb();

        var branch = await new TreeQueries(db).GetDescendantsAsync("root", 2, default);

        branch.Select(d => d.ExternalId).ShouldBe(["a", "b", "a1"]);
    }

    [Fact]
    public async Task Leaf_has_no_descendants()
    {
        await SeedAsync();
        await using var db = NewDb();

        (await new TreeQueries(db).GetDescendantsAsync("a2", 10, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_user_has_an_empty_branch()
    {
        await SeedAsync();
        await using var db = NewDb();

        (await new TreeQueries(db).GetAncestorsAsync("nobody", 10, default)).ShouldBeEmpty();
        (await new TreeQueries(db).GetDescendantsAsync("nobody", 10, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task External_id_is_unique()
    {
        await SeedAsync();
        await using var db = NewDb();
        db.Users.Add(User.Create("root", DateTimeOffset.UtcNow));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
