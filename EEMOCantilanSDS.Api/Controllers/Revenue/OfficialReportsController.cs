using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// The official financial readers (IA-051, IA-052): the Monthly Income statement that combines authoritative legacy and
/// canonical cash exactly once, the Collections register with its RCD-style summary, and the serial trace. Read-only.
/// </summary>
[ApiController]
[Route("api/official-reports")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class OfficialReportsController(ISender sender, CollectionsReportWorkflow collections) : ApiBaseController(sender)
{
    [HttpGet("monthly-income")]
    public async Task<ActionResult<OfficialMonthlyIncomeDto>> MonthlyIncomeAsync(
        [FromQuery] int year, [FromQuery] int? month, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetOfficialMonthlyIncomeQuery(year, month), ct));

    [HttpGet("source-performance")]
    public async Task<ActionResult<RevenueSourcePerformanceDto>> SourcePerformanceAsync(
        [FromQuery] int year, [FromQuery] int? month, CancellationToken ct) =>
        HandleResponse(await Sender.Send(new GetRevenueSourcePerformanceQuery(year, month), ct));

    [HttpGet("collections")]
    public async Task<ActionResult<CollectionsRegisterDto>> CollectionsAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? collectorId,
        [FromQuery] RevenueInstrumentType? instrument, [FromQuery] Guid? classificationId, CancellationToken ct) =>
        HandleResponse(await collections.GetRegisterAsync(from, to, collectorId, instrument, classificationId, ct));

    [HttpGet("collections/{collectionId:guid}")]
    public async Task<ActionResult<CollectionDocumentDto>> CollectionAsync(Guid collectionId, CancellationToken ct) =>
        HandleResponse(await collections.GetCollectionAsync(collectionId, ct));

    [HttpGet("documents/{documentNumber}")]
    public async Task<ActionResult<DocumentTraceDto>> TraceAsync(string documentNumber, CancellationToken ct) =>
        HandleResponse(await collections.TraceAsync(documentNumber, ct));
}
