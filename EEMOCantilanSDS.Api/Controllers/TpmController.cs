using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;
using EEMOCantilanSDS.Application.Command.TaboanMarket.SaveVendorOrNumber;
using EEMOCantilanSDS.Application.Command.TaboanMarket.UpdateVendor;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMarketDays;
using EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMonthAttendance;
using EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmHistory;
using EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;
using EEMOCantilanSDS.Application.Queries.TaboanMarket.GetVendorAttendance;
using EEMOCantilanSDS.Application.Requests.TaboanMarket;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin,Collector")]
[Route("api/tpm")]
public class TpmController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("overview")]
    public async Task<ActionResult<TpmOverviewDto>> GetOverview([FromQuery] int year, [FromQuery] int month)
        => HandleResponse(await Sender.Send(new GetTpmOverviewQuery(year, month)));

    [HttpGet("market-days")]
    public async Task<ActionResult<IReadOnlyList<TpmMarketDayDto>>> GetMarketDays([FromQuery] int year, [FromQuery] int month)
        => HandleResponse(await Sender.Send(new GetMarketDaysQuery(year, month)));

    [HttpGet("month-attendance")]
    public async Task<ActionResult<IReadOnlyList<TpmVendorAttendanceDto>>> GetMonthAttendance([FromQuery] int year, [FromQuery] int month)
        => HandleResponse(await Sender.Send(new GetMonthAttendanceQuery(year, month)));

    [HttpGet("history")]
    public async Task<ActionResult<TpmHistoryDto>> GetHistory([FromQuery] int year)
        => HandleResponse(await Sender.Send(new GetTpmHistoryQuery(year)));

    [HttpGet("attendance")]
    public async Task<ActionResult<IReadOnlyList<TpmVendorAttendanceDto>>> GetVendorAttendance([FromQuery] string marketDate)
    {
        if (!DateOnly.TryParse(marketDate, out var date))
            return BadRequest("Invalid date format.");
        return HandleResponse(await Sender.Send(new GetVendorAttendanceQuery(date)));
    }

    [HttpPost("attendance")]
    public async Task<ActionResult<TpmVendorAttendanceDto>> AddVendor([FromBody] AddVendorToMarketDayCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpPatch("attendance/{attendanceId}/payment")]
    public async Task<ActionResult<bool>> MarkVendorPaid(Guid attendanceId, [FromBody] MarkVendorPaidRequest request)
        => HandleResponse(await Sender.Send(new MarkVendorPaidCommand(attendanceId, request.IsPaid)));

    [HttpPatch("attendance/{attendanceId}/or-number")]
    public async Task<ActionResult<bool>> SaveOrNumber(Guid attendanceId, [FromBody] SaveOrNumberRequest request)
        => HandleResponse(await Sender.Send(new SaveVendorOrNumberCommand(attendanceId, request.ORNumber)));

    [HttpPatch("attendance/{attendanceId}")]
    public async Task<ActionResult<bool>> UpdateVendor(Guid attendanceId, [FromBody] UpdateTpmVendorRequest request)
        => HandleResponse(await Sender.Send(new UpdateTpmVendorCommand(
            attendanceId, request.VendorName, request.Goods, request.IsPaid, request.ORNumber)));
}


