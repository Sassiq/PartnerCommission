using Commission.Api.Data;
using Microsoft.EntityFrameworkCore;
using PartnerCommission.Contracts;

namespace Commission.Api.Endpoints;

public sealed record SchemaRequest(SchemaType Schema);

public sealed record SchemaResponse(SchemaType Schema);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");
        group.MapGet("/schema", GetSchema);
        group.MapPut("/schema", SetSchema);
        return app;
    }

    private static async Task<IResult> GetSchema(CommissionDbContext db, CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().SingleAsync(ct);
        return Results.Ok(new SchemaResponse(settings.ActiveSchema));
    }

    /// <summary>
    /// Switches the scheme used for future events calculations.
    /// </summary>
    private static async Task<IResult> SetSchema(SchemaRequest request, CommissionDbContext db, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Schema))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["schema"] = ["Schema must be 'Linear' or 'Fibonacci'."]
            });
        }

        await db.Settings.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveSchema, request.Schema), ct);

        return Results.Ok(new SchemaResponse(request.Schema));
    }
}
