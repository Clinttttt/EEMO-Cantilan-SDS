using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Application.Queries.Audit.GetAuditTrail;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class AuditController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>
    /// Returns a filtered, paginated page of audit-trail entries plus action-summary counts and
    /// the distinct actor/entity filter options. SuperAdmin only.
    /// </summary>
    [HttpGet("trail")]
    public async Task<ActionResult<AuditTrailDto>> GetTrail(
        [FromQuery] string? search = null,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? actor = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool includeOptions = true)
    {
        var query = new GetAuditTrailQuery(search, action, entityType, actor, fromUtc, toUtc, page, pageSize, includeOptions);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }
}
