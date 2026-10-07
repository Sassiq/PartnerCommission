namespace PartnerCommission.Messaging.Outbox;

/// <summary>
/// A message to be published to Kafka (outbox pattern)
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = "";
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// Remembers handled messages so a redelivered message is applied only once.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
