using Commission.Api.Data;
using Commission.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Commission.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(CommissionDbContext db) : ControllerBase
{
    [HttpGet("schema")]
    public async Task<ActionResult<SchemaResponse>> GetSchema(CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().SingleAsync(ct);
        return new SchemaResponse(settings.ActiveSchema);
    }

    /// <summary>
    /// Switches the scheme used for future events calculations.
    /// </summary>
    [HttpPut("schema")]
    public async Task<ActionResult<SchemaResponse>> SetSchema(SchemaRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Schema))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["schema"] = ["Schema must be 'Linear' or 'Fibonacci'."]
            }));
        }

        await db.Settings.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveSchema, request.Schema), ct);

        return new SchemaResponse(request.Schema);
    }
}
