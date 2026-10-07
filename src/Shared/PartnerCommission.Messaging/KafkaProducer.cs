using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PartnerCommission.Messaging;

public sealed class KafkaProducer : IDisposable
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private readonly IProducer<string, string> _producer;

    public KafkaProducer(IOptions<KafkaOptions> options, ILogger<KafkaProducer> logger)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 15_000,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => logger.LogWarning("Kafka producer error: {Reason} (fatal: {IsFatal})", e.Reason, e.IsFatal))
            .Build();
    }

    public async Task ProduceAsync(
        string topic, string key, string value, IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        var message = new Message<string, string> { Key = key, Value = value, Headers = new Headers() };
        if (headers is not null)
        {
            foreach (var (name, headerValue) in headers)
            {
                message.Headers.Add(name, Encoding.UTF8.GetBytes(headerValue));
            }
        }

        await _producer.ProduceAsync(topic, message, ct);
    }

    public void Dispose()
    {
        _producer.Flush(FlushTimeout);
        _producer.Dispose();
    }
}
