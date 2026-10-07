using System.Diagnostics.Metrics;

namespace PartnerCommission.Messaging;

internal static class MessagingMetrics
{
    public const string MeterName = "PartnerCommission.Messaging";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Consumed = Meter.CreateCounter<long>("messaging.consumed", description: "Messages handled successfully");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("messaging.failed", description: "Handler failures (each attempt)");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("messaging.dead_lettered", description: "Messages sent to a dead-letter topic");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("outbox.published", description: "Outbox messages delivered to Kafka");
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("outbox.publish_failed", description: "Outbox messages that failed to deliver (will be retried)");
}
