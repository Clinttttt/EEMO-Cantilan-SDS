using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetTenantUsage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Per-tenant storage footprint for the authenticated Head's OWN municipality. Unlike the whole-database
/// <see cref="DatabaseHealthController"/> (platform-operator only), this is available to every LGU's Head
/// and is scoped server-side to the caller's tenant — it can only ever report the caller's own data.
/// Read-only: it issues COUNT / pg_column_size aggregates filtered by the caller's MunicipalityId.
/// </summary>
[Route("api/tenant-usage")]
[ApiController]
public class TenantUsageController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Record counts + estimated storage for the caller's municipality.</summary>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<TenantUsageDto>> GetAsync()
    {
        var result = await Sender.Send(new GetTenantUsageQuery());
        return HandleResponse(result);
    }
}
