using System.ComponentModel.DataAnnotations;

namespace Partners.Api.Models;

public sealed record CreateUserRequest(
    [Required, StringLength(200)] string ExternalId);
