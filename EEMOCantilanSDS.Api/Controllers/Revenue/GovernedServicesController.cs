using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Governed configurable services (IA-044): approved setup, activity, and the collector's document custody read.
/// Posting happens through the offline sync path (one canonical writer); nothing here lets a client define a charge.
/// </summary>
[ApiController]
[Route("api/governed-services")]
[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public sealed class GovernedServicesController(ISender sender, GovernedServiceWorkflow workflow)
    : ApiBaseController(sender)
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<GovernedServiceDefinitionDto>>> DefinitionsAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetDefinitionsAsync(ct));

    [HttpPut("{operationCode}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<GovernedServiceDefinitionDto>> ConfigureAsync(
        string operationCode, [FromBody] ConfigureGovernedServiceRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ConfigureAsync(operationCode, request, ct));

    [HttpGet("{operationCode}/activity")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<GovernedServiceActivityDto>>> ActivityAsync(
        string operationCode, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetActivityAsync(operationCode, from, to, ct));

    /// <summary>The calling collector's own posted operation collections (read from the canonical Collection).</summary>
    [HttpGet("records")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<IReadOnlyList<GovernedServiceRecordDto>>> RecordsAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetCollectorRecordsAsync(from, to, ct));

    /// <summary>The approved terms a collector may record today for an assigned operation (display only; posting revalidates).</summary>
    [HttpGet("{operationCode}/terms")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<GovernedServiceTermsDto>> TermsAsync(
        string operationCode, [FromQuery] GovernedServiceMode? mode, CancellationToken ct) =>
        HandleResponse(await workflow.GetTermsAsync(operationCode, mode, ct));

    [HttpGet("{operationCode}/documents")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<IReadOnlyList<CashTicketDocumentDto>>> DocumentsAsync(
        string operationCode, [FromQuery] GovernedServiceMode? mode, CancellationToken ct) =>
        HandleResponse(await workflow.GetAvailableDocumentsAsync(operationCode, mode, ct));
}
