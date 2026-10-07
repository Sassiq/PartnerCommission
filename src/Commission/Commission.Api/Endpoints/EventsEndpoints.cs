using System.ComponentModel.DataAnnotations;
using Commission.Api.Data;
using Commission.Domain;
using Commission.Domain.Entities;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging;

namespace Commission.Api.Endpoints;

public sealed record ProfitEventRequest(
    [Required, StringLength(200)] string ExternalId,
    [Required, StringLength(200)] string UserExternalId,
    decimal Profit,
    DateTimeOffset? OccurredAt);

public sealed record EventSummary(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt);

public sealed record PagedResult<T>(
    int PageNumber,
    int PageSize,
    int Total,
    IReadOnlyList<T> Items);

public sealed record CommissionDetail(
    Guid Id,
    string PartnerExternalId,
    int Level,
    decimal Amount,
    SchemaType SchemaType,
    CommissionStatus Status,
    DateTimeOffset AccruedAt,
    DateTimeOffset? PaidAt,
    Guid? PayoutId);

public sealed record EventDetail(
    string ExternalId,
    string UserExternalId,
    decimal Profit,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt,
    IReadOnlyList<CommissionDetail> Commissions);

public static class EventsEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/events", ReceiveEvent).WithTags("Events");
        app.MapGet("/api/users/{externalId}/events", GetUserEvents).WithTags("Events");
        app.MapGet("/api/events/{externalId}", GetEvent).WithTags("Events");
        return app;
    }

    /// <summary>
    /// Accepts the event by writing it to Kafka.
    /// </summary>
    private static async Task<IResult> ReceiveEvent(
        ProfitEventRequest request, KafkaProducer producer, CancellationToken ct)
    {
        if (!ProfitValidation.IsValid(request.Profit))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["profit"] = [$"Profit must have at most {MoneyFormat.Scale} decimals and be below 10^18 in absolute value."]
            });
        }

        var message = new ProfitEventReceived(
            request.ExternalId, request.UserExternalId, request.Profit,
            (request.OccurredAt ?? DateTimeOffset.UtcNow).ToUniversalTime());

        try
        {
            await producer.ProduceAsync(
                Topics.ProfitEvents, message.UserExternalId, MessageSerializer.Serialize(message),
                new Dictionary<string, string> { [MessageHeaders.MessageType] = nameof(ProfitEventReceived) }, ct);
        }
        catch (KafkaException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Event was not accepted",
                detail: "The message broker is unavailable. Retry with the same externalId; duplicates are ignored.");
        }

        return Results.Accepted($"/api/events/{Uri.EscapeDataString(message.ExternalId)}", new { message.ExternalId });
    }

    private static async Task<IResult> GetUserEvents(
        string externalId, CommissionDbContext db, CancellationToken ct, int page = 1, int size = 20)
    {
        if (page < 1 || size < 1 || size > MaxPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = [$"page must be >= 1 and size between 1 and {MaxPageSize}."]
            });
        }

        var query = db.ProfitEvents.AsNoTracking().Where(e => e.UserExternalId == externalId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new EventSummary(e.ExternalId, e.UserExternalId, e.Profit, e.OccurredAt, e.ProcessedAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<EventSummary>(page, size, total, items));
    }

    private static async Task<IResult> GetEvent(string externalId, CommissionDbContext db, CancellationToken ct)
    {
        var evt = await db.ProfitEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ExternalId == externalId, ct);
        if (evt is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Event not found",
                detail: $"Event '{externalId}' does not exist (it may still be waiting to be processed).");
        }

        var commissions = await db.Commissions.AsNoTracking()
            .Where(c => c.EventId == evt.Id)
            .OrderBy(c => c.Level)
            .Select(c => new CommissionDetail(
                c.Id, c.PartnerExternalId, c.Level, c.Amount, c.SchemaType, c.Status, c.AccruedAt, c.PaidAt, c.PayoutId))
            .ToListAsync(ct);

        return Results.Ok(new EventDetail(
            evt.ExternalId, evt.UserExternalId, evt.Profit, evt.OccurredAt, evt.ProcessedAt, commissions));
    }
}
