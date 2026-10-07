using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wallet.Api.Data;
using Wallet.Api.Models;
using Wallet.Domain.Entities;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/wallets/{externalId}")]
public sealed class WalletsController(WalletDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(string externalId, CancellationToken ct)
    {
        var balance = await db.Wallets.AsNoTracking()
            .Where(w => w.UserExternalId == externalId)
            .Select(w => w.Balance)
            .SingleOrDefaultAsync(ct);

        var pending = await db.Accruals.AsNoTracking()
            .Where(a => a.UserExternalId == externalId && a.Status == AccrualStatus.Pending)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        return new BalanceResponse(externalId, balance, pending);
    }

    [HttpGet("payouts")]
    public async Task<ActionResult<PagedResult<PayoutResponse>>> GetPayouts(
        string externalId, CancellationToken ct, int page = 1, int size = 20)
    {
        if (page < 1 || size < 1 || size > MaxPageSize)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["page"] = [$"page must be >= 1 and size between 1 and {MaxPageSize}."]
            }));
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

        var items = payouts.Select(p => new PayoutResponse(
            p.Id, p.Amount, p.CommissionCount, p.PaidAt,
            byPayout[p.Id].Select(a => new PaidCommissionResponse(
                CommissionId: a.CommissionId,
                EventExternalId: a.EventExternalId,
                Level: a.Level,
                Amount: a.Amount,
                SchemaType: a.SchemaType,
                AccruedAt: a.AccruedAt)).ToList()))
            .ToList();

        return new PagedResult<PayoutResponse>(page, size, total, items);
    }
}
