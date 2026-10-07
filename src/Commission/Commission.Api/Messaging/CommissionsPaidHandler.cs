using Commission.Api.Data;
using Commission.Domain;
using Commission.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging.Consuming;
using PartnerCommission.Messaging.Outbox;

namespace Commission.Api.Messaging;

/// <summary>
/// Marks commissions as paid once the wallet reports a payout.
/// </summary>
public sealed class CommissionsPaidHandler(
    CommissionDbContext db, ILogger<CommissionsPaidHandler> logger) : IMessageHandler<CommissionsPaid>
{
    public async Task HandleAsync(CommissionsPaid message, MessageContext context, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (context.MessageId is { } messageId && !await db.TryMarkProcessedAsync(messageId, ct))
        {
            logger.LogInformation("Payout notification {MessageId} already applied; skipping", messageId);
            return;
        }

        var ids = message.CommissionIds.ToArray();
        var updated = await db.Commissions
            .Where(c => ids.Contains(c.Id) && c.Status != CommissionStatus.Paid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CommissionStatus.Paid)
                .SetProperty(c => c.PaidAt, message.PaidAt.ToUniversalTime())
                .SetProperty(c => c.PayoutId, message.PayoutId), ct);

        await transaction.CommitAsync(ct);
        logger.LogInformation("Payout {PayoutId} of {User}: {Updated}/{Total} commissions marked paid",
            message.PayoutId, message.UserExternalId, updated, ids.Length);
    }
}
