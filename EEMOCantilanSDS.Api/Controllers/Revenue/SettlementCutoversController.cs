using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Api.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers.Revenue;

/// <summary>Read-only source-scoped cutover readiness. Mutating cutover commands are intentionally not exposed in Phase 5A.</summary>
[ApiController]
[Route("api/settlement-cutovers")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class SettlementCutoversController(
    ISender sender,
    SettlementCutoverWorkflow workflow) : ApiBaseController(sender)
{
    [HttpPost("readiness")]
    public async Task<ActionResult<SettlementCutoverReadinessDto>> EvaluateAsync(
        [FromBody] SettlementCutoverReadinessRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.EvaluateReadinessAsync(request, ct));
}
