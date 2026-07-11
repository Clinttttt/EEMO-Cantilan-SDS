using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistory;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
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
        [FromQuery] FacilityCode? facility = null,
        [FromQuery] bool allTime = false)
    {
        var result = await Sender.Send(new GetFinancialReportQuery(period, year, month, facility, allTime));
        return HandleResponse(result);
    }

    /// <summary>
    /// The admin Follow-up Queue — an action list of accounts/records/facilities needing attention,
    /// computed "as of" the given collection period (current month by default on the dashboard).
    /// </summary>
    [HttpGet("follow-up")]
    public async Task<ActionResult<FollowUpQueueDto>> GetFollowUp(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await Sender.Send(new GetFollowUpQueueQuery(year, month));
        return HandleResponse(result);
    }

    /// <summary>
    /// Follow-up History — the same action list as the live queue, but a read snapshot "as of" a PAST
    /// collection period (chosen on the History page). Contract-expiry and online-awaiting-OR sources
    /// are period-scoped so the snapshot reflects that period, not today.
    /// </summary>
    [HttpGet("follow-up/history")]
    public async Task<ActionResult<FollowUpQueueDto>> GetFollowUpHistory(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await Sender.Send(new GetFollowUpHistoryQuery(year, month));
        return HandleResponse(result);
    }

    /// <summary>
    /// Per-facility collection report for the Export Data page (print / PDF / CSV), for one month.
    /// </summary>
    [HttpGet("collection-report")]
    public async Task<ActionResult<CollectionReportDto>> GetCollectionReport(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var result = await Sender.Send(new GetCollectionReportQuery(year, month));
        return HandleResponse(result);
    }
}
