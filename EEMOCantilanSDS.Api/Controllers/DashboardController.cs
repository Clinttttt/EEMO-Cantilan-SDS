using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Application.Queries.Dashboard.GetDashboardOverview;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class DashboardController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("overview")]
    public async Task<ActionResult<DashboardOverviewDto>> GetOverview([FromQuery] int year, [FromQuery] int month)
        => HandleResponse(await Sender.Send(new GetDashboardOverviewQuery(year, month)));
}
