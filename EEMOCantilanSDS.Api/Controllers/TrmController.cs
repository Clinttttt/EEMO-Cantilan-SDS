using EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;
using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Command.TransportTerminal.SaveTripOrNumber;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTodayTrips;
using EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporterProfile;
using EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporters;
using EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmOverview;
using EEMOCantilanSDS.Application.Requests.TransportTerminal;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize]
[Route("api/trm")]
public class TrmController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("overview")]
    public async Task<ActionResult<TrmOverviewDto>> GetOverview()
        => HandleResponse(await Sender.Send(new GetTrmOverviewQuery()));

    [HttpGet("transporters")]
    public async Task<ActionResult<IReadOnlyList<TrmTransporterListDto>>> GetTransporters()
        => HandleResponse(await Sender.Send(new GetTransportersQuery()));

    [HttpGet("transporters/{transporterId:guid}")]
    public async Task<ActionResult<TrmTransporterProfileDto>> GetTransporterProfile(Guid transporterId)
        => HandleResponse(await Sender.Send(new GetTransporterProfileQuery(transporterId)));

    [HttpPost("transporters")]
    public async Task<ActionResult<TrmTransporterDto>> AddTransporter([FromBody] AddTransporterCommand command)
        => HandleResponse(await Sender.Send(command));

    [HttpGet("trips/today")]
    public async Task<ActionResult<IReadOnlyList<TrmTripDto>>> GetTodayTrips()
        => HandleResponse(await Sender.Send(new GetTodayTripsQuery()));

    [HttpPost("trips/{transporterId:guid}")]
    public async Task<ActionResult<TrmTripDto>> RecordTrip(Guid transporterId, [FromBody] RecordTripRequest request)
        => HandleResponse(await Sender.Send(new RecordTripCommand(
            transporterId,
            request.DriverName,
            request.PlateNumber,
            request.Route,
            request.ORNumber,
            request.Remarks)));

    [HttpPatch("trips/{tripId:guid}/or-number")]
    public async Task<ActionResult<bool>> SaveOrNumber(Guid tripId, [FromBody] SaveTripOrNumberRequest request)
        => HandleResponse(await Sender.Send(new SaveTripOrNumberCommand(tripId, request.ORNumber)));
}
