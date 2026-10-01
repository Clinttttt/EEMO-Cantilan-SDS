using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Route("api/wcf-collections")]
[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public sealed class WcfCollectionsController(
    ISender sender,
    WcfCollectionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("obligations")]
    public async Task<ActionResult<IReadOnlyList<WcfObligationQuoteDto>>> ObligationsAsync(
        [FromQuery] int throughYear, [FromQuery] int throughMonth, CancellationToken ct) =>
        HandleResponse(await workflow.GetObligationsAsync(throughYear, throughMonth, ct));

    [HttpGet("cash-tickets/available")]
    public async Task<ActionResult<IReadOnlyList<CashTicketDocumentDto>>> CashTicketsAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetAvailableCashTicketsAsync(ct));

    [HttpGet("mobile-sources")]
    public async Task<ActionResult<IReadOnlyList<WcfMobileSourceDto>>> MobileSourcesAsync(
        [FromQuery] int billingYear, [FromQuery] int billingMonth, CancellationToken ct) =>
        HandleResponse(await workflow.GetMobileSourcesAsync(billingYear, billingMonth, ct));

    [HttpGet("mobile-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<WcfMobileStatusDto>> MobileStatusAsync(
        [FromServices] WcfMobileCollectionWorkflow mobileCollection, CancellationToken ct) =>
        HandleResponse(await mobileCollection.GetStatusAsync(ct));

    [HttpPost("mobile-status/enable")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<WcfMobileStatusDto>> EnableMobileAsync(
        [FromServices] WcfMobileCollectionWorkflow mobileCollection, CancellationToken ct) =>
        HandleResponse(await mobileCollection.EnableAsync(ct));

    [HttpGet("setup-sources")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<WcfSetupSourceDto>>> SetupSourcesAsync(
        [FromQuery] int billingYear, [FromQuery] int billingMonth, CancellationToken ct) =>
        HandleResponse(await workflow.GetSetupSourcesAsync(billingYear, billingMonth, ct));

    [HttpPost("obligations")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<WcfSetupSourceDto>> EstablishObligationAsync(
        [FromBody] WcfObligationSetupRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.EstablishObligationAsync(request, ct));

    [HttpPost("activation-readiness")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<SettlementCutoverReadinessDto>> ActivationReadinessAsync(
        [FromBody] WcfActivationReadinessRequest request, [FromServices] WcfActivationWorkflow activation, CancellationToken ct) =>
        HandleResponse(await activation.GetReadinessAsync(request.UtilityBillId, request.Evidence, ct));

    [HttpPost("activations")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<SettlementCutoverOutcomeDto>> ActivateAsync(
        [FromBody] WcfActivationRequest request, [FromServices] WcfActivationWorkflow activation, CancellationToken ct) =>
        HandleResponse(await activation.ActivateAsync(request, ct));

    [HttpPost("collections")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<WcfCollectionOutcomeDto>> PostWebAsync(
        [FromBody] WcfCollectionPostRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.PostWebAsync(request, ct));

    [HttpPost("mobile-collections")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<WcfCollectionOutcomeDto>> PostMobileAsync(
        [FromBody] WcfCollectionPostRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.PostMobileAsync(request, ct));

    [HttpGet("activity")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<WcfCollectionActivityDto>>> ActivityAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetActivityAsync(from, to, ct));

    [HttpGet("reconciliation")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<WcfReconciliationExceptionDto>>> ReconciliationAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetReconciliationExceptionsAsync(ct));
}
