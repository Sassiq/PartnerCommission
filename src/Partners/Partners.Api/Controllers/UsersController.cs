using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Partners.Api.Data;
using Partners.Api.Models;
using Partners.Domain;
using UserEntity = Partners.Domain.Entities.User; // ControllerBase already has a User property

namespace Partners.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(PartnersDbContext db, TreeQueries tree) : ControllerBase
{
    public const int DefaultTreeDepth = 10;
    public const int MaxTreeDepth = 100;

    // Need this lock to perform two concurrent updates when setting partners.
    // Without this lock we can get in loop of partners
    private const string PartnerChangeLockName = "partners.partner-change";

    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        var user = UserEntity.Create(request.ExternalId, DateTimeOffset.UtcNow);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "User already exists",
                detail: $"User '{request.ExternalId}' already exists.");
        }

        return Created(
            $"/api/users/{Uri.EscapeDataString(user.ExternalId)}",
            new UserResponse(user.ExternalId, null, user.CreatedAt));
    }

    [HttpGet("{externalId}")]
    public async Task<ActionResult<UserResponse>> GetUser(string externalId, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.ExternalId == externalId)
            .Select(u => new UserResponse(
                u.ExternalId,
                db.Users.Where(r => r.Id == u.PartnerId).Select(r => r.ExternalId).FirstOrDefault(),
                u.CreatedAt))
            .SingleOrDefaultAsync(ct);

        if (user is null)
        {
            return UserNotFound(externalId);
        }

        return user;
    }

    [HttpPut("{externalId}/partner")]
    public async Task<IActionResult> SetPartner(string externalId, SetPartnerRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Need this lock to perform two concurrent updates when setting partners.
        // Without this lock we can get in loop of partners
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({PartnerChangeLockName}, 0))", ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is null)
        {
            return UserNotFound(externalId);
        }

        if (request.PartnerExternalId is null)
        {
            user.PartnerId = null;
        }
        else
        {
            var partner = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == request.PartnerExternalId, ct);
            if (partner is null)
            {
                return UserNotFound(request.PartnerExternalId);
            }

            var ancestors = await tree.GetAncestorsAsync(partner.ExternalId, TreeQueries.TreeDepthLimit, ct);
            try
            {
                PartnerLinkGuard.EnsureCanLink(user.ExternalId, partner.ExternalId, ancestors.Select(a => a.ExternalId));
            }
            catch (InvalidPartnerLinkException ex)
            {
                return Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Invalid partner link",
                    detail: ex.Message);
            }

            user.PartnerId = partner.Id;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("{externalId}/tree")]
    public async Task<ActionResult<TreeResponse>> GetTree(
        string externalId, CancellationToken ct, string direction = "up", int depth = DefaultTreeDepth)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.TryParse<TreeDirection>(direction, ignoreCase: true, out var dir) || !Enum.IsDefined(dir))
        {
            errors["direction"] = ["Direction must be 'up' or 'down'."];
        }

        if (depth is < 1 or > MaxTreeDepth)
        {
            errors["depth"] = [$"Depth must be between 1 and {MaxTreeDepth}."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        if (!await db.Users.AnyAsync(u => u.ExternalId == externalId, ct))
        {
            return UserNotFound(externalId);
        }

        IReadOnlyList<TreeNodeResponse> nodes;
        if (dir == TreeDirection.Up)
        {
            var ancestors = await tree.GetAncestorsAsync(externalId, depth, ct);
            nodes = ancestors
                .Select((a, i) => new TreeNodeResponse(
                    a.Level,
                    a.ExternalId,
                    i + 1 < ancestors.Count ? ancestors[i + 1].ExternalId : null))
                .ToList();
        }
        else
        {
            var descendants = await tree.GetDescendantsAsync(externalId, depth, ct);
            nodes = descendants.Select(d => new TreeNodeResponse(d.Level, d.ExternalId, d.PartnerExternalId)).ToList();
        }

        return new TreeResponse(externalId, dir, nodes);
    }

    private ObjectResult UserNotFound(string externalId) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "User not found",
        detail: $"User '{externalId}' does not exist.");
}
