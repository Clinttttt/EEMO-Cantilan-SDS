using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummary;
using EEMOCantilanSDS.Application.Queries.Stalls.GetSectionSummaries;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacility;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

public class FacilitiesController(ISender sender) : ApiBaseController(sender)
{
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

    [HttpGet("{facilityCode}/summary")]
    public async Task<ActionResult<FacilitySummaryDto>> GetSummary(FacilityCode facilityCode, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetFacilitySummaryQuery(facilityCode, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }
}
