using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Platform-operator (default-LGU SuperAdmin) live PostgreSQL health snapshot. Read-only: every metric is
/// queried from pg_* system views server-side; no writes are issued and no secrets are returned. DB-wide
/// metrics span every LGU, so this is gated by the "PlatformOperator" policy (not per-LGU Heads/Admins).
/// </summary>
[Route("api/database-health")]
[ApiController]
public class DatabaseHealthController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Current database health metrics (connections, cache hit, size, uptime, etc.).</summary>
    [HttpGet]
    [Authorize(Policy = "PlatformOperator")]
    public async Task<ActionResult<DatabaseHealthDto>> GetAsync()
    {
        var result = await Sender.Send(new GetDatabaseHealthQuery());
        return HandleResponse(result);
    }
}
