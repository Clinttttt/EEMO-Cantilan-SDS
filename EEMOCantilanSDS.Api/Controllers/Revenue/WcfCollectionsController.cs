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
