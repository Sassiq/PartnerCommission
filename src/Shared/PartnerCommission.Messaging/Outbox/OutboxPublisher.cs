using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PartnerCommission.Messaging.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long published rows are kept before they are deleted.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Delivers outbox rows to Kafka.
/// </summary>
public sealed class OutboxPublisher<TDbContext>(
    IServiceScopeFactory scopes,
    KafkaProducer producer,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox publisher for {DbContext} started", typeof(TDbContext).Name);

        using var timer = new PeriodicTimer(_options.PollInterval);
        try
        {
            do
            {
                try
                {
                    int published;
                    do
                    {
                        published = await PublishBatchAsync(stoppingToken);
                    }
                    while (published == _options.BatchSize && !stoppingToken.IsCancellationRequested);

                    await CleanupIfDueAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Outbox publishing cycle failed; will retry");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }

        logger.LogInformation("Outbox publisher for {DbContext} stopped", typeof(TDbContext).Name);
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED, so any number of service instances can run this publisher
        var batch = await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE published_at IS NULL
                ORDER BY created_at, id
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            return 0;
        }

        var sends = batch
            .Select(m => (Message: m, Task: producer.ProduceAsync(
                m.Topic, m.Key, m.Payload,
                new Dictionary<string, string>
                {
                    [MessageHeaders.MessageId] = m.Id.ToString(),
                    [MessageHeaders.MessageType] = m.Type
                },
                ct)))
            .ToList();

        var published = 0;
        Exception? firstFailure = null;
        foreach (var (message, task) in sends)
        {
            try
            {
                await task;
                message.PublishedAt = DateTimeOffset.UtcNow;
                published++;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                MessagingMetrics.OutboxFailed.Add(1, new KeyValuePair<string, object?>("topic", message.Topic));
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);

        if (published > 0)
        {
            MessagingMetrics.OutboxPublished.Add(published);
        }

        if (firstFailure is not null && !ct.IsCancellationRequested)
        {
            logger.LogWarning(firstFailure,
                "Outbox: {Failed} of {Total} messages were not delivered and stay queued for the next attempt",
                batch.Count - published, batch.Count);
        }

        return published == batch.Count ? published : 0;
    }

    private async Task CleanupIfDueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextCleanup)
        {
            return;
        }

        _nextCleanup = now + _options.CleanupInterval;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var cutoff = now - _options.Retention;
        var deleted = await db.Set<OutboxMessage>()
            .Where(m => m.PublishedAt != null && m.PublishedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation("Outbox cleanup removed {Count} published messages", deleted);
        }
    }
}
