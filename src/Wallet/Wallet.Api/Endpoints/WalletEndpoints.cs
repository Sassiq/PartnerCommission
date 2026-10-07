using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;
using Wallet.Api.Data;
using Wallet.Api.Payouts;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace Wallet.Api.Endpoints;

public sealed record BalanceResponse(
    string UserExternalId,
    decimal Balance,
    decimal PendingAmount);

public sealed record PaidCommission(
    Guid CommissionId,
    string EventExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    DateTimeOffset AccruedAt);

public sealed record PayoutDetail(
    Guid PayoutId,
    decimal Amount,
    int CommissionCount,
    DateTimeOffset PaidAt,
    IReadOnlyList<PaidCommission> Commissions);

public sealed record PagedResult<T>(
    int PageNumber,
    int PageSize,
    int Total,
    IReadOnlyList<T> Items);

public static class WalletEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapWallets(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/wallets/{externalId}/balance", GetBalance).WithTags("Wallets");
        app.MapGet("/api/wallets/{externalId}/payouts", GetPayouts).WithTags("Wallets");
        app.MapPost("/api/admin/payouts/run", RunPayouts).WithTags("Admin");
        return app;
    }

    private static async Task<IResult> GetBalance(string externalId, WalletDbContext db, CancellationToken ct)
    {
        var balance = await db.Wallets.AsNoTracking()
            .Where(w => w.UserExternalId == externalId)
            .Select(w => w.Balance)
            .SingleOrDefaultAsync(ct);

        var pending = await db.Accruals.AsNoTracking()
            .Where(a => a.UserExternalId == externalId && a.Status == AccrualStatus.Pending)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        return Results.Ok(new BalanceResponse(externalId, balance, pending));
    }

    private static async Task<IResult> GetPayouts(
        string externalId, WalletDbContext db, CancellationToken ct, int page = 1, int size = 20)
    {
        if (page < 1 || size < 1 || size > MaxPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = [$"page must be >= 1 and size between 1 and {MaxPageSize}."]
            });
        }

        var query = db.Payouts.AsNoTracking().Where(p => p.UserExternalId == externalId);
        var total = await query.CountAsync(ct);
        var payouts = await query
            .OrderByDescending(p => p.PaidAt).ThenBy(p => p.Id)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        var payoutIds = payouts.Select(p => p.Id).ToList();
        var commissions = await db.Accruals.AsNoTracking()
            .Where(a => a.PayoutId != null && payoutIds.Contains(a.PayoutId.Value))
            .OrderBy(a => a.AccruedAt)
            .ToListAsync(ct);
        var byPayout = commissions.ToLookup(a => a.PayoutId!.Value);

        var items = payouts.Select(p => new PayoutDetail(
            p.Id, p.Amount, p.CommissionCount, p.PaidAt,
            byPayout[p.Id].Select(a => new PaidCommission(
                CommissionId: a.CommissionId,
                EventExternalId: a.EventExternalId,
                Level: a.Level,
                Amount: a.Amount,
                SchemaType: a.SchemaType,
                AccruedAt: a.AccruedAt)).ToList()))
            .ToList();

        return Results.Ok(new PagedResult<PayoutDetail>(page, size, total, items));
    }

    /// <summary>
    /// Manually run payouts.
    /// </summary>
    private static async Task<IResult> RunPayouts(PayoutRunner runner, CancellationToken ct) =>
        Results.Ok(await runner.RunAsync(ct));
}
