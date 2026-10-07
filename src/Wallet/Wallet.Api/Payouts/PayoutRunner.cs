using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging.Outbox;
using Wallet.Api.Data;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace Wallet.Api.Payouts;

public sealed class PayoutOptions
{
    public const string SectionName = "Payout";

    /// <summary>
    /// Interval which indicates how often pending commissions are paid into wallets.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 200;
}

public sealed record PayoutRunResult(int Payouts, decimal TotalAmount);

/// <summary>
/// Pays pending commissions into wallets.
/// </summary>
public sealed class PayoutRunner(
    IServiceScopeFactory scopes,
    IOptions<PayoutOptions> options,
    ILogger<PayoutRunner> logger)
{
    public async Task<PayoutRunResult> RunAsync(CancellationToken ct)
    {
        int payouts = 0;
        decimal total = 0;

        int paidThisRound;
        do
        {
            paidThisRound = 0;
            foreach (var user in await FindUsersWithPendingAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                var payout = await PayUserAsync(user, ct);
                if (payout is not null)
                {
                    paidThisRound++;
                    payouts++;
                    total += payout.Amount;
                }
            }
        }
        while (paidThisRound > 0 && !ct.IsCancellationRequested);

        if (payouts > 0)
        {
            logger.LogInformation("Paid {Payouts} payouts, {Total} in total", payouts, total);
        }

        return new PayoutRunResult(payouts, total);
    }

    private async Task<List<string>> FindUsersWithPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Accruals.AsNoTracking()
            .Where(a => a.Status == AccrualStatus.Pending)
            .Select(a => a.UserExternalId)
            .Distinct()
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);
    }

    private async Task<Payout?> PayUserAsync(string userExternalId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var pendingStatus = (int)AccrualStatus.Pending;

        //FOR UPDATE SKIP LOCKED so several service instances never pay the same commission twice.
        var pending = await db.Accruals
            .FromSql($"""
                SELECT * FROM accruals
                WHERE user_external_id = {userExternalId} AND status = {pendingStatus}
                ORDER BY accrued_at
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return null; // already paid, or being paid by another instance
        }

        var now = DateTimeOffset.UtcNow;
        var payout = Payout.Create(pending, now);
        db.Payouts.Add(payout);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO wallets (user_external_id, balance, updated_at)
            VALUES ({userExternalId}, {payout.Amount}, {now})
            ON CONFLICT (user_external_id)
            DO UPDATE SET balance = wallets.balance + EXCLUDED.balance, updated_at = EXCLUDED.updated_at
            """, ct);

        db.AddToOutbox(Topics.CommissionsPaid, userExternalId, new CommissionsPaid(
            payout.Id, userExternalId, pending.Select(a => a.CommissionId).ToList(), payout.Amount, now));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        PayoutMetrics.PaidOut(payout.CommissionCount, payout.Amount);
        return payout;
    }
}

/// <summary>
/// Service which runs PayoutRunner periodically.
/// </summary>
public sealed class PayoutBackgroundService(
    PayoutRunner runner, IOptions<PayoutOptions> options, ILogger<PayoutBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Payout job started, interval {Interval}", options.Value.Interval);

        using var timer = new PeriodicTimer(options.Value.Interval);
        try
        {
            do
            {
                try
                {
                    await runner.RunAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Payout round failed; will retry at the next interval");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }

        logger.LogInformation("Payout job stopped");
    }
}
