using Commission.Api.Data;
using Commission.Api.Models;
using Commission.Domain;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;
using PartnerCommission.Contracts.Events;
using PartnerCommission.Messaging;

namespace Commission.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class EventsController(CommissionDbContext db, KafkaProducer producer) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Accepts the event by writing it to Kafka.
    /// </summary>
    [HttpPost("events")]
    public async Task<IActionResult> ReceiveEvent(ProfitEventRequest request, CancellationToken ct)
    {
        if (!ProfitValidation.IsValid(request.Profit))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["profit"] = [$"Profit must have at most {MoneyFormat.Scale} decimals and be below 10^18 in absolute value."]
            }));
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
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Event was not accepted",
                detail: "The message broker is unavailable. Retry with the same externalId; duplicates are ignored.");
        }

        return Accepted($"/api/events/{Uri.EscapeDataString(message.ExternalId)}", new { message.ExternalId });
    }

    [HttpGet("users/{externalId}/events")]
    public async Task<ActionResult<PagedResult<EventSummaryResponse>>> GetUserEvents(
        string externalId, CancellationToken ct, int page = 1, int size = 20)
    {
        if (page < 1 || size < 1 || size > MaxPageSize)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["page"] = [$"page must be >= 1 and size between 1 and {MaxPageSize}."]
            }));
        }

        var query = db.ProfitEvents.AsNoTracking().Where(e => e.UserExternalId == externalId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new EventSummaryResponse(e.ExternalId, e.UserExternalId, e.Profit, e.OccurredAt, e.ProcessedAt))
            .ToListAsync(ct);

        return new PagedResult<EventSummaryResponse>(page, size, total, items);
    }

    [HttpGet("events/{externalId}")]
    public async Task<ActionResult<EventDetailResponse>> GetEvent(string externalId, CancellationToken ct)
    {
        var evt = await db.ProfitEvents.AsNoTracking().SingleOrDefaultAsync(e => e.ExternalId == externalId, ct);
        if (evt is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Event not found",
                detail: $"Event '{externalId}' does not exist (it may still be waiting to be processed).");
        }

        var commissions = await db.Commissions.AsNoTracking()
            .Where(c => c.EventId == evt.Id)
            .OrderBy(c => c.Level)
            .Select(c => new CommissionResponse(
                c.Id, c.PartnerExternalId, c.Level, c.Amount, c.SchemaType, c.Status, c.AccruedAt, c.PaidAt, c.PayoutId))
            .ToListAsync(ct);

        return new EventDetailResponse(
            evt.ExternalId, evt.UserExternalId, evt.Profit, evt.OccurredAt, evt.ProcessedAt, commissions);
    }
}
