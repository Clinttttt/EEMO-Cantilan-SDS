using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetClientProfile;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetGroupedSlaughterTransactions;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetOwnerTransactionHistory;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterHistory;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterTransactions;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterAnimalRates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class SlaughterController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("overview")]
    public async Task<ActionResult<SlaughterOverviewDto>> GetOverview([FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetSlaughterOverviewQuery(year, month);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("history")]
    public async Task<ActionResult<SlaughterHistoryDto>> GetHistory([FromQuery] int year)
    {
        var query = new GetSlaughterHistoryQuery(year);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<SlaughterTransactionDto>>> GetTransactions([FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetSlaughterTransactionsQuery(year, month);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("grouped-transactions")]
    public async Task<ActionResult<IReadOnlyList<OwnerTransactionGroupDto>>> GetGroupedTransactions([FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetGroupedSlaughterTransactionsQuery(year, month);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("owner-history")]
    public async Task<ActionResult<OwnerTransactionHistoryDto>> GetOwnerHistory([FromQuery] string ownerName, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetOwnerTransactionHistoryQuery(ownerName, year, month);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    [HttpPost("record")]
    public async Task<ActionResult<bool>> RecordSlaughter([FromBody] RecordSlaughterCommand command)
    {
        var result = await sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut("update")]
    public async Task<ActionResult<bool>> UpdateSlaughter([FromBody] UpdateSlaughterCommand command)
    {
        var result = await sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPost("or")]
    public async Task<ActionResult<bool>> SaveOrNumber([FromBody] SaveSlaughterOrNumberCommand command)
    {
        var result = await sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("client/{ownerName}")]
    public async Task<ActionResult<ClientProfileDto>> GetClientProfile(string ownerName)
    {
        var query = new GetClientProfileQuery(ownerName);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }

    /// <summary>Lists the caller LGU's custom animal types + default per-head rates (for the SLH record screen).</summary>
    [HttpGet("animal-rates")]
    public async Task<ActionResult<IReadOnlyList<SlaughterAnimalRateDto>>> GetAnimalRates([FromQuery] bool activeOnly = true)
    {
        var query = new GetSlaughterAnimalRatesQuery(activeOnly);
        var result = await sender.Send(query);
        return HandleResponse(result);
    }
}
