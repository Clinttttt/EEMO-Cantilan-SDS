using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Live PostgreSQL health snapshot for an LGU Head. Read-only: every metric is queried from pg_* system
/// views server-side; no writes are issued and no secrets are returned. The shared-server metrics
/// (connections, CPU/memory, deadlocks, uptime) are the same for every LGU; the storage figure is scoped
/// to the caller's own municipality. Cross-LGU/destructive tools (backup &amp; restore) stay operator-only.
/// </summary>
[Route("api/database-health")]
[ApiController]
public class DatabaseHealthController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Current database health metrics (connections, CPU/memory, storage, uptime, etc.).</summary>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DatabaseHealthDto>> GetAsync()
    {
        var result = await Sender.Send(new GetDatabaseHealthQuery());
        return HandleResponse(result);
    }
}
