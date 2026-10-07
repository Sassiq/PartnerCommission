using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging.Consuming;
using Wallet.Api.Data;
using Wallet.Domain;
using Wallet.Domain.Entities;

namespace Wallet.Api.Messaging;

/// <summary>
/// Stores an accrued commission as pending.
/// </summary>
public sealed class CommissionAccruedHandler(
    WalletDbContext db, ILogger<CommissionAccruedHandler> logger) : IMessageHandler<CommissionAccrued>
{
    public async Task HandleAsync(CommissionAccrued message, MessageContext context, CancellationToken ct)
    {
        if (message.Amount <= 0 || string.IsNullOrWhiteSpace(message.PartnerExternalId))
        {
            throw new PermanentMessageException($"Invalid commission {message.CommissionId}: amount {message.Amount}.");
        }

        var schemaType = (int)message.SchemaType;
        var pending = (int)AccrualStatus.Pending;

        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO accruals (commission_id, user_external_id, event_external_id, level, amount, schema_type, accrued_at, status)
            VALUES ({message.CommissionId}, {message.PartnerExternalId}, {message.EventExternalId}, {message.Level},
                    {message.Amount}, {schemaType}, {message.AccruedAt.ToUniversalTime()}, {pending})
            ON CONFLICT (commission_id) DO NOTHING
            """, ct);

        if (inserted == 0)
        {
            logger.LogInformation("Commission {CommissionId} was already received; skipping", message.CommissionId);
        }
    }
}
