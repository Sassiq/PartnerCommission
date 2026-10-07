using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PartnerCommission.Messaging.Consuming;

/// <summary>
/// One consumer-group member for one topic. Instances of the same service share the partitions.
/// </summary>
public sealed class KafkaConsumerService<TMessage> : BackgroundService
{
    private static readonly TimeSpan DeadLetterRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ConsumerOptions _options;
    private readonly KafkaOptions _kafka;
    private readonly IServiceScopeFactory _scopes;
    private readonly KafkaProducer _producer;
    private readonly ILogger<KafkaConsumerService<TMessage>> _logger;
    private readonly RetryPolicy _retryPolicy;

    public KafkaConsumerService(
        ConsumerOptions options,
        IOptions<KafkaOptions> kafka,
        IServiceScopeFactory scopes,
        KafkaProducer producer,
        ILogger<KafkaConsumerService<TMessage>> logger)
    {
        _options = options;
        _kafka = kafka.Value;
        _scopes = scopes;
        _producer = producer;
        _logger = logger;
        _retryPolicy = new RetryPolicy(options.MaxAttempts, options.BaseDelay, options.MaxDelay);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,  // commits periodically offsets we stored ourselves after handling
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            // Transient retries can pause a partition for a long time
            MaxPollIntervalMs = 600_000
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogWarning("Kafka consumer error: {Reason}", e.Reason))
            .SetPartitionsAssignedHandler((_, parts) =>
                _logger.LogInformation("Consumer {Group} assigned {Partitions}", _options.GroupId, string.Join(",", parts)))
            .SetPartitionsRevokedHandler((_, parts) =>
                _logger.LogInformation("Consumer {Group} revoked {Partitions}", _options.GroupId, string.Join(",", parts.Select(p => p.TopicPartition))))
            .Build();

        consumer.Subscribe(_options.Topic);
        _logger.LogInformation("Consuming {Topic} as group {Group}", _options.Topic, _options.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume failed: {Reason}", ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                if (await ProcessAsync(result, stoppingToken))
                {
                    consumer.StoreOffset(result);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        finally
        {
            consumer.Close();
            _logger.LogInformation("Consumer {Group} for {Topic} stopped", _options.GroupId, _options.Topic);
        }
    }

    /// <returns>True when the message is finished (handled or dead-lettered) and its offset may be stored.</returns>
    private async Task<bool> ProcessAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        var topicTag = new KeyValuePair<string, object?>("topic", _options.Topic);

        TMessage message;
        try
        {
            message = MessageSerializer.Deserialize<TMessage>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Cannot deserialize message at {Tpo}; dead-lettering", result.TopicPartitionOffset);
            return await DeadLetterAsync(result, ex, attempts: 1, stoppingToken);
        }

        var messageId = ReadHeader(result, MessageHeaders.MessageId) is { } id && Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var context = new MessageContext(
                    result.Topic, result.Partition.Value, result.Offset.Value, result.Message.Key, messageId, attempt);

                await using var scope = _scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<TMessage>>();

                // CancellationToken.None: once a message is being handled it is finished, so shutdown never leaves it half-applied.
                await handler.HandleAsync(message, context, CancellationToken.None);

                MessagingMetrics.Consumed.Add(1, topicTag);
                return true;
            }
            catch (Exception ex)
            {
                MessagingMetrics.Failed.Add(1, topicTag);
                var decision = _retryPolicy.Decide(ex, attempt);

                if (decision.DeadLetter)
                {
                    _logger.LogError(ex, "Message at {Tpo} failed on attempt {Attempt}; dead-lettering", result.TopicPartitionOffset, attempt);
                    return await DeadLetterAsync(result, ex, attempt, stoppingToken);
                }

                _logger.LogWarning(ex, "Message at {Tpo} failed on attempt {Attempt}; retrying in {Delay}",
                    result.TopicPartitionOffset, attempt, decision.Delay);

                try
                {
                    await Task.Delay(decision.Delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return false; // shutting down: leave the offset alone, the message is redelivered after restart
                }
            }
        }
    }

    private async Task<bool> DeadLetterAsync(ConsumeResult<string, string> result, Exception error, int attempts, CancellationToken stoppingToken)
    {
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.DeadLetterError] = Truncate(error.Message, 1000),
            [MessageHeaders.DeadLetterExceptionType] = error.GetType().FullName ?? error.GetType().Name,
            [MessageHeaders.DeadLetterOriginalTopic] = result.Topic,
            [MessageHeaders.DeadLetterOriginalPartition] = result.Partition.Value.ToString(),
            [MessageHeaders.DeadLetterOriginalOffset] = result.Offset.Value.ToString(),
            [MessageHeaders.DeadLetterAttempts] = attempts.ToString()
        };
        foreach (var name in new[] { MessageHeaders.MessageId, MessageHeaders.MessageType })
        {
            if (ReadHeader(result, name) is { } value)
            {
                headers[name] = value;
            }
        }

        // Never drop a message: if the dead-letter topic is unreachable, keep trying.
        while (true)
        {
            try
            {
                await _producer.ProduceAsync(_options.DeadLetterTopic, result.Message.Key, result.Message.Value, headers, stoppingToken);
                MessagingMetrics.DeadLettered.Add(1, new KeyValuePair<string, object?>("topic", _options.Topic));
                return true;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Could not write to dead-letter topic {Topic}; retrying", _options.DeadLetterTopic);
                try
                {
                    await Task.Delay(DeadLetterRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    private static string? ReadHeader(ConsumeResult<string, string> result, string name) =>
        result.Message.Headers is { } headers && headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
