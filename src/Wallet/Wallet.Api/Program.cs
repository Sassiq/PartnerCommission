using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging;
using PartnerCommission.ServiceDefaults;
using Scalar.AspNetCore;
using Serilog;
using Wallet.Api.Data;
using Wallet.Api.Messaging;
using Wallet.Api.Payouts;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.AddServiceDefaults("wallet");

builder.Services.AddDbContext<WalletDbContext>(options => options
    .UseNpgsql(config.GetConnectionString("WalletDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddKafkaMessaging(config);
builder.Services.AddOutboxPublisher<WalletDbContext>(config);
builder.Services.AddKafkaConsumer<CommissionAccrued, CommissionAccruedHandler>(o =>
{
    o.Topic = Topics.CommissionAccrued;
    o.GroupId = "wallet.commission-accrued";
    o.DeadLetterTopic = Topics.CommissionAccruedDlt;
});

// Periodic payout of accumulated commissions into wallets.
builder.Services.Configure<PayoutOptions>(config.GetSection(PayoutOptions.SectionName));
builder.Services.AddSingleton<PayoutRunner>();
builder.Services.AddHostedService<PayoutBackgroundService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<WalletDbContext>("postgres", tags: [ServiceDefaultsExtensions.ReadyTag])
    .AddKafkaHealthCheck(ServiceDefaultsExtensions.ReadyTag);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

await app.RunAsync();

public partial class Program;
