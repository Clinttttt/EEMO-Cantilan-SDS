using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class ReportsController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>
    /// All-facility (or single-facility) financial report for the admin Reports page.
    /// <paramref name="facility"/> omitted = all facilities. <paramref name="month"/> is required for Monthly.
    /// </summary>
    [HttpGet("financial")]
    public async Task<ActionResult<FinancialReportDto>> GetFinancial(
        [FromQuery] ReportPeriod period,
        [FromQuery] int year,
        [FromQuery] int? month = null,
        [FromQuery] FacilityCode? facility = null)
    {
        var result = await Sender.Send(new GetFinancialReportQuery(period, year, month, facility));
        return HandleResponse(result);
    }
}
