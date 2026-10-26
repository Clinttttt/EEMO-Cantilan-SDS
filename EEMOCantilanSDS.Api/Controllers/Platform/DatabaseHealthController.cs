using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Live PostgreSQL health snapshot. The platform operator sees the shared database; an LGU Head sees only their own storage. Read-only: every metric is queried from pg_* system
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
        // Platform-wide load is the operator's; an LGU Head gets only their own storage (ST-11 #4).
        var platformWide = string.Equals(User.FindFirst(AppClaimTypes.PlatformOperator)?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var result = await Sender.Send(new GetDatabaseHealthQuery(platformWide));
        return HandleResponse(result);
    }
}
