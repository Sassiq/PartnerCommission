using System.ComponentModel.DataAnnotations;
using Commission.Api.Data;
using Commission.Api.Partners;
using Commission.Domain;
using Commission.Domain.Entities;
using Commission.Domain.Schemes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging.Consuming;
using PartnerCommission.Messaging.Outbox;

namespace Commission.Api.Messaging;

public sealed class CommissionOptions
{
    public const string SectionName = "Commission";

    [Range(1, CommissionLimits.MaxLevels)]
    public int MaxLevels { get; set; } = 10;
}

public sealed class ProfitEventHandler(
    CommissionDbContext db,
    IAncestorsProvider ancestors,
    IOptions<CommissionOptions> options,
    ILogger<ProfitEventHandler> logger) : IMessageHandler<ProfitEventReceived>
{
    public async Task HandleAsync(ProfitEventReceived message, MessageContext context, CancellationToken ct)
    {
        if (!ProfitValidation.IsValid(message.Profit))
        {
            throw new PermanentMessageException($"Profit {message.Profit} does not fit {MoneyFormat.Scale} decimals / numeric({MoneyFormat.Precision},{MoneyFormat.Scale}).");
        }

        if (await db.ProfitEvents.AnyAsync(e => e.ExternalId == message.ExternalId, ct))
        {
            logger.LogInformation("Event {EventId} was already processed; skipping", message.ExternalId);
            return;
        }

        var maxLevels = options.Value.MaxLevels;

        var chain = message.Profit > 0
            ? await ancestors.GetAncestorsAsync(message.UserExternalId, maxLevels, ct)
            : [];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var schema = (await db.Settings.AsNoTracking().SingleAsync(ct)).ActiveSchema;
        var calculated = CommissionCalculator.Calculate(CommissionSchemes.For(schema), message.Profit, chain, maxLevels);

        var now = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();

        // Insert and do nothing at conflict
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO profit_events (id, external_id, user_external_id, profit, occurred_at, processed_at)
            VALUES ({eventId}, {message.ExternalId}, {message.UserExternalId}, {message.Profit}, {message.OccurredAt.ToUniversalTime()}, {now})
            ON CONFLICT (external_id) DO NOTHING
            """, ct);

        if (inserted == 0)
        {
            logger.LogInformation("Event {EventId} was processed concurrently; skipping", message.ExternalId);
            return;
        }

        foreach (var c in calculated)
        {
            var commission = CommissionRecord.From(c, eventId, now);
            db.Commissions.Add(commission);
            db.AddToOutbox(Topics.CommissionAccrued, commission.PartnerExternalId, new CommissionAccrued(
                commission.Id, message.ExternalId, commission.PartnerExternalId, commission.Level,
                commission.Amount, commission.SchemaType, commission.AccruedAt));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        CommissionMetrics.EventProcessed();
        foreach (var c in calculated)
        {
            CommissionMetrics.Accrued(c.SchemaType, c.Amount);
        }

        logger.LogInformation("Event {EventId} of {User}: profit {Profit}, {Count} commissions ({Schema})",
            message.ExternalId, message.UserExternalId, message.Profit, calculated.Count, schema);
    }
}
