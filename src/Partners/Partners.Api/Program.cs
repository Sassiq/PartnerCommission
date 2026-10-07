using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Partners.Api.Data;
using Partners.Api.GrpcServices;
using PartnerCommission.ServiceDefaults;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("partners");

builder.Services.AddDbContext<PartnersDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("PartnersDb"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<TreeQueries>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PartnersDbContext>("postgres", tags: [ServiceDefaultsExtensions.ReadyTag]);

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenApi();
builder.Services.AddGrpc();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PartnersDbContext>().Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();
app.MapGrpcService<PartnersInternalService>();

await app.RunAsync();

public partial class Program;
