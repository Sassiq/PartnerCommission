using System.ComponentModel.DataAnnotations;

namespace Commission.Api.Models;

public sealed record ProfitEventRequest(
    [Required, StringLength(200)] string ExternalId,
    [Required, StringLength(200)] string UserExternalId,
    decimal Profit,
    DateTimeOffset? OccurredAt);
