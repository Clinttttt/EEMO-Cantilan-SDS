using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Read-only canonical cash readers. These are backend foundations beside the existing legacy reports; no current
/// report or page is switched to them.
/// </summary>
[ApiController]
[Route("api/canonical-reports")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class CanonicalReportsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("monthly-income")]
    public async Task<ActionResult<CanonicalMonthlyIncomeDto>> MonthlyIncomeAsync(
        [FromQuery] int year, [FromQuery] int? month, [FromQuery] CanonicalReportingBasis basis,
        [FromQuery] DateTimeOffset? asOf, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetCanonicalMonthlyIncomeQuery(year, month, basis, asOf), ct));
}
