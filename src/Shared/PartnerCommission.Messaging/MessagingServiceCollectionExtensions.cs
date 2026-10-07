using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PartnerCommission.Messaging.Consuming;
using PartnerCommission.Messaging.Outbox;

namespace PartnerCommission.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.TryAddSingleton<KafkaProducer>();
        return services;
    }

    public static IServiceCollection AddOutboxPublisher<TDbContext>(this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddHostedService<OutboxPublisher<TDbContext>>();
        return services;
    }

    public static IServiceCollection AddKafkaConsumer<TMessage, THandler>(
        this IServiceCollection services, Action<ConsumerOptions> configure)
        where THandler : class, IMessageHandler<TMessage>
    {
        var options = new ConsumerOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.Topic) || string.IsNullOrWhiteSpace(options.GroupId) ||
            string.IsNullOrWhiteSpace(options.DeadLetterTopic))
        {
            throw new ArgumentException("Topic, GroupId and DeadLetterTopic are required.", nameof(configure));
        }

        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        services.AddSingleton<IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<KafkaConsumerService<TMessage>>(sp, options));
        return services;
    }

    public static IHealthChecksBuilder AddKafkaHealthCheck(this IHealthChecksBuilder builder, params string[] tags) =>
        builder.AddCheck<KafkaHealthCheck>("kafka", tags: tags);
}

internal sealed class KafkaHealthCheck(IOptions<KafkaOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.Value.BootstrapServers }).Build();
            admin.GetMetadata(TimeSpan.FromSeconds(3));
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka is unreachable", ex));
        }
    }
}
