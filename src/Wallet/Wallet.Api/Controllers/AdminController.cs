using Microsoft.AspNetCore.Mvc;
using Wallet.Api.Payouts;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(PayoutRunner runner) : ControllerBase
{
    /// <summary>
    /// Manually run payouts.
    /// </summary>
    [HttpPost("payouts/run")]
    public async Task<ActionResult<PayoutRunResult>> RunPayouts(CancellationToken ct) =>
        await runner.RunAsync(ct);
}
