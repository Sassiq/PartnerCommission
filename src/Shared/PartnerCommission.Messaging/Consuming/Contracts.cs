namespace PartnerCommission.Messaging.Consuming;

/// <summary>
/// Handles one deserialized message.
/// </summary>
public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct);
}

/// <param name="Attempt">1 for the first try, then 2, 3, ... after failures.</param>
public sealed record MessageContext(
    string Topic,
    int Partition,
    long Offset,
    string Key,
    Guid? MessageId,
    int Attempt);

/// <summary>
/// Throw when retrying can never help. The message goes to the dead-letter topic.
/// </summary>
public sealed class PermanentMessageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Throw when a dependency is temporarily unavailable.
/// The message is retried with backoff without attempts limit.
/// </summary>
public sealed class TransientMessageException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ConsumerOptions
{
    public string Topic { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string DeadLetterTopic { get; set; } = "";

    /// <summary>
    /// Attempts for an ordinary exception before the message is dead-lettered. Transient failures ignore this limit
    /// </summary>
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}
