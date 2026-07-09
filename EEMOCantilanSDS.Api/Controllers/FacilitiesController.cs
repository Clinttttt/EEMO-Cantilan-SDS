using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityConfiguration;
using EEMOCantilanSDS.Application.Command.Facilities.AddFacility;
using EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;
using EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummary;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummaries;
using EEMOCantilanSDS.Application.Queries.Stalls.GetSectionSummaries;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacility;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class FacilitiesController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("configuration")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FacilityConfigurationDto>> GetConfiguration()
    {
        var result = await Sender.Send(new GetFacilityConfigurationQuery());
        return HandleResponse(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> AddFacility([FromBody] AddFacilityCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> UpdateFacility([FromBody] UpdateFacilityCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("{facilityCode}/stalls")]
    public async Task<ActionResult<IReadOnlyList<StallDto>>> GetStalls(FacilityCode facilityCode, [FromQuery] MarketSection? section)
    {
        var query = new GetStallsByFacilityQuery(facilityCode, section);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("{facilityCode}/sections")]
    public async Task<ActionResult<Dictionary<MarketSection, StallSummaryDto>>> GetSectionSummaries(FacilityCode facilityCode, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetSectionSummariesQuery(facilityCode, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("summaries")]
    public async Task<ActionResult<IReadOnlyList<FacilitySidebarSummaryDto>>> GetSummaries([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetFacilitySummariesQuery(year, month));
        return HandleResponse(result);
    }

    [HttpGet("{facilityCode}/summary")]
    public async Task<ActionResult<FacilitySummaryDto>> GetSummary(FacilityCode facilityCode, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetFacilitySummaryQuery(facilityCode, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("{facilityCode}/reports")]
    public async Task<ActionResult<FacilityReportsDto>> GetFacilityReports(
        [FromRoute] FacilityCode facilityCode,
        [FromQuery] ReportPeriod period,
        [FromQuery] int year,
        [FromQuery] int? month = null,
        [FromQuery] int? weekNumber = null)
    {
        var query = new GetFacilityReportsQuery(facilityCode, period, year, month, weekNumber);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("{facilityCode}/history")]
    public async Task<ActionResult<FacilityHistoryDto>> GetFacilityHistory(
        [FromRoute] FacilityCode facilityCode,
        [FromQuery] int year)
    {
        var query = new GetFacilityHistoryQuery(facilityCode, year);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("month-end-report")]
    public async Task<ActionResult<MonthEndReportDto>> GetMonthEndReport(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMonthEndReportQuery(year, month));
        return HandleResponse(result);
    }
}
