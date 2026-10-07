using System.ComponentModel.DataAnnotations;

namespace Partners.Api.Models;

public sealed record SetPartnerRequest(
    [StringLength(200)] string? PartnerExternalId);
