using System.Text.Json.Serialization;
using Commission.Api.Data;
using Commission.Api.Endpoints;
using Commission.Api.Messaging;
using Commission.Api.Partners;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Contracts.Partners;
using PartnerCommission.Messaging;
using PartnerCommission.ServiceDefaults;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.AddServiceDefaults("commission");

builder.Services.AddDbContext<CommissionDbContext>(options => options
    .UseNpgsql(config.GetConnectionString("CommissionDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddOptions<CommissionOptions>()
    .Bind(config.GetSection(CommissionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddKafkaMessaging(config);
builder.Services.AddOutboxPublisher<CommissionDbContext>(config);
builder.Services.AddKafkaConsumer<ProfitEventReceived, ProfitEventHandler>(o =>
{
    o.Topic = Topics.ProfitEvents;
    o.GroupId = "commission.profit-events";
    o.DeadLetterTopic = Topics.ProfitEventsDlt;
});
builder.Services.AddKafkaConsumer<CommissionsPaid, CommissionsPaidHandler>(o =>
{
    o.Topic = Topics.CommissionsPaid;
    o.GroupId = "commission.commissions-paid";
    o.DeadLetterTopic = Topics.CommissionsPaidDlt;
});

builder.Services
    .AddGrpcClient<PartnersInternal.PartnersInternalClient>(o =>
        o.Address = new Uri(config["Services:Partners:GrpcUrl"]
                            ?? throw new InvalidOperationException("Services:Partners:GrpcUrl is not configured.")))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });
builder.Services.AddScoped<IAncestorsProvider, GrpcAncestorsProvider>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CommissionDbContext>("postgres", tags: [ServiceDefaultsExtensions.ReadyTag])
    .AddKafkaHealthCheck(ServiceDefaultsExtensions.ReadyTag);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddValidation();
builder.Services.AddOpenApi();

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CommissionDbContext>().Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapEvents();
app.MapAdmin();

await app.RunAsync();

public partial class Program;
