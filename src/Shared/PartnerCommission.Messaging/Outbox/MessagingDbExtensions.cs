using Microsoft.EntityFrameworkCore;

namespace PartnerCommission.Messaging.Outbox;

public static class MessagingDbExtensions
{
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(m => m.Topic).HasColumnName("topic").HasMaxLength(200).IsRequired();
            e.Property(m => m.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
            e.Property(m => m.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
            e.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.PublishedAt).HasColumnName("published_at");

            // Index to find rows which are waiting to be published.
            e.HasIndex(m => m.CreatedAt).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_messages_unpublished");
        });

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox_messages");
            e.HasKey(m => m.MessageId);
            e.Property(m => m.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });

        return modelBuilder;
    }

    /// <summary>
    /// Queues a message for Kafka.
    /// </summary>
    public static OutboxMessage AddToOutbox<T>(this DbContext db, string topic, string key, T message)
    {
        var outbox = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Topic = topic,
            Key = key,
            Type = typeof(T).Name,
            Payload = MessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Set<OutboxMessage>().Add(outbox);
        return outbox;
    }

    /// <summary>
    /// Checks if the message has already been processed.
    /// </summary>
    public static async Task<bool> TryMarkProcessedAsync(
        this DbContext db, Guid messageId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var inserted = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO inbox_messages (message_id, processed_at) VALUES ({messageId}, {now}) ON CONFLICT (message_id) DO NOTHING",
            ct);
        return inserted == 1;
    }
}
