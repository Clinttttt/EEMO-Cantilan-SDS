using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// ST-19: a payor's read-only view of the unpaid obligations of HISTORICAL accounts (lapsed, closed, renewed, former) they were explicitly linked to by an
/// office-issued code, and the signed-in linking of such an account. Nothing here can be paid online.
/// </summary>
[Route("api/payor/historical")]
[ApiController]
[Authorize(Roles = "Payor")]
public class PayorHistoricalController(ISender sender, HistoricalAccessWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("obligations")]
    public async Task<ActionResult<IReadOnlyList<PayorHistoricalObligationDto>>> GetAsync(CancellationToken ct) => HandleResponse(await workflow.GetMineAsync(ct));

    [HttpPost("link")]
    public async Task<ActionResult<int>> LinkAsync([FromBody] LinkHistoricalAccessRequest request, CancellationToken ct) => HandleResponse(await workflow.LinkAsync(request, ct));
}
