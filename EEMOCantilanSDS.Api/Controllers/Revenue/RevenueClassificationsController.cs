using EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Tenant-local revenue configuration. This is Head-only, matching the existing facility-rate configuration
/// boundary; the endpoint does not connect classifications to money or reporting flows.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
[Route("api/revenue-classifications")]
[ApiController]
public sealed class RevenueClassificationsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RevenueClassificationDto>>> ListAsync([FromQuery] DateOnly? asOf = null) =>
        HandleResponse(await Sender.Send(new GetRevenueClassificationsQuery(asOf)));

    [HttpGet("{classificationId:guid}/policies")]
    public async Task<ActionResult<IReadOnlyList<RevenueClassificationPolicyDto>>> PolicyHistoryAsync(Guid classificationId) =>
        HandleResponse(await Sender.Send(new GetRevenueClassificationPolicyHistoryQuery(classificationId)));

    [HttpPost]
    public async Task<ActionResult<RevenueClassificationDto>> CreateAsync(
        [FromBody] CreateRevenueClassificationCommand command) =>
        HandleResponse(await Sender.Send(command));

    [HttpPost("{classificationId:guid}/policies")]
    public async Task<ActionResult<RevenueClassificationPolicyDto>> AppendPolicyAsync(
        Guid classificationId,
        [FromBody] AppendRevenueClassificationPolicyRequest request) =>
        HandleResponse(await Sender.Send(new AppendRevenueClassificationPolicyCommand(
            classificationId, request.EffectiveDate, request.DisplayName, request.Description,
            request.PermittedInstrumentType)));

    [HttpPost("{classificationId:guid}/retire")]
    public async Task<ActionResult<bool>> RetireAsync(Guid classificationId) =>
        HandleResponse(await Sender.Send(new RetireRevenueClassificationCommand(classificationId)));
}
