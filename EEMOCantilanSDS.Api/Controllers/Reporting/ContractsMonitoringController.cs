using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Route("api/monitoring/contracts")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class ContractsMonitoringController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<ContractsMonitoringPageDto>> GetAsync([FromQuery] ContractsMonitoringRequest filters, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetContractsMonitoringQuery(filters), ct));
}
