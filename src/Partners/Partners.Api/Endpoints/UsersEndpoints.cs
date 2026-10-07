using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Partners.Api.Data;
using Partners.Domain;
using Partners.Domain.Entities;

namespace Partners.Api.Endpoints;

public sealed record CreateUserRequest(
    [Required, StringLength(200)] string ExternalId);

public sealed record SetPartnerRequest(
    [StringLength(200)] string? PartnerExternalId);

public sealed record UserResponse(
    string ExternalId,
    string? PartnerExternalId,
    DateTimeOffset CreatedAt);

public enum TreeDirection
{
    Up,
    Down
}

public sealed record TreeNodeResponse(int Level, string ExternalId, string? PartnerExternalId);

public sealed record TreeResponse(string ExternalId, TreeDirection Direction, IReadOnlyList<TreeNodeResponse> Nodes);

public static class UsersEndpoints
{
    public const int DefaultTreeDepth = 10;
    public const int MaxTreeDepth = 100;

    // Need this lock to perform two concurrent updates when setting partners.
    // Without this lock we can get in loop of partners
    private const string PartnerChangeLockName = "partners.partner-change";

    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapPost("/", CreateUser);
        group.MapGet("/{externalId}", GetUser);
        group.MapPut("/{externalId}/partner", SetPartner);
        group.MapGet("/{externalId}/tree", GetTree);

        return app;
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, PartnersDbContext db, CancellationToken ct)
    {
        var user = User.Create(request.ExternalId, DateTimeOffset.UtcNow);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "User already exists",
                detail: $"User '{request.ExternalId}' already exists.");
        }

        return Results.Created(
            $"/api/users/{Uri.EscapeDataString(user.ExternalId)}",
            new UserResponse(user.ExternalId, null, user.CreatedAt));
    }

    private static async Task<IResult> GetUser(string externalId, PartnersDbContext db, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.ExternalId == externalId)
            .Select(u => new UserResponse(
                u.ExternalId,
                db.Users.Where(r => r.Id == u.PartnerId).Select(r => r.ExternalId).FirstOrDefault(),
                u.CreatedAt))
            .SingleOrDefaultAsync(ct);

        return user is null ? UserNotFound(externalId) : Results.Ok(user);
    }

    private static async Task<IResult> SetPartner(
        string externalId, SetPartnerRequest request, PartnersDbContext db, TreeQueries tree, CancellationToken ct)
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
                return Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Invalid partner link",
                    detail: ex.Message);
            }

            user.PartnerId = partner.Id;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetTree(
        string externalId, PartnersDbContext db, TreeQueries tree, CancellationToken ct,
        string direction = "up", int depth = DefaultTreeDepth)
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
            return Results.ValidationProblem(errors);
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

        return Results.Ok(new TreeResponse(externalId, dir, nodes));
    }

    private static IResult UserNotFound(string externalId) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "User not found",
        detail: $"User '{externalId}' does not exist.");
}
