using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Head/Admin-only live PostgreSQL health snapshot. Read-only: every metric is queried from pg_*
/// system views server-side; no writes are issued and no secrets are returned to the browser.
/// </summary>
[Route("api/database-health")]
[ApiController]
public class DatabaseHealthController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Current database health metrics (connections, cache hit, size, uptime, etc.).</summary>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<DatabaseHealthDto>> GetAsync()
    {
        var result = await Sender.Send(new GetDatabaseHealthQuery());
        return HandleResponse(result);
    }
}
